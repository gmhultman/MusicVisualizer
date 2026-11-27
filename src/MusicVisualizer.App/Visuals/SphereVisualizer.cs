using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// 3D globe of glowing points that bulges with the spectrum (bass around the equator, treble at the poles),
/// circled by two counter-rotating orbit rings. Spins faster with the music and kicks on beats.
/// </summary>
public sealed class SphereVisualizer : IVisualizer
{
    private const int PointCount = 1400;
    private const int RingPoints = 200;
    private const float CameraDistance = 3.4f;

    private readonly float[] _x = new float[PointCount], _y = new float[PointCount], _z = new float[PointCount];
    private readonly float[] _lat = new float[PointCount];    // 0 equator .. 1 poles
    private readonly float[] _jitter = new float[PointCount];

    private float _spin, _ringSpin, _time;

    private readonly SKPaint _dot = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _core = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };

    public SphereVisualizer()
    {
        // Fibonacci sphere: evenly spread points without clumping at the poles.
        float golden = MathF.PI * (3f - MathF.Sqrt(5f));
        var rng = new Random(7);
        for (int i = 0; i < PointCount; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / PointCount;
            float r = MathF.Sqrt(1f - y * y);
            float phi = i * golden;
            _x[i] = MathF.Cos(phi) * r;
            _y[i] = y;
            _z[i] = MathF.Sin(phi) * r;
            _lat[i] = MathF.Abs(y);
            _jitter[i] = (float)rng.NextDouble();
        }
    }

    public string Name => "Sphere";
    public VisualizerCategory Category => VisualizerCategory.Abstract;

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        var bands = frame.Bands;
        int n = bands.Length;
        var canvas = ctx.Canvas;
        float cx = ctx.Width / 2, cy = ctx.Height / 2;
        float size = MathF.Min(ctx.Width, ctx.Height);
        float focal = size * 0.27f * CameraDistance;

        _time += ctx.Dt;
        _spin += ctx.Dt * (0.25f + 0.9f * frame.Mid + 2.5f * frame.Pulse);
        _ringSpin -= ctx.Dt * (0.4f + 1.2f * frame.Treble + 1.5f * frame.Pulse);
        float tilt = 0.4f + 0.18f * MathF.Sin(_time * 0.23f);
        float cosY = MathF.Cos(_spin), sinY = MathF.Sin(_spin);
        float cosX = MathF.Cos(tilt), sinX = MathF.Sin(tilt);

        // Core glow
        var coreColor = ctx.Palette.Sample(0.1f);
        float coreR = focal / CameraDistance * (1.1f + 0.25f * frame.Bass);
        using (var shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), coreR,
                   new[] { coreColor.WithAlpha((byte)(50 + 110 * frame.Pulse)), coreColor.WithAlpha(0) }, (float[]?)null, SKShaderTileMode.Clamp))
        {
            _core.Shader = shader;
            canvas.DrawCircle(cx, cy, coreR, _core);
            _core.Shader = null;
        }

        // Sphere
        for (int i = 0; i < PointCount; i++)
        {
            float bf = _lat[i] * (n - 1) * 0.8f;
            int b = (int)bf;
            float v = bands[b] + (bands[Math.Min(b + 1, n - 1)] - bands[b]) * (bf - b);
            float r = 1f + 0.5f * v * (0.55f + 0.45f * _jitter[i]) + 0.06f * frame.Pulse;

            Project(_x[i] * r, _y[i] * r, _z[i] * r, cosY, sinY, cosX, sinX, cx, cy, focal,
                    out float sx, out float sy, out float zc);

            float depth = Math.Clamp((zc - (CameraDistance - 1.5f)) / 3f, 0f, 1f); // 0 front .. 1 back
            float persp = CameraDistance / zc;
            byte alpha = (byte)(255 * (1f - 0.8f * depth));
            var color = ctx.Palette.Sample(bf / (n - 1));
            float dotR = (0.9f + 2.4f * v) * ctx.Scale * persp;

            if (ctx.GlowCanvas is { } glowCanvas && v > 0.35f)
            {
                // Low-res layer: the GPU's upscaling softens these into halos for free.
                _dot.Color = color.WithAlpha((byte)(alpha * 0.35f));
                glowCanvas.DrawCircle(sx, sy, dotR * 3f, _dot);
            }
            _dot.Color = color.WithAlpha(alpha);
            canvas.DrawCircle(sx, sy, dotR, _dot);
        }

        // Orbit rings (each tilted differently, spinning against the sphere).
        DrawRing(ctx, 1.7f, 0.55f, _ringSpin, cosY, sinY, cosX, sinX, cx, cy, focal);
        DrawRing(ctx, 2.0f, -0.35f, -_ringSpin * 0.7f, cosY, sinY, cosX, sinX, cx, cy, focal);
    }

    private void DrawRing(in RenderContext ctx, float radius, float ringTilt, float ringSpin,
                          float cosY, float sinY, float cosX, float sinX, float cx, float cy, float focal)
    {
        var bands = ctx.Frame.Bands;
        int n = bands.Length;
        float ct = MathF.Cos(ringTilt), st = MathF.Sin(ringTilt);

        for (int j = 0; j < RingPoints; j++)
        {
            float u = j / (float)RingPoints;
            float m = u < 0.5f ? u * 2f : (1f - u) * 2f;           // mirrored so the ring is symmetric
            int b = (int)(m * (n - 1) * 0.8f);
            float v = bands[b];
            float a = u * MathF.Tau + ringSpin;
            float r = radius * (1f + 0.12f * v);
            float x = MathF.Cos(a) * r, z = MathF.Sin(a) * r;
            float y = -z * st;                                      // tilt the ring's plane
            z *= ct;

            Project(x, y, z, cosY, sinY, cosX, sinX, cx, cy, focal, out float sx, out float sy, out float zc);
            float depth = Math.Clamp((zc - (CameraDistance - 2.2f)) / 4.4f, 0f, 1f);
            float persp = CameraDistance / zc;
            _dot.Color = ctx.Palette.Sample(m).WithAlpha((byte)(230 * (1f - 0.75f * depth) * (0.35f + 0.65f * v)));
            ctx.Canvas.DrawCircle(sx, sy, (0.8f + 1.8f * v) * ctx.Scale * persp, _dot);
        }
    }

    private static void Project(float x, float y, float z, float cosY, float sinY, float cosX, float sinX,
                                float cx, float cy, float focal, out float sx, out float sy, out float zc)
    {
        // Rotate around Y (spin), then X (tilt), then perspective-project.
        float x1 = x * cosY + z * sinY;
        float z1 = -x * sinY + z * cosY;
        float y2 = y * cosX - z1 * sinX;
        float z2 = y * sinX + z1 * cosX;
        zc = z2 + CameraDistance;
        float s = focal / zc;
        sx = cx + x1 * s;
        sy = cy + y2 * s;
    }
}
