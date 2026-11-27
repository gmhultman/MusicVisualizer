namespace MusicVisualizer.Visuals;

/// <summary>The list of built-in visualizers, in display order.</summary>
public static class VisualizerCatalog
{
    /// <summary>
    /// Creates one of every visualizer. Keep the list grouped by <see cref="VisualizerCategory"/>, in enum order:
    /// number keys, cycling and every menu follow this order (a test checks it).
    /// </summary>
    public static IReadOnlyList<IVisualizer> CreateAll() =>
        [
            // Abstract
            new BarsVisualizer(), new RadialVisualizer(), new ParticlesVisualizer(), new SphereVisualizer(),
            // Nature
            new FireVisualizer(), new TerrainVisualizer(),
            // Scenes
            new FireplaceVisualizer(), new HighwayVisualizer(), new DanceDisasterVisualizer(),
            new AquariumVisualizer(), new SpaceVisualizer(),
        ];

    /// <summary>Groups visualizer indexes by category, in category order; categories with no indexes are left out.</summary>
    public static IEnumerable<IGrouping<VisualizerCategory, int>> GroupByCategory(IReadOnlyList<IVisualizer> visualizers, IEnumerable<int> indexes) =>
        indexes.GroupBy(i => visualizers[i].Category).OrderBy(g => g.Key);
}
