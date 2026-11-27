using SkiaSharp;

namespace MusicVisualizer.Visuals.Space;

/// <summary>
/// The Space scene's static scenery, painted once per (palette, size): a deep-space gradient with a
/// palette-tinted nebula and fine star dust, and two planet sprites that drift slowly across.
/// </summary>
internal sealed class SpaceBackdrop : IDisposable
{
    public SKImage? Sky { get; private set; }
    public SKImage? BigPlanet { get; private set; }
    public SKImage? SmallPlanet { get; private set; }

    public void Build(Palette palette, int w, int h, float s, bool overlay)
    {
        Dispose();
        var rng = new Random(1234);
        float minDim = MathF.Min(w, h);
        bool strip = h < w * 0.35f;
        var cols = palette.Colors;

        // Planets (also used in overlay windows, more subtly — see the visualizer).
        float bigR = strip ? h * 0.32f : minDim * 0.17f;
        float smallR = strip ? h * 0.12f : minDim * 0.055f;
        BigPlanet = PaintPlanet(bigR, cols[cols.Length > 2 ? 2 : 1], cols[^1], false, rng);
        SmallPlanet = PaintPlanet(smallR, cols[1], cols[0], true, rng);

        if (overlay) return;

        // Opaque, so drawing it each frame is a straight copy rather than a blend.
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        var c = surface.Canvas;

        var top = ColorUtil.Mix(new SKColor(4, 5, 12), cols[^1], 0.07f);
        var bottom = ColorUtil.Mix(new SKColor(3, 4, 9), cols[0], 0.06f);
        using (var bg = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w * 0.3f, h), [top, bottom], (float[]?)null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = bg })
            c.DrawRect(0, 0, w, h, p);

        // Nebula: soft palette clouds strewn along a diagonal band, with darker dust lanes through it.
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        float bandA = (float)rng.NextDouble() * 0.3f + 0.15f;
        for (int i = 0; i < 34; i++)
        {
            float u = (float)rng.NextDouble();
            float x = u * w * 1.1f - w * 0.05f;
            float y = (strip ? 0.5f : bandA + u * 0.45f) * h + (float)(rng.NextDouble() - 0.5) * h * (strip ? 0.9f : 0.32f);
            float r = (strip ? h * 0.5f : minDim * 0.16f) * (0.5f + (float)rng.NextDouble() * 1.4f);
            var col = palette.Sample((float)rng.NextDouble());
            byte a = (byte)(10 + rng.Next(24));
            using var sh = SKShader.CreateRadialGradient(new SKPoint(x, y), r,
                [col.WithAlpha(a), col.WithAlpha((byte)(a / 3)), col.WithAlpha(0)], [0f, 0.5f, 1f], SKShaderTileMode.Clamp);
            paint.Shader = sh;
            c.DrawCircle(x, y, r, paint);
        }
        // A couple of brighter cores.
        for (int i = 0; i < 3; i++)
        {
            float u = 0.2f + 0.6f * (float)rng.NextDouble();
            float x = u * w, y = (strip ? 0.5f : bandA + u * 0.45f) * h;
            float r = (strip ? h * 0.35f : minDim * 0.1f) * (0.6f + (float)rng.NextDouble());
            var col = ColorUtil.Mix(palette.Sample((float)rng.NextDouble()), SKColors.White, 0.3f);
            using var sh = SKShader.CreateRadialGradient(new SKPoint(x, y), r,
                [col.WithAlpha(34), col.WithAlpha(0)], (float[]?)null, SKShaderTileMode.Clamp);
            paint.Shader = sh;
            c.DrawCircle(x, y, r, paint);
        }
        paint.BlendMode = SKBlendMode.SrcOver;
        for (int i = 0; i < 10; i++)
        {
            float u = (float)rng.NextDouble();
            float x = u * w, y = (strip ? 0.5f : bandA + u * 0.45f) * h + (float)(rng.NextDouble() - 0.5) * h * 0.2f;
            float r = (strip ? h * 0.3f : minDim * 0.08f) * (0.5f + (float)rng.NextDouble());
            var dark = new SKColor(2, 2, 6);
            using var sh = SKShader.CreateRadialGradient(new SKPoint(x, y), r,
                [dark.WithAlpha(110), dark.WithAlpha(0)], (float[]?)null, SKShaderTileMode.Clamp);
            paint.Shader = sh;
            c.DrawCircle(x, y, r, paint);
        }
        paint.Shader = null;

        // Star dust: many faint pinpricks, thicker inside the nebula band.
        int dust = (int)(w * (float)h / (strip ? 900f : 2600f));
        for (int i = 0; i < dust; i++)
        {
            float u = (float)rng.NextDouble();
            float x = u * w;
            float y = rng.NextDouble() < 0.4 && !strip
                ? (bandA + u * 0.45f) * h + (float)((rng.NextDouble() + rng.NextDouble() - 1) * h * 0.2)
                : (float)rng.NextDouble() * h;
            var tint = ColorUtil.Mix(SKColors.White, palette.Sample((float)rng.NextDouble()), 0.3f);
            paint.Color = tint.WithAlpha((byte)(30 + rng.Next(120)));
            c.DrawCircle(x, y, (0.35f + (float)rng.NextDouble() * 0.6f) * s, paint);
        }
        // A few bright stars with little cross flares.
        using var flare = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        for (int i = 0; i < (strip ? 6 : 9); i++)
        {
            float x = (float)rng.NextDouble() * w, y = (float)rng.NextDouble() * h;
            var tint = ColorUtil.Mix(SKColors.White, palette.Sample((float)rng.NextDouble()), 0.35f);
            float len = (4f + (float)rng.NextDouble() * 6f) * s;
            flare.StrokeWidth = 0.8f * s;
            flare.Color = tint.WithAlpha(110);
            c.DrawLine(x - len, y, x + len, y, flare);
            c.DrawLine(x, y - len, x, y + len, flare);
            paint.Color = tint.WithAlpha(200);
            c.DrawCircle(x, y, 1.3f * s, paint);
        }

        Sky = surface.Snapshot();
    }

    /// <summary>A shaded planet sprite, lit from the upper left, optionally ringed. The image is centred on the planet.</summary>
    private static SKImage PaintPlanet(float r, SKColor baseColor, SKColor bandColor, bool ring, Random rng)
    {
        r = MathF.Max(r, 4f);
        float margin = ring ? r * 1.25f : r * 0.18f;
        int size = (int)MathF.Ceiling(2 * (r + margin));
        float cx = size / 2f, cy = size / 2f;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        var surfaceCol = ColorUtil.Mix(ColorUtil.Darken(baseColor, 0.4f), new SKColor(0x50, 0x56, 0x68), 0.4f);
        var ringRect = new SKRect(cx - r * 2.1f, cy - r * 0.55f, cx + r * 2.1f, cy + r * 0.55f);
        const float RingTilt = -14f;

        void DrawRing(bool front)
        {
            c.Save();
            c.RotateDegrees(RingTilt, cx, cy);
            c.ClipRect(front ? new SKRect(0, cy, size, size) : new SKRect(0, 0, size, cy));
            var rc = ColorUtil.Mix(bandColor, SKColors.White, 0.35f);
            stroke.StrokeWidth = r * 0.16f;
            stroke.Color = rc.WithAlpha(120);
            c.DrawOval(ringRect, stroke);
            stroke.StrokeWidth = r * 0.06f;
            stroke.Color = rc.WithAlpha(150);
            c.DrawOval(new SKRect(ringRect.Left + r * 0.3f, ringRect.Top + r * 0.08f, ringRect.Right - r * 0.3f, ringRect.Bottom - r * 0.08f), stroke);
            c.Restore();
        }

        if (ring) DrawRing(false);

        // Atmosphere halo.
        using (var halo = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 1.16f,
                   [bandColor.WithAlpha(0), bandColor.WithAlpha(0), bandColor.WithAlpha(45), bandColor.WithAlpha(0)],
                   [0f, 0.8f, 0.87f, 1f], SKShaderTileMode.Clamp))
        {
            paint.Shader = halo;
            c.DrawCircle(cx, cy, r * 1.16f, paint);
        }

        c.Save();
        using (var clip = new SKPath())
        {
            clip.AddCircle(cx, cy, r);
            c.ClipPath(clip, antialias: true);
        }
        using (var sh = SKShader.CreateRadialGradient(new SKPoint(cx - r * 0.35f, cy - r * 0.35f), r * 1.5f,
                   [ColorUtil.Mix(surfaceCol, SKColors.White, 0.35f), surfaceCol, ColorUtil.Darken(surfaceCol, 0.5f)],
                   [0f, 0.45f, 1f], SKShaderTileMode.Clamp))
        {
            paint.Shader = sh;
            c.DrawCircle(cx, cy, r, paint);
        }
        paint.Shader = null;
        // Cloud bands.
        c.RotateDegrees(RingTilt, cx, cy);
        for (int i = 0; i < 7; i++)
        {
            float y = cy - r + (float)rng.NextDouble() * 2 * r;
            float bh = r * (0.05f + (float)rng.NextDouble() * 0.16f);
            paint.Color = (i % 2 == 0 ? ColorUtil.Mix(bandColor, surfaceCol, 0.4f) : ColorUtil.Darken(surfaceCol, 0.4f)).WithAlpha((byte)(25 + rng.Next(40)));
            c.DrawOval(new SKRect(cx - r * 1.3f, y - bh, cx + r * 1.3f, y + bh), paint);
        }
        c.RotateDegrees(-RingTilt, cx, cy);
        // Night side.
        using (var sh = SKShader.CreateRadialGradient(new SKPoint(cx - r * 0.45f, cy - r * 0.45f), r * 1.75f,
                   [new SKColor(2, 2, 8, 0), new SKColor(2, 2, 8, 0), new SKColor(2, 2, 8, 190), new SKColor(2, 2, 8, 245)],
                   [0f, 0.32f, 0.72f, 1f], SKShaderTileMode.Clamp))
        {
            paint.Shader = sh;
            c.DrawCircle(cx, cy, r, paint);
        }
        paint.Shader = null;
        c.Restore();

        // Lit rim.
        stroke.StrokeWidth = MathF.Max(1f, r * 0.03f);
        stroke.Color = ColorUtil.Mix(bandColor, SKColors.White, 0.4f).WithAlpha(150);
        c.DrawArc(new SKRect(cx - r, cy - r, cx + r, cy + r), 160, 120, false, stroke);

        if (ring) DrawRing(true);
        return surface.Snapshot();
    }

    public void Dispose()
    {
        Sky?.Dispose(); Sky = null;
        BigPlanet?.Dispose(); BigPlanet = null;
        SmallPlanet?.Dispose(); SmallPlanet = null;
    }
}
