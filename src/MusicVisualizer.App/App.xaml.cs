using System.Windows;
using System.Windows.Input;
using MusicVisualizer.Audio;
using MusicVisualizer.Services;
using MusicVisualizer.Settings;
using MusicVisualizer.Visuals;

namespace MusicVisualizer;

/// <summary>
/// Owns everything that outlives a window: audio, visualizers, settings, tray icon and hotkeys.
/// Switching between windowed and overlay mode recreates the window, because
/// <c>AllowsTransparency</c> can't be changed once a window has been shown.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _tray;
    private GlobalHotkeys? _hotkeys;
    private ScreensaverBlocker? _screensaver;
    private SongChangeDetector? _songs;
    private AutoCycler? _cycler;
    private MainWindow? _window;
    private SettingsWindow? _settingsWindow;
    private readonly Rotation _visualizerShuffle = new();
    private readonly Rotation _paletteShuffle = new();
    private bool _batchingNotices;

    public Updater Updater { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public AudioEngine Audio { get; } = new();

    /// <summary>Every visualizer, enabled or not. Indexes into this list identify a visualizer.</summary>
    public IReadOnlyList<IVisualizer> Visualizers { get; } = VisualizerCatalog.CreateAll();

    /// <summary>The enabled visualizers' indexes grouped by category, in display order. Empty categories are left out.</summary>
    public IEnumerable<IGrouping<VisualizerCategory, int>> EnabledVisualizersByCategory =>
        VisualizerCatalog.GroupByCategory(Visualizers, EnabledVisualizerIndexes);

    /// <summary>Built-in palettes followed by the user's custom ones. Rebuilt by <see cref="ApplySettings"/>.</summary>
    public IReadOnlyList<Palette> Palettes { get; private set; } = Palette.All;

    /// <summary>Indexes (into <see cref="Visualizers"/>) of the visualizers the user has switched on, in order. Never empty.</summary>
    public IReadOnlyList<int> EnabledVisualizerIndexes { get; private set; } = [];

    /// <summary>Indexes (into <see cref="Palettes"/>) of the palettes included when stepping/cycling. Never empty.</summary>
    public IReadOnlyList<int> EnabledPaletteIndexes { get; private set; } = [];

    public int VisualizerIndex { get; private set; }
    public int PaletteIndex { get; private set; }
    public IVisualizer CurrentVisualizer => Visualizers[VisualizerIndex];
    public Palette CurrentPalette => Palettes[PaletteIndex];

    /// <summary>Raised when the visualizer, palette or glow changes, so the window can show a hint.</summary>
    public event Action<string>? Notice;

    /// <summary>Raised after <see cref="ApplySettings"/>: enabled lists, palettes, opacity etc. may have changed.</summary>
    public event Action? SettingsChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = AppSettings.Load();
        RebuildLists();
        VisualizerIndex = Math.Max(0, Visualizers.ToList().FindIndex(v => v.Name == Settings.Visualizer));
        if (!EnabledVisualizerIndexes.Contains(VisualizerIndex)) VisualizerIndex = EnabledVisualizerIndexes[0];
        Settings.Visualizer = CurrentVisualizer.Name;

        ConfigureAudio();
        Audio.Start();

        _screensaver = new ScreensaverBlocker(Audio); // blocks the screensaver only while music plays
        _cycler = new AutoCycler(this);
        _songs = new SongChangeDetector(Audio);
        _songs.SongChanged += OnSongChanged;

        _tray = new TrayIcon(this);

        _hotkeys = new GlobalHotkeys();
        _hotkeys.RegisterCtrlAlt(Key.V, ToggleMode);
        _hotkeys.RegisterCtrlAlt(Key.N, () => NextVisualizer());
        _hotkeys.RegisterCtrlAlt(Key.C, () => NextPalette());

        ShowWindowFor(Settings.Mode);

        Updater.UpdateReady += version => _tray?.ShowMessage($"Version {version} is ready. Restart to update from the tray menu.");
        _ = CheckForUpdatesLaterAsync();
    }

    /// <summary>Waits a little after startup so the update check never slows down launching.</summary>
    private async Task CheckForUpdatesLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        await Updater.CheckAsync(manual: false);
    }

    public async void CheckForUpdatesNow()
    {
        var message = await Updater.CheckAsync(manual: true);
        if (message != null) _tray?.ShowMessage(message);
    }

    public void RestartToUpdate()
    {
        Settings.Save();
        Updater.RestartToUpdate(); // exits this process
    }

    // ---------------------------------------------------------------- mode

    public void ToggleMode() =>
        SetMode(Settings.Mode == DisplayMode.Overlay ? DisplayMode.Windowed : DisplayMode.Overlay);

    public void SetMode(DisplayMode mode)
    {
        if (mode == Settings.Mode && _window != null)
        {
            _window.Activate();
            return;
        }
        Settings.Mode = mode;
        Settings.Save();
        ShowWindowFor(mode);
    }

    public void SetOverlayLayout(OverlayLayout layout)
    {
        Settings.OverlayLayout = layout;
        Settings.Save();
        if (Settings.Mode == DisplayMode.Overlay)
            ShowWindowFor(DisplayMode.Overlay); // rebuild with the new placement
        else
            SetMode(DisplayMode.Overlay);
    }

    // ---------------------------------------------------------------- visualizers

    /// <summary>Shows the visualizer at <paramref name="index"/> in <see cref="Visualizers"/>.</summary>
    public void SetVisualizer(int index) => SetVisualizer(index, auto: false);

    /// <summary>Steps to the next (1) or previous (-1) enabled visualizer. Automatic changes shuffle if that's on.</summary>
    public void NextVisualizer(int direction = 1, bool auto = false)
    {
        int next = auto && Settings.Shuffle
            ? _visualizerShuffle.Shuffle(VisualizerIndex, EnabledVisualizerIndexes)
            : Rotation.Step(VisualizerIndex, EnabledVisualizerIndexes, direction);
        SetVisualizer(next, auto);
    }

    private void SetVisualizer(int index, bool auto)
    {
        if (index < 0 || index >= Visualizers.Count) return;
        bool changed = index != VisualizerIndex;
        VisualizerIndex = index;
        Settings.Visualizer = CurrentVisualizer.Name;
        if (!auto) _cycler?.Restart();

        if (changed && Settings.CycleColors == ColorCycleMode.WithVisualizer)
        {
            // One hint for both changes.
            StepPaletteQuietly(auto);
            Settings.Save();
            RaiseNotice($"{CurrentVisualizer.Name}   ·   Colors: {CurrentPalette.Name}");
            return;
        }
        Settings.Save();
        RaiseNotice(CurrentVisualizer.Name);
    }

    // ---------------------------------------------------------------- palettes

    /// <summary>Selects the palette at <paramref name="index"/> in <see cref="Palettes"/>.</summary>
    public void SetPalette(int index)
    {
        if (index < 0 || index >= Palettes.Count) return;
        PaletteIndex = index;
        Settings.PaletteName = CurrentPalette.Name;
        Settings.Save();
        RaiseNotice($"Colors: {CurrentPalette.Name}");
    }

    /// <summary>Steps to the next (1) or previous (-1) enabled palette. Automatic changes shuffle if that's on.</summary>
    public void NextPalette(int direction = 1, bool auto = false)
    {
        StepPalette(direction, auto);
        SetPalette(PaletteIndex);
    }

    private void StepPaletteQuietly(bool auto)
    {
        StepPalette(1, auto);
        Settings.PaletteName = CurrentPalette.Name;
    }

    private void StepPalette(int direction, bool auto) =>
        PaletteIndex = auto && Settings.Shuffle
            ? _paletteShuffle.Shuffle(PaletteIndex, EnabledPaletteIndexes)
            : Rotation.Step(PaletteIndex, EnabledPaletteIndexes, direction);

    // ---------------------------------------------------------------- other settings

    public void ToggleGlow()
    {
        Settings.Glow = !Settings.Glow;
        Settings.Save();
        RaiseNotice(Settings.Glow ? "Glow on" : "Glow off");
    }

    /// <summary>Tray quick toggle: visualizer auto-cycle off, or back on in the last mode used.</summary>
    public void ToggleAutoCycle()
    {
        if (Settings.CycleVisualizers == CycleMode.Off)
            Settings.CycleVisualizers = Settings.LastCycleMode;
        else
        {
            Settings.LastCycleMode = Settings.CycleVisualizers;
            Settings.CycleVisualizers = CycleMode.Off;
        }
        ApplySettings();
        RaiseNotice(Settings.CycleVisualizers switch
        {
            CycleMode.Timer => $"Auto-cycle: every {FormatMinutes(Settings.CycleMinutes)}",
            CycleMode.Song => "Auto-cycle: each new song",
            _ => "Auto-cycle off",
        });
    }

    internal static string FormatMinutes(double minutes) =>
        minutes < 1 ? $"{Math.Round(minutes * 60)} s" : minutes == 1 ? "1 minute" : $"{minutes:0.##} minutes";

    /// <summary>
    /// Call after changing anything in <see cref="Settings"/> directly (the Settings window does this on every edit):
    /// rebuilds palettes and enabled lists, moves off a visualizer/palette that was just disabled or deleted,
    /// restarts the auto-cycle timer, saves, and raises <see cref="SettingsChanged"/>.
    /// </summary>
    public void ApplySettings()
    {
        if (Settings.CycleVisualizers != CycleMode.Off) Settings.LastCycleMode = Settings.CycleVisualizers;
        Settings.Normalize();
        RebuildLists();

        if (!EnabledVisualizerIndexes.Contains(VisualizerIndex))
        {
            VisualizerIndex = Rotation.Step(VisualizerIndex, EnabledVisualizerIndexes, 1);
            Settings.Visualizer = CurrentVisualizer.Name;
        }

        ConfigureAudio();
        _cycler?.Restart();
        Settings.Save();
        SettingsChanged?.Invoke();
    }

    /// <summary>Points the audio capture at the source and microphone chosen in settings.</summary>
    private void ConfigureAudio() =>
        Audio.Configure(Settings.AudioSource, Settings.MicrophoneId, (float)Settings.MicrophoneGain);

    /// <summary>Tray quick toggle between what the PC plays and the microphone.</summary>
    public void ToggleMicrophone()
    {
        Settings.AudioSource = Settings.AudioSource == AudioSource.Microphone ? AudioSource.Speakers : AudioSource.Microphone;
        ApplySettings();
        RaiseNotice(Settings.AudioSource == AudioSource.Microphone ? "Listening to the microphone" : "Listening to PC audio");
    }

    /// <summary>Rebuilds <see cref="Palettes"/> and the enabled lists from settings, and re-finds the current palette.</summary>
    private void RebuildLists()
    {
        Palettes = [.. Palette.All, .. Settings.BuildCustomPalettes()];

        var offV = new HashSet<string>(Settings.DisabledVisualizers, StringComparer.OrdinalIgnoreCase);
        var onV = Enumerable.Range(0, Visualizers.Count).Where(i => !offV.Contains(Visualizers[i].Name)).ToList();
        EnabledVisualizerIndexes = onV.Count > 0 ? onV : Enumerable.Range(0, Visualizers.Count).ToList();

        var offP = new HashSet<string>(Settings.DisabledPalettes, StringComparer.OrdinalIgnoreCase);
        var onP = Enumerable.Range(0, Palettes.Count).Where(i => !offP.Contains(Palettes[i].Name)).ToList();
        EnabledPaletteIndexes = onP.Count > 0 ? onP : Enumerable.Range(0, Palettes.Count).ToList();

        int found = Palettes.ToList().FindIndex(p => string.Equals(p.Name, Settings.PaletteName, StringComparison.OrdinalIgnoreCase));
        PaletteIndex = found >= 0 ? found : 0;
        if (!EnabledPaletteIndexes.Contains(PaletteIndex))
            PaletteIndex = Rotation.Step(PaletteIndex, EnabledPaletteIndexes, 1);
        Settings.PaletteName = CurrentPalette.Name;
    }

    // ---------------------------------------------------------------- songs and notices

    private void OnSongChanged(string? title, string? artist)
    {
        bool songCycling = Settings.CycleVisualizers == CycleMode.Song || Settings.CycleColors == ColorCycleMode.Song;
        if (!songCycling) return;

        int visualizer = VisualizerIndex, palette = PaletteIndex;
        _batchingNotices = true;
        try { _cycler?.OnSongChanged(); }
        finally { _batchingNotices = false; }

        var parts = new List<string>();
        if (title != null) parts.Add(artist != null ? $"♪  {artist} — {title}" : $"♪  {title}");
        if (VisualizerIndex != visualizer) parts.Add(CurrentVisualizer.Name);
        if (PaletteIndex != palette) parts.Add($"Colors: {CurrentPalette.Name}");
        if (parts.Count > 0) Notice?.Invoke(string.Join("   ·   ", parts));
    }

    private void RaiseNotice(string text)
    {
        if (!_batchingNotices) Notice?.Invoke(text);
    }

    // ---------------------------------------------------------------- windows

    /// <summary>Opens the Settings window, or brings it to the front if it's already open.</summary>
    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowWindowFor(DisplayMode mode)
    {
        var old = _window;
        _window = new MainWindow(this, mode);
        _window.Show();
        old?.CloseForSwitch();
    }

    /// <summary>Called when the user closes the normal window with its X button.</summary>
    internal void OnWindowClosedByUser(MainWindow window)
    {
        if (window == _window) Quit();
    }

    public void Quit()
    {
        Settings.Save();
        Updater.ApplyPendingOnExit();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWindow?.Close();
        _cycler?.Dispose();
        _songs?.Dispose();
        _screensaver?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Audio.Dispose();
        base.OnExit(e);
    }
}
