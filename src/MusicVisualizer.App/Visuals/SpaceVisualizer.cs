using MusicVisualizer.Audio;
using MusicVisualizer.Visuals.Space;
using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// A hero ship flies through deep space. In calm music it cruises: parallax stars stream past faster as the
/// music gets louder and kick on every beat, planets drift by in front of a palette-tinted nebula, the ship
/// swoops around the screen, lunges forward on beats and barrel-rolls on big ones, and every few seconds
/// something happens (an asteroid field to weave through and shoot, a passing enemy scout, a wingman joining
/// for a while, a comet); drops kick in a burst of warp speed. When the music stays loud and beat-heavy, enemy fighters warp in and
/// a dogfight breaks out — the hero fires on the beats, hi-hats trade quick shots, strong beats land the killing
/// blows (explosions, debris, a little screen shake) and new waves keep coming until the music calms down and
/// the survivors retreat.
/// </summary>
public sealed class SpaceVisualizer : IVisualizer
{
    // ---- mode switching (see Direct)
    private const float EnterDogfight = 0.66f;   // smoothed energy needed…
    private const float EnterHold = 1.0f;        // …for this long
    private const float ExitDogfight = 0.54f;    // and below this…
    private const float ExitHold = 2.0f;         // …for this long to calm down again
    private const float MinModeTime = 4f;
    private const float IdleSkirmishAfter = 45f; // cruising this long to music with a beat starts a small skirmish (+0..15 s)
    private const float SkirmishMaxTime = 15f;

    private const int MaxEnemies = 6;
    private const int StarLayers = 3;
    private static readonly int[] StarCounts = [150, 90, 46];
    private static readonly float[] StarSpeeds = [18f, 50f, 125f];    // DIP/s at speed factor 1
    private static readonly float[] StarSizes = [0.9f, 1.4f, 2.1f];
    private static readonly byte[] StarAlpha = [120, 175, 235];

    private enum Mode { Cruise, Dogfight }
    private enum EnemyState { Off, WarpIn, Fight, Retreat, Scout }

    private sealed class Enemy
    {
        public EnemyState State;
        public EnemyKind Kind;
        public float X, Y, VX, VY, U, V, Phase, T, Hp, MaxHp, Hit, Charge, Facing = -1, Size, SmokeT, LowFor, Angle;
        public int Gun;
        public bool Fired;
    }

    private readonly Random _rng = new(2077);
    private readonly SpaceShips _ships = new();
    private readonly SpaceBackdrop _backdrop = new();
    private readonly FxPool _fx = new(520);
    private readonly BoltPool _bolts = new(96);
    private readonly Enemy[] _enemies = new Enemy[MaxEnemies];

    // Cache
    private (Palette? palette, int w, int h, bool overlay) _cacheKey;
    private SKColor _warm, _ember;

    // Music analysis
    private float _time, _energy, _beatRate, _levelSmooth, _levelSlow, _levelMed, _levelLong = 0.15f, _levelPeak = 0.1f, _trebleAvg, _beatAvg = 0.5f;
    private float _loudNorm, _starKick;
    private float _sinceBeat = 10f, _sinceHat = 10f;

    // Director
    private Mode _mode = Mode.Cruise;
    private float _modeTime, _overFor, _underFor, _combat;
    private float _warpT = 10f, _warpCd;
    private float _spawnCd, _enemyFireIn = -1f, _autoFire;
    private int _fireTurn;
    private bool _hatHero;
    private bool _skirmish;
    private int _skirmishQuota;
    private float _calmFor, _calmLimit = IdleSkirmishAfter;

    // Hero
    private float _heroBaseX = float.NaN, _heroX, _heroY, _heroVy, _heroAngle, _heroScaleY = 1f, _shield, _heroRecoil;
    private float _lunge, _lungeV, _flare, _rollT = 10f, _rollCd;
    private int _rollDir = 1;
    private int _target = -1;

    // Cruise events
    private readonly SpaceRocks _rocks = new(28);
    private Palette? _palette;
    private float _eventCd = 3f, _rockFieldLeft, _rockSpawnT;
    private float _cometT = -1f, _cometX, _cometY, _cometVX, _cometVY;
    private SKColor _cometColor;
    private float _wingT = -1f, _wingX, _wingY, _wingVy, _wingSide = 1f, _wingScaleY = 1f, _wingAngle;

    // Scenery
    private readonly float[][] _starX = new float[StarLayers][], _starY = new float[StarLayers][];
    private readonly SKPoint[][] _starPts = new SKPoint[StarLayers][];
    private float _speed = 1f;
    private float _bigX = float.NaN, _bigY, _smallX, _smallY;

    // Shake
    private float _shake, _shakeX, _shakeY;

    private readonly SKPaint _image = new() { IsAntialias = false, BlendMode = SKBlendMode.Src };
    private readonly SKPaint _planetPaint = new() { IsAntialias = true };
    private readonly SKPaint _starPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _streak = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

    public SpaceVisualizer()
    {
        for (int i = 0; i < MaxEnemies; i++) _enemies[i] = new Enemy();
        for (int l = 0; l < StarLayers; l++)
        {
            int n = StarCounts[l];
            _starX[l] = new float[n];
            _starY[l] = new float[n];
            _starPts[l] = new SKPoint[n * 2];
            for (int i = 0; i < n; i++)
            {
                _starX[l][i] = (float)_rng.NextDouble();
                _starY[l][i] = (float)_rng.NextDouble();
            }
        }
    }

    public string Name => "Space";
    public VisualizerCategory Category => VisualizerCategory.Scenes;

    // Deep space is opaque in a normal window, so engines, lasers and explosions bloom over the scene.
    public bool GlowAbove => true;

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        float w = ctx.Width, h = ctx.Height, s = ctx.Scale;
        float dt = Math.Clamp(ctx.Dt, 0.001f, 0.05f);
        bool strip = h < w * 0.35f;
        _time += dt;

        EnsureCache(ctx, (int)w, (int)h);

        // ---- layout
        float L = strip ? h * 0.36f : ctx.Overlay ? MathF.Min(w * 0.075f, h * 0.135f) : MathF.Min(w * 0.085f, h * 0.15f);
        float top = strip ? h * 0.22f : h * 0.18f, bottom = strip ? h * 0.78f : h * 0.82f;

        Analyze(frame, dt);
        Direct(frame, dt);
        _combat += ((_mode == Mode.Dogfight ? 1f : 0f) - _combat) * (1f - MathF.Exp(-dt / 0.9f));

        float warp = WarpEnvelope();
        // Star speed follows the song's loudness (relative to its recent peaks) and kicks on every beat.
        float speedTarget = 0.35f + 2.6f * MathF.Pow(_loudNorm, 1.5f) + 0.6f * _combat;
        _speed += (speedTarget - _speed) * (1f - MathF.Exp(-dt / 0.3f));
        _starKick *= MathF.Exp(-dt / 0.22f);
        float speedMul = _speed + 2.4f * _starKick + 14f * warp;

        UpdateStars(dt, w, h, s, speedMul, warp, ctx.Overlay, strip);
        UpdatePlanets(dt, w, h, s, speedMul);
        UpdateEvents(frame, dt, w, h, L, top, bottom, strip, s, speedMul);
        UpdateHero(frame, dt, w, h, L, top, bottom, strip, warp);
        UpdateWingman(dt, w, L, top, bottom, h);
        UpdateEnemies(frame, dt, w, h, L, top, bottom, strip, s);
        UpdateBolts(dt, w, h, L, s);
        _fx.Update(dt);

        _shield *= MathF.Exp(-dt / 0.15f);
        _shake *= MathF.Exp(-dt / 0.16f);
        _shakeX = (float)(_rng.NextDouble() * 2 - 1) * _shake;
        _shakeY = (float)(_rng.NextDouble() * 2 - 1) * _shake;

        Draw(ctx, frame, L, warp, strip);
    }

    // ------------------------------------------------------------------ music → director

    private void Analyze(AudioFrame frame, float dt)
    {
        _sinceBeat += dt;
        _sinceHat += dt;
        _levelSmooth += (frame.Level - _levelSmooth) * (1f - MathF.Exp(-dt / 0.15f));
        _levelSlow += (frame.Level - _levelSlow) * (1f - MathF.Exp(-dt / 5f));
        _levelMed += (frame.Level - _levelMed) * (1f - MathF.Exp(-dt / 0.6f));
        _levelLong += (_levelMed - _levelLong) * (1f - MathF.Exp(-dt / MathF.Min(20f, 2f + _time * 0.5f))); // settles fast at start-up
        // Peak follower (fast up, slow down): loudness as a fraction of how loud this song has been lately.
        _levelPeak += (_levelSmooth - _levelPeak) * (1f - MathF.Exp(-dt / (_levelSmooth > _levelPeak ? 0.3f : 6f)));
        _loudNorm = Math.Clamp(_levelSmooth / MathF.Max(_levelPeak, 0.06f), 0f, 1f);

        // Beats per second, as a leaky counter (≈ the true rate after a few seconds).
        _beatRate *= MathF.Exp(-dt / 3f);
        if (frame.Beat)
        {
            _beatRate += 1f / 3f;
            _sinceBeat = 0;
            _beatAvg += (frame.BeatStrength - _beatAvg) * 0.25f;
            _starKick = MathF.Max(_starKick, 0.4f + 0.6f * frame.BeatStrength);
        }

        // How hard the music is going, smoothed over a couple of seconds. Three parts:
        //  - rise: louder than this song's last ~20 s (choruses and drops after verses), whatever the volume;
        //  - level: absolute loudness, so loud wall-to-wall music fights too (real streamed music sits around
        //    Level 0.15-0.4; quiet passages below ~0.12);
        //  - beats: a steady beat. (Bass is auto-gained, so it reads high even in quiet songs; it is left out.)
        float rise = Math.Clamp((_levelMed / MathF.Max(_levelLong, 0.06f) - 1.05f) / 0.35f, 0f, 1f);
        float level = Smooth((_levelMed - 0.1f) / 0.22f);
        float raw = 0.3f * rise + 0.45f * level + 0.25f * Math.Clamp(_beatRate / 2f, 0f, 1f);
        _energy += (raw - _energy) * (1f - MathF.Exp(-dt / 1.4f));

        // Drops: a strong beat clearly louder than the last few seconds kicks in warp speed.
        _warpCd -= dt;
        _warpT += dt;
        if (frame.Beat && frame.BeatStrength > 0.45f && _levelSmooth > 1.35f * _levelSlow && _levelSmooth > 0.05f && _warpCd <= 0)
            StartWarp();
    }

    private bool HatOnset(AudioFrame frame)
    {
        bool hat = frame.Treble > 0.12f && frame.Treble - _trebleAvg > 0.035f && _sinceHat > 0.11f;
        if (hat) _sinceHat = 0;
        return hat;
    }

    private void StartWarp()
    {
        _warpT = 0;
        _warpCd = 7f;
    }

    /// <summary>0..1: quick rise, short hold, slower fall.</summary>
    private float WarpEnvelope()
    {
        float t = _warpT;
        if (t < 0.18f) return Smooth(t / 0.18f);
        if (t < 0.85f) return 1f;
        if (t < 1.7f) return 1f - Smooth((t - 0.85f) / 0.85f);
        return 0f;
    }

    private static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3 - 2 * x); }

    private void Direct(AudioFrame frame, float dt)
    {
        _modeTime += dt;
        if (_mode == Mode.Cruise)
        {
            _overFor = _energy > EnterDogfight ? _overFor + dt : 0f;
            // Never idle too long: after a long cruise through music with a beat, a small skirmish breaks out.
            if (_beatRate > 0.6f && _levelMed > 0.04f) _calmFor += dt;
            if (_overFor > EnterHold && _modeTime > MinModeTime) StartDogfight(skirmish: false);
            else if (_calmFor > _calmLimit && _modeTime > MinModeTime) StartDogfight(skirmish: true);
        }
        else if (_skirmish)
        {
            // A skirmish is one or two enemies; it ends when they're gone (or after a while) unless the music
            // gets intense enough to turn it into a real fight.
            _overFor = _energy > EnterDogfight ? _overFor + dt : 0f;
            if (_overFor > EnterHold) _skirmish = false;
            else if ((_skirmishQuota == 0 && AliveCount() == 0 && _modeTime > 2f) || _modeTime > SkirmishMaxTime) EndDogfight();
        }
        else
        {
            _underFor = _energy < ExitDogfight ? _underFor + dt : 0f;
            if (_underFor > ExitHold && _modeTime > MinModeTime) EndDogfight();
        }
    }

    private void StartDogfight(bool skirmish)
    {
        _mode = Mode.Dogfight;
        _modeTime = 0;
        _underFor = _overFor = 0;
        _spawnCd = 0.25f;
        _skirmish = skirmish;
        _skirmishQuota = 2;
        _calmFor = 0;
        _calmLimit = IdleSkirmishAfter + 15f * (float)_rng.NextDouble();
        _rockFieldLeft = 0;
        if (_warpCd <= 0) StartWarp();
    }

    private void EndDogfight()
    {
        _mode = Mode.Cruise;
        _modeTime = 0;
        _overFor = 0;
        _skirmish = false;
        _calmFor = 0;
        _eventCd = 2.5f;
        foreach (var e in _enemies)
            if (e.State is EnemyState.Fight or EnemyState.WarpIn) { e.State = EnemyState.Retreat; e.T = 0; }
    }

    private int AliveCount()
    {
        int n = 0;
        foreach (var e in _enemies) if (e.State is EnemyState.WarpIn or EnemyState.Fight) n++;
        return n;
    }

    // ------------------------------------------------------------------ hero

    private void UpdateHero(AudioFrame frame, float dt, float w, float h, float L, float top, float bottom, bool strip, float warp)
    {
        // Cruise: swoop around a good part of the screen (slow Lissajous), weave with the mids, steer around rocks.
        float xMin = strip ? w * 0.06f : w * 0.12f, xMax = strip ? w * 0.42f : w * 0.46f, fightX = strip ? w * 0.09f : w * 0.19f;
        float midY = (top + bottom) / 2, span = (bottom - top) / 2;
        float u = 0.5f + 0.32f * MathF.Sin(_time * 0.23f) + 0.18f * MathF.Sin(_time * 0.61f + 2f);
        float cruiseX = xMin + (xMax - xMin) * u;
        float cruiseY = midY + span * (0.55f * MathF.Sin(_time * 0.31f) + 0.25f * MathF.Sin(_time * 0.77f + 1f))
                        + span * 0.3f * frame.Mid * MathF.Sin(_time * 2.1f);
        cruiseY = AvoidRocks(cruiseY, L, w);

        // In a fight, line up with the current target, weaving so incoming fire mostly misses.
        float fightY = cruiseY;
        var target = TargetEnemy();
        if (target != null) fightY = target.Y + span * 0.22f * MathF.Sin(_time * 1.9f);
        float ty = Math.Clamp(cruiseY + (fightY - cruiseY) * _combat, top, bottom);
        float tx = cruiseX + (fightX - cruiseX) * _combat - warp * w * 0.015f;

        // Beats: a forward lunge with an engine flare. Strong beats: a barrel roll with a sideways juke.
        if (frame.Beat)
        {
            _lungeV += w * (0.2f + 0.35f * frame.BeatStrength);
            _flare = 1f;
        }
        _lungeV += (-_lunge * 30f - _lungeV * 9f) * dt;
        _lunge += _lungeV * dt;
        _flare *= MathF.Exp(-dt / 0.25f);
        _rollCd -= dt;
        _rollT += dt;
        if (frame.Beat && frame.BeatStrength >= MathF.Max(0.6f, _beatAvg * 1.1f) && _rollCd <= 0 && _rollT > 0.8f)
        {
            _rollT = 0;
            _rollDir = _heroY > midY ? -1 : 1;
            _rollCd = _mode == Mode.Cruise ? 2.5f : 4f;
            _heroVy += _rollDir * h * (strip ? 1.4f : 1.1f);
        }

        if (float.IsNaN(_heroBaseX)) { _heroBaseX = tx; _heroY = ty; }
        float k = 26f, damp = 8.5f;
        _heroVy += ((ty - _heroY) * k - _heroVy * damp) * dt;
        _heroY += _heroVy * dt;
        _heroBaseX += (tx - _heroBaseX) * (1f - MathF.Exp(-dt / 0.5f));
        _heroX = _heroBaseX + _lunge;
        float bank = Math.Clamp(_heroVy / (h * 0.45f), -1f, 1f);
        _heroAngle = bank * 0.2f;
        const float RollTime = 0.7f;
        _heroScaleY = _rollT < RollTime ? MathF.Cos(MathF.Tau * Smooth(_rollT / RollTime)) : 1f - 0.32f * MathF.Abs(bank);
        _heroRecoil *= MathF.Exp(-dt / 0.08f);
    }

    /// <summary>Moves a target height out of the way of rocks just ahead of the hero.</summary>
    private float AvoidRocks(float y, float L, float w)
    {
        for (int i = 0; i < _rocks.Count; i++)
        {
            ref var r = ref _rocks[i];
            if (r.X < _heroX - L * 0.3f || r.X > _heroX + w * 0.3f) continue;
            float clear = r.R + L * 0.55f, dy = y - r.Y;
            if (MathF.Abs(dy) < clear) y = r.Y + (dy >= 0 ? clear : -clear);
        }
        return y;
    }

    private Enemy? TargetEnemy()
    {
        if (_target >= 0 && _enemies[_target].State == EnemyState.Fight) return _enemies[_target];
        _target = -1;
        float best = float.MaxValue;
        for (int i = 0; i < MaxEnemies; i++)
        {
            var e = _enemies[i];
            if (e.State != EnemyState.Fight) continue;
            float d = MathF.Abs(e.Y - _heroY) + e.X * 0.2f;
            if (d < best) { best = d; _target = i; }
        }
        return _target >= 0 ? _enemies[_target] : null;
    }

    private SKPoint HeroPoint(SKPoint local, float L)
    {
        float lx = local.X * L, ly = local.Y * L * _heroScaleY;
        float c = MathF.Cos(_heroAngle), sn = MathF.Sin(_heroAngle);
        return new SKPoint(_heroX + lx * c - ly * sn, _heroY + lx * sn + ly * c);
    }

    private void HeroFire(float w, float L, float s, bool heavy, bool small, bool aim = false, float ax = 0, float ay = 0)
    {
        if (!aim && TargetEnemy() is { } t) { aim = true; ax = t.X; ay = t.Y; }
        float speed = 2.5f * w;
        float width = MathF.Max(3f * s, L * 0.042f) * (heavy ? 1.5f : small ? 0.6f : 1f);
        float len = L * (heavy ? 0.75f : small ? 0.3f : 0.55f);
        var col = heavy ? ColorUtil.Mix(_ships.HeroLaser, SKColors.White, 0.35f) : _ships.HeroLaser;
        float dmg = heavy ? 2f : small ? 0.35f : 1f;
        var guns = small ? SpaceShips.HeroWingTips : SpaceShips.HeroGuns;
        for (int gi = 0; gi < guns.Length; gi++)
        {
            if (small && gi != (_fireTurn & 1)) continue;
            var p = HeroPoint(guns[gi], L);
            float ang = _heroAngle;
            if (aim) ang = Math.Clamp(MathF.Atan2(ay - p.Y, ax - p.X), _heroAngle - 0.4f, _heroAngle + 0.4f);
            _bolts.Fire(p.X, p.Y, MathF.Cos(ang) * speed, MathF.Sin(ang) * speed, len, width, col, true, dmg, heavy);
            _fx.Spawn(FxKind.Flash, p.X, p.Y, 0, 0, 0.12f, L * (heavy ? 0.22f : 0.12f), col);
        }
        _heroRecoil = heavy ? 1f : 0.5f;
        _fireTurn++;
    }

    /// <summary>The wingman (if flying alongside) fires one bolt at the given point.</summary>
    private void WingFire(float w, float L, float s, float ax, float ay)
    {
        if (_wingT < 0.8f || _wingT > 13f) return;
        float WL = L * 0.72f;
        float x = _wingX + WL * 0.5f, y = _wingY;
        float ang = Math.Clamp(MathF.Atan2(ay - y, ax - x), -0.4f, 0.4f);
        float speed = 2.5f * w;
        _bolts.Fire(x, y, MathF.Cos(ang) * speed, MathF.Sin(ang) * speed, WL * 0.5f, MathF.Max(2.5f * s, WL * 0.04f), _ships.HeroLaser, true, 1f, false);
        _fx.Spawn(FxKind.Flash, x, y, 0, 0, 0.12f, WL * 0.12f, _ships.HeroLaser);
    }

    // ------------------------------------------------------------------ enemies

    private void UpdateEnemies(AudioFrame frame, float dt, float w, float h, float L, float top, float bottom, bool strip, float s)
    {
        bool fighting = _mode == Mode.Dogfight;
        int alive = AliveCount();

        // Waves: keep a few enemies around while the music stays intense; new ones arrive on beats.
        // A skirmish sends just two, one after the other.
        int want = _skirmish ? (_skirmishQuota > 0 ? 1 : 0)
                 : strip ? (_energy > 0.85f ? 3 : 2) : 2 + (_energy > 0.78f ? 1 : 0) + (_energy > 0.9f ? 1 : 0);
        _spawnCd -= dt;
        if (fighting && alive < want && _spawnCd <= 0 && (frame.Beat || _spawnCd < -1f))
        {
            Spawn(w, h, L, top, bottom, strip);
            _spawnCd = 0.6f + (float)_rng.NextDouble() * 0.9f;
            if (_skirmish)
            {
                // Skirmishers are a bit tougher and come one at a time, so it plays out over ~10-15 s.
                _skirmishQuota--;
                foreach (var e in _enemies) if (e.State == EnemyState.WarpIn && e.T == 0) e.MaxHp = e.Hp = e.Hp * 1.6f;
            }
        }

        bool hat = HatOnset(frame);
        _trebleAvg += (frame.Treble - _trebleAvg) * (1f - MathF.Exp(-dt / 0.25f));

        if (fighting)
        {
            // Beats: the hero fires (strong beats fire heavy bolts that can finish an enemy off), and an
            // enemy answers a moment later. Hi-hats trade quick small shots.
            bool heavy = frame.BeatStrength >= 0.75f || frame.BeatStrength >= _beatAvg * 1.05f;
            if (frame.Beat && TargetEnemy() is { } tgt)
            {
                HeroFire(w, L, s, heavy, false);
                if (_rng.NextDouble() < 0.5) WingFire(w, L, s, tgt.X, tgt.Y);
                _enemyFireIn = 0.16f;
                _autoFire = 0;
            }
            else if (_sinceBeat > 1.2f && TargetEnemy() != null && (_autoFire += dt) > 0.7f)
            {
                _autoFire = 0;
                HeroFire(w, L, s, false, false);
                _enemyFireIn = 0.25f;
            }
            if (hat && alive > 0)
            {
                _hatHero = !_hatHero;
                if (_hatHero && TargetEnemy() != null) HeroFire(w, L, s, false, true);
                else EnemyFire(w, L, s, small: true);
            }
            if (_enemyFireIn >= 0 && (_enemyFireIn -= dt) < 0) EnemyFire(w, L, s, small: false);

            if (frame.Beat)
                foreach (var e in _enemies)
                    if (e.State == EnemyState.Fight && _rng.NextDouble() < 0.35) e.V = PickV(e, top, bottom, h);
        }
        else if (frame.Beat && CruiseTarget(w, h, L, out float ax, out float ay))
        {
            // Cruising: pick off passing scouts and rocks in the way, on the beat.
            HeroFire(w, L, s, frame.BeatStrength > 0.7f, false, true, ax, ay);
            if (_rng.NextDouble() < 0.6) WingFire(w, L, s, ax, ay);
        }

        float weave = (strip ? h * 0.11f : h * 0.06f) * (0.6f + 0.9f * frame.Mid);
        foreach (var e in _enemies)
        {
            if (e.State == EnemyState.Off) continue;
            e.T += dt;
            e.Hit *= MathF.Exp(-dt / 0.09f);
            e.Charge *= MathF.Exp(-dt / 0.25f);
            float px = e.X, py = e.Y;
            switch (e.State)
            {
                case EnemyState.WarpIn:
                {
                    float k = Math.Clamp(e.T / 0.55f, 0f, 1f);
                    float ease = 1f - MathF.Pow(1f - k, 3f);
                    float startX = w + e.Size * 2f;
                    e.X = startX + (e.U * w - startX) * ease;
                    e.Y = e.V * h;
                    if (k >= 1f)
                    {
                        e.State = EnemyState.Fight;
                        e.T = 0;
                        _fx.Spawn(FxKind.Ring, e.X, e.Y, 0, 0, 0.4f, e.Size * 0.9f, ColorUtil.Mix(_ships.EnemyColor, SKColors.White, 0.4f));
                    }
                    break;
                }
                case EnemyState.Fight:
                {
                    float tx = e.U * w + w * 0.035f * MathF.Sin(e.T * 0.6f + e.Phase);
                    float ty = Math.Clamp(e.V * h + weave * MathF.Sin(e.T * (1.1f + 0.25f * (int)e.Kind) + e.Phase * 1.7f), top, bottom);
                    float k = 1f - MathF.Exp(-dt / (e.Kind == EnemyKind.Claw ? 0.3f : 0.45f));
                    e.X += (tx - e.X) * k;
                    e.Y += (ty - e.Y) * k;
                    // Wander the anchor now and then.
                    if (_rng.NextDouble() < dt * 0.25) e.U = PickU(strip);
                    if (e.Hp <= e.MaxHp * 0.34f)
                    {
                        e.LowFor += dt;
                        if ((e.SmokeT -= dt) <= 0)
                        {
                            e.SmokeT = 0.1f;
                            float ox = (float)(_rng.NextDouble() - 0.5) * e.Size * 0.3f, oy = (float)(_rng.NextDouble() - 0.5) * e.Size * 0.3f;
                            _fx.Spawn(FxKind.Smoke, e.X + ox, e.Y + oy, w * 0.12f, (float)(_rng.NextDouble() - 0.5) * 20f * s, 0.75f,
                                      e.Size * 0.12f, new SKColor(40, 40, 48, 110), drag: 1f);
                            if (_rng.NextDouble() < 0.4)
                                _fx.Spawn(FxKind.Spark, e.X + ox, e.Y + oy, w * 0.15f + (float)(_rng.NextDouble() - 0.5) * 200 * s,
                                          (float)(_rng.NextDouble() - 0.5) * 300 * s, 0.3f, 1.6f * s, new SKColor(255, 190, 90), drag: 2f);
                        }
                    }
                    break;
                }
                case EnemyState.Scout:
                {
                    // Zips across from right to left, takes one pot shot at the hero, and is gone.
                    e.X += e.VX * dt;
                    e.Y = Math.Clamp(e.V * h + weave * 1.6f * MathF.Sin(e.T * 2.2f + e.Phase), top, bottom);
                    if (!e.Fired && e.X < w * 0.72f && e.X > _heroX + L)
                    {
                        e.Fired = true;
                        EnemyShoot(e, w, L, s, small: false, hitChance: 0.35f);
                    }
                    if (e.X < -e.Size * 3f) e.State = EnemyState.Off;
                    break;
                }
                case EnemyState.Retreat:
                {
                    // Turn around, then boost away to the right.
                    e.Facing = -MathF.Cos(MathF.PI * Math.Clamp(e.T / 0.45f, 0f, 1f));
                    if (e.T > 0.3f) e.VX += w * 2.8f * dt;
                    e.X += e.VX * dt;
                    e.Y += (e.V * h - e.Y) * (1f - MathF.Exp(-dt / 0.6f));
                    if (e.X > w + e.Size * 3f) e.State = EnemyState.Off;
                    break;
                }
            }
            if (e.State is not (EnemyState.Retreat or EnemyState.Scout))
            {
                e.VX = (e.X - px) / dt;
            }
            e.VY = (e.Y - py) / dt;
            float bank = Math.Clamp(e.VY / (h * 0.9f), -0.45f, 0.45f);
            e.Angle += (e.Facing * bank - e.Angle) * (1f - MathF.Exp(-dt / 0.1f));
        }
    }

    /// <summary>Something worth shooting while cruising: a scout ahead, else (sometimes) a rock in the way.</summary>
    private bool CruiseTarget(float w, float h, float L, out float ax, out float ay)
    {
        ax = ay = 0;
        foreach (var e in _enemies)
            if (e.State == EnemyState.Scout && e.X > _heroX + L && e.X < w) { ax = e.X; ay = e.Y; return true; }
        float best = float.MaxValue;
        for (int i = 0; i < _rocks.Count; i++)
        {
            ref var r = ref _rocks[i];
            float dx = r.X - _heroX;
            if (dx < L * 0.6f || dx > w * 0.55f || MathF.Abs(r.Y - _heroY) > h * 0.22f) continue;
            if (dx < best) { best = dx; ax = r.X; ay = r.Y; }
        }
        return best < float.MaxValue && _rng.NextDouble() < 0.65;
    }

    private void SpawnScout(float w, float h, float L, float top, float bottom)
    {
        Enemy? e = null;
        foreach (var c in _enemies) if (c.State == EnemyState.Off) { e = c; break; }
        if (e == null) return;
        e.Kind = _rng.NextDouble() < 0.5 ? EnemyKind.Manta : EnemyKind.Claw;
        e.Size = L * 0.7f;
        e.MaxHp = e.Hp = 2f;
        e.V = (top + (bottom - top) * (float)_rng.NextDouble()) / h;
        e.X = w + e.Size * 2f;
        e.Y = e.V * h;
        e.VX = -(w + e.Size * 5f) / 3.2f;
        e.VY = 0;
        e.Phase = (float)(_rng.NextDouble() * MathF.Tau);
        e.T = e.Hit = e.Charge = e.Angle = e.LowFor = 0;
        e.Facing = -1;
        e.Fired = false;
        e.State = EnemyState.Scout;
    }

    private float PickU(bool strip) => strip ? 0.45f + 0.47f * (float)_rng.NextDouble() : 0.56f + 0.32f * (float)_rng.NextDouble();

    private float PickV(Enemy self, float top, float bottom, float h)
    {
        // The candidate farthest from the other enemies, so they spread out.
        float best = 0.5f, bestScore = -1;
        for (int tries = 0; tries < 6; tries++)
        {
            float v = (top + (bottom - top) * (float)_rng.NextDouble()) / h;
            float score = 10f;
            foreach (var o in _enemies)
                if (o != self && o.State is EnemyState.Fight or EnemyState.WarpIn) score = MathF.Min(score, MathF.Abs(o.V - v));
            if (score > bestScore) { bestScore = score; best = v; }
        }
        return best;
    }

    private void Spawn(float w, float h, float L, float top, float bottom, bool strip)
    {
        Enemy? e = null;
        foreach (var c in _enemies) if (c.State == EnemyState.Off) { e = c; break; }
        if (e == null) return;
        e.Kind = _rng.NextDouble() < 0.35 ? EnemyKind.Manta : EnemyKind.Claw;
        e.Size = L * (e.Kind == EnemyKind.Claw ? 0.78f : 0.98f);
        e.MaxHp = e.Hp = e.Kind == EnemyKind.Claw ? 3f : 5f;
        e.U = PickU(strip);
        e.V = PickV(e, top, bottom, h);
        e.X = w + e.Size * 2f;
        e.Y = e.V * h;
        e.VX = e.VY = 0;
        e.Phase = (float)(_rng.NextDouble() * MathF.Tau);
        e.T = 0;
        e.Hit = 0;
        e.Charge = 0;
        e.Facing = -1;
        e.Angle = 0;
        e.LowFor = 0;
        e.State = EnemyState.WarpIn;
    }

    private void EnemyFire(float w, float L, float s, bool small)
    {
        // Round-robin among fighting enemies.
        Enemy? shooter = null;
        for (int k = 0; k < MaxEnemies; k++)
        {
            var e = _enemies[(_fireTurn + k) % MaxEnemies];
            if (e.State == EnemyState.Fight && e.X < w * 0.98f) { shooter = e; break; }
        }
        _fireTurn++;
        if (shooter == null) return;
        EnemyShoot(shooter, w, L, s, small, small ? 0f : 0.18f);
    }

    private void EnemyShoot(Enemy e2, float w, float L, float s, bool small, float hitChance)
    {
        var guns = SpaceShips.GunsOf(e2.Kind);
        var g = guns[e2.Gun++ % guns.Length];
        float c = MathF.Cos(e2.Angle), sn = MathF.Sin(e2.Angle);
        float lx = g.X * e2.Size * e2.Facing, ly = g.Y * e2.Size;
        float x = e2.X + lx * c - ly * sn, y = e2.Y + lx * sn + ly * c;

        // Mostly near-misses past the hero; now and then one splashes on the shield.
        float miss = _rng.NextDouble() < hitChance ? 0f : (_rng.NextDouble() < 0.5 ? -1 : 1) * L * (0.35f + 0.4f * (float)_rng.NextDouble());
        float ang = MathF.Atan2(_heroY + miss - y, _heroX - x);
        float speed = 1.35f * w;
        float width = MathF.Max(2.8f * s, L * 0.038f) * (small ? 0.6f : 1f);
        _bolts.Fire(x, y, MathF.Cos(ang) * speed, MathF.Sin(ang) * speed, L * (small ? 0.28f : 0.45f), width, _ships.EnemyColor, false, 0, false);
        e2.Charge = 1f;
        _fx.Spawn(FxKind.Flash, x, y, 0, 0, 0.12f, L * (small ? 0.1f : 0.16f), _ships.EnemyColor);
    }

    // ------------------------------------------------------------------ bolts & hits

    private void UpdateBolts(float dt, float w, float h, float L, float s)
    {
        _bolts.Update(dt, w, h);
        for (int i = 0; i < _bolts.Count; i++)
        {
            ref var b = ref _bolts[i];
            if (b.FromHero)
            {
                bool hit = false;
                for (int k = 0; k < MaxEnemies && !hit; k++)
                {
                    var e = _enemies[k];
                    if (e.State is not (EnemyState.Fight or EnemyState.Scout)) continue;
                    float r = e.Size * 0.36f;
                    if (SegDist2(b.PX, b.PY, b.X, b.Y, e.X, e.Y) > r * r) continue;
                    HitEnemy(e, ref b, w, h, s);
                    hit = true;
                }
                for (int k = 0; k < _rocks.Count && !hit; k++)
                {
                    ref var r = ref _rocks[k];
                    if (SegDist2(b.PX, b.PY, b.X, b.Y, r.X, r.Y) > r.R * r.R * 0.8f) continue;
                    r.Hit = 1f;
                    r.Hp -= L * 0.3f * b.Damage;
                    for (int j = 0; j < 5; j++)
                    {
                        float a = MathF.Atan2(-b.VY, -b.VX) + (float)(_rng.NextDouble() - 0.5) * 2.4f;
                        float sp = (200 + 400 * (float)_rng.NextDouble()) * s;
                        _fx.Spawn(FxKind.Spark, b.X, b.Y, MathF.Cos(a) * sp, MathF.Sin(a) * sp, 0.25f, 1.5f * s, ColorUtil.Mix(b.Color, SKColors.White, 0.5f), drag: 3f);
                    }
                    if (r.Hp <= 0) BreakRock(k, L, s, w, h);
                    hit = true;
                }
                if (hit)
                {
                    _bolts.RemoveAt(i);
                    i--;
                }
            }
            else
            {
                float r = L * 0.36f;
                if (SegDist2(b.PX, b.PY, b.X, b.Y, _heroX, _heroY) <= r * r)
                {
                    _shield = 1f;
                    float nx = b.X - _heroX, ny = b.Y - _heroY;
                    float nl = MathF.Max(1f, MathF.Sqrt(nx * nx + ny * ny));
                    var col = ColorUtil.Mix(_ships.HeroLaser, SKColors.White, 0.4f);
                    for (int j = 0; j < 7; j++)
                    {
                        float a = MathF.Atan2(ny, nx) + (float)(_rng.NextDouble() - 0.5) * 2f;
                        float sp = (200 + 400 * (float)_rng.NextDouble()) * s;
                        _fx.Spawn(FxKind.Spark, _heroX + nx / nl * r, _heroY + ny / nl * r, MathF.Cos(a) * sp, MathF.Sin(a) * sp, 0.3f, 1.8f * s, col, drag: 3f);
                    }
                    _bolts.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    private void HitEnemy(Enemy e, ref Bolt b, float w, float h, float s)
    {
        e.Hit = 1f;
        e.Hp -= b.Damage;
        // Sparks fly back along the bolt.
        float sp0 = MathF.Sqrt(b.VX * b.VX + b.VY * b.VY);
        float bx = -b.VX / sp0, by = -b.VY / sp0;
        int n = b.Heavy ? 12 : 6;
        for (int j = 0; j < n; j++)
        {
            float a = MathF.Atan2(by, bx) + (float)(_rng.NextDouble() - 0.5) * 2.4f;
            float sp = (250 + 550 * (float)_rng.NextDouble()) * s;
            _fx.Spawn(FxKind.Spark, b.X, b.Y, MathF.Cos(a) * sp, MathF.Sin(a) * sp, 0.25f + 0.2f * (float)_rng.NextDouble(),
                      (1.4f + (float)_rng.NextDouble()) * s, ColorUtil.Mix(b.Color, SKColors.White, 0.5f), drag: 3f);
        }
        _fx.Spawn(FxKind.Flash, b.X, b.Y, 0, 0, 0.15f, e.Size * (b.Heavy ? 0.5f : 0.3f), b.Color);

        if (e.Hp <= 0)
        {
            // The killing blow lands on a strong beat (or if it has limped on for too long).
            if (b.Heavy || e.LowFor > 4f || e.State == EnemyState.Scout) Explode(e, b.Heavy ? 1f : 0.6f, w, h, s);
            else e.Hp = 0.01f;
        }
    }

    private void Explode(Enemy e, float strength, float w, float h, float s)
    {
        e.State = EnemyState.Off;
        _spawnCd = MathF.Max(_spawnCd, (_skirmish ? 2.5f : 0.9f) + 0.6f * (float)_rng.NextDouble());
        float sz = e.Size;
        var warm = _warm;
        var ember = _ember;
        float drift = -w * 0.05f; // debris falls behind as we fly on

        _fx.Spawn(FxKind.Flash, e.X, e.Y, 0, 0, 0.3f, sz * (0.7f + 0.3f * strength), ColorUtil.Mix(warm, SKColors.White, 0.3f));
        _fx.Spawn(FxKind.Ring, e.X, e.Y, drift, 0, 0.5f, sz * (1.0f + 0.4f * strength), ColorUtil.Mix(_ships.EnemyColor, SKColors.White, 0.35f));
        // White-hot core, then the fireball.
        _fx.Spawn(FxKind.Fire, e.X, e.Y, drift, 0, 0.3f, sz * 0.55f, warm, ember);
        for (int i = 0; i < 20; i++)
        {
            float a = (float)(_rng.NextDouble() * MathF.Tau);
            float sp = sz * (0.3f + 2.2f * (float)_rng.NextDouble());
            _fx.Spawn(FxKind.Fire, e.X + MathF.Cos(a) * sz * 0.1f, e.Y + MathF.Sin(a) * sz * 0.1f, MathF.Cos(a) * sp + drift, MathF.Sin(a) * sp,
                      0.45f + 0.55f * (float)_rng.NextDouble(), sz * (0.14f + 0.2f * (float)_rng.NextDouble()), warm, ember, drag: 2.2f);
        }
        for (int i = 0; i < 26; i++)
        {
            float a = (float)(_rng.NextDouble() * MathF.Tau);
            float sp = sz * (2f + 4.5f * (float)_rng.NextDouble());
            _fx.Spawn(FxKind.Spark, e.X, e.Y, MathF.Cos(a) * sp + drift, MathF.Sin(a) * sp, 0.3f + 0.45f * (float)_rng.NextDouble(),
                      (1.3f + 1.5f * (float)_rng.NextDouble()) * s, ColorUtil.Mix(warm, SKColors.White, 0.4f), drag: 1.8f);
        }
        for (int i = 0; i < 9; i++)
        {
            float a = (float)(_rng.NextDouble() * MathF.Tau);
            float sp = sz * (0.6f + 2.2f * (float)_rng.NextDouble());
            _fx.Spawn(FxKind.Debris, e.X, e.Y, MathF.Cos(a) * sp + drift, MathF.Sin(a) * sp, 0.9f + 0.8f * (float)_rng.NextDouble(),
                      sz * (0.07f + 0.1f * (float)_rng.NextDouble()), _ships.EnemyHull, warm, drag: 0.6f,
                      spin: (float)(_rng.NextDouble() - 0.5) * 14f, rot: a);
        }
        for (int i = 0; i < 6; i++)
        {
            float a = (float)(_rng.NextDouble() * MathF.Tau);
            float sp = sz * (0.2f + 0.6f * (float)_rng.NextDouble());
            _fx.Spawn(FxKind.Smoke, e.X, e.Y, MathF.Cos(a) * sp + drift, MathF.Sin(a) * sp, 1.2f + 0.8f * (float)_rng.NextDouble(),
                      sz * (0.15f + 0.12f * (float)_rng.NextDouble()), new SKColor(36, 34, 44, 120), drag: 1.2f);
        }
        _shake = MathF.Max(_shake, MathF.Min(w, h) * (h < w * 0.35f ? 0.025f : 0.009f) * (0.6f + 0.6f * strength));
        if (_target >= 0 && _enemies[_target] == e) _target = -1;
    }

    // ------------------------------------------------------------------ cruise events

    private void UpdateEvents(AudioFrame frame, float dt, float w, float h, float L, float top, float bottom, bool strip, float s, float speedMul)
    {
        // Rocks are near, so they stream past faster than the stars.
        _rocks.Update(dt, w, h, 40f * s * speedMul);
        for (int i = 0; i < _rocks.Count; i++)
        {
            // Too close to dodge: it smashes on the shield.
            ref var r = ref _rocks[i];
            float dx = r.X - _heroX, dy = r.Y - _heroY, rr = r.R + L * 0.3f;
            if (dx * dx + dy * dy < rr * rr) { _shield = 1f; BreakRock(i, L, s, w, h); i--; }
        }

        if (_cometT >= 0)
        {
            _cometT += dt;
            _cometX += _cometVX * dt;
            _cometY += _cometVY * dt;
            if (_cometX < -w * 0.3f || _cometY > h * 1.4f || _cometT > 8f) _cometT = -1f;
        }

        if (_rockFieldLeft > 0)
        {
            _rockFieldLeft -= dt;
            _rockSpawnT -= dt;
            if ((frame.Beat && _rng.NextDouble() < 0.75) || _rockSpawnT <= 0)
            {
                SpawnRock(w, h, L, strip);
                _rockSpawnT = 0.45f + 0.6f * (float)_rng.NextDouble();
            }
        }

        if (_mode != Mode.Cruise || _combat > 0.5f) return;
        // Something happens every few seconds, landing on a beat when there is one.
        _eventCd -= dt;
        if (_eventCd > 0 || !(frame.Beat || _eventCd < -1.5f)) return;
        _eventCd = 3.5f + 3f * (float)_rng.NextDouble();
        bool scoutOut = false;
        foreach (var e in _enemies) if (e.State == EnemyState.Scout) scoutOut = true;
        double pick = _rng.NextDouble();
        if (pick < 0.32 && _rockFieldLeft <= 0) { _rockFieldLeft = 7f + 5f * (float)_rng.NextDouble(); _rockSpawnT = 0; }
        else if (pick < 0.62 && !scoutOut) SpawnScout(w, h, L, top, bottom);
        else if (pick < 0.82 && _wingT < 0) StartWingman(L, top, bottom);
        else if (_cometT < 0) StartComet(w, h, strip);
        else if (_rockFieldLeft <= 0) { _rockFieldLeft = 7f + 5f * (float)_rng.NextDouble(); _rockSpawnT = 0; }
    }

    private void SpawnRock(float w, float h, float L, bool strip)
    {
        float r = L * (0.1f + 0.32f * MathF.Pow((float)_rng.NextDouble(), 1.5f));
        if (strip) r = MathF.Min(r, h * 0.16f);
        // Often right in the hero's lane, so it has to weave (or shoot).
        float y = _rng.NextDouble() < 0.4
            ? _heroY + (float)(_rng.NextDouble() - 0.5) * L * 1.5f
            : h * (0.05f + 0.9f * (float)_rng.NextDouble());
        _rocks.Spawn(w + r * 2f, y, -w * (0.03f + 0.07f * (float)_rng.NextDouble()), (float)(_rng.NextDouble() - 0.5) * h * 0.05f,
                     r, _rng.Next(5), (float)(_rng.NextDouble() - 0.5) * 3f);
    }

    private void BreakRock(int index, float L, float s, float w, float h)
    {
        var r = _rocks[index];
        _rocks.RemoveAt(index);
        var col = _rocks.Color;
        for (int i = 0; i < 6 + (int)(r.R / L * 12); i++)
        {
            float a = (float)(_rng.NextDouble() * MathF.Tau);
            float sp = r.R * (1f + 3f * (float)_rng.NextDouble());
            _fx.Spawn(FxKind.Debris, r.X, r.Y, MathF.Cos(a) * sp + r.VX, MathF.Sin(a) * sp, 0.7f + 0.6f * (float)_rng.NextDouble(),
                      r.R * (0.18f + 0.2f * (float)_rng.NextDouble()), col, _warm, drag: 0.8f, spin: (float)(_rng.NextDouble() - 0.5) * 10f, rot: a);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = (float)(_rng.NextDouble() * MathF.Tau);
            float sp = r.R * (0.5f + 1.5f * (float)_rng.NextDouble());
            _fx.Spawn(FxKind.Fire, r.X, r.Y, MathF.Cos(a) * sp, MathF.Sin(a) * sp, 0.3f + 0.3f * (float)_rng.NextDouble(), r.R * 0.45f, _warm, _ember, drag: 2f);
        }
        _fx.Spawn(FxKind.Smoke, r.X, r.Y, r.VX * 0.3f, 0, 0.9f, r.R * 0.6f, new SKColor(60, 58, 66, 90), drag: 1f);
        // Big rocks split in two.
        if (r.R > L * 0.24f)
            for (int k = -1; k <= 1; k += 2)
                _rocks.Spawn(r.X, r.Y + k * r.R * 0.3f, r.VX - w * 0.02f, k * h * (0.06f + 0.06f * (float)_rng.NextDouble()),
                             r.R * 0.55f, _rng.Next(5), (float)(_rng.NextDouble() - 0.5) * 5f);
        _shake = MathF.Max(_shake, r.R * 0.05f);
    }

    private void StartComet(float w, float h, bool strip)
    {
        _cometT = 0;
        _cometX = w * (0.55f + 0.5f * (float)_rng.NextDouble());
        _cometY = -h * 0.08f;
        _cometVX = -w * (0.3f + 0.15f * (float)_rng.NextDouble());
        _cometVY = h * (strip ? 0.3f : 0.22f + 0.12f * (float)_rng.NextDouble());
        _cometColor = _palette?.Sample((float)_rng.NextDouble()) ?? SKColors.White;
    }

    private void StartWingman(float L, float top, float bottom)
    {
        _wingT = 0;
        _wingSide = _heroY > (top + bottom) / 2 ? -1f : 1f;
        _wingX = -L;
        _wingY = _heroY + _wingSide * L;
        _wingVy = 0;
    }

    /// <summary>A friendly wingman: flies in from behind, holds formation for a while, then peels off ahead.</summary>
    private void UpdateWingman(float dt, float w, float L, float top, float bottom, float h)
    {
        if (_wingT < 0) return;
        _wingT += dt;
        float WL = L * 0.72f;
        float ty = Math.Clamp(_heroY + _wingSide * L * 0.95f, top, bottom);
        if (_wingT < 13f)
            _wingX += (_heroX - L * 0.95f - _wingX) * (1f - MathF.Exp(-dt / 0.45f));
        else
        {
            _wingX += w * 0.9f * MathF.Min(1f, _wingT - 13f) * dt;
            ty = _wingY - _wingSide * h * 0.05f;
        }
        _wingVy += ((ty - _wingY) * 18f - _wingVy * 7f) * dt;
        _wingY += _wingVy * dt;
        float bank = Math.Clamp(_wingVy / (h * 0.45f), -1f, 1f);
        _wingAngle = bank * 0.2f;
        _wingScaleY = 1f - 0.32f * MathF.Abs(bank);
        if (_wingX > w + WL * 2f) _wingT = -1f;
    }

    private static float SegDist2(float ax, float ay, float bx, float by, float px, float py)
    {
        float dx = bx - ax, dy = by - ay;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 0 ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0f, 1f) : 0f;
        float cx = ax + dx * t - px, cy = ay + dy * t - py;
        return cx * cx + cy * cy;
    }

    // ------------------------------------------------------------------ scenery

    private void EnsureCache(in RenderContext ctx, int w, int h)
    {
        var key = (ctx.Palette, w, h, ctx.Overlay);
        if (key == _cacheKey && (ctx.Overlay || _backdrop.Sky != null)) return;
        _cacheKey = key;
        _backdrop.Build(ctx.Palette, w, h, ctx.Scale, ctx.Overlay);
        _ships.SetPalette(ctx.Palette);
        _rocks.SetPalette(ctx.Palette);
        _palette = ctx.Palette;
        _warm = ColorUtil.Mix(new SKColor(255, 176, 64), _ships.EnemyColor, 0.12f);
        _ember = ColorUtil.Mix(new SKColor(200, 60, 20), ColorUtil.Darken(_ships.EnemyColor, 0.4f), 0.5f);
        _fx.SetFireColors(_warm, _ember);
    }

    private void UpdateStars(float dt, float w, float h, float s, float speedMul, float warp, bool overlay, bool strip)
    {
        for (int l = 0; l < StarLayers; l++)
        {
            int n = StarCount(l, overlay, strip);
            float v = StarSpeeds[l] * s * speedMul / w;   // screen widths per second
            float streak = StarSpeeds[l] * s * speedMul * (0.018f + 0.09f * warp);
            var xs = _starX[l];
            var ys = _starY[l];
            var pts = _starPts[l];
            if (pts.Length != n * 2) _starPts[l] = pts = new SKPoint[n * 2];   // only when the layout mode changes
            for (int i = 0; i < n; i++)
            {
                xs[i] -= v * dt;
                if (xs[i] < 0f)
                {
                    xs[i] += 1f + streak / w;
                    ys[i] = (float)_rng.NextDouble();
                }
                float x = xs[i] * w, y = ys[i] * h;
                pts[2 * i] = new SKPoint(x, y);
                pts[2 * i + 1] = new SKPoint(x + streak, y);
            }
        }
    }

    private int StarCount(int layer, bool overlay, bool strip)
    {
        int n = StarCounts[layer];
        if (overlay) n = n / (strip ? 6 : 4);
        else if (strip) n = n * 2 / 3;
        return n;
    }

    private void UpdatePlanets(float dt, float w, float h, float s, float speedMul)
    {
        var big = _backdrop.BigPlanet;
        var small = _backdrop.SmallPlanet;
        if (big == null || small == null) return;
        if (float.IsNaN(_bigX))
        {
            _bigX = w * 0.8f; _bigY = h * 0.3f;
            _smallX = w * 0.42f; _smallY = h * 0.78f;
        }
        float m = 0.6f + 0.4f * MathF.Min(speedMul, 6f);
        _bigX -= 3.5f * s * m * dt;
        _smallX -= 9f * s * m * dt;
        if (_bigX < -big.Width / 2f) { _bigX = w + big.Width / 2f; _bigY = h * (0.15f + 0.7f * (float)_rng.NextDouble()); }
        if (_smallX < -small.Width / 2f) { _smallX = w + small.Width / 2f; _smallY = h * (0.15f + 0.7f * (float)_rng.NextDouble()); }
    }

    // ------------------------------------------------------------------ drawing

    private void Draw(in RenderContext ctx, AudioFrame frame, float L, float warp, bool strip)
    {
        var c = ctx.Canvas;
        var g = ctx.GlowCanvas;
        float w = ctx.Width, h = ctx.Height, s = ctx.Scale;
        bool overlay = ctx.Overlay;

        if (!overlay && _backdrop.Sky != null)
        {
            c.DrawImage(_backdrop.Sky, 0, 0, _image);
            if (_backdrop.BigPlanet is { } big && _backdrop.SmallPlanet is { } small)
            {
                c.DrawImage(big, _bigX - big.Width / 2f, _bigY - big.Height / 2f, _planetPaint);
                c.DrawImage(small, _smallX - small.Width / 2f, _smallY - small.Height / 2f, _planetPaint);
            }
        }

        c.Save();
        c.Translate(_shakeX, _shakeY);
        g?.Save();
        g?.Translate(_shakeX, _shakeY);

        // Stars, tinted slightly by the palette; hi-hats make them sparkle.
        float twinkle = 0.7f + 0.35f * frame.Treble + 0.35f * _starKick;
        for (int l = 0; l < StarLayers; l++)
        {
            int n = StarCount(l, overlay, strip);
            var tint = ColorUtil.Mix(SKColors.White, ctx.Palette.Sample(l / 2f), 0.18f + 0.1f * l);
            float a = StarAlpha[l] * twinkle * (overlay ? 0.75f : 1f);
            _starPaint.Color = tint.WithAlpha((byte)Math.Clamp(a, 0, 255));
            _starPaint.StrokeWidth = StarSizes[l] * s * (overlay ? 1.1f : 1f);
            c.DrawPoints(SKPointMode.Lines, _starPts[l], _starPaint);
            if (g != null && warp > 0.05f && l == StarLayers - 1)
            {
                _starPaint.Color = tint.WithAlpha((byte)(160 * warp));
                _starPaint.StrokeWidth = StarSizes[l] * s * 3f;
                g.DrawPoints(SKPointMode.Lines, _starPts[l], _starPaint);
            }
        }

        if (_cometT >= 0)
        {
            float alpha = Math.Clamp(_cometT / 0.4f, 0f, 1f);
            _rocks.DrawComet(c, g, _cometX, _cometY, _cometVX, _cometVY, MathF.Min(w * 0.16f, MathF.Max(h * 0.5f, w * 0.1f)),
                             MathF.Max(2.2f * s, L * 0.03f), _cometColor, alpha);
        }
        _rocks.Draw(c, s, overlay);

        // Enemies (with warp streaks while arriving or leaving).
        foreach (var e in _enemies)
        {
            if (e.State == EnemyState.Off) continue;
            float streak = 0;
            if (e.State == EnemyState.WarpIn) streak = (1f - Math.Clamp(e.T / 0.55f, 0f, 1f)) * w * 0.6f;
            else if (e.State == EnemyState.Retreat && e.VX > 0) streak = -MathF.Min(w * 0.5f, e.VX * 0.12f);
            if (MathF.Abs(streak) > 2f) DrawWarpStreak(c, g, e.X, e.Y, streak, e.Size, s);

            float thrust = e.State == EnemyState.Retreat ? 1.2f : 0.35f + 0.4f * frame.Bass;
            float alpha = e.State == EnemyState.WarpIn ? Math.Clamp(e.T / 0.15f, 0f, 1f) : 1f;
            _ships.DrawEnemy(c, g, e.Kind, e.X, e.Y, e.Angle, e.Facing, e.Size, thrust, e.Hit, e.Charge, alpha, s, overlay);
        }

        // Hero
        float heroThrust = 0.3f + 0.6f * frame.Bass + 0.35f * frame.Pulse + 0.25f * _combat + 0.8f * _flare + 1.3f * warp;
        if (_wingT >= 0)
            _ships.DrawHero(c, g, _wingX, _wingY, _wingAngle, _wingScaleY, L * 0.72f, heroThrust * 0.8f + (_wingT > 13f ? 0.8f : 0f), 0f, s, overlay);
        if (warp > 0.05f) DrawWarpStreak(c, g, _heroX - L * 0.45f, _heroY, -w * 0.35f * warp, L * 0.5f, s, hero: true);
        _ships.DrawHero(c, g, _heroX - _heroRecoil * L * 0.03f, _heroY, _heroAngle, _heroScaleY, L, heroThrust, _shield, s, overlay);

        _bolts.Draw(c, g);
        _fx.Draw(c, g);

        c.Restore();
        g?.Restore();
    }

    /// <param name="length">Positive: streak trails to the right of (x, y); negative: to the left.</param>
    private void DrawWarpStreak(SKCanvas c, SKCanvas? g, float x, float y, float length, float size, float s, bool hero = false)
    {
        var col = hero ? _ships.HeroLaser : _ships.EnemyColor;
        float x0 = x + MathF.Sign(length) * size * 0.25f;
        float x1 = x0 + length;
        float a = Math.Clamp(MathF.Abs(length) / (size * 4f), 0f, 1f);
        // Tapered: a few lines of decreasing length and increasing width/alpha toward the ship.
        for (int i = 0; i < 3; i++)
        {
            float k = 1f - i * 0.3f;
            _streak.Color = col.WithAlpha((byte)(a * (50 + 45 * i)));
            _streak.StrokeWidth = size * (hero ? 0.03f : 0.04f) * (1 + i);
            c.DrawLine(x0, y, x0 + length * k, y, _streak);
        }
        _streak.Color = SKColors.White.WithAlpha((byte)(210 * a));
        _streak.StrokeWidth = MathF.Max(1.2f * s, size * 0.025f);
        c.DrawLine(x0, y, x0 + length * 0.6f, y, _streak);
        if (g != null)
        {
            _streak.Color = col.WithAlpha((byte)(150 * a));
            _streak.StrokeWidth = size * 0.16f;
            g.DrawLine(x0, y, x0 + length * 0.7f, y, _streak);
        }
    }
}
