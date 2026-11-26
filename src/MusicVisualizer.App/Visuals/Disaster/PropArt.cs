using System.Numerics;
using MusicVisualizer.Audio;
using SkiaSharp;

namespace MusicVisualizer.Visuals.Disaster;

internal enum PropKind
{
    Book, Frame, Can, Headphones, Plates, Pan, Jar, FlourBag, Fruit, Mug, Plant, FloorLamp, Lantern, Clock,
    Binder, Monitor, Present, Ornament, Tree, Stocking, Block, Ball, Bear, Pendant, Shelf, Trophy, Skis,
    Antlers, Wreath, Mobile, Candle, Vase, Bowl, Toy,
}

/// <summary>How a prop reacts to <see cref="Room.Rattle"/>.</summary>
internal enum RattleMode { None, Hop, Rock, Swing, Creep }

/// <summary>A movable thing in the room: its physics body plus how to draw it and where it lives.</summary>
internal sealed class Prop
{
    public required Body Body;
    public required PropKind Kind;
    public SKColor Color;
    public SKColor Accent;
    public int Seed;
    public string? Label;
    public RattleMode Rattle = RattleMode.Hop;
    public float CreepDir;                // -1/+1 for RattleMode.Creep
    public readonly List<Stick> Nails = [];
    public readonly List<Stick> SnapNails = [];   // shelves: the side that snaps
    public Vector2 Home;
    public float HomeAngle;
    public Vector2? RopeAnchor;          // drawn cord/wire for hanging props

    // tidy animation
    public Vector2 TidyFrom;
    public float TidyFromAngle;
    public float TidyDelay;
    public bool TidyActive;
    public bool TidyArrived;

    public bool Displaced => Vector2.Distance(Body.Center, Home) > 0.25f || MathF.Abs(AngleDiff(Body.Angle, HomeAngle)) > 0.35f;

    public static float AngleDiff(float a, float b)
    {
        float d = a - b;
        while (d > MathF.PI) d -= MathF.Tau;
        while (d < -MathF.PI) d += MathF.Tau;
        return d;
    }
}

/// <summary>Flat-vector artwork for props and furniture, drawn in world metres.</summary>
internal static class PropArt
{
    private static readonly SKPaint Fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private static readonly SKPaint Line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private static readonly SKPath Path = new();
    private static readonly SKFont Font = new(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 0.12f);

    public static SKColor Shade(SKColor c, float k) => new((byte)Math.Clamp(c.Red * k, 0, 255), (byte)Math.Clamp(c.Green * k, 0, 255), (byte)Math.Clamp(c.Blue * k, 0, 255), c.Alpha);

    private static void F(SKCanvas c, SKColor col, SKRect r, float radius = 0)
    {
        Fill.Shader = null;
        Fill.Color = col;
        if (radius > 0) c.DrawRoundRect(r, radius, radius, Fill); else c.DrawRect(r, Fill);
    }

    private static void L(SKCanvas c, SKColor col, float w, float x0, float y0, float x1, float y1)
    {
        Line.Color = col;
        Line.StrokeWidth = w;
        c.DrawLine(x0, y0, x1, y1, Line);
    }

    private static void Circle(SKCanvas c, SKColor col, float x, float y, float r)
    {
        Fill.Shader = null;
        Fill.Color = col;
        c.DrawCircle(x, y, r, Fill);
    }

    /// <summary>Draws a prop following its physics pose.</summary>
    public static void Draw(SKCanvas c, Prop p, float time, AudioFrame f, Palette pal)
    {
        var b = p.Body;
        var center = b.Center;

        // Cords/wires of hanging things (drawn in world space, before rotating into the prop's frame).
        if (p.RopeAnchor is { } anchor && p.Nails.Count > 0 && !p.Nails[0].Broken && b.Kind == BodyKind.Box)
        {
            Line.Color = new SKColor(0x2A, 0x22, 0x20, 220);
            Line.StrokeWidth = 0.012f;
            if (p.Kind is PropKind.Frame or PropKind.Antlers or PropKind.Wreath or PropKind.Clock)
            {
                c.DrawLine(anchor.X, anchor.Y, b.Points[0].Pos.X, b.Points[0].Pos.Y, Line);
                c.DrawLine(anchor.X, anchor.Y, b.Points[1].Pos.X, b.Points[1].Pos.Y, Line);
                Circle(c, new SKColor(0x6A, 0x5A, 0x4A), anchor.X, anchor.Y, 0.018f);
            }
            else
            {
                var top = (b.Points[0].Pos + b.Points[1].Pos) / 2;
                c.DrawLine(anchor.X, anchor.Y, top.X, top.Y, Line);
            }
        }

        c.Save();
        c.Translate(center.X, center.Y);
        c.RotateRadians(b.Angle);
        float w = b.Size.X, h = b.Size.Y, hw = w / 2, hh = h / 2;
        var col = p.Color;
        var acc = p.Accent;

        switch (p.Kind)
        {
            case PropKind.Book:
                F(c, col, new SKRect(-hw, -hh, hw, hh), 0.012f);
                F(c, Shade(col, 0.75f), new SKRect(-hw, -hh, -hw + w * 0.18f, hh), 0.01f);
                F(c, acc, new SKRect(-hw + w * 0.25f, -hh + h * 0.15f, hw - w * 0.15f, -hh + h * 0.22f));
                F(c, acc, new SKRect(-hw + w * 0.25f, hh - h * 0.22f, hw - w * 0.15f, hh - h * 0.15f));
                break;

            case PropKind.Frame:
            {
                F(c, Shade(col, 0.7f), new SKRect(-hw, -hh, hw, hh), 0.02f);
                F(c, col, new SKRect(-hw + 0.025f, -hh + 0.025f, hw - 0.025f, hh - 0.025f), 0.01f);
                var inner = new SKRect(-hw + 0.06f, -hh + 0.06f, hw - 0.06f, hh - 0.06f);
                using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, inner.Top), new SKPoint(0, inner.Bottom),
                           new[] { ColorUtil.Mix(acc, SKColors.White, 0.5f), acc }, null, SKShaderTileMode.Clamp))
                {
                    Fill.Shader = sky;
                    Fill.Color = SKColors.White;
                    c.DrawRect(inner, Fill);
                    Fill.Shader = null;
                }
                // little landscape: sun + hills
                Circle(c, new SKColor(0xFF, 0xE8, 0xA0), inner.Left + inner.Width * 0.72f, inner.Top + inner.Height * 0.32f, inner.Height * 0.14f);
                Path.Reset();
                Path.MoveTo(inner.Left, inner.Bottom);
                Path.QuadTo(inner.Left + inner.Width * 0.3f, inner.Top + inner.Height * 0.35f, inner.Left + inner.Width * 0.6f, inner.Bottom - inner.Height * 0.25f);
                Path.QuadTo(inner.Left + inner.Width * 0.8f, inner.Top + inner.Height * 0.45f, inner.Right, inner.Bottom - inner.Height * 0.15f);
                Path.LineTo(inner.Right, inner.Bottom);
                Path.Close();
                Fill.Color = Shade(acc, 0.55f);
                c.DrawPath(Path, Fill);
                break;
            }

            case PropKind.Can:
                F(c, col, new SKRect(-hw, -hh + 0.02f, hw, hh - 0.01f), 0.015f);
                F(c, new SKColor(0xC8, 0xCC, 0xD4), new SKRect(-hw * 0.9f, -hh, hw * 0.9f, -hh + 0.03f), 0.01f);
                F(c, acc, new SKRect(-hw, -hh * 0.2f, hw, hh * 0.25f));
                L(c, SKColors.White.WithAlpha(120), 0.012f, -hw * 0.5f, -hh * 0.7f, -hw * 0.5f, hh * 0.7f);
                break;

            case PropKind.Headphones:
            {
                Line.Color = Shade(col, 0.6f);
                Line.StrokeWidth = 0.035f;
                Line.Style = SKPaintStyle.Stroke;
                c.DrawArc(new SKRect(-hw * 0.85f, -hh, hw * 0.85f, hh * 1.4f), 180, 180, false, Line);
                F(c, col, new SKRect(-hw, 0, -hw + w * 0.28f, hh), 0.03f);
                F(c, col, new SKRect(hw - w * 0.28f, 0, hw, hh), 0.03f);
                F(c, acc, new SKRect(-hw + 0.02f, hh * 0.3f, -hw + w * 0.2f, hh * 0.8f), 0.02f);
                F(c, acc, new SKRect(hw - w * 0.2f, hh * 0.3f, hw - 0.02f, hh * 0.8f), 0.02f);
                break;
            }

            case PropKind.Plates:
            {
                int n = Math.Max(3, (int)(h / 0.035f));
                for (int i = 0; i < n; i++)
                {
                    float y = hh - (i + 1) * (h / n);
                    F(c, i % 2 == 0 ? new SKColor(0xF4, 0xF2, 0xEC) : new SKColor(0xDC, 0xE6, 0xF0), new SKRect(-hw, y, hw, y + h / n * 0.85f), 0.012f);
                    L(c, acc, 0.008f, -hw * 0.8f, y + h / n * 0.4f, hw * 0.8f, y + h / n * 0.4f);
                }
                break;
            }

            case PropKind.Pan:
            {
                F(c, new SKColor(0x2E, 0x2E, 0x34), new SKRect(-hw * 0.95f, -hh * 0.4f, hw * 0.4f, hh), 0.04f);
                F(c, Shade(col, 1.1f), new SKRect(-hw * 0.95f, -hh * 0.4f, hw * 0.4f, -hh * 0.2f), 0.01f);
                F(c, new SKColor(0x4A, 0x32, 0x22), new SKRect(hw * 0.35f, -hh * 0.35f, hw, -hh * 0.1f), 0.02f);
                Circle(c, new SKColor(0x7A, 0x7A, 0x80), -hw * 0.3f, -hh * 0.55f, 0.022f);
                break;
            }

            case PropKind.Jar:
                F(c, ColorUtil.Mix(col, SKColors.White, 0.35f).WithAlpha(235), new SKRect(-hw, -hh + h * 0.2f, hw, hh), 0.025f);
                F(c, col, new SKRect(-hw * 0.85f, -hh + h * 0.45f, hw * 0.85f, hh - 0.015f), 0.02f);
                F(c, acc, new SKRect(-hw * 0.9f, -hh, hw * 0.9f, -hh + h * 0.22f), 0.012f);
                F(c, new SKColor(0xF6, 0xEE, 0xD8), new SKRect(-hw * 0.7f, -hh + h * 0.5f, hw * 0.7f, -hh + h * 0.72f));
                break;

            case PropKind.FlourBag:
                Path.Reset();
                Path.MoveTo(-hw * 0.9f, -hh);
                Path.LineTo(hw * 0.9f, -hh);
                Path.LineTo(hw, hh);
                Path.LineTo(-hw, hh);
                Path.Close();
                Fill.Color = new SKColor(0xEE, 0xE4, 0xCC);
                c.DrawPath(Path, Fill);
                F(c, new SKColor(0xD6, 0xC8, 0xA8), new SKRect(-hw * 0.9f, -hh, hw * 0.9f, -hh + h * 0.15f));
                F(c, acc, new SKRect(-hw * 0.6f, -hh * 0.1f, hw * 0.6f, hh * 0.45f), 0.02f);
                break;

            case PropKind.Fruit:
            case PropKind.Ornament:
            case PropKind.Ball:
            {
                float r = b.Kind == BodyKind.Circle ? b.Radius : MathF.Min(hw, hh);
                Circle(c, col, 0, 0, r);
                if (p.Kind == PropKind.Ball)
                {
                    Fill.Color = acc;
                    c.DrawArc(new SKRect(-r, -r, r, r), -30, 120, true, Fill);
                    c.DrawArc(new SKRect(-r, -r, r, r), 150, 120, true, Fill);
                }
                else if (p.Kind == PropKind.Ornament)
                {
                    F(c, new SKColor(0xD8, 0xB8, 0x50), new SKRect(-r * 0.3f, -r * 1.2f, r * 0.3f, -r * 0.85f), 0.01f);
                    L(c, acc, r * 0.18f, -r * 0.9f, 0, r * 0.9f, 0);
                }
                else
                {
                    L(c, new SKColor(0x4A, 0x6A, 0x22), r * 0.15f, 0, -r, r * 0.25f, -r * 1.3f);
                }
                Circle(c, SKColors.White.WithAlpha(110), -r * 0.35f, -r * 0.35f, r * 0.28f);
                break;
            }

            case PropKind.Mug:
                F(c, col, new SKRect(-hw, -hh, hw * 0.6f, hh), 0.02f);
                Line.Color = col;
                Line.StrokeWidth = 0.022f;
                c.DrawArc(new SKRect(hw * 0.35f, -hh * 0.5f, hw, hh * 0.5f), -90, 180, false, Line);
                F(c, acc, new SKRect(-hw, -hh * 0.1f, hw * 0.6f, hh * 0.15f));
                break;

            case PropKind.Plant:
            {
                // pot in the lower third, leaves above
                float potTop = hh - h * 0.32f;
                Path.Reset();
                Path.MoveTo(-hw * 0.8f, potTop);
                Path.LineTo(hw * 0.8f, potTop);
                Path.LineTo(hw * 0.6f, hh);
                Path.LineTo(-hw * 0.6f, hh);
                Path.Close();
                Fill.Color = col;
                c.DrawPath(Path, Fill);
                F(c, Shade(col, 0.8f), new SKRect(-hw * 0.85f, potTop, hw * 0.85f, potTop + h * 0.06f), 0.01f);
                var leaf = new SKColor(0x3C, 0x8C, 0x4A);
                for (int i = 0; i < 7; i++)
                {
                    float a = -MathF.PI / 2 + (i - 3) * 0.38f;
                    float len = h * (0.55f + 0.12f * MathF.Sin(i * 2.3f));
                    float ex = MathF.Cos(a) * len * 0.75f, ey = potTop + MathF.Sin(a) * len;
                    Path.Reset();
                    Path.MoveTo(0, potTop);
                    Path.QuadTo(ex * 0.3f - 0.06f, (potTop + ey) / 2, ex, ey);
                    Path.QuadTo(ex * 0.3f + 0.06f, (potTop + ey) / 2, 0, potTop);
                    Fill.Color = i % 2 == 0 ? leaf : Shade(leaf, 1.25f);
                    c.DrawPath(Path, Fill);
                }
                break;
            }

            case PropKind.FloorLamp:
            {
                F(c, new SKColor(0x3A, 0x30, 0x2A), new SKRect(-hw, hh - 0.04f, hw, hh), 0.02f);
                L(c, new SKColor(0x4A, 0x3E, 0x36), 0.03f, 0, hh - 0.04f, 0, -hh + h * 0.2f);
                Path.Reset();
                Path.MoveTo(-hw * 0.55f, -hh);
                Path.LineTo(hw * 0.55f, -hh);
                Path.LineTo(hw, -hh + h * 0.22f);
                Path.LineTo(-hw, -hh + h * 0.22f);
                Path.Close();
                Fill.Color = col;
                c.DrawPath(Path, Fill);
                F(c, new SKColor(0xFF, 0xF0, 0xC0, 200), new SKRect(-hw * 0.9f, -hh + h * 0.2f, hw * 0.9f, -hh + h * 0.24f));
                break;
            }

            case PropKind.Lantern:
            case PropKind.Pendant:
            {
                if (p.Kind == PropKind.Lantern)
                {
                    F(c, new SKColor(0x2E, 0x2A, 0x26), new SKRect(-hw, -hh, hw, -hh + h * 0.15f), 0.02f);
                    F(c, new SKColor(0xFF, 0xC8, 0x6A, 230), new SKRect(-hw * 0.8f, -hh + h * 0.15f, hw * 0.8f, hh - h * 0.12f), 0.02f);
                    L(c, new SKColor(0x2E, 0x2A, 0x26), 0.015f, -hw * 0.8f, -hh, -hw * 0.8f, hh);
                    L(c, new SKColor(0x2E, 0x2A, 0x26), 0.015f, hw * 0.8f, -hh, hw * 0.8f, hh);
                    F(c, new SKColor(0x2E, 0x2A, 0x26), new SKRect(-hw, hh - h * 0.12f, hw, hh), 0.02f);
                    Circle(c, new SKColor(0xFF, 0xF0, 0xB0), 0, 0, w * 0.15f);
                }
                else
                {
                    Path.Reset();
                    Path.MoveTo(-hw * 0.2f, -hh);
                    Path.LineTo(hw * 0.2f, -hh);
                    Path.LineTo(hw, hh * 0.6f);
                    Path.LineTo(-hw, hh * 0.6f);
                    Path.Close();
                    Fill.Color = col;
                    c.DrawPath(Path, Fill);
                    F(c, Shade(col, 0.75f), new SKRect(-hw, hh * 0.45f, hw, hh * 0.6f), 0.01f);
                    Circle(c, new SKColor(0xFF, 0xF4, 0xD0), 0, hh * 0.65f, w * 0.13f);
                }
                break;
            }

            case PropKind.Clock:
            {
                float r = MathF.Min(hw, hh);
                Circle(c, col, 0, 0, r);
                Circle(c, new SKColor(0xFA, 0xF6, 0xEE), 0, 0, r * 0.84f);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * MathF.Tau / 12;
                    L(c, new SKColor(0x3A, 0x3A, 0x44), 0.01f, MathF.Cos(a) * r * 0.7f, MathF.Sin(a) * r * 0.7f, MathF.Cos(a) * r * 0.78f, MathF.Sin(a) * r * 0.78f);
                }
                float t = time * 0.6f;
                L(c, new SKColor(0x2A, 0x2A, 0x30), 0.022f, 0, 0, MathF.Cos(t / 12 - 1.5f) * r * 0.45f, MathF.Sin(t / 12 - 1.5f) * r * 0.45f);
                L(c, new SKColor(0x2A, 0x2A, 0x30), 0.016f, 0, 0, MathF.Cos(t - 1.5f) * r * 0.65f, MathF.Sin(t - 1.5f) * r * 0.65f);
                float sec = time * MathF.Tau / 60f - MathF.PI / 2;   // a real second hand
                L(c, acc, 0.008f, 0, 0, MathF.Cos(sec) * r * 0.7f, MathF.Sin(sec) * r * 0.7f);
                break;
            }

            case PropKind.Binder:
                F(c, col, new SKRect(-hw, -hh, hw, hh), 0.012f);
                F(c, new SKColor(0xF4, 0xF4, 0xF0), new SKRect(-hw * 0.6f, -hh + h * 0.12f, hw * 0.6f, -hh + h * 0.32f), 0.01f);
                Circle(c, Shade(col, 0.6f), 0, hh - h * 0.2f, w * 0.18f);
                break;

            case PropKind.Monitor:
            {
                F(c, new SKColor(0x1C, 0x1C, 0x22), new SKRect(-hw, -hh, hw, hh - h * 0.25f), 0.025f);
                var screen = new SKRect(-hw + 0.03f, -hh + 0.03f, hw - 0.03f, hh - h * 0.25f - 0.03f);
                F(c, new SKColor(0x0A, 0x0C, 0x18), screen);
                // the music on screen: little spectrum bars
                int bars = 12;
                float bw = screen.Width / bars;
                for (int i = 0; i < bars; i++)
                {
                    float v = f.Bands.Length > 0 ? f.Bands[Math.Min(f.Bands.Length - 1, i * f.Bands.Length / bars)] : 0.3f;
                    float bh = MathF.Max(0.01f, v * screen.Height * 0.9f);
                    F(c, pal.Sample(i / (float)(bars - 1)), new SKRect(screen.Left + i * bw + bw * 0.15f, screen.Bottom - bh, screen.Left + (i + 1) * bw - bw * 0.15f, screen.Bottom));
                }
                F(c, new SKColor(0x2A, 0x2A, 0x32), new SKRect(-w * 0.06f, hh - h * 0.25f, w * 0.06f, hh - 0.02f));
                F(c, new SKColor(0x2A, 0x2A, 0x32), new SKRect(-w * 0.22f, hh - 0.03f, w * 0.22f, hh), 0.01f);
                break;
            }

            case PropKind.Present:
                F(c, col, new SKRect(-hw, -hh, hw, hh), 0.015f);
                F(c, acc, new SKRect(-w * 0.08f, -hh, w * 0.08f, hh));
                F(c, acc, new SKRect(-hw, -h * 0.08f, hw, h * 0.08f));
                Fill.Color = acc;
                c.DrawOval(-w * 0.16f, -hh - h * 0.06f, w * 0.14f, h * 0.08f, Fill);
                c.DrawOval(w * 0.16f, -hh - h * 0.06f, w * 0.14f, h * 0.08f, Fill);
                break;

            case PropKind.Tree:
            {
                // trunk + stand at the bottom, three tiers, ornaments, star
                F(c, new SKColor(0x6A, 0x40, 0x22), new SKRect(-w * 0.07f, hh - h * 0.14f, w * 0.07f, hh - h * 0.04f));
                F(c, new SKColor(0x8A, 0x22, 0x22), new SKRect(-w * 0.2f, hh - h * 0.05f, w * 0.2f, hh), 0.02f);
                var green = new SKColor(0x1E, 0x6A, 0x3A);
                for (int k = 0; k < 3; k++)
                {
                    float top = -hh + h * (0.04f + k * 0.24f);
                    float bot = -hh + h * (0.36f + k * 0.25f);
                    float half = hw * (0.55f + k * 0.22f);
                    Path.Reset();
                    Path.MoveTo(0, top);
                    Path.LineTo(half, bot);
                    Path.QuadTo(0, bot + h * 0.04f, -half, bot);
                    Path.Close();
                    Fill.Color = Shade(green, 1f - k * 0.1f);
                    c.DrawPath(Path, Fill);
                }
                // garland
                Line.Color = new SKColor(0xE8, 0xC8, 0x60);
                Line.StrokeWidth = 0.02f;
                for (int k = 0; k < 3; k++)
                {
                    float y = -hh + h * (0.27f + k * 0.24f);
                    float half = hw * (0.45f + k * 0.2f);
                    Path.Reset();
                    Path.MoveTo(-half, y - 0.05f);
                    Path.QuadTo(0, y + 0.12f, half, y - 0.02f);
                    c.DrawPath(Path, Line);
                }
                var rng = new Random(p.Seed);
                for (int k = 0; k < 14; k++)
                {
                    float ty = (float)rng.NextDouble();
                    float y = -hh + h * (0.12f + ty * 0.72f);
                    float half = hw * (0.15f + ty * 0.8f);
                    float x = ((float)rng.NextDouble() * 2 - 1) * half * 0.8f;
                    float tw = 0.75f + 0.25f * MathF.Sin(time * 3 + k);
                    Circle(c, Shade(pal.Colors[k % pal.Colors.Length], tw), x, y, 0.045f);
                    Circle(c, SKColors.White.WithAlpha(120), x - 0.014f, y - 0.014f, 0.013f);
                }
                Star(c, 0, -hh + 0.02f, 0.11f, new SKColor(0xFF, 0xD8, 0x4A));
                break;
            }

            case PropKind.Stocking:
            {
                Path.Reset();
                Path.MoveTo(-hw * 0.7f, -hh + h * 0.2f);
                Path.LineTo(hw * 0.3f, -hh + h * 0.2f);
                Path.LineTo(hw * 0.3f, hh - h * 0.3f);
                Path.QuadTo(hw * 1.1f, hh - h * 0.3f, hw, hh - h * 0.08f);
                Path.QuadTo(hw * 0.8f, hh, hw * 0.1f, hh);
                Path.QuadTo(-hw * 0.7f, hh, -hw * 0.7f, hh - h * 0.25f);
                Path.Close();
                Fill.Color = col;
                c.DrawPath(Path, Fill);
                F(c, new SKColor(0xF6, 0xF2, 0xEA), new SKRect(-hw * 0.8f, -hh, hw * 0.4f, -hh + h * 0.24f), 0.03f);
                F(c, acc, new SKRect(-hw * 0.7f, hh - h * 0.48f, hw * 0.3f, hh - h * 0.4f));
                break;
            }

            case PropKind.Block:
            {
                F(c, col, new SKRect(-hw, -hh, hw, hh), 0.025f);
                F(c, ColorUtil.Mix(col, SKColors.White, 0.35f), new SKRect(-hw + 0.025f, -hh + 0.025f, hw - 0.025f, hh - 0.025f), 0.02f);
                if (p.Label != null)
                {
                    Fill.Color = Shade(col, 0.7f);
                    c.DrawText(p.Label, 0, hh * 0.45f, SKTextAlign.Center, BlockFont(h), Fill);
                }
                break;
            }

            case PropKind.Bear:
            {
                var fur = col;
                Circle(c, fur, 0, hh * 0.25f, hw * 0.75f);                 // body
                Circle(c, fur, -hw * 0.55f, hh * 0.8f, hw * 0.3f);          // feet
                Circle(c, fur, hw * 0.55f, hh * 0.8f, hw * 0.3f);
                Circle(c, fur, 0, -hh * 0.45f, hw * 0.6f);                  // head
                Circle(c, fur, -hw * 0.45f, -hh * 0.85f, hw * 0.22f);       // ears
                Circle(c, fur, hw * 0.45f, -hh * 0.85f, hw * 0.22f);
                Circle(c, ColorUtil.Mix(fur, SKColors.White, 0.4f), 0, hh * 0.3f, hw * 0.42f);
                Circle(c, ColorUtil.Mix(fur, SKColors.White, 0.4f), 0, -hh * 0.3f, hw * 0.25f);
                Circle(c, new SKColor(0x22, 0x18, 0x14), -hw * 0.2f, -hh * 0.55f, 0.018f);
                Circle(c, new SKColor(0x22, 0x18, 0x14), hw * 0.2f, -hh * 0.55f, 0.018f);
                Circle(c, new SKColor(0x22, 0x18, 0x14), 0, -hh * 0.36f, 0.022f);
                F(c, acc, new SKRect(-hw * 0.35f, -hh * 0.05f, hw * 0.35f, hh * 0.05f), 0.02f);
                break;
            }

            case PropKind.Shelf:
                F(c, col, new SKRect(-hw, -hh, hw, hh), 0.01f);
                F(c, Shade(col, 1.15f), new SKRect(-hw, -hh, hw, -hh + h * 0.3f), 0.01f);
                F(c, Shade(col, 0.75f), new SKRect(-hw, hh - h * 0.25f, hw, hh));
                break;

            case PropKind.Trophy:
                F(c, new SKColor(0x3A, 0x30, 0x28), new SKRect(-hw, hh - h * 0.2f, hw, hh), 0.01f);
                F(c, col, new SKRect(-w * 0.08f, -hh + h * 0.45f, w * 0.08f, hh - h * 0.2f));
                Fill.Color = col;
                c.DrawArc(new SKRect(-hw * 0.9f, -hh - h * 0.25f, hw * 0.9f, -hh + h * 0.55f), 0, 180, true, Fill);
                Star(c, 0, -hh + h * 0.12f, w * 0.18f, ColorUtil.Mix(col, SKColors.White, 0.5f));
                break;

            case PropKind.Skis:
                F(c, col, new SKRect(-hw, -hh, -hw * 0.1f, hh), 0.03f);
                F(c, acc, new SKRect(hw * 0.1f, -hh, hw, hh), 0.03f);
                F(c, new SKColor(0x2A, 0x2A, 0x30), new SKRect(-hw, -hh * 0.05f, hw, hh * 0.12f), 0.01f);
                break;

            case PropKind.Antlers:
            {
                F(c, new SKColor(0x6A, 0x46, 0x2A), new SKRect(-hw * 0.28f, -hh * 0.2f, hw * 0.28f, hh), 0.04f);  // wooden plaque
                Line.Color = new SKColor(0xE8, 0xDC, 0xC0);
                Line.StrokeWidth = 0.035f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Path.Reset();
                    Path.MoveTo(s * hw * 0.15f, 0);
                    Path.QuadTo(s * hw * 0.6f, -hh * 0.1f, s * hw * 0.92f, -hh * 0.85f);
                    c.DrawPath(Path, Line);
                    for (int k = 0; k < 3; k++)
                    {
                        float tx = s * hw * (0.35f + k * 0.2f), ty = -hh * (0.15f + k * 0.25f);
                        c.DrawLine(tx, ty, tx + s * 0.02f, ty - hh * 0.45f, Line);
                    }
                }
                break;
            }

            case PropKind.Wreath:
            {
                float r = MathF.Min(hw, hh) * 0.8f;
                Line.Color = new SKColor(0x1E, 0x6A, 0x3A);
                Line.StrokeWidth = r * 0.42f;
                c.DrawCircle(0, 0, r * 0.78f, Line);
                Line.Color = new SKColor(0x2C, 0x88, 0x4C);
                Line.StrokeWidth = r * 0.16f;
                c.DrawCircle(0, 0, r * 0.88f, Line);
                for (int k = 0; k < 10; k++)
                {
                    float a = k * MathF.Tau / 10;
                    Circle(c, k % 2 == 0 ? new SKColor(0xD6, 0x22, 0x32) : pal.Colors[k % pal.Colors.Length], MathF.Cos(a) * r * 0.78f, MathF.Sin(a) * r * 0.78f, r * 0.09f);
                }
                Bow(c, 0, r * 0.75f, r * 0.35f, new SKColor(0xC8, 0x18, 0x28));
                break;
            }

            case PropKind.Mobile:
            {
                L(c, new SKColor(0x8A, 0x6A, 0x4A), 0.02f, -hw, -hh, hw, -hh);
                for (int k = 0; k < 4; k++)
                {
                    float x = -hw + (k + 0.5f) * w / 4;
                    float len = h * (0.45f + 0.2f * (k % 2));
                    float sway = MathF.Sin(time * 1.3f + k) * 0.03f;
                    L(c, new SKColor(0x6A, 0x6A, 0x70), 0.006f, x, -hh, x + sway, -hh + len);
                    if (k % 2 == 0) Star(c, x + sway, -hh + len + 0.05f, 0.07f, pal.Colors[k % pal.Colors.Length]);
                    else Circle(c, pal.Colors[k % pal.Colors.Length], x + sway, -hh + len + 0.05f, 0.05f);
                }
                break;
            }

            case PropKind.Candle:
            {
                F(c, col, new SKRect(-hw, -hh + h * 0.2f, hw, hh), 0.01f);
                F(c, new SKColor(0xC8, 0xA0, 0x40), new SKRect(-hw * 1.4f, hh - 0.02f, hw * 1.4f, hh), 0.01f);
                float fl = 0.06f * (1 + 0.15f * MathF.Sin(time * 13 + p.Seed));
                Fill.Color = new SKColor(0xFF, 0xB8, 0x40);
                c.DrawOval(0, -hh + h * 0.2f - fl * 0.7f, fl * 0.35f, fl * 0.7f, Fill);
                Fill.Color = new SKColor(0xFF, 0xF4, 0xC0);
                c.DrawOval(0, -hh + h * 0.2f - fl * 0.5f, fl * 0.15f, fl * 0.35f, Fill);
                break;
            }

            case PropKind.Vase:
                Path.Reset();
                Path.MoveTo(-hw * 0.4f, -hh);
                Path.LineTo(hw * 0.4f, -hh);
                Path.QuadTo(hw * 0.2f, -hh * 0.4f, hw, hh * 0.3f);
                Path.QuadTo(hw, hh, 0, hh);
                Path.QuadTo(-hw, hh, -hw, hh * 0.3f);
                Path.QuadTo(-hw * 0.2f, -hh * 0.4f, -hw * 0.4f, -hh);
                Path.Close();
                Fill.Color = col;
                c.DrawPath(Path, Fill);
                L(c, acc, 0.02f, -hw * 0.8f, hh * 0.35f, hw * 0.8f, hh * 0.35f);
                break;

            case PropKind.Bowl:
                Fill.Color = col;
                c.DrawArc(new SKRect(-hw, -hh * 2.2f, hw, hh), 0, 180, true, Fill);
                L(c, Shade(col, 0.75f), 0.015f, -hw, -hh * 0.6f + hh * 0.6f, hw, 0);
                break;

            case PropKind.Toy:
                // a little toy car
                F(c, col, new SKRect(-hw, -hh * 0.1f, hw, hh * 0.55f), 0.03f);
                F(c, ColorUtil.Mix(col, SKColors.White, 0.3f), new SKRect(-hw * 0.5f, -hh, hw * 0.4f, -hh * 0.05f), 0.04f);
                F(c, new SKColor(0xBC, 0xE4, 0xFF), new SKRect(-hw * 0.38f, -hh * 0.8f, hw * 0.28f, -hh * 0.2f), 0.02f);
                Circle(c, new SKColor(0x22, 0x22, 0x28), -hw * 0.55f, hh * 0.6f, h * 0.22f);
                Circle(c, new SKColor(0x22, 0x22, 0x28), hw * 0.55f, hh * 0.6f, h * 0.22f);
                break;
        }
        c.Restore();
    }

    private static readonly Dictionary<int, SKFont> BlockFonts = [];

    private static SKFont BlockFont(float h)
    {
        int key = (int)(h * 1000);
        if (!BlockFonts.TryGetValue(key, out var font))
        {
            font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), h * 0.7f);
            BlockFonts[key] = font;
        }
        return font;
    }

    public static void Star(SKCanvas c, float x, float y, float r, SKColor col)
    {
        Path.Reset();
        for (int i = 0; i < 10; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            if (i == 0) Path.MoveTo(x + MathF.Cos(a) * rr, y + MathF.Sin(a) * rr);
            else Path.LineTo(x + MathF.Cos(a) * rr, y + MathF.Sin(a) * rr);
        }
        Path.Close();
        Fill.Shader = null;
        Fill.Color = col;
        c.DrawPath(Path, Fill);
    }

    public static void Bow(SKCanvas c, float x, float y, float s, SKColor col)
    {
        Fill.Color = col;
        Path.Reset();
        Path.MoveTo(x, y);
        Path.LineTo(x - s, y - s * 0.5f);
        Path.LineTo(x - s, y + s * 0.5f);
        Path.Close();
        Path.MoveTo(x, y);
        Path.LineTo(x + s, y - s * 0.5f);
        Path.LineTo(x + s, y + s * 0.5f);
        Path.Close();
        c.DrawPath(Path, Fill);
        c.DrawCircle(x, y, s * 0.22f, Fill);
        L(c, col, s * 0.18f, x, y, x - s * 0.4f, y + s * 1.1f);
        L(c, col, s * 0.18f, x, y, x + s * 0.4f, y + s * 1.1f);
    }

    /// <summary>A 4-point twinkle used for tidy-up sparkles.</summary>
    public static void Sparkle(SKCanvas c, float x, float y, float r, SKColor col)
    {
        Path.Reset();
        Path.MoveTo(x, y - r);
        Path.QuadTo(x, y, x + r, y);
        Path.QuadTo(x, y, x, y + r);
        Path.QuadTo(x, y, x - r, y);
        Path.QuadTo(x, y, x, y - r);
        Path.Close();
        Fill.Shader = null;
        Fill.Color = col;
        c.DrawPath(Path, Fill);
    }
}
