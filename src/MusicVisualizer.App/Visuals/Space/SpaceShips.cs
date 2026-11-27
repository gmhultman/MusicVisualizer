using SkiaSharp;

namespace MusicVisualizer.Visuals.Space;

internal enum EnemyKind { Claw, Manta }

/// <summary>
/// Vector art for the Space scene's ships, seen from above. Geometry is in units of the ship's length with the
/// nose at +x (the canvas is transformed per ship), built once; colour shaders are rebuilt only when the palette
/// changes, so drawing a ship allocates nothing.
/// </summary>
internal sealed class SpaceShips : IDisposable
{
    // ---- hero geometry (nose at +0.52)
    private static readonly SKPath HeroFuselage = BuildHeroFuselage();
    private static readonly SKPath HeroWings = BuildHeroWings();
    private static readonly SKPath HeroCanards = BuildHeroCanards();
    private static readonly SKPath HeroStripes = BuildHeroStripes();
    private static readonly SKPath HeroEngineBlock = BuildRect(-0.47f, -0.085f, -0.36f, 0.085f);
    private static readonly SKPath HeroPanelLines = BuildHeroPanelLines();
    private static readonly SKRect HeroCanopy = new(0.11f, -0.04f, 0.34f, 0.04f);

    /// <summary>Wing gun muzzles (ship units).</summary>
    public static readonly SKPoint[] HeroGuns = [new(0.0f, -0.24f), new(0.0f, 0.24f)];
    public static readonly SKPoint[] HeroWingTips = [new(-0.31f, -0.385f), new(-0.31f, 0.385f)];
    private static readonly SKPoint[] HeroNozzles = [new(-0.47f, -0.045f), new(-0.47f, 0.045f)];

    // ---- enemies
    private static readonly SKPath ClawCore = BuildClawCore();
    private static readonly SKPath ClawProngs = BuildClawProngs();
    private static readonly SKPath ClawFins = BuildClawFins();
    public static readonly SKPoint[] ClawGuns = [new(0.5f, -0.2f), new(0.5f, 0.2f)];
    private static readonly SKPoint[] ClawNozzles = [new(-0.45f, 0f)];

    private static readonly SKPath MantaBody = BuildMantaBody();
    private static readonly SKPath MantaSpine = BuildMantaSpine();
    private static readonly SKPath MantaEyes = BuildMantaEyes();
    public static readonly SKPoint[] MantaGuns = [new(0.3f, -0.13f), new(0.3f, 0.13f)];
    private static readonly SKPoint[] MantaNozzles = [new(-0.42f, -0.035f), new(-0.42f, 0.035f)];

    /// <summary>Unit flame: nozzle at x = 0, tip at x = -1, half-width 0.5 at the nozzle.</summary>
    private static readonly SKPath Flame = BuildFlame();

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _plus = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };

    private SKShader? _heroHull, _heroWing, _heroGlass, _heroFlame, _enemyHull, _enemySpine, _enemyFlame;
    private Palette? _palette;

    public SKColor HeroColor { get; private set; }
    public SKColor HeroAccent { get; private set; }
    public SKColor HeroLaser { get; private set; }
    public SKColor EnemyColor { get; private set; }
    public SKColor EnemyHullDark { get; private set; }
    public SKColor EnemyHull { get; private set; }
    public SKColor HeroHullDark { get; private set; }
    private SKColor _heroOutline, _enemyOutline, _heroEngine, _enemyEngine;

    public void SetPalette(Palette palette)
    {
        if (ReferenceEquals(palette, _palette)) return;
        _palette = palette;
        var cols = palette.Colors;
        HeroColor = cols[0];
        // Stripes: the palette colour most different from the hull. Enemies: likewise, so the sides read apart.
        HeroAccent = Farthest(cols, HeroColor, cols[1]);
        EnemyColor = HeroAccent;
        // If the palette only has two hues, give the hero a lighter stripe instead of the enemy colour.
        if (Dist(HeroAccent, HeroColor) < 60 * 60) EnemyColor = HeroAccent = ColorUtil.Mix(HeroColor, SKColors.White, 0.6f);
        if (cols.Length > 2) HeroAccent = Farthest(cols, HeroColor, cols[1], EnemyColor);

        var hullLight = ColorUtil.Mix(HeroColor, SKColors.White, 0.55f);
        var hullMid = ColorUtil.Mix(HeroColor, new SKColor(0x9A, 0xA4, 0xB4), 0.45f);
        HeroHullDark = ColorUtil.Darken(ColorUtil.Mix(HeroColor, new SKColor(0x50, 0x58, 0x68), 0.4f), 0.45f);
        _heroOutline = ColorUtil.Darken(HeroColor, 0.86f);
        HeroLaser = ColorUtil.Mix(HeroColor, SKColors.White, 0.25f);
        _heroEngine = ColorUtil.Mix(HeroColor, SKColors.White, 0.15f);

        // Gunmetal tinted with the enemy colour: darker than the hero, but never so dark it vanishes in space.
        var eDark = ColorUtil.Darken(ColorUtil.Mix(EnemyColor, new SKColor(0x40, 0x42, 0x4C), 0.5f), 0.25f);
        var eLight = ColorUtil.Mix(ColorUtil.Mix(EnemyColor, new SKColor(0x8A, 0x8C, 0x98), 0.55f), SKColors.White, 0.22f);
        EnemyHullDark = ColorUtil.Darken(eDark, 0.3f);
        EnemyHull = ColorUtil.Mix(eLight, eDark, 0.4f);
        _enemyOutline = ColorUtil.Darken(EnemyColor, 0.9f);
        _enemyEngine = ColorUtil.Mix(EnemyColor, SKColors.White, 0.1f);

        DisposeShaders();
        _heroHull = SKShader.CreateLinearGradient(new SKPoint(0, -0.11f), new SKPoint(0, 0.11f),
            [hullLight, hullMid, HeroHullDark], [0f, 0.45f, 1f], SKShaderTileMode.Clamp);
        _heroWing = SKShader.CreateLinearGradient(new SKPoint(0, -0.42f), new SKPoint(0, 0.42f),
            [ColorUtil.Mix(hullMid, SKColors.White, 0.25f), HeroHullDark], (float[]?)null, SKShaderTileMode.Clamp);
        var glass = ColorUtil.Mix(HeroAccent, new SKColor(0x9C, 0xE8, 0xFF), 0.5f);
        _heroGlass = SKShader.CreateLinearGradient(new SKPoint(0.16f, -0.045f), new SKPoint(0.28f, 0.045f),
            [ColorUtil.Mix(glass, SKColors.White, 0.7f), glass, ColorUtil.Darken(glass, 0.7f)], [0f, 0.3f, 1f], SKShaderTileMode.Clamp);
        _heroFlame = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(-1, 0),
            [SKColors.White, _heroEngine, _heroEngine.WithAlpha(0)], [0f, 0.22f, 1f], SKShaderTileMode.Clamp);
        _enemyHull = SKShader.CreateLinearGradient(new SKPoint(0, -0.45f), new SKPoint(0, 0.45f),
            [eLight, eDark, EnemyHullDark], [0f, 0.5f, 1f], SKShaderTileMode.Clamp);
        _enemySpine = SKShader.CreateLinearGradient(new SKPoint(0, -0.12f), new SKPoint(0, 0.12f),
            [ColorUtil.Mix(eLight, SKColors.White, 0.25f), eDark], (float[]?)null, SKShaderTileMode.Clamp);
        _enemyFlame = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(-1, 0),
            [SKColors.White, _enemyEngine, _enemyEngine.WithAlpha(0)], [0f, 0.25f, 1f], SKShaderTileMode.Clamp);
    }

    // ------------------------------------------------------------------ hero

    /// <param name="thrust">0..1+, engine flame length/brightness.</param>
    /// <param name="scaleY">Width scale for banking and barrel rolls: 1 = flat, near 0 = on its side, negative = upside down.</param>
    /// <param name="shield">0..1 shield flash after a hit.</param>
    public void DrawHero(SKCanvas c, SKCanvas? g, float x, float y, float angle, float scaleY, float L, float thrust,
                         float shield, float pxPerDip, bool overlay)
    {
        float squash = MathF.Abs(scaleY) < 0.06f ? (scaleY < 0 ? -0.06f : 0.06f) : scaleY;
        float unit = pxPerDip / L; // one DIP in ship units
        c.Save();
        c.Translate(x, y);
        c.RotateRadians(angle);
        c.Scale(L, L * squash);

        // Flames behind the hull.
        float flameLen = 0.16f + 0.42f * thrust;
        foreach (var n in HeroNozzles) DrawFlame(c, n, flameLen, 0.065f + 0.02f * MathF.Min(thrust, 1.5f), _heroFlame!);

        // Overlay: a soft dark rim so the ship separates from any desktop.
        if (overlay)
        {
            _stroke.Shader = null;
            _stroke.Color = new SKColor(0, 0, 0, 90);
            _stroke.StrokeWidth = 4.5f * unit;
            c.DrawPath(HeroWings, _stroke);
            c.DrawPath(HeroFuselage, _stroke);
        }

        _stroke.Color = _heroOutline;
        _stroke.StrokeWidth = 1.6f * unit;
        _fill.Color = SKColors.White;
        _fill.Shader = _heroWing;
        c.DrawPath(HeroWings, _fill);
        c.DrawPath(HeroCanards, _fill);
        _fill.Shader = null;
        c.DrawPath(HeroWings, _stroke);
        c.DrawPath(HeroCanards, _stroke);

        _fill.Color = HeroAccent;
        c.DrawPath(HeroStripes, _fill);

        _fill.Color = ColorUtil.Darken(HeroHullDark, 0.4f);
        c.DrawPath(HeroEngineBlock, _fill);

        _fill.Color = SKColors.White;
        _fill.Shader = _heroHull;
        c.DrawPath(HeroFuselage, _fill);
        _fill.Shader = null;
        c.DrawPath(HeroFuselage, _stroke);

        _stroke.Color = _heroOutline.WithAlpha(150);
        _stroke.StrokeWidth = 1f * unit;
        c.DrawPath(HeroPanelLines, _stroke);

        // Canopy
        _fill.Shader = _heroGlass;
        c.DrawOval(HeroCanopy, _fill);
        _fill.Shader = null;
        _stroke.Color = _heroOutline;
        _stroke.StrokeWidth = 1.2f * unit;
        c.DrawOval(HeroCanopy, _stroke);

        // Nozzles
        _fill.Color = ColorUtil.Mix(_heroEngine, SKColors.White, 0.5f);
        foreach (var n in HeroNozzles) c.DrawCircle(n.X + 0.005f, n.Y, 0.028f, _fill);

        // Wing-tip running lights blink.
        _fill.Color = HeroAccent;
        foreach (var t in HeroWingTips) c.DrawCircle(t.X, t.Y, 0.018f, _fill);

        if (shield > 0.01f)
        {
            _plus.Shader = null;
            _plus.Color = HeroLaser.WithAlpha((byte)(40 * shield));
            c.DrawOval(new SKRect(-0.6f, -0.52f, 0.68f, 0.52f), _plus);
            _stroke.Color = ColorUtil.Mix(HeroLaser, SKColors.White, 0.5f).WithAlpha((byte)(230 * shield));
            _stroke.StrokeWidth = 2.2f * unit;
            c.DrawOval(new SKRect(-0.6f, -0.52f, 0.68f, 0.52f), _stroke);
        }
        c.Restore();

        if (g != null)
        {
            g.Save();
            g.Translate(x, y);
            g.RotateRadians(angle);
            g.Scale(L, L * squash);
            _fill.Color = _heroEngine.WithAlpha((byte)Math.Clamp(130 + 110 * thrust, 0, 255));
            g.DrawOval(new SKRect(-0.5f - flameLen * 1.1f, -0.13f, -0.38f, 0.13f), _fill);
            _fill.Color = HeroAccent.WithAlpha(160);
            foreach (var t in HeroWingTips) g.DrawCircle(t.X, t.Y, 0.05f, _fill);
            if (shield > 0.01f)
            {
                _fill.Color = HeroLaser.WithAlpha((byte)(170 * shield));
                g.DrawOval(new SKRect(-0.6f, -0.55f, 0.7f, 0.55f), _fill);
            }
            g.Restore();
        }
    }

    // ------------------------------------------------------------------ enemies

    /// <param name="facing">+1 nose right, -1 nose left (values in between squash through a turn).</param>
    /// <param name="hit">0..1 white flash after taking a hit.</param>
    /// <param name="charge">0..1 how bright the eye/core glows.</param>
    public void DrawEnemy(SKCanvas c, SKCanvas? g, EnemyKind kind, float x, float y, float angle, float facing,
                          float size, float thrust, float hit, float charge, float alpha, float pxPerDip, bool overlay)
    {
        float unit = pxPerDip / size;
        float fx = MathF.Abs(facing) < 0.08f ? 0.08f * MathF.Sign(facing == 0 ? 1 : facing) : facing;
        var body = kind == EnemyKind.Claw ? ClawCore : MantaBody;
        byte a = (byte)(255 * Math.Clamp(alpha, 0f, 1f));

        c.Save();
        c.Translate(x, y);
        c.RotateRadians(angle);
        c.Scale(size * fx, size);

        float flameLen = 0.12f + 0.3f * thrust;
        foreach (var n in kind == EnemyKind.Claw ? ClawNozzles : MantaNozzles)
            DrawFlame(c, n, flameLen, kind == EnemyKind.Claw ? 0.08f : 0.05f, _enemyFlame!);

        if (overlay)
        {
            _stroke.Color = new SKColor(0, 0, 0, (byte)(90 * alpha));
            _stroke.StrokeWidth = 4.5f * unit;
            c.DrawPath(body, _stroke);
            if (kind == EnemyKind.Claw) c.DrawPath(ClawProngs, _stroke);
        }

        _fill.Color = SKColors.White.WithAlpha(a);
        _fill.Shader = _enemyHull;
        _stroke.Color = _enemyOutline.WithAlpha(a);
        _stroke.StrokeWidth = 1.6f * unit;
        if (kind == EnemyKind.Claw)
        {
            c.DrawPath(ClawFins, _fill);
            c.DrawPath(ClawProngs, _fill);
            c.DrawPath(ClawCore, _fill);
            _fill.Shader = null;
            c.DrawPath(ClawFins, _stroke);
            c.DrawPath(ClawProngs, _stroke);
            c.DrawPath(ClawCore, _stroke);
        }
        else
        {
            c.DrawPath(MantaBody, _fill);
            _fill.Shader = null;
            c.DrawPath(MantaBody, _stroke);
            _fill.Shader = _enemySpine;
            c.DrawPath(MantaSpine, _fill);
            _fill.Shader = null;
            _stroke.StrokeWidth = 1.1f * unit;
            c.DrawPath(MantaSpine, _stroke);
        }

        // Glowing eye(s) in the enemy colour.
        var eye = ColorUtil.Mix(EnemyColor, SKColors.White, 0.25f + 0.5f * charge).WithAlpha(a);
        _fill.Color = eye;
        if (kind == EnemyKind.Claw)
        {
            c.DrawCircle(0.1f, 0f, 0.058f, _fill);
            _fill.Color = SKColors.White.WithAlpha(a);
            c.DrawCircle(0.11f, 0f, 0.024f, _fill);
            _fill.Color = eye;
            foreach (var p in ClawGuns) c.DrawCircle(p.X - 0.02f, p.Y * 0.98f, 0.02f, _fill);
        }
        else
        {
            c.DrawPath(MantaEyes, _fill);
        }

        if (hit > 0.01f)
        {
            _fill.Color = SKColors.White.WithAlpha((byte)(200 * hit * alpha));
            c.DrawPath(body, _fill);
            if (kind == EnemyKind.Claw) c.DrawPath(ClawProngs, _fill);
        }
        c.Restore();

        if (g != null)
        {
            g.Save();
            g.Translate(x, y);
            g.RotateRadians(angle);
            g.Scale(size * fx, size);
            _fill.Color = _enemyEngine.WithAlpha((byte)(Math.Clamp(110 + 120 * thrust, 0, 255) * alpha));
            if (kind == EnemyKind.Claw) g.DrawOval(new SKRect(-0.47f - flameLen, -0.1f, -0.38f, 0.1f), _fill);
            else g.DrawOval(new SKRect(-0.45f - flameLen, -0.09f, -0.36f, 0.09f), _fill);
            _fill.Color = EnemyColor.WithAlpha((byte)((90 + 140 * charge) * alpha));
            if (kind == EnemyKind.Claw) g.DrawCircle(0.1f, 0, 0.14f, _fill);
            else { g.DrawCircle(0.2f, -0.09f, 0.09f, _fill); g.DrawCircle(0.2f, 0.09f, 0.09f, _fill); }
            if (hit > 0.01f)
            {
                _fill.Color = SKColors.White.WithAlpha((byte)(160 * hit * alpha));
                g.DrawCircle(0, 0, 0.45f, _fill);
            }
            g.Restore();
        }
    }

    public static SKPoint[] GunsOf(EnemyKind kind) => kind == EnemyKind.Claw ? ClawGuns : MantaGuns;

    private void DrawFlame(SKCanvas c, SKPoint nozzle, float length, float width, SKShader shader)
    {
        c.Save();
        c.Translate(nozzle.X, nozzle.Y);
        c.Scale(length, width * 2f);
        _plus.Color = SKColors.White;
        _plus.Shader = shader;
        c.DrawPath(Flame, _plus);
        _plus.Shader = null;
        c.Restore();
    }

    // ------------------------------------------------------------------ geometry

    private static SKPath BuildHeroFuselage()
    {
        var p = new SKPath();
        p.MoveTo(0.53f, 0);
        p.CubicTo(0.46f, -0.04f, 0.32f, -0.07f, 0.12f, -0.082f);
        p.LineTo(-0.3f, -0.095f);
        p.LineTo(-0.4f, -0.08f);
        p.LineTo(-0.44f, -0.05f);
        p.LineTo(-0.44f, 0.05f);
        p.LineTo(-0.4f, 0.08f);
        p.LineTo(-0.3f, 0.095f);
        p.LineTo(0.12f, 0.082f);
        p.CubicTo(0.32f, 0.07f, 0.46f, 0.04f, 0.53f, 0);
        p.Close();
        return p;
    }

    private static SKPath BuildHeroWings()
    {
        var p = new SKPath();
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            p.MoveTo(0.14f, 0.07f * sgn);
            p.LineTo(0.02f, 0.2f * sgn);
            p.LineTo(-0.2f, 0.36f * sgn);
            p.LineTo(-0.3f, 0.4f * sgn);
            p.LineTo(-0.34f, 0.37f * sgn);
            p.LineTo(-0.3f, 0.2f * sgn);
            p.LineTo(-0.38f, 0.14f * sgn);
            p.LineTo(-0.36f, 0.08f * sgn);
            p.Close();
        }
        return p;
    }

    private static SKPath BuildHeroCanards()
    {
        var p = new SKPath();
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            p.MoveTo(0.34f, 0.05f * sgn);
            p.LineTo(0.24f, 0.13f * sgn);
            p.LineTo(0.2f, 0.125f * sgn);
            p.LineTo(0.24f, 0.06f * sgn);
            p.Close();
        }
        return p;
    }

    private static SKPath BuildHeroStripes()
    {
        var p = new SKPath();
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            // Along the wing's leading edge.
            p.MoveTo(0.06f, 0.155f * sgn);
            p.LineTo(-0.2f, 0.335f * sgn);
            p.LineTo(-0.23f, 0.33f * sgn);
            p.LineTo(0.03f, 0.15f * sgn);
            p.Close();
            // Tip block.
            p.MoveTo(-0.24f, 0.365f * sgn);
            p.LineTo(-0.3f, 0.395f * sgn);
            p.LineTo(-0.325f, 0.375f * sgn);
            p.LineTo(-0.315f, 0.33f * sgn);
            p.Close();
        }
        // Nose stripe.
        p.MoveTo(0.44f, -0.012f);
        p.LineTo(0.36f, -0.03f);
        p.LineTo(0.36f, 0.03f);
        p.LineTo(0.44f, 0.012f);
        p.Close();
        return p;
    }

    private static SKPath BuildHeroPanelLines()
    {
        var p = new SKPath();
        p.MoveTo(0.08f, -0.06f); p.LineTo(-0.36f, -0.06f);
        p.MoveTo(0.08f, 0.06f); p.LineTo(-0.36f, 0.06f);
        p.MoveTo(-0.1f, -0.06f); p.LineTo(-0.1f, 0.06f);
        p.MoveTo(-0.26f, -0.06f); p.LineTo(-0.26f, 0.06f);
        return p;
    }

    private static SKPath BuildRect(float l, float t, float r, float b)
    {
        var p = new SKPath();
        p.AddRoundRect(new SKRect(l, t, r, b), 0.02f, 0.02f);
        return p;
    }

    private static SKPath BuildClawCore()
    {
        var p = new SKPath();
        p.MoveTo(0.3f, 0);
        p.LineTo(0.06f, -0.13f);
        p.LineTo(-0.3f, -0.11f);
        p.LineTo(-0.44f, -0.05f);
        p.LineTo(-0.44f, 0.05f);
        p.LineTo(-0.3f, 0.11f);
        p.LineTo(0.06f, 0.13f);
        p.Close();
        return p;
    }

    private static SKPath BuildClawProngs()
    {
        var p = new SKPath();
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            p.MoveTo(-0.2f, 0.09f * sgn);
            p.CubicTo(-0.08f, 0.44f * sgn, 0.3f, 0.47f * sgn, 0.53f, 0.2f * sgn);
            p.CubicTo(0.3f, 0.3f * sgn, 0.12f, 0.27f * sgn, 0.03f, 0.11f * sgn);
            p.Close();
        }
        return p;
    }

    private static SKPath BuildClawFins()
    {
        var p = new SKPath();
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            p.MoveTo(-0.24f, 0.1f * sgn);
            p.LineTo(-0.48f, 0.26f * sgn);
            p.LineTo(-0.4f, 0.07f * sgn);
            p.Close();
        }
        return p;
    }

    private static SKPath BuildMantaBody()
    {
        var p = new SKPath();
        p.MoveTo(0.4f, 0);
        p.CubicTo(0.34f, -0.1f, 0.16f, -0.36f, -0.18f, -0.5f);
        p.LineTo(-0.13f, -0.32f);
        p.LineTo(-0.3f, -0.25f);
        p.LineTo(-0.22f, -0.13f);
        p.LineTo(-0.42f, -0.07f);
        p.LineTo(-0.42f, 0.07f);
        p.LineTo(-0.22f, 0.13f);
        p.LineTo(-0.3f, 0.25f);
        p.LineTo(-0.13f, 0.32f);
        p.LineTo(-0.18f, 0.5f);
        p.CubicTo(0.16f, 0.36f, 0.34f, 0.1f, 0.4f, 0);
        p.Close();
        return p;
    }

    private static SKPath BuildMantaSpine()
    {
        var p = new SKPath();
        p.MoveTo(0.36f, 0);
        p.LineTo(0.04f, -0.075f);
        p.LineTo(-0.38f, -0.045f);
        p.LineTo(-0.38f, 0.045f);
        p.LineTo(0.04f, 0.075f);
        p.Close();
        return p;
    }

    private static SKPath BuildMantaEyes()
    {
        var p = new SKPath();
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            p.MoveTo(0.3f, 0.06f * sgn);
            p.LineTo(0.13f, 0.14f * sgn);
            p.LineTo(0.12f, 0.1f * sgn);
            p.Close();
        }
        return p;
    }

    private static SKPath BuildFlame()
    {
        var p = new SKPath();
        p.MoveTo(0, -0.5f);
        p.CubicTo(-0.3f, -0.45f, -0.7f, -0.15f, -1f, 0);
        p.CubicTo(-0.7f, 0.15f, -0.3f, 0.45f, 0, 0.5f);
        p.Close();
        return p;
    }

    private static int Dist(SKColor a, SKColor b)
    {
        int dr = a.Red - b.Red, dg = a.Green - b.Green, db = a.Blue - b.Blue;
        return dr * dr + dg * dg + db * db;
    }

    private static SKColor Farthest(SKColor[] cols, SKColor from, SKColor fallback, SKColor? avoid = null)
    {
        SKColor best = fallback;
        int bestD = -1;
        foreach (var c in cols)
        {
            if (avoid is { } av && c == av) continue;
            int d = Dist(c, from);
            if (d > bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private void DisposeShaders()
    {
        _heroHull?.Dispose(); _heroWing?.Dispose(); _heroGlass?.Dispose(); _heroFlame?.Dispose();
        _enemyHull?.Dispose(); _enemySpine?.Dispose(); _enemyFlame?.Dispose();
    }

    public void Dispose()
    {
        DisposeShaders();
        _fill.Dispose(); _stroke.Dispose(); _plus.Dispose();
    }
}
