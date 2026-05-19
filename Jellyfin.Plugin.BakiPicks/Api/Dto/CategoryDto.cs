using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.BakiPicks.Api.Dto;

public class CategoryDto
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Rank { get; set; }

    public int ItemCount { get; set; }

    public string Source { get; set; } = string.Empty;

    /// <summary>Sort hint for clients: Random | CommunityRating | DateCreated | PremiereDate | SortName.</summary>
    public string SortBy { get; set; } = "Random";

    /// <summary>
    /// Library item ids in this category, in rebuild-time order.
    /// Clients can pass these directly to Jellyfin's standard
    /// <c>GET /Items?Ids=...</c> endpoint to avoid an extra round-trip.
    /// </summary>
    public List<Guid> MatchedItemIds { get; set; } = new();
}
