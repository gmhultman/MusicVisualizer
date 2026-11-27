using MusicVisualizer.Visuals;

namespace MusicVisualizer.Tests;

public class VisualizerCatalogTests
{
    private static readonly IReadOnlyList<IVisualizer> All = VisualizerCatalog.CreateAll();

    [Fact]
    public void Catalog_is_sorted_by_category_so_menus_keys_and_cycling_agree()
    {
        var categories = All.Select(v => v.Category).ToList();
        Assert.Equal(categories.Order(), categories);
    }

    [Fact]
    public void Names_are_unique_because_settings_store_visualizers_by_name()
    {
        var names = All.Select(v => v.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GroupByCategory_keeps_category_order_and_skips_empty_categories()
    {
        // Only the last visualizer and the first one are enabled, passed in reverse order.
        int last = All.Count - 1;
        var groups = VisualizerCatalog.GroupByCategory(All, [last, 0]).ToList();

        Assert.Equal(All[0].Category, groups[0].Key);
        Assert.Equal(All[last].Category, groups[^1].Key);
        Assert.All(groups, g => Assert.NotEmpty(g));
        Assert.Equal(2, groups.Sum(g => g.Count()));
    }
}
