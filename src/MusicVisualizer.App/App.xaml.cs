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
    private MainWindow? _window;

    public AppSettings Settings { get; private set; } = new();
    public AudioEngine Audio { get; } = new();
    public IReadOnlyList<IVisualizer> Visualizers { get; } =
        [
            new BarsVisualizer(), new RadialVisualizer(), new ParticlesVisualizer(),
            new FireVisualizer(), new TerrainVisualizer(), new SphereVisualizer(),
            new FireplaceVisualizer(), new HighwayVisualizer(), new DanceDisasterVisualizer(),
        ];

    public int VisualizerIndex { get; private set; }
    public IVisualizer CurrentVisualizer => Visualizers[VisualizerIndex];
    public Palette CurrentPalette => Palette.All[Settings.PaletteIndex];

    /// <summary>Raised when the visualizer, palette or glow changes, so the window can show a hint.</summary>
    public event Action<string>? Notice;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = AppSettings.Load();
        Settings.PaletteIndex = Math.Clamp(Settings.PaletteIndex, 0, Palette.All.Count - 1);
        VisualizerIndex = Math.Max(0, Visualizers.ToList().FindIndex(v => v.Name == Settings.Visualizer));

        Audio.Start();

        _screensaver = new ScreensaverBlocker(Audio); // blocks the screensaver only while music plays

        _tray = new TrayIcon(this);

        _hotkeys = new GlobalHotkeys();
        _hotkeys.RegisterCtrlAlt(Key.V, ToggleMode);
        _hotkeys.RegisterCtrlAlt(Key.N, () => SetVisualizer((VisualizerIndex + 1) % Visualizers.Count));
        _hotkeys.RegisterCtrlAlt(Key.C, () => SetPalette((Settings.PaletteIndex + 1) % Palette.All.Count));

        ShowWindowFor(Settings.Mode);
    }

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

    public void SetVisualizer(int index)
    {
        VisualizerIndex = index;
        Settings.Visualizer = CurrentVisualizer.Name;
        Settings.Save();
        Notice?.Invoke(CurrentVisualizer.Name);
    }

    public void SetPalette(int index)
    {
        Settings.PaletteIndex = index;
        Settings.Save();
        Notice?.Invoke($"Colors: {CurrentPalette.Name}");
    }

    public void ToggleGlow()
    {
        Settings.Glow = !Settings.Glow;
        Settings.Save();
        Notice?.Invoke(Settings.Glow ? "Glow on" : "Glow off");
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
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _screensaver?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Audio.Dispose();
        base.OnExit(e);
    }
}
