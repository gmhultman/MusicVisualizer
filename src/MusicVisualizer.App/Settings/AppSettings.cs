using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicVisualizer.Settings;

public enum DisplayMode { Windowed, Overlay }

public enum OverlayLayout { BottomStrip, FullScreen }

public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicVisualizer", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Visualizer { get; set; } = "Radial";
    public int PaletteIndex { get; set; }
    public bool Glow { get; set; } = true;
    public DisplayMode Mode { get; set; } = DisplayMode.Windowed;
    public OverlayLayout OverlayLayout { get; set; } = OverlayLayout.BottomStrip;
    public double OverlayHeight { get; set; } = 240;

    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 650;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch { /* corrupt or unreadable settings: fall back to defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
