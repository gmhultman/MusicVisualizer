using System.Numerics;
using MusicVisualizer.Audio;
using MusicVisualizer.Visuals.Disaster;
using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// A stick figure dances to the music in a room that slowly falls apart around it. Chaos builds with the
/// song's energy: bass hits rattle the room, and each threshold triggers a bigger disaster (things fall off
/// walls and shelves, shelves collapse, the ceiling light comes down). Falling things bonk the dancer, who
/// flops over like a ragdoll and gets back up. When the music calms down, the dancer sweeps up, the room
/// puts itself back together, and it can all happen again. The room changes with the colour scheme.
/// </summary>
public sealed class DanceDisasterVisualizer : IVisualizer
{
    private const float PhysicsStep = 1f / 120f;
    private static readonly float[] ChaosThresholds = [0.18f, 0.42f, 0.68f, 0.9f];
    private static readonly string[] BonkWords = ["OOF!", "BONK!", "OUCH!", "WHOA!", "YIKES!", "OOPS!"];

    private enum Phase { Party, Tidying }

    private PhysicsWorld? _world;
    private Room? _room;
    private Ragdoll? _dancer;
    private (Palette? palette, bool overlay) _builtFor;
    private readonly Random _rng = new(7);

    private float _physicsDebt, _time;
    private float _energy, _chaos, _quietFor, _sinceDisaster;
    private int _level;
    private Phase _phase = Phase.Party;
    private bool _roomTidying;
    private float _phaseTime;

    private Vector2 _shake;
    private float _shakeAmount;

    private readonly Dictionary<Body, float> _bonkCooldown = [];
    private readonly Dictionary<Body, float> _prevVy = [];
    private readonly List<Fx> _fx = [];

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKFont _font = new(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 0.3f);
    private readonly SKFont _hudFont = new(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 14f);
    private readonly SKPath _path = new();

    private enum FxKind { Bubble, Dust, Sparkle }

    private sealed class Fx
    {
        public FxKind Kind;
        public Vector2 Pos, Vel;
        public float Age, Life, Size;
        public string Text = "";
        public float Tilt;
    }

    public string Name => "Dance Disaster";

    // The room is opaque, so glows (lamps, LEDs, string lights) bloom over it rather than hiding behind it.
    public bool GlowAbove => true;

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        float dt = ctx.Dt;
        _time += dt;
        EnsureScene(ctx);
        var world = _world!;
        var room = _room!;
        var dancer = _dancer!;

        // How hard the music is going right now (smoothed over ~½ s).
        float rawEnergy = Math.Clamp(0.6f * frame.Level + 0.5f * frame.Bass, 0f, 1f);
        _energy += (rawEnergy - _energy) * (1f - MathF.Exp(-dt / 0.5f));

        Direct(frame, dt, room, dancer);
        dancer.Update(frame, dt, _energy);

        // Fixed-step physics.
        _physicsDebt = MathF.Min(_physicsDebt + dt, PhysicsStep * 8);
        while (_physicsDebt >= PhysicsStep)
        {
            dancer.PreStep(PhysicsStep); // muscles pull toward the dance pose each substep
            world.Step(PhysicsStep);
            _physicsDebt -= PhysicsStep;
        }

        DetectBonksAndLandings(room, dancer, dt);
        UpdateFx(dt);
        _shakeAmount *= MathF.Exp(-dt / 0.18f);
        _shake = new Vector2((float)(_rng.NextDouble() * 2 - 1), (float)(_rng.NextDouble() * 2 - 1)) * _shakeAmount;

        Draw(ctx, room, dancer, frame);
    }

    // ------------------------------------------------------------------ scene

    private void EnsureScene(in RenderContext ctx)
    {
        var key = (ctx.Palette, ctx.Overlay);
        if (_world != null && key == _builtFor) return;
        _builtFor = key;
        _world = new PhysicsWorld(0f, Room.Width, 0f, Room.FloorY);
        _room = new Room(_world, ctx.Palette, new Random(11));
        _dancer = new Ragdoll(_world, _room.DancerHomeX);
        _chaos = 0;
        _level = 0;
        _phase = Phase.Party;
        _roomTidying = false;
        _fx.Clear();
        _bonkCooldown.Clear();
        _prevVy.Clear();
    }

    // ------------------------------------------------------------------ the chaos director

    private void Direct(AudioFrame frame, float dt, Room room, Ragdoll dancer)
    {
        _phaseTime += dt;
        _sinceDisaster += dt;
        _quietFor = _energy < 0.12f ? _quietFor + dt : 0f;

        if (_phase == Phase.Party)
        {
            // Chaos builds while the music is going; big beats and drops push it faster.
            _chaos += dt * MathF.Max(0f, _energy - 0.2f) * 0.055f;
            if (frame.Beat)
            {
                room.Rattle(0.25f + 0.75f * frame.BeatStrength * _energy, PhysicsStep);
                _shakeAmount = MathF.Max(_shakeAmount, 0.012f * frame.BeatStrength * (1 + _level));
                _chaos += 0.012f * frame.BeatStrength;
                if (frame.BeatStrength > 0.8f && _energy > 0.5f) _chaos += 0.06f; // a drop!
            }
            _chaos = MathF.Min(_chaos, 1.05f);

            while (_level < ChaosThresholds.Length && _chaos >= ChaosThresholds[_level])
            {
                _level++;
                room.TriggerDisaster(_level);
                _sinceDisaster = 0;
                _shakeAmount = MathF.Max(_shakeAmount, 0.03f + 0.025f * _level);
            }

            // Calm down → tidy up (after a quiet spell, or a while after the finale).
            bool calm = _quietFor > 3f || (_level >= ChaosThresholds.Length && _sinceDisaster > 14f);
            if (_level > 0 && calm && dancer.State == DancerState.Dancing)
            {
                _phase = Phase.Tidying;
                _phaseTime = 0;
                _roomTidying = false;
                dancer.StartTidy(room.MessiestX ?? dancer.HomeX);
            }
        }
        else
        {
            // The dancer sweeps first, then the room magically puts itself back.
            if (!_roomTidying && (dancer.IsTidyDone || _phaseTime > 8f))
            {
                room.BeginTidy();
                _roomTidying = true;
            }
            if (_roomTidying && (room.UpdateTidy(dt) || _phaseTime > 16f))
            {
                dancer.StopTidy();
                _phase = Phase.Party;
                _phaseTime = 0;
                _chaos = 0;
                _level = 0;
                for (int i = 0; i < 6; i++) Spawn(FxKind.Sparkle, new Vector2(1f + (float)_rng.NextDouble() * 6f, 1f + (float)_rng.NextDouble() * 2.5f));
            }
        }
    }

    private void DetectBonksAndLandings(Room room, Ragdoll dancer, float dt)
    {
        var head = dancer.Head;
        foreach (var body in room.Props)
        {
            var v = body.Velocity(PhysicsStep);
            float speed = v.Length();
            var c = body.Center;
            float r = body.Kind == BodyKind.Circle ? body.Radius : MathF.Max(body.Size.X, body.Size.Y) * 0.5f;

            if (_bonkCooldown.TryGetValue(body, out float cd)) _bonkCooldown[body] = cd - dt;

            // Bonk: something fast hits the dancer's head or upper body.
            // (Not while the room tidies itself: props fly home with collisions off.)
            if (speed > 2.2f && !room.IsTidying && body.Collidable
                && dancer.State is DancerState.Dancing or DancerState.Tidying
                && (!_bonkCooldown.TryGetValue(body, out cd) || cd <= 0))
            {
                bool hit = Vector2.Distance(c, head.Pos) < head.Radius + r + 0.05f;
                if (!hit)
                    foreach (var j in dancer.Joints)
                        if (j.Pos.Y < head.Pos.Y + 0.6f && Vector2.Distance(c, j.Pos) < r + 0.08f) { hit = true; break; }
                if (hit)
                {
                    var kick = Vector2.Normalize(v + new Vector2(0, 0.001f)) * MathF.Min(speed * 0.5f, 4f);
                    dancer.Knock(kick);
                    _bonkCooldown[body] = 2f;
                    _shakeAmount = MathF.Max(_shakeAmount, 0.04f);
                    SpawnBubble(head.Pos + new Vector2(0.15f, -0.45f), BonkWords[_rng.Next(BonkWords.Length)]);
                }
            }

            // Dust puff when something lands hard on the floor.
            _prevVy.TryGetValue(body, out float prevVy);
            if (prevVy > 2.5f && v.Y < 0.6f && c.Y > Room.FloorY - r - 0.25f)
            {
                for (int i = 0; i < 4; i++) Spawn(FxKind.Dust, new Vector2(c.X, Room.FloorY - 0.05f));
                _shakeAmount = MathF.Max(_shakeAmount, 0.015f);
            }
            _prevVy[body] = v.Y;

            // Banana-peel moment: something rolling past the dancer's feet trips them up (sometimes).
            if (dancer.State == DancerState.Dancing && body.Kind == BodyKind.Circle && MathF.Abs(v.X) > 0.8f
                && c.Y > Room.FloorY - 0.4f && MathF.Abs(c.X - head.Pos.X) < 0.3f && _rng.NextDouble() < 0.08)
            {
                dancer.Trip();
                SpawnBubble(head.Pos + new Vector2(0.15f, -0.45f), "WHOA!");
            }
        }
    }

    // ------------------------------------------------------------------ effects

    private void SpawnBubble(Vector2 pos, string text) =>
        _fx.Add(new Fx { Kind = FxKind.Bubble, Pos = pos, Vel = new Vector2(0, -0.25f), Life = 1.3f, Text = text, Tilt = ((float)_rng.NextDouble() - 0.5f) * 0.3f });

    private void Spawn(FxKind kind, Vector2 pos)
    {
        float a = (float)(_rng.NextDouble() * MathF.Tau);
        _fx.Add(kind == FxKind.Dust
            ? new Fx { Kind = kind, Pos = pos, Vel = new Vector2(MathF.Cos(a) * 0.8f, -0.2f - (float)_rng.NextDouble() * 0.4f), Life = 0.7f, Size = 0.08f + (float)_rng.NextDouble() * 0.08f }
            : new Fx { Kind = kind, Pos = pos, Vel = new Vector2(0, -0.15f), Life = 0.9f, Size = 0.06f + (float)_rng.NextDouble() * 0.06f });
    }

    private void UpdateFx(float dt)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var f = _fx[i];
            f.Age += dt;
            f.Pos += f.Vel * dt;
            if (f.Kind == FxKind.Dust) { f.Vel *= MathF.Exp(-dt * 3f); f.Size += dt * 0.25f; }
            if (f.Age >= f.Life) _fx.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ drawing

    private void Draw(in RenderContext ctx, Room room, Ragdoll dancer, AudioFrame frame)
    {
        var canvas = ctx.Canvas;
        var glow = ctx.GlowCanvas;
        float w = ctx.Width, h = ctx.Height;

        // Fit the 8 × 4.5 m room to the screen (overlay: anchor the floor to the bottom).
        float scale = MathF.Min(w / Room.Width, h / Room.Height);
        float ox = (w - Room.Width * scale) / 2 + _shake.X * scale;
        float oy = (ctx.Overlay ? h - Room.Height * scale : (h - Room.Height * scale) / 2) + _shake.Y * scale;

        canvas.Save();
        canvas.Translate(ox, oy);
        canvas.Scale(scale);
        glow?.Save();
        glow?.Translate(ox, oy);
        glow?.Scale(scale);

        room.DrawBack(canvas, glow, _time, frame, ctx.Overlay);
        room.DrawProps(canvas, glow, _time, frame);
        dancer.Draw(canvas, glow, DancerInk(room.ThemeName, ctx.Overlay), _time);
        room.DrawFront(canvas, glow, _time, frame, ctx.Overlay);
        if (dancer.Dizzy) DrawStars(canvas, dancer.Head.Pos, dancer.Head.Radius);
        DrawFx(canvas);

        glow?.Restore();
        canvas.Restore();

        if (!ctx.Overlay) DrawMeter(canvas, ctx.Scale);
    }

    private static SKColor DancerInk(string theme, bool overlay) =>
        // The figure's face and outline are dark, so the body needs a mid/light tone: white in dark rooms
        // (and over the desktop), a cheerful blue in light rooms.
        overlay || theme is "Neon" or "Aurora" ? new SKColor(0xF4, 0xF6, 0xFA) : new SKColor(0x2F, 0x6F, 0xE0);

    private void DrawStars(SKCanvas c, Vector2 head, float r)
    {
        // Cartoon stars circling the head while dizzy.
        for (int i = 0; i < 4; i++)
        {
            float a = _time * 4f + i * MathF.Tau / 4;
            var p = head + new Vector2(MathF.Cos(a) * r * 2.1f, -r * 1.2f + MathF.Sin(a) * r * 0.6f);
            DrawStar(c, p.X, p.Y, r * 0.42f, new SKColor(0xFF, 0xD8, 0x3A));
        }
    }

    private void DrawStar(SKCanvas c, float x, float y, float r, SKColor color)
    {
        _path.Reset();
        for (int k = 0; k < 10; k++)
        {
            float a = -MathF.PI / 2 + k * MathF.PI / 5;
            float rr = k % 2 == 0 ? r : r * 0.45f;
            if (k == 0) _path.MoveTo(x + MathF.Cos(a) * rr, y + MathF.Sin(a) * rr);
            else _path.LineTo(x + MathF.Cos(a) * rr, y + MathF.Sin(a) * rr);
        }
        _path.Close();
        _fill.Color = color;
        c.DrawPath(_path, _fill);
        _stroke.Color = new SKColor(0x7A, 0x52, 0x00);
        _stroke.StrokeWidth = r * 0.15f;
        c.DrawPath(_path, _stroke);
    }

    private void DrawFx(SKCanvas c)
    {
        foreach (var f in _fx)
        {
            float t = f.Age / f.Life;
            switch (f.Kind)
            {
                case FxKind.Dust:
                    _fill.Color = new SKColor(0xB8, 0xB0, 0xA4, (byte)(150 * (1 - t)));
                    c.DrawCircle(f.Pos.X, f.Pos.Y, f.Size, _fill);
                    break;
                case FxKind.Sparkle:
                    DrawStar(c, f.Pos.X, f.Pos.Y, f.Size * (1 - t), new SKColor(0xFF, 0xF4, 0xB0, (byte)(255 * (1 - t))));
                    break;
                case FxKind.Bubble:
                    DrawBubble(c, f, t);
                    break;
            }
        }
    }

    private void DrawBubble(SKCanvas c, Fx f, float t)
    {
        // Comic speech burst: pops in, holds, fades.
        float pop = t < 0.15f ? 0.6f + 0.4f * (t / 0.15f) * 1.15f : 1f;
        byte alpha = (byte)(255 * (t > 0.75f ? (1 - t) / 0.25f : 1f));
        float width = _font.MeasureText(f.Text) + 0.24f, height = 0.46f;

        c.Save();
        c.Translate(f.Pos.X, f.Pos.Y);
        c.RotateRadians(f.Tilt);
        c.Scale(pop);
        var rect = new SKRect(-width / 2, -height / 2, width / 2, height / 2);
        _path.Reset();
        _path.AddRoundRect(rect, 0.16f, 0.16f);
        _path.MoveTo(-0.08f, height / 2 - 0.02f);
        _path.LineTo(-0.22f, height / 2 + 0.2f);
        _path.LineTo(0.06f, height / 2 - 0.02f);
        _fill.Color = new SKColor(0xFF, 0xFF, 0xFF, alpha);
        c.DrawPath(_path, _fill);
        _stroke.Color = new SKColor(0x1C, 0x1C, 0x24, alpha);
        _stroke.StrokeWidth = 0.03f;
        c.DrawRoundRect(rect, 0.16f, 0.16f, _stroke);
        _fill.Color = new SKColor(0xE8, 0x26, 0x3A, alpha);
        c.DrawText(f.Text, 0, 0.1f, SKTextAlign.Center, _font, _fill);
        c.Restore();
    }

    /// <summary>A small "disaster level" meter in the corner.</summary>
    private void DrawMeter(SKCanvas c, float s)
    {
        float x = 18 * s, y = 18 * s, bw = 120 * s, bh = 8 * s;
        _hudFont.Size = 12 * s;
        _fill.Color = new SKColor(0, 0, 0, 90);
        c.DrawRoundRect(x - 8 * s, y - 6 * s, bw + 16 * s, 36 * s, 8 * s, 8 * s, _fill);
        _fill.Color = new SKColor(255, 255, 255, 200);
        c.DrawText(_phase == Phase.Tidying ? "TIDYING UP…" : "DISASTER LEVEL", x, y + 9 * s, SKTextAlign.Left, _hudFont, _fill);
        _fill.Color = new SKColor(255, 255, 255, 50);
        c.DrawRoundRect(x, y + 15 * s, bw, bh, bh / 2, bh / 2, _fill);
        float fillW = bw * Math.Clamp(_chaos, 0f, 1f);
        if (fillW > 1)
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(x, 0), new SKPoint(x + bw, 0),
                new[] { new SKColor(0x6A, 0xE0, 0x6A), new SKColor(0xFF, 0xC8, 0x3A), new SKColor(0xFF, 0x3A, 0x3A) }, null, SKShaderTileMode.Clamp);
            _fill.Color = SKColors.White;
            _fill.Shader = shader;
            c.DrawRoundRect(x, y + 15 * s, fillW, bh, bh / 2, bh / 2, _fill);
            _fill.Shader = null;
        }
    }
}
