using SkiaSharp;

namespace MusicVisualizer.Visuals;

internal enum FarKind { Skyline, Mountains, SnowMountains, Mesas, Hills }

internal enum NearKind { Lamps, Palms, Pines, SnowPines, Cacti, Trees, Houses }

/// <summary>Scenery for the Highway visualizer. Chosen from the active palette, so cycling colours changes the scene.</summary>
internal sealed class HighwayTheme
{
    public required string Name { get; init; }
    public required SKColor[] Sky { get; init; }          // top → horizon
    public bool Night { get; init; }                       // headlight beam, lit windows
    public bool Stars { get; init; }
    public bool Aurora { get; init; }
    public bool Moon { get; init; }
    public bool Clouds { get; init; }
    public bool Rainbow { get; init; }
    public bool SynthSun { get; init; }                    // striped retro sun in palette colours
    public float SunSize { get; init; }                    // fraction of height; 0 = no sun
    public float SunX { get; init; } = 0.7f;
    public SKColor SunTop { get; init; }
    public SKColor SunBottom { get; init; }
    public FarKind Far { get; init; }
    public SKColor FarColor { get; init; }
    public SKColor FarColor2 { get; init; }                // second tone (snow caps, back hills, windows)
    public NearKind Near { get; init; }
    public SKColor NearColor { get; init; }
    public SKColor MidGround { get; init; }
    public SKColor GroundTop { get; init; }
    public SKColor GroundBottom { get; init; }
    public SKColor Road { get; init; }
    public SKColor RoadEdge { get; init; }
    public bool EdgeGlow { get; init; }
    public SKColor CenterLine { get; init; }
    public bool Grid { get; init; }
    public bool Snow { get; init; }

    public static HighwayTheme For(Palette palette) => palette.Name switch
    {
        "Sunset" => new()
        {
            Name = "Sunset",
            Sky = [new(0x2B, 0x10, 0x55), new(0x8E, 0x2D, 0x6E), new(0xFF, 0x7E, 0x5F), new(0xFE, 0xB4, 0x7B)],
            SunSize = 0.16f, SunX = 0.62f, SunTop = new(0xFF, 0xE0, 0x8A), SunBottom = new(0xFF, 0x6B, 0x4A),
            Far = FarKind.Mountains, FarColor = new(0x5A, 0x2A, 0x6E), FarColor2 = new(0x7A, 0x3A, 0x7E),
            Near = NearKind.Palms, NearColor = new(0x1E, 0x0E, 0x26), MidGround = new(0x2A, 0x14, 0x30),
            GroundTop = new(0x3A, 0x1C, 0x2E), GroundBottom = new(0x14, 0x08, 0x14),
            Road = new(0x26, 0x22, 0x30), RoadEdge = new(0xFF, 0xC2, 0x8A), CenterLine = new(0xFF, 0xD8, 0x6A),
        },
        "Aurora" => new()
        {
            Name = "Aurora", Night = true, Stars = true, Aurora = true,
            Sky = [new(0x01, 0x04, 0x0C), new(0x04, 0x12, 0x22), new(0x0B, 0x24, 0x36)],
            Far = FarKind.Mountains, FarColor = new(0x0A, 0x1C, 0x2C), FarColor2 = new(0x12, 0x2A, 0x3C),
            Near = NearKind.Pines, NearColor = new(0x04, 0x10, 0x0E), MidGround = new(0x06, 0x14, 0x12),
            GroundTop = new(0x0A, 0x1C, 0x18), GroundBottom = new(0x02, 0x08, 0x08),
            Road = new(0x16, 0x1A, 0x20), RoadEdge = new(0x6A, 0xFF, 0xC8), CenterLine = new(0xE8, 0xF0, 0xF0),
        },
        "Fire" => new()
        {
            Name = "Fire",
            Sky = [new(0x22, 0x06, 0x06), new(0x7A, 0x1E, 0x0A), new(0xE0, 0x62, 0x1E), new(0xFF, 0xB8, 0x5A)],
            SunSize = 0.2f, SunX = 0.68f, SunTop = new(0xFF, 0x9A, 0x3A), SunBottom = new(0xE8, 0x2A, 0x10),
            Far = FarKind.Mesas, FarColor = new(0x6E, 0x22, 0x14), FarColor2 = new(0x8E, 0x34, 0x1A),
            Near = NearKind.Cacti, NearColor = new(0x2A, 0x10, 0x0A), MidGround = new(0x8A, 0x42, 0x20),
            GroundTop = new(0xB8, 0x62, 0x30), GroundBottom = new(0x5A, 0x24, 0x10),
            Road = new(0x2E, 0x22, 0x1E), RoadEdge = new(0xF2, 0xC0, 0x8A), CenterLine = new(0xFF, 0xD0, 0x50),
        },
        "Ice" or "Christmas" or "Candy Cane" => new()
        {
            Name = palette.Name, Night = true, Stars = true, Moon = true, Snow = true,
            Sky = [new(0x03, 0x07, 0x18), new(0x0E, 0x1E, 0x44), new(0x24, 0x3E, 0x70)],
            Far = FarKind.SnowMountains, FarColor = new(0x2A, 0x3C, 0x62), FarColor2 = new(0xDC, 0xE8, 0xF8),
            Near = palette.Name == "Ice" ? NearKind.SnowPines : NearKind.Houses,
            NearColor = new(0x0C, 0x1E, 0x1C), MidGround = new(0xB8, 0xC8, 0xE0),
            GroundTop = new(0xE6, 0xEE, 0xFA), GroundBottom = new(0x8C, 0x9C, 0xBC),
            Road = new(0x2A, 0x2E, 0x3A), RoadEdge = new(0xF4, 0xF8, 0xFF), CenterLine = new(0xFF, 0xD6, 0x5A),
        },
        "Rainbow" => new()
        {
            Name = "Rainbow", Clouds = true, Rainbow = true,
            Sky = [new(0x3A, 0x9C, 0xF0), new(0x7C, 0xC4, 0xFF), new(0xC8, 0xEA, 0xFF)],
            SunSize = 0.09f, SunX = 0.82f, SunTop = new(0xFF, 0xF6, 0xB0), SunBottom = new(0xFF, 0xD8, 0x40),
            Far = FarKind.Hills, FarColor = new(0x4C, 0xA8, 0x5A), FarColor2 = new(0x7C, 0xC8, 0x7A),
            Near = NearKind.Trees, NearColor = new(0x2C, 0x8A, 0x3E), MidGround = new(0x5C, 0xB8, 0x4C),
            GroundTop = new(0x6C, 0xC4, 0x50), GroundBottom = new(0x3A, 0x84, 0x2A),
            Road = new(0x48, 0x4A, 0x52), RoadEdge = new(0xF4, 0xF4, 0xF4), CenterLine = new(0xFF, 0xFF, 0xFF),
        },
        // Neon and anything unknown: synthwave night drive in the palette's colours.
        _ => new()
        {
            Name = "Neon", Night = true, Stars = true, SynthSun = true, Grid = true, EdgeGlow = true,
            Sky = [new(0x06, 0x02, 0x14), new(0x1A, 0x06, 0x36), new(0x40, 0x0C, 0x5A)],
            SunSize = 0.2f, SunX = 0.62f, SunTop = palette.Sample(0f), SunBottom = palette.Sample(1f),
            Far = FarKind.Skyline, FarColor = new(0x0C, 0x06, 0x1C), FarColor2 = palette.Sample(0.5f),
            Near = NearKind.Lamps, NearColor = new(0x10, 0x0A, 0x22), MidGround = new(0x0A, 0x04, 0x16),
            GroundTop = new(0x0E, 0x04, 0x1E), GroundBottom = new(0x04, 0x01, 0x0A),
            Road = new(0x0C, 0x0A, 0x16), RoadEdge = palette.Sample(1f), CenterLine = palette.Sample(0.3f),
        },
    };
}

/// <summary>
/// Paints the static scenery once per (theme, size): the sky, and two seamlessly-wrapping parallax strips.
/// The visualizer scrolls these images instead of redrawing the shapes every frame.
/// </summary>
internal static class HighwayPainter
{
    public static SKImage PaintSky(HighwayTheme t, int w, int h, float horizonY)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        using var paint = new SKPaint { IsAntialias = true };

        using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, horizonY), t.Sky, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = sky;
            c.DrawRect(0, 0, w, h, paint);
            paint.Shader = null;
        }

        if (t.Rainbow)
        {
            float cx = w * 0.32f, cy = horizonY + h * 0.1f, r = h * 0.55f, band = h * 0.022f;
            SKColor[] bow = [new(0xFF, 0x3B, 0x3B), new(0xFF, 0xA6, 0x3B), new(0xF5, 0xF5, 0x3B), new(0x3B, 0xD8, 0x6B), new(0x3B, 0x9C, 0xFF), new(0x8A, 0x3B, 0xFF)];
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = band;
            for (int i = 0; i < bow.Length; i++)
            {
                paint.Color = bow[i].WithAlpha(90);
                float rr = r - i * band;
                c.DrawArc(new SKRect(cx - rr, cy - rr, cx + rr, cy + rr), 180, 180, false, paint);
            }
            paint.Style = SKPaintStyle.Fill;
        }

        if (t.Moon)
        {
            float mx = w * 0.78f, my = horizonY * 0.32f, mr = h * 0.045f;
            paint.Color = new SKColor(0xF4, 0xF2, 0xE4);
            c.DrawCircle(mx, my, mr, paint);
            paint.Color = new SKColor(0xD8, 0xD6, 0xC8);
            c.DrawCircle(mx - mr * 0.3f, my - mr * 0.2f, mr * 0.18f, paint);
            c.DrawCircle(mx + mr * 0.35f, my + mr * 0.3f, mr * 0.12f, paint);
            c.DrawCircle(mx + mr * 0.1f, my - mr * 0.45f, mr * 0.09f, paint);
        }

        if (t.Clouds)
        {
            var rng = new Random(5);
            paint.Color = SKColors.White.WithAlpha(215);
            for (int k = 0; k < 6; k++)
            {
                float x = (float)rng.NextDouble() * w, y = horizonY * (0.15f + 0.45f * (float)rng.NextDouble());
                float s = h * (0.03f + 0.03f * (float)rng.NextDouble());
                c.DrawOval(x, y, s * 2.6f, s * 0.9f, paint);
                c.DrawCircle(x - s * 0.9f, y - s * 0.3f, s, paint);
                c.DrawCircle(x + s * 0.5f, y - s * 0.6f, s * 1.25f, paint);
            }
        }
        return surface.Snapshot();
    }

    /// <summary>Distant silhouettes in a strip <paramref name="w"/> wide that tiles seamlessly.</summary>
    public static SKImage PaintFar(HighwayTheme t, int w, float top, float baseY, float bottom, float scale)
    {
        int stripH = Math.Max(1, (int)MathF.Ceiling(bottom - top));
        using var surface = SKSurface.Create(new SKImageInfo(w, stripH, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.Translate(0, -top);
        using var paint = new SKPaint { IsAntialias = true, Color = t.FarColor };
        using var path = new SKPath();
        float H = baseY - top;
        var rng = new Random(17);

        switch (t.Far)
        {
            case FarKind.Mountains or FarKind.SnowMountains or FarKind.Hills:
            {
                bool hills = t.Far == FarKind.Hills;
                // Back ridge (second tone), then front ridge. Integer frequencies over the width → seamless wrap.
                for (int layer = 0; layer < 2; layer++)
                {
                    float a = (float)rng.NextDouble() * 6, b = (float)rng.NextDouble() * 6, d = (float)rng.NextDouble() * 6;
                    float amp = layer == 0 ? 1f : 0.75f;
                    path.Reset();
                    path.MoveTo(0, bottom);
                    for (float x = 0; x <= w; x += 4)
                    {
                        float u = x / w * MathF.Tau;
                        float v = hills
                            ? 0.45f + 0.25f * MathF.Sin(u * 2 + a) + 0.12f * MathF.Sin(u * 5 + b)
                            : 0.5f + 0.25f * MathF.Sin(u * 2 + a) + 0.15f * MathF.Sin(u * 5 + b) + 0.07f * MathF.Sin(u * 13 + d);
                        path.LineTo(x, baseY - H * amp * Math.Clamp(v, 0.05f, 1f));
                    }
                    path.LineTo(w, bottom);
                    path.Close();
                    paint.Color = layer == 0 ? t.FarColor2 : t.FarColor;
                    if (t.Far == FarKind.SnowMountains) paint.Color = layer == 0 ? ColorUtil.Mix(t.FarColor, SKColors.White, 0.2f) : t.FarColor;
                    c.DrawPath(path, paint);

                    if (t.Far == FarKind.SnowMountains)
                    {
                        // Snow caps: the ridge, clipped to the upper part.
                        c.Save();
                        c.ClipPath(path, antialias: true);
                        paint.Color = t.FarColor2.WithAlpha((byte)(layer == 0 ? 170 : 235));
                        c.DrawRect(0, top, w, H * (layer == 0 ? 0.42f : 0.34f), paint);
                        c.Restore();
                    }
                }
                break;
            }
            case FarKind.Mesas:
            {
                float x = -40 * scale;
                while (x < w)
                {
                    float mw = (90 + 220 * (float)rng.NextDouble()) * scale;
                    float mh = H * (0.35f + 0.6f * (float)rng.NextDouble());
                    var tone = rng.Next(2) == 0 ? t.FarColor : t.FarColor2;
                    foreach (float ox in new[] { 0f, -w, w })
                    {
                        path.Reset();
                        float l = x + ox, slope = mh * 0.35f;
                        path.MoveTo(l, bottom);
                        path.LineTo(l + slope, baseY - mh);
                        path.LineTo(l + mw - slope, baseY - mh);
                        path.LineTo(l + mw, bottom);
                        path.Close();
                        paint.Color = tone;
                        c.DrawPath(path, paint);
                    }
                    x += mw * (0.6f + 0.9f * (float)rng.NextDouble());
                }
                paint.Color = t.FarColor;
                c.DrawRect(0, baseY - 2, w, bottom - baseY + 2, paint);
                break;
            }
            case FarKind.Skyline:
            {
                float x = 0;
                var windowPaint = new SKPaint { IsAntialias = false };
                while (x < w)
                {
                    float bw = (34 + 70 * (float)rng.NextDouble()) * scale;
                    float bh = H * (0.25f + 0.75f * (float)rng.NextDouble());
                    int seed = rng.Next();
                    foreach (float ox in new[] { 0f, -w, w })
                    {
                        float l = x + ox;
                        if (l > w || l + bw < 0) continue;
                        paint.Color = t.FarColor;
                        c.DrawRect(l, baseY - bh, bw, bottom - (baseY - bh), paint);
                        // Lit windows in the palette's middle colour
                        var wr = new Random(seed);
                        float cell = 7f * scale;
                        for (float wy = baseY - bh + cell; wy < baseY - cell; wy += cell * 1.6f)
                            for (float wx = l + cell * 0.6f; wx < l + bw - cell; wx += cell * 1.4f)
                                if (wr.NextDouble() < 0.33)
                                {
                                    windowPaint.Color = t.FarColor2.WithAlpha((byte)(90 + wr.Next(120)));
                                    c.DrawRect(wx, wy, cell * 0.6f, cell * 0.7f, windowPaint);
                                }
                    }
                    x += bw + (2 + 10 * (float)rng.NextDouble()) * scale;
                }
                windowPaint.Dispose();
                break;
            }
        }
        return surface.Snapshot();
    }

    /// <summary>Roadside props standing on <paramref name="baseY"/>, in a seamlessly tiling strip. Also returns
    /// the positions of lights (lamp heads, lit windows) in strip coordinates so they can glow each frame.</summary>
    public static SKImage PaintNear(HighwayTheme t, int w, float top, float baseY, float bottom, float propH, float scale, List<SKPoint> lights)
    {
        int stripH = Math.Max(1, (int)MathF.Ceiling(bottom - top));
        using var surface = SKSurface.Create(new SKImageInfo(w, stripH, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.Translate(0, -top);
        using var paint = new SKPaint { IsAntialias = true };
        using var path = new SKPath();
        var rng = new Random(29);

        // Ground the props stand on.
        paint.Color = t.MidGround;
        c.DrawRect(0, baseY - 1, w, bottom - baseY + 1, paint);

        float gapMin = t.Near == NearKind.Lamps ? 240 : 110, gapMax = t.Near == NearKind.Lamps ? 260 : 330;
        float x = 30 * scale;
        int index = 0;
        while (x < w - 30 * scale)
        {
            float size = propH * (0.7f + 0.45f * (float)rng.NextDouble());
            var kind = t.Near;
            if (kind == NearKind.Houses && index % 3 != 0) kind = NearKind.SnowPines; // a cabin among the pines
            int seed = rng.Next();
            foreach (float ox in new[] { 0f, -w, w })
            {
                float px = x + ox;
                if (px < -size * 1.5f || px > w + size * 1.5f) continue;
                DrawProp(c, paint, path, t, kind, px, baseY, size, scale, new Random(seed), ox == 0 ? lights : null, top);
            }
            x += (gapMin + (gapMax - gapMin) * (float)rng.NextDouble()) * scale;
            index++;
        }
        return surface.Snapshot();
    }

    private static void DrawProp(SKCanvas c, SKPaint paint, SKPath path, HighwayTheme t, NearKind kind, float x, float baseY,
                                 float s, float scale, Random rng, List<SKPoint>? lights, float top)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.Color = t.NearColor;
        switch (kind)
        {
            case NearKind.Lamps:
            {
                float pole = 3f * scale, hgt = s * 1.15f;
                c.DrawRect(x - pole / 2, baseY - hgt, pole, hgt, paint);
                c.DrawRect(x - pole / 2, baseY - hgt, s * 0.28f, pole, paint);
                paint.Color = t.FarColor2;
                c.DrawRoundRect(x + s * 0.2f, baseY - hgt + pole, s * 0.12f, pole * 1.2f, pole / 2, pole / 2, paint);
                lights?.Add(new SKPoint(x + s * 0.26f, baseY - hgt + pole * 2 - top));
                break;
            }
            case NearKind.Palms:
            {
                float hgt = s * 1.3f, lean = s * 0.25f * (rng.NextDouble() < 0.5 ? -1 : 1);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.StrokeWidth = s * 0.07f;
                path.Reset();
                path.MoveTo(x, baseY);
                path.QuadTo(x + lean * 0.2f, baseY - hgt * 0.6f, x + lean, baseY - hgt);
                c.DrawPath(path, paint);
                paint.Style = SKPaintStyle.Fill;
                float tx = x + lean, ty = baseY - hgt;
                for (int f = 0; f < 7; f++)
                {
                    float a = -MathF.PI / 2 + (f - 3) * 0.48f;
                    float len = s * (0.55f + 0.15f * (float)rng.NextDouble());
                    float ex = tx + MathF.Cos(a) * len, ey = ty + MathF.Sin(a) * len * 0.55f + len * 0.35f;
                    path.Reset();
                    path.MoveTo(tx, ty);
                    path.QuadTo(tx + MathF.Cos(a) * len * 0.6f, ty + MathF.Sin(a) * len * 0.6f - s * 0.05f, ex, ey);
                    path.QuadTo(tx + MathF.Cos(a) * len * 0.5f, ty + MathF.Sin(a) * len * 0.4f + s * 0.04f, tx, ty);
                    c.DrawPath(path, paint);
                }
                break;
            }
            case NearKind.Pines or NearKind.SnowPines:
            {
                float hgt = s * 1.25f, half = s * 0.38f;
                c.DrawRect(x - s * 0.05f, baseY - hgt * 0.18f, s * 0.1f, hgt * 0.18f, paint);
                for (int k = 0; k < 3; k++)
                {
                    float tTop = baseY - hgt + k * hgt * 0.22f, tBot = baseY - hgt * 0.12f - (2 - k) * hgt * 0.16f;
                    float hw = half * (0.55f + k * 0.25f);
                    path.Reset();
                    path.MoveTo(x, tTop);
                    path.LineTo(x + hw, tBot);
                    path.LineTo(x - hw, tBot);
                    path.Close();
                    paint.Color = t.NearColor;
                    c.DrawPath(path, paint);
                    if (kind == NearKind.SnowPines)
                    {
                        // Snow resting on each tier
                        paint.Color = new SKColor(0xEE, 0xF4, 0xFF);
                        path.Reset();
                        path.MoveTo(x, tTop);
                        path.LineTo(x + hw * 0.45f, tTop + (tBot - tTop) * 0.45f);
                        path.QuadTo(x, tTop + (tBot - tTop) * 0.32f, x - hw * 0.45f, tTop + (tBot - tTop) * 0.45f);
                        path.Close();
                        c.DrawPath(path, paint);
                    }
                }
                break;
            }
            case NearKind.Cacti:
            {
                float hgt = s * (0.8f + 0.4f * (float)rng.NextDouble()), wd = s * 0.16f;
                c.DrawRoundRect(x - wd / 2, baseY - hgt, wd, hgt, wd / 2, wd / 2, paint);
                float armY = baseY - hgt * (0.45f + 0.15f * (float)rng.NextDouble());
                c.DrawRoundRect(x - wd * 2f, armY - hgt * 0.3f, wd * 0.8f, hgt * 0.3f, wd * 0.4f, wd * 0.4f, paint);
                c.DrawRect(x - wd * 1.6f, armY - wd * 0.4f, wd * 1.2f, wd * 0.8f, paint);
                float armY2 = baseY - hgt * (0.55f + 0.15f * (float)rng.NextDouble());
                c.DrawRoundRect(x + wd * 1.2f, armY2 - hgt * 0.25f, wd * 0.8f, hgt * 0.25f, wd * 0.4f, wd * 0.4f, paint);
                c.DrawRect(x + wd * 0.4f, armY2 - wd * 0.4f, wd * 1.2f, wd * 0.8f, paint);
                break;
            }
            case NearKind.Trees:
            {
                float hgt = s * 1.0f;
                paint.Color = new SKColor(0x6A, 0x44, 0x26);
                c.DrawRect(x - s * 0.05f, baseY - hgt * 0.5f, s * 0.1f, hgt * 0.5f, paint);
                paint.Color = t.NearColor;
                c.DrawCircle(x, baseY - hgt * 0.72f, s * 0.32f, paint);
                c.DrawCircle(x - s * 0.22f, baseY - hgt * 0.55f, s * 0.24f, paint);
                c.DrawCircle(x + s * 0.22f, baseY - hgt * 0.56f, s * 0.25f, paint);
                paint.Color = ColorUtil.Mix(t.NearColor, SKColors.White, 0.15f);
                c.DrawCircle(x - s * 0.08f, baseY - hgt * 0.8f, s * 0.14f, paint);
                break;
            }
            case NearKind.Houses:
            {
                float hw = s * 0.62f, hh = s * 0.5f;
                paint.Color = new SKColor(0x4A, 0x2C, 0x1C);
                c.DrawRect(x - hw / 2, baseY - hh, hw, hh, paint);
                // Roof with snow
                paint.Color = new SKColor(0x2A, 0x18, 0x14);
                path.Reset();
                path.MoveTo(x - hw * 0.62f, baseY - hh);
                path.LineTo(x, baseY - hh - s * 0.38f);
                path.LineTo(x + hw * 0.62f, baseY - hh);
                path.Close();
                c.DrawPath(path, paint);
                paint.Color = new SKColor(0xF0, 0xF4, 0xFF);
                path.Reset();
                path.MoveTo(x - hw * 0.64f, baseY - hh + 1);
                path.LineTo(x, baseY - hh - s * 0.4f);
                path.LineTo(x + hw * 0.64f, baseY - hh + 1);
                path.LineTo(x + hw * 0.5f, baseY - hh - s * 0.06f);
                path.LineTo(x, baseY - hh - s * 0.3f);
                path.LineTo(x - hw * 0.5f, baseY - hh - s * 0.06f);
                path.Close();
                c.DrawPath(path, paint);
                // Warm windows
                paint.Color = new SKColor(0xFF, 0xC8, 0x5A);
                float ww = hw * 0.22f;
                c.DrawRect(x - hw * 0.32f, baseY - hh * 0.72f, ww, ww, paint);
                c.DrawRect(x + hw * 0.1f, baseY - hh * 0.72f, ww, ww, paint);
                lights?.Add(new SKPoint(x - hw * 0.32f + ww / 2, baseY - hh * 0.72f + ww / 2 - top));
                lights?.Add(new SKPoint(x + hw * 0.1f + ww / 2, baseY - hh * 0.72f + ww / 2 - top));
                break;
            }
        }
    }

}
