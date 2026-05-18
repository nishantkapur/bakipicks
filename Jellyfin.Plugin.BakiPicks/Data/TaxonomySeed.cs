namespace Jellyfin.Plugin.BakiPicks.Data;

public class TaxonomySeed
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public CategoryFilter Filter { get; set; } = new();

    public string SortBy { get; set; } = "Random";
}
