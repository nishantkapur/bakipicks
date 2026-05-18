namespace Jellyfin.Plugin.BakiPicks.Api.Dto;

public class CategoryDto
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Rank { get; set; }

    public int ItemCount { get; set; }

    public string Source { get; set; } = string.Empty;
}
