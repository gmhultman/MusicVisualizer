using System.Diagnostics;
using System.Windows.Threading;
using MusicVisualizer.Audio;
using Windows.Media.Control;

namespace MusicVisualizer.Services;

/// <summary>
/// Notices when a new song starts. It asks Windows' media sessions (what Spotify, browsers, Media Player etc.
/// report to the volume flyout) for the current title and artist. When no app is reporting, it falls back to
/// treating a short silence followed by sound as a new track. Create on the UI thread; events fire on it too.
/// </summary>
public sealed class SongChangeDetector : IDisposable
{
    private const float QuietThreshold = 0.01f; // same as ScreensaverBlocker
    private static readonly TimeSpan GapForNewSong = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan SessionEventSettle = TimeSpan.FromSeconds(1); // apps send several updates per change
    private static readonly TimeSpan SuppressGapAfterSession = TimeSpan.FromSeconds(4);

    private readonly AudioEngine _audio;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _silenceTimer = new() { Interval = TimeSpan.FromSeconds(0.1) };
    private readonly DispatcherTimer _settleTimer = new() { Interval = SessionEventSettle };
    private readonly Stopwatch _quiet = new();
    private readonly Stopwatch _sinceSessionChange = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _lastKey;
    private bool _heardSound;
    private bool _disposed;

    /// <summary>A new song started. Title and artist are null when it was detected from a silence gap.</summary>
    public event Action<string?, string?>? SongChanged;

    public SongChangeDetector(AudioEngine audio)
    {
        _audio = audio;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _silenceTimer.Tick += (_, _) => CheckSilence();
        _silenceTimer.Start();
        _settleTimer.Tick += async (_, _) => { _settleTimer.Stop(); await ReadSessionAsync(raise: true); };
        _ = ConnectAsync();
    }

    /// <summary>True while a reporting app is playing; it then announces its own track changes.</summary>
    private bool SessionPlaying
    {
        get
        {
            try { return _session?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------- media sessions

    private async Task ConnectAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager = manager;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            AttachSession(_manager.GetCurrentSession());
            await ReadSessionAsync(raise: false); // remember what's playing now without announcing it
        }
        catch
        {
            // Media sessions unavailable (old Windows build, policy, etc.): the silence fallback still works.
            _manager = null;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed || _manager == null) return;
            AttachSession(SafeCurrentSession());
            Settle();
        });

    private GlobalSystemMediaTransportControlsSession? SafeCurrentSession()
    {
        try { return _manager?.GetCurrentSession(); }
        catch { return null; }
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null) _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session = session;
        if (_session != null) _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _dispatcher.BeginInvoke(Settle);

    /// <summary>Waits for the burst of updates an app sends on a track change to finish before reading it.</summary>
    private void Settle()
    {
        if (_disposed) return;
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private async Task ReadSessionAsync(bool raise)
    {
        var session = _session;
        if (session == null || _disposed) return;
        string title, artist;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            title = props?.Title?.Trim() ?? "";
            artist = props?.Artist?.Trim() ?? "";
        }
        catch { return; }
        if (_disposed || title.Length == 0) return;

        string key = artist + "\u001F" + title;
        if (key == _lastKey) return;
        bool first = _lastKey == null;
        _lastKey = key;
        _sinceSessionChange.Restart();
        if (raise && !first) SongChanged?.Invoke(title, artist.Length > 0 ? artist : null);
    }

    // ---------------------------------------------------------------- silence fallback

    private void CheckSilence()
    {
        bool loud = _audio.Frame.Level > QuietThreshold;
        if (!loud)
        {
            if (!_quiet.IsRunning) _quiet.Restart();
            return;
        }

        bool gap = _heardSound && _quiet.IsRunning && _quiet.Elapsed >= GapForNewSong;
        _quiet.Reset();
        _heardSound = true;
        if (!gap) return;

        // A reporting app announces its own track changes; the gap between its songs would double up.
        bool sessionRecentlyChanged = _sinceSessionChange.IsRunning && _sinceSessionChange.Elapsed < SuppressGapAfterSession;
        if (SessionPlaying || sessionRecentlyChanged) return;
        SongChanged?.Invoke(null, null);
    }

    public void Dispose()
    {
        _disposed = true;
        _silenceTimer.Stop();
        _settleTimer.Stop();
        AttachSession(null);
        if (_manager != null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        _manager = null;
    }
}
