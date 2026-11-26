using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>Small colour helpers shared by the visualizers.</summary>
internal static class ColorUtil
{
    /// <summary>Blends from <paramref name="a"/> (t = 0) to <paramref name="b"/> (t = 1), including alpha.</summary>
    public static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t),
        (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));

    /// <summary>Darkens by fraction <paramref name="k"/> (0 = unchanged, 1 = black); the result is opaque.</summary>
    public static SKColor Darken(SKColor c, float k) =>
        new((byte)(c.Red * (1 - k)), (byte)(c.Green * (1 - k)), (byte)(c.Blue * (1 - k)));
}
