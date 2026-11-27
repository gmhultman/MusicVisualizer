using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// A wall of flames along the bottom: two overlapping rows of tongues (bass in the centre, treble at the
/// edges) that rise with the music and jump on beats, over a glowing bed of coals, throwing embers.
/// </summary>
public sealed class FireVisualizer : IVisualizer
{
    private const int MaxEmbers = 300;

    private readonly FlameRenderer _flames = new();
    private readonly FlameRow _back = new();
    private readonly FlameRow _front = new();
    private readonly Random _rng = new(3);

    private readonly float[] _ex = new float[MaxEmbers], _ey = new float[MaxEmbers];
    private readonly float[] _evx = new float[MaxEmbers], _evy = new float[MaxEmbers];
    private readonly float[] _elife = new float[MaxEmbers], _emax = new float[MaxEmbers], _esize = new float[MaxEmbers];
    private int _nextEmber;
    private float _emberDebt;

    private readonly SKPaint _ember = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _coals = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public string Name => "Fire";
    public VisualizerCategory Category => VisualizerCategory.Nature;

    // Big overlapping gradient shapes are expensive to fill on the CPU; smooth flames lose nothing at 60%.
    public float RenderScale => 0.6f;

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        float w = ctx.Width, h = ctx.Height;
        bool strip = h < w * 0.35f;

        // Tongue count follows width so flames keep their proportions on any screen.
        float spacing = 64f * ctx.Scale;
        int count = Math.Max(10, (int)(w / spacing));
        static float Mirror(int i, int n) => MathF.Abs(i / (float)(n - 1) * 2f - 1f); // 0 centre .. 1 edges
        _back.Ensure(count + 1, i => Mirror(i, count + 1), 11);
        _front.Ensure(count, i => Mirror(i, count), 23);
        _back.Update(frame, ctx.Dt);
        _front.Update(frame, ctx.Dt);

        float maxH = strip ? h * 0.95f : h * 0.62f;
        float baseY = h + 4f * ctx.Scale;
        float slot = w / count;
        var canvas = ctx.Canvas;

        // Glowing coal bed / heat haze at the base. With glow on it's taller and goes on the low-res glow
        // layer (a full-width gradient is a lot of pixels at full resolution).
        var heat = ctx.Palette.Sample(0.4f);
        var bedCanvas = ctx.GlowCanvas ?? canvas;
        float bedH = maxH * (0.25f + 0.25f * frame.Level) * (ctx.Glow ? 2f : 1f);
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, h), new SKPoint(0, h - bedH),
                   new[] { heat.WithAlpha((byte)(170 + 80 * frame.Pulse)), heat.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
        {
            _coals.Shader = shader;
            bedCanvas.DrawRect(0, h - bedH, w, bedH, _coals);
            _coals.Shader = null;
        }

        // Back row: taller, wider, slightly dimmer, offset half a slot.
        _flames.BeginRow(ctx.Palette, baseY, maxH * 1.1f, 0.8f);
        for (int i = 0; i < _back.Count; i++)
        {
            float x = (i - 0.5f) * slot + slot / 2;
            float tw = slot * 1.45f;
            float th = maxH * 1.0f * _back.Height(i);
            _flames.DrawTongue(canvas, x, baseY, tw, th, _back.Sway(i) * tw, _back.Wiggle(i) * tw, ctx.GlowCanvas, ctx.Scale, layers: 1);
        }
        // Front row.
        _flames.BeginRow(ctx.Palette, baseY, maxH * 0.9f);
        for (int i = 0; i < _front.Count; i++)
        {
            float x = i * slot + slot / 2;
            float tw = slot * 1.15f;
            float th = maxH * 0.78f * _front.Height(i);
            _flames.DrawTongue(canvas, x, baseY, tw, th, _front.Sway(i) * tw, _front.Wiggle(i) * tw);
        }

        UpdateAndDrawEmbers(ctx, maxH);
    }

    private void UpdateAndDrawEmbers(in RenderContext ctx, float maxH)
    {
        var frame = ctx.Frame;
        float w = ctx.Width, h = ctx.Height, dt = ctx.Dt;

        if (frame.Beat) Spawn((int)(12 + 40 * frame.BeatStrength), w, h, maxH, ctx.Scale);
        _emberDebt += dt * frame.Level * 35f;
        if (_emberDebt >= 1f)
        {
            Spawn((int)_emberDebt, w, h, maxH, ctx.Scale);
            _emberDebt -= (int)_emberDebt;
        }

        var hot = ctx.Palette.Sample(0.85f);
        for (int i = 0; i < MaxEmbers; i++)
        {
            if (_elife[i] <= 0) continue;
            _elife[i] -= dt;
            if (_elife[i] <= 0) continue;
            _evx[i] += ((float)_rng.NextDouble() - 0.5f) * 80f * ctx.Scale * dt;
            _ex[i] += _evx[i] * dt;
            _ey[i] += _evy[i] * dt;
            float t = _elife[i] / _emax[i];
            float flicker = 0.6f + 0.4f * (float)_rng.NextDouble();
            _ember.Color = hot.WithAlpha((byte)(255 * MathF.Min(1f, t * 1.5f) * flicker));
            ctx.Canvas.DrawCircle(_ex[i], _ey[i], _esize[i], _ember);
        }
    }

    private void Spawn(int count, float w, float h, float maxH, float scale)
    {
        for (int k = 0; k < count; k++)
        {
            int i = _nextEmber;
            _nextEmber = (_nextEmber + 1) % MaxEmbers;
            // Bias toward the middle, where the bass flames are tallest.
            float u = (float)(_rng.NextDouble() + _rng.NextDouble()) / 2f;
            _ex[i] = u * w;
            _ey[i] = h - maxH * (0.2f + 0.5f * (float)_rng.NextDouble());
            _evx[i] = ((float)_rng.NextDouble() - 0.5f) * 40f * scale;
            _evy[i] = -(0.25f + 0.5f * (float)_rng.NextDouble()) * Math.Max(maxH, 200f * scale);
            _emax[i] = _elife[i] = 0.8f + 1.4f * (float)_rng.NextDouble();
            _esize[i] = (0.8f + 1.5f * (float)_rng.NextDouble()) * scale;
        }
    }
}
