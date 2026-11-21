using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// Draws stylised flame "tongues" as layered vector shapes (outer → inner, cool → hot colours).
/// Crisp at any size and cheap. Shared by the Fire and Fireplace visualizers.
/// </summary>
public sealed class FlameRenderer
{
    // Outer (cool), middle, inner core (hot): relative width, height and sway of each layer.
    private static readonly (float w, float h, float s)[] Layers = [(1.00f, 1.00f, 1.00f), (0.66f, 0.70f, 0.80f), (0.34f, 0.40f, 0.55f)];

    private readonly SKPath _path = new();
    private readonly SKPaint[] _layerPaint = Layers.Select(_ => new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill }).ToArray();
    private readonly SKPaint _glow = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
    private readonly SKShader?[] _shaders = new SKShader?[Layers.Length];
    private (Palette? palette, float baseY, float maxH, float alpha) _rowKey;

    /// <summary>
    /// Sets up colouring for a row of flames sharing one baseline: hot at the base, cooling with height and
    /// fading out at the tips. Call before <see cref="DrawTongue"/> whenever the row or palette changes.
    /// </summary>
    public void BeginRow(Palette palette, float baseY, float maxHeight, float alpha = 1f)
    {
        var key = (palette, baseY, maxHeight, alpha);
        if (key == _rowKey && _shaders[0] != null) return;
        _rowKey = key;

        byte A(float a) => (byte)(255 * a * alpha);
        var top = new SKPoint(0, baseY - maxHeight);
        var bottom = new SKPoint(0, baseY);
        (SKColor[] colors, float[] pos)[] gradients =
        [
            // Outer: orange at the base → red → transparent tip
            ([palette.Sample(0.45f).WithAlpha(A(0.95f)), palette.Sample(0.1f).WithAlpha(A(0.9f)), palette.Sample(0f).WithAlpha(A(0.55f)), palette.Sample(0f).WithAlpha(0)],
             [0f, 0.45f, 0.8f, 1f]),
            // Middle: yellow → orange → fading
            ([palette.Sample(0.85f).WithAlpha(A(1f)), palette.Sample(0.5f).WithAlpha(A(0.95f)), palette.Sample(0.3f).WithAlpha(0)],
             [0f, 0.5f, 0.85f]),
            // Core: near-white → yellow
            ([new SKColor(255, 250, 235).WithAlpha(A(1f)), palette.Sample(0.95f).WithAlpha(A(0.95f)), palette.Sample(0.8f).WithAlpha(0)],
             [0f, 0.35f, 0.6f]),
        ];
        for (int i = 0; i < Layers.Length; i++)
        {
            _shaders[i]?.Dispose();
            // Gradients run from the baseline (0) up to maxHeight (1).
            _shaders[i] = SKShader.CreateLinearGradient(bottom, top, gradients[i].colors, gradients[i].pos, SKShaderTileMode.Clamp);
            _layerPaint[i].Shader = _shaders[i];
        }
        _glow.Color = palette.Sample(0.4f).WithAlpha(A(0.45f));
    }

    /// <summary>One flame. <paramref name="sway"/> moves the tip sideways; <paramref name="wiggle"/> bends the middle.
    /// <paramref name="glowCanvas"/> (the low-res glow layer, if on) gets a blurred halo.
    /// <paramref name="layers"/> &lt; 3 skips the inner layers (for flames mostly hidden behind others).</summary>
    public void DrawTongue(SKCanvas canvas, float cx, float baseY, float width, float height, float sway, float wiggle,
                           SKCanvas? glowCanvas = null, float scale = 1f, int layers = 3)
    {
        if (height < 1f || width < 1f) return;

        if (glowCanvas != null)
        {
            BuildTongue(cx, baseY, width * 1.3f, height * 0.95f, sway * 0.7f, wiggle);
            glowCanvas.DrawPath(_path, _glow);
        }

        for (int i = 0; i < Math.Min(layers, Layers.Length); i++)
        {
            var l = Layers[i];
            BuildTongue(cx, baseY, width * l.w, height * l.h, sway * l.s, wiggle * l.s);
            canvas.DrawPath(_path, _layerPaint[i]);
        }
    }

    private void BuildTongue(float cx, float baseY, float w, float h, float sway, float wiggle)
    {
        float half = w / 2;
        float tipX = cx + sway, tipY = baseY - h;
        float midX = cx + sway * 0.4f + wiggle;
        _path.Reset();
        _path.MoveTo(cx - half, baseY);
        // Full rounded belly low down, then a long curl up to the tip.
        _path.CubicTo(cx - half * 1.35f, baseY - h * 0.3f, midX - half * 0.7f, baseY - h * 0.6f, tipX, tipY);
        _path.CubicTo(midX + half * 0.7f, baseY - h * 0.6f, cx + half * 1.35f, baseY - h * 0.3f, cx + half, baseY);
        _path.QuadTo(cx, baseY + half * 0.5f, cx - half, baseY);
        _path.Close();
    }

}

/// <summary>
/// Animated state for a row of flames: each tongue follows its share of the spectrum with
/// fast rise / slow fall, flickers, sways, and jumps on beats.
/// </summary>
public sealed class FlameRow
{
    private float[] _height = [];
    private float[] _phase = [];
    private float[] _band = [];
    private float _time;

    public int Count => _height.Length;

    /// <summary>(Re)builds the row. <paramref name="bandFor"/> maps tongue index → band position 0..1 (0 = bass).</summary>
    public void Ensure(int count, Func<int, float> bandFor, int seed)
    {
        if (_height.Length == count) return;
        var rng = new Random(seed);
        _height = new float[count];
        _phase = new float[count];
        _band = new float[count];
        for (int i = 0; i < count; i++)
        {
            _phase[i] = (float)(rng.NextDouble() * 100);
            _band[i] = bandFor(i);
        }
    }

    public void Update(Audio.AudioFrame frame, float dt)
    {
        _time += dt;
        var bands = frame.Bands;
        int n = bands.Length;
        float rise = 1f - MathF.Exp(-dt / 0.05f), fall = 1f - MathF.Exp(-dt / 0.35f);
        for (int i = 0; i < _height.Length; i++)
        {
            float bf = _band[i] * (n - 1) * 0.85f;
            int b = (int)bf;
            float v = bands[b] + (bands[Math.Min(b + 1, n - 1)] - bands[b]) * (bf - b);
            float target = 0.12f + 0.88f * v + 0.35f * frame.Pulse;  // never fully out; jumps on beats
            _height[i] += (target - _height[i]) * (target > _height[i] ? rise : fall);
        }
    }

    /// <summary>Height 0..~1.3 including flicker.</summary>
    public float Height(int i) =>
        _height[i] * (0.86f + 0.14f * MathF.Sin(_time * 11f + _phase[i]) * MathF.Sin(_time * 7.3f + _phase[i] * 1.7f));

    /// <summary>Sideways tip offset in units of the tongue's width.</summary>
    public float Sway(int i) =>
        0.35f * MathF.Sin(_time * 2.1f + _phase[i]) + 0.2f * MathF.Sin(_time * 5.3f + _phase[i] * 2.3f);

    public float Wiggle(int i) => 0.25f * MathF.Sin(_time * 8.7f + _phase[i] * 0.7f);
}
