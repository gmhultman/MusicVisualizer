using SkiaSharp;

namespace MusicVisualizer.Visuals.Aquarium;

/// <summary>Scene colours for the aquarium, tinted by the active palette (water stays recognisably water).</summary>
internal sealed class AquariumTheme
{
    public SKColor WaterTop, WaterMid, WaterBottom;
    public SKColor Sand, SandDark, Rock, RockLight, Far, Light, Plankton;
    public SKColor[] Plants = [];
    public SKColor[] Coral = [];
    public SKColor Wood, WoodDark, Gold;

    public static AquariumTheme For(Palette p)
    {
        var avg = Average(p.Colors);
        // Tint the water with the palette's coolest colour: keeps it watery (an average of warm and cool
        // colours turns it a murky grey).
        var cool = p.Colors[0];
        foreach (var c in p.Colors)
            if (c.Blue - c.Red > cool.Blue - cool.Red) cool = c;
        float tint = cool.Blue >= cool.Red ? 0.3f : 0.18f;
        var t = new AquariumTheme
        {
            WaterTop = ColorUtil.Mix(new SKColor(0x1A, 0x6C, 0x94), cool, tint),
            WaterMid = ColorUtil.Mix(new SKColor(0x08, 0x30, 0x52), cool, tint * 0.75f),
            WaterBottom = ColorUtil.Mix(new SKColor(0x02, 0x0C, 0x1C), cool, tint * 0.4f),
            Sand = ColorUtil.Darken(ColorUtil.Mix(new SKColor(0xD8, 0xC0, 0x90), avg, 0.12f), 0.28f),
            Rock = ColorUtil.Mix(new SKColor(0x2C, 0x34, 0x40), avg, 0.12f),
            Light = ColorUtil.Mix(new SKColor(0xD8, 0xF6, 0xFF), p.Colors[0], 0.22f),
            Wood = new SKColor(0x6A, 0x3E, 0x22),
            WoodDark = new SKColor(0x3A, 0x20, 0x12),
            Gold = new SKColor(0xFF, 0xC8, 0x48),
        };
        t.SandDark = ColorUtil.Darken(t.Sand, 0.45f);
        t.RockLight = ColorUtil.Mix(t.Rock, new SKColor(0x8C, 0xA8, 0xB8), 0.35f);
        t.Far = ColorUtil.Mix(t.WaterMid, t.WaterBottom, 0.45f);
        t.Plankton = ColorUtil.Mix(SKColors.White, p.Colors[^1], 0.35f);

        // Mostly greens (lightly tinted) plus one seaweed in a deep palette colour.
        t.Plants =
        [
            ColorUtil.Mix(new SKColor(0x2E, 0xA0, 0x5C), p.Sample(0.1f), 0.15f),
            ColorUtil.Mix(new SKColor(0x6A, 0x9A, 0x2A), p.Sample(0.4f), 0.12f),
            ColorUtil.Mix(new SKColor(0x1C, 0x8C, 0x7C), p.Sample(0.7f), 0.15f),
            ColorUtil.Mix(ColorUtil.Darken(p.Sample(0.85f), 0.3f), new SKColor(0x8A, 0x2A, 0x4A), 0.2f),
        ];
        t.Coral = [p.Sample(0.9f), p.Sample(0.55f), p.Sample(0.2f)];
        for (int i = 0; i < t.Coral.Length; i++) t.Coral[i] = ColorUtil.Mix(t.Coral[i], new SKColor(0xFF, 0x7A, 0x6A), 0.25f);
        return t;
    }

    private static SKColor Average(SKColor[] cs)
    {
        int r = 0, g = 0, b = 0;
        foreach (var c in cs) { r += c.Red; g += c.Green; b += c.Blue; }
        return new SKColor((byte)(r / cs.Length), (byte)(g / cs.Length), (byte)(b / cs.Length));
    }
}

/// <summary>Paints the static parts of the aquarium (water, distant reef, sand, rocks, coral) into cached images.</summary>
internal static class AquariumPainter
{
    /// <summary>Height of the sand surface at <paramref name="x"/> (dunes), as an offset from floorY.</summary>
    public static float SandOffset(float x, float w, float floorH) =>
        floorH * (0.12f * MathF.Sin(x / w * 7.3f + 0.6f) + 0.07f * MathF.Sin(x / w * 17.1f + 2.1f));

    /// <summary>Full opaque background: water gradient, light haze, distant reef, floor, vignette.</summary>
    public static SKImage PaintBackground(AquariumTheme t, int w, int h, float floorY, float floorH, float u, float chestX, bool strip)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        using var paint = new SKPaint { IsAntialias = true };

        // Water: bright near the surface, deep below.
        using (var water = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h),
                   [t.WaterTop, t.WaterMid, t.WaterBottom], [0f, 0.45f, 1f], SKShaderTileMode.Clamp))
        {
            paint.Shader = water;
            c.DrawRect(0, 0, w, h, paint);
        }
        // Soft brightening where the light enters, upper middle.
        using (var haze = SKShader.CreateRadialGradient(new SKPoint(w * 0.45f, -h * 0.15f), MathF.Max(w, h) * 0.75f,
                   [t.Light.WithAlpha(70), t.Light.WithAlpha(0)], SKShaderTileMode.Clamp))
        {
            paint.Shader = haze;
            c.DrawRect(0, 0, w, h, paint);
        }
        // Surface shimmer band at the very top.
        using (var surf = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h * 0.06f),
                   [t.Light.WithAlpha(60), t.Light.WithAlpha(0)], (float[]?)null, SKShaderTileMode.Clamp))
        {
            paint.Shader = surf;
            c.DrawRect(0, 0, w, h * 0.06f, paint);
        }
        paint.Shader = null;

        var rng = new Random(11);
        if (!strip) PaintDistantReef(c, paint, t, w, h, floorY, floorH, u, rng);
        PaintFloor(c, paint, t, w, h, floorY, floorH, u, chestX, strip, rng, opaque: true);

        // Vignette.
        using (var vig = SKShader.CreateRadialGradient(new SKPoint(w / 2f, h * 0.45f), MathF.Max(w, h) * 0.75f,
                   [SKColors.Transparent, new SKColor(0, 0, 0, 30), new SKColor(0, 0, 0, 120)], [0f, 0.6f, 1f], SKShaderTileMode.Clamp))
        {
            paint.Shader = vig;
            c.DrawRect(0, 0, w, h, paint);
            paint.Shader = null;
        }
        return surface.Snapshot();
    }

    /// <summary>Overlay mode: only the floor, on a transparent image covering [top, h).</summary>
    public static SKImage PaintOverlayFloor(AquariumTheme t, int w, int h, float floorY, float floorH, float u, float chestX, bool strip, out float top)
    {
        top = MathF.Floor(floorY - floorH * 1.6f - u * 0.06f);
        int ih = Math.Max(1, (int)(h - top));
        using var surface = SKSurface.Create(new SKImageInfo(w, ih, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.Translate(0, -top);
        using var paint = new SKPaint { IsAntialias = true };
        PaintFloor(c, paint, t, w, h, floorY, floorH, u, chestX, strip, new Random(11), opaque: false);
        return surface.Snapshot();
    }

    private static void PaintDistantReef(SKCanvas c, SKPaint paint, AquariumTheme t, int w, int h, float floorY, float floorH, float u, Random rng)
    {
        // Two layers of hazy reef ridges, fading into the water.
        for (int layer = 0; layer < 2; layer++)
        {
            float baseY = floorY - floorH * (0.2f + 0.25f * layer);
            float peak = h * (0.16f - 0.05f * layer);
            using var path = new SKPath();
            path.MoveTo(0, h);
            float x = 0;
            float y = baseY - peak * 0.4f;
            path.LineTo(0, y);
            while (x < w)
            {
                float step = u * (0.05f + 0.08f * (float)rng.NextDouble());
                float ny = baseY - peak * (0.15f + 0.85f * (float)Math.Pow(rng.NextDouble(), 1.6));
                path.QuadTo(x + step * 0.5f, MathF.Min(y, ny) - peak * 0.08f, x + step, ny);
                x += step;
                y = ny;
            }
            path.LineTo(w, h);
            path.Close();
            var col = ColorUtil.Mix(t.Far, t.WaterMid, layer == 0 ? 0.55f : 0.15f);
            using var sh = SKShader.CreateLinearGradient(new SKPoint(0, baseY - peak), new SKPoint(0, floorY),
                [col.WithAlpha(150), col], (float[]?)null, SKShaderTileMode.Clamp);
            paint.Shader = sh;
            c.DrawPath(path, paint);
            paint.Shader = null;
        }

        // Faint distant kelp silhouettes.
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        for (int i = 0; i < 18; i++)
        {
            float x = (float)rng.NextDouble() * w;
            float len = h * (0.12f + 0.22f * (float)rng.NextDouble());
            float y0 = floorY - floorH * 0.3f;
            using var p = new SKPath();
            p.MoveTo(x, y0);
            float ph = (float)rng.NextDouble() * 6;
            for (int k = 1; k <= 8; k++)
            {
                float f = k / 8f;
                p.LineTo(x + MathF.Sin(ph + f * 3f) * u * 0.02f * f, y0 - len * f);
            }
            paint.StrokeWidth = u * (0.006f + 0.006f * (float)rng.NextDouble());
            paint.Color = ColorUtil.Mix(t.Far, t.Plants[i % t.Plants.Length], 0.18f).WithAlpha(160);
            c.DrawPath(p, paint);
        }
        paint.Style = SKPaintStyle.Fill;
    }

    private static void PaintFloor(SKCanvas c, SKPaint paint, AquariumTheme t, int w, int h, float floorY, float floorH, float u,
                                   float chestX, bool strip, Random rng, bool opaque)
    {
        // Rocks behind the sand line (they sink into it).
        int rocks = strip ? 4 : 5;
        for (int i = 0; i < rocks; i++)
        {
            float x = w * (i + 0.2f + 0.6f * (float)rng.NextDouble()) / rocks;
            if (MathF.Abs(x - chestX) < u * 0.12f) x += u * 0.2f;
            float r = u * (0.035f + 0.04f * (float)rng.NextDouble());
            if (strip) r = MathF.Min(r, floorH * 1.5f);
            float y = floorY + SandOffset(x, w, floorH) + r * 0.15f;
            PaintRock(c, paint, t, x, y, r * (1.3f + 0.6f * (float)rng.NextDouble()), r, rng);
        }

        // Coral clusters.
        int corals = strip ? 3 : 4;
        for (int i = 0; i < corals; i++)
        {
            float x = w * (i + 0.5f + 0.4f * ((float)rng.NextDouble() - 0.5f)) / corals;
            if (MathF.Abs(x - chestX) < u * 0.1f) x -= u * 0.16f;
            float y = floorY + SandOffset(x, w, floorH) + floorH * 0.05f;
            float size = strip ? floorH * 1.6f : u * (0.07f + 0.05f * (float)rng.NextDouble());
            var col = t.Coral[i % t.Coral.Length];
            if (i % 2 == 0) PaintBranchCoral(c, paint, col, x, y, size, rng);
            else PaintTubeSponge(c, paint, col, x, y, size, rng);
        }

        // Sand with gentle dunes.
        using (var sand = new SKPath())
        {
            sand.MoveTo(-2, h + 2);
            for (float x = -2; x <= w + 8; x += 8)
                sand.LineTo(x, floorY + SandOffset(x, w, floorH));
            sand.LineTo(w + 8, h + 2);
            sand.Close();
            using var sh = SKShader.CreateLinearGradient(new SKPoint(0, floorY - floorH * 0.2f), new SKPoint(0, h),
                [ColorUtil.Mix(t.Sand, SKColors.White, 0.08f), t.Sand, t.SandDark], [0f, 0.35f, 1f], SKShaderTileMode.Clamp);
            paint.Shader = sh;
            c.DrawPath(sand, paint);
            paint.Shader = null;
        }

        // Ripples and pebbles.
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        int ripples = (int)(w / (u * 0.03f));
        for (int i = 0; i < ripples; i++)
        {
            float x = (float)rng.NextDouble() * w;
            float top = floorY + SandOffset(x, w, floorH);
            float y = top + (h - top) * (0.15f + 0.8f * (float)rng.NextDouble());
            float len = u * (0.02f + 0.04f * (float)rng.NextDouble());
            using var p = new SKPath();
            p.MoveTo(x - len, y);
            p.QuadTo(x, y - len * 0.18f, x + len, y);
            paint.StrokeWidth = MathF.Max(1f, u * 0.0022f);
            paint.Color = (i % 3 == 0 ? ColorUtil.Mix(t.Sand, SKColors.White, 0.2f) : t.SandDark).WithAlpha(110);
            c.DrawPath(p, paint);
        }
        paint.Style = SKPaintStyle.Fill;
        int pebbles = (int)(w / (u * 0.02f));
        for (int i = 0; i < pebbles; i++)
        {
            float x = (float)rng.NextDouble() * w;
            float top = floorY + SandOffset(x, w, floorH);
            float y = top + (h - top) * (0.1f + 0.9f * (float)rng.NextDouble());
            float r = u * (0.002f + 0.005f * (float)rng.NextDouble());
            var col = ColorUtil.Mix(t.Rock, t.Sand, (float)rng.NextDouble());
            paint.Color = col;
            c.DrawOval(x, y, r * 1.4f, r, paint);
            paint.Color = ColorUtil.Mix(col, SKColors.White, 0.25f).WithAlpha(150);
            c.DrawOval(x - r * 0.3f, y - r * 0.35f, r * 0.6f, r * 0.35f, paint);
        }
    }

    private static void PaintRock(SKCanvas c, SKPaint paint, AquariumTheme t, float cx, float cy, float rx, float ry, Random rng)
    {
        using var path = new SKPath();
        const int n = 11;
        var pts = new SKPoint[n];
        for (int i = 0; i < n; i++)
        {
            float a = MathF.PI + MathF.PI * i / (n - 1);      // upper half, left to right
            float k = 0.82f + 0.3f * (float)rng.NextDouble();
            pts[i] = new SKPoint(cx + MathF.Cos(a) * rx * k, cy + MathF.Sin(a) * ry * k * 1.35f);
        }
        path.MoveTo(cx - rx, cy + ry * 0.3f);
        path.LineTo(pts[0]);
        for (int i = 1; i < n; i++)
        {
            var m = new SKPoint((pts[i - 1].X + pts[i].X) / 2, (pts[i - 1].Y + pts[i].Y) / 2);
            path.QuadTo(pts[i - 1], m);
        }
        path.LineTo(pts[n - 1]);
        path.LineTo(cx + rx, cy + ry * 0.3f);
        path.Close();
        using var sh = SKShader.CreateLinearGradient(new SKPoint(cx - rx * 0.3f, cy - ry * 1.3f), new SKPoint(cx + rx * 0.2f, cy + ry * 0.3f),
            [t.RockLight, t.Rock, ColorUtil.Darken(t.Rock, 0.5f)], [0f, 0.45f, 1f], SKShaderTileMode.Clamp);
        paint.Shader = sh;
        c.DrawPath(path, paint);
        paint.Shader = null;
        // A few mossy/algae speckles.
        for (int i = 0; i < 6; i++)
        {
            float a = MathF.PI * (1.1f + 0.8f * (float)rng.NextDouble());
            float rr = 0.5f + 0.4f * (float)rng.NextDouble();
            paint.Color = ColorUtil.Mix(t.Plants[i % t.Plants.Length], t.Rock, 0.4f).WithAlpha(150);
            c.DrawOval(cx + MathF.Cos(a) * rx * rr, cy + MathF.Sin(a) * ry * rr, rx * 0.12f, ry * 0.07f, paint);
        }
    }

    private static void PaintBranchCoral(SKCanvas c, SKPaint paint, SKColor col, float x, float y, float size, Random rng)
    {
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        var dark = ColorUtil.Darken(col, 0.35f);
        void Branch(float bx, float by, float angle, float len, float width, int depth, bool shade)
        {
            float ex = bx + MathF.Cos(angle) * len, ey = by + MathF.Sin(angle) * len;
            paint.StrokeWidth = width;
            paint.Color = shade ? dark : col;
            c.DrawLine(bx, by, ex, ey, paint);
            if (depth == 0)
            {
                if (!shade)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = ColorUtil.Mix(col, SKColors.White, 0.4f);
                    c.DrawCircle(ex, ey, width * 0.55f, paint);
                    paint.Style = SKPaintStyle.Stroke;
                }
                return;
            }
            int kids = 2;
            for (int k = 0; k < kids; k++)
            {
                float da = (k == 0 ? -1 : 1) * (0.3f + 0.35f * (float)rng.NextDouble());
                Branch(ex, ey, angle + da, len * (0.68f + 0.15f * (float)rng.NextDouble()), width * 0.75f, depth - 1, shade);
            }
        }
        int seed = rng.Next();
        // Draw a darker offset copy first for depth, then the lit coral (same random sequence).
        var saved = rng;
        rng = new Random(seed);
        Branch(x + size * 0.04f, y, -MathF.PI / 2, size * 0.38f, size * 0.12f, 3, true);
        rng = new Random(seed);
        Branch(x, y, -MathF.PI / 2, size * 0.38f, size * 0.1f, 3, false);
        rng = saved;
        paint.Style = SKPaintStyle.Fill;
    }

    private static void PaintTubeSponge(SKCanvas c, SKPaint paint, SKColor col, float x, float y, float size, Random rng)
    {
        int tubes = 3 + rng.Next(2);
        for (int i = 0; i < tubes; i++)
        {
            float tw = size * (0.14f + 0.06f * (float)rng.NextDouble());
            float th = size * (0.45f + 0.5f * (float)rng.NextDouble());
            float tx = x + (i - (tubes - 1) / 2f) * tw * 1.05f;
            var shade = ColorUtil.Mix(col, new SKColor(0x40, 0x20, 0x60), 0.25f + 0.1f * i);
            using var sh = SKShader.CreateLinearGradient(new SKPoint(tx - tw / 2, 0), new SKPoint(tx + tw / 2, 0),
                [ColorUtil.Darken(shade, 0.3f), ColorUtil.Mix(shade, SKColors.White, 0.15f), ColorUtil.Darken(shade, 0.45f)], [0f, 0.4f, 1f], SKShaderTileMode.Clamp);
            paint.Shader = sh;
            c.DrawRoundRect(new SKRect(tx - tw / 2, y - th, tx + tw / 2, y + size * 0.05f), tw * 0.35f, tw * 0.35f, paint);
            paint.Shader = null;
            paint.Color = ColorUtil.Darken(shade, 0.7f);
            c.DrawOval(tx, y - th + tw * 0.12f, tw * 0.36f, tw * 0.12f, paint);
        }
    }
}
