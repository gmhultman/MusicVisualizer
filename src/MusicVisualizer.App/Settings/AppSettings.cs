using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MusicVisualizer.Audio;
using MusicVisualizer.Visuals;
using SkiaSharp;

namespace MusicVisualizer.Settings;

public enum DisplayMode { Windowed, Overlay }

public enum OverlayLayout { BottomStrip, FullScreen }

/// <summary>When the visualizer changes by itself.</summary>
public enum CycleMode { Off, Timer, Song }

/// <summary>When the colour palette changes by itself.</summary>
public enum ColorCycleMode { Off, WithVisualizer, Timer, Song }

/// <summary>A user-made palette as stored in settings.json.</summary>
public sealed class CustomPaletteData
{
    public string Name { get; set; } = "";

    /// <summary>Colours as <c>#RRGGBB</c>.</summary>
    public List<string> Colors { get; set; } = [];
}

public sealed class AppSettings
{
    public const double MinCycleMinutes = 0.25;
    public const double MaxCycleMinutes = 60;
    public const double MinOverlayOpacity = 0.2;
    public const double MinMicrophoneGain = 1;
    public const double MaxMicrophoneGain = 20;
    public const double DefaultMicrophoneGain = 4;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicVisualizer", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Visualizer { get; set; } = "Radial";

    /// <summary>The selected palette, by name (built-in or custom).</summary>
    public string? PaletteName { get; set; }

    /// <summary>Pre-1.1 settings stored the palette by index. Read for migration only; never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PaletteIndex { get; set; }

    public bool Glow { get; set; } = true;
    public DisplayMode Mode { get; set; } = DisplayMode.Windowed;
    public OverlayLayout OverlayLayout { get; set; } = OverlayLayout.BottomStrip;
    public double OverlayHeight { get; set; } = 240;
    public double OverlayOpacity { get; set; } = 1.0;

    /// <summary>Names of visualizers the user switched off. Stored this way so new visualizers start enabled.</summary>
    public List<string> DisabledVisualizers { get; set; } = [];

    /// <summary>Names of palettes skipped when stepping or cycling through colours.</summary>
    public List<string> DisabledPalettes { get; set; } = [];

    public List<CustomPaletteData> CustomPalettes { get; set; } = [];

    public CycleMode CycleVisualizers { get; set; } = CycleMode.Off;

    /// <summary>The mode the tray's "Auto-cycle" toggle turns back on.</summary>
    public CycleMode LastCycleMode { get; set; } = CycleMode.Timer;

    public ColorCycleMode CycleColors { get; set; } = ColorCycleMode.Off;
    public double CycleMinutes { get; set; } = 3;
    public bool Shuffle { get; set; }
    public bool FadeTransitions { get; set; } = true;

    /// <summary>Listen to what the PC plays, or to a microphone.</summary>
    public AudioSource AudioSource { get; set; } = AudioSource.Speakers;

    /// <summary>The chosen microphone's device id; null = the Windows default microphone.</summary>
    public string? MicrophoneId { get; set; }

    /// <summary>Boost applied to microphone input (mics are much quieter than playback).</summary>
    public double MicrophoneGain { get; set; } = DefaultMicrophoneGain;

    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 650;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return FromJson(File.ReadAllText(FilePath));
        }
        catch { /* unreadable settings: fall back to defaults */ }
        return new AppSettings().Normalize();
    }

    /// <summary>Parses settings JSON, migrating older formats and clamping out-of-range values.</summary>
    internal static AppSettings FromJson(string json)
    {
        AppSettings? s = null;
        try { s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions); }
        catch { /* corrupt: defaults */ }
        return (s ?? new AppSettings()).Normalize();
    }

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Migrates old fields and fixes anything a hand-edited file might have broken.</summary>
    internal AppSettings Normalize()
    {
        if (string.IsNullOrWhiteSpace(PaletteName))
            PaletteName = Palette.All[Math.Clamp(PaletteIndex, 0, Palette.All.Count - 1)].Name;
        PaletteIndex = 0;

        DisabledVisualizers ??= [];
        DisabledPalettes ??= [];
        CustomPalettes ??= [];
        CustomPalettes.RemoveAll(p => p == null);
        foreach (var p in CustomPalettes) p.Colors ??= [];

        if (LastCycleMode == CycleMode.Off) LastCycleMode = CycleMode.Timer;
        CycleMinutes = double.IsFinite(CycleMinutes) ? Math.Clamp(CycleMinutes, MinCycleMinutes, MaxCycleMinutes) : 3;
        OverlayOpacity = double.IsFinite(OverlayOpacity) ? Math.Clamp(OverlayOpacity, MinOverlayOpacity, 1.0) : 1.0;
        MicrophoneGain = double.IsFinite(MicrophoneGain) ? Math.Clamp(MicrophoneGain, MinMicrophoneGain, MaxMicrophoneGain) : DefaultMicrophoneGain;
        if (!Enum.IsDefined(AudioSource)) AudioSource = AudioSource.Speakers;
        if (string.IsNullOrWhiteSpace(MicrophoneId)) MicrophoneId = null;
        return this;
    }

    /// <summary>
    /// The custom palettes that are usable: a name that's not empty and doesn't clash with a built-in or an
    /// earlier custom palette, and at least two valid colours. Invalid entries are skipped, not deleted.
    /// </summary>
    public List<Palette> BuildCustomPalettes()
    {
        var names = new HashSet<string>(Palette.All.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var result = new List<Palette>();
        foreach (var data in CustomPalettes)
        {
            var name = data.Name?.Trim() ?? "";
            if (name.Length == 0 || !names.Add(name)) continue;
            var colors = new List<SKColor>();
            foreach (var hex in data.Colors)
                if (Palette.TryParseHex(hex, out var c)) colors.Add(c);
            if (colors.Count < Palette.MinColors) continue;
            result.Add(Palette.Create(name, colors));
        }
        return result;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, ToJson());
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
