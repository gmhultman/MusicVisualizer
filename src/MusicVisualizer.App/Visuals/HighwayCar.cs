using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// The Highway visualizer's car: a sporty side-view coupe drawn as layered vector shapes.
/// Geometry is defined in units of the car's length (the canvas is scaled), and every clipped shape
/// (wheel arches, highlights, glass reflection) is intersected once up front — no per-frame clipping.
/// </summary>
internal sealed class HighwayCar
{
    // Geometry contract (the physics relies on it): wheel radius and centres, wheels touching local y = 0.
    public const float WheelRadius = 0.115f;
    public const float WheelOffset = 0.31f;

    private static readonly SKColor Tyre = new(0x14, 0x14, 0x18);
    private static readonly SKColor Sidewall = new(0x2E, 0x2E, 0x36);
    private static readonly SKColor Spoke = new(0x3A, 0x3E, 0x46);
    private static readonly SKColor Hub = new(0xC4, 0xC8, 0xD0);
    private static readonly SKColor Trim = new(0x16, 0x18, 0x1E);
    private static readonly SKColor Needles = new(0x1E, 0x6E, 0x3A);
    private static readonly SKColor NeedlesLight = new(0x3C, 0xA8, 0x5E);

    private readonly SKPath _body;
    private readonly SKPath _glass;
    private readonly SKPath _arches;       // body ∩ arch circles
    private readonly SKPath _shine;        // body ∩ shoulder highlight
    private readonly SKPath _lowerShade;   // body ∩ lower band
    private readonly SKPath _reflection;   // glass ∩ diagonal band
    private readonly SKPath _beam = BuildBeam();
    private readonly SKPath _headlight = BuildHeadlight();
    private readonly SKPath _taillight = BuildTaillight();
    private readonly SKPath _spoiler = BuildSpoiler();
    private readonly SKPath _doorLine = BuildDoorLine();
    private readonly SKPath _tree = BuildTree();
    private readonly SKPath _treeBranches = BuildTreeBranches();

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _bodyPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _glassPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _rimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _beamPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    private SKColor _shaderColor;
    private SKShader? _bodyShader;

    public HighwayCar()
    {
        _body = BuildBody();
        _glass = BuildGlass();

        using var archCircles = new SKPath();
        archCircles.AddCircle(-WheelOffset, -WheelRadius, 0.142f);
        archCircles.AddCircle(WheelOffset, -WheelRadius, 0.142f);
        _arches = _body.Op(archCircles, SKPathOp.Intersect) ?? new SKPath();

        using var shoulder = new SKPath();
        shoulder.MoveTo(-0.47f, -0.247f);
        shoulder.QuadTo(0f, -0.266f, 0.47f, -0.222f);
        shoulder.LineTo(0.47f, -0.21f);
        shoulder.QuadTo(0f, -0.252f, -0.47f, -0.235f);
        shoulder.Close();
        _shine = _body.Op(shoulder, SKPathOp.Intersect) ?? new SKPath();

        using var lower = new SKPath();
        lower.AddRect(new SKRect(-0.52f, -0.16f, 0.52f, -0.08f));
        _lowerShade = _body.Op(lower, SKPathOp.Intersect) ?? new SKPath();
        _lowerShade = _lowerShade.Op(_arches, SKPathOp.Difference) ?? _lowerShade;

        using var band = new SKPath();
        band.MoveTo(-0.05f, -0.4f);
        band.LineTo(0.03f, -0.4f);
        band.LineTo(-0.07f, -0.26f);
        band.LineTo(-0.15f, -0.26f);
        band.Close();
        _reflection = _glass.Op(band, SKPathOp.Intersect) ?? new SKPath();

        // Colour-independent shaders, in car units.
        _glassPaint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, -0.37f), new SKPoint(0, -0.27f),
            new[] { new SKColor(0x40, 0x54, 0x74), new SKColor(0x10, 0x16, 0x22) }, (float[]?)null, SKShaderTileMode.Clamp);
        _rimPaint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, -0.075f), new SKPoint(0, 0.075f),
            new[] { new SKColor(0xE8, 0xEC, 0xF2), new SKColor(0x86, 0x8C, 0x98) }, (float[]?)null, SKShaderTileMode.Clamp);
        _beamPaint.Shader = SKShader.CreateLinearGradient(new SKPoint(0.5f, 0), new SKPoint(1.6f, 0),
            new[] { new SKColor(0xFF, 0xF4, 0xD0, 85), new SKColor(0xFF, 0xF4, 0xD0, 28), new SKColor(0xFF, 0xF4, 0xD0, 0) },
            new[] { 0f, 0.4f, 1f }, SKShaderTileMode.Clamp);
        _shadowPaint.Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 0.5f,
            new[] { new SKColor(0, 0, 0, 110), new SKColor(0, 0, 0, 0) }, (float[]?)null, SKShaderTileMode.Clamp,
            SKMatrix.CreateScale(1f, 0.1f));
    }

    /// <param name="x">Road point midway between the wheels' contact points (local origin).</param>
    /// <param name="groundY">Screen y of that point.</param>
    /// <param name="angle">Radians; positive = nose down.</param>
    /// <param name="length">Bumper-to-bumper length in pixels.</param>
    /// <param name="wheelAngle">Wheel spin in radians.</param>
    /// <param name="squash">0..1 suspension compression after a landing (body sinks ~4% of length).</param>
    /// <param name="glowCanvas">Low-res glow layer under the main canvas, or null when glow is off.</param>
    public void Draw(SKCanvas canvas, SKCanvas? glowCanvas, float x, float groundY, float angle, float length,
                     float wheelAngle, float squash, SKColor bodyColor, bool night, bool christmasTree, float scale)
    {
        EnsureBodyShader(bodyColor);
        float sink = Math.Clamp(squash, 0f, 1f) * 0.04f;

        if (glowCanvas != null) DrawGlow(glowCanvas, x, groundY, angle, length, sink, night);

        canvas.Save();
        canvas.Translate(x, groundY);
        canvas.RotateRadians(angle);
        canvas.Scale(length);

        // Soft contact shadow on the road.
        canvas.DrawOval(0, 0.004f, 0.56f, 0.05f, _shadowPaint);
        if (night) canvas.DrawPath(_beam, _beamPaint);

        // ---- Body (sinks toward the wheels on landing) ----
        canvas.Save();
        canvas.Translate(0, sink);

        _fill.Color = Darken(bodyColor, 0.55f);
        canvas.DrawPath(_spoiler, _fill);
        canvas.DrawPath(_body, _bodyPaint);

        _fill.Color = SKColors.Black.WithAlpha(52);
        canvas.DrawPath(_lowerShade, _fill);
        _fill.Color = SKColors.White.WithAlpha(60);
        canvas.DrawPath(_shine, _fill);
        _fill.Color = Darken(bodyColor, 0.85f);
        canvas.DrawPath(_arches, _fill);

        // Side skirt between the wheels
        _fill.Color = Trim;
        canvas.DrawRoundRect(-0.16f, -0.1f, 0.32f, 0.016f, 0.006f, 0.006f, _fill);

        // Glass with a diagonal reflection, B-pillar and trim
        canvas.DrawPath(_glass, _glassPaint);
        _fill.Color = SKColors.White.WithAlpha(48);
        canvas.DrawPath(_reflection, _fill);
        _fill.Color = Darken(bodyColor, 0.62f);
        canvas.DrawRect(-0.012f, -0.366f, 0.018f, 0.092f, _fill);
        _stroke.Color = Trim.WithAlpha(210);
        _stroke.StrokeWidth = 0.006f;
        canvas.DrawPath(_glass, _stroke);

        // Door line, handle, mirror
        _stroke.Color = Darken(bodyColor, 0.6f).WithAlpha(170);
        _stroke.StrokeWidth = 0.0045f;
        canvas.DrawPath(_doorLine, _stroke);
        _fill.Color = Darken(bodyColor, 0.5f);
        canvas.DrawRoundRect(0.03f, -0.24f, 0.045f, 0.011f, 0.005f, 0.005f, _fill);
        canvas.DrawRoundRect(0.16f, -0.302f, 0.042f, 0.022f, 0.009f, 0.009f, _fill);

        // Lights
        _fill.Color = night ? new SKColor(0xFF, 0xFB, 0xE6) : new SKColor(0xE6, 0xEA, 0xEE);
        canvas.DrawPath(_headlight, _fill);
        _fill.Color = night ? new SKColor(0xFF, 0x30, 0x30) : new SKColor(0xC8, 0x1E, 0x24);
        canvas.DrawPath(_taillight, _fill);

        if (christmasTree) DrawTree(canvas);

        canvas.Restore(); // body sink

        // ---- Wheels (stay on the road) ----
        DrawWheel(canvas, -WheelOffset, wheelAngle);
        DrawWheel(canvas, WheelOffset, wheelAngle);

        canvas.Restore();
    }

    private void DrawWheel(SKCanvas canvas, float cx, float spin)
    {
        float cy = -WheelRadius;
        _fill.Color = Tyre;
        canvas.DrawCircle(cx, cy, WheelRadius, _fill);
        _stroke.Color = Sidewall;
        _stroke.StrokeWidth = 0.008f;
        canvas.DrawCircle(cx, cy, WheelRadius * 0.86f, _stroke);

        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.DrawCircle(0, 0, 0.074f, _rimPaint);
        canvas.RotateRadians(spin);
        _stroke.Color = Spoke;
        _stroke.StrokeWidth = 0.016f;
        for (int i = 0; i < 5; i++)
        {
            float a = i * MathF.Tau / 5;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            canvas.DrawLine(c * 0.022f, s * 0.022f, c * 0.066f, s * 0.066f, _stroke);
        }
        _fill.Color = Hub;
        canvas.DrawCircle(0, 0, 0.02f, _fill);
        _fill.Color = Spoke;
        canvas.DrawCircle(0, 0, 0.008f, _fill);
        canvas.Restore();
    }

    private void DrawTree(SKCanvas canvas)
    {
        // Trunk stub sticking out the back
        _fill.Color = new SKColor(0x6A, 0x42, 0x22);
        canvas.DrawRect(-0.215f, -0.408f, 0.06f, 0.016f, _fill);
        // The tree, lying on the roof with its tip forward
        _fill.Color = Needles;
        canvas.DrawPath(_tree, _fill);
        _stroke.Color = NeedlesLight;
        _stroke.StrokeWidth = 0.005f;
        canvas.DrawPath(_treeBranches, _stroke);
        // Baubles
        _fill.Color = new SKColor(0xE8, 0x26, 0x3A);
        canvas.DrawCircle(-0.11f, -0.43f, 0.012f, _fill);
        canvas.DrawCircle(0.03f, -0.418f, 0.011f, _fill);
        _fill.Color = new SKColor(0xFF, 0xC2, 0x3D);
        canvas.DrawCircle(-0.04f, -0.437f, 0.011f, _fill);
        canvas.DrawCircle(0.1f, -0.404f, 0.009f, _fill);
        // Straps holding it down
        _stroke.Color = new SKColor(0xC8, 0x1E, 0x2E);
        _stroke.StrokeWidth = 0.008f;
        canvas.DrawLine(-0.075f, -0.382f, -0.068f, -0.452f, _stroke);
        canvas.DrawLine(0.065f, -0.38f, 0.06f, -0.432f, _stroke);
    }

    private void DrawGlow(SKCanvas glow, float x, float groundY, float angle, float length, float sink, bool night)
    {
        glow.Save();
        glow.Translate(x, groundY);
        glow.RotateRadians(angle);
        glow.Scale(length);
        glow.Translate(0, sink);

        _fill.Color = night ? new SKColor(0xFF, 0xF0, 0xC0, 230) : new SKColor(0xFF, 0xF0, 0xC0, 80);
        glow.DrawCircle(0.5f, -0.205f, night ? 0.09f : 0.045f, _fill);
        if (night)
        {
            _fill.Color = new SKColor(0xFF, 0xF0, 0xC0, 60);
            glow.DrawPath(_beam, _fill);
        }
        _fill.Color = night ? new SKColor(0xFF, 0x20, 0x20, 220) : new SKColor(0xFF, 0x20, 0x20, 60);
        glow.DrawCircle(-0.49f, -0.225f, night ? 0.075f : 0.04f, _fill);
        glow.Restore();
    }

    private void EnsureBodyShader(SKColor c)
    {
        if (_bodyShader != null && c == _shaderColor) return;
        _bodyShader?.Dispose();
        _bodyShader = SKShader.CreateLinearGradient(new SKPoint(0, -0.385f), new SKPoint(0, -0.085f),
            new[] { Mix(c, SKColors.White, 0.42f), Mix(c, SKColors.White, 0.1f), c, Darken(c, 0.45f) },
            new[] { 0f, 0.42f, 0.6f, 1f }, SKShaderTileMode.Clamp);
        _bodyPaint.Shader = _bodyShader;
        _shaderColor = c;
    }

    // ---------------------------------------------------------------- geometry (car units, y up = negative)

    private static SKPath BuildBody()
    {
        var p = new SKPath();
        p.MoveTo(-0.49f, -0.12f);
        p.LineTo(-0.505f, -0.195f);                         // rear bumper
        p.QuadTo(-0.505f, -0.25f, -0.45f, -0.258f);         // rear deck corner
        p.LineTo(-0.32f, -0.266f);                          // trunk
        p.QuadTo(-0.24f, -0.276f, -0.17f, -0.33f);          // fastback
        p.QuadTo(-0.09f, -0.38f, 0.0f, -0.382f);            // roof
        p.QuadTo(0.09f, -0.382f, 0.16f, -0.33f);            // windshield top
        p.LineTo(0.235f, -0.278f);                          // windshield base
        p.QuadTo(0.4f, -0.262f, 0.47f, -0.232f);            // hood
        p.QuadTo(0.518f, -0.214f, 0.512f, -0.168f);         // nose
        p.LineTo(0.5f, -0.115f);                            // front bumper
        p.QuadTo(0.49f, -0.084f, 0.44f, -0.084f);           // chin
        p.LineTo(-0.44f, -0.084f);
        p.QuadTo(-0.488f, -0.084f, -0.49f, -0.12f);
        p.Close();
        return p;
    }

    private static SKPath BuildGlass()
    {
        var p = new SKPath();
        p.MoveTo(-0.19f, -0.276f);
        p.QuadTo(-0.13f, -0.318f, -0.08f, -0.348f);
        p.QuadTo(-0.02f, -0.366f, 0.02f, -0.364f);
        p.QuadTo(0.09f, -0.362f, 0.14f, -0.326f);
        p.LineTo(0.198f, -0.28f);
        p.Close();
        return p;
    }

    private static SKPath BuildDoorLine()
    {
        var p = new SKPath();
        p.MoveTo(-0.012f, -0.274f);
        p.QuadTo(-0.03f, -0.19f, -0.02f, -0.11f);
        p.MoveTo(0.205f, -0.274f);
        p.QuadTo(0.2f, -0.2f, 0.17f, -0.112f);
        p.MoveTo(-0.02f, -0.11f);
        p.LineTo(0.17f, -0.112f);
        return p;
    }

    private static SKPath BuildHeadlight()
    {
        var p = new SKPath();
        p.MoveTo(0.44f, -0.232f);
        p.QuadTo(0.49f, -0.222f, 0.509f, -0.2f);
        p.LineTo(0.507f, -0.187f);
        p.QuadTo(0.47f, -0.195f, 0.44f, -0.216f);
        p.Close();
        return p;
    }

    private static SKPath BuildTaillight()
    {
        var p = new SKPath();
        p.MoveTo(-0.503f, -0.232f);
        p.QuadTo(-0.49f, -0.252f, -0.455f, -0.256f);
        p.LineTo(-0.455f, -0.23f);
        p.QuadTo(-0.48f, -0.222f, -0.505f, -0.205f);
        p.Close();
        return p;
    }

    private static SKPath BuildSpoiler()
    {
        var p = new SKPath();
        p.MoveTo(-0.47f, -0.26f);
        p.LineTo(-0.49f, -0.29f);
        p.LineTo(-0.38f, -0.29f);
        p.LineTo(-0.37f, -0.268f);
        p.Close();
        return p;
    }

    private static SKPath BuildBeam()
    {
        var p = new SKPath();
        p.MoveTo(0.5f, -0.22f);
        p.LineTo(1.6f, -0.32f);
        p.LineTo(1.6f, -0.02f);
        p.LineTo(0.5f, -0.18f);
        p.Close();
        return p;
    }

    private static SKPath BuildTree()
    {
        // Side view of a fir lying on the roof: wide at the back, jagged branch tips, pointed at the front.
        var p = new SKPath();
        p.MoveTo(-0.165f, -0.378f);
        p.LineTo(-0.175f, -0.432f);
        p.LineTo(-0.13f, -0.458f);
        p.LineTo(-0.115f, -0.44f);
        p.LineTo(-0.06f, -0.462f);
        p.LineTo(-0.05f, -0.44f);
        p.LineTo(0.01f, -0.452f);
        p.LineTo(0.015f, -0.43f);
        p.LineTo(0.075f, -0.436f);
        p.LineTo(0.075f, -0.416f);
        p.LineTo(0.17f, -0.398f);
        p.LineTo(0.12f, -0.38f);
        p.Close();
        return p;
    }

    private static SKPath BuildTreeBranches()
    {
        var p = new SKPath();
        p.MoveTo(-0.16f, -0.41f);
        p.LineTo(0.14f, -0.395f);          // spine
        p.MoveTo(-0.12f, -0.408f); p.LineTo(-0.13f, -0.45f);
        p.MoveTo(-0.06f, -0.406f); p.LineTo(-0.065f, -0.452f);
        p.MoveTo(0.0f, -0.403f); p.LineTo(0.005f, -0.442f);
        p.MoveTo(0.06f, -0.4f); p.LineTo(0.068f, -0.428f);
        return p;
    }

    private static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t));

    private static SKColor Darken(SKColor c, float k) => new((byte)(c.Red * (1 - k)), (byte)(c.Green * (1 - k)), (byte)(c.Blue * (1 - k)));
}
