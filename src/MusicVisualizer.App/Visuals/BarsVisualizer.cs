using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>Classic spectrum bars with a palette gradient, glow, peak caps and a floor reflection.</summary>
public sealed class BarsVisualizer : IVisualizer
{
    private readonly SKPaint _bar = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _glow = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White.WithAlpha(150) };
    private readonly SKPaint _reflect = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White.WithAlpha(45) };
    private readonly SKPaint _cap = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White.WithAlpha(230) };

    private SKShader? _shader;
    private (Palette? palette, float width, float top, float bottom) _shaderKey;

    public string Name => "Bars";
    public VisualizerCategory Category => VisualizerCategory.Abstract;

    public void Render(in RenderContext ctx)
    {
        var canvas = ctx.Canvas;
        var frame = ctx.Frame;
        int n = frame.Bands.Length;
        float w = ctx.Width, h = ctx.Height;

        float baseline = ctx.Overlay ? h : h * 0.78f;
        float maxH = ctx.Overlay ? h * 0.94f : baseline - h * 0.08f;
        float margin = ctx.Overlay ? 0 : w * 0.04f;
        float slot = (w - margin * 2) / n;
        float barW = slot * 0.74f;
        float radius = MathF.Min(barW * 0.35f, 6f * ctx.Scale);
        float minH = 2f * ctx.Scale;

        EnsureShader(ctx.Palette, w, baseline - maxH, baseline);

        // Soft glow underneath (low-res layer, see RenderContext.GlowCanvas).
        if (ctx.GlowCanvas is { } glowCanvas)
        {
            for (int i = 0; i < n; i++)
            {
                float bh = MathF.Max(minH, frame.Bands[i] * maxH);
                float x = margin + i * slot + (slot - barW) / 2;
                glowCanvas.DrawRect(x, baseline - bh, barW, bh, _glow);
            }
        }

        for (int i = 0; i < n; i++)
        {
            float bh = MathF.Max(minH, frame.Bands[i] * maxH);
            float x = margin + i * slot + (slot - barW) / 2;
            canvas.DrawRoundRect(new SKRect(x, baseline - bh, x + barW, baseline + radius), radius, radius, _bar);

            // Peak cap
            float py = baseline - frame.Peaks[i] * maxH - 4f * ctx.Scale;
            canvas.DrawRect(x, py, barW, 2.5f * ctx.Scale, _cap);

            // Reflection below the floor
            if (!ctx.Overlay)
            {
                float rh = MathF.Min(bh * 0.35f, h - baseline);
                canvas.DrawRect(x, baseline + 2f * ctx.Scale, barW, rh, _reflect);
            }
        }
    }

    private void EnsureShader(Palette palette, float width, float top, float bottom)
    {
        var key = (palette, width, top, bottom);
        if (_shader != null && _shaderKey == key) return;
        _shader?.Dispose();
        // Hue runs left→right across the spectrum; the reflection reuses it.
        _shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, 0), palette.Colors, null, SKShaderTileMode.Clamp);
        _shaderKey = key;
        _bar.Shader = _shader;
        _glow.Shader = _shader;
        _reflect.Shader = _shader;
    }

}
