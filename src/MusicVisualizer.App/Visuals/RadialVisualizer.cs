using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>Spectrum spokes radiating from a bass-pulsing core, mirrored for symmetry and slowly rotating.</summary>
public sealed class RadialVisualizer : IVisualizer
{
    private readonly SKPaint _spoke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _spokeGlow = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, Color = SKColors.White.WithAlpha(140) };
    private readonly SKPaint _dot = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White.WithAlpha(210) };
    private readonly SKPaint _core = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _ring = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

    private float _rotation;

    public string Name => "Radial";

    public void Render(in RenderContext ctx)
    {
        var canvas = ctx.Canvas;
        var frame = ctx.Frame;
        int n = frame.Bands.Length;
        float cx = ctx.Width / 2, cy = ctx.Height / 2;
        float size = MathF.Min(ctx.Width, ctx.Height);

        _rotation += ctx.Dt * (0.12f + frame.Mid * 0.5f);

        float r0 = size * 0.17f * (1f + 0.14f * frame.Bass + 0.10f * frame.Pulse);
        float maxLen = size * 0.30f;
        int spokes = n * 2;
        float step = MathF.Tau / spokes;
        float strokeW = MathF.Max(1.5f * ctx.Scale, MathF.Tau * r0 / spokes * 0.55f);

        using var sweep = SKShader.CreateSweepGradient(new SKPoint(cx, cy), ctx.Palette.Wrapped, null);
        _spoke.Shader = sweep;
        _spoke.StrokeWidth = strokeW;
        _spokeGlow.Shader = sweep;
        _spokeGlow.StrokeWidth = strokeW * 1.6f;

        // Soft glow underneath (low-res layer, see RenderContext.GlowCanvas).
        if (ctx.GlowCanvas is { } glowCanvas)
        {
            glowCanvas.Save();
            glowCanvas.RotateRadians(_rotation, cx, cy);
            DrawSpokes(glowCanvas, _spokeGlow, frame.Bands, n, spokes, step, r0, strokeW, maxLen, cx, cy, ctx.Scale);
            glowCanvas.Restore();
        }

        canvas.Save();
        canvas.RotateRadians(_rotation, cx, cy);
        DrawSpokes(canvas, _spoke, frame.Bands, n, spokes, step, r0, strokeW, maxLen, cx, cy, ctx.Scale);

        // Peak dots
        float dotR = MathF.Max(1.2f * ctx.Scale, strokeW * 0.35f);
        for (int j = 0; j < spokes; j++)
        {
            int band = j < n ? j : spokes - 1 - j;
            if (frame.Peaks[band] < 0.02f) continue;
            float a = j * step - MathF.PI / 2;
            float d = r0 + strokeW * 2.2f + frame.Peaks[band] * maxLen;
            canvas.DrawCircle(cx + MathF.Cos(a) * d, cy + MathF.Sin(a) * d, dotR, _dot);
        }

        canvas.Restore();

        // Core: soft radial glow + ring that brightens on beats.
        var inner = ctx.Palette.Sample(0.15f);
        var outer = ctx.Palette.Sample(0.85f);
        using (var coreShader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r0,
                   [inner.WithAlpha((byte)(90 + 120 * frame.Pulse)), outer.WithAlpha(40), SKColors.Transparent],
                   [0f, 0.75f, 1f], SKShaderTileMode.Clamp))
        {
            _core.Shader = coreShader;
            canvas.DrawCircle(cx, cy, r0, _core);
            _core.Shader = null;
        }

        _ring.Color = SKColors.White.WithAlpha((byte)(70 + 170 * frame.Pulse));
        _ring.StrokeWidth = (1.5f + 3f * frame.Pulse) * ctx.Scale;
        canvas.DrawCircle(cx, cy, r0 * 0.92f, _ring);

        _spoke.Shader = null;
        _spokeGlow.Shader = null;
    }

    // Spoke j and its mirror share a band, so low frequencies meet at the top and bottom.
    private static void DrawSpokes(SKCanvas canvas, SKPaint paint, float[] bands, int n, int spokes, float step,
                                   float r0, float strokeW, float maxLen, float cx, float cy, float scale)
    {
        for (int j = 0; j < spokes; j++)
        {
            int band = j < n ? j : spokes - 1 - j;
            float len = MathF.Max(1.5f * scale, bands[band] * maxLen);
            float a = j * step - MathF.PI / 2;
            float cos = MathF.Cos(a), sin = MathF.Sin(a);
            float rs = r0 + strokeW;
            canvas.DrawLine(cx + cos * rs, cy + sin * rs, cx + cos * (rs + len), cy + sin * (rs + len), paint);
        }
    }

}
