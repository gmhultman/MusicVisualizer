using System.Numerics;
using MusicVisualizer.Audio;
using SkiaSharp;

namespace MusicVisualizer.Visuals.Disaster;

/// <summary>
/// A themed room: static furniture plus props that rattle, fall and get tidied back into place.
/// Room space: x 0..8 m, y 0 (ceiling) .. 4.2 m (floor), y down. Draw methods receive canvases already
/// transformed into these world metres. The room's look follows the palette (see RoomLayouts.cs).
/// </summary>
internal sealed partial class Room
{
    public const float Width = 8f, Height = 4.5f, FloorY = 4.2f;

    /// <summary>Area kept clear of props at rest so the dancer has room.</summary>
    public const float ClearLeft = 3.1f, ClearRight = 4.9f;

    private static readonly SKRect DrawBounds = new(-4f, -2f, 12f, 7f);

    private readonly PhysicsWorld _world;
    private readonly Palette _palette;
    private readonly Random _rng;
    private readonly List<Prop> _props = [];
    private readonly List<Body> _bodies = [];
    private readonly List<Action>[] _disasters = [[], [], [], [], []];
    private readonly int[] _nextDisaster = new int[5];

    // Static artwork, recorded once into resolution-independent pictures.
    private readonly List<Action<SKCanvas>> _wallArt = [];
    private readonly List<Action<SKCanvas>> _furnitureArt = [];
    private SKRect _furnitureBounds = SKRect.Empty;
    private SKPicture? _wallPicture, _furniturePicture;

    // Dynamic decor
    private readonly List<RoomLight> _lights = [];
    private readonly List<Action<SKCanvas, SKCanvas?, float, AudioFrame>> _animatedBack = [];

    // Tidy
    private bool _tidying;
    private float _tidyTime;
    private readonly List<(Vector2 pos, float age)> _sparkles = [];

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _layer = new();

    public Room(PhysicsWorld world, Palette palette, Random rng)
    {
        _world = world;
        _palette = palette;
        _rng = rng;
        ThemeName = palette.Name;
        BuildLayout();
        foreach (var p in _props)
        {
            p.Home = p.Body.Center;
            p.HomeAngle = p.Body.Angle;
        }
    }

    public string ThemeName { get; }
    public IReadOnlyList<Body> Props => _bodies;
    public float DancerHomeX { get; private set; } = 4f;

    /// <summary>True while props are floating back into place.</summary>
    public bool IsTidying => _tidying;

    /// <summary>x of the biggest pile of fallen stuff on the floor (for the dancer to sweep), or null if tidy.</summary>
    public float? MessiestX
    {
        get
        {
            Span<int> count = stackalloc int[8];
            Span<float> sum = stackalloc float[8];
            foreach (var p in _props)
            {
                if (p.Kind == PropKind.Shelf || !p.Displaced) continue;
                var c = p.Body.Center;
                if (c.Y < FloorY - 0.9f) continue;
                int bin = Math.Clamp((int)c.X, 0, 7);
                count[bin]++;
                sum[bin] += c.X;
            }
            int best = -1;
            for (int i = 0; i < 8; i++)
                if (count[i] > 0 && (best < 0 || count[i] > count[best])) best = i;
            return best < 0 ? null : sum[best] / count[best];
        }
    }

    // ------------------------------------------------------------------ music reactions

    public void Rattle(float strength, float dt)
    {
        if (_tidying) return;
        float s = Math.Clamp(strength, 0f, 1.5f);
        foreach (var p in _props)
        {
            var b = p.Body;
            switch (p.Rattle)
            {
                case RattleMode.Swing:
                    if (p.Nails.Count == 0 || p.Nails[0].Broken) break;
                    // bleed off the old swing (the engine has almost no air drag), then a gentle nudge
                    foreach (var pt in b.Points) pt.Prev = pt.Pos - (pt.Pos - pt.Prev) * 0.55f;
                    if (_rng.NextDouble() < 0.5) b.Push(new Vector2(Rand(-1, 1) * 0.2f * s, 0), dt);
                    break;
                case RattleMode.Hop:
                    if (IsResting(b, dt))
                        b.Push(new Vector2(Rand(-0.12f, 0.12f) * s, -Rand(0.25f, 0.55f) * s), dt);
                    break;
                case RattleMode.Rock:
                    // lift one bottom corner: it rocks and settles
                    if (IsResting(b, dt) && b.Kind == BodyKind.Box)
                        b.Points[_rng.Next(2) + 2].Push(new Vector2(0, -0.9f * s), dt);
                    break;
                case RattleMode.Creep:
                    if (IsResting(b, dt))
                        b.Push(new Vector2(p.CreepDir * Rand(0.1f, 0.3f) * s, -0.2f * s), dt);
                    break;
            }
        }
    }

    private bool IsResting(Body b, float dt) => b.Velocity(dt).LengthSquared() < 0.04f;

    public bool TriggerDisaster(int level)
    {
        if (_tidying) return false;
        level = Math.Clamp(level, 1, 4);
        var list = _disasters[level];
        if (_nextDisaster[level] >= list.Count) return false;
        list[_nextDisaster[level]++]();
        return true;
    }

    // ------------------------------------------------------------------ tidy

    public void BeginTidy()
    {
        if (_tidying) return;
        _tidying = true;
        _tidyTime = 0;
        int order = 0;
        foreach (var p in _props.OrderBy(p => p.Kind == PropKind.Shelf ? 0 : 1).ThenBy(p => Vector2.Distance(p.Body.Center, p.Home)))
        {
            p.TidyFrom = p.Body.Center;
            p.TidyFromAngle = p.Body.Angle;
            p.TidyActive = p.Displaced || Vector2.Distance(p.Body.Center, p.Home) > 0.03f;
            p.TidyArrived = !p.TidyActive;
            // Flying home passes through other things; colliding would shove props apart at silly speeds
            // (which could also look like a "bonk" to the host). Collisions come back when everything is home.
            p.Body.Collidable = false;
            // shelves first so things have somewhere to land; the rest staggered
            p.TidyDelay = p.Kind == PropKind.Shelf ? 0f : 0.25f + 0.12f * order++ % 1.6f;
            foreach (var n in p.Nails) n.Broken = true;  // re-attached when everything is home
        }
    }

    public bool UpdateTidy(float dt)
    {
        if (!_tidying) return true;
        _tidyTime += dt;
        const float Travel = 1.4f;
        bool done = true;
        foreach (var p in _props)
        {
            if (!p.TidyActive)
            {
                // keep undisturbed props exactly in place while nails are off
                p.Body.Reset(p.Home, p.HomeAngle);
                continue;
            }
            float u = Math.Clamp((_tidyTime - p.TidyDelay) / Travel, 0f, 1f);
            if (u < 1f) done = false;
            float e = u * u * (3f - 2f * u);
            var pos = Vector2.Lerp(p.TidyFrom, p.Home, e) + new Vector2(0, -MathF.Sin(MathF.PI * u) * 0.45f); // float up and over
            float ang = p.TidyFromAngle - Prop.AngleDiff(p.TidyFromAngle, p.HomeAngle) * e;
            p.Body.Reset(pos, ang);
            if (u >= 1f && !p.TidyArrived)
            {
                p.TidyArrived = true;
                for (int k = 0; k < 3; k++)
                    _sparkles.Add((p.Home + new Vector2(Rand(-0.2f, 0.2f), Rand(-0.2f, 0.1f)), -k * 0.08f));
            }
        }
        if (!done) return false;

        foreach (var p in _props)
        {
            p.Body.Reset(p.Home, p.HomeAngle);
            foreach (var n in p.Nails) n.Broken = false;
            p.Body.Collidable = true;
            p.TidyActive = false;
        }
        Array.Clear(_nextDisaster);
        _tidying = false;
        return true;
    }

    // ------------------------------------------------------------------ drawing

    public void DrawBack(SKCanvas c, SKCanvas? glow, float time, AudioFrame f, bool overlay)
    {
        EnsurePictures();
        if (!overlay)
        {
            DrawStaticCached(c);
            foreach (var a in _animatedBack) a(c, glow, time, f);   // window sky, LEDs, fire — above the static art
        }
        else if (!_furnitureBounds.IsEmpty)
        {
            // Over the desktop: furniture only, see-through.
            _layer.Color = SKColors.White.WithAlpha(150);
            c.SaveLayer(_furnitureBounds, _layer);
            c.DrawPicture(_furniturePicture!);
            c.Restore();
        }
        DrawLights(c, glow, time, f, behindProps: true);
    }

    public void DrawProps(SKCanvas c, SKCanvas? glow, float time, AudioFrame f)
    {
        foreach (var p in _props) PropArt.Draw(c, p, time, f, _palette);
        DrawLights(c, glow, time, f, behindProps: false);

        // tidy sparkles
        for (int i = _sparkles.Count - 1; i >= 0; i--)
        {
            var (pos, age) = _sparkles[i];
            age += 1f / 60f;
            if (age > 0.7f) { _sparkles.RemoveAt(i); continue; }
            _sparkles[i] = (pos, age);
            if (age < 0) continue;
            float k = age / 0.7f;
            float r = 0.12f * MathF.Sin(MathF.PI * k);
            PropArt.Sparkle(c, pos.X, pos.Y - k * 0.15f, r, new SKColor(0xFF, 0xF6, 0xC8, (byte)(255 * (1 - k))));
            if (glow != null)
            {
                _fill.Color = new SKColor(0xFF, 0xF0, 0xB0, (byte)(160 * (1 - k)));
                glow.DrawCircle(pos.X, pos.Y - k * 0.15f, 0.2f, _fill);
            }
        }
    }

    public void DrawFront(SKCanvas c, SKCanvas? glow, float time, AudioFrame f, bool overlay)
    {
        if (overlay)
        {
            _stroke.Color = new SKColor(0xFF, 0xFF, 0xFF, 120);
            _stroke.StrokeWidth = 0.02f;
            c.DrawLine(-4, FloorY + 0.01f, 12, FloorY + 0.01f, _stroke);
            return;
        }
        // soft shadow line where the floor meets the wall is in the picture; here just a gentle foreground vignette
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, FloorY + 0.1f), new SKPoint(0, FloorY + 1.6f),
            new[] { SKColors.Transparent, new SKColor(0, 0, 0, 90) }, null, SKShaderTileMode.Clamp);
        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        c.DrawRect(-4, FloorY + 0.1f, 16, 3, _fill);
        _fill.Shader = null;
    }

    private void DrawLights(SKCanvas c, SKCanvas? glow, float time, AudioFrame f, bool behindProps)
    {
        foreach (var l in _lights)
        {
            if (l.BehindProps != behindProps) continue;
            var pos = l.Pos;
            float on = 1f;
            if (l.Owner is { } owner)
            {
                // a light follows its lamp; knocked over → flickers and dims
                var b = owner.Body;
                float ca = MathF.Cos(b.Angle), sa = MathF.Sin(b.Angle);
                pos = b.Center + new Vector2(l.Offset.X * ca - l.Offset.Y * sa, l.Offset.X * sa + l.Offset.Y * ca);
                if (MathF.Abs(Prop.AngleDiff(b.Angle, owner.HomeAngle)) > 0.6f)
                    on = MathF.Sin(time * 37f + l.Seed) > 0.3f ? 0.25f : 0.05f;
            }
            float level = l.Kind switch
            {
                LightKind.Steady => 0.85f + 0.15f * f.Bass,
                LightKind.Bass => 0.45f + 0.55f * MathF.Min(1f, f.Bass * 1.3f + f.Pulse * 0.4f),
                LightKind.Twinkle => 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(time * (2f + l.Seed % 5) + l.Seed)) * (0.6f + 0.6f * f.Treble),
                _ => 1f,
            };
            level *= on;
            if (l.BulbRadius > 0)
            {
                _fill.Color = ColorUtil.Mix(PropArt.Shade(l.Color, 0.5f), l.Color, Math.Clamp(level, 0, 1));
                c.DrawCircle(pos.X, pos.Y, l.BulbRadius, _fill);
            }
            if (glow != null && l.GlowRadius > 0)
            {
                _fill.Color = l.Color.WithAlpha((byte)(Math.Clamp(level, 0f, 1f) * l.GlowAlpha));
                glow.DrawCircle(pos.X, pos.Y, l.GlowRadius, _fill);
            }
        }
    }

    private SKImage? _raster;
    private SKMatrix _rasterMatrix;
    private SKRectI _rasterRect;

    /// <summary>
    /// Draws walls + furniture from a screen-resolution raster made once from the recorded pictures.
    /// Replaying hundreds of vector shapes every frame is slow on the CPU; a single image blit is cheap.
    /// Re-rasterises only when the canvas scale changes or a translation (e.g. screen shake) moves past the margin.
    /// </summary>
    private void DrawStaticCached(SKCanvas c)
    {
        var m = c.TotalMatrix;
        if (m.SkewX != 0 || m.SkewY != 0 || m.Persp0 != 0 || m.Persp1 != 0)
        {
            c.DrawPicture(_wallPicture!);
            c.DrawPicture(_furniturePicture!);
            return;
        }
        var clip = c.DeviceClipBounds;
        float dx = m.TransX - _rasterMatrix.TransX, dy = m.TransY - _rasterMatrix.TransY;
        bool reuse = _raster != null && m.ScaleX == _rasterMatrix.ScaleX && m.ScaleY == _rasterMatrix.ScaleY;
        if (reuse)
        {
            var moved = new SKRect(_rasterRect.Left + dx, _rasterRect.Top + dy, _rasterRect.Right + dx, _rasterRect.Bottom + dy);
            var mapped = m.MapRect(DrawBounds);
            // still covers the visible area (or everything the room draws)?
            var need = SKRect.Intersect(new SKRect(clip.Left, clip.Top, clip.Right, clip.Bottom), mapped);
            reuse = moved.Contains(need);
        }
        if (!reuse)
        {
            const int Margin = 48;
            var mapped = m.MapRect(DrawBounds);
            var area = SKRect.Intersect(new SKRect(clip.Left - Margin, clip.Top - Margin, clip.Right + Margin, clip.Bottom + Margin), mapped);
            var rect = SKRectI.Round(area);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            _raster?.Dispose();
            using var surface = SKSurface.Create(new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var sc = surface.Canvas;
            sc.Clear(SKColors.Transparent);
            sc.SetMatrix(SKMatrix.CreateTranslation(-rect.Left, -rect.Top).PreConcat(m));
            sc.DrawPicture(_wallPicture!);
            sc.DrawPicture(_furniturePicture!);
            _raster = surface.Snapshot();
            _rasterMatrix = m;
            _rasterRect = rect;
            dx = dy = 0;
        }
        c.Save();
        c.ResetMatrix();
        c.DrawImage(_raster!, _rasterRect.Left + dx, _rasterRect.Top + dy);
        c.Restore();
    }

    private void EnsurePictures()
    {
        if (_wallPicture != null) return;
        using (var rec = new SKPictureRecorder())
        {
            var c = rec.BeginRecording(DrawBounds);
            foreach (var a in _wallArt) a(c);
            _wallPicture = rec.EndRecording();
        }
        using (var rec = new SKPictureRecorder())
        {
            var c = rec.BeginRecording(DrawBounds);
            foreach (var a in _furnitureArt) a(c);
            _furniturePicture = rec.EndRecording();
        }
    }

    // ------------------------------------------------------------------ building helpers

    private float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    private Prop AddProp(Body body, PropKind kind, SKColor color, SKColor accent, RattleMode rattle = RattleMode.Hop)
    {
        var p = new Prop { Body = body, Kind = kind, Color = color, Accent = accent, Rattle = rattle, Seed = _rng.Next() };
        body.Tag = p;
        foreach (var pt in body.Points) pt.Friction = 0.7f;
        _props.Add(p);
        _bodies.Add(body);
        return p;
    }

    /// <summary>A box prop resting with its bottom at <paramref name="bottomY"/>.</summary>
    private Prop Box(PropKind kind, float x, float bottomY, float w, float h, float mass, SKColor color, SKColor accent, RattleMode rattle = RattleMode.Hop)
        => AddProp(_world.AddBox(new Vector2(x, bottomY - h / 2 - 0.004f), new Vector2(w, h), mass), kind, color, accent, rattle);

    private Prop Ball(PropKind kind, float x, float bottomY, float r, float mass, SKColor color, SKColor accent, RattleMode rattle = RattleMode.Hop)
    {
        var p = AddProp(_world.AddCircle(new Vector2(x, bottomY - r - 0.004f), r, mass), kind, color, accent, rattle);
        p.Body.Points[0].Friction = 0.25f;
        return p;
    }

    /// <summary>Something hanging from a single nail/hook by a wire to its two top corners; swings, and drops when the wire breaks.</summary>
    private Prop Hang(PropKind kind, Vector2 center, float w, float h, float mass, Vector2 anchor, SKColor color, SKColor accent)
    {
        var p = AddProp(_world.AddBox(center, new Vector2(w, h), mass), kind, color, accent, RattleMode.Swing);
        p.Nails.Add(_world.Nail(p.Body.Points[0], anchor, 0.002f));
        p.Nails.Add(_world.Nail(p.Body.Points[1], anchor, 0.002f));
        p.RopeAnchor = anchor;
        foreach (var pt in p.Body.Points) pt.Mask = CollisionMask.Bounds | CollisionMask.Props; // hangs in front of shelves
        return p;
    }

    /// <summary>A wall shelf plank held by brackets at both ends; the right (or left) end can snap so it tips and dumps its load.</summary>
    private Prop Shelf(float x0, float x1, float topY, SKColor wood, bool snapRight = true)
    {
        const float T = 0.06f;
        var p = AddProp(_world.AddBox(new Vector2((x0 + x1) / 2, topY + T / 2), new Vector2(x1 - x0, T), 6f), PropKind.Shelf, wood, wood, RattleMode.None);
        var pts = p.Body.Points;
        // each end held from above and below → rigid; snapping one end turns the other into a hinge
        Stick N(Particle q, float dy) => _world.Nail(q, q.Pos + new Vector2(0, dy));
        var left = new[] { N(pts[0], -0.05f), N(pts[3], 0.05f) };
        var right = new[] { N(pts[1], -0.05f), N(pts[2], 0.05f) };
        p.Nails.AddRange(left);
        p.Nails.AddRange(right);
        p.SnapNails.AddRange(snapRight ? right : left);
        p.CreepDir = snapRight ? 1 : -1;   // the way the plank tips when it snaps
        foreach (var pt in pts) pt.Mask = CollisionMask.Bounds | CollisionMask.Props;

        // brackets (drawn on the wall)
        _furnitureArt.Add(c =>
        {
            foreach (float bx in new[] { x0 + 0.12f, x1 - 0.12f })
            {
                _fill.Color = PropArt.Shade(wood, 0.55f);
                using var path = new SKPath();
                path.MoveTo(bx - 0.03f, topY + T);
                path.LineTo(bx + 0.03f, topY + T);
                path.LineTo(bx + 0.03f, topY + T + 0.22f);
                path.Close();
                c.DrawPath(path, _fill);
            }
        });
        return p;
    }

    /// <summary>A static, collidable surface (table/desk/counter/cabinet top) — drawing supplied separately.</summary>
    private void Surface(float x0, float x1, float topY, float thickness = 0.06f)
    {
        var b = _world.AddBox(new Vector2((x0 + x1) / 2, topY + thickness / 2), new Vector2(x1 - x0, thickness), 0f);
        b.Collidable = true;
        foreach (var pt in b.Points) pt.Friction = 0.8f;
        _furnitureBounds = Union(_furnitureBounds, new SKRect(x0 - 0.1f, topY - 0.1f, x1 + 0.1f, FloorY + 0.05f));
    }

    private static SKRect Union(SKRect a, SKRect b) => a.IsEmpty ? b : SKRect.Union(a, b);

    private void Furniture(SKRect bounds, Action<SKCanvas> draw)
    {
        _furnitureArt.Add(draw);
        _furnitureBounds = Union(_furnitureBounds, bounds);
    }

    // ------------------------------------------------------------------ disaster helpers

    private void Disaster(int level, Action action) => _disasters[level].Add(action);

    private const float StepDt = 1f / 120f;

    /// <summary>Cuts whatever holds the prop up and gives it a little nudge.</summary>
    private void Drop(Prop p, float nudgeX = 0f)
    {
        foreach (var n in p.Nails) n.Broken = true;
        p.Body.Push(new Vector2(nudgeX, 0.2f), StepDt);
        foreach (var pt in p.Body.Points) pt.Mask = CollisionMask.All;
    }

    /// <summary>Shoves props sideways off whatever they stand on. They hop as they go: contact friction is
    /// strong in this engine, so a little air time is what gets things over the edge.</summary>
    private void Slide(IEnumerable<Prop> props, float dir, float speed = 2.2f)
    {
        float k = 0;
        foreach (var p in props)
        {
            p.Body.Push(new Vector2(dir * speed * 1.35f * Rand(0.85f, 1.25f), -Rand(1.6f, 2.4f) - k), StepDt);
            k += 0.15f;
        }
    }

    /// <summary>Snaps one end of a shelf: it swings down and its load slides off the low end.</summary>
    private void Snap(Prop shelf, IEnumerable<Prop>? load = null)
    {
        foreach (var n in shelf.SnapNails) n.Broken = true;
        shelf.Body.Push(new Vector2(0, 1.2f), StepDt);
        if (load == null) return;
        float dir = shelf.CreepDir; // which way the plank tips (set in Shelf())
        foreach (var p in load)
            p.Body.Push(new Vector2(dir * Rand(1.2f, 2.2f), -Rand(0.8f, 1.6f)), StepDt);
    }

    /// <summary>Tips a standing prop over: shoves its top and lifts the trailing foot so it pivots on the other.</summary>
    private void Topple(Prop p, float dir, float speed = 2.2f)
    {
        foreach (var n in p.Nails) n.Broken = true;
        var pts = p.Body.Points;
        pts[0].Push(new Vector2(dir * speed, -0.3f), StepDt);
        pts[1].Push(new Vector2(dir * speed, -0.3f), StepDt);
        (dir > 0 ? pts[3] : pts[2]).Push(new Vector2(0, -speed * 0.6f), StepDt);
    }

    private void Kick(Prop p, float vx, float vy = -1.5f) => p.Body.Push(new Vector2(vx, vy), StepDt);
}

internal enum LightKind { Steady, Bass, Twinkle }

/// <summary>A light source drawn every frame (bulb on the main canvas, soft halo on the glow layer).</summary>
internal sealed class RoomLight
{
    public Vector2 Pos;
    public Prop? Owner;            // follow this prop (lamp) — Pos ignored, Offset in the prop's frame
    public Vector2 Offset;
    public SKColor Color;
    public LightKind Kind = LightKind.Steady;
    public float BulbRadius;        // 0 = halo only
    public float GlowRadius = 0.5f;
    public float GlowAlpha = 150f;
    public int Seed;
    public bool BehindProps = true;
}
