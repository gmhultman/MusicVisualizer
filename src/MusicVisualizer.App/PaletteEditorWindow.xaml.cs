using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MusicVisualizer.Settings;
using MusicVisualizer.Visuals;
using SkiaSharp;

namespace MusicVisualizer;

/// <summary>Modal dialog that creates or edits a custom palette. Read <see cref="Result"/> after <c>ShowDialog() == true</c>.</summary>
public partial class PaletteEditorWindow : Window
{
    private static readonly SKColor[] DefaultColors = [new(0xFF00E5FF), new(0xFFFF3DCB), new(0xFFFFB700)];

    private readonly HashSet<string> _takenNames;
    private readonly List<SKColor> _colors = [];

    /// <param name="otherCustomNames">Names of the other custom palettes (not the one being edited); built-in names are always taken.</param>
    /// <param name="existing">The palette to edit, or null to make a new one.</param>
    public PaletteEditorWindow(IEnumerable<string> otherCustomNames, CustomPaletteData? existing)
    {
        InitializeComponent();

        _takenNames = new HashSet<string>(Palette.All.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var n in otherCustomNames)
            if (!string.IsNullOrWhiteSpace(n)) _takenNames.Add(n.Trim());

        if (existing != null)
        {
            Title = "Edit palette";
            foreach (var hex in existing.Colors)
                if (Palette.TryParseHex(hex, out var c) && _colors.Count < Palette.MaxColors) _colors.Add(c);
            NameBox.Text = existing.Name?.Trim() ?? "";
        }
        else
        {
            Title = "New palette";
            NameBox.Text = SuggestName();
        }
        if (_colors.Count < Palette.MinColors)
        {
            _colors.Clear();
            _colors.AddRange(DefaultColors);
        }

        RebuildSwatches();
        Validate();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public CustomPaletteData? Result { get; private set; }

    private string SuggestName()
    {
        const string baseName = "My palette";
        if (!_takenNames.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
            if (!_takenNames.Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }

    // ---------------------------------------------------------------- colours

    private void RebuildSwatches()
    {
        SwatchPanel.Children.Clear();
        var style = (Style)FindResource("SwatchButton");
        for (int i = 0; i < _colors.Count; i++)
        {
            var button = new Button
            {
                Style = style,
                Tag = i,
                Background = new SolidColorBrush(SettingsWindow.ToWpf(_colors[i])),
                ToolTip = Palette.ToHex(_colors[i]),
            };
            button.Click += OnSwatchClick;
            SwatchPanel.Children.Add(button);
        }
        AddButton.IsEnabled = _colors.Count < Palette.MaxColors;
        RemoveButton.IsEnabled = _colors.Count > Palette.MinColors;
        Preview.Background = SettingsWindow.GradientFor(_colors);
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        int index = (int)((Button)sender).Tag;
        var c = _colors[index];
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(c.Red, c.Green, c.Blue),
            // Offer the palette's other colours as custom colours (format 0x00BBGGRR).
            CustomColors = _colors.Select(x => x.Red | (x.Green << 8) | (x.Blue << 16)).ToArray(),
        };
        if (dialog.ShowDialog(new OwnerHandle(new WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;

        var picked = dialog.Color;
        _colors[index] = new SKColor(picked.R, picked.G, picked.B);
        RebuildSwatches();
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_colors.Count >= Palette.MaxColors) return;
        _colors[^1].ToHsv(out float h, out float s, out float v);
        _colors.Add(SKColor.FromHsv((h + 40) % 360, s, v));
        RebuildSwatches();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_colors.Count <= Palette.MinColors) return;
        _colors.RemoveAt(_colors.Count - 1);
        RebuildSwatches();
    }

    // ---------------------------------------------------------------- name and save

    private void OnNameChanged(object sender, TextChangedEventArgs e) => Validate();

    /// <summary>Shows an inline error and disables Save when the name is unusable. Returns whether it's valid.</summary>
    private bool Validate()
    {
        if (SaveButton == null) return false; // during InitializeComponent
        var name = NameBox.Text.Trim();
        string? error =
            name.Length == 0 ? "Give the palette a name." :
            _takenNames.Contains(name) ? $"There's already a palette called \"{name}\"." :
            null;

        ErrorText.Text = error ?? "";
        ErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = error == null;
        return error == null;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        Result = new CustomPaletteData
        {
            Name = NameBox.Text.Trim(),
            Colors = _colors.Select(Palette.ToHex).ToList(),
        };
        DialogResult = true;
    }

    private sealed class OwnerHandle(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
