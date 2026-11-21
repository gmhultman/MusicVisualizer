using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// Particles burst outward on every beat and stream continuously with the music's loudness.
/// Each particle takes the colour of the frequency band that spawned it.
/// </summary>
public sealed class ParticlesVisualizer : IVisualizer
{
    private const int MaxParticles = 2500;

    private readonly float[] _x = new float[MaxParticles];
    private readonly float[] _y = new float[MaxParticles];
    private readonly float[] _vx = new float[MaxParticles];
    private readonly float[] _vy = new float[MaxParticles];
    private readonly float[] _life = new float[MaxParticles];
    private readonly float[] _maxLife = new float[MaxParticles];
    private readonly float[] _size = new float[MaxParticles];
    private readonly float[] _hue = new float[MaxParticles];
    private int _next;
    private float _emitDebt;

    private readonly Random _rng = new();
    private readonly SKPaint _streak = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _core = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };

    public string Name => "Particles";

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        float w = ctx.Width, h = ctx.Height, dt = ctx.Dt;
        float size = MathF.Min(w, h);
        // In a short, wide strip (bottom overlay) particles rise from the bottom edge instead of the centre.
        bool strip = h < w * 0.35f;
        float cx = w / 2, cy = h / 2;
        float baseSpeed = strip ? h * 1.1f : size * 0.55f;

        if (frame.Beat)
        {
            int count = (int)(70 + 180 * frame.BeatStrength);
            for (int i = 0; i < count; i++)
            {
                int band = _rng.Next(frame.Bands.Length / 3); // bursts are bass-coloured
                Spawn(strip, w, h, cx, cy, baseSpeed * (0.7f + 0.9f * frame.Bass) * (0.4f + (float)_rng.NextDouble()),
                      band, frame.Bands.Length, ctx.Scale, 1.0f + frame.BeatStrength);
            }
        }

        // Continuous emission driven by loudness; colour/speed from a random active band.
        _emitDebt += dt * frame.Level * 420f;
        while (_emitDebt >= 1f)
        {
            _emitDebt -= 1f;
            int band = _rng.Next(frame.Bands.Length);
            float v = frame.Bands[band];
            if (v < 0.2f) continue;
            Spawn(strip, w, h, cx, cy, baseSpeed * (0.25f + 0.8f * v) * (0.6f + 0.6f * (float)_rng.NextDouble()),
                  band, frame.Bands.Length, ctx.Scale, 0.7f);
        }

        // Core glow (centre mode only).
        if (!strip)
        {
            float r = size * (0.06f + 0.08f * frame.Bass + 0.06f * frame.Pulse);
            var c = ctx.Palette.Sample(0.2f);
            using var shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 2.5f,
                new[] { c.WithAlpha((byte)(60 + 140 * frame.Pulse)), c.WithAlpha(0) }, (float[]?)null, SKShaderTileMode.Clamp);
            _core.Shader = shader;
            ctx.Canvas.DrawCircle(cx, cy, r * 2.5f, _core);
            _core.Shader = null;
        }

        float drag = MathF.Exp(-dt * (strip ? 0.9f : 1.3f));
        float gravity = strip ? h * 0.6f : 0f;
        float streakTime = 0.035f + 0.03f * frame.Pulse;
        var canvas = ctx.Canvas;

        for (int i = 0; i < MaxParticles; i++)
        {
            if (_life[i] <= 0) continue;
            _life[i] -= dt;
            if (_life[i] <= 0) continue;

            _vx[i] *= drag;
            _vy[i] = _vy[i] * drag + gravity * dt;
            _x[i] += _vx[i] * dt;
            _y[i] += _vy[i] * dt;

            float t = _life[i] / _maxLife[i];
            byte alpha = (byte)(255 * MathF.Min(1f, t * 1.6f));
            _streak.Color = ctx.Palette.Sample(_hue[i]).WithAlpha(alpha);
            _streak.StrokeWidth = _size[i] * (0.5f + 0.5f * t);
            canvas.DrawLine(_x[i] - _vx[i] * streakTime, _y[i] - _vy[i] * streakTime, _x[i], _y[i], _streak);
        }
    }

    private void Spawn(bool strip, float w, float h, float cx, float cy, float speed, int band, int bandCount, float scale, float sizeMul)
    {
        int i = _next;
        _next = (_next + 1) % MaxParticles;

        if (strip)
        {
            // Spread along the bottom by frequency (low left → high right), fanning upward.
            float bx = (band + (float)_rng.NextDouble()) / bandCount * w;
            float a = -MathF.PI / 2 + ((float)_rng.NextDouble() - 0.5f) * 0.7f;
            _x[i] = bx;
            _y[i] = h;
            _vx[i] = MathF.Cos(a) * speed;
            _vy[i] = MathF.Sin(a) * speed;
        }
        else
        {
            float a = (float)(_rng.NextDouble() * MathF.Tau);
            float r = MathF.Min(w, h) * 0.04f * (float)_rng.NextDouble();
            _x[i] = cx + MathF.Cos(a) * r;
            _y[i] = cy + MathF.Sin(a) * r;
            _vx[i] = MathF.Cos(a) * speed;
            _vy[i] = MathF.Sin(a) * speed;
        }

        _maxLife[i] = _life[i] = 0.9f + 1.4f * (float)_rng.NextDouble();
        _size[i] = (1.2f + 2.4f * (float)_rng.NextDouble()) * scale * sizeMul;
        _hue[i] = (float)band / Math.Max(1, bandCount - 1);
    }
}
