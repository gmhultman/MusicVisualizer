using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MusicVisualizer.Audio;
using MusicVisualizer.Settings;
using MusicVisualizer.Visuals;
using SkiaSharp;

namespace MusicVisualizer;

/// <summary>
/// Live-apply settings: every edit writes to <see cref="App.Settings"/> and calls <see cref="App.ApplySettings"/>.
/// Not owned by the visualizer window, because the overlay window is click-through.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly List<CheckBox> _visualizerBoxes = [];
    private readonly List<(VisualizerCategory Category, Expander Expander, List<CheckBox> Boxes)> _visualizerGroups = [];
    private bool _loading;
    private float _meter;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);

        BuildVisualizerList();
        BuildMicList();
        Refresh();

        var meterTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        meterTimer.Tick += (_, _) => UpdateLevelMeter();
        meterTimer.Start();
        Closed += (_, _) => meterTimer.Stop();

        _app.SettingsChanged += OnAppSettingsChanged;
        _app.Notice += OnAppNotice;
        Activated += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _app.SettingsChanged -= OnAppSettingsChanged;
            _app.Notice -= OnAppNotice;
        };
    }

    private AppSettings S => _app.Settings;

    private void OnAppSettingsChanged() => Refresh();

    // Visualizer, palette and glow changes (toolbar, keys, auto-cycle) raise a notice but not SettingsChanged.
    private void OnAppNotice(string _) => Refresh();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Writes the settings through the app; the resulting SettingsChanged refreshes this window.</summary>
    private void Apply()
    {
        if (_loading) return;
        _app.ApplySettings();
    }

    // ---------------------------------------------------------------- populate

    private void Refresh()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            SourceBox.SelectedIndex = (int)S.AudioSource;
            SelectMicrophone();
            GainSlider.Value = S.MicrophoneGain;
            GainText.Text = $"{S.MicrophoneGain:0.#}×";
            UpdateMicEnabled();

            var offV = new HashSet<string>(S.DisabledVisualizers, StringComparer.OrdinalIgnoreCase);
            foreach (var box in _visualizerBoxes)
                box.IsChecked = !offV.Contains((string)box.Tag);
            UpdateGroupHeaders();

            CycleVisualizersBox.SelectedIndex = (int)S.CycleVisualizers;
            CycleColorsBox.SelectedIndex = (int)S.CycleColors;
            if (!MinutesBox.IsKeyboardFocusWithin) ShowMinutes();
            UpdateMinutesEnabled();
            ShuffleBox.IsChecked = S.Shuffle;

            BuildPaletteList();

            GlowBox.IsChecked = S.Glow;
            FadeBox.IsChecked = S.FadeTransitions;
            OpacitySlider.Value = Math.Round(S.OverlayOpacity * 100);
            OpacityText.Text = $"{Math.Round(OpacitySlider.Value)}%";
        }
        finally { _loading = false; }
    }

    // ---------------------------------------------------------------- sound

    /// <summary>Fills the microphone list: "Default" first (Tag null), then each active recording device (Tag = id).</summary>
    private void BuildMicList()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            MicBox.Items.Clear();
            MicBox.Items.Add(new ComboBoxItem { Content = "Windows default microphone", Tag = null });
            foreach (var mic in _app.Audio.ListMicrophones())
                MicBox.Items.Add(new ComboBoxItem { Content = mic.Name, Tag = mic.Id });
            if (S.MicrophoneId != null && !MicBox.Items.Cast<ComboBoxItem>().Any(i => (string?)i.Tag == S.MicrophoneId))
                MicBox.Items.Add(new ComboBoxItem { Content = "(Chosen microphone — not connected)", Tag = S.MicrophoneId });
            SelectMicrophone();
        }
        finally { _loading = was; }
    }

    private void SelectMicrophone() =>
        MicBox.SelectedItem = MicBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == S.MicrophoneId)
                              ?? (MicBox.Items.Count > 0 ? MicBox.Items[0] : null);

    private void UpdateMicEnabled()
    {
        bool mic = S.AudioSource == AudioSource.Microphone;
        MicBox.IsEnabled = GainSlider.IsEnabled = mic;
        MicLabel.Opacity = GainLabel.Opacity = GainText.Opacity = mic ? 1 : 0.45;
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SourceBox.SelectedIndex < 0) return;
        S.AudioSource = (AudioSource)SourceBox.SelectedIndex;
        Apply();
    }

    private void OnMicDropDownOpened(object? sender, EventArgs e) => BuildMicList(); // pick up newly plugged-in mics

    private void OnMicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicBox.SelectedItem is not ComboBoxItem item) return;
        S.MicrophoneId = (string?)item.Tag;
        Apply();
    }

    private void OnGainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GainText == null) return; // during InitializeComponent
        GainText.Text = $"{GainSlider.Value:0.#}×";
        if (_loading || Math.Abs(GainSlider.Value - S.MicrophoneGain) < 0.001) return;
        S.MicrophoneGain = GainSlider.Value;
        Apply();
    }

    /// <summary>Live input meter, so the user can see whether the mic hears anything and set the sensitivity.</summary>
    private void UpdateLevelMeter()
    {
        float level = _app.Audio.Frame.Level;
        _meter += (level - _meter) * (level > _meter ? 0.6f : 0.15f);
        LevelBar.Width = Math.Max(0, LevelTrack.ActualWidth * Math.Clamp(_meter, 0f, 1f));
    }

    // ---------------------------------------------------------------- visualizers

    /// <summary>One expander per category (empty categories are skipped), each holding a 2-column grid of checkboxes.</summary>
    private void BuildVisualizerList()
    {
        VisualizerGroups.Children.Clear();
        _visualizerBoxes.Clear();
        _visualizerGroups.Clear();
        var all = Enumerable.Range(0, _app.Visualizers.Count);
        foreach (var group in VisualizerCatalog.GroupByCategory(_app.Visualizers, all))
        {
            var grid = new UniformGrid { Columns = 2 };
            var boxes = new List<CheckBox>();
            foreach (int i in group)
            {
                var name = _app.Visualizers[i].Name;
                var box = new CheckBox { Content = name, Tag = name };
                box.Click += OnVisualizerClick;
                _visualizerBoxes.Add(box);
                boxes.Add(box);
                grid.Children.Add(box);
            }
            var expander = new Expander { IsExpanded = true, Content = grid };
            _visualizerGroups.Add((group.Key, expander, boxes));
            VisualizerGroups.Children.Add(expander);
        }
    }

    /// <summary>Header text like "Scenes   2 of 3 on".</summary>
    private void UpdateGroupHeaders()
    {
        foreach (var (category, expander, boxes) in _visualizerGroups)
        {
            int on = boxes.Count(b => b.IsChecked == true);
            expander.Header = new TextBlock
            {
                Inlines =
                {
                    new System.Windows.Documents.Run(category.ToString()) { FontWeight = FontWeights.SemiBold },
                    new System.Windows.Documents.Run($"   {on} of {boxes.Count} on") { Foreground = (Brush)FindResource("Dlg.SubText"), FontSize = 12 },
                },
            };
        }
    }

    private void OnVisualizerClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var box = (CheckBox)sender;
        var name = (string)box.Tag;
        if (box.IsChecked == true)
            S.DisabledVisualizers.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        else
        {
            if (!_visualizerBoxes.Any(b => b.IsChecked == true))
            {
                box.IsChecked = true; // at least one visualizer must stay on
                return;
            }
            if (!S.DisabledVisualizers.Contains(name, StringComparer.OrdinalIgnoreCase))
                S.DisabledVisualizers.Add(name);
        }
        Apply();
    }

    // ---------------------------------------------------------------- auto-cycle

    private void OnCycleVisualizersChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CycleVisualizersBox.SelectedIndex < 0) return;
        S.CycleVisualizers = (CycleMode)CycleVisualizersBox.SelectedIndex;
        UpdateMinutesEnabled();
        Apply();
    }

    private void OnCycleColorsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CycleColorsBox.SelectedIndex < 0) return;
        S.CycleColors = (ColorCycleMode)CycleColorsBox.SelectedIndex;
        UpdateMinutesEnabled();
        Apply();
    }

    private void UpdateMinutesEnabled()
    {
        bool timer = S.CycleVisualizers == CycleMode.Timer || S.CycleColors == ColorCycleMode.Timer;
        MinutesPanel.IsEnabled = timer;
        MinutesLabel.Opacity = timer ? 1 : 0.45;
    }

    private void ShowMinutes()
    {
        MinutesBox.Text = S.CycleMinutes.ToString("0.##", CultureInfo.CurrentCulture);
        MinutesHint.Text = S.CycleMinutes < 1
            ? $"minutes ({App.FormatMinutes(S.CycleMinutes)})"
            : S.CycleMinutes == 1 ? "minute" : "minutes";
    }

    private void OnMinutesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitMinutes();
            MinutesBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ShowMinutes(); // revert; keep the window open
            e.Handled = true;
        }
    }

    private void OnMinutesLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitMinutes();

    private void CommitMinutes()
    {
        if (_loading) return;
        var text = MinutesBox.Text.Trim();
        if (TryParseNumber(text, out double minutes) && double.IsFinite(minutes))
        {
            minutes = Math.Clamp(minutes, AppSettings.MinCycleMinutes, AppSettings.MaxCycleMinutes);
            if (minutes != S.CycleMinutes)
            {
                S.CycleMinutes = minutes;
                Apply();
            }
        }
        ShowMinutes(); // shows the clamped value, or reverts invalid input
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void OnShuffleClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.Shuffle = ShuffleBox.IsChecked == true;
        Apply();
    }

    // ---------------------------------------------------------------- colors

    private void BuildPaletteList()
    {
        PaletteList.Children.Clear();
        var off = new HashSet<string>(S.DisabledPalettes, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _app.Palettes.Count; i++)
            PaletteList.Children.Add(BuildPaletteRow(i, _app.Palettes[i], !off.Contains(_app.Palettes[i].Name)));
    }

    private UIElement BuildPaletteRow(int index, Palette palette, bool included)
    {
        bool current = index == _app.PaletteIndex;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new CheckBox
        {
            IsChecked = included,
            Tag = palette.Name,
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Include when stepping or cycling colors",
        };
        box.Click += OnPaletteIncludedClick;
        grid.Children.Add(box);

        var swatch = new Border
        {
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Background = GradientFor(palette.Colors),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(swatch, 1);
        grid.Children.Add(swatch);

        var name = new TextBlock
        {
            Text = palette.IsCustom ? palette.Name + "  (custom)" : palette.Name,
            Margin = new Thickness(10, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var smallButton = (Style)FindResource("SmallButton");
        if (palette.IsCustom)
        {
            var edit = new Button { Content = "Edit", Style = smallButton, Tag = palette.Name };
            edit.Click += OnEditPaletteClick;
            var delete = new Button { Content = "Delete", Style = smallButton, Tag = palette.Name };
            delete.Click += OnDeletePaletteClick;
            buttons.Children.Add(edit);
            buttons.Children.Add(delete);
        }
        if (current)
            buttons.Children.Add(new TextBlock
            {
                Text = "In use",
                Foreground = (Brush)FindResource("Dlg.Accent"),
                FontSize = 12,
                Width = 44,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        else
        {
            var use = new Button { Content = "Use", Style = smallButton, Width = 44, Tag = index };
            use.Click += OnUsePaletteClick;
            buttons.Children.Add(use);
        }
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(buttons);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(4),
            Background = current ? new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xB8, 0xE6)) : Brushes.Transparent,
        };
    }

    /// <summary>A horizontal gradient through the given colours.</summary>
    internal static LinearGradientBrush GradientFor(IReadOnlyList<SKColor> colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i < colors.Count; i++)
            brush.GradientStops.Add(new GradientStop(ToWpf(colors[i]), colors.Count == 1 ? 0 : (double)i / (colors.Count - 1)));
        brush.Freeze();
        return brush;
    }

    internal static Color ToWpf(SKColor c) => Color.FromRgb(c.Red, c.Green, c.Blue);

    private void OnPaletteIncludedClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var box = (CheckBox)sender;
        var name = (string)box.Tag;
        if (box.IsChecked == true)
            S.DisabledPalettes.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        else
        {
            var off = new HashSet<string>(S.DisabledPalettes, StringComparer.OrdinalIgnoreCase) { name };
            if (_app.Palettes.All(p => off.Contains(p.Name)))
            {
                box.IsChecked = true; // at least one palette must stay in the rotation
                return;
            }
            S.DisabledPalettes.Add(name);
        }
        Apply();
    }

    private void OnUsePaletteClick(object sender, RoutedEventArgs e)
    {
        _app.SetPalette((int)((Button)sender).Tag); // raises Notice, which refreshes the rows
        Refresh();
    }

    private CustomPaletteData? FindCustom(string name) =>
        S.CustomPalettes.FirstOrDefault(p => string.Equals(p.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private void OnNewPaletteClick(object sender, RoutedEventArgs e)
    {
        var editor = new PaletteEditorWindow(S.CustomPalettes.Select(p => p.Name), null) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is not { } result) return;

        S.CustomPalettes.Add(result);
        _app.ApplySettings();
        int index = IndexOfPalette(result.Name);
        if (index >= 0) _app.SetPalette(index);
        Refresh();
    }

    private void OnEditPaletteClick(object sender, RoutedEventArgs e)
    {
        var oldName = (string)((Button)sender).Tag;
        var data = FindCustom(oldName);
        if (data == null) return;

        var others = S.CustomPalettes.Where(p => !ReferenceEquals(p, data)).Select(p => p.Name);
        var editor = new PaletteEditorWindow(others, data) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is not { } result) return;

        int at = S.CustomPalettes.IndexOf(data);
        if (at < 0) return; // removed meanwhile
        S.CustomPalettes[at] = result;

        if (!string.Equals(oldName, result.Name, StringComparison.Ordinal))
        {
            if (string.Equals(S.PaletteName, oldName, StringComparison.OrdinalIgnoreCase))
                S.PaletteName = result.Name;
            for (int i = 0; i < S.DisabledPalettes.Count; i++)
                if (string.Equals(S.DisabledPalettes[i], oldName, StringComparison.OrdinalIgnoreCase))
                    S.DisabledPalettes[i] = result.Name;
        }
        _app.ApplySettings();
    }

    private void OnDeletePaletteClick(object sender, RoutedEventArgs e)
    {
        var name = (string)((Button)sender).Tag;
        if (MessageBox.Show(this, $"Delete the palette \"{name}\"?", "Delete palette",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        S.CustomPalettes.RemoveAll(p => string.Equals(p.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
        S.DisabledPalettes.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        _app.ApplySettings(); // the app falls back to another palette if this one was in use
    }

    private int IndexOfPalette(string name)
    {
        for (int i = 0; i < _app.Palettes.Count; i++)
            if (string.Equals(_app.Palettes[i].Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // ---------------------------------------------------------------- display

    private void OnGlowClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if ((GlowBox.IsChecked == true) != S.Glow) _app.ToggleGlow();
    }

    private void OnFadeClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.FadeTransitions = FadeBox.IsChecked == true;
        Apply();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText == null) return; // during InitializeComponent
        OpacityText.Text = $"{Math.Round(OpacitySlider.Value)}%";
        if (_loading) return;
        double opacity = Math.Round(OpacitySlider.Value) / 100.0;
        if (Math.Abs(opacity - S.OverlayOpacity) < 0.001) return;
        S.OverlayOpacity = opacity;
        Apply();
    }
}
