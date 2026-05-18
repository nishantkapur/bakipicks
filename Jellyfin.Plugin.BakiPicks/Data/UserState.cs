using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.BakiPicks.Data;

public class UserState
{
    public int Version { get; set; } = 1;

    public DateTime LastBuiltAt { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int LibraryItemCount { get; set; }

    public int SignalCount { get; set; }

    public Dictionary<string, double> Affinity { get; set; } = new();

    public List<CategoryDefinition> Categories { get; set; } = new();
}

public class CategoryDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Rank { get; set; }

    public double Score { get; set; }

    public int ItemCount { get; set; }

    /// <summary>"taxonomy" | "affinity" | "exploration".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Diagnostic record of the filter used at rebuild time. Not used at serve time.</summary>
    public CategoryFilter Filter { get; set; } = new();

    /// <summary>Sort key applied at serve time: Random | CommunityRating | DateCreated | PremiereDate | SortName.</summary>
    public string SortBy { get; set; } = "Random";

    /// <summary>
    /// The exact set of library item ids that matched the filter at rebuild time.
    /// The serve-side controller pages from this list to keep semantics stable
    /// and avoid divergence between proposer logic and Jellyfin's query engine.
    /// </summary>
    public List<Guid> MatchedItemIds { get; set; } = new();
}

public class CategoryFilter
{
    public List<string> Genres { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public List<string> Studios { get; set; } = new();

    /// <summary>Person IDs (directors / actors).</summary>
    public List<string> People { get; set; } = new();

    public int? YearMin { get; set; }

    public int? YearMax { get; set; }

    public int? RuntimeMinutesMin { get; set; }

    public int? RuntimeMinutesMax { get; set; }

    public double? CommunityRatingMin { get; set; }

    public bool? UnplayedOnly { get; set; }
}
