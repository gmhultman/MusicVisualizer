using SkiaSharp;

namespace MusicVisualizer.Visuals.Space;

internal enum FxKind : byte { Spark, Fire, Smoke, Debris, Ring, Flash }

internal struct FxParticle
{
    public float X, Y, VX, VY, Age, Life, Size, Rot, Spin, Drag;
    public FxKind Kind;
    public SKColor Color, Color2;
}

/// <summary>Fixed-size pool of explosion/impact particles (no allocations once constructed).</summary>
internal sealed class FxPool : IDisposable
{
    private static readonly SKPath Shard = BuildShard();

    private readonly FxParticle[] _p;
    private int _count;
    private readonly SKPaint _plus = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _plusStroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _sprite = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _smokeSprite = new() { IsAntialias = true };
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Nearest); // sprites are soft and big enough

    // Soft round sprites (the main canvas is CPU-rendered, so no per-particle blurs or gradient shaders):
    // a colour ramp for fireballs (white → warm → ember), and one for smoke.
    private const int RampSteps = 16;
    private readonly SKImage?[] _fireRamp = new SKImage?[RampSteps];
    private SKImage? _smoke;

    public FxPool(int capacity) => _p = new FxParticle[capacity];

    /// <summary>Rebuilds the fireball colour ramp (call when the palette changes).</summary>
    public void SetFireColors(SKColor warm, SKColor ember)
    {
        for (int i = 0; i < RampSteps; i++)
        {
            float t = i / (RampSteps - 1f);
            var col = t < 0.15f ? ColorUtil.Mix(SKColors.White, warm, t / 0.15f) : ColorUtil.Mix(warm, ember, (t - 0.15f) / 0.85f);
            _fireRamp[i]?.Dispose();
            _fireRamp[i] = SoftDisc(col, 0.85f);
        }
        _smoke ??= SoftDisc(new SKColor(70, 68, 80), 0.6f);
    }

    private static SKImage SoftDisc(SKColor c, float core)
    {
        const int N = 128;
        using var surface = SKSurface.Create(new SKImageInfo(N, N, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var shader = SKShader.CreateRadialGradient(new SKPoint(N / 2f, N / 2f), N / 2f,
            [c, c.WithAlpha((byte)(190 * core)), c.WithAlpha((byte)(55 * core)), c.WithAlpha(0)], [0f, 0.18f, 0.55f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        surface.Canvas.DrawCircle(N / 2f, N / 2f, N / 2f, paint);
        return surface.Snapshot();
    }

    public int Count => _count;

    public void Clear() => _count = 0;

    public void Spawn(FxKind kind, float x, float y, float vx, float vy, float life, float size, SKColor color,
                      SKColor color2 = default, float drag = 0f, float spin = 0f, float rot = 0f)
    {
        if (_count == _p.Length)
        {
            // Full: recycle the oldest-looking slot (the first) rather than dropping new effects.
            _p[0] = _p[--_count];
        }
        ref var p = ref _p[_count++];
        p.Kind = kind; p.X = x; p.Y = y; p.VX = vx; p.VY = vy; p.Age = 0; p.Life = life; p.Size = size;
        p.Color = color; p.Color2 = color2; p.Drag = drag; p.Spin = spin; p.Rot = rot;
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var p = ref _p[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _p[i] = _p[--_count];
                i--;
                continue;
            }
            if (p.Drag > 0)
            {
                float k = MathF.Exp(-p.Drag * dt);
                p.VX *= k; p.VY *= k;
            }
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Rot += p.Spin * dt;
        }
    }

    public void Draw(SKCanvas c, SKCanvas? g)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var p = ref _p[i];
            float t = p.Age / p.Life;
            switch (p.Kind)
            {
                case FxKind.Spark:
                {
                    float a = 1f - t;
                    _plusStroke.Color = p.Color.WithAlpha((byte)(255 * a));
                    _plusStroke.StrokeWidth = p.Size * (1f - 0.6f * t);
                    c.DrawLine(p.X, p.Y, p.X - p.VX * 0.05f, p.Y - p.VY * 0.05f, _plusStroke);
                    break;
                }
                case FxKind.Fire:
                {
                    float r = p.Size * (0.6f + 1.2f * MathF.Sqrt(t));
                    float a = MathF.Pow(1f - t, 1.2f);
                    var img = _fireRamp[Math.Min(RampSteps - 1, (int)(t * RampSteps))];
                    if (img != null)
                    {
                        _sprite.Color = SKColors.White.WithAlpha((byte)(235 * a));
                        c.DrawImage(img, new SKRect(p.X - r, p.Y - r, p.X + r, p.Y + r), Sampling, _sprite);
                    }
                    if (g != null && t < 0.6f)
                    {
                        var col = t < 0.15f ? ColorUtil.Mix(SKColors.White, p.Color, t / 0.15f) : ColorUtil.Mix(p.Color, p.Color2, (t - 0.15f) / 0.85f);
                        _fill.Color = col.WithAlpha((byte)(140 * a));
                        g.DrawCircle(p.X, p.Y, r * 0.9f, _fill);
                    }
                    break;
                }
                case FxKind.Smoke:
                {
                    float r = p.Size * (0.8f + 2f * t);
                    float a = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
                    if (_smoke != null)
                    {
                        _smokeSprite.Color = SKColors.White.WithAlpha((byte)(p.Color.Alpha * a));
                        c.DrawImage(_smoke, new SKRect(p.X - r, p.Y - r, p.X + r, p.Y + r), Sampling, _smokeSprite);
                    }
                    break;
                }
                case FxKind.Debris:
                {
                    float a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                    var col = ColorUtil.Mix(p.Color2, p.Color, MathF.Min(1f, t * 3f));
                    _fill.Color = col.WithAlpha((byte)(255 * a));
                    c.Save();
                    c.Translate(p.X, p.Y);
                    c.RotateRadians(p.Rot);
                    c.Scale(p.Size);
                    c.DrawPath(Shard, _fill);
                    c.Restore();
                    break;
                }
                case FxKind.Ring:
                {
                    float e = 1f - (1f - t) * (1f - t);
                    float r = p.Size * (0.15f + 0.85f * e);
                    _plusStroke.Color = p.Color.WithAlpha((byte)(150 * (1f - t)));
                    _plusStroke.StrokeWidth = p.Size * 0.04f * (1f - t) + 0.5f;
                    c.DrawCircle(p.X, p.Y, r, _plusStroke);
                    if (g != null)
                    {
                        _plusStroke.Color = p.Color.WithAlpha((byte)(110 * (1f - t)));
                        _plusStroke.StrokeWidth = p.Size * 0.12f * (1f - t) + 1f;
                        g.DrawCircle(p.X, p.Y, r, _plusStroke);
                    }
                    break;
                }
                case FxKind.Flash:
                {
                    float a = (1f - t) * (1f - t);
                    float r = p.Size * (0.6f + 0.6f * t);
                    if (g != null)
                    {
                        _fill.Color = p.Color.WithAlpha((byte)(190 * a));
                        g.DrawCircle(p.X, p.Y, r, _fill);
                    }
                    else if (_fireRamp[0] is { } white)
                    {
                        // No glow layer: a soft additive puff instead.
                        _sprite.Color = SKColors.White.WithAlpha((byte)(150 * a));
                        r *= 0.8f;
                        c.DrawImage(white, new SKRect(p.X - r, p.Y - r, p.X + r, p.Y + r), Sampling, _sprite);
                    }
                    break;
                }
            }
        }
    }

    private static SKPath BuildShard()
    {
        var p = new SKPath();
        p.MoveTo(0.6f, 0f);
        p.LineTo(-0.4f, -0.45f);
        p.LineTo(-0.2f, 0.05f);
        p.LineTo(-0.5f, 0.4f);
        p.Close();
        return p;
    }

    public void Dispose()
    {
        _plus.Dispose(); _plusStroke.Dispose(); _fill.Dispose(); _sprite.Dispose(); _smokeSprite.Dispose();
        foreach (var img in _fireRamp) img?.Dispose();
        _smoke?.Dispose();
    }
}

internal struct Bolt
{
    public float X, Y, PX, PY, VX, VY, Len, Width, Age, Damage;
    public bool FromHero, Heavy;
    public SKColor Color;
}

/// <summary>Fixed-size pool of laser bolts.</summary>
internal sealed class BoltPool : IDisposable
{
    private readonly Bolt[] _b;
    private int _count;
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _glow = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

    public BoltPool(int capacity) => _b = new Bolt[capacity];

    public int Count => _count;
    public ref Bolt this[int i] => ref _b[i];

    public void Clear() => _count = 0;

    public void Fire(float x, float y, float vx, float vy, float len, float width, SKColor color, bool fromHero, float damage, bool heavy)
    {
        if (_count == _b.Length) return;
        ref var b = ref _b[_count++];
        b.X = b.PX = x; b.Y = b.PY = y; b.VX = vx; b.VY = vy; b.Len = len; b.Width = width; b.Age = 0;
        b.Color = color; b.FromHero = fromHero; b.Damage = damage; b.Heavy = heavy;
    }

    public void RemoveAt(int i) => _b[i] = _b[--_count];

    public void Update(float dt, float w, float h)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var b = ref _b[i];
            b.PX = b.X; b.PY = b.Y;
            b.X += b.VX * dt;
            b.Y += b.VY * dt;
            b.Age += dt;
            float m = b.Len + 20;
            if (b.X < -m || b.X > w + m || b.Y < -m || b.Y > h + m || b.Age > 3f) { RemoveAt(i); i--; }
        }
    }

    public void Draw(SKCanvas c, SKCanvas? g)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var b = ref _b[i];
            float sp = MathF.Sqrt(b.VX * b.VX + b.VY * b.VY);
            if (sp < 1e-3f) continue;
            // Grows to full length over the first few frames so it seems to leave the muzzle.
            float len = MathF.Min(b.Len, sp * (b.Age + 0.012f));
            float tx = b.X - b.VX / sp * len, ty = b.Y - b.VY / sp * len;
            _line.Color = b.Color;
            _line.StrokeWidth = b.Width;
            c.DrawLine(tx, ty, b.X, b.Y, _line);
            _line.Color = ColorUtil.Mix(b.Color, SKColors.White, 0.75f);
            _line.StrokeWidth = b.Width * 0.42f;
            c.DrawLine(tx, ty, b.X, b.Y, _line);
            if (g != null)
            {
                _glow.Color = b.Color.WithAlpha(210);
                _glow.StrokeWidth = b.Width * 3.2f;
                g.DrawLine(tx, ty, b.X, b.Y, _glow);
            }
        }
    }

    public void Dispose()
    {
        _line.Dispose(); _glow.Dispose();
    }
}
