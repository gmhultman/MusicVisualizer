using SkiaSharp;

namespace MusicVisualizer.Visuals.Aquarium;

internal enum FishKind { Minnow, Tetra, Angel, Clown, Tang, Grouper }

/// <summary>
/// One fish silhouette, built once at unit size (length 1, nose at +x, centred on the origin) so drawing
/// is just a transform plus a handful of cached paths.
/// </summary>
internal sealed class FishShape
{
    public SKPath Body { get; } = new();
    public SKPath Belly { get; } = new();
    public SKPath? Stripes { get; private set; }
    public SKPath Tail { get; } = new();          // drawn rotated about (TailPivot, 0)
    public SKPath Fins { get; } = new();          // dorsal + ventral, drawn behind the body
    public SKPath Pectoral { get; } = new();      // small side fin, drawn on top of the body
    public float TailPivot { get; private set; }
    public SKPoint Eye { get; private set; }
    public float EyeR { get; private set; }

    private static readonly FishShape[] Cache = Build();

    public static FishShape Get(FishKind kind) => Cache[(int)kind];

    private static FishShape[] Build()
    {
        var all = new FishShape[6];
        all[(int)FishKind.Minnow] = Make(bh: 0.12f, tb: -0.26f, tail: 0.15f, dorsal: 0.6f, ventral: 0f, stripes: StripeKind.None);
        all[(int)FishKind.Tetra] = Make(bh: 0.15f, tb: -0.25f, tail: 0.17f, dorsal: 0.8f, ventral: 0.3f, stripes: StripeKind.Lateral);
        all[(int)FishKind.Angel] = Make(bh: 0.36f, tb: -0.2f, tail: 0.2f, dorsal: 2.1f, ventral: 2.1f, stripes: StripeKind.Vertical3, nose: 0.42f);
        all[(int)FishKind.Clown] = Make(bh: 0.22f, tb: -0.25f, tail: 0.2f, dorsal: 0.9f, ventral: 0.4f, stripes: StripeKind.Clown);
        all[(int)FishKind.Tang] = Make(bh: 0.3f, tb: -0.24f, tail: 0.24f, dorsal: 0.5f, ventral: 0.4f, stripes: StripeKind.Swoosh);
        all[(int)FishKind.Grouper] = Make(bh: 0.19f, tb: -0.28f, tail: 0.17f, dorsal: 0.7f, ventral: 0.3f, stripes: StripeKind.Spots);
        return all;
    }

    private enum StripeKind { None, Lateral, Vertical3, Clown, Swoosh, Spots }

    private static FishShape Make(float bh, float tb, float tail, float dorsal, float ventral, StripeKind stripes, float nose = 0.5f)
    {
        var f = new FishShape { TailPivot = tb };

        // Body: blunt rounded head, widest a little ahead of the middle, narrowing to the tail stalk.
        var b = f.Body;
        b.MoveTo(nose, 0.02f * bh);
        b.CubicTo(nose - 0.06f, -bh * 0.95f, 0.12f, -bh * 1.05f, -0.03f, -bh);
        b.CubicTo(-0.13f, -bh * 0.95f, tb + 0.06f, -bh * 0.4f, tb, -bh * 0.17f - 0.012f);
        b.LineTo(tb, bh * 0.17f + 0.012f);
        b.CubicTo(tb + 0.06f, bh * 0.4f, -0.13f, bh * 0.92f, -0.03f, bh * 0.95f);
        b.CubicTo(0.14f, bh * 1.0f, nose - 0.06f, bh * 0.8f, nose, 0.02f * bh);
        b.Close();

        using (var lower = new SKPath())
        {
            lower.AddOval(new SKRect(-0.4f, bh * 0.05f, nose + 0.1f, bh * 2.6f));
            f.Belly.AddPath(b.Op(lower, SKPathOp.Intersect) ?? new SKPath());
        }

        // Forked tail, pivoting at the tail stalk.
        var t = f.Tail;
        float tip = -0.5f;
        t.MoveTo(tb + 0.05f, -bh * 0.12f);
        t.CubicTo(tb - 0.05f, -tail * 0.5f, tip + 0.02f, -tail * 1.0f, tip - 0.01f, -tail * 1.1f);
        t.QuadTo(tip + 0.09f, 0, tip - 0.01f, tail * 1.1f);
        t.CubicTo(tip + 0.02f, tail * 1.0f, tb - 0.05f, tail * 0.5f, tb + 0.05f, bh * 0.12f);
        t.Close();

        // Dorsal / ventral fins (swept back).
        if (dorsal > 0)
        {
            f.Fins.MoveTo(0.14f, -bh * 0.9f);
            f.Fins.QuadTo(-0.02f, -bh * (1f + dorsal * 0.55f), -0.16f - dorsal * 0.04f, -bh * (1f + dorsal * 0.6f));
            f.Fins.QuadTo(-0.12f, -bh * 1.0f, -0.17f, -bh * 0.7f);
            f.Fins.Close();
        }
        if (ventral > 0)
        {
            f.Fins.MoveTo(0.06f, bh * 0.85f);
            f.Fins.QuadTo(-0.04f, bh * (1f + ventral * 0.55f), -0.15f - ventral * 0.04f, bh * (1f + ventral * 0.6f));
            f.Fins.QuadTo(-0.1f, bh * 1.0f, -0.15f, bh * 0.72f);
            f.Fins.Close();
        }

        f.Pectoral.MoveTo(0.2f, bh * 0.2f);
        f.Pectoral.QuadTo(0.06f, bh * 0.25f, 0.03f, bh * 0.62f);
        f.Pectoral.QuadTo(0.13f, bh * 0.5f, 0.2f, bh * 0.2f);
        f.Pectoral.Close();

        f.Eye = new SKPoint(nose - 0.15f, -bh * 0.22f);
        f.EyeR = MathF.Max(0.028f, bh * 0.16f);

        using var band = new SKPath();
        switch (stripes)
        {
            case StripeKind.Lateral:   // neon-tetra line from eye to tail
                band.MoveTo(nose - 0.12f, -bh * 0.16f);
                band.QuadTo(0.0f, -bh * 0.3f, tb - 0.02f, -bh * 0.06f);
                band.LineTo(tb - 0.02f, bh * 0.14f);
                band.QuadTo(0.0f, bh * 0.02f, nose - 0.12f, bh * 0.08f);
                band.Close();
                break;
            case StripeKind.Vertical3:
                for (int i = 0; i < 3; i++)
                {
                    float x = 0.26f - i * 0.18f;
                    band.AddRect(new SKRect(x - 0.035f, -1, x + 0.035f, 1));
                }
                break;
            case StripeKind.Clown:
                band.AddOval(new SKRect(0.15f, -bh * 1.3f, 0.29f, bh * 1.3f));
                band.AddOval(new SKRect(-0.07f, -bh * 1.4f, 0.05f, bh * 1.4f));
                band.AddRect(new SKRect(tb - 0.02f, -1, tb + 0.05f, 1));
                break;
            case StripeKind.Swoosh:
                band.MoveTo(nose - 0.05f, -bh * 0.55f);
                band.CubicTo(0.2f, -bh * 0.1f, -0.05f, -bh * 0.75f, tb - 0.05f, -bh * 0.1f);
                band.LineTo(tb - 0.05f, bh * 0.05f);
                band.CubicTo(-0.05f, -bh * 0.45f, 0.2f, bh * 0.15f, nose - 0.05f, -bh * 0.25f);
                band.Close();
                break;
            case StripeKind.Spots:
                var rng = new Random(3);
                for (int i = 0; i < 14; i++)
                {
                    float x = 0.33f - (float)rng.NextDouble() * 0.55f, y = ((float)rng.NextDouble() * 2 - 1) * bh * 0.8f;
                    float r = bh * (0.08f + 0.06f * (float)rng.NextDouble());
                    band.AddCircle(x, y, r);
                }
                break;
        }
        if (stripes != StripeKind.None) f.Stripes = b.Op(band, SKPathOp.Intersect);
        return f;
    }
}

/// <summary>Colours for one fish, derived from the palette when the palette changes.</summary>
internal readonly record struct FishColors(SKColor Body, SKColor Belly, SKColor Stripe, SKColor Fin, SKColor Outline)
{
    public static FishColors From(SKColor baseColor, SKColor stripe)
    {
        var body = baseColor;
        return new FishColors(
            body,
            ColorUtil.Mix(body, SKColors.White, 0.45f),
            stripe,
            ColorUtil.Mix(body, stripe, 0.3f).WithAlpha(200),
            ColorUtil.Darken(body, 0.65f).WithAlpha(150));
    }
}

internal static class FishPainter
{
    private static readonly SKColor Pupil = new(0x08, 0x0A, 0x14);

    /// <param name="angle">Heading in radians (0 = right). Fish heading left are mirrored so they stay upright.</param>
    /// <param name="tail">Tail swing in radians.</param>
    /// <param name="detail">False for tiny fish: skips stripes, fins and eye highlight.</param>
    public static void Draw(SKCanvas c, FishShape f, float x, float y, float angle, float len, float tail,
                            in FishColors col, SKPaint fill, SKPaint outline, bool detail, float outlineDip)
    {
        c.Save();
        c.Translate(x, y);
        c.RotateRadians(angle);
        bool flip = MathF.Cos(angle) < 0;
        c.Scale(len, flip ? -len : len);

        fill.Color = col.Fin;
        if (detail) c.DrawPath(f.Fins, fill);

        c.Save();
        c.Translate(f.TailPivot, 0);
        c.RotateRadians(tail);
        c.Translate(-f.TailPivot, 0);
        c.DrawPath(f.Tail, fill);
        c.Restore();

        if (outlineDip > 0)
        {
            // Thin dark rim keeps fish readable on busy desktops (overlay mode).
            outline.StrokeWidth = outlineDip / len;
            outline.Color = col.Outline;
            c.DrawPath(f.Body, outline);
        }

        fill.Color = col.Body;
        c.DrawPath(f.Body, fill);
        fill.Color = col.Belly;
        c.DrawPath(f.Belly, fill);
        if (f.Stripes != null)
        {
            fill.Color = col.Stripe;
            c.DrawPath(f.Stripes, fill);
        }
        if (detail)
        {
            fill.Color = col.Fin;
            c.DrawPath(f.Pectoral, fill);
        }

        fill.Color = SKColors.White;
        c.DrawCircle(f.Eye, f.EyeR, fill);
        fill.Color = Pupil;
        c.DrawCircle(f.Eye.X + f.EyeR * 0.15f, f.Eye.Y, f.EyeR * 0.62f, fill);
        if (detail)
        {
            fill.Color = SKColors.White;
            c.DrawCircle(f.Eye.X + f.EyeR * 0.32f, f.Eye.Y - f.EyeR * 0.28f, f.EyeR * 0.22f, fill);
        }
        c.Restore();
    }
}
