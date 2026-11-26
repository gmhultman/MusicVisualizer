using System.Numerics;

namespace MusicVisualizer.Visuals.Disaster;

// A small Verlet physics engine for the Dance Disaster scene.
// World units are metres, y points DOWN (like the screen): the room spans x = Left..Right, y = Top..Floor.
// Everything is made of particles joined by sticks: a box is 4 corner particles + 6 sticks (edges and
// diagonals), a ragdoll is particles for its joints. Nails/hinges are sticks to pinned (immovable) particles.

/// <summary>Which other things a particle collides with.</summary>
[Flags]
internal enum CollisionMask
{
    None = 0,
    Bounds = 1,        // floor, walls, ceiling
    Props = 2,         // dynamic props (boxes/circles that can move)
    Static = 4,        // static furniture marked collidable (shelves, tables)
    All = Bounds | Props | Static,
}

internal sealed class Particle
{
    public Vector2 Pos;
    public Vector2 Prev;
    public float InvMass = 1f;          // 0 = pinned / immovable
    public float Radius;                // > 0 makes it a ball for collisions (e.g. a ragdoll's head)
    public float Friction = 0.5f;       // 0 = ice, 1 = sticky (tangential velocity removed on contact)
    public float Bounce = 0.1f;         // restitution against the room bounds
    public CollisionMask Mask = CollisionMask.All;
    public Body? Owner;                 // the prop this particle belongs to (null for ragdoll joints)
    public bool OnGround;               // touched the floor this step

    public Particle(Vector2 pos, float invMass = 1f, float radius = 0f)
    {
        Pos = Prev = pos;
        InvMass = invMass;
        Radius = radius;
    }

    public bool Pinned => InvMass == 0f;

    /// <summary>Current velocity in m/s (Verlet stores it implicitly as Pos - Prev).</summary>
    public Vector2 Velocity(float dt) => (Pos - Prev) / dt;

    /// <summary>Changes the velocity by <paramref name="dv"/> m/s.</summary>
    public void Push(Vector2 dv, float dt)
    {
        if (!Pinned) Prev -= dv * dt;
    }

    /// <summary>Moves the particle without giving it velocity.</summary>
    public void Teleport(Vector2 pos)
    {
        Prev += pos - Pos;
        Pos = pos;
    }

    /// <summary>Moves the particle and stops it.</summary>
    public void Place(Vector2 pos) => Pos = Prev = pos;
}

internal sealed class Stick
{
    public Particle A, B;
    public float Length;
    public float Stiffness = 1f;        // 0..1 per iteration; < 1 = springy (muscles, ropes)
    public float BreakStretch;          // break when stretched beyond Length*(1+BreakStretch); 0 = never
    public bool Broken;
    public bool MaxOnly;                // rope/chain: only resists stretching, can go slack

    public Stick(Particle a, Particle b, float stiffness = 1f, float? length = null)
    {
        A = a;
        B = b;
        Stiffness = stiffness;
        Length = length ?? Vector2.Distance(a.Pos, b.Pos);
    }
}

internal enum BodyKind { Box, Circle }

/// <summary>A prop: a rigid box (4 corners, clockwise TL,TR,BR,BL in its local frame) or a ball (1 particle).</summary>
internal sealed class Body
{
    public BodyKind Kind;
    public Particle[] Points = [];
    public List<Stick> Sticks = [];
    public Vector2 Size;                // box width/height (m); for a circle, (2r, 2r)
    public bool IsStatic;               // furniture: pinned, never moves
    public bool Collidable = true;      // static furniture can be decorative only
    public object? Tag;                 // owner data (prop type, drawing info…)
    public float RollAngle;             // circles: accumulated rotation from rolling, for drawing

    public Vector2 Center => Kind == BodyKind.Circle ? Points[0].Pos : (Points[0].Pos + Points[1].Pos + Points[2].Pos + Points[3].Pos) / 4f;

    /// <summary>Rotation in radians (boxes: from the top edge; circles: rolling angle).</summary>
    public float Angle
    {
        get
        {
            if (Kind == BodyKind.Circle) return RollAngle;
            var e = Points[1].Pos - Points[0].Pos;
            return MathF.Atan2(e.Y, e.X);
        }
    }

    public Vector2 Velocity(float dt) => Kind == BodyKind.Circle
        ? Points[0].Velocity(dt)
        : (Points[0].Velocity(dt) + Points[1].Velocity(dt) + Points[2].Velocity(dt) + Points[3].Velocity(dt)) / 4f;

    public float Radius => Kind == BodyKind.Circle ? Points[0].Radius : 0f;

    public void Push(Vector2 dv, float dt)
    {
        foreach (var p in Points) p.Push(dv, dt);
    }

    /// <summary>Puts the body back at a position/angle, at rest (used when tidying up).</summary>
    public void Reset(Vector2 center, float angle = 0f)
    {
        if (Kind == BodyKind.Circle)
        {
            Points[0].Place(center);
            RollAngle = 0;
            return;
        }
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        Vector2 Rot(float x, float y) => center + new Vector2(x * c - y * s, x * s + y * c);
        float hw = Size.X / 2, hh = Size.Y / 2;
        Points[0].Place(Rot(-hw, -hh));
        Points[1].Place(Rot(hw, -hh));
        Points[2].Place(Rot(hw, hh));
        Points[3].Place(Rot(-hw, hh));
    }
}

internal sealed class PhysicsWorld
{
    public Vector2 Gravity = new(0, 9.8f);
    public float Left, Right, Top, Floor;
    public int Iterations = 8;
    public float AirDrag = 0.02f;       // fraction of velocity lost per second

    public readonly List<Particle> Particles = [];
    public readonly List<Stick> Sticks = [];
    public readonly List<Body> Bodies = [];

    public PhysicsWorld(float left, float right, float top, float floor)
    {
        Left = left;
        Right = right;
        Top = top;
        Floor = floor;
    }

    // ------------------------------------------------------------------ construction helpers

    public Particle AddParticle(Vector2 pos, float invMass = 1f, float radius = 0f)
    {
        var p = new Particle(pos, invMass, radius);
        Particles.Add(p);
        return p;
    }

    public Stick AddStick(Particle a, Particle b, float stiffness = 1f, float? length = null)
    {
        var s = new Stick(a, b, stiffness, length);
        Sticks.Add(s);
        return s;
    }

    /// <summary>A rigid box. <paramref name="mass"/> in kg spread over the corners; mass 0 makes it static.</summary>
    public Body AddBox(Vector2 center, Vector2 size, float mass = 1f, float angle = 0f, object? tag = null)
    {
        bool isStatic = mass <= 0f;
        float inv = isStatic ? 0f : 4f / mass;
        var b = new Body { Kind = BodyKind.Box, Size = size, IsStatic = isStatic, Tag = tag };
        b.Points = new Particle[4];
        for (int i = 0; i < 4; i++)
        {
            var p = AddParticle(Vector2.Zero, inv);
            p.Owner = b;
            b.Points[i] = p;
        }
        b.Reset(center, angle);
        if (!isStatic)
        {
            for (int i = 0; i < 4; i++) b.Sticks.Add(AddStick(b.Points[i], b.Points[(i + 1) % 4]));
            b.Sticks.Add(AddStick(b.Points[0], b.Points[2]));
            b.Sticks.Add(AddStick(b.Points[1], b.Points[3]));
        }
        Bodies.Add(b);
        return b;
    }

    /// <summary>A ball (plant pot, orange, ornament…). Mass 0 makes it static.</summary>
    public Body AddCircle(Vector2 center, float radius, float mass = 1f, object? tag = null)
    {
        bool isStatic = mass <= 0f;
        var b = new Body { Kind = BodyKind.Circle, Size = new Vector2(radius * 2), IsStatic = isStatic, Tag = tag };
        var p = AddParticle(center, isStatic ? 0f : 1f / mass, radius);
        p.Owner = b;
        b.Points = [p];
        Bodies.Add(b);
        return b;
    }

    /// <summary>Nails a point of a body (or any particle) to a fixed spot. Break the returned stick to release it.</summary>
    public Stick Nail(Particle p, Vector2 anchor, float slack = 0f)
    {
        var pin = AddParticle(anchor, 0f);
        pin.Mask = CollisionMask.None;
        var s = AddStick(pin, p, 1f, Vector2.Distance(anchor, p.Pos) + slack);
        s.MaxOnly = slack > 0;
        return s;
    }

    /// <summary>Gives every movable particle a velocity kick (room shake on a big bass hit).</summary>
    public void Shake(Vector2 dv, float dt, Func<Particle, bool>? filter = null)
    {
        foreach (var p in Particles)
            if (!p.Pinned && (filter == null || filter(p))) p.Push(dv, dt);
    }

    // ------------------------------------------------------------------ simulation

    public void Step(float dt)
    {
        if (dt <= 0) return;
        float keep = MathF.Max(0f, 1f - AirDrag * dt);
        var g = Gravity * dt * dt;

        foreach (var p in Particles)
        {
            p.OnGround = false;
            if (p.Pinned) continue;
            var v = (p.Pos - p.Prev) * keep;
            p.Prev = p.Pos;
            p.Pos += v + g;
        }

        for (int it = 0; it < Iterations; it++)
        {
            foreach (var s in Sticks) SolveStick(s);
            foreach (var p in Particles) CollideBounds(p);
            CollideBodies();
        }

        foreach (var b in Bodies)
        {
            if (b.Kind != BodyKind.Circle || b.IsStatic) continue;
            var p = b.Points[0];
            if (p.Radius > 0) b.RollAngle += (p.Pos.X - p.Prev.X) / p.Radius; // roll along the floor
        }
    }

    private static void SolveStick(Stick s)
    {
        if (s.Broken) return;
        var a = s.A;
        var b = s.B;
        float w = a.InvMass + b.InvMass;
        if (w == 0) return;
        var d = b.Pos - a.Pos;
        float len = d.Length();
        if (len < 1e-6f) return;
        if (s.BreakStretch > 0 && len > s.Length * (1 + s.BreakStretch))
        {
            s.Broken = true;
            return;
        }
        if (s.MaxOnly && len <= s.Length) return;
        float diff = (len - s.Length) / len * s.Stiffness;
        var corr = d * diff;
        a.Pos += corr * (a.InvMass / w);
        b.Pos -= corr * (b.InvMass / w);
    }

    private void CollideBounds(Particle p)
    {
        if (p.Pinned || (p.Mask & CollisionMask.Bounds) == 0) return;
        float r = p.Radius;
        if (p.Pos.Y > Floor - r)
        {
            float vy = p.Pos.Y - p.Prev.Y;
            p.Pos.Y = Floor - r;
            p.Prev.Y = p.Pos.Y + vy * p.Bounce;      // bounce
            p.Prev.X += (p.Pos.X - p.Prev.X) * p.Friction; // friction
            p.OnGround = true;
        }
        if (p.Pos.Y < Top + r) { p.Pos.Y = Top + r; }
        if (p.Pos.X < Left + r)
        {
            float vx = p.Pos.X - p.Prev.X;
            p.Pos.X = Left + r;
            p.Prev.X = p.Pos.X + vx * p.Bounce;
        }
        else if (p.Pos.X > Right - r)
        {
            float vx = p.Pos.X - p.Prev.X;
            p.Pos.X = Right - r;
            p.Prev.X = p.Pos.X + vx * p.Bounce;
        }
    }

    private void CollideBodies()
    {
        var bodies = Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            var a = bodies[i];
            if (!a.Collidable) continue;

            // Free particles (ragdoll joints) against this body.
            var need = a.IsStatic ? CollisionMask.Static : CollisionMask.Props;
            foreach (var p in Particles)
            {
                if (p.Owner != null || p.Pinned || (p.Mask & need) == 0) continue;
                CollideParticleBody(p, a);
            }

            for (int j = i + 1; j < bodies.Count; j++)
            {
                var b = bodies[j];
                if (!b.Collidable || (a.IsStatic && b.IsStatic)) continue;
                if (!BoundsOverlap(a, b)) continue;
                if (a.Kind == BodyKind.Circle && b.Kind == BodyKind.Circle)
                {
                    CollideCircles(a.Points[0], b.Points[0]);
                    continue;
                }
                // Each body's corner (or ball) against the other's polygon.
                if (b.Kind == BodyKind.Box) foreach (var p in a.Points) CollideParticleBody(p, b);
                if (a.Kind == BodyKind.Box) foreach (var p in b.Points) CollideParticleBody(p, a);
            }
        }
    }

    private static bool BoundsOverlap(Body a, Body b)
    {
        var (aMin, aMax) = Bounds(a);
        var (bMin, bMax) = Bounds(b);
        return aMin.X <= bMax.X && aMax.X >= bMin.X && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y;
    }

    private static (Vector2 min, Vector2 max) Bounds(Body b)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var p in b.Points)
        {
            min = Vector2.Min(min, p.Pos - new Vector2(p.Radius));
            max = Vector2.Max(max, p.Pos + new Vector2(p.Radius));
        }
        return (min, max);
    }

    private static void CollideCircles(Particle a, Particle b)
    {
        float w = a.InvMass + b.InvMass;
        if (w == 0) return;
        var d = b.Pos - a.Pos;
        float dist = d.Length(), r = a.Radius + b.Radius;
        if (dist >= r || dist < 1e-6f) return;
        var n = d / dist;
        float pen = r - dist;
        a.Pos -= n * pen * (a.InvMass / w);
        b.Pos += n * pen * (b.InvMass / w);
    }

    /// <summary>Pushes a particle (a point or a ball of its radius) out of a box body, sharing the correction
    /// between the particle and the edge it hit, weighted by mass (Jakobsen-style).</summary>
    private static void CollideParticleBody(Particle p, Body box)
    {
        if (box.Kind != BodyKind.Box)
        {
            // ball vs ball handled elsewhere; a free particle against a ball:
            if (p.Owner == null) CollideCircles(p, box.Points[0]);
            return;
        }
        if (p.Owner == box) return;

        var pts = box.Points;
        float r = p.Radius;
        float best = float.MinValue;
        int bestEdge = -1;
        Vector2 bestN = default;
        for (int e = 0; e < 4; e++)
        {
            var e0 = pts[e].Pos;
            var e1 = pts[(e + 1) % 4].Pos;
            var edge = e1 - e0;
            float len = edge.Length();
            if (len < 1e-6f) return;
            var n = new Vector2(edge.Y, -edge.X) / len;   // outward normal for clockwise corners (y down)
            float dist = Vector2.Dot(p.Pos - e0, n);
            if (dist > r) return;                          // outside this edge → no contact
            if (dist > best)
            {
                best = dist;
                bestEdge = e;
                bestN = n;
            }
        }
        if (bestEdge < 0) return;

        float pen = r - best;
        var a = pts[bestEdge];
        var b = pts[(bestEdge + 1) % 4];
        var ab = b.Pos - a.Pos;
        float t = Math.Clamp(Vector2.Dot(p.Pos - a.Pos, ab) / ab.LengthSquared(), 0f, 1f);
        float ea = (1 - t) * a.InvMass, eb = t * b.InvMass;
        float wEdge = ea + eb;
        float w = p.InvMass + wEdge;
        if (w == 0) return;

        var corr = bestN * pen;
        p.Pos += corr * (p.InvMass / w);
        if (wEdge > 0)
        {
            float edgeShare = wEdge / w;
            // distribute over the two edge corners so that the contact point moves by edgeShare
            float lambda = edgeShare / ((1 - t) * (1 - t) * a.InvMass + t * t * b.InvMass + 1e-9f);
            a.Pos -= corr * ((1 - t) * a.InvMass * lambda);
            b.Pos -= corr * (t * b.InvMass * lambda);
        }

        // Friction: damp the particle's sliding along the contact surface.
        if (!p.Pinned)
        {
            var v = p.Pos - p.Prev;
            var vt = v - bestN * Vector2.Dot(v, bestN);
            float f = MathF.Max(p.Friction, box.Points[0].Friction) * 0.5f;
            p.Prev += vt * f;
        }
    }
}
