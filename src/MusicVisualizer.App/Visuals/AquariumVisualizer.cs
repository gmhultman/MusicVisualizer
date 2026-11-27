using MusicVisualizer.Audio;
using MusicVisualizer.Visuals.Aquarium;
using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// An underwater scene: palette-tinted water with swaying light rays, sand, rocks, coral and kelp; a school of
/// fish that swims with simple flocking (boids) plus a few larger fish cruising past; jellyfish, drifting plankton
/// and a treasure chest that pops open and burps bubbles on the beat.
/// Music: overall level sets swim speed, beats make the school dart outward (then regroup), pop the chest lid,
/// release bubble bursts (bigger/more with bass) and pulse the jellyfish; mids sway the plants; treble makes the
/// plankton sparkle.
/// In overlay mode the water is left out so the desktop shows through; fish get a thin dark rim to stay readable.
/// </summary>
/// <remarks>
/// Performance: water, distant reef, sand, rocks and coral are painted once per (palette, size, overlay) into a
/// cached image. Fish shapes are cached unit paths drawn through a transform; every per-frame path and paint is a
/// reused field. The light rays are Gouraud-shaded triangles (much cheaper than a gradient shader over that area)
/// and go on the low-res glow layer when glow is on.
/// </remarks>
public sealed class AquariumVisualizer : IVisualizer
{
    private const int MaxSchool = 40;
    private const int MaxBig = 4;
    private const int MaxJelly = 3;
    private const int MaxBubbles = 220;
    private const int PlanktonCount = 90;
    private const int RayCount = 7;
    private const int MaxPlants = 48;

    private readonly Random _rng = new(23);
    private float _time;

    // Smoothed audio
    private float _level, _mid, _bass, _treble, _excite, _chestOpen, _bigBoost;

    // Layout (recomputed every frame — cheap)
    private float _w, _h, _u, _floorY, _floorH, _swimTop, _swimBottom, _chestX, _chestW;
    private bool _strip, _overlay;

    // Cached scenery
    private (Palette? palette, int w, int h, bool overlay) _cacheKey;
    private AquariumTheme _theme = AquariumTheme.For(Palette.All[0]);
    private SKImage? _background;     // opaque mode: whole scene backdrop
    private SKImage? _floor;          // overlay mode: just the sand strip
    private float _floorTop;
    private SKImage?[] _halo = new SKImage?[MaxJelly + 1];  // soft colored dots (jelly halos, chest glow) for when glow is off
    private readonly SKPath _lidPath = new();
    private float _lidW;

    // School (boids)
    private int _schoolCount;
    private readonly float[] _fx = new float[MaxSchool], _fy = new float[MaxSchool], _fvx = new float[MaxSchool], _fvy = new float[MaxSchool];
    private readonly float[] _fTail = new float[MaxSchool], _fSize = new float[MaxSchool], _fPhase = new float[MaxSchool];
    private readonly FishColors[] _schoolColors = new FishColors[2];
    private bool _schoolReady;
    private (int w, int h) _schoolKey;

    // Big fish
    private int _bigCount;
    private readonly float[] _bx = new float[MaxBig], _by = new float[MaxBig], _bDir = new float[MaxBig], _bLen = new float[MaxBig];
    private readonly float[] _bSpeed = new float[MaxBig], _bPhase = new float[MaxBig], _bTail = new float[MaxBig], _bColorT = new float[MaxBig];
    private readonly FishKind[] _bKind = new FishKind[MaxBig];
    private readonly FishColors[] _bColors = new FishColors[MaxBig];
    private readonly int[] _bOrder = new int[MaxBig];

    // Jellyfish
    private int _jellyCount;
    private readonly float[] _jx = new float[MaxJelly], _jy = new float[MaxJelly], _jvy = new float[MaxJelly], _jr = new float[MaxJelly];
    private readonly float[] _jc = new float[MaxJelly], _jPhase = new float[MaxJelly];
    private readonly SKColor[] _jColor = new SKColor[MaxJelly];
    private readonly SKPath _jellyBell = new(), _jellyInner = new();

    // Bubbles
    private readonly float[] _ubx = new float[MaxBubbles], _uby = new float[MaxBubbles], _ubr = new float[MaxBubbles], _ubPhase = new float[MaxBubbles];
    private readonly bool[] _ubAlive = new bool[MaxBubbles];
    private int _bubbleCursor;
    private float _chestEmit, _bubblerEmit;

    // Plankton (normalised positions)
    private readonly float[] _px = new float[PlanktonCount], _py = new float[PlanktonCount], _pSize = new float[PlanktonCount], _pPhase = new float[PlanktonCount];

    // Rays
    private readonly SKPoint[] _rayPos = new SKPoint[RayCount * 9];
    private readonly SKColor[] _rayCol = new SKColor[RayCount * 9];
    private readonly ushort[] _rayIdx = BuildRayIndices();
    private readonly float[] _rayX = new float[RayCount], _rayW = new float[RayCount], _rayPhase = new float[RayCount], _raySpeed = new float[RayCount];

    // Plants (generated with the scenery)
    private int _plantCount;
    private readonly float[] _plX = new float[MaxPlants], _plY = new float[MaxPlants], _plH = new float[MaxPlants], _plW = new float[MaxPlants];
    private readonly float[] _plPhase = new float[MaxPlants], _plSpeed = new float[MaxPlants], _plLean = new float[MaxPlants];
    private readonly int[] _plColor = new int[MaxPlants];
    private readonly bool[] _plKelp = new bool[MaxPlants], _plFront = new bool[MaxPlants];
    private readonly SKPath _leaf = new();
    private readonly SKPath[] _kelpPaths = NewPaths(), _leafPaths = NewPaths(), _grassA = NewPaths(), _grassB = NewPaths();
    private static SKPath[] NewPaths() => [new(), new(), new(), new()];

    // Scratch for ribbons
    private const int RibbonMax = 12;
    private readonly SKPoint[] _left = new SKPoint[RibbonMax + 1], _right = new SKPoint[RibbonMax + 1], _spine = new SKPoint[RibbonMax + 1];

    private readonly SKPath _path = new();
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _outline = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _ray = new() { IsAntialias = false, Color = SKColors.White };
    private readonly SKPaint _image = new() { IsAntialias = false };
    private readonly SKPaint _sprite = new() { IsAntialias = false };

    public AquariumVisualizer()
    {
        for (int i = 0; i < PlanktonCount; i++)
        {
            _px[i] = (float)_rng.NextDouble();
            _py[i] = (float)_rng.NextDouble();
            _pSize[i] = 0.6f + 1.6f * (float)Math.Pow(_rng.NextDouble(), 2);
            _pPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
        }
        for (int i = 0; i < RayCount; i++)
        {
            _rayX[i] = (i + 0.3f + 0.4f * (float)_rng.NextDouble()) / RayCount;
            _rayW[i] = 0.4f + 0.8f * (float)_rng.NextDouble();
            _rayPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
            _raySpeed[i] = 0.12f + 0.15f * (float)_rng.NextDouble();
        }
        for (int i = 0; i < MaxSchool; i++)
        {
            _fSize[i] = 0.85f + 0.3f * (float)_rng.NextDouble();
            _fPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
            _fTail[i] = (float)(_rng.NextDouble() * MathF.Tau);
        }
        for (int i = 0; i < MaxJelly; i++)
        {
            _jPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
        }

        // Unit jellyfish bell (width 2, height ~1, rim at y = 0) with a scalloped rim.
        _jellyBell.MoveTo(-1, 0);
        _jellyBell.CubicTo(-1.02f, -1.3f, 1.02f, -1.3f, 1, 0);
        for (int k = 0; k < 6; k++)
        {
            float x0 = 1 - k / 3f, x1 = 1 - (k + 1) / 3f;
            _jellyBell.QuadTo((x0 + x1) / 2, 0.16f, x1, 0);
        }
        _jellyBell.Close();
        _jellyInner.MoveTo(-0.62f, -0.05f);
        _jellyInner.CubicTo(-0.62f, -0.92f, 0.62f, -0.92f, 0.62f, -0.05f);
        _jellyInner.QuadTo(0, 0.1f, -0.62f, -0.05f);
        _jellyInner.Close();

        // Unit kelp leaf, pointing along +x.
        _leaf.MoveTo(0, 0);
        _leaf.CubicTo(0.3f, -0.22f, 0.75f, -0.18f, 1, 0);
        _leaf.CubicTo(0.75f, 0.14f, 0.3f, 0.16f, 0, 0);
        _leaf.Close();
    }

    public string Name => "Aquarium";
    public VisualizerCategory Category => VisualizerCategory.Scenes;

    // Opaque water would hide a glow layer underneath, so jellies and the chest bloom over the scene instead.
    public bool GlowAbove => true;

    // A full illustrated scene is a lot of pixels; at 75% the soft underwater look is unchanged and it costs ~half.
    public float RenderScale => 0.75f;

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        float dt = MathF.Min(ctx.Dt, 0.1f);
        _time += dt;
        Smooth(frame, dt);
        Layout(ctx);
        EnsureScenery(ctx);
        EnsureCreatures();

        UpdateSchool(frame, dt);
        UpdateBigFish(frame, dt);
        UpdateJellies(frame, dt);
        UpdateBubbles(frame, dt);

        var c = ctx.Canvas;
        if (!_overlay)
        {
            c.DrawImage(_background!, 0, 0, _image);
            DrawRays(ctx.GlowCanvas ?? c, frame);   // on the soft glow layer when there is one (cheaper, and it blooms)
        }
        else
        {
            c.DrawImage(_floor!, 0, _floorTop, _image);
        }

        DrawBigFish(c, ctx.Scale, far: true);    // smaller (farther) big fish pass behind the kelp
        DrawPlants(c, front: false);
        DrawJellies(ctx);
        DrawSchool(c, ctx.Scale);
        DrawBigFish(c, ctx.Scale, far: false);
        DrawChest(ctx);
        DrawBubbles(c, ctx.Scale);
        DrawPlants(c, front: true);
        DrawPlankton(ctx, dt);
    }

    // ================================================================ audio & layout

    private void Smooth(AudioFrame f, float dt)
    {
        _level += (f.Level - _level) * (1f - MathF.Exp(-dt / 0.3f));
        _mid += (f.Mid - _mid) * (1f - MathF.Exp(-dt / 0.25f));
        _bass += (f.Bass - _bass) * (1f - MathF.Exp(-dt / 0.15f));
        _treble += (f.Treble - _treble) * (1f - MathF.Exp(-dt / 0.12f));
        _excite *= MathF.Exp(-dt / 0.45f);
        _bigBoost *= MathF.Exp(-dt / 0.5f);
        _chestOpen *= MathF.Exp(-dt / 0.3f);
        if (f.Beat)
        {
            _excite = MathF.Min(1.6f, _excite + 0.4f + 0.8f * f.BeatStrength);
            _bigBoost = MathF.Min(1.5f, _bigBoost + 0.6f * f.BeatStrength);
            _chestOpen = MathF.Max(_chestOpen, 0.35f + 0.65f * f.BeatStrength);
        }
    }

    private void Layout(in RenderContext ctx)
    {
        _w = ctx.Width;
        _h = ctx.Height;
        _overlay = ctx.Overlay;
        _strip = _h < _w * 0.35f;
        _u = _strip ? _h * 2.4f : MathF.Min(_h, _w * 0.5625f);
        _floorH = _strip ? _h * 0.16f : _overlay ? _h * 0.075f : _h * 0.14f;
        _floorY = _h - _floorH;
        _swimTop = _strip ? _h * 0.1f : _overlay ? _h * 0.38f : _h * 0.1f;
        _swimBottom = _floorY - (_strip ? _h * 0.08f : _u * 0.05f);
        _chestX = _w * (_strip ? 0.86f : 0.74f);
        _chestW = _strip ? MathF.Min(_u * 0.085f, _h * 0.32f) : _u * 0.085f;
    }

    private float SandY(float x) => _floorY + AquariumPainter.SandOffset(x, _w, _floorH);

    // ================================================================ cached scenery

    private void EnsureScenery(in RenderContext ctx)
    {
        int w = (int)_w, h = (int)_h;
        var key = (ctx.Palette, w, h, _overlay);
        if (key == _cacheKey && (_background != null || _floor != null)) return;
        _cacheKey = key;
        _theme = AquariumTheme.For(ctx.Palette);
        _background?.Dispose(); _background = null;
        _floor?.Dispose(); _floor = null;
        if (_overlay)
            _floor = AquariumPainter.PaintOverlayFloor(_theme, w, h, _floorY, _floorH, _u, _chestX, _strip, out _floorTop);
        else
            _background = AquariumPainter.PaintBackground(_theme, w, h, _floorY, _floorH, _u, _chestX, _strip);

        // Fish colours.
        var p = ctx.Palette;
        _schoolColors[0] = FishColors.From(ColorUtil.Mix(p.Sample(0.05f), SKColors.White, 0.12f), ColorUtil.Mix(p.Sample(0.95f), SKColors.White, 0.25f));
        _schoolColors[1] = FishColors.From(ColorUtil.Mix(p.Sample(0.35f), SKColors.White, 0.12f), ColorUtil.Mix(p.Sample(0.75f), SKColors.White, 0.3f));
        for (int i = 0; i < MaxBig; i++) _bColors[i] = BigFishColors(_bKind[i], _bColorT[i]);

        // Jelly colours + halo sprites (used when glow is off).
        for (int i = 0; i < MaxJelly; i++)
            _jColor[i] = ColorUtil.Mix(p.Sample(1f - i / (float)(MaxJelly - 1) * 0.8f), SKColors.White, 0.35f);
        for (int i = 0; i < _halo.Length; i++)
        {
            _halo[i]?.Dispose();
            _halo[i] = MakeHalo(i < MaxJelly ? _jColor[i] : _theme.Gold);
        }

        GeneratePlants();
        BuildLid();
        _schoolReady = _schoolReady && _schoolKey == (w, h);
    }

    private FishColors BigFishColors(FishKind kind, float t)
    {
        var p = _cacheKey.palette ?? Palette.All[0];
        var c = p.Sample(t);
        var dark = new SKColor(0x18, 0x14, 0x22);
        return kind switch
        {
            FishKind.Angel => FishColors.From(ColorUtil.Mix(c, SKColors.White, 0.45f), ColorUtil.Darken(c, 0.55f)),
            FishKind.Clown => FishColors.From(c, new SKColor(0xF8, 0xF8, 0xFF)),
            FishKind.Tang => FishColors.From(c, ColorUtil.Mix(dark, c, 0.2f)),
            _ => FishColors.From(ColorUtil.Darken(c, 0.2f), ColorUtil.Mix(c, SKColors.White, 0.55f)),
        };
    }

    private static SKImage MakeHalo(SKColor color)
    {
        const int size = 64;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var shader = SKShader.CreateRadialGradient(new SKPoint(size / 2f, size / 2f), size / 2f,
            [color.WithAlpha(255), color.WithAlpha(90), color.WithAlpha(0)], [0f, 0.4f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        surface.Canvas.DrawRect(0, 0, size, size, paint);
        return surface.Snapshot();
    }

    private void GeneratePlants()
    {
        var rng = new Random(7);
        _plantCount = 0;
        float h = _h, u = _u;
        int kelp = _strip ? (int)(_w / (h * 1.1f)) : (int)(_w / (u * 0.17f));
        int grass = _strip ? (int)(_w / (h * 0.9f)) : (int)(_w / (u * 0.15f));
        kelp = Math.Clamp(kelp, 3, 18);
        grass = Math.Clamp(grass, 3, MaxPlants - kelp - 4);

        for (int i = 0; i < kelp && _plantCount < MaxPlants; i++)
        {
            float x = _w * (i + 0.15f + 0.7f * (float)rng.NextDouble()) / kelp;
            if (MathF.Abs(x - _chestX) < _chestW * 0.9f) x = _chestX + MathF.Sign(x - _chestX + 0.01f) * _chestW * 1.1f;
            float height = _strip ? h * (0.45f + 0.35f * (float)rng.NextDouble())
                         : _overlay ? h * (0.13f + 0.15f * (float)rng.NextDouble())
                         : h * (0.25f + 0.32f * (float)rng.NextDouble());
            AddPlant(true, false, x, height, u * (0.016f + 0.01f * (float)rng.NextDouble()), rng);
        }
        for (int i = 0; i < grass && _plantCount < MaxPlants; i++)
        {
            float x = _w * (i + (float)rng.NextDouble()) / grass;
            float height = _strip ? h * (0.22f + 0.2f * (float)rng.NextDouble())
                         : _overlay ? h * (0.05f + 0.06f * (float)rng.NextDouble())
                         : h * (0.07f + 0.09f * (float)rng.NextDouble());
            AddPlant(false, false, x, height, u * 0.008f, rng);
        }
        // A few tufts in front of everything, near the bottom edge.
        int front = _strip ? 3 : 4;
        for (int i = 0; i < front && _plantCount < MaxPlants; i++)
        {
            float x = _w * (i + 0.2f + 0.6f * (float)rng.NextDouble()) / front;
            float height = _strip ? h * (0.25f + 0.15f * (float)rng.NextDouble())
                         : _overlay ? h * (0.06f + 0.05f * (float)rng.NextDouble())
                         : h * (0.1f + 0.08f * (float)rng.NextDouble());
            AddPlant(false, true, x, height, u * 0.01f, rng);
        }
    }

    private void AddPlant(bool kelp, bool front, float x, float height, float width, Random rng)
    {
        int i = _plantCount++;
        _plKelp[i] = kelp;
        _plFront[i] = front;
        _plX[i] = x;
        float sink = front ? _floorH * (0.45f + 0.3f * (float)rng.NextDouble()) : _floorH * 0.08f;
        _plY[i] = MathF.Min(SandY(x) + sink, _h + 2);
        _plH[i] = height;
        _plW[i] = width;
        _plPhase[i] = (float)(rng.NextDouble() * MathF.Tau);
        _plSpeed[i] = kelp ? 0.5f + 0.35f * (float)rng.NextDouble() : 0.8f + 0.6f * (float)rng.NextDouble();
        _plLean[i] = ((float)rng.NextDouble() - 0.5f) * 0.3f;
        _plColor[i] = rng.Next(_theme.Plants.Length);
    }

    private void BuildLid()
    {
        float cw = _chestW, lh = cw * 0.32f;
        _lidW = cw;
        _lidPath.Reset();
        _lidPath.MoveTo(0, 0);
        _lidPath.LineTo(0, -lh * 0.55f);
        _lidPath.CubicTo(0, -lh * 1.15f, cw, -lh * 1.15f, cw, -lh * 0.55f);
        _lidPath.LineTo(cw, 0);
        _lidPath.Close();
    }

    // ================================================================ creatures: setup

    private void EnsureCreatures()
    {
        if (_schoolReady) return;
        _schoolReady = true;
        _schoolKey = ((int)_w, (int)_h);

        _schoolCount = _strip ? 26 : _overlay ? 30 : MaxSchool;
        float cx = _w * 0.4f, cy = (_swimTop + _swimBottom) / 2;
        for (int i = 0; i < _schoolCount; i++)
        {
            _fx[i] = cx + ((float)_rng.NextDouble() - 0.5f) * _u * 0.3f;
            _fy[i] = cy + ((float)_rng.NextDouble() - 0.5f) * (_swimBottom - _swimTop) * 0.4f;
            _fvx[i] = _u * 0.1f;
            _fvy[i] = 0;
        }

        _bigCount = _strip ? 3 : MaxBig;
        for (int i = 0; i < _bigCount; i++)
        {
            SpawnBig(i, initial: true);
        }

        _jellyCount = _strip ? 2 : MaxJelly;
        for (int i = 0; i < _jellyCount; i++)
        {
            _jr[i] = _u * (_strip ? 0.03f : 0.035f + 0.015f * i);
            _jx[i] = _w * (0.15f + 0.7f * (i + 0.5f) / _jellyCount) + _u * 0.05f * ((float)_rng.NextDouble() - 0.5f);
            _jy[i] = _swimTop + (_swimBottom - _swimTop) * (0.2f + 0.5f * (float)_rng.NextDouble());
            _jvy[i] = 0;
            _jc[i] = 0;
        }
        Array.Clear(_ubAlive);
    }

    private void SpawnBig(int i, bool initial)
    {
        _bKind[i] = (FishKind)(2 + (i + _rng.Next(4)) % 4);   // Angel, Clown, Tang or Grouper
        _bDir[i] = _rng.Next(2) == 0 ? -1 : 1;
        float scale = _bKind[i] switch { FishKind.Angel => 0.085f, FishKind.Clown => 0.075f, FishKind.Tang => 0.1f, _ => 0.13f };
        _bLen[i] = _u * scale * (0.8f + 0.45f * (float)_rng.NextDouble());
        _bSpeed[i] = 0.7f + 0.6f * (float)_rng.NextDouble();
        _bPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
        _bColorT[i] = (float)_rng.NextDouble();
        float band = _swimBottom - _swimTop;
        _by[i] = _swimTop + band * (0.15f + 0.7f * (float)_rng.NextDouble());
        _bx[i] = initial ? _w * (0.1f + 0.8f * (float)_rng.NextDouble())
                         : _bDir[i] > 0 ? -_bLen[i] * (1f + 2f * (float)_rng.NextDouble()) : _w + _bLen[i] * (1f + 2f * (float)_rng.NextDouble());
        if (_cacheKey.palette != null) _bColors[i] = BigFishColors(_bKind[i], _bColorT[i]);
    }

    // ================================================================ creatures: update

    private void UpdateSchool(AudioFrame frame, float dt)
    {
        int n = _schoolCount;
        float u = _u;
        float baseSpeed = u * (0.07f + 0.2f * _level);
        float band = _swimBottom - _swimTop;

        // A slowly wandering point the school is drawn toward.
        float tx = _w * (0.5f + 0.36f * MathF.Sin(_time * 0.071f) + 0.08f * MathF.Sin(_time * 0.23f + 1f));
        float ty = _swimTop + band * (0.5f + 0.32f * MathF.Sin(_time * 0.113f + 0.7f) + 0.08f * MathF.Sin(_time * 0.31f));

        float cx = 0, cy = 0;
        for (int i = 0; i < n; i++) { cx += _fx[i]; cy += _fy[i]; }
        cx /= n; cy /= n;

        if (frame.Beat)
        {
            // Dart outward from the school's centre, a little forward.
            float kick = u * (0.05f + 0.12f * frame.BeatStrength);
            for (int i = 0; i < n; i++)
            {
                float dx = _fx[i] - cx, dy = _fy[i] - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy) + 1e-3f;
                float sp = MathF.Sqrt(_fvx[i] * _fvx[i] + _fvy[i] * _fvy[i]) + 1e-3f;
                _fvx[i] += dx / d * kick + _fvx[i] / sp * kick * 0.8f;
                _fvy[i] += dy / d * kick * 0.6f + _fvy[i] / sp * kick * 0.8f;
            }
        }

        float r = u * 0.12f, r2 = r * r;
        float rs = u * 0.034f * (_strip ? 0.8f : 1f);
        float maxSpeed = baseSpeed * (1.4f + 1.2f * _excite);
        float minSpeed = baseSpeed * 0.55f;
        float wall = u * 0.06f;
        for (int i = 0; i < n; i++)
        {
            float px = _fx[i], py = _fy[i];
            float sx = 0, sy = 0, avx = 0, avy = 0, sepX = 0, sepY = 0;
            int count = 0;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                float dx = _fx[j] - px, dy = _fy[j] - py;
                float d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                count++;
                sx += _fx[j]; sy += _fy[j];
                avx += _fvx[j]; avy += _fvy[j];
                float rsi = rs * _fSize[i];
                if (d2 < rsi * rsi)
                {
                    float d = MathF.Sqrt(d2) + 1e-3f;
                    float k = (1f - d / rsi) / d;
                    sepX -= dx * k; sepY -= dy * k;
                }
            }
            float ax = 0, ay = 0;
            if (count > 0)
            {
                ax += (sx / count - px) * 0.9f + (avx / count - _fvx[i]) * 1.6f;
                ay += (sy / count - py) * 0.9f + (avy / count - _fvy[i]) * 1.6f;
            }
            // Stay with the school as a whole: pulled back once outside its core.
            {
                float dx = cx - px, dy = cy - py;
                float d = MathF.Sqrt(dx * dx + dy * dy) + 1e-3f;
                float over = d - u * 0.07f;
                if (over > 0) { ax += dx / d * over * 2.5f; ay += dy / d * over * 2.5f; }
            }
            ax += sepX * u * 5f;
            ay += sepY * u * 5f;
            // Seek the wander point: steer toward a cruising velocity aimed at it (no spring overshoot).
            {
                float dx = tx - px, dy = (ty - py) * 1.3f;
                float d = MathF.Sqrt(dx * dx + dy * dy) + 1e-3f;
                float want = baseSpeed * MathF.Min(1f, d / (u * 0.25f));
                float k = 1.2f + 0.4f * MathF.Sin(_fPhase[i] + _time * 0.5f);
                ax += (dx / d * want - _fvx[i]) * k;
                ay += (dy / d * want - _fvy[i]) * k;
            }
            // Individual wiggle so the school isn't rigid.
            ax += MathF.Sin(_time * 1.7f + _fPhase[i] * 3f) * u * 0.08f;
            ay += MathF.Cos(_time * 1.3f + _fPhase[i] * 2f) * u * 0.06f;

            // Avoid the big fish.
            for (int b = 0; b < _bigCount; b++)
            {
                float dx = px - _bx[b], dy = py - _by[b];
                float rr = _bLen[b] * 0.9f;
                float d2 = dx * dx + dy * dy;
                if (d2 < rr * rr)
                {
                    float d = MathF.Sqrt(d2) + 1e-3f;
                    float k = (1f - d / rr) * u * 3f / d;
                    ax += dx * k; ay += dy * k;
                }
            }

            // Soft walls.
            if (py < _swimTop + wall) ay += (_swimTop + wall - py) * 6f;
            if (py > _swimBottom - wall) ay -= (py - (_swimBottom - wall)) * 6f;
            if (px < wall) ax += (wall - px) * 6f;
            if (px > _w - wall) ax -= (px - (_w - wall)) * 6f;

            float vx = _fvx[i] + ax * dt, vy = _fvy[i] + ay * dt;
            float sp = MathF.Sqrt(vx * vx + vy * vy) + 1e-4f;
            float lim = Math.Clamp(sp, minSpeed, maxSpeed);
            vx *= lim / sp; vy *= lim / sp;
            _fvx[i] = vx; _fvy[i] = vy;
            _fTail[i] += dt * (7f + 18f * lim / (u * 0.25f));
        }
        for (int i = 0; i < n; i++)
        {
            _fx[i] += _fvx[i] * dt;
            _fy[i] += _fvy[i] * dt;
            // Hard limits so a strong dart never pushes fish into the sand or far off-screen.
            if (_fy[i] > _swimBottom) { _fy[i] = _swimBottom; if (_fvy[i] > 0) _fvy[i] *= -0.3f; }
            else if (_fy[i] < _swimTop - _u * 0.04f) { _fy[i] = _swimTop - _u * 0.04f; if (_fvy[i] < 0) _fvy[i] *= -0.3f; }
            _fx[i] = Math.Clamp(_fx[i], -_u * 0.1f, _w + _u * 0.1f);
        }
    }

    private void UpdateBigFish(AudioFrame frame, float dt)
    {
        for (int i = 0; i < _bigCount; i++)
        {
            float speed = _u * (0.035f + 0.09f * _level) * _bSpeed[i] * (1f + _bigBoost);
            _bx[i] += _bDir[i] * speed * dt;
            _bTail[i] += dt * (4f + 10f * (1f + _bigBoost) * (0.5f + _level));
            if ((_bDir[i] > 0 && _bx[i] > _w + _bLen[i] * 1.2f) || (_bDir[i] < 0 && _bx[i] < -_bLen[i] * 1.2f))
                SpawnBig(i, initial: false);
        }
    }

    private float BigFishY(int i, out float pitch)
    {
        float amp = (_swimBottom - _swimTop) * 0.06f;
        float ph = _bPhase[i] + _time * 0.45f * _bSpeed[i];
        pitch = MathF.Atan2(MathF.Cos(ph) * amp * 0.45f * _bSpeed[i], _u * 0.06f) * 0.8f;
        return _by[i] + MathF.Sin(ph) * amp;
    }

    private void UpdateJellies(AudioFrame frame, float dt)
    {
        float band = _swimBottom - _swimTop;
        for (int i = 0; i < _jellyCount; i++)
        {
            // Gentle natural pulsing, plus a strong contraction on each beat.
            _jPhase[i] += dt * (1.6f + 0.3f * i);
            float natural = MathF.Pow(MathF.Max(0, MathF.Sin(_jPhase[i])), 4) * 0.35f;
            _jc[i] *= MathF.Exp(-dt / 0.28f);
            if (frame.Beat) _jc[i] = MathF.Max(_jc[i], 0.45f + 0.55f * frame.BeatStrength);
            float c = MathF.Max(_jc[i], natural);

            float lo = _swimTop + _jr[i] * 1.2f, hi = MathF.Max(lo, _swimBottom - _jr[i] * 2.5f);
            float target = lo + (hi - lo) * (0.5f + 0.45f * MathF.Sin(_time * (0.05f + 0.02f * i) + i * 2.1f));
            float acc = (target - _jy[i]) * 0.25f - _jvy[i] * 0.9f - c * _u * 0.12f + _u * 0.015f;
            _jvy[i] += acc * dt;
            _jy[i] += _jvy[i] * dt;
            _jy[i] = Math.Clamp(_jy[i], _swimTop - band * 0.1f, _swimBottom);
            _jx[i] += MathF.Sin(_time * 0.13f + i * 1.7f) * _u * 0.012f * dt;
        }
    }

    private void UpdateBubbles(AudioFrame frame, float dt)
    {
        float u = _u;
        float chestTop = SandY(_chestX) + _chestW * 0.1f - _chestW * 0.5f;
        float scaleR = _strip ? 0.75f : 1f;

        // Steady trickle from the chest, more when it's loud; a burst on every beat.
        _chestEmit += dt * (1.2f + 6f * _level);
        while (_chestEmit >= 1f)
        {
            _chestEmit -= 1f;
            SpawnBubble(_chestX + _chestW * (0.05f + 0.35f * (float)_rng.NextDouble()), chestTop,
                        u * (0.0025f + 0.003f * (float)_rng.NextDouble()) * scaleR);
        }
        if (frame.Beat)
        {
            int count = 3 + (int)(12 * frame.BeatStrength * (0.35f + _bass));
            for (int k = 0; k < count; k++)
            {
                float r = u * (0.003f + 0.008f * _bass) * (0.5f + (float)_rng.NextDouble()) * scaleR;
                SpawnBubble(_chestX + _chestW * (-0.1f + 0.55f * (float)_rng.NextDouble()),
                            chestTop - (float)_rng.NextDouble() * u * 0.03f, r);
            }
        }

        // A thin bubbler stream rising from the sand on the left.
        _bubblerEmit += dt * (3f + 8f * _level);
        float bx = _w * (_strip ? 0.12f : 0.18f);
        while (_bubblerEmit >= 1f)
        {
            _bubblerEmit -= 1f;
            SpawnBubble(bx + ((float)_rng.NextDouble() - 0.5f) * u * 0.006f, SandY(bx), u * (0.0018f + 0.0022f * (float)_rng.NextDouble()) * scaleR);
        }

        for (int i = 0; i < MaxBubbles; i++)
        {
            if (!_ubAlive[i]) continue;
            float r = _ubr[i];
            _uby[i] -= (u * 0.05f + r * 14f) * (1f + 0.4f * _level) * dt;
            _ubr[i] = r * (1f + dt * 0.04f);
            _ubx[i] += MathF.Sin(_time * 0.35f + _uby[i] / u * 5f) * u * 0.02f * dt;   // drift in the current
            _ubPhase[i] += dt * (3f + 1.5f / MathF.Max(0.3f, r / (u * 0.004f)));
            if (_uby[i] < -r * 3) _ubAlive[i] = false;
        }
    }

    private void SpawnBubble(float x, float y, float r)
    {
        for (int k = 0; k < MaxBubbles; k++)
        {
            int i = (_bubbleCursor + k) % MaxBubbles;
            if (_ubAlive[i]) continue;
            _ubAlive[i] = true;
            _ubx[i] = x; _uby[i] = y; _ubr[i] = MathF.Max(0.8f, r);
            _ubPhase[i] = (float)(_rng.NextDouble() * MathF.Tau);
            _bubbleCursor = i + 1;
            return;
        }
    }

    // ================================================================ drawing

    private void DrawRays(SKCanvas c, AudioFrame frame)
    {
        // Each ray is a 3×3 grid of vertex colours: bright along its centre line, fading to the sides and
        // toward the bottom. Gouraud-shaded triangles are far cheaper than a gradient shader over this much area.
        float w = _w, len = _strip ? _h : _h * 0.85f;
        var light = _theme.Light;
        int v = 0;
        for (int i = 0; i < RayCount; i++)
        {
            float sway = MathF.Sin(_time * _raySpeed[i] + _rayPhase[i]);
            float x = _rayX[i] * w + sway * w * 0.02f;
            float tw = _u * 0.02f * _rayW[i] * (_strip ? 0.6f : 1f);
            float bw = tw * 3f + _u * 0.03f * _rayW[i];
            float skew = w * 0.08f + sway * w * 0.045f;
            float a = (0.32f + 0.2f * MathF.Sin(_time * (0.7f + _raySpeed[i]) + _rayPhase[i] * 2f)) * (0.55f + 0.4f * _level + 0.3f * frame.Pulse);
            a = Math.Clamp(a, 0f, 1f) * 255f;
            for (int r = 0; r < 3; r++)
            {
                float f = r == 0 ? 0 : r == 1 ? 0.4f : 1f;
                float y = r == 0 ? -2 : len * f;
                float cx = x + skew * f, hw = tw + (bw - tw) * f;
                byte alpha = (byte)(a * (r == 0 ? 1f : r == 1 ? 0.5f : 0f));
                _rayPos[v] = new SKPoint(cx - hw, y); _rayCol[v++] = light.WithAlpha(0);
                _rayPos[v] = new SKPoint(cx, y); _rayCol[v++] = light.WithAlpha(alpha);
                _rayPos[v] = new SKPoint(cx + hw, y); _rayCol[v++] = light.WithAlpha(0);
            }
        }
        c.DrawVertices(SKVertexMode.Triangles, _rayPos, null, _rayCol, SKBlendMode.Modulate, _rayIdx, _ray);
    }

    private static ushort[] BuildRayIndices()
    {
        var idx = new ushort[RayCount * 8 * 3];
        int k = 0;
        for (int i = 0; i < RayCount; i++)
        {
            int b = i * 9;
            for (int r = 0; r < 2; r++)
                for (int col = 0; col < 2; col++)
                {
                    int a0 = b + r * 3 + col, a1 = a0 + 1, b0 = a0 + 3, b1 = b0 + 1;
                    idx[k++] = (ushort)a0; idx[k++] = (ushort)a1; idx[k++] = (ushort)b1;
                    idx[k++] = (ushort)a0; idx[k++] = (ushort)b1; idx[k++] = (ushort)b0;
                }
        }
        return idx;
    }

    private void DrawPlants(SKCanvas c, bool front)
    {
        // Plants are batched into one path per colour (leaves, blades, stalks), so each layer is a handful
        // of draw calls rather than one per leaf.
        float sway = 0.05f + 0.16f * _mid;
        float current = MathF.Sin(_time * 0.27f) * 0.05f + MathF.Sin(_time * 0.11f + 1f) * 0.03f;
        int nc = _theme.Plants.Length;
        for (int k = 0; k < nc; k++)
        {
            _kelpPaths[k].Reset(); _leafPaths[k].Reset(); _grassA[k].Reset(); _grassB[k].Reset();
        }
        for (int i = 0; i < _plantCount; i++)
        {
            if (_plFront[i] != front) continue;
            int ci = _plColor[i];
            if (_plKelp[i]) BuildKelp(i, sway, current, ci);
            else BuildGrass(i, sway, current, ci);
        }
        for (int k = 0; k < nc; k++)
        {
            var col = _theme.Plants[k];
            if (front) col = ColorUtil.Darken(col, 0.25f);
            if (!_leafPaths[k].IsEmpty)
            {
                _fill.Color = ColorUtil.Darken(col, 0.15f);
                c.DrawPath(_leafPaths[k], _fill);
            }
            if (!_kelpPaths[k].IsEmpty)
            {
                _fill.Color = col;
                c.DrawPath(_kelpPaths[k], _fill);
            }
            if (!_grassA[k].IsEmpty)
            {
                _fill.Color = col;
                c.DrawPath(_grassA[k], _fill);
            }
            if (!_grassB[k].IsEmpty)
            {
                _fill.Color = ColorUtil.Mix(col, SKColors.White, 0.12f);
                c.DrawPath(_grassB[k], _fill);
            }
        }
    }

    private void BuildKelp(int i, float sway, float current, int ci)
    {
        const int K = 10;
        float x0 = _plX[i], y0 = _plY[i], H = _plH[i], W = _plW[i];
        for (int k = 0; k <= K; k++)
        {
            float f = k / (float)K;
            float s = H * (sway * MathF.Pow(f, 1.4f) * MathF.Sin(_time * _plSpeed[i] * 1.6f + _plPhase[i] - f * 2.4f)
                           + (current + _plLean[i]) * f * f);
            _spine[k] = new SKPoint(x0 + s, y0 - H * f);
            float wk = W * (0.75f + 0.35f * MathF.Sin(f * 9f + _plPhase[i])) * (1f - 0.8f * f);
            _left[k] = new SKPoint(_spine[k].X - wk, _spine[k].Y);
            _right[k] = new SKPoint(_spine[k].X + wk, _spine[k].Y);
        }
        // Leaves at every other node from the lower part up, alternating sides.
        var leaves = _leafPaths[ci];
        for (int k = 2; k < K; k += 2)
        {
            float side = ((k / 2) & 1) == 0 ? 1 : -1;
            var p = _spine[k];
            var q = _spine[k + 1];
            float dir = MathF.Atan2(q.Y - p.Y, q.X - p.X);       // up the stalk
            float ang = dir + side * (0.75f + 0.25f * MathF.Sin(_time * 1.2f + k + _plPhase[i]));
            float leaf = H * 0.2f * (1f - 0.5f * k / K);
            var m = SKMatrix.CreateScale(leaf, leaf * side);
            m = m.PostConcat(SKMatrix.CreateRotation(ang));
            m = m.PostConcat(SKMatrix.CreateTranslation(p.X, p.Y));
            leaves.AddPath(_leaf, in m);
        }
        AddRibbon(_kelpPaths[ci], K);
    }

    private void BuildGrass(int i, float sway, float current, int ci)
    {
        const int K = 6;
        const int blades = 4;
        float x0 = _plX[i], y0 = _plY[i], H0 = _plH[i], W = _plW[i];
        for (int b = 0; b < blades; b++)
        {
            float spread = (b - (blades - 1) / 2f) / blades;
            float H = H0 * (0.7f + 0.3f * MathF.Cos(b * 1.7f + _plPhase[i]));
            float lean = spread * 0.55f + _plLean[i];
            float bx = x0 + spread * W * 3f;
            for (int k = 0; k <= K; k++)
            {
                float f = k / (float)K;
                float s = H * (sway * 1.3f * MathF.Pow(f, 1.5f) * MathF.Sin(_time * _plSpeed[i] * 2f + _plPhase[i] + b * 0.7f - f * 1.8f)
                               + (current + lean) * f * f);
                float sx = bx + s, sy = y0 - H * f;
                float wk = W * (1f - f * 0.92f);
                _left[k] = new SKPoint(sx - wk, sy);
                _right[k] = new SKPoint(sx + wk, sy);
            }
            AddRibbon((b & 1) == 0 ? _grassA[ci] : _grassB[ci], K);
        }
    }

    /// <summary>Appends a closed smooth outline up the left edge points and back down the right ones.</summary>
    private void AddRibbon(SKPath path, int k)
    {
        path.MoveTo(_left[0]);
        for (int i = 1; i < k; i++)
            path.QuadTo(_left[i], Mid(_left[i], _left[i + 1]));
        var tip = Mid(_left[k], _right[k]);
        path.QuadTo(_left[k], tip);
        path.QuadTo(_right[k], Mid(_right[k], _right[k - 1]));
        for (int i = k - 1; i >= 1; i--)
            path.QuadTo(_right[i], Mid(_right[i], _right[i - 1]));
        path.LineTo(_right[0]);
        path.Close();
    }

    private static SKPoint Mid(SKPoint a, SKPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private void DrawSchool(SKCanvas c, float scale)
    {
        float baseLen = _u * 0.03f;
        float outline = _overlay ? 1.6f * scale : 0;
        for (int i = 0; i < _schoolCount; i++)
        {
            float vx = _fvx[i], vy = _fvy[i];
            float pitch = Math.Clamp(MathF.Atan2(vy, MathF.Abs(vx)), -0.7f, 0.7f);
            float angle = vx >= 0 ? pitch : MathF.PI - pitch;
            float len = baseLen * _fSize[i];
            var kind = (i % 3 == 0) ? FishKind.Minnow : FishKind.Tetra;
            FishPainter.Draw(c, FishShape.Get(kind), _fx[i], _fy[i], angle, len, MathF.Sin(_fTail[i]) * 0.38f,
                             _schoolColors[i & 1], _fill, _outline, detail: len > 22f, outline);
        }
    }

    private void DrawBigFish(SKCanvas c, float scale, bool far)
    {
        // Smaller ones are drawn behind the kelp (far); the biggest pass in front of the school (near).
        float split = _u * 0.1f;
        float outline = _overlay ? 2f * scale : 0;
        for (int i = 0; i < _bigCount; i++)
        {
            if ((_bLen[i] < split) != far) continue;
            float y = BigFishY(i, out float pitch);
            float angle = _bDir[i] > 0 ? pitch : MathF.PI - pitch;
            FishPainter.Draw(c, FishShape.Get(_bKind[i]), _bx[i], y, angle, _bLen[i], MathF.Sin(_bTail[i]) * 0.3f,
                             _bColors[i], _fill, _outline, detail: true, outline);
        }
    }

    private void DrawJellies(in RenderContext ctx)
    {
        var c = ctx.Canvas;
        for (int i = 0; i < _jellyCount; i++)
        {
            float natural = MathF.Pow(MathF.Max(0, MathF.Sin(_jPhase[i])), 4) * 0.35f;
            float k = MathF.Max(_jc[i], natural);
            float r = _jr[i];
            float bw = r * (1f - 0.22f * k), bh = r * 0.9f * (1f + 0.18f * k);
            float x = _jx[i], y = _jy[i];
            var col = _jColor[i];

            // Halo: on the glow layer when available, else a soft sprite.
            if (ctx.GlowCanvas is { } g)
            {
                _fill.Color = col.WithAlpha((byte)(50 + 130 * k));
                g.DrawCircle(x, y - bh * 0.4f, r * (1.1f + 0.3f * k), _fill);
            }
            else
            {
                float hr = r * (2.0f + 0.5f * k);
                _sprite.Color = SKColors.White.WithAlpha((byte)(50 + 90 * k));
                c.DrawImage(_halo[i]!, new SKRect(x - hr, y - bh * 0.4f - hr, x + hr, y - bh * 0.4f + hr), _sprite);
            }

            // Tentacles trail below, waving; they bunch up a little as the bell contracts.
            _stroke.Color = col.WithAlpha(140);
            _stroke.StrokeWidth = MathF.Max(1f, r * 0.035f);
            float tlen = r * (2.1f - 0.4f * k);
            for (int t = 0; t < 6; t++)
            {
                float tx0 = x + bw * (-0.8f + 1.6f * t / 5f);
                _path.Reset();
                _path.MoveTo(tx0, y);
                for (int s = 1; s <= 7; s++)
                {
                    float f = s / 7f;
                    float wave = MathF.Sin(_time * 2.4f + f * 5f + t * 1.3f + i) * r * 0.16f * f;
                    _path.LineTo(tx0 + wave + (tx0 - x) * 0.2f * f, y + tlen * f);
                }
                c.DrawPath(_path, _stroke);
            }
            // Frilly oral arms.
            _stroke.Color = ColorUtil.Mix(col, SKColors.White, 0.3f).WithAlpha(120);
            _stroke.StrokeWidth = r * 0.12f;
            for (int t = 0; t < 2; t++)
            {
                float tx0 = x + bw * (t == 0 ? -0.18f : 0.18f);
                _path.Reset();
                _path.MoveTo(tx0, y);
                for (int s = 1; s <= 5; s++)
                {
                    float f = s / 5f;
                    float wave = MathF.Sin(_time * 1.8f + f * 4f + t * 2f + i) * r * 0.12f;
                    _path.LineTo(tx0 + wave, y + r * 1.2f * f);
                }
                c.DrawPath(_path, _stroke);
            }

            // Bell.
            c.Save();
            c.Translate(x, y);
            c.Scale(bw, bh);
            _fill.Color = col.WithAlpha((byte)(_overlay ? 170 : 120));
            c.DrawPath(_jellyBell, _fill);
            _fill.Color = ColorUtil.Mix(col, SKColors.White, 0.5f).WithAlpha(110);
            c.DrawPath(_jellyInner, _fill);
            _fill.Color = SKColors.White.WithAlpha(120);
            c.DrawOval(-0.35f, -0.72f, 0.22f, 0.12f, _fill);
            c.Restore();
        }
    }

    private void DrawChest(in RenderContext ctx)
    {
        var c = ctx.Canvas;
        var t = _theme;
        float cw = _chestW, ch = cw * 0.5f;
        float cx = _chestX, by = SandY(cx) + ch * 0.2f;
        float left = cx - cw / 2, top = by - ch;
        float open = _chestOpen;

        // Treasure glow from inside when the lid lifts.
        if (open > 0.02f)
        {
            if (ctx.GlowCanvas is { } g)
            {
                _fill.Color = t.Gold.WithAlpha((byte)(220 * open));
                g.DrawOval(cx, top - cw * 0.1f, cw * 0.55f, cw * 0.35f, _fill);
            }
            else
            {
                float hr = cw * 0.7f;
                _sprite.Color = SKColors.White.WithAlpha((byte)(170 * open));
                c.DrawImage(_halo[MaxJelly]!, new SKRect(cx - hr, top - hr * 0.7f, cx + hr, top + hr * 0.5f), _sprite);
            }
        }

        // Coins heaped inside (visible when the lid is up).
        _fill.Color = t.Gold;
        c.DrawOval(cx, top, cw * 0.44f, cw * 0.09f, _fill);
        _fill.Color = ColorUtil.Mix(t.Gold, SKColors.White, 0.5f);
        c.DrawCircle(cx - cw * 0.15f, top - cw * 0.04f, cw * 0.03f, _fill);
        c.DrawCircle(cx + cw * 0.12f, top - cw * 0.02f, cw * 0.025f, _fill);

        // Body.
        _fill.Color = t.Wood;
        c.DrawRect(left, top, cw, ch, _fill);
        _stroke.Color = t.WoodDark;
        _stroke.StrokeWidth = MathF.Max(1f, cw * 0.02f);
        c.DrawLine(left, top + ch * 0.36f, left + cw, top + ch * 0.36f, _stroke);
        c.DrawLine(left, top + ch * 0.7f, left + cw, top + ch * 0.7f, _stroke);
        var band = ColorUtil.Darken(t.Gold, 0.35f);
        _fill.Color = band;
        c.DrawRect(left + cw * 0.12f, top, cw * 0.08f, ch, _fill);
        c.DrawRect(left + cw * 0.8f, top, cw * 0.08f, ch, _fill);
        _fill.Color = ColorUtil.Darken(t.Wood, 0.45f);
        c.DrawRect(left, top, cw, ch * 0.08f, _fill);

        // Lid, hinged at the back-left corner, pops open on beats.
        float angle = 0.05f + 0.55f * open + 0.03f * MathF.Sin(_time * 2f);
        c.Save();
        c.Translate(left, top);
        c.RotateRadians(-angle);
        _fill.Color = ColorUtil.Mix(t.Wood, SKColors.White, 0.06f);
        c.DrawPath(_lidPath, _fill);
        _fill.Color = band;
        float lh = _lidW * 0.32f;
        c.DrawRect(_lidW * 0.12f, -lh * 0.95f, _lidW * 0.08f, lh * 0.95f, _fill);
        c.DrawRect(_lidW * 0.8f, -lh * 0.95f, _lidW * 0.08f, lh * 0.95f, _fill);
        _fill.Color = t.Gold;
        c.DrawRect(_lidW * 0.44f, -lh * 0.25f, _lidW * 0.12f, lh * 0.3f, _fill);
        c.Restore();

        // Lock plate.
        _fill.Color = t.Gold;
        c.DrawRoundRect(new SKRect(cx - cw * 0.06f, top + ch * 0.08f, cx + cw * 0.06f, top + ch * 0.36f), cw * 0.015f, cw * 0.015f, _fill);
        _fill.Color = t.WoodDark;
        c.DrawCircle(cx, top + ch * 0.2f, cw * 0.018f, _fill);
    }

    private void DrawBubbles(SKCanvas c, float scale)
    {
        var light = _theme.Light;
        float u = _u;
        for (int i = 0; i < MaxBubbles; i++)
        {
            if (!_ubAlive[i]) continue;
            float r = _ubr[i];
            float x = _ubx[i] + MathF.Sin(_ubPhase[i]) * r * 0.9f;
            float y = _uby[i];
            if (r > u * 0.005f)
            {
                _fill.Color = light.WithAlpha(30);
                c.DrawCircle(x, y, r, _fill);
            }
            _stroke.Color = light.WithAlpha(_overlay ? (byte)230 : (byte)170);
            _stroke.StrokeWidth = MathF.Max(1f * scale, r * 0.13f);
            c.DrawCircle(x, y, r, _stroke);
            _fill.Color = SKColors.White.WithAlpha(210);
            c.DrawCircle(x - r * 0.35f, y - r * 0.35f, MathF.Max(0.6f, r * 0.24f), _fill);
        }
    }

    private void DrawPlankton(in RenderContext ctx, float dt)
    {
        var c = ctx.Canvas;
        float w = _w, top = _overlay && !_strip ? _swimTop : 0, hh = _floorY - top;
        float flow = (0.004f + 0.01f * _treble);
        var col = _theme.Plankton;
        for (int i = 0; i < PlanktonCount; i++)
        {
            _px[i] += (flow + MathF.Sin(_time * 0.3f + _pPhase[i]) * 0.006f) * dt;
            _py[i] += MathF.Cos(_time * 0.25f + _pPhase[i] * 1.3f) * 0.006f * dt;
            if (_px[i] > 1.02f) _px[i] -= 1.04f;
            if (_py[i] < 0) _py[i] += 1; else if (_py[i] > 1) _py[i] -= 1;
            float tw = 0.5f + 0.5f * MathF.Sin(_time * (2f + _pPhase[i]) + _pPhase[i] * 5f);
            float a = (0.18f + 0.3f * tw) + 0.6f * _treble * tw;
            _fill.Color = col.WithAlpha((byte)(Math.Clamp(a, 0f, 1f) * 255));
            float r = _pSize[i] * ctx.Scale * (1f + 0.5f * _treble);
            c.DrawCircle(_px[i] * w, top + _py[i] * hh, r, _fill);
        }
    }
}
