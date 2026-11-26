using System.Diagnostics;
using System.Windows.Threading;
using MusicVisualizer.Audio;
using MusicVisualizer.Interop;

namespace MusicVisualizer.Services;

/// <summary>
/// Keeps the display awake and the screensaver away while music is playing, and lets
/// Windows go idle normally once it has been quiet for <see cref="QuietHold"/>.
/// Must be created on the UI thread: the execution state is per-thread.
/// </summary>
public sealed class ScreensaverBlocker : IDisposable
{
    /// <summary>Level (0..1, see <see cref="AudioFrame.Level"/>) above which audio counts as playing.</summary>
    private const float PlayingThreshold = 0.01f;

    /// <summary>How long it must stay quiet before blocking stops — covers gaps between tracks.</summary>
    private static readonly TimeSpan QuietHold = TimeSpan.FromSeconds(15);

    private readonly AudioEngine _audio;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(0.5) };
    private readonly Stopwatch _sinceHeard = new();
    private bool _blocking;

    public ScreensaverBlocker(AudioEngine audio)
    {
        _audio = audio;
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        if (_audio.Frame.Level > PlayingThreshold) _sinceHeard.Restart();
        bool playing = _sinceHeard.IsRunning && _sinceHeard.Elapsed < QuietHold;
        if (playing == _blocking) return;

        _blocking = playing;
        NativeMethods.SetThreadExecutionState(playing
            ? NativeMethods.ES_CONTINUOUS | NativeMethods.ES_DISPLAY_REQUIRED
            : NativeMethods.ES_CONTINUOUS);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_blocking) NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        _blocking = false;
    }
}
