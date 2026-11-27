using SkiaSharp;

namespace MusicVisualizer.Visuals.Space;

internal struct Rock
{
    public float X, Y, VX, VY, R, Rot, Spin, Hp, Hit;
    public int Shape;
}

/// <summary>
/// Asteroids drifting past during cruise: a fixed pool of lumpy rocks (a few pre-built shapes, tinted from the
/// palette), plus the occasional comet. No allocations once built.
/// </summary>
internal sealed class SpaceRocks : IDisposable
{
    private const int Shapes = 5;
    private static readonly SKPath[] Outline = new SKPath[Shapes];
    private static readonly SKPath[] Craters = new SKPath[Shapes];
    private static readonly SKPath[] Shade = new SKPath[Shapes];   // lower-right "night" half of each rock

    private readonly Rock[] _r;
    private int _count;

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private SKColor _rock, _rockDark, _outline;

    static SpaceRocks()
    {
        var rng = new Random(99);
        for (int k = 0; k < Shapes; k++)
        {
            var p = new SKPath();
            int n = 9 + k % 3;
            var pts = new SKPoint[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * MathF.Tau;
                float r = 0.78f + 0.24f * (float)rng.NextDouble();
                pts[i] = new SKPoint(MathF.Cos(a) * r, MathF.Sin(a) * r * (0.8f + 0.06f * k));
            }
            // Rounded polygon: quads through edge midpoints.
            var m0 = Mid(pts[n - 1], pts[0]);
            p.MoveTo(m0);
            for (int i = 0; i < n; i++) p.QuadTo(pts[i], Mid(pts[i], pts[(i + 1) % n]));
            p.Close();
            Outline[k] = p;

            var cr = new SKPath();
            for (int i = 0; i < 3; i++)
            {
                float a = (float)(rng.NextDouble() * MathF.Tau), d = 0.15f + 0.4f * (float)rng.NextDouble();
                cr.AddCircle(MathF.Cos(a) * d, MathF.Sin(a) * d, 0.1f + 0.12f * (float)rng.NextDouble());
            }
            Craters[k] = cr;

            using var night = new SKPath();
            night.AddCircle(0.32f, 0.32f, 1.0f);
            Shade[k] = p.Op(night, SKPathOp.Intersect) ?? new SKPath();
        }
    }

    private static SKPoint Mid(SKPoint a, SKPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    public SpaceRocks(int capacity) => _r = new Rock[capacity];

    public int Count => _count;
    public ref Rock this[int i] => ref _r[i];

    public void SetPalette(Palette palette)
    {
        _rock = ColorUtil.Mix(new SKColor(0x76, 0x70, 0x78), palette.Colors[^1], 0.22f);
        _rockDark = ColorUtil.Darken(_rock, 0.55f);
        _outline = ColorUtil.Darken(_rock, 0.8f);
    }

    public SKColor Color => _rock;

    public void Spawn(float x, float y, float vx, float vy, float r, int shape, float spin)
    {
        if (_count == _r.Length) return;
        ref var k = ref _r[_count++];
        k.X = x; k.Y = y; k.VX = vx; k.VY = vy; k.R = r; k.Shape = shape % Shapes; k.Spin = spin; k.Rot = 0;
        k.Hp = r; k.Hit = 0;
    }

    public void RemoveAt(int i) => _r[i] = _r[--_count];

    /// <param name="scroll">Extra leftward drift (px/s) from the ship's own speed.</param>
    public void Update(float dt, float w, float h, float scroll)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var k = ref _r[i];
            k.X += (k.VX - scroll) * dt;
            k.Y += k.VY * dt;
            k.Rot += k.Spin * dt;
            k.Hit *= MathF.Exp(-dt / 0.08f);
            if (k.X < -k.R * 2 || k.Y < -k.R * 3 || k.Y > h + k.R * 3) { RemoveAt(i); i--; }
        }
    }

    public void Draw(SKCanvas c, float pxPerDip, bool overlay)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var k = ref _r[i];
            float unit = pxPerDip / k.R;
            c.Save();
            c.Translate(k.X, k.Y);
            c.Scale(k.R);
            if (overlay)
            {
                _stroke.Color = new SKColor(0, 0, 0, 80);
                _stroke.StrokeWidth = 4f * unit;
                c.DrawPath(Outline[k.Shape], _stroke);
            }
            c.RotateRadians(k.Rot);
            _fill.Color = _rock;
            c.DrawPath(Outline[k.Shape], _fill);
            _fill.Color = _rockDark.WithAlpha(110);
            c.DrawPath(Craters[k.Shape], _fill);
            _fill.Color = _rockDark.WithAlpha(150);
            c.DrawPath(Shade[k.Shape], _fill);
            _stroke.Color = _outline;
            _stroke.StrokeWidth = 1.4f * unit;
            c.DrawPath(Outline[k.Shape], _stroke);
            if (k.Hit > 0.02f)
            {
                _fill.Color = SKColors.White.WithAlpha((byte)(170 * k.Hit));
                c.DrawPath(Outline[k.Shape], _fill);
            }
            c.Restore();
        }
    }

    /// <summary>A comet: bright head with a tapered tail trailing opposite its motion.</summary>
    public void DrawComet(SKCanvas c, SKCanvas? g, float x, float y, float vx, float vy, float tail, float head, SKColor color, float alpha)
    {
        float sp = MathF.Sqrt(vx * vx + vy * vy);
        if (sp < 1e-3f || alpha <= 0.01f) return;
        float dx = -vx / sp, dy = -vy / sp;
        for (int i = 0; i < 4; i++)
        {
            float k = 1f - i * 0.22f;
            _line.Color = ColorUtil.Mix(color, SKColors.White, i * 0.2f).WithAlpha((byte)(alpha * (40 + 40 * i)));
            _line.StrokeWidth = head * (2.2f - 0.45f * i);
            c.DrawLine(x, y, x + dx * tail * k, y + dy * tail * k, _line);
        }
        _fill.Color = SKColors.White.WithAlpha((byte)(255 * alpha));
        c.DrawCircle(x, y, head * 0.9f, _fill);
        if (g != null)
        {
            _line.Color = color.WithAlpha((byte)(170 * alpha));
            _line.StrokeWidth = head * 5f;
            g.DrawLine(x, y, x + dx * tail * 0.6f, y + dy * tail * 0.6f, _line);
            _fill.Color = ColorUtil.Mix(color, SKColors.White, 0.5f).WithAlpha((byte)(230 * alpha));
            g.DrawCircle(x, y, head * 4f, _fill);
        }
    }

    public void Dispose()
    {
        _fill.Dispose(); _stroke.Dispose(); _line.Dispose();
    }
}
