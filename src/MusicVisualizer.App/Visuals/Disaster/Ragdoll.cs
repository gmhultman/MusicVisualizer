using System.Numerics;
using MusicVisualizer.Audio;
using SkiaSharp;

namespace MusicVisualizer.Visuals.Disaster;

internal enum DancerState { Dancing, Down, GettingUp, Tidying }

/// <summary>
/// The stick-figure dancer: an "active ragdoll". Its joints are physics particles joined by bone sticks;
/// "muscles" pull each joint toward an animated target pose (the dance), with a strength that drops to zero
/// when it gets knocked over, so it flops like a ragdoll — then it staggers back up and keeps dancing.
/// World metres, y down; the figure is ~1.65 m tall standing on <see cref="PhysicsWorld.Floor"/>.
/// </summary>
/// <remarks>
/// Host: call <see cref="Update"/> once per rendered frame, then for each physics substep call
/// <see cref="PreStep"/>(h) followed by <c>world.Step(h)</c>. (Without PreStep the muscles are applied once per
/// frame from Update — it still works, just a little looser.)
/// </remarks>
internal sealed class Ragdoll
{
    // Joint indices
    private const int JHead = 0, JNeck = 1, JPelvis = 2, JElbowL = 3, JHandL = 4, JElbowR = 5, JHandR = 6,
                      JKneeL = 7, JFootL = 8, JKneeR = 9, JFootR = 10, JointCount = 11;

    // Proportions (m)
    private const float TorsoLen = 0.5f, NeckLen = 0.19f, UpperArm = 0.3f, Forearm = 0.3f, Thigh = 0.42f, Shin = 0.42f;
    private const float HeadRadius = 0.13f, FootRadius = 0.04f, JointRadius = 0.035f;
    private const float BroomLength = 1.3f;

    private static readonly (int a, int b)[] Bones =
    [
        (JHead, JNeck), (JNeck, JPelvis),
        (JNeck, JElbowL), (JElbowL, JHandL), (JNeck, JElbowR), (JElbowR, JHandR),
        (JPelvis, JKneeL), (JKneeL, JFootL), (JPelvis, JKneeR), (JKneeR, JFootR),
    ];

    // How hard each joint's muscle pulls (hands/elbows looser so arms swing a bit).
    private static readonly float[] JointGain = [0.75f, 0.95f, 1f, 0.55f, 0.6f, 0.55f, 0.6f, 0.75f, 1f, 0.75f, 1f];

    private enum Move { SideStep, Wave, Disco, Robot, Chicken, Flail }

    private readonly PhysicsWorld _world;
    private readonly Particle[] _p = new Particle[JointCount];
    private readonly List<Stick> _sticks = [];
    private readonly Vector2[] _target = new Vector2[JointCount];
    private readonly Vector2[] _prevTarget = new Vector2[JointCount];
    private readonly Vector2[] _targetVel = new Vector2[JointCount];
    private readonly Vector2[] _snapshot = new Vector2[JointCount];
    private readonly Vector2[] _poseA = new Vector2[JointCount];
    private readonly Vector2[] _poseB = new Vector2[JointCount];
    private readonly Random _rng = new(7);

    private float _strength = 1f;
    private bool _preStepUsed;
    private float _h = 1f / 120f;
    private float _time;

    // Music tracking
    private float _interval = 0.5f, _sinceBeat = 10f, _phase;
    private int _beat;
    private float _energy, _music;
    private Move _move = Move.SideStep, _prevMove = Move.SideStep;
    private float _moveBlend = 1f;
    private int _flailUntil = -1, _flailCooldownUntil = -1;

    // Body / locomotion
    private float _rootX, _facing = 1f, _gait;
    private float _stagger;

    // States
    private float _downTimer, _downTime, _getUpT;
    private bool _tidyActive, _returning;
    private float _tidyTarget, _sweepTime;
    private bool _holdingBroom;
    private float _sinceKnock = 10f;

    private readonly SKPath _path = new();
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public Ragdoll(PhysicsWorld world, float homeX)
    {
        _world = world;
        HomeX = homeX;
        _rootX = homeX;
        StandPose(_poseA, homeX, 0f);
        for (int i = 0; i < JointCount; i++)
        {
            float r = i == JHead ? HeadRadius : i is JFootL or JFootR ? FootRadius : JointRadius;
            var p = world.AddParticle(_poseA[i], i == JHead ? 0.8f : 1f, r);
            p.Mask = CollisionMask.Bounds | CollisionMask.Props;
            p.Friction = i is JFootL or JFootR ? 0.8f : 0.5f;
            p.Bounce = 0.15f;
            _p[i] = p;
            _target[i] = _prevTarget[i] = _poseA[i];
        }
        foreach (var (a, b) in Bones) _sticks.Add(world.AddStick(_p[a], _p[b]));
        // A weak spine brace so the limp body keeps its head roughly on its shoulders.
        _sticks.Add(world.AddStick(_p[JHead], _p[JPelvis], 0.08f));
        Head = _p[JHead];
        Joints = _p;
    }

    public float HomeX { get; }
    public DancerState State { get; private set; } = DancerState.Dancing;
    public Particle Head { get; }
    public IReadOnlyList<Particle> Joints { get; }
    public bool Dizzy => State is DancerState.Down or DancerState.GettingUp;
    public bool IsTidyDone => _tidyActive && _sweepTime >= 2f;

    /// <summary>Which way the figure faces (+1 right, -1 left).</summary>
    public float Facing => _facing;

    /// <summary>Pelvis position (roughly the figure's centre).</summary>
    public Vector2 Position => _p[JPelvis].Pos;

    // ------------------------------------------------------------------ public actions

    public void Knock(Vector2 impulse, float seconds = 2.5f)
    {
        GoLimp(seconds);
        _p[JHead].Push(impulse, _h);
        _p[JNeck].Push(impulse * 0.9f, _h);
        _p[JPelvis].Push(impulse * 0.45f, _h);
        _p[JHandL].Push(impulse * 0.8f + new Vector2(0, -1.5f), _h);
        _p[JHandR].Push(impulse * 0.8f + new Vector2(0, -1.5f), _h);
        _p[JFootL].Push(new Vector2(-impulse.X * 0.25f, -0.6f), _h);
    }

    public void Trip()
    {
        GoLimp(2.6f);
        float f = _facing;
        _p[JFootL].Push(new Vector2(f * 4.2f, -2.6f), _h);
        _p[JFootR].Push(new Vector2(f * 3.8f, -2.3f), _h);
        _p[JKneeL].Push(new Vector2(f * 2.5f, -1.5f), _h);
        _p[JKneeR].Push(new Vector2(f * 2.5f, -1.5f), _h);
        _p[JHead].Push(new Vector2(-f * 1.6f, 0.3f), _h);
        _p[JNeck].Push(new Vector2(-f * 1.2f, 0.2f), _h);
        _p[JHandL].Push(new Vector2(-f * 1f, -3f), _h);
        _p[JHandR].Push(new Vector2(-f * 0.5f, -3.2f), _h);
    }

    public void StartTidy(float targetX)
    {
        _tidyActive = true;
        _returning = false;
        _tidyTarget = Math.Clamp(targetX, _world.Left + 0.7f, _world.Right - 0.7f);
        _sweepTime = 0f;
        if (State == DancerState.Dancing) State = DancerState.Tidying;
    }

    public void StopTidy()
    {
        _tidyActive = false;
        _sweepTime = 0f;
        _returning = true;
        if (State == DancerState.Tidying) State = DancerState.Dancing;
    }

    /// <summary>Applies the muscles for one physics substep. Call before each <c>world.Step(h)</c>.</summary>
    public void PreStep(float h)
    {
        _preStepUsed = true;
        _h = h;
        ApplyMuscles(h);
    }

    // ------------------------------------------------------------------ per frame

    public void Update(AudioFrame frame, float dt, float energy)
    {
        dt = Math.Clamp(dt, 0.0001f, 0.1f);
        _time += dt;
        _sinceKnock += dt;
        if (!CheckHealth()) return;

        TrackBeat(frame, dt);
        float e = Math.Clamp(energy, 0f, 1f);
        _energy += (e - _energy) * (1f - MathF.Exp(-dt / 0.6f));
        float musicTarget = Math.Clamp(frame.Level * 5f, 0f, 1f);
        _music += (musicTarget - _music) * (1f - MathF.Exp(-dt / (musicTarget > _music ? 0.15f : 1.2f)));

        switch (State)
        {
            case DancerState.Down:
                _strength = 0f;
                _downTime += dt;
                _downTimer -= dt;
                if (_downTimer <= 0f && (BodySpeed() < 1.2f || _downTimer < -1.5f)) BeginGetUp();
                break;

            case DancerState.GettingUp:
                _getUpT += dt;
                BuildGetUp();
                break;

            case DancerState.Tidying:
                _stagger = MathF.Max(0f, _stagger - dt);
                BuildTidy(dt);
                break;

            default:
                _stagger = MathF.Max(0f, _stagger - dt);
                BuildDance(frame, dt);
                break;
        }

        for (int i = 0; i < JointCount; i++)
        {
            _targetVel[i] = (_target[i] - _prevTarget[i]) / dt;
            _prevTarget[i] = _target[i];
        }
        if (!_preStepUsed) ApplyMuscles(dt);
    }

    // ------------------------------------------------------------------ muscles & health

    private void ApplyMuscles(float h)
    {
        if (_strength <= 0.001f) return;
        float k = 1f - MathF.Exp(-h / 0.022f);
        for (int i = 0; i < JointCount; i++)
        {
            var p = _p[i];
            float s = _strength * JointGain[i];
            p.Pos += (_target[i] - p.Pos) * (k * s);
            // Damp toward the pose's own motion so it tracks the dance instead of oscillating.
            var desiredPrev = p.Pos - _targetVel[i] * h;
            p.Prev += (desiredPrev - p.Prev) * (0.4f * s);
        }
    }

    private bool CheckHealth()
    {
        bool bad = false;
        foreach (var p in _p)
        {
            if (!float.IsFinite(p.Pos.X) || !float.IsFinite(p.Pos.Y) || !float.IsFinite(p.Prev.X) || !float.IsFinite(p.Prev.Y)
                || p.Pos.X < _world.Left - 1f || p.Pos.X > _world.Right + 1f || p.Pos.Y > _world.Floor + 0.5f || p.Pos.Y < _world.Top - 2f)
            {
                bad = true;
                break;
            }
        }
        if (!bad)
        {
            for (int i = 0; i < Bones.Length; i++)
            {
                var s = _sticks[i];
                float len = Vector2.Distance(s.A.Pos, s.B.Pos);
                if (s.Broken || len > s.Length * 2.2f) { bad = true; break; }
            }
        }
        if (!bad) return true;
        ResetStanding();
        return false;
    }

    private void ResetStanding()
    {
        _rootX = HomeX;
        StandPose(_poseA, HomeX, 0f);
        for (int i = 0; i < JointCount; i++)
        {
            _p[i].Place(_poseA[i]);
            _target[i] = _prevTarget[i] = _poseA[i];
            _targetVel[i] = Vector2.Zero;
        }
        foreach (var s in _sticks) s.Broken = false;
        _strength = 1f;
        State = _tidyActive ? DancerState.Tidying : DancerState.Dancing;
    }

    private float BodySpeed()
    {
        float sum = 0f;
        foreach (var p in _p) sum += (p.Pos - p.Prev).Length();
        return sum / JointCount / _h;
    }

    private void GoLimp(float seconds)
    {
        State = DancerState.Down;
        _strength = 0f;
        _downTimer = MathF.Max(0.6f, seconds);
        _downTime = 0f;
        _sinceKnock = 0f;
        _holdingBroom = false;
    }

    // ------------------------------------------------------------------ getting up

    private void BeginGetUp()
    {
        State = DancerState.GettingUp;
        _getUpT = 0f;
        for (int i = 0; i < JointCount; i++) _snapshot[i] = _p[i].Pos;
        _rootX = Math.Clamp(_p[JPelvis].Pos.X, _world.Left + 0.6f, _world.Right - 0.6f);
        // Face the way the head ended up, so it pushes up off its hands naturally.
        _facing = _p[JHead].Pos.X >= _p[JPelvis].Pos.X ? 1f : -1f;
        _strength = 0.3f;
    }

    private void BuildGetUp()
    {
        const float Phase1 = 0.65f, Phase2 = 0.7f;
        CrouchPose(_poseA, _rootX, _facing);
        StandPose(_poseB, _rootX, 0.25f * _facing * MathF.Sin(_time * 9f));
        float t = _getUpT;
        if (t < Phase1)
        {
            float k = Ease(t / Phase1);
            for (int i = 0; i < JointCount; i++) _target[i] = Vector2.Lerp(_snapshot[i], _poseA[i], k);
            _strength = 0.3f + 0.5f * k;
        }
        else
        {
            float k = Ease(Math.Clamp((t - Phase1) / Phase2, 0f, 1f));
            for (int i = 0; i < JointCount; i++) _target[i] = Vector2.Lerp(_poseA[i], _poseB[i], k);
            _strength = 0.8f + 0.2f * k;
        }
        if (t >= Phase1 + Phase2)
        {
            _strength = 1f;
            _stagger = 1.3f;
            State = _tidyActive ? DancerState.Tidying : DancerState.Dancing;
        }
    }

    // ------------------------------------------------------------------ music

    private void TrackBeat(AudioFrame frame, float dt)
    {
        _sinceBeat += dt;
        if (frame.Beat)
        {
            if (_sinceBeat is > 0.28f and < 1.2f) _interval += (_sinceBeat - _interval) * 0.35f;
            _sinceBeat = 0f;
            // Phase-lock: snap the internal beat clock to the detected beat.
            if (_phase > 0.5f) { _phase = 0f; OnBeat(frame); }
            else _phase *= 0.4f;
        }
        else if (_sinceBeat > 2.5f)
        {
            _interval += (0.5f - _interval) * dt; // no beats lately: drift back to ~120 BPM
        }

        _phase += dt / _interval;
        while (_phase >= 1f)
        {
            _phase -= 1f;
            OnBeat(frame);
        }
        _moveBlend = MathF.Min(1f, _moveBlend + dt / (_interval * 0.6f));
    }

    private void OnBeat(AudioFrame frame)
    {
        _beat++;
        // Big drop with lots of energy: lose it for a few beats.
        if (_energy > 0.7f && frame.Pulse > 0.5f && _beat >= _flailCooldownUntil && _move != Move.Flail)
        {
            SetMove(Move.Flail);
            _flailUntil = _beat + 4;
            _flailCooldownUntil = _beat + 16;
            return;
        }
        if (_move == Move.Flail && _beat >= _flailUntil) SetMove(PickMove());
        else if (_beat % 8 == 0) SetMove(PickMove());
    }

    private Move PickMove()
    {
        Move m;
        do m = (Move)_rng.Next(5); while (m == _move);
        return m;
    }

    private void SetMove(Move m)
    {
        if (m == _move) return;
        _prevMove = _move;
        _move = m;
        _moveBlend = 0f;
    }

    // ------------------------------------------------------------------ dancing

    private struct Rig
    {
        public float RootX, HipShift, Dip, Lean, HeadTilt, Nod, Jump;
        public float FootLx, FootLLift, FootRx, FootRLift;
        public Vector2 HandL, HandR;   // relative to the neck
        public float ElbowUp;          // 0 = elbows out/down, 1 = elbows out/up

        public static Rig Lerp(in Rig a, in Rig b, float t) => new()
        {
            RootX = L(a.RootX, b.RootX, t), HipShift = L(a.HipShift, b.HipShift, t), Dip = L(a.Dip, b.Dip, t),
            Lean = L(a.Lean, b.Lean, t), HeadTilt = L(a.HeadTilt, b.HeadTilt, t), Nod = L(a.Nod, b.Nod, t),
            Jump = L(a.Jump, b.Jump, t), FootLx = L(a.FootLx, b.FootLx, t), FootLLift = L(a.FootLLift, b.FootLLift, t),
            FootRx = L(a.FootRx, b.FootRx, t), FootRLift = L(a.FootRLift, b.FootRLift, t),
            HandL = Vector2.Lerp(a.HandL, b.HandL, t), HandR = Vector2.Lerp(a.HandR, b.HandR, t),
            ElbowUp = L(a.ElbowUp, b.ElbowUp, t),
        };

        private static float L(float a, float b, float t) => a + (b - a) * t;
    }

    private Rig BaseRig(float rootX) => new()
    {
        RootX = rootX,
        FootLx = rootX - 0.17f,
        FootRx = rootX + 0.17f,
        HandL = new Vector2(-0.24f, 0.54f),
        HandR = new Vector2(0.24f, 0.54f),
    };

    private void BuildDance(AudioFrame frame, float dt)
    {
        // Wander a little around home; walk properly if far away (after a fall or tidying).
        float desired = _returning ? HomeX : HomeX + 0.35f * MathF.Sin(_time * 0.19f) + 0.12f * MathF.Sin(_time * 0.47f);
        float gap = desired - _rootX;
        if (_returning && MathF.Abs(gap) < 0.1f) _returning = false;
        if (MathF.Abs(gap) > 0.45f || (_returning && MathF.Abs(gap) > 0.1f))
        {
            WalkToward(desired, dt, 1.15f);
            var walk = WalkRig();
            Assemble(walk, _target);
            return;
        }
        _rootX += Math.Clamp(gap, -0.25f * dt, 0.25f * dt);

        var idle = IdleRig(_rootX);
        var cur = MoveRig(_move, _rootX);
        if (_moveBlend < 1f) cur = Rig.Lerp(MoveRig(_prevMove, _rootX), cur, Ease(_moveBlend));
        var rig = Rig.Lerp(idle, cur, _music * (1f - 0.6f * MathF.Min(1f, _stagger)));

        if (_stagger > 0f)
        {
            // Woozy after getting up: lurching lean and wobbly head.
            float s = MathF.Min(1f, _stagger);
            rig.Lean += 0.22f * s * MathF.Sin(_time * 6.5f);
            rig.HeadTilt += 0.35f * s * MathF.Sin(_time * 4.3f + 1f);
            rig.HipShift += 0.08f * s * MathF.Sin(_time * 3.1f);
        }
        Assemble(rig, _target);
    }

    private Rig IdleRig(float rootX)
    {
        var r = BaseRig(rootX);
        r.Dip = 0.012f * (0.5f + 0.5f * MathF.Sin(_time * 2f));
        r.HeadTilt = 0.06f * MathF.Sin(_time * 0.7f);
        r.HipShift = 0.02f * MathF.Sin(_time * 0.9f);
        r.FootRLift = 0.025f * MathF.Max(0f, MathF.Sin(_time * 6f)) * (MathF.Sin(_time * 0.5f) > 0.3f ? 1f : 0f); // toe tap
        return r;
    }

    private Rig MoveRig(Move move, float rootX)
    {
        var r = BaseRig(rootX);
        float e = _energy;
        float amp = 0.45f + 0.55f * e;
        float ph = _phase;
        float t = _beat + ph;
        float onBeat = 0.5f + 0.5f * MathF.Cos(MathF.Tau * ph);          // 1 right on the beat
        float dip = (0.03f + 0.1f * e) * onBeat;
        float sway = MathF.Sin(MathF.PI * t);                               // flips side every beat
        r.Dip = dip;
        r.Nod = 0.03f * onBeat * amp;

        switch (move)
        {
            case Move.SideStep:
            {
                r.HipShift = 0.09f * sway * amp;
                r.Lean = -0.07f * sway * amp;
                r.FootLx += 0.1f * sway * amp;
                r.FootRx += 0.1f * sway * amp;
                float lift = 0.08f * amp * MathF.Max(0f, MathF.Sin(MathF.Tau * ph));
                if (_beat % 2 == 0) r.FootRLift = lift; else r.FootLLift = lift;
                r.HandL = new Vector2(-0.3f + 0.12f * sway * amp, 0.42f - 0.15f * amp * onBeat);
                r.HandR = new Vector2(0.3f + 0.12f * sway * amp, 0.42f - 0.15f * amp * onBeat);
                r.HeadTilt = 0.12f * sway * amp;
                if (e > 0.4f && _beat % 2 == 1)
                {
                    // Clap on the off-beats when it's getting lively.
                    float c = MathF.Pow(MathF.Max(0f, MathF.Cos(MathF.PI * ph)), 4f);
                    r.HandL = Vector2.Lerp(r.HandL, new Vector2(-0.03f, 0.2f), c);
                    r.HandR = Vector2.Lerp(r.HandR, new Vector2(0.03f, 0.2f), c);
                }
                break;
            }
            case Move.Wave:
            {
                float w = MathF.Sin(MathF.PI * t * 0.5f);
                r.HandL = new Vector2(-0.3f + 0.24f * w, -0.44f - 0.08f * onBeat);
                r.HandR = new Vector2(0.3f + 0.24f * w, -0.44f - 0.08f * onBeat);
                r.ElbowUp = 1f;
                r.HipShift = -0.1f * w * amp;
                r.Lean = 0.07f * w;
                r.HeadTilt = 0.16f * w;
                break;
            }
            case Move.Disco:
            {
                // Point up-right, then down across the body, other hand on the hip.
                bool up = _beat % 2 == 0;
                float k = Ease(Math.Clamp(ph / 0.22f, 0f, 1f));
                var upPt = new Vector2(0.4f, -0.56f);
                var downPt = new Vector2(-0.36f, 0.48f);
                r.HandR = Vector2.Lerp(up ? downPt : upPt, up ? upPt : downPt, k);
                r.HandL = new Vector2(-0.16f, 0.44f);
                float side = up ? 1f : -1f;
                float hip = 0.1f * amp * (side * k - side * (1f - k));
                r.HipShift = hip;
                r.Lean = -0.08f * side * k;
                r.HeadTilt = 0.22f * side * k;
                r.FootLx = rootX - 0.22f;
                r.FootRx = rootX + 0.22f;
                r.FootRLift = up ? 0.05f * amp * MathF.Max(0f, MathF.Sin(MathF.Tau * ph)) : 0f;
                r.Dip = dip * 0.7f;
                break;
            }
            case Move.Robot:
            {
                // Jerky: snap to a new pose on every beat.
                float k = Ease(Math.Clamp(ph / 0.12f, 0f, 1f));
                var a = RobotPose(_beat - 1);
                var b = RobotPose(_beat);
                r.HandL = Vector2.Lerp(a.l, b.l, k);
                r.HandR = Vector2.Lerp(a.r, b.r, k);
                r.HeadTilt = a.head + (b.head - a.head) * k;
                r.HipShift = a.hip + (b.hip - a.hip) * k;
                r.Dip = 0.02f * onBeat;
                r.Nod = 0f;
                break;
            }
            case Move.Chicken:
            {
                // Hands in the armpits, elbows flapping on the beat, pecking head, bent knees.
                float flap = onBeat;
                r.HandL = new Vector2(-0.09f, 0.12f);
                r.HandR = new Vector2(0.09f, 0.12f);
                r.ElbowUp = flap;
                r.Dip = dip + 0.08f;
                r.Nod = 0.07f * MathF.Max(0f, MathF.Sin(MathF.Tau * ph * 2f));
                r.HeadTilt = 0.1f * sway;
                r.HipShift = 0.06f * sway;
                r.FootLx = rootX - 0.24f;
                r.FootRx = rootX + 0.24f;
                break;
            }
            case Move.Flail:
            {
                // Windmilling arms, headbanging, jumping.
                float a = MathF.PI * t * 1.1f;
                r.HandL = new Vector2(-0.12f + 0.5f * MathF.Cos(a + MathF.PI), 0.5f * MathF.Sin(a + MathF.PI));
                r.HandR = new Vector2(0.12f + 0.5f * MathF.Cos(-a), 0.5f * MathF.Sin(-a));
                r.ElbowUp = 0.5f;
                r.Jump = 0.16f * e * MathF.Max(0f, MathF.Sin(MathF.Tau * ph));
                r.Dip = dip * 1.3f;
                r.Nod = 0.09f * onBeat;
                r.HeadTilt = 0.35f * sway;
                r.Lean = 0.1f * MathF.Sin(MathF.PI * t * 0.5f);
                r.FootLx = rootX - 0.24f;
                r.FootRx = rootX + 0.24f;
                break;
            }
        }
        return r;
    }

    private static (Vector2 l, Vector2 r, float head, float hip) RobotPose(int beat)
    {
        uint hsh = (uint)(beat * 2654435761u);
        Vector2[] left = [new(-0.3f, -0.3f), new(-0.3f, 0.3f), new(-0.58f, 0f), new(-0.12f, 0.35f)];
        Vector2[] right = [new(0.3f, -0.3f), new(0.3f, 0.3f), new(0.58f, 0f), new(0.12f, 0.35f)];
        float[] heads = [0f, 0.2f, -0.2f, 0f];
        float[] hips = [0f, 0.06f, -0.06f, 0f];
        return (left[hsh % 4], right[(hsh >> 8) % 4], heads[(hsh >> 16) % 4], hips[(hsh >> 20) % 4]);
    }

    // ------------------------------------------------------------------ walking & tidying

    private void WalkToward(float x, float dt, float speed)
    {
        float gap = x - _rootX;
        if (MathF.Abs(gap) > 0.02f) _facing = MathF.Sign(gap);
        float stepLen = MathF.Min(MathF.Abs(gap), speed * dt);
        _rootX += MathF.Sign(gap) * stepLen;
        _gait += stepLen / 0.42f;
    }

    private Rig WalkRig()
    {
        var r = BaseRig(_rootX);
        float f = _facing;
        float s = MathF.Sin(MathF.PI * _gait);
        float c = MathF.Cos(MathF.PI * _gait);
        r.FootLx = _rootX + f * 0.27f * s;
        r.FootRx = _rootX - f * 0.27f * s;
        r.FootLLift = 0.14f * MathF.Max(0f, c);
        r.FootRLift = 0.14f * MathF.Max(0f, -c);
        r.Dip = 0.05f + 0.04f * MathF.Abs(c);
        r.Lean = 0.08f * f;
        r.HandL = new Vector2(-0.2f - f * 0.17f * s, 0.5f);
        r.HandR = new Vector2(0.2f + f * 0.17f * s, 0.5f);
        r.HeadTilt = 0.05f * f;
        if (_holdingBroom)
        {
            // Carry the broom upright in front.
            r.HandL = new Vector2(f * 0.2f, 0.12f);
            r.HandR = new Vector2(f * 0.24f, 0.42f);
        }
        return r;
    }

    private void BuildTidy(float dt)
    {
        _holdingBroom = true;
        float gap = _tidyTarget - _rootX;
        if (MathF.Abs(gap) > 0.12f)
        {
            WalkToward(_tidyTarget, dt, 1.1f);
            Assemble(WalkRig(), _target);
            return;
        }
        _sweepTime += dt;

        // Sweep in time with the music: one stroke per beat, bobbing.
        float f = _facing;
        float t = _beat + _phase;
        float stroke = MathF.Sin(MathF.PI * t);
        float onBeat = 0.5f + 0.5f * MathF.Cos(MathF.Tau * _phase);
        var r = BaseRig(_rootX);
        r.FootLx = _rootX - 0.22f;
        r.FootRx = _rootX + 0.22f;
        r.Lean = 0.22f * f;
        r.Dip = 0.06f + 0.04f * onBeat * _music;
        r.HipShift = -0.05f * f + 0.04f * stroke;
        r.HeadTilt = 0.12f * f + 0.08f * stroke * _music;
        Assemble(r, _target);

        // Hands on the handle, aimed at the bristles sweeping back and forth on the floor.
        var neck = _target[JNeck];
        var bristles = new Vector2(_rootX + f * (0.62f + 0.2f * stroke), _world.Floor - 0.06f);
        var top = neck + new Vector2(f * 0.1f, 0.16f);
        var dir = Vector2.Normalize(bristles - top);
        SetArm(top, top + dir * 0.42f);
    }

    private void SetArm(Vector2 handA, Vector2 handB)
    {
        // Upper hand = left joint, lower hand = right joint (both on the broom handle).
        var neck = _target[JNeck];
        var (eL, hL) = IK(neck, handA, UpperArm, Forearm, new Vector2(-_facing, 0.3f));
        var (eR, hR) = IK(neck, handB, UpperArm, Forearm, new Vector2(_facing * 0.3f, 1f));
        _target[JElbowL] = eL;
        _target[JHandL] = hL;
        _target[JElbowR] = eR;
        _target[JHandR] = hR;
    }

    // ------------------------------------------------------------------ pose assembly

    private float StandPelvisY => _world.Floor - FootRadius - (Thigh + Shin - 0.03f);

    private void Assemble(in Rig r, Vector2[] pose)
    {
        float floorFoot = _world.Floor - FootRadius;
        var pelvis = new Vector2(r.RootX + r.HipShift, StandPelvisY + r.Dip - r.Jump);
        var neck = pelvis + new Vector2(MathF.Sin(r.Lean) * TorsoLen, -MathF.Cos(r.Lean) * TorsoLen);
        float ha = r.Lean + r.HeadTilt;
        var head = neck + new Vector2(MathF.Sin(ha) * NeckLen, -MathF.Cos(ha) * NeckLen + r.Nod);

        var footL = new Vector2(r.FootLx, floorFoot - r.FootLLift - r.Jump);
        var footR = new Vector2(r.FootRx, floorFoot - r.FootRLift - r.Jump);
        var (kneeL, fL) = IK(pelvis, footL, Thigh, Shin, new Vector2(-1f, -0.1f));
        var (kneeR, fR) = IK(pelvis, footR, Thigh, Shin, new Vector2(1f, -0.1f));

        float up = r.ElbowUp;
        var (elbowL, hL) = IK(neck, neck + r.HandL, UpperArm, Forearm, new Vector2(-1f, 0.5f - 1.3f * up));
        var (elbowR, hR) = IK(neck, neck + r.HandR, UpperArm, Forearm, new Vector2(1f, 0.5f - 1.3f * up));

        pose[JHead] = head;
        pose[JNeck] = neck;
        pose[JPelvis] = pelvis;
        pose[JElbowL] = elbowL;
        pose[JHandL] = hL;
        pose[JElbowR] = elbowR;
        pose[JHandR] = hR;
        pose[JKneeL] = kneeL;
        pose[JFootL] = fL;
        pose[JKneeR] = kneeR;
        pose[JFootR] = fR;
    }

    private void StandPose(Vector2[] pose, float x, float lean)
    {
        var r = BaseRig(x);
        r.Lean = lean;
        Assemble(r, pose);
    }

    private void CrouchPose(Vector2[] pose, float x, float facing)
    {
        var r = BaseRig(x);
        r.Dip = 0.36f;
        r.Lean = 0.45f * facing;
        r.HeadTilt = -0.2f * facing;
        r.FootLx = x - 0.22f;
        r.FootRx = x + 0.22f;
        r.HandL = new Vector2(-0.14f + facing * 0.05f, 0.42f);
        r.HandR = new Vector2(0.14f + facing * 0.05f, 0.42f);
        Assemble(r, pose);
    }

    /// <summary>Two-bone IK: returns the middle joint (elbow/knee) and the reachable end point.
    /// Of the two possible bends, picks the one whose middle joint points most along <paramref name="pref"/>.</summary>
    private static (Vector2 mid, Vector2 end) IK(Vector2 a, Vector2 t, float l1, float l2, Vector2 pref)
    {
        var d = t - a;
        float dist = d.Length();
        var dir = dist > 1e-4f ? d / dist : new Vector2(0f, 1f);
        dist = Math.Clamp(dist, MathF.Abs(l1 - l2) + 0.03f, l1 + l2 - 0.002f);
        var end = a + dir * dist;
        float x = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
        float h = MathF.Sqrt(MathF.Max(0f, l1 * l1 - x * x));
        var perp = new Vector2(-dir.Y, dir.X);
        var m1 = a + dir * x + perp * h;
        var m2 = a + dir * x - perp * h;
        return (Vector2.Dot(m1 - a, pref) >= Vector2.Dot(m2 - a, pref) ? m1 : m2, end);
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    // ------------------------------------------------------------------ drawing

    public void Draw(SKCanvas canvas, SKCanvas? glow, SKColor color, float time)
    {
        var outline = new SKColor((byte)(color.Red * 0.18f), (byte)(color.Green * 0.18f), (byte)(color.Blue * 0.18f), 230);

        if (_holdingBroom && State == DancerState.Tidying) DrawBroom(canvas);

        // Body as one path (limbs + neck), outlined so it reads on light and dark rooms.
        var head = _p[JHead].Pos;
        var neck = _p[JNeck].Pos;
        var toHead = head - neck;
        float hl = toHead.Length();
        var headBase = hl > 1e-4f ? head - toHead / hl * HeadRadius : neck;
        _path.Reset();
        Seg(neck, headBase);
        Seg(neck, _p[JPelvis].Pos);
        Seg(neck, _p[JElbowL].Pos); _path.LineTo(P(_p[JHandL].Pos));
        Seg(neck, _p[JElbowR].Pos); _path.LineTo(P(_p[JHandR].Pos));
        Seg(_p[JPelvis].Pos, _p[JKneeL].Pos); _path.LineTo(P(_p[JFootL].Pos));
        Seg(_p[JPelvis].Pos, _p[JKneeR].Pos); _path.LineTo(P(_p[JFootR].Pos));

        if (glow != null)
        {
            _stroke.Color = color.WithAlpha(140);
            _stroke.StrokeWidth = 0.16f;
            glow.DrawPath(_path, _stroke);
            _fill.Color = color.WithAlpha(120);
            glow.DrawCircle(head.X, head.Y, HeadRadius * 1.5f, _fill);
        }

        _stroke.Color = outline;
        _stroke.StrokeWidth = 0.085f;
        canvas.DrawPath(_path, _stroke);
        _stroke.Color = color;
        _stroke.StrokeWidth = 0.052f;
        canvas.DrawPath(_path, _stroke);

        // Head
        _fill.Color = outline;
        canvas.DrawCircle(head.X, head.Y, HeadRadius + 0.016f, _fill);
        _fill.Color = color;
        canvas.DrawCircle(head.X, head.Y, HeadRadius, _fill);
        DrawFace(canvas, head, toHead, time);
    }

    private void Seg(Vector2 a, Vector2 b)
    {
        _path.MoveTo(P(a));
        _path.LineTo(P(b));
    }

    private static SKPoint P(Vector2 v) => new(v.X, v.Y);

    private void DrawFace(SKCanvas canvas, Vector2 head, Vector2 toHead, float time)
    {
        float angle = toHead.LengthSquared() > 1e-6f ? MathF.Atan2(toHead.Y, toHead.X) + MathF.PI / 2f : 0f;
        canvas.Save();
        canvas.Translate(head.X, head.Y);
        canvas.RotateRadians(angle);
        canvas.Translate(_facing * 0.025f, 0f); // look the way it's facing

        var ink = new SKColor(0x1A, 0x14, 0x1E);
        _fill.Color = ink;
        _stroke.Color = ink;
        _stroke.StrokeWidth = 0.018f;

        bool falling = State == DancerState.Down && _sinceKnock < 0.9f;
        if (Dizzy && !falling)
        {
            // Spiral-ish dizzy eyes and a wobbly mouth.
            for (int side = -1; side <= 1; side += 2)
            {
                float ex = side * 0.045f, ey = -0.025f;
                _path.Reset();
                for (int k = 0; k <= 14; k++)
                {
                    float a = time * 9f * side + k * 0.55f;
                    float rr = 0.004f + k * 0.0021f;
                    var pt = new SKPoint(ex + MathF.Cos(a) * rr, ey + MathF.Sin(a) * rr);
                    if (k == 0) _path.MoveTo(pt); else _path.LineTo(pt);
                }
                _stroke.StrokeWidth = 0.011f;
                canvas.DrawPath(_path, _stroke);
            }
            _path.Reset();
            _path.MoveTo(-0.05f, 0.05f);
            _path.QuadTo(-0.025f, 0.025f, 0f, 0.05f);
            _path.QuadTo(0.025f, 0.075f, 0.05f, 0.05f);
            _stroke.StrokeWidth = 0.014f;
            canvas.DrawPath(_path, _stroke);
        }
        else
        {
            // Eyes (blink every few seconds)
            bool blink = (time % 3.7f) < 0.12f;
            for (int side = -1; side <= 1; side += 2)
            {
                if (blink) canvas.DrawLine(side * 0.045f - 0.014f, -0.025f, side * 0.045f + 0.014f, -0.025f, _stroke);
                else canvas.DrawOval(side * 0.045f, -0.025f, falling ? 0.019f : 0.014f, falling ? 0.021f : 0.019f, _fill);
            }
            if (falling)
            {
                // "O" mouth
                _stroke.StrokeWidth = 0.015f;
                canvas.DrawOval(0f, 0.058f, 0.024f, 0.032f, _stroke);
            }
            else if (State == DancerState.Tidying)
            {
                _stroke.StrokeWidth = 0.014f;
                canvas.DrawLine(-0.03f, 0.055f, 0.03f, 0.05f, _stroke); // unimpressed
            }
            else if (_energy > 0.6f && _music > 0.4f)
            {
                // Big open grin
                _path.Reset();
                _path.MoveTo(-0.055f, 0.035f);
                _path.QuadTo(0f, 0.115f, 0.055f, 0.035f);
                _path.Close();
                canvas.DrawPath(_path, _fill);
            }
            else
            {
                _path.Reset();
                _path.MoveTo(-0.048f, 0.04f);
                _path.QuadTo(0f, 0.085f, 0.048f, 0.04f);
                _stroke.StrokeWidth = 0.015f;
                canvas.DrawPath(_path, _stroke);
            }
        }
        canvas.Restore();
    }

    private void DrawBroom(SKCanvas canvas)
    {
        var upper = _p[JHandL].Pos;
        var lower = _p[JHandR].Pos;
        var d = lower - upper;
        if (d.LengthSquared() < 1e-4f) d = new Vector2(_facing * 0.4f, 1f);
        d = Vector2.Normalize(d);
        var top = upper - d * 0.18f;
        var bottom = upper + d * (BroomLength - 0.18f);
        float limit = _world.Floor - 0.01f - MathF.Max(0f, d.Y) * 0.2f;
        if (bottom.Y > limit)
        {
            // Keep the bristles on the floor rather than through it.
            float over = bottom.Y - limit;
            bottom -= d * (over / MathF.Max(0.2f, d.Y));
        }

        _stroke.Color = new SKColor(0x6B, 0x45, 0x22);
        _stroke.StrokeWidth = 0.032f;
        canvas.DrawLine(top.X, top.Y, bottom.X, bottom.Y, _stroke);

        // Bristles: a little fan at the end
        var side = new Vector2(-d.Y, d.X);
        _path.Reset();
        _path.MoveTo(P(bottom - side * 0.05f));
        _path.LineTo(P(bottom + side * 0.05f));
        _path.LineTo(P(bottom + d * 0.2f + side * 0.13f));
        _path.LineTo(P(bottom + d * 0.2f - side * 0.13f));
        _path.Close();
        _fill.Color = new SKColor(0xD9, 0xB0, 0x5C);
        canvas.DrawPath(_path, _fill);
        _stroke.Color = new SKColor(0x8A, 0x62, 0x2A);
        _stroke.StrokeWidth = 0.008f;
        for (int k = -2; k <= 2; k++)
        {
            var a = bottom + side * (k * 0.02f);
            var b = bottom + d * 0.2f + side * (k * 0.05f);
            canvas.DrawLine(a.X, a.Y, b.X, b.Y, _stroke);
        }
    }
}
