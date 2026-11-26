using System.Globalization;
using SkiaSharp;

namespace MusicVisualizer.Visuals;

public sealed class Palette
{
    public const int MinColors = 2;
    public const int MaxColors = 6;

    private Palette(string name, bool isCustom, IEnumerable<SKColor> colors)
    {
        Name = name;
        IsCustom = isCustom;
        Colors = colors.ToArray();
        if (Colors.Length < MinColors) throw new ArgumentException($"A palette needs at least {MinColors} colors.", nameof(colors));
        // Colours mirrored back to the start, for seamless sweeps around a circle.
        Wrapped = Colors.Concat(Colors.Reverse().Skip(1)).ToArray();
    }

    private Palette(string name, params uint[] argb) : this(name, false, argb.Select(c => new SKColor(c))) { }

    public string Name { get; }
    public bool IsCustom { get; }
    public SKColor[] Colors { get; }
    public SKColor[] Wrapped { get; }

    /// <summary>Makes a user-defined palette. Colours are forced opaque; throws if there are fewer than two.</summary>
    public static Palette Create(string name, IEnumerable<SKColor> colors) =>
        new(name, true, colors.Take(MaxColors).Select(c => c.WithAlpha(255)));

    public SKColor Sample(float t)
    {
        t = Math.Clamp(t, 0f, 1f) * (Colors.Length - 1);
        int i = Math.Min((int)t, Colors.Length - 2);
        float f = t - i;
        SKColor a = Colors[i], b = Colors[i + 1];
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * f),
            (byte)(a.Green + (b.Green - a.Green) * f),
            (byte)(a.Blue + (b.Blue - a.Blue) * f));
    }

    /// <summary>Formats a colour as <c>#RRGGBB</c>.</summary>
    public static string ToHex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

    /// <summary>Parses <c>#RRGGBB</c> or <c>RRGGBB</c> (case-insensitive) into an opaque colour.</summary>
    public static bool TryParseHex(string? text, out SKColor color)
    {
        color = default;
        if (text == null) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) return false;
        color = new SKColor(0xFF000000 | rgb);
        return true;
    }

    public static IReadOnlyList<Palette> All { get; } =
    [
        new("Neon", 0xFF00E5FF, 0xFF4D7CFF, 0xFFB14DFF, 0xFFFF3DCB),
        new("Sunset", 0xFFFFB347, 0xFFFF6B6B, 0xFFE84393, 0xFF7B2FF7),
        new("Aurora", 0xFF2BFFA3, 0xFF1DE9B6, 0xFF00B0FF, 0xFF6C5CE7),
        new("Fire", 0xFFFF1E1E, 0xFFFF6A00, 0xFFFFB700, 0xFFFFF27A),
        new("Ice", 0xFFE8FBFF, 0xFF9BE7FF, 0xFF3FA9F5, 0xFF2E5BFF),
        new("Rainbow", 0xFFFF3B3B, 0xFFFFA63B, 0xFFF5F53B, 0xFF3BFF6B, 0xFF3BC4FF, 0xFF8A3BFF),
        new("Christmas", 0xFFE8263A, 0xFFFFC23D, 0xFF2ECC71, 0xFFE8263A),
        new("Candy Cane", 0xFFFF2E4D, 0xFFFFE3E8, 0xFFFF2E4D, 0xFFFFFFFF),
    ];
}
