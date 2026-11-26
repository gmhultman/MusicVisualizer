namespace MusicVisualizer.Services;

/// <summary>
/// Picks the next item (visualizer or palette) from those the user has enabled. Items are identified by their
/// index in the full list; <c>enabled</c> is the ascending list of indexes that are switched on.
/// </summary>
public sealed class Rotation
{
    private readonly Random _random;
    private readonly List<int> _bag = [];

    public Rotation(Random? random = null) => _random = random ?? Random.Shared;

    /// <summary>
    /// The enabled item after (<paramref name="direction"/> = 1) or before (-1) <paramref name="current"/>, wrapping
    /// around. If <paramref name="current"/> is itself disabled, the nearest enabled item in that direction is used.
    /// </summary>
    public static int Step(int current, IReadOnlyList<int> enabled, int direction)
    {
        if (enabled.Count == 0) return current;
        int pos = IndexOf(enabled, current);
        if (pos >= 0)
            return enabled[Mod(pos + Math.Sign(direction == 0 ? 1 : direction), enabled.Count)];

        if (direction >= 0)
        {
            foreach (int i in enabled) if (i > current) return i;
            return enabled[0];
        }
        for (int k = enabled.Count - 1; k >= 0; k--) if (enabled[k] < current) return enabled[k];
        return enabled[^1];
    }

    /// <summary>
    /// A random enabled item, drawn from a shuffle bag: every enabled item comes up once before any repeats,
    /// and never the same as <paramref name="current"/> (unless it's the only one).
    /// </summary>
    public int Shuffle(int current, IReadOnlyList<int> enabled)
    {
        if (enabled.Count == 0) return current;
        if (enabled.Count == 1) return enabled[0];

        _bag.RemoveAll(i => IndexOf(enabled, i) < 0);
        while (true)
        {
            if (_bag.Count == 0) Refill(current, enabled);
            int next = _bag[^1];
            _bag.RemoveAt(_bag.Count - 1);
            if (next != current) return next;
        }
    }

    private void Refill(int current, IReadOnlyList<int> enabled)
    {
        _bag.AddRange(enabled.Where(i => i != current));
        for (int i = _bag.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
        }
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }

    private static int Mod(int a, int n) => ((a % n) + n) % n;
}
