using MusicVisualizer.Audio;
using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// Side-scrolling drive: the road's hills and valleys are generated from the music as it plays (bass builds
/// big hills, mids add rolls, beats throw in ramps) and a car drives over them, catching air off the crests.
/// The scenery follows the colour scheme — neon city at night, sunset palms, aurora forest, desert, snowy
/// cabins, sunny hills (see <see cref="HighwayTheme"/>).
/// </summary>
public sealed class HighwayVisualizer : IVisualizer
{
    private const int StarCount = 110;
    private const int SnowCount = 170;
    private const byte OverlayGroundAlpha = 90;   // ground tint under the road in overlay mode (0 = none)

    private readonly HighwayCar _car = new();
    private readonly Random _rng = new(41);

    // Road: heights (≈0..1, 1 = top of a big hill) at fixed spacing; index 0 is the leftmost sample.
    private float[] _road = [];
    private float _spacing, _scroll, _world;
    private float _gen, _gen2;
    private int _bumpLeft, _bumpLen;
    private float _bumpAmp;
    private bool _pendingBeat;
    private float _pendingStrength;

    // Car state
    private float _carY, _vy, _angle, _squash, _wheel, _prevRoadY;
    private bool _grounded, _carReady;

    // Cached scenery
    private HighwayTheme? _theme;
    private (Palette? palette, int w, int h, bool overlay) _cacheKey;
    private SKImage? _sky, _far, _near;
    private float _farTop, _nearTop;
    private readonly List<SKPoint> _lights = [];

    private readonly float[] _starX = new float[StarCount], _starY = new float[StarCount], _starPhase = new float[StarCount], _starSize = new float[StarCount];
    private readonly float[] _snowX = new float[SnowCount], _snowY = new float[SnowCount], _snowSize = new float[SnowCount], _snowPhase = new float[SnowCount];
    private float _time;

    private readonly SKPath _path = new();
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Butt, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _plus = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _image = new() { IsAntialias = false };

    public HighwayVisualizer()
    {
        for (int i = 0; i < StarCount; i++)
        {
            _starX[i] = (float)_rng.NextDouble();
            _starY[i] = (float)Math.Pow(_rng.NextDouble(), 1.4);   // denser toward the top
            _starPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
            _starSize[i] = 0.6f + 1.1f * (float)_rng.NextDouble();
        }
        for (int i = 0; i < SnowCount; i++)
        {
            _snowX[i] = (float)_rng.NextDouble();
            _snowY[i] = (float)_rng.NextDouble();
            _snowSize[i] = 0.8f + 2f * (float)_rng.NextDouble();
            _snowPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
        }
    }

    public string Name => "Highway";
    public VisualizerCategory Category => VisualizerCategory.Scenes;

    // The sky is opaque, so lights (sun, lamps, road edge, aurora, car lights) bloom over the scene instead.
    public bool GlowAbove => true;

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        float w = ctx.Width, h = ctx.Height, s = ctx.Scale, dt = ctx.Dt;
        bool strip = h < w * 0.35f;
        _time += dt;

        // ---- layout
        // Full-screen overlay keeps the road low with smaller hills so it covers less of the desktop.
        float baseY = ctx.Overlay ? (strip ? h * 0.78f : h * 0.9f) : h * 0.72f;
        float amp = strip ? h * 0.4f : ctx.Overlay ? h * 0.13f : h * 0.2f;   // height of a "full" hill
        float nearBase = baseY - amp * 0.55f;
        float farBase = nearBase - h * 0.02f;
        float farTop = farBase - h * 0.24f;
        float propH = h * 0.15f;
        float carLen = strip ? h * 0.45f : MathF.Min(w * 0.11f, h * 0.2f);
        float carX = w * 0.28f;
        float roadT = MathF.Max(5f * s, carLen * 0.075f);

        EnsureRoad(w, s);
        EnsureScenery(ctx, (int)w, (int)h, farTop, farBase, nearBase, propH);
        var theme = _theme!;

        // ---- scroll the road; new samples enter at the right with the music as it is now
        if (frame.Beat) { _pendingBeat = true; _pendingStrength = Math.Max(_pendingStrength, frame.BeatStrength); }
        float speed = (240f + 420f * frame.Level + 160f * frame.Pulse) * s;
        _scroll += speed * dt;
        _world += speed * dt;
        float maxStep = 0.6f * _spacing / amp;                     // keeps slopes drivable (≤ ~31°)
        while (_scroll >= _spacing)
        {
            _scroll -= _spacing;
            Array.Copy(_road, 1, _road, 0, _road.Length - 1);
            _road[^1] = NextSample(frame, maxStep, s);
        }

        // ---- background
        if (!ctx.Overlay)
        {
            ctx.Canvas.DrawImage(_sky!, 0, 0, _image);
            if (theme.Stars) DrawStars(ctx, frame, nearBase);
            if (theme.Aurora) DrawAurora(ctx, frame);
            if (theme.SunSize > 0) DrawSun(ctx, theme, frame, farBase, farTop);
            if (theme.Moon && ctx.GlowCanvas is { } g)
            {
                _fill.Color = new SKColor(0xC8, 0xD8, 0xFF, 50);
                g.DrawCircle(w * 0.78f, nearBase * 0.32f, h * 0.11f, _fill);
            }
            DrawTiled(ctx.Canvas, _far!, _world * 0.12f, _farTop, w);
            float nearOffset = DrawTiled(ctx.Canvas, _near!, _world * 0.45f, _nearTop, w);
            DrawPropLights(ctx, theme, nearOffset, w, s);
        }

        // ---- road
        DrawRoad(ctx, theme, baseY, amp, roadT, w, h, s);

        // ---- car
        UpdateCar(frame, dt, carX, carLen, h, speed, baseY, amp);
        var body = ctx.Palette.Colors[0];
        _car.Draw(ctx.Canvas, ctx.GlowCanvas, carX, _carY, _angle, carLen, _wheel, _squash, body,
                  theme.Night, theme.Name == "Christmas", s);

        if (theme.Snow && !strip) DrawSnow(ctx, speed);
    }

    // ------------------------------------------------------------------ road

    private void EnsureRoad(float w, float scale)
    {
        float spacing = 10f * scale;
        int n = (int)MathF.Ceiling(w / spacing) + 4;
        if (n == _road.Length && spacing == _spacing) return;
        var old = _road;
        _road = new float[n];
        // Start with gentle rolling hills (not a flat line) until the music fills the road;
        // on resize keep the existing road, right-aligned.
        for (int i = 0; i < n; i++) _road[i] = 0.25f + Wander(i * 10f);
        int keep = Math.Min(old.Length, n);
        if (keep > 0) Array.Copy(old, old.Length - keep, _road, n - keep, keep);
        _spacing = spacing;
        if (old.Length == 0) _gen = _gen2 = _road[^1];
    }

    private float NextSample(AudioFrame frame, float maxStep, float scale)
    {
        // Bass builds the big hills, mids add smaller rolls; a slow wander keeps quiet passages from going flat.
        float worldDip = _world / scale;
        float target = -0.1f + 0.95f * frame.Bass + 0.45f * frame.Mid + Wander(worldDip);
        // Two-stage smoothing turns the music's jumps into rounded hills rather than zigzags.
        _gen += (target - _gen) * 0.1f;
        _gen2 += (_gen - _gen2) * 0.1f;

        float v = _gen2;
        if (_pendingBeat && _bumpLeft == 0)
        {
            // A beat drops a smooth ramp onto the road ahead.
            _bumpLen = 18;
            _bumpLeft = _bumpLen;
            _bumpAmp = 0.1f + 0.22f * _pendingStrength;
        }
        _pendingBeat = false;
        _pendingStrength = 0;
        if (_bumpLeft > 0)
        {
            float k = (_bumpLen - _bumpLeft + 0.5f) / _bumpLen;
            v += _bumpAmp * 0.5f * (1f - MathF.Cos(MathF.Tau * k)); // raised cosine: no sharp corners
            _bumpLeft--;
        }
        float last = _road[^1];
        v = Math.Clamp(v, last - maxStep, last + maxStep);
        return Math.Clamp(v, -0.35f, 1.3f);
    }

    /// <summary>Gentle rolling baseline so the road is never dead flat.</summary>
    private static float Wander(float worldDip) =>
        0.14f * MathF.Sin(worldDip * 0.0031f) + 0.06f * MathF.Sin(worldDip * 0.0089f + 1.3f);

    private float PointY(int i, float baseY, float amp) => baseY - _road[i] * amp;

    /// <summary>Exact height of the drawn (smoothed) road at screen x.</summary>
    private float RoadYAt(float x, float baseY, float amp)
    {
        // The road is drawn as quadratic curves between sample midpoints with the samples as control points;
        // with even spacing x is linear in t, so t comes straight from x.
        float u = (x + _scroll) / _spacing - 0.5f;
        int k = Math.Clamp((int)MathF.Floor(u), 0, _road.Length - 3);
        float t = Math.Clamp(u - k, 0f, 1f);
        float y0 = PointY(k, baseY, amp), y1 = PointY(k + 1, baseY, amp), y2 = PointY(k + 2, baseY, amp);
        float m0 = (y0 + y1) / 2, m1 = (y1 + y2) / 2;
        return (1 - t) * (1 - t) * m0 + 2 * (1 - t) * t * y1 + t * t * m1;
    }

    private void BuildRoadPath(SKPath path, float baseY, float amp, float dy)
    {
        path.Reset();
        float X(int i) => i * _spacing - _scroll;
        path.MoveTo(X(0), PointY(0, baseY, amp) + dy);
        for (int i = 0; i < _road.Length - 1; i++)
        {
            float mx = (X(i) + X(i + 1)) / 2, my = (PointY(i, baseY, amp) + PointY(i + 1, baseY, amp)) / 2 + dy;
            if (i == 0) path.LineTo(mx, my);
            else path.QuadTo(X(i), PointY(i, baseY, amp) + dy, mx, my);
        }
        int last = _road.Length - 1;
        path.LineTo(X(last), PointY(last, baseY, amp) + dy);
    }

    private void DrawRoad(in RenderContext ctx, HighwayTheme t, float baseY, float amp, float roadT, float w, float h, float s)
    {
        var canvas = ctx.Canvas;

        // Ground below the road. In overlay mode it's only a light tint that fades out a short way below
        // the road, so the desktop stays visible underneath.
        BuildRoadPath(_path, baseY, amp, roadT - 1f);
        _path.LineTo(w + 10, h + 10);
        _path.LineTo(-10, h + 10);
        _path.Close();
        using (var ground = ctx.Overlay
                   ? SKShader.CreateLinearGradient(new SKPoint(0, baseY - amp), new SKPoint(0, baseY + amp * 0.9f),
                       new[] { t.GroundTop.WithAlpha(OverlayGroundAlpha), t.GroundTop.WithAlpha(0) }, null, SKShaderTileMode.Clamp)
                   : SKShader.CreateLinearGradient(new SKPoint(0, baseY - amp), new SKPoint(0, h),
                       new[] { t.GroundTop, t.GroundBottom }, null, SKShaderTileMode.Clamp))
        {
            _fill.Color = SKColors.White; // shader fills are modulated by the paint's alpha
            _fill.Shader = ground;
            canvas.DrawPath(_path, _fill);
            _fill.Shader = null;
        }

        if (t.Grid) DrawGrid(ctx, baseY, amp, roadT, w, h, s);

        // Asphalt band
        BuildRoadPath(_path, baseY, amp, roadT / 2);
        _stroke.PathEffect = null;
        _stroke.Color = t.Road;
        _stroke.StrokeWidth = roadT;
        canvas.DrawPath(_path, _stroke);

        // Dashed centre line, moving with the road
        float dash = 22f * s, gap = 18f * s;
        using (var effect = SKPathEffect.CreateDash([dash, gap], _world % (dash + gap)))
        {
            _stroke.PathEffect = effect;
            _stroke.Color = t.CenterLine;
            _stroke.StrokeWidth = MathF.Max(1.5f * s, roadT * 0.12f);
            canvas.DrawPath(_path, _stroke);
            _stroke.PathEffect = null;
        }

        // Top edge (and its glow)
        BuildRoadPath(_path, baseY, amp, 0);
        _stroke.Color = t.RoadEdge;
        _stroke.StrokeWidth = MathF.Max(1.5f * s, roadT * 0.16f);
        canvas.DrawPath(_path, _stroke);
        if (ctx.GlowCanvas is { } g && (t.EdgeGlow || t.Night))
        {
            _stroke.Color = t.RoadEdge.WithAlpha(t.EdgeGlow ? (byte)200 : (byte)70);
            _stroke.StrokeWidth = roadT * 1.2f;
            g.DrawPath(_path, _stroke);
        }
    }

    private void DrawGrid(in RenderContext ctx, float baseY, float amp, float roadT, float w, float h, float s)
    {
        var canvas = ctx.Canvas;
        // Horizontal lines follow the road's shape, spreading out toward the viewer.
        // In overlay mode only a few, quickly fading, so the desktop shows through.
        int lines = ctx.Overlay ? 3 : 6;
        for (int k = 1; k <= lines; k++)
        {
            float dy = roadT + MathF.Pow(k, 1.55f) * 9f * s;
            BuildRoadPath(_path, baseY, amp, dy);
            byte alpha = ctx.Overlay ? (byte)(110 - k * 30) : (byte)(150 - k * 14);
            _stroke.Color = ctx.Palette.Sample(k / 6f).WithAlpha(alpha);
            _stroke.StrokeWidth = 1.2f * s;
            canvas.DrawPath(_path, _stroke);
        }
        if (ctx.Overlay) return;

        // Vertical lines anchored to the world so they scroll with the road.
        float step = 70f * s;
        _stroke.Color = ctx.Palette.Sample(0.5f).WithAlpha(70);
        _stroke.StrokeWidth = 1f * s;
        for (float x = -(_world % step); x < w; x += step)
            canvas.DrawLine(x, RoadYAt(x, baseY, amp) + roadT, x, h, _stroke);
    }

    // ------------------------------------------------------------------ car

    private void UpdateCar(AudioFrame frame, float dt, float carX, float L, float h, float speed, float baseY, float amp)
    {
        float yRear = RoadYAt(carX - 0.31f * L, baseY, amp);
        float yFront = RoadYAt(carX + 0.31f * L, baseY, amp);
        float roadY = (yRear + yFront) / 2;
        float slope = MathF.Atan2(yFront - yRear, 0.62f * L);

        if (!_carReady)
        {
            _carY = _prevRoadY = roadY;
            _angle = slope;
            _grounded = _carReady = true;
        }

        float gravity = h * 2.8f;
        float roadVy = Math.Clamp((roadY - _prevRoadY) / dt, -h * 4f, h * 4f);
        _prevRoadY = roadY;

        _vy += gravity * dt;
        _carY += _vy * dt;
        if (_carY >= roadY)
        {
            // Touching the road. A crest falling away faster than gravity is what launches the car.
            float impact = _vy - roadVy;
            if (!_grounded && impact > h * 0.25f) _squash = MathF.Min(1f, impact / (h * 1.4f));
            _carY = roadY;
            _vy = roadVy;
            _grounded = true;
        }
        else if (_carY < roadY - 2f)
        {
            _grounded = false;
        }

        // Strong beats make it hop.
        if (_grounded && frame.Beat && frame.BeatStrength > 0.55f)
        {
            _vy = roadVy - h * (0.55f + 0.5f * frame.BeatStrength);
            _grounded = false;
        }

        // Tilt with the road; in the air, ease toward it and lift the nose a little while rising.
        float target = _grounded ? slope : slope * 0.4f + Math.Clamp(_vy / (h * 6f), -0.2f, 0.2f);
        _angle += (target - _angle) * (1f - MathF.Exp(-dt / (_grounded ? 0.04f : 0.3f)));
        _squash *= MathF.Exp(-dt / 0.12f);
        _wheel += speed * dt / (0.115f * L);
    }

    // ------------------------------------------------------------------ scenery

    private void EnsureScenery(in RenderContext ctx, int w, int h, float farTop, float farBase, float nearBase, float propH)
    {
        var key = (ctx.Palette, w, h, ctx.Overlay);
        if (key == _cacheKey && _sky != null) return;
        _cacheKey = key;
        _theme = HighwayTheme.For(ctx.Palette);
        _sky?.Dispose();
        _far?.Dispose();
        _near?.Dispose();
        _lights.Clear();
        if (ctx.Overlay)
        {
            _sky = SKImage.Create(new SKImageInfo(1, 1));
            return;
        }
        float s = ctx.Scale;
        _sky = HighwayPainter.PaintSky(_theme, w, h, nearBase);
        _farTop = farTop;
        _far = HighwayPainter.PaintFar(_theme, w, farTop, farBase, nearBase + 4, s);
        _nearTop = nearBase - propH * 1.6f;
        _near = HighwayPainter.PaintNear(_theme, w, _nearTop, nearBase, h, propH, s, _lights);
    }

    /// <summary>Draws a seamlessly tiling strip scrolled left by <paramref name="offset"/>; returns the wrapped offset.</summary>
    private float DrawTiled(SKCanvas canvas, SKImage image, float offset, float top, float w)
    {
        float o = offset % image.Width;
        canvas.DrawImage(image, -o, top, _image);
        canvas.DrawImage(image, image.Width - o, top, _image);
        return o;
    }

    private void DrawPropLights(in RenderContext ctx, HighwayTheme t, float offset, float w, float s)
    {
        if (ctx.GlowCanvas is not { } g || _lights.Count == 0) return;
        var color = t.Near == NearKind.Lamps ? t.FarColor2 : new SKColor(0xFF, 0xB8, 0x50);
        float r = (t.Near == NearKind.Lamps ? 26f : 14f) * s;
        _fill.Color = color.WithAlpha(170);
        foreach (var p in _lights)
        {
            for (int tile = 0; tile < 2; tile++)
            {
                float x = p.X - offset + tile * _near!.Width;
                if (x < -r || x > w + r) continue;
                g.DrawCircle(x, _nearTop + p.Y, r, _fill);
            }
        }
    }

    private void DrawSun(in RenderContext ctx, HighwayTheme t, AudioFrame frame, float farBase, float farTop)
    {
        var canvas = ctx.Canvas;
        float w = ctx.Width, h = ctx.Height;
        float r = t.SunSize * h * (1f + 0.06f * frame.Bass + 0.05f * frame.Pulse);
        float cx = t.SunX * w, cy = farBase - (farBase - farTop) * 0.45f;
        if (t.Name == "Rainbow") cy = h * 0.16f;

        // No halo: the glow layer sits above this scene, so a halo would spill over the hills in front of the sun.

        using var shader = t.SynthSun
            ? SKShader.CreateLinearGradient(new SKPoint(0, cy - r), new SKPoint(0, cy + r), new[] { t.SunTop, t.SunBottom }, null, SKShaderTileMode.Clamp)
            : SKShader.CreateRadialGradient(new SKPoint(cx, cy), r, new[] { new SKColor(255, 252, 235), t.SunTop, t.SunBottom }, new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
        canvas.Save();
        if (t.SynthSun)
        {
            // Retro stripes cut through the lower half (the sky shows through).
            for (int i = 0; i < 6; i++)
            {
                float y = cy + r * (0.08f + i * 0.16f);
                canvas.ClipRect(new SKRect(cx - r - 1, y, cx + r + 1, y + r * (0.02f + i * 0.016f)), SKClipOperation.Difference);
            }
        }
        _fill.Color = SKColors.White; // shader fills are modulated by the paint's alpha
        _fill.Shader = shader;
        canvas.DrawCircle(cx, cy, r, _fill);
        _fill.Shader = null;
        canvas.Restore();
    }

    private void DrawStars(in RenderContext ctx, AudioFrame frame, float horizon)
    {
        float w = ctx.Width;
        for (int i = 0; i < StarCount; i++)
        {
            float tw = 0.55f + 0.45f * MathF.Sin(_time * (1.5f + _starPhase[i]) + _starPhase[i] * 3f);
            float a = Math.Clamp(tw * (0.5f + 0.8f * frame.Treble), 0f, 1f);
            _fill.Color = SKColors.White.WithAlpha((byte)(220 * a));
            ctx.Canvas.DrawCircle(_starX[i] * w, _starY[i] * horizon * 0.85f, _starSize[i] * ctx.Scale, _fill);
        }
    }

    private void DrawAurora(in RenderContext ctx, AudioFrame frame)
    {
        float w = ctx.Width, h = ctx.Height;
        float step = 24f * ctx.Scale;
        for (int r = 0; r < 3; r++)
        {
            float baseY = h * (0.1f + 0.07f * r), height = h * (0.16f - 0.03f * r);
            float wave = h * 0.045f * (1f + 0.8f * frame.Mid);
            _path.Reset();
            for (float x = -step; x <= w + step; x += step)
            {
                float y = baseY + wave * MathF.Sin(x * 0.0035f / ctx.Scale + _time * (0.25f + 0.12f * r) + r * 2.1f)
                                + wave * 0.4f * MathF.Sin(x * 0.011f / ctx.Scale - _time * 0.6f + r);
                if (x < 0) _path.MoveTo(x, y); else _path.LineTo(x, y);
            }
            for (float x = w + step; x >= -step; x -= step)
            {
                float y = baseY + height + wave * 0.5f * MathF.Sin(x * 0.003f / ctx.Scale + _time * 0.2f + r);
                _path.LineTo(x, y);
            }
            _path.Close();
            var c = ctx.Palette.Sample(r / 2f);
            // Soft by nature: on the low-res glow layer when available (cheaper, and the blur suits it).
            var target = ctx.GlowCanvas ?? ctx.Canvas;
            byte a = (byte)(45 + 70 * frame.Mid + 40 * frame.Pulse);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, baseY - wave), new SKPoint(0, baseY + height + wave),
                new[] { c.WithAlpha(0), c.WithAlpha(a), c.WithAlpha(0) }, new[] { 0f, 0.35f, 1f }, SKShaderTileMode.Clamp);
            _plus.Color = SKColors.White;
            _plus.Shader = shader;
            target.DrawPath(_path, _plus);
            _plus.Shader = null;
        }
    }

    private void DrawSnow(in RenderContext ctx, float speed)
    {
        float w = ctx.Width, h = ctx.Height, dt = ctx.Dt;
        float drift = speed * 0.35f / w; // the car's motion blows the snow backward
        _fill.Color = SKColors.White.WithAlpha(200);
        for (int i = 0; i < SnowCount; i++)
        {
            _snowY[i] += (0.04f + 0.03f * _snowSize[i]) * dt;
            _snowX[i] -= drift * dt * (0.4f + 0.3f * _snowSize[i]) - MathF.Sin(_time + _snowPhase[i]) * 0.01f * dt;
            if (_snowY[i] > 1.02f) { _snowY[i] = -0.02f; _snowX[i] = (float)_rng.NextDouble(); }
            if (_snowX[i] < -0.02f) _snowX[i] += 1.04f;
            _fill.Color = SKColors.White.WithAlpha((byte)(120 + 40 * _snowSize[i]));
            ctx.Canvas.DrawCircle(_snowX[i] * w, _snowY[i] * h, _snowSize[i] * ctx.Scale, _fill);
        }
    }
}
