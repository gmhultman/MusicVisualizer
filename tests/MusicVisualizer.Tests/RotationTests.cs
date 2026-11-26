using MusicVisualizer.Services;

namespace MusicVisualizer.Tests;

public class RotationTests
{
    private static readonly int[] Sparse = [0, 2, 5];

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 1, 5)]
    [InlineData(5, 1, 0)]
    [InlineData(0, -1, 5)]
    [InlineData(5, -1, 2)]
    [InlineData(2, -1, 0)]
    public void Step_moves_through_enabled_items_and_wraps(int current, int direction, int expected)
    {
        Assert.Equal(expected, Rotation.Step(current, Sparse, direction));
    }

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(3, 1, 5)]
    [InlineData(6, 1, 0)]  // nothing above: wraps to the first
    [InlineData(3, -1, 2)]
    [InlineData(4, -1, 2)]
    [InlineData(1, -1, 0)]
    public void Step_from_a_disabled_item_picks_the_nearest_enabled_in_that_direction(int current, int direction, int expected)
    {
        Assert.Equal(expected, Rotation.Step(current, Sparse, direction));
    }

    [Fact]
    public void Step_backward_from_below_all_enabled_wraps_to_the_last()
    {
        Assert.Equal(5, Rotation.Step(1, new[] { 2, 3, 5 }, -1));
    }

    [Fact]
    public void Step_with_nothing_enabled_returns_current()
    {
        Assert.Equal(4, Rotation.Step(4, Array.Empty<int>(), 1));
        Assert.Equal(4, Rotation.Step(4, Array.Empty<int>(), -1));
    }

    [Fact]
    public void Step_with_one_enabled_item_returns_it()
    {
        Assert.Equal(3, Rotation.Step(3, new[] { 3 }, 1));
        Assert.Equal(3, Rotation.Step(3, new[] { 3 }, -1));
        Assert.Equal(3, Rotation.Step(0, new[] { 3 }, 1));
        Assert.Equal(3, Rotation.Step(7, new[] { 3 }, -1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void Shuffle_visits_every_other_enabled_item_before_repeating(int seed)
    {
        int[] enabled = [0, 1, 3, 4, 6, 8];
        var rotation = new Rotation(new Random(seed));
        int start = 3, current = start;
        var seen = new HashSet<int>();

        for (int i = 0; i < enabled.Length - 1; i++)
        {
            current = rotation.Shuffle(current, enabled);
            Assert.Contains(current, enabled);
            Assert.NotEqual(start, current);
            Assert.True(seen.Add(current), $"{current} came up twice in one bag");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void Shuffle_never_repeats_back_to_back(int seed)
    {
        int[] enabled = [0, 2, 3, 5, 7];
        var rotation = new Rotation(new Random(seed));
        int current = 0;

        for (int i = 0; i < 200; i++)
        {
            int next = rotation.Shuffle(current, enabled);
            Assert.Contains(next, enabled);
            Assert.NotEqual(current, next);
            current = next;
        }
    }

    [Fact]
    public void Shuffle_with_two_items_alternates()
    {
        int[] enabled = [1, 4];
        var rotation = new Rotation(new Random(5));
        int current = 1;
        for (int i = 0; i < 20; i++)
        {
            int next = rotation.Shuffle(current, enabled);
            Assert.Equal(current == 1 ? 4 : 1, next);
            current = next;
        }
    }

    [Fact]
    public void Shuffle_with_one_enabled_item_returns_it()
    {
        var rotation = new Rotation(new Random(1));
        Assert.Equal(2, rotation.Shuffle(2, new[] { 2 }));
        Assert.Equal(2, rotation.Shuffle(5, new[] { 2 }));
    }

    [Fact]
    public void Shuffle_with_nothing_enabled_returns_current()
    {
        Assert.Equal(3, new Rotation(new Random(1)).Shuffle(3, Array.Empty<int>()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Shuffle_never_returns_an_item_after_it_is_disabled_mid_bag(int seed)
    {
        var enabled = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
        var rotation = new Rotation(new Random(seed));
        int current = 0;

        // Start a bag, then switch off two items that may still be waiting in it.
        current = rotation.Shuffle(current, enabled);
        current = rotation.Shuffle(current, enabled);
        int[] removed = enabled.Where(i => i != current).Take(2).ToArray();
        foreach (int r in removed) enabled.Remove(r);

        for (int i = 0; i < 100; i++)
        {
            current = rotation.Shuffle(current, enabled);
            Assert.DoesNotContain(current, removed);
            Assert.Contains(current, enabled);
        }
    }
}
