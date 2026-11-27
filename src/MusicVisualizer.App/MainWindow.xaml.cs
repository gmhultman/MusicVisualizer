using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicVisualizer.Interop;
using MusicVisualizer.Settings;
using MusicVisualizer.Visuals;
using SkiaSharp;

namespace MusicVisualizer;

public partial class MainWindow : Window
{
    private const int GlowDownscale = 4;
    private const float FadeSeconds = 0.25f; // each way: out, then in

    private readonly App _app;
    private readonly DisplayMode _mode;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    private WriteableBitmap? _bitmap;
    private SKSurface? _surface;
    private WriteableBitmap? _glowBitmap;
    private SKSurface? _glowSurface;
    private SKPaint? _glowBlurPaint;
    private float _glowBlurScale;
    private TimeSpan _lastRenderTime;
    private double _lastTick;
    private bool _switching;
    private bool _fullscreen;
    private string _status = "";
    private IVisualizer? _shown; // what's on screen; lags CurrentVisualizer while fading out
    private float _fade = 1;
    private readonly Dictionary<VisualizerCategory, System.Windows.Controls.Button> _categoryButtons = [];
    private bool _menuOpen; // a toolbar dropdown is showing, so don't auto-hide the toolbar

    public MainWindow(App app, DisplayMode mode)
    {
        _app = app;
        _mode = mode;
        InitializeComponent();

        if (mode == DisplayMode.Overlay) ConfigureOverlay();
        else ConfigureWindowed();

        Loaded += OnLoaded;
        Closed += OnClosed;
        _app.Notice += OnNotice;
        _app.SettingsChanged += OnSettingsChanged;
        _idleTimer.Tick += (_, _) => OnIdle();
    }

    // ---------------------------------------------------------------- setup

    private void ConfigureWindowed()
    {
        var s = _app.Settings;
        Width = Math.Max(400, s.WindowWidth);
        Height = Math.Max(250, s.WindowHeight);
        if (s.WindowLeft is double left && s.WindowTop is double top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        RebuildVisualizerButtons();

        MouseMove += (_, _) => OnActivity();
        KeyDown += OnKeyDown;
        Root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && !Toolbar.IsMouseOver) ToggleFullscreen();
        };
        Closing += (_, _) => SaveBounds();
    }

    private void ConfigureOverlay()
    {
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Toolbar.Visibility = Visibility.Collapsed;

        var s = _app.Settings;
        Opacity = s.OverlayOpacity;
        if (s.OverlayLayout == OverlayLayout.FullScreen)
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            double h = Math.Clamp(s.OverlayHeight, 80, wa.Height);
            Left = wa.Left;
            Width = wa.Width;
            Height = h;
            Top = wa.Bottom - h;
        }

        // Click-through, no taskbar/Alt+Tab entry, never steals focus.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            nint ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += OnRendering;
        _lastTick = _clock.Elapsed.TotalSeconds;

        if (_mode == DisplayMode.Windowed)
        {
            ShowHint($"←/→  visualizers   ·   C  colors   ·   G  glow   ·   F11  fullscreen   ·   O  overlay   ·   S  settings", 5);
            OnActivity();
        }
        else
        {
            ShowHint("Overlay on — Ctrl+Alt+V or the tray icon to return", 4);
        }
    }

    /// <summary>
    /// One toolbar button per category that has enabled visualizers. Each opens a menu of that category's
    /// visualizers; the button for the category that's showing is highlighted.
    /// </summary>
    private void RebuildVisualizerButtons()
    {
        VisualizerButtons.Children.Clear();
        _categoryButtons.Clear();
        foreach (var group in _app.EnabledVisualizersByCategory)
        {
            var button = new System.Windows.Controls.Button { Content = $"{group.Key}  ▾" };
            var indexes = group.ToList();
            button.Click += (_, _) => OpenCategoryMenu(button, indexes);
            _categoryButtons[group.Key] = button;
            VisualizerButtons.Children.Add(button);
        }
        UpdateActiveCategory();
    }

    private void UpdateActiveCategory()
    {
        var current = _app.CurrentVisualizer;
        foreach (var (category, button) in _categoryButtons)
        {
            bool active = category == current.Category;
            button.Style = (Style)FindResource(active ? "ToolbarButtonActive" : "ToolbarButton");
            button.ToolTip = active ? current.Name : null;
        }
    }

    private void OpenCategoryMenu(System.Windows.Controls.Button button, List<int> indexes) =>
        OpenToolbarMenu(button, indexes.Select(i => (_app.Visualizers[i].Name, i == _app.VisualizerIndex, (Action)(() => _app.SetVisualizer(i)))));

    /// <summary>Opens a dark dropdown under a toolbar button; the toolbar stays up while it's open.</summary>
    private void OpenToolbarMenu(System.Windows.Controls.Button button, IEnumerable<(string Name, bool Checked, Action Pick)> items)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)FindResource("ToolbarMenu"),
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        var itemStyle = (Style)FindResource("ToolbarMenuItem");
        foreach (var (name, isChecked, pick) in items)
        {
            var item = new System.Windows.Controls.MenuItem { Header = name, IsChecked = isChecked, Style = itemStyle };
            item.Click += (_, _) => pick();
            menu.Items.Add(item);
        }

        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => { _menuOpen = false; OnActivity(); };
        menu.IsOpen = true;
    }

    private void OnNotice(string text)
    {
        ShowHint(text);
        if (_mode == DisplayMode.Windowed) UpdateActiveCategory(); // the visualizer may have changed
    }

    private void OnSettingsChanged()
    {
        if (_mode == DisplayMode.Windowed) RebuildVisualizerButtons();
        else Opacity = _app.Settings.OverlayOpacity;
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50 &&
        top >= SystemParameters.VirtualScreenTop - 50 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    // ---------------------------------------------------------------- render loop

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per actual frame; only draw once per frame.
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderTime) return;
            _lastRenderTime = args.RenderingTime;
        }

        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastTick, 0.001, 0.1);
        _lastTick = now;

        var frame = _app.Audio.Update(dt);
        UpdateStatus();

        var visualizer = AdvanceFade(dt);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX * visualizer.RenderScale;
        int pw = (int)Math.Round(Root.ActualWidth * scale);
        int ph = (int)Math.Round(Root.ActualHeight * scale);
        if (pw < 2 || ph < 2) return;
        EnsureSurface(pw, ph);

        bool glow = _app.Settings.Glow;
        GlowSurface.Visibility = glow ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Controls.Panel.SetZIndex(GlowSurface, visualizer.GlowAbove ? 1 : -1);

        var bitmap = _bitmap!;
        var glowBitmap = _glowBitmap!;
        bitmap.Lock();
        if (glow) glowBitmap.Lock();
        try
        {
            // The main layer is transparent where nothing is drawn: the glow layer and the
            // window background (or, in overlay mode, the desktop) show through.
            var canvas = _surface!.Canvas;
            canvas.Clear(SKColors.Transparent);

            SKCanvas? glowCanvas = null;
            if (glow)
            {
                glowCanvas = _glowSurface!.Canvas;
                glowCanvas.Clear(SKColors.Transparent);
                glowCanvas.Save();
                glowCanvas.Scale(glowBitmap.PixelWidth / (float)pw, glowBitmap.PixelHeight / (float)ph);
                // Everything drawn on the glow layer is blurred once, as a whole, when the layer is restored.
                EnsureGlowBlur((float)scale);
                glowCanvas.SaveLayer(_glowBlurPaint);
            }

            var ctx = new RenderContext(canvas, pw, ph, (float)scale, frame, _app.CurrentPalette, dt, glowCanvas, _mode == DisplayMode.Overlay);
            visualizer.Render(ctx);
            canvas.Flush();
            bitmap.AddDirtyRect(new Int32Rect(0, 0, pw, ph));

            if (glowCanvas != null)
            {
                glowCanvas.Restore(); // the blur layer
                glowCanvas.Restore(); // the scale
                glowCanvas.Flush();
                glowBitmap.AddDirtyRect(new Int32Rect(0, 0, glowBitmap.PixelWidth, glowBitmap.PixelHeight));
            }
        }
        finally
        {
            bitmap.Unlock();
            if (glow) glowBitmap.Unlock();
        }
    }

    /// <summary>
    /// Returns the visualizer to draw this frame. After a switch, keeps drawing the old one while the stage
    /// fades out, then swaps and fades back in. With fades off (or on the first frame) it swaps at once.
    /// </summary>
    private IVisualizer AdvanceFade(float dt)
    {
        var target = _app.CurrentVisualizer;
        if (_shown == null || !_app.Settings.FadeTransitions)
        {
            _shown = target;
            _fade = 1;
        }
        else if (_shown != target)
        {
            _fade -= dt / FadeSeconds;
            if (_fade <= 0)
            {
                _fade = 0;
                _shown = target;
            }
        }
        else if (_fade < 1)
        {
            _fade = Math.Min(1, _fade + dt / FadeSeconds);
        }
        Stage.Opacity = _fade;
        return _shown;
    }

    private void EnsureGlowBlur(float scale)
    {
        if (_glowBlurPaint != null && _glowBlurScale == scale) return;
        _glowBlurPaint?.Dispose();
        float sigma = 10f * scale; // in full-res pixels; the canvas scale maps it to the low-res layer
        _glowBlurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };
        _glowBlurScale = scale;
    }

    private void EnsureSurface(int pw, int ph)
    {
        if (_bitmap != null && _bitmap.PixelWidth == pw && _bitmap.PixelHeight == ph) return;

        _surface?.Dispose();
        _bitmap = new WriteableBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32, null);
        _surface = SKSurface.Create(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul), _bitmap.BackBuffer, _bitmap.BackBufferStride);
        Surface.Source = _bitmap;

        int gw = Math.Max(1, (pw + GlowDownscale - 1) / GlowDownscale);
        int gh = Math.Max(1, (ph + GlowDownscale - 1) / GlowDownscale);
        _glowSurface?.Dispose();
        _glowBitmap = new WriteableBitmap(gw, gh, 96, 96, PixelFormats.Pbgra32, null);
        _glowSurface = SKSurface.Create(new SKImageInfo(gw, gh, SKColorType.Bgra8888, SKAlphaType.Premul), _glowBitmap.BackBuffer, _glowBitmap.BackBufferStride);
        GlowSurface.Source = _glowBitmap;
    }

    private void UpdateStatus()
    {
        string status = _app.Audio.Status;
        if (status == _status) return;
        _status = status;
        StatusText.Text = status;
    }

    // ---------------------------------------------------------------- input (windowed)

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.Space: _app.NextVisualizer(1); break;
            case Key.Left: _app.NextVisualizer(-1); break;
            case Key.C: _app.NextPalette(); break;
            case Key.S: _app.OpenSettings(); break;
            case Key.G: _app.ToggleGlow(); break;
            case Key.O: _app.SetMode(DisplayMode.Overlay); break;
            case Key.F11 or Key.F: ToggleFullscreen(); break;
            case Key.Escape when _fullscreen: ToggleFullscreen(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnPaletteClick(object sender, RoutedEventArgs e) => _app.NextPalette();
    private void OnSettingsClick(object sender, RoutedEventArgs e) => _app.OpenSettings();
    private void OnGlowClick(object sender, RoutedEventArgs e) => _app.ToggleGlow();
    private void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void OnOverlayClick(object sender, RoutedEventArgs e) => _app.SetMode(DisplayMode.Overlay);

    private void ToggleFullscreen()
    {
        if (_mode != DisplayMode.Windowed) return;
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            SaveBounds();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal; // re-maximise so the chromeless window covers the taskbar
            WindowState = WindowState.Maximized;
            Topmost = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Topmost = false;
        }
        OnActivity();
    }

    private void OnActivity()
    {
        Fade(Toolbar, 1, 0.15);
        Cursor = null;
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdle()
    {
        _idleTimer.Stop();
        if (Toolbar.IsMouseOver || _menuOpen) { _idleTimer.Start(); return; }
        Fade(Toolbar, 0, 0.5);
        if (_fullscreen) Cursor = Cursors.None;
    }

    private void SaveBounds()
    {
        if (_mode != DisplayMode.Windowed || _fullscreen) return;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (b.IsEmpty) return;
        var s = _app.Settings;
        (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight) = (b.Left, b.Top, b.Width, b.Height);
        s.Save();
    }

    // ---------------------------------------------------------------- hints

    private void ShowHint(string text) => ShowHint(text, 1.6);

    private void ShowHint(string text, double seconds)
    {
        Hint.Text = text;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15 + seconds))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.65 + seconds))));
        Hint.BeginAnimation(OpacityProperty, anim);
    }

    private static void Fade(UIElement element, double to, double seconds) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)));

    // ---------------------------------------------------------------- lifetime

    /// <summary>Closes this window because the app is switching modes (not quitting).</summary>
    internal void CloseForSwitch()
    {
        _switching = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _app.Notice -= OnNotice;
        _app.SettingsChanged -= OnSettingsChanged;
        _idleTimer.Stop();
        _surface?.Dispose();
        _surface = null;
        _glowSurface?.Dispose();
        _glowSurface = null;
        if (!_switching) _app.OnWindowClosedByUser(this);
    }
}
