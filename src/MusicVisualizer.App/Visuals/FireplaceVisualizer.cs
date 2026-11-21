using MusicVisualizer.Audio;
using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// A cozy log-cabin Christmas scene: a stone fireplace whose flames bounce to the music, stockings on the
/// mantle that swing and shake (each one listening to its own slice of the spectrum), a garland of lights
/// that twinkle with the treble, a wreath, frosty windows with snow falling outside and warm firelight
/// flickering across the room.
/// In overlay mode (where an opaque room would hide the desktop) it becomes a strand of lights with snow.
/// </summary>
/// <remarks>
/// Performance: everything static (walls, stone, mantle, windows, rug, garland greenery) is painted once per
/// window size into two cached images — the room dimly lit, and the room in full firelight. Each frame draws the
/// dim room, cross-fades in the lit one around the hearth by the fire's brightness, then only the moving parts.
/// The opaque scene would hide the host's glow layer, so halos here use a pre-rendered sprite instead.
/// </remarks>
public sealed class FireplaceVisualizer : IVisualizer
{
    private const int SnowCount = 90;
    private const int OverlaySnowCount = 260;
    private const int StockingCount = 4;
    private const int Swags = StockingCount + 1;

    // Fixed scene colours: warm wood, stone, cream, deep reds and greens.
    private static readonly SKColor Mortar = new(0x26, 0x1B, 0x14);
    private static readonly SKColor Cream = new(0xF4, 0xEC, 0xDC);
    private static readonly SKColor StockingRed = new(0xB0, 0x1E, 0x28);
    private static readonly SKColor StockingGreen = new(0x1E, 0x66, 0x3A);
    private static readonly SKColor Gold = new(0xFF, 0xD2, 0x5A);
    private static readonly SKColor Pine = new(0x1A, 0x4D, 0x2E);
    private static readonly SKColor PineLight = new(0x2F, 0x75, 0x45);
    private static readonly SKColor Warm = new(0xFF, 0x9A, 0x3C);

    private readonly FlameRenderer _flames = new();
    private readonly FlameRenderer _candleFlames = new();
    private readonly FlameRow _backFlames = new();
    private readonly FlameRow _frontFlames = new();
    private readonly Palette _firePalette = Palette.All.First(p => p.Name == "Fire");
    private readonly Random _rng = new(5);

    // Cached scene
    private SKImage? _dimRoom, _litRoom;
    private float _cacheW, _cacheH;
    private Layout _l;
    private readonly SKPath _fireboxClip = new();
    private readonly SKPath _paneClip = new();
    private readonly SKPath _logsPath = new();
    private SKShader? _logShader;

    // Stockings: angle, angular velocity
    private readonly float[] _angle = new float[StockingCount], _spin = new float[StockingCount];
    private readonly SKPath _stockingBody = new(), _stockingCuff = new(), _stockingToe = new(), _stockingHeel = new();

    // Snow (normalised coordinates)
    private readonly float[] _sx = new float[OverlaySnowCount], _sy = new float[OverlaySnowCount];
    private readonly float[] _sSpeed = new float[OverlaySnowCount], _sSize = new float[OverlaySnowCount], _sPhase = new float[OverlaySnowCount];
    private float _time, _wind;

    private readonly SKPath _path = new();
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _image = new() { IsAntialias = false };
    private readonly SKPaint _haloPaint = new() { BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _snow = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _glowFill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public FireplaceVisualizer()
    {
        for (int i = 0; i < OverlaySnowCount; i++)
        {
            _sx[i] = (float)_rng.NextDouble();
            _sy[i] = (float)_rng.NextDouble();
            RandomizeFlake(i);
        }
    }

    public string Name => "Fireplace";

    // A full illustrated scene is a lot of pixels; at 75% it's visually identical and ~half the CPU.
    public float RenderScale => 0.75f;

    public void Render(in RenderContext ctx)
    {
        _time += ctx.Dt;
        _wind *= MathF.Exp(-ctx.Dt * 1.5f);
        if (ctx.Frame.Beat)
            _wind += (_rng.Next(2) == 0 ? -1 : 1) * 0.06f * ctx.Frame.BeatStrength;
        EnsureHalo();

        if (ctx.Overlay)
        {
            // Opaque room would cover the desktop: just the lights and snow.
            float stripH = MathF.Min(ctx.Height, ctx.Width * 0.12f);
            DrawLightString(ctx, stripH);
            UpdateSnow(ctx.Dt, ctx.Frame.Level, OverlaySnowCount);
            DrawSnow(ctx.Canvas, new SKRect(0, 0, ctx.Width, ctx.Height), OverlaySnowCount, 0, ctx.Scale, 1f);
            return;
        }

        EnsureScene(ctx.Width, ctx.Height, ctx.Scale);
        DrawRoom(ctx);
    }

    // ================================================================ layout

    private struct Layout
    {
        public float W, H, U, Cx, FloorY;
        public SKRect Chimney, Mantle, Firebox, Hearth;
        public float MantleTop, MantleBottom, FireboxArchTop, LogTop;
        public SKRect[] Windows;
        public float[] HookX;
        public SKPoint Wreath;
        public float WreathR;
        public SKRect LitRegion;
        public SKPoint[] Candles;
    }

    private static Layout ComputeLayout(float w, float h)
    {
        var l = new Layout { W = w, H = h, U = MathF.Min(h, w / 1.45f), Cx = w / 2, FloorY = h * 0.86f };
        float u = l.U, cx = l.Cx;
        l.Chimney = new SKRect(cx - 0.40f * u, -1, cx + 0.40f * u, l.FloorY);
        l.MantleTop = l.FloorY - 0.50f * u;
        l.MantleBottom = l.MantleTop + 0.05f * u;
        l.Mantle = new SKRect(cx - 0.50f * u, l.MantleTop, cx + 0.50f * u, l.MantleBottom);
        float hearthTop = l.FloorY - 0.035f * u;
        l.Hearth = new SKRect(cx - 0.40f * u, hearthTop, cx + 0.40f * u, l.FloorY + 0.02f * u);
        l.Firebox = new SKRect(cx - 0.235f * u, l.MantleBottom + 0.10f * u, cx + 0.235f * u, hearthTop);
        l.FireboxArchTop = l.Firebox.Top;
        l.LogTop = hearthTop - 0.045f * u;

        float winW = 0.24f * u, winH = 0.34f * u, gap = 0.13f * u;
        float winTop = l.MantleTop - 0.20f * u;
        var left = new SKRect(l.Chimney.Left - gap - winW, winTop, l.Chimney.Left - gap, winTop + winH);
        var right = new SKRect(l.Chimney.Right + gap, winTop, l.Chimney.Right + gap + winW, winTop + winH);
        l.Windows = left.Left > w * 0.02f ? [left, right] : [];

        l.HookX = new float[StockingCount];
        for (int i = 0; i < StockingCount; i++)
            l.HookX[i] = l.Mantle.Left + l.Mantle.Width * (i + 1) / Swags;

        l.WreathR = 0.085f * u;
        l.Wreath = new SKPoint(cx, l.MantleTop - 0.19f * u);
        l.LitRegion = SKRect.Intersect(new SKRect(cx - 1.15f * u, l.MantleTop - 0.5f * u, cx + 1.15f * u, h), new SKRect(0, 0, w, h));
        l.Candles = [new SKPoint(cx - 0.43f * u, l.MantleTop), new SKPoint(cx + 0.43f * u, l.MantleTop)];
        return l;
    }

    // ================================================================ static scene (cached)

    private void EnsureScene(float w, float h, float scale)
    {
        if (_dimRoom != null && w == _cacheW && h == _cacheH) return;
        _cacheW = w;
        _cacheH = h;
        _l = ComputeLayout(w, h);

        var info = new SKImageInfo((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var surface = SKSurface.Create(info))
        {
            PaintRoom(surface.Canvas, scale, lit: false);
            _dimRoom?.Dispose();
            _dimRoom = surface.Snapshot();
        }
        using (var surface = SKSurface.Create(info))
        {
            PaintRoom(surface.Canvas, scale, lit: true);
            _litRoom?.Dispose();
            _litRoom = surface.Snapshot();
        }

        BuildFireboxPath(_fireboxClip, _l.Firebox, 0);
        _paneClip.Reset();
        foreach (var win in _l.Windows)
            foreach (var pane in Panes(win))
                _paneClip.AddRect(pane);

        BuildLogs();
        BuildStockingPaths();
    }

    private void PaintRoom(SKCanvas c, float scale, bool lit)
    {
        var l = _l;
        var rng = new Random(42); // same random details in both versions
        c.Clear(new SKColor(0x1A, 0x10, 0x0A));

        PaintLogWall(c, rng);
        for (int i = 0; i < l.Windows.Length; i++) PaintWindow(c, l.Windows[i], 100 + i, viewOnly: false);
        PaintFloorAndRug(c, rng);
        PaintChimney(c, rng);
        PaintMantle(c);
        PaintWreath(c, rng);
        PaintGarland(c, rng);
        PaintCandles(c);

        // Lighting. It's night: the room is lit only by the fire. Multiply the whole room down with a light map
        // that's warm and bright near the hearth and falls off into dusky shadow — the "lit" version is the
        // fire at full blaze, the dim one at a low smoulder. Frames cross-fade between the two.
        float fx = l.Cx, fy = l.Firebox.MidY;
        float reach = l.U * (lit ? 1.25f : 0.95f);
        SKColor[] lightMap = lit
            ? [new SKColor(255, 214, 160), new SKColor(235, 160, 100), new SKColor(120, 72, 52), new SKColor(46, 32, 34)]
            : [new SKColor(200, 140, 96), new SKColor(150, 96, 66), new SKColor(78, 50, 42), new SKColor(36, 26, 30)];
        using (var shader = Radial(new SKPoint(fx, fy), reach, lightMap, [0f, 0.3f, 0.7f, 1f], SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Multiply })
            c.DrawRect(0, 0, l.W, l.H, p);

        // A soft additive bloom spilling out of the firebox onto stone, hearth and rug.
        using (var bloom = Radial(new SKPoint(fx, l.Firebox.Bottom - l.Firebox.Height * 0.3f), l.U * (lit ? 0.62f : 0.45f),
                   [new SKColor(0xFF, 0x8C, 0x38, (byte)(lit ? 95 : 45)), new SKColor(0xFF, 0x70, 0x28, 0)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = bloom, BlendMode = SKBlendMode.Plus })
            c.DrawRect(0, 0, l.W, l.H, p);

        // Moonlight through the windows: a faint cool spill on the logs below each one.
        foreach (var win in l.Windows)
        {
            using var moon = Radial(new SKPoint(win.MidX, win.MidY), win.Width * 1.1f,
                [new SKColor(0x70, 0x90, 0xD0, 34), new SKColor(0x70, 0x90, 0xD0, 0)], null, SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = moon, BlendMode = SKBlendMode.Plus };
            c.DrawRect(win.Left - win.Width, win.Top - win.Height * 0.5f, win.Width * 3, win.Height * 2.5f, p);
        }

        // The view outside is lit by the moon, not the fire: repaint it over the firelit version.
        for (int i = 0; i < l.Windows.Length; i++) PaintWindow(c, l.Windows[i], 100 + i, viewOnly: true);

        // Vignette
        using (var vignette = Radial(new SKPoint(l.Cx, l.H * 0.62f), MathF.Max(l.W, l.H) * 0.72f,
                   [SKColors.Transparent, SKColors.Transparent, new SKColor(0, 0, 0, 170)], [0f, 0.55f, 1f], SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = vignette })
            c.DrawRect(0, 0, l.W, l.H, p);
    }

    private void PaintLogWall(SKCanvas c, Random rng)
    {
        var l = _l;
        float logH = l.H / 10.5f;
        _stroke.Shader = null;
        for (float y = -logH * 0.3f; y < l.FloorY; y += logH)
        {
            // Each log: rounded cylinder shading top-to-bottom, slightly varied tone.
            float tone = 0.85f + 0.3f * (float)rng.NextDouble();
            SKColor T(int r, int g, int b) => new((byte)Math.Min(255, r * tone), (byte)Math.Min(255, g * tone), (byte)Math.Min(255, b * tone));
            using var shader = Linear(new SKPoint(0, y), new SKPoint(0, y + logH),
                [T(0x2E, 0x1B, 0x10), T(0x7A, 0x4C, 0x2B), T(0x93, 0x5E, 0x36), T(0x6A, 0x40, 0x24), T(0x2A, 0x18, 0x0E)],
                [0f, 0.22f, 0.4f, 0.75f, 1f], SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader, IsAntialias = true };
            c.DrawRoundRect(new SKRect(-20, y + logH * 0.04f, l.W + 20, y + logH * 0.96f), logH * 0.45f, logH * 0.45f, p);

            // Grain lines and the odd knot.
            using var grain = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1, logH * 0.02f), Color = new SKColor(0x2A, 0x16, 0x0B, 70) };
            for (int g = 0; g < 7; g++)
            {
                float gy = y + logH * (0.25f + 0.5f * (float)rng.NextDouble());
                float gx = (float)rng.NextDouble() * l.W;
                float len = l.W * (0.08f + 0.2f * (float)rng.NextDouble());
                c.DrawLine(gx, gy, gx + len, gy + logH * 0.03f * ((float)rng.NextDouble() - 0.5f), grain);
            }
            for (int k = 0; k < 2; k++)
            {
                if (rng.NextDouble() < 0.5) continue;
                float kx = (float)rng.NextDouble() * l.W, ky = y + logH * (0.35f + 0.3f * (float)rng.NextDouble());
                using var knot = new SKPaint { IsAntialias = true, Color = new SKColor(0x30, 0x1A, 0x0D, 160) };
                c.DrawOval(kx, ky, logH * 0.09f, logH * 0.05f, knot);
            }
            // Chinking between logs
            using var chink = new SKPaint { Color = Mortar };
            c.DrawRect(-1, y + logH * 0.955f, l.W + 2, logH * 0.09f, chink);
        }
    }

    private static IEnumerable<SKRect> Panes(SKRect glass)
    {
        // 2 x 3 panes separated by muntins
        float mw = glass.Width * 0.035f;
        float pw = (glass.Width - mw) / 2, ph = (glass.Height - 2 * mw) / 3;
        for (int r = 0; r < 3; r++)
            for (int col = 0; col < 2; col++)
                yield return new SKRect(glass.Left + col * (pw + mw), glass.Top + r * (ph + mw),
                                        glass.Left + col * (pw + mw) + pw, glass.Top + r * (ph + mw) + ph);
    }

    /// <param name="viewOnly">Repaint just the moonlit view through the panes (after the room's firelight was applied).</param>
    private void PaintWindow(SKCanvas c, SKRect glass, int seed, bool viewOnly)
    {
        var rng = new Random(seed);
        float u = _l.U;
        if (viewOnly)
        {
            c.Save();
            using var panes = new SKPath();
            foreach (var pane in Panes(glass)) panes.AddRect(pane);
            c.ClipPath(panes);
        }
        float frameW = 0.022f * u;
        var outer = new SKRect(glass.Left - frameW, glass.Top - frameW, glass.Right + frameW, glass.Bottom + frameW);

        // Night sky, distant snowy hill, pines
        using (var sky = Linear(new SKPoint(0, glass.Top), new SKPoint(0, glass.Bottom),
                   [new SKColor(0x0A, 0x12, 0x2A), new SKColor(0x1C, 0x2E, 0x52), new SKColor(0x3A, 0x4E, 0x78)], [0f, 0.6f, 1f], SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = sky })
            c.DrawRect(glass, p);

        c.Save();
        c.ClipRect(glass);
        // Moon glow
        using (var moon = Radial(new SKPoint(glass.Left + glass.Width * 0.7f, glass.Top + glass.Height * 0.2f), glass.Width * 0.35f,
                   [new SKColor(0xE8, 0xF0, 0xFF, 90), new SKColor(0xE8, 0xF0, 0xFF, 0)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = moon })
            c.DrawRect(glass, p);
        // Snowy ground
        _path.Reset();
        float gy = glass.Top + glass.Height * 0.74f;
        _path.MoveTo(glass.Left, gy);
        _path.QuadTo(glass.MidX, gy - glass.Height * 0.08f, glass.Right, gy + glass.Height * 0.02f);
        _path.LineTo(glass.Right, glass.Bottom);
        _path.LineTo(glass.Left, glass.Bottom);
        _path.Close();
        using (var snowGround = new SKPaint { IsAntialias = true, Color = new SKColor(0xB8, 0xC6, 0xDE) })
            c.DrawPath(_path, snowGround);
        // Pines
        using var pine = new SKPaint { IsAntialias = true, Color = new SKColor(0x0E, 0x22, 0x1C) };
        for (int t = 0; t < 4; t++)
        {
            float tx = glass.Left + glass.Width * (0.12f + 0.26f * t + 0.08f * (float)rng.NextDouble());
            float th = glass.Height * (0.22f + 0.12f * (float)rng.NextDouble());
            float tb = gy + glass.Height * 0.03f;
            _path.Reset();
            for (int tier = 0; tier < 3; tier++)
            {
                float top = tb - th + tier * th * 0.25f, bot = tb - th * 0.55f + tier * th * 0.25f;
                float half = th * (0.18f + tier * 0.07f);
                _path.MoveTo(tx, top);
                _path.LineTo(tx + half, bot);
                _path.LineTo(tx - half, bot);
                _path.Close();
            }
            c.DrawPath(_path, pine);
        }
        c.Restore();

        // Wooden frame, muntins and sill
        if (!viewOnly) PaintWindowFrame(c, glass, outer, frameW);
        // Drifts of snow on the outside of the bottom panes
        using var frost = new SKPaint { IsAntialias = true, Color = new SKColor(0xF0, 0xF6, 0xFF, 220) };
        foreach (var pane in Panes(glass).Skip(4))
        {
            _path.Reset();
            _path.MoveTo(pane.Left, pane.Bottom);
            _path.QuadTo(pane.MidX, pane.Bottom - pane.Height * 0.16f, pane.Right, pane.Bottom);
            _path.Close();
            c.DrawPath(_path, frost);
        }
        // Frosted corners on every pane
        foreach (var pane in Panes(glass))
        {
            using var corner = Radial(new SKPoint(pane.Left, pane.Top), pane.Width * 0.5f,
                [new SKColor(0xE8, 0xF2, 0xFF, 70), new SKColor(0xE8, 0xF2, 0xFF, 0)], null, SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = corner };
            c.DrawRect(pane, p);
        }
        if (viewOnly) c.Restore();
    }

    private void PaintWindowFrame(SKCanvas c, SKRect glass, SKRect outer, float frameW)
    {
        using var wood = new SKPaint { IsAntialias = true, Color = new SKColor(0x4A, 0x2C, 0x18) };
        using var woodHi = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1, frameW * 0.18f), Color = new SKColor(0x8A, 0x5A, 0x34) };
        _path.Reset();
        _path.AddRect(outer);
        foreach (var pane in Panes(glass)) _path.AddRect(pane, SKPathDirection.CounterClockwise);
        _path.FillType = SKPathFillType.EvenOdd;
        c.DrawPath(_path, wood);
        _path.FillType = SKPathFillType.Winding;
        c.DrawRect(outer, woodHi);
        var sill = new SKRect(outer.Left - frameW * 0.8f, outer.Bottom - frameW * 0.2f, outer.Right + frameW * 0.8f, outer.Bottom + frameW * 0.9f);
        c.DrawRoundRect(sill, frameW * 0.2f, frameW * 0.2f, wood);
    }

    private void PaintFloorAndRug(SKCanvas c, Random rng)
    {
        var l = _l;
        float floorH = l.H - l.FloorY;
        using (var floor = Linear(new SKPoint(0, l.FloorY), new SKPoint(0, l.H),
                   [new SKColor(0x3A, 0x22, 0x12), new SKColor(0x5A, 0x36, 0x1D)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = floor })
            c.DrawRect(0, l.FloorY, l.W, floorH, p);
        // Plank seams (converging slightly, for a hint of perspective)
        using var seam = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1, l.U * 0.002f), Color = new SKColor(0x1E, 0x10, 0x08, 160) };
        for (int i = -12; i <= 12; i++)
        {
            float xTop = l.Cx + i * l.U * 0.11f;
            float xBot = l.Cx + i * l.U * 0.16f;
            c.DrawLine(xTop, l.FloorY, xBot, l.H, seam);
        }
        // Baseboard
        using (var board = new SKPaint { Color = new SKColor(0x2C, 0x18, 0x0C) })
            c.DrawRect(0, l.FloorY - l.U * 0.012f, l.W, l.U * 0.016f, board);

        // Braided oval rug in front of the hearth
        float rcx = l.Cx, rcy = l.FloorY + floorH * 0.62f;
        float rx = l.U * 0.52f, ry = floorH * 0.42f;
        SKColor[] rings = [new(0x7A, 0x1A, 0x1E), new(0xC9, 0xB9, 0x96), new(0x2A, 0x5A, 0x38), new(0x9A, 0x26, 0x24), new(0xC9, 0xB9, 0x96), new(0x6E, 0x18, 0x1A)];
        using var rp = new SKPaint { IsAntialias = true };
        for (int i = 0; i < rings.Length; i++)
        {
            float f = 1f - i / (float)rings.Length;
            rp.Color = rings[i];
            c.DrawOval(rcx, rcy, rx * f, ry * f, rp);
        }
        using var braid = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1, l.U * 0.002f), Color = new SKColor(0, 0, 0, 50) };
        for (int i = 1; i < rings.Length * 2; i++)
        {
            float f = 1f - i / (float)(rings.Length * 2);
            c.DrawOval(rcx, rcy, rx * f, ry * f, braid);
        }
    }

    private void PaintChimney(SKCanvas c, Random rng)
    {
        var l = _l;
        var ch = l.Chimney;
        using (var mortar = new SKPaint { Color = new SKColor(0x2B, 0x26, 0x22) })
            c.DrawRect(ch, mortar);

        // Irregular river-rock fieldstones: courses of varying height, stones of varying width and tone,
        // each slightly offset and with its own corner rounding. Clipped to the chimney.
        c.Save();
        c.ClipRect(ch);
        using var stone = new SKPaint { IsAntialias = true };
        using var stoneHi = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1, l.U * 0.0025f), Color = new SKColor(255, 240, 220, 45) };
        float sy = ch.Top - l.U * 0.02f;
        while (sy < ch.Bottom)
        {
            float course = l.U * (0.045f + 0.03f * (float)rng.NextDouble());
            float x = ch.Left - (float)rng.NextDouble() * course;
            while (x < ch.Right)
            {
                float sw = course * (1.0f + 1.1f * (float)rng.NextDouble());
                float g = l.U * 0.0045f;
                float jy = ((float)rng.NextDouble() - 0.5f) * course * 0.12f;
                var r = new SKRect(x + g, sy + g + jy, x + sw - g, sy + course - g + jy);
                // Fieldstone palette: grey-browns with the odd warm or cool stone.
                int v = 70 + rng.Next(45);
                int warm = rng.Next(-6, 18);
                var top = new SKColor((byte)Math.Clamp(v + warm, 0, 255), (byte)Math.Clamp(v + warm / 2, 0, 255), (byte)Math.Clamp(v - 4, 0, 255));
                var bottom = new SKColor((byte)(top.Red * 0.55f), (byte)(top.Green * 0.53f), (byte)(top.Blue * 0.5f));
                using var shade = Linear(new SKPoint(r.Left, r.Top), new SKPoint(r.Left + r.Width * 0.2f, r.Bottom), [top, bottom], null, SKShaderTileMode.Clamp);
                stone.Shader = shade;
                float rx = MathF.Min(r.Width, r.Height) * (0.3f + 0.2f * (float)rng.NextDouble());
                c.DrawRoundRect(r, rx, rx * 0.9f, stone);
                stone.Shader = null;
                c.DrawLine(r.Left + rx, r.Top + stoneHi.StrokeWidth, r.Right - rx, r.Top + stoneHi.StrokeWidth, stoneHi);
                x += sw;
            }
            sy += course;
        }
        c.Restore();
        // Shadow where the chimney meets the log wall
        using (var edge = Linear(new SKPoint(ch.Left - l.U * 0.04f, 0), new SKPoint(ch.Left, 0),
                   [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 120)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = edge })
            c.DrawRect(ch.Left - l.U * 0.04f, ch.Top, l.U * 0.04f, ch.Height, p);
        using (var edge = Linear(new SKPoint(ch.Right, 0), new SKPoint(ch.Right + l.U * 0.04f, 0),
                   [new SKColor(0, 0, 0, 120), new SKColor(0, 0, 0, 0)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = edge })
            c.DrawRect(ch.Right, ch.Top, l.U * 0.04f, ch.Height, p);

        // Firebox: arched opening with a soot-dark brick interior
        var fb = l.Firebox;
        using (var arch = new SKPaint { IsAntialias = true, Color = new SKColor(0x4E, 0x46, 0x40) })
        {
            BuildFireboxPath(_path, fb, l.U * 0.03f);
            c.DrawPath(_path, arch); // keystone border
        }
        BuildFireboxPath(_path, fb, 0);
        c.Save();
        c.ClipPath(_path, SKClipOperation.Intersect, true);
        using (var inner = Linear(new SKPoint(0, fb.Top), new SKPoint(0, fb.Bottom),
                   [new SKColor(0x0C, 0x06, 0x04), new SKColor(0x22, 0x10, 0x09)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = inner })
            c.DrawRect(fb, p);
        using var brick = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1, l.U * 0.0025f), Color = new SKColor(0x3A, 0x1E, 0x12, 140) };
        float bh = l.U * 0.03f;
        int br = 0;
        for (float y = fb.Top; y < fb.Bottom; y += bh, br++)
        {
            c.DrawLine(fb.Left, y, fb.Right, y, brick);
            for (float x = fb.Left + (br % 2) * bh; x < fb.Right; x += bh * 2)
                c.DrawLine(x, y, x, y + bh, brick);
        }
        c.Restore();

        // Hearth slab
        var hs = l.Hearth;
        using (var slab = Linear(new SKPoint(0, hs.Top), new SKPoint(0, hs.Bottom),
                   [new SKColor(0x8C, 0x84, 0x7A), new SKColor(0x55, 0x4E, 0x47)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = slab, IsAntialias = true })
            c.DrawRoundRect(hs, l.U * 0.006f, l.U * 0.006f, p);
    }

    private static void BuildFireboxPath(SKPath p, SKRect fb, float inflate)
    {
        // Straight sides with a shallow segmental arch on top.
        float l = fb.Left - inflate, r = fb.Right + inflate, b = fb.Bottom, top = fb.Top - inflate;
        float springY = top + fb.Height * 0.16f;
        p.Reset();
        p.MoveTo(l, b);
        p.LineTo(l, springY);
        p.QuadTo((l + r) / 2, top - fb.Height * 0.12f, r, springY);
        p.LineTo(r, b);
        p.Close();
    }

    private void PaintMantle(SKCanvas c)
    {
        var l = _l;
        var m = l.Mantle;
        // Thick hand-hewn beam
        using (var beam = Linear(new SKPoint(0, m.Top), new SKPoint(0, m.Bottom),
                   [new SKColor(0x8A, 0x55, 0x30), new SKColor(0x6A, 0x3E, 0x22), new SKColor(0x3C, 0x22, 0x12)], [0f, 0.35f, 1f], SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = beam, IsAntialias = true })
            c.DrawRoundRect(m, l.U * 0.008f, l.U * 0.008f, p);
        using var grain = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1, l.U * 0.0015f), Color = new SKColor(0x2A, 0x16, 0x0A, 90) };
        for (int i = 0; i < 4; i++)
        {
            float y = m.Top + m.Height * (0.3f + 0.15f * i);
            c.DrawLine(m.Left + m.Width * 0.05f * i, y, m.Right - m.Width * 0.07f * (3 - i), y + m.Height * 0.05f, grain);
        }
        // Shadow cast below the beam
        using (var sh = Linear(new SKPoint(0, m.Bottom), new SKPoint(0, m.Bottom + l.U * 0.05f),
                   [new SKColor(0, 0, 0, 110), new SKColor(0, 0, 0, 0)], null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = sh })
            c.DrawRect(m.Left, m.Bottom, m.Width, l.U * 0.05f, p);
    }

    private void PaintWreath(SKCanvas c, Random rng)
    {
        var l = _l;
        float r = l.WreathR, cx = l.Wreath.X, cy = l.Wreath.Y;
        using var leaf = new SKPaint { IsAntialias = true };
        // Layered needle clusters around the ring
        for (int layer = 0; layer < 3; layer++)
        {
            int n = 34;
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n + layer * 0.37f;
                float rr = r * (0.82f + 0.12f * layer + 0.08f * (float)rng.NextDouble());
                leaf.Color = layer == 2 ? PineLight : layer == 1 ? new SKColor(0x22, 0x5E, 0x38) : Pine;
                float bx = cx + MathF.Cos(a) * rr, by = cy + MathF.Sin(a) * rr;
                c.Save();
                c.RotateRadians(a + MathF.PI / 2 + 0.6f, bx, by);
                c.DrawOval(bx, by, r * 0.2f, r * 0.08f, leaf);
                c.Restore();
            }
        }
        // Berries
        using var berry = new SKPaint { IsAntialias = true, Color = new SKColor(0xC4, 0x1A, 0x22) };
        for (int i = 0; i < 9; i++)
        {
            float a = i * MathF.Tau / 9 + 0.2f;
            c.DrawCircle(cx + MathF.Cos(a) * r * 1.02f, cy + MathF.Sin(a) * r * 1.02f, r * 0.055f, berry);
        }
        // Velvet bow at the bottom
        using var bow = new SKPaint { IsAntialias = true, Color = new SKColor(0xB3, 0x16, 0x22) };
        float by2 = cy + r * 0.98f;
        _path.Reset();
        _path.MoveTo(cx, by2);
        _path.CubicTo(cx - r * 0.5f, by2 - r * 0.35f, cx - r * 0.55f, by2 + r * 0.25f, cx, by2);
        _path.CubicTo(cx + r * 0.5f, by2 - r * 0.35f, cx + r * 0.55f, by2 + r * 0.25f, cx, by2);
        c.DrawPath(_path, bow);
        _path.Reset();
        _path.MoveTo(cx - r * 0.05f, by2);
        _path.LineTo(cx - r * 0.28f, by2 + r * 0.5f);
        _path.LineTo(cx - r * 0.12f, by2 + r * 0.45f);
        _path.LineTo(cx, by2);
        _path.LineTo(cx + r * 0.12f, by2 + r * 0.45f);
        _path.LineTo(cx + r * 0.28f, by2 + r * 0.5f);
        _path.LineTo(cx + r * 0.05f, by2);
        _path.Close();
        c.DrawPath(_path, bow);
        c.DrawCircle(cx, by2, r * 0.07f, bow);
    }

    private void PaintGarland(SKCanvas c, Random rng)
    {
        // Pine garland drooping in swags along the mantle's front edge.
        var m = _l.Mantle;
        float u = _l.U;
        using var leaf = new SKPaint { IsAntialias = true };
        for (int s = 0; s < Swags; s++)
        {
            float x0 = m.Left + m.Width * s / Swags, x1 = m.Left + m.Width * (s + 1) / Swags;
            for (int k = 0; k < 40; k++)
            {
                float t = k / 39f;
                var p = SwagPoint(x0, x1, m.Bottom, u, t);
                for (int j = 0; j < 2; j++)
                {
                    leaf.Color = (k + j) % 3 == 0 ? PineLight : (k + j) % 3 == 1 ? Pine : new SKColor(0x24, 0x5C, 0x37);
                    float a = (float)(rng.NextDouble() * MathF.Tau);
                    float ox = MathF.Cos(a) * u * 0.008f, oy = MathF.Sin(a) * u * 0.008f;
                    c.Save();
                    c.RotateRadians(a, p.X + ox, p.Y + oy);
                    c.DrawOval(p.X + ox, p.Y + oy, u * 0.018f, u * 0.007f, leaf);
                    c.Restore();
                }
            }
        }
    }

    private static SKPoint SwagPoint(float x0, float x1, float y, float u, float t)
    {
        float sag = u * 0.045f;
        return new SKPoint(x0 + (x1 - x0) * t, y - u * 0.004f + sag * 4f * t * (1 - t));
    }

    private void PaintCandles(SKCanvas c)
    {
        float u = _l.U;
        using var wax = new SKPaint { IsAntialias = true };
        using var holder = new SKPaint { IsAntialias = true, Color = new SKColor(0x8A, 0x6A, 0x3A) };
        foreach (var p in _l.Candles)
        {
            float cw = u * 0.024f, chh = u * 0.075f;
            c.DrawRoundRect(new SKRect(p.X - cw * 1.1f, p.Y - u * 0.008f, p.X + cw * 1.1f, p.Y), u * 0.003f, u * 0.003f, holder);
            using var shade = Linear(new SKPoint(p.X - cw / 2, 0), new SKPoint(p.X + cw / 2, 0),
                [new SKColor(0xD8, 0xCC, 0xB4), Cream, new SKColor(0xBE, 0xB0, 0x96)], null, SKShaderTileMode.Clamp);
            wax.Shader = shade;
            c.DrawRoundRect(new SKRect(p.X - cw / 2, p.Y - u * 0.008f - chh, p.X + cw / 2, p.Y - u * 0.008f), u * 0.004f, u * 0.004f, wax);
            wax.Shader = null;
        }
    }

    // ================================================================ dynamic parts

    private void DrawRoom(in RenderContext ctx)
    {
        var c = ctx.Canvas;
        var l = _l;
        var frame = ctx.Frame;

        c.DrawImage(_dimRoom, 0, 0, _image);

        // Firelight: cross-fade in the lit room around the hearth by the fire's brightness, with a flicker.
        float flicker = 0.08f * MathF.Sin(_time * 13f) * MathF.Sin(_time * 7.7f + 1.3f);
        float light = Math.Clamp(0.35f + 0.4f * frame.Level + 0.3f * frame.Pulse + flicker, 0f, 1f);
        _image.Color = SKColors.White.WithAlpha((byte)(255 * light));
        c.DrawImage(_litRoom, l.LitRegion, l.LitRegion, SKSamplingOptions.Default, _image);
        _image.Color = SKColors.White;

        // Snow falling past the windows
        if (l.Windows.Length > 0)
        {
            UpdateSnow(ctx.Dt, frame.Level, SnowCount);
            c.Save();
            c.ClipPath(_paneClip);
            foreach (var win in l.Windows)
                DrawSnow(c, win, SnowCount, win.Left, ctx.Scale, 0.8f);
            c.Restore();
        }

        DrawFire(ctx);
        DrawStockings(ctx);
        DrawGarlandLights(ctx);
        DrawWreathLights(ctx);
        DrawCandleFlames(ctx);
    }

    private void DrawFire(in RenderContext ctx)
    {
        var c = ctx.Canvas;
        var l = _l;
        var frame = ctx.Frame;
        var fb = l.Firebox;
        float u = l.U;

        c.Save();
        c.ClipPath(_fireboxClip, SKClipOperation.Intersect, false);

        // Glow on the back wall of the firebox
        float glowA = 0.45f + 0.35f * frame.Level + 0.25f * frame.Pulse;
        using (var back = Radial(new SKPoint(fb.MidX, l.LogTop), fb.Width * 0.7f,
                   [Warm.WithAlpha((byte)(200 * Math.Min(1f, glowA))), new SKColor(0xC0, 0x40, 0x10, (byte)(90 * Math.Min(1f, glowA))), SKColors.Transparent],
                   [0f, 0.45f, 1f], SKShaderTileMode.Clamp))
        {
            _glowFill.Shader = back;
            c.DrawRect(fb, _glowFill);
            _glowFill.Shader = null;
        }

        // Flames: bass in the middle, highs to the sides.
        static float Mirror(int i, int n) => MathF.Abs(i / (float)(n - 1) * 2f - 1f);
        _backFlames.Ensure(6, i => Mirror(i, 6), 31);
        _frontFlames.Ensure(5, i => Mirror(i, 5), 47);
        _backFlames.Update(frame, ctx.Dt);
        _frontFlames.Update(frame, ctx.Dt);

        float baseY = l.LogTop + u * 0.02f;
        float maxH = (baseY - fb.Top) * 0.95f;
        float span = fb.Width * 0.8f;
        float left = fb.MidX - span / 2;

        _flames.BeginRow(_firePalette, baseY, maxH * 1.15f, 0.9f);
        for (int i = 0; i < _backFlames.Count; i++)
        {
            float x = left + span * i / (_backFlames.Count - 1);
            float centre = 1f - Mirror(i, _backFlames.Count) * 0.45f; // taller in the middle
            float tw = span / 2.5f;
            float th = maxH * 1.25f * centre * _backFlames.Height(i);
            _flames.DrawTongue(c, x, baseY, tw, th, _backFlames.Sway(i) * tw * 0.6f, _backFlames.Wiggle(i) * tw, layers: 2);
        }
        _flames.BeginRow(_firePalette, baseY, maxH * 0.95f);
        for (int i = 0; i < _frontFlames.Count; i++)
        {
            float x = left + span * (i + 0.5f) / _frontFlames.Count;
            float centre = 1f - Mirror(i, _frontFlames.Count) * 0.4f;
            float tw = span / 2.9f;
            float th = maxH * 0.95f * centre * _frontFlames.Height(i);
            _flames.DrawTongue(c, x, baseY, tw, th, _frontFlames.Sway(i) * tw * 0.6f, _frontFlames.Wiggle(i) * tw);
        }

        // Logs in front, their undersides glowing like coals.
        c.DrawPath(_logsPath, LogPaint());
        float coal = Math.Clamp(0.5f + 0.4f * frame.Level + 0.3f * frame.Pulse, 0f, 1f);
        using (var ember = Linear(new SKPoint(0, l.Hearth.Top), new SKPoint(0, l.Hearth.Top - u * 0.03f),
                   [new SKColor(0xFF, 0x7A, 0x20, (byte)(230 * coal)), new SKColor(0xFF, 0x40, 0x10, 0)], null, SKShaderTileMode.Clamp))
        {
            _glowFill.Shader = ember;
            c.DrawRect(fb.Left, l.Hearth.Top - u * 0.03f, fb.Width, u * 0.03f, _glowFill);
            _glowFill.Shader = null;
        }
        c.Restore();
    }

    private SKPaint LogPaint()
    {
        _fill.Color = SKColors.White;
        _fill.Shader = _logShader;
        return _fill;
    }

    private void BuildLogs()
    {
        var l = _l;
        var fb = l.Firebox;
        float u = l.U;
        float bottom = l.Hearth.Top;
        float r = u * 0.028f;
        _logsPath.Reset();
        // Two logs side by side and one across the top, ends slightly rounded.
        _logsPath.AddRoundRect(new SKRect(fb.Left + fb.Width * 0.08f, bottom - r * 2, fb.MidX + fb.Width * 0.06f, bottom), r, r);
        _logsPath.AddRoundRect(new SKRect(fb.MidX - fb.Width * 0.04f, bottom - r * 2, fb.Right - fb.Width * 0.08f, bottom), r, r);
        var top = new SKPath();
        top.AddRoundRect(new SKRect(fb.Left + fb.Width * 0.22f, bottom - r * 3.6f, fb.Right - fb.Width * 0.24f, bottom - r * 1.7f), r, r);
        top.Transform(SKMatrix.CreateRotationDegrees(-5, fb.MidX, bottom - r * 2.6f));
        _logsPath.AddPath(top);
        top.Dispose();

        _logShader?.Dispose();
        _logShader = Linear(new SKPoint(0, bottom - r * 3.6f), new SKPoint(0, bottom),
            [new SKColor(0x5A, 0x38, 0x20), new SKColor(0x2E, 0x1A, 0x0E), new SKColor(0x4A, 0x2A, 0x16), new SKColor(0x1A, 0x0C, 0x06)],
            [0f, 0.45f, 0.6f, 1f], SKShaderTileMode.Clamp);
    }

    // ---------------------------------------------------------------- stockings

    private void BuildStockingPaths()
    {
        float u = _l.U;
        float half = u * 0.04f, len = u * 0.21f, top = u * 0.018f;
        float cuffH = u * 0.05f;

        _stockingBody.Reset();
        _stockingBody.MoveTo(-half, top + cuffH * 0.8f);
        _stockingBody.LineTo(-half, len * 0.72f);
        _stockingBody.CubicTo(-half, len * 1.0f, half * 0.3f, len * 1.04f, half * 1.9f, len * 0.99f); // heel → sole
        _stockingBody.CubicTo(half * 3.0f, len * 0.96f, half * 3.0f, len * 0.74f, half * 1.7f, len * 0.71f); // toe
        _stockingBody.CubicTo(half * 1.0f, len * 0.70f, half, len * 0.64f, half, len * 0.56f);
        _stockingBody.LineTo(half, top + cuffH * 0.8f);
        _stockingBody.Close();

        _stockingCuff.Reset();
        _stockingCuff.AddRoundRect(new SKRect(-half * 1.25f, top, half * 1.25f, top + cuffH), half * 0.35f, half * 0.35f);

        // Toe and heel patches
        _stockingToe.Reset();
        _stockingToe.MoveTo(half * 1.55f, len * 0.715f);
        _stockingToe.CubicTo(half * 3.0f, len * 0.74f, half * 3.0f, len * 0.96f, half * 1.9f, len * 0.99f);
        _stockingToe.QuadTo(half * 1.3f, len * 0.85f, half * 1.55f, len * 0.715f);
        _stockingToe.Close();
        _stockingHeel.Reset();
        _stockingHeel.MoveTo(-half, len * 0.8f);
        _stockingHeel.CubicTo(-half, len * 1.0f, -half * 0.2f, len * 1.03f, half * 0.4f, len * 1.01f);
        _stockingHeel.QuadTo(-half * 0.1f, len * 0.86f, -half, len * 0.8f);
        _stockingHeel.Close();
    }

    private void DrawStockings(in RenderContext ctx)
    {
        var c = ctx.Canvas;
        var frame = ctx.Frame;
        var bands = frame.Bands;
        int n = bands.Length;
        float dt = Math.Min(ctx.Dt, 0.05f);
        float u = _l.U;
        using var firelight = SKColorFilter.CreateBlendMode(new SKColor(232, 178, 138), SKBlendMode.Modulate);
        _fill.ColorFilter = firelight;

        for (int i = 0; i < StockingCount; i++)
        {
            // Each stocking listens to its own quarter of the spectrum (left = bass).
            int lo = (int)(i / (float)StockingCount * n * 0.8f), hi = (int)((i + 1) / (float)StockingCount * n * 0.8f);
            float v = 0;
            for (int b = lo; b < hi; b++) v = MathF.Max(v, bands[b]);

            // Damped spring, jostled by its band and kicked on beats.
            float k = 38f, damping = 2.6f;
            _spin[i] += (-k * _angle[i] - damping * _spin[i]) * dt;
            _spin[i] += ((float)_rng.NextDouble() - 0.5f) * v * v * 60f * dt;
            if (frame.Beat) _spin[i] += (i % 2 == 0 ? 1 : -1) * frame.BeatStrength * (0.6f + v) * 2.2f;
            _angle[i] = Math.Clamp(_angle[i] + _spin[i] * dt, -0.6f, 0.6f);

            float hx = _l.HookX[i], hy = _l.MantleBottom - u * 0.004f;
            bool red = i % 2 == 0;
            int dir = i < StockingCount / 2 ? -1 : 1; // toes point outward
            float stretch = 1f + 0.06f * v;

            // Hook and loop
            _stroke.Shader = null;
            _stroke.Color = Gold;
            _stroke.StrokeWidth = MathF.Max(1.2f, u * 0.004f);
            c.DrawCircle(hx, hy, u * 0.006f, _stroke);

            c.Save();
            c.Translate(hx, hy);
            c.RotateRadians(_angle[i]);
            c.Scale(dir, stretch);

            _fill.Shader = null;
            _fill.Color = red ? StockingRed : StockingGreen;
            c.DrawPath(_stockingBody, _fill);
            // Knit stripes on the green ones
            if (!red)
            {
                c.Save();
                c.ClipPath(_stockingBody, SKClipOperation.Intersect, true);
                _fill.Color = Cream.WithAlpha(210);
                for (int s = 0; s < 4; s++)
                    c.DrawRect(-u * 0.06f, u * (0.09f + s * 0.035f), u * 0.2f, u * 0.012f, _fill);
                c.Restore();
            }
            // Soft shading down one side
            using (var shade = Linear(new SKPoint(-u * 0.04f, 0), new SKPoint(u * 0.04f, 0),
                       [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 70)], null, SKShaderTileMode.Clamp))
            {
                _fill.Shader = shade;
                c.DrawPath(_stockingBody, _fill);
                _fill.Shader = null;
            }
            _fill.Color = red ? Cream : StockingRed;
            c.DrawPath(_stockingToe, _fill);
            c.DrawPath(_stockingHeel, _fill);
            // Fluffy cuff
            _fill.Color = Cream;
            c.DrawPath(_stockingCuff, _fill);
            _fill.Color = new SKColor(0xD8, 0xCE, 0xBC);
            c.DrawRect(-u * 0.05f, u * 0.06f, u * 0.1f, u * 0.006f, _fill);
            c.Restore();
        }
        _fill.ColorFilter = null;
    }

    // ---------------------------------------------------------------- lights

    private void DrawGarlandLights(in RenderContext ctx)
    {
        var m = _l.Mantle;
        float u = _l.U;
        var bands = ctx.Frame.Bands;
        int n = bands.Length;
        int perSwag = 5;
        float bulbR = u * 0.0085f;
        for (int s = 0; s < Swags; s++)
        {
            float x0 = m.Left + m.Width * s / Swags, x1 = m.Left + m.Width * (s + 1) / Swags;
            for (int i = 0; i < perSwag; i++)
            {
                float t = (i + 0.5f) / perSwag;
                var p = SwagPoint(x0, x1, m.Bottom, u, t);
                int idx = s * perSwag + i;
                // Treble-weighted: lights sample the upper half of the spectrum, mirrored across the mantle.
                float mm = MathF.Abs(idx / (float)(Swags * perSwag - 1) * 2f - 1f);
                int band = (int)((0.35f + 0.5f * (1f - mm)) * (n - 1));
                var color = ctx.Palette.Colors[idx % ctx.Palette.Colors.Length];
                DrawBulb(ctx, p.X, p.Y + bulbR * 1.4f, bulbR, color, bands[band], glowOnMain: true);
            }
        }
    }

    private void DrawWreathLights(in RenderContext ctx)
    {
        float r = _l.WreathR;
        var frame = ctx.Frame;
        int count = 10;
        for (int i = 0; i < count; i++)
        {
            float a = i * MathF.Tau / count - MathF.PI / 2 + 0.3f;
            float x = _l.Wreath.X + MathF.Cos(a) * r * 0.9f, y = _l.Wreath.Y + MathF.Sin(a) * r * 0.9f;
            float twinkle = 0.5f + 0.5f * MathF.Sin(_time * 3f + i * 1.7f);
            float level = Math.Clamp(frame.Bass * 0.7f + frame.Pulse * 0.6f + 0.2f * twinkle, 0f, 1f);
            DrawHalo(ctx.Canvas, x, y, r * 0.28f * (0.6f + level), Gold, (byte)(180 * level));
            _fill.Shader = null;
            _fill.Color = SKColors.White.WithAlpha((byte)(120 + 135 * level));
            ctx.Canvas.DrawCircle(x, y, r * 0.035f, _fill);
        }
    }

    private void DrawCandleFlames(in RenderContext ctx)
    {
        float u = _l.U;
        foreach (var (p, i) in _l.Candles.Select((p, i) => (p, i)))
        {
            float top = p.Y - u * 0.083f;
            float flick = 0.85f + 0.15f * MathF.Sin(_time * 17f + i * 2f) * MathF.Sin(_time * 9f + i);
            float h = u * 0.042f * flick * (1f + 0.25f * ctx.Frame.Pulse);
            DrawHalo(ctx.Canvas, p.X, top - h * 0.4f, u * 0.05f, Warm, (byte)(110 + 60 * flick));
            _candleFlames.BeginRow(_firePalette, top, u * 0.05f);
            _candleFlames.DrawTongue(ctx.Canvas, p.X, top, u * 0.014f, h, MathF.Sin(_time * 3f + i) * u * 0.003f, 0);
            _stroke.Color = new SKColor(0x20, 0x14, 0x0A);
            _stroke.StrokeWidth = MathF.Max(1, u * 0.002f);
            ctx.Canvas.DrawLine(p.X, top + u * 0.002f, p.X, top - u * 0.006f, _stroke);
        }
    }

    /// <summary>A Christmas bulb. Halo goes on the main canvas (room view, where the glow layer is hidden behind
    /// the opaque scene) or on the host's glow layer (overlay view).</summary>
    private void DrawBulb(in RenderContext ctx, float x, float y, float r, SKColor color, float level, bool glowOnMain)
    {
        float b = 0.3f + 0.7f * MathF.Pow(level, 0.7f);
        if (glowOnMain)
            DrawHalo(ctx.Canvas, x, y, r * (3f + 2.5f * b), color, (byte)(230 * b * b));
        else if (ctx.GlowCanvas is { } glow)
        {
            _glowFill.Shader = null;
            _glowFill.Color = color.WithAlpha((byte)(220 * b * b));
            glow.DrawCircle(x, y, r * (1.6f + 1.4f * b), _glowFill);
        }
        _fill.Shader = null;
        _fill.Color = Mix(color, SKColors.White, b * 0.5f).WithAlpha((byte)(255 * (0.4f + 0.6f * b)));
        ctx.Canvas.DrawOval(x, y, r * 0.8f, r, _fill);
        _fill.Color = new SKColor(0x9A, 0x90, 0x78);
        ctx.Canvas.DrawRect(x - r * 0.35f, y - r * 1.25f, r * 0.7f, r * 0.4f, _fill);
    }

    /// <summary>Soft additive glow from a cached radial sprite — cheap, no blur needed.</summary>
    private void DrawHalo(SKCanvas c, float x, float y, float radius, SKColor color, byte alpha)
    {
        if (alpha < 3 || radius < 1) return;
        _haloPaint.Color = SKColors.White.WithAlpha(alpha); // paint alpha scales the pre-tinted sprite
        c.DrawImage(HaloFor(color), new SKRect(x - radius, y - radius, x + radius, y + radius), new SKSamplingOptions(SKFilterMode.Linear), _haloPaint);
    }

    /// <summary>One small pre-tinted radial sprite per colour (palette colours + gold + warm), built on first use.</summary>
    private SKImage HaloFor(SKColor color)
    {
        uint key = (uint)color.WithAlpha(255);
        if (_halos.TryGetValue(key, out var img)) return img;
        using var surface = SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = color.WithAlpha(255);
        using var shader = Radial(new SKPoint(32, 32), 32,
            [c, c.WithAlpha(110), c.WithAlpha(30), c.WithAlpha(0)], [0f, 0.2f, 0.55f, 1f], SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(0, 0, 64, 64, p);
        img = surface.Snapshot();
        _halos[key] = img;
        return img;
    }

    private readonly Dictionary<uint, SKImage> _halos = new();

    private void EnsureHalo() { }

    // ---------------------------------------------------------------- overlay: strand of lights

    private void DrawLightString(in RenderContext ctx, float h)
    {
        var canvas = ctx.Canvas;
        var bands = ctx.Frame.Bands;
        int n = bands.Length;
        float w = ctx.Width;
        int swags = Math.Max(3, (int)(w / (h * 2.2f)));
        float swagW = w / swags;
        float y0 = h * 0.2f, sag = h * 0.35f;
        float bulbR = MathF.Max(3f * ctx.Scale, h * 0.06f);
        int perSwag = Math.Max(4, (int)(swagW / (bulbR * 5f)));
        int total = swags * perSwag;

        _stroke.Shader = null;
        _stroke.Color = new SKColor(0x1E, 0x4D, 0x2B);
        _stroke.StrokeWidth = 2f * ctx.Scale;
        for (int sw = 0; sw < swags; sw++)
        {
            float x0 = sw * swagW, x1 = x0 + swagW;
            _path.Reset();
            _path.MoveTo(x0, y0);
            _path.QuadTo((x0 + x1) / 2, y0 + sag * 2f, x1, y0);
            canvas.DrawPath(_path, _stroke);

            for (int i = 0; i < perSwag; i++)
            {
                float u = (i + 0.5f) / perSwag;
                float px = Quad(x0, (x0 + x1) / 2, x1, u);
                float py = Quad(y0, y0 + sag * 2f, y0, u);
                int idx = sw * perSwag + i;
                // Mirror across the screen: bass at both ends, treble in the middle.
                float m = MathF.Abs(idx / (float)(total - 1) * 2f - 1f);
                int band = (int)((1f - m) * (n - 1) * 0.85f);
                var color = ctx.Palette.Colors[idx % ctx.Palette.Colors.Length];
                DrawBulb(ctx, px, py + bulbR * 0.9f, bulbR, color, bands[band], glowOnMain: false);
            }
        }
    }

    // ---------------------------------------------------------------- snow

    private void UpdateSnow(float dt, float level, int count)
    {
        float speedUp = 1f + 0.8f * level;
        for (int i = 0; i < count; i++)
        {
            _sy[i] += _sSpeed[i] * speedUp * dt;
            _sx[i] += (MathF.Sin(_time * 0.9f + _sPhase[i]) * 0.012f + _wind * _sSize[i]) * dt;
            if (_sy[i] > 1.02f) { _sy[i] = -0.02f; _sx[i] = (float)_rng.NextDouble(); RandomizeFlake(i); }
            if (_sx[i] < -0.02f) _sx[i] += 1.04f;
            else if (_sx[i] > 1.02f) _sx[i] -= 1.04f;
        }
    }

    /// <param name="seed">Offsets the pattern so two windows don't show identical snow.</param>
    private void DrawSnow(SKCanvas c, SKRect area, int count, float seed, float scale, float alpha)
    {
        float off = seed * 0.0137f;
        for (int i = 0; i < count; i++)
        {
            float x = (_sx[i] + off) % 1f;
            float r = _sSize[i] * scale * (area.Width < 400 * scale ? 0.8f : 1f);
            _snow.Color = SKColors.White.WithAlpha((byte)((110 + 50 * _sSize[i]) * alpha));
            c.DrawCircle(area.Left + x * area.Width, area.Top + _sy[i] * area.Height, r, _snow);
        }
    }

    private void RandomizeFlake(int i)
    {
        // Bigger flakes are "closer": they fall faster and drift more.
        _sSize[i] = 0.8f + 2.2f * (float)_rng.NextDouble();
        _sSpeed[i] = 0.03f + 0.035f * _sSize[i];
        _sPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
    }

    // Typed wrappers: with collection expressions the SKColor[] and SKColorF[] overloads are otherwise ambiguous.
    private static SKShader Linear(SKPoint a, SKPoint b, SKColor[] colors, float[]? pos, SKShaderTileMode mode) =>
        SKShader.CreateLinearGradient(a, b, colors, pos, mode);

    private static SKShader Radial(SKPoint c, float r, SKColor[] colors, float[]? pos, SKShaderTileMode mode) =>
        SKShader.CreateRadialGradient(c, r, colors, pos, mode);

    private static float Quad(float a, float b, float c, float t) =>
        (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

    private static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t));
}