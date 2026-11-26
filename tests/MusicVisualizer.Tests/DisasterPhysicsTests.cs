using System.Numerics;
using MusicVisualizer.Audio;
using MusicVisualizer.Visuals.Disaster;

namespace MusicVisualizer.Tests;

/// <summary>Stability checks for the Dance Disaster physics: nothing explodes, tunnels or escapes the room.</summary>
public class DisasterPhysicsTests
{
    private const float H = 1f / 120f;

    private static PhysicsWorld NewWorld() => new(0f, 8f, 0f, 4.2f);

    private static void Run(PhysicsWorld world, float seconds, Action<int>? each = null)
    {
        int steps = (int)(seconds / H);
        for (int i = 0; i < steps; i++)
        {
            each?.Invoke(i);
            world.Step(H);
        }
    }

    private static void AssertSane(PhysicsWorld world)
    {
        foreach (var p in world.Particles)
        {
            Assert.True(float.IsFinite(p.Pos.X) && float.IsFinite(p.Pos.Y), "particle position is not finite");
            if (p.Pinned || (p.Mask & CollisionMask.Bounds) == 0) continue;
            Assert.InRange(p.Pos.X, world.Left - 0.01f, world.Right + 0.01f);
            Assert.InRange(p.Pos.Y, world.Top - 0.01f, world.Floor + 0.01f);
        }
    }

    [Fact]
    public void Dropped_box_comes_to_rest_on_the_floor()
    {
        var world = NewWorld();
        var box = world.AddBox(new Vector2(4f, 1f), new Vector2(0.4f, 0.3f), 1f);
        Run(world, 4f);

        AssertSane(world);
        Assert.InRange(box.Center.Y, 4.2f - 0.15f - 0.02f, 4.2f - 0.15f + 0.02f);
        Assert.True(box.Velocity(H).Length() < 0.05f, "box should be at rest");
    }

    [Fact]
    public void Stacked_boxes_stay_stacked_and_do_not_interpenetrate()
    {
        var world = NewWorld();
        var bottom = world.AddBox(new Vector2(4f, 3.9f), new Vector2(0.6f, 0.3f), 2f);
        var top = world.AddBox(new Vector2(4f, 3.5f), new Vector2(0.4f, 0.3f), 1f);
        Run(world, 4f);

        AssertSane(world);
        Assert.True(top.Center.Y < bottom.Center.Y - 0.25f, "top box sank into the bottom one");
        Assert.InRange(top.Center.X, 3.8f, 4.2f);
    }

    [Fact]
    public void Nailed_frame_hangs_until_the_nail_breaks()
    {
        var world = NewWorld();
        var frame = world.AddBox(new Vector2(2f, 1.5f), new Vector2(0.8f, 0.6f), 1.5f);
        var nail = world.Nail(frame.Points[0], frame.Points[0].Pos + new Vector2(0.4f, -0.2f));
        Run(world, 2f);
        Assert.True(frame.Center.Y < 2.2f, "frame should still be hanging");

        nail.Broken = true;
        Run(world, 3f);
        AssertSane(world);
        Assert.True(frame.Center.Y > 3.6f, "frame should have fallen to the floor");
    }

    [Fact]
    public void Heavy_shaking_never_explodes_or_escapes()
    {
        var world = NewWorld();
        var rng = new Random(3);
        for (int i = 0; i < 12; i++)
            world.AddBox(new Vector2(1f + i * 0.5f, 1f + (i % 3) * 0.5f), new Vector2(0.3f, 0.25f), 1f);
        for (int i = 0; i < 4; i++)
            world.AddCircle(new Vector2(2f + i, 0.5f), 0.12f, 0.5f);

        Run(world, 10f, step =>
        {
            if (step % 30 == 0)
                world.Shake(new Vector2((float)(rng.NextDouble() * 6 - 3), -(float)rng.NextDouble() * 5), H);
        });
        AssertSane(world);
    }

    [Fact]
    public void Ragdoll_survives_knocks_and_gets_back_up()
    {
        var world = NewWorld();
        var dancer = new Ragdoll(world, 4f);
        var frame = new AudioFrame(64);

        void Frames(float seconds)
        {
            for (int i = 0; i < (int)(seconds * 60); i++)
            {
                dancer.Update(frame, 1f / 60f, 0.6f);
                dancer.PreStep(H); world.Step(H);
                dancer.PreStep(H); world.Step(H);
            }
        }

        Frames(1f);
        dancer.Knock(new Vector2(4f, -1f));
        Frames(0.5f);
        Assert.True(dancer.Dizzy, "knocked dancer should be down");

        Frames(8f);
        AssertSane(world);
        Assert.Equal(DancerState.Dancing, dancer.State);
        Assert.True(dancer.Head.Pos.Y < 4.2f - 1.2f, "dancer should be standing again");
    }
}
