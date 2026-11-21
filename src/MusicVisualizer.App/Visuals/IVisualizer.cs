using MusicVisualizer.Audio;
using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <param name="GlowCanvas">
/// When glow is on: a low-resolution layer shown underneath <paramref name="Canvas"/> and stretched to full size
/// by the GPU. It uses the same coordinates as the main canvas. Draw soft halos here with blurred paints
/// (blur mask filters scale with the canvas, so the blur runs at low resolution and is cheap). Null when glow is off.
/// </param>
public readonly record struct RenderContext(
    SKCanvas Canvas,
    float Width,
    float Height,
    float Scale,        // device pixels per DIP, for sizing strokes consistently
    AudioFrame Frame,
    Palette Palette,
    float Dt,
    SKCanvas? GlowCanvas,
    bool Overlay)
{
    public bool Glow => GlowCanvas != null;
}

public interface IVisualizer
{
    string Name { get; }

    /// <summary>
    /// Fraction of full resolution to render at; the GPU stretches the result to fit. Lower it for
    /// visualizers that fill a lot of pixels with smooth shapes, where the slight softening isn't noticeable.
    /// </summary>
    float RenderScale => 1f;

    /// <summary>Draws one frame. The canvas has already been cleared by the host.</summary>
    void Render(in RenderContext ctx);
}
