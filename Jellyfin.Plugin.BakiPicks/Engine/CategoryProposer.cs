using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.BakiPicks.Configuration;
using Jellyfin.Plugin.BakiPicks.Data;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BakiPicks.Engine;

public class CategoryProposer
{
    private readonly StateStore _store;
    private readonly LibraryProfiler _profiler;
    private readonly ILogger<CategoryProposer> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CategoryProposer(StateStore store, LibraryProfiler profiler, ILogger<CategoryProposer> logger)
    {
        _store = store;
        _profiler = profiler;
        _logger = logger;
    }

    public List<CategoryCandidate> Propose(
        IReadOnlyList<BaseItem> library,
        IReadOnlyDictionary<string, double> affinity,
        IReadOnlyDictionary<Guid, UserItemData> watchedById,
        PluginConfiguration config)
    {
        var candidates = new List<CategoryCandidate>();
        var libraryFeatures = BuildLibraryFeatureMap(library);

        // Source A — taxonomy seeds
        int taxonomyCount = 0;
        foreach (var seed in LoadSeeds())
        {
            var matches = MatchFilter(library, seed.Filter, watchedById);
            if (matches.Count < config.MinItemsPerCategory)
            {
                continue;
            }
            candidates.Add(new CategoryCandidate
            {
                Id = seed.Id,
                Title = seed.Title,
                Filter = seed.Filter,
                SortBy = seed.SortBy,
                Source = "taxonomy",
                MatchedItemIds = matches.Select(m => m.Id).ToHashSet()
            });
            taxonomyCount++;
        }

        // Source B — affinity-driven combos
        int affinityCount = 0;
        foreach (var cand in BuildAffinityCombos(library, affinity, watchedById, config))
        {
            candidates.Add(cand);
            affinityCount++;
        }

        // Source C — exploration
        int explorationCount = 0;
        if (config.IncludeExploration)
        {
            foreach (var cand in BuildExplorationCandidates(library, affinity, libraryFeatures, watchedById, config))
            {
                candidates.Add(cand);
                explorationCount++;
            }
        }

        _logger.LogInformation(
            "Proposed {Total} candidates ({Taxonomy} taxonomy, {Affinity} affinity, {Exploration} exploration)",
            candidates.Count, taxonomyCount, affinityCount, explorationCount);
        return candidates;
    }

    public List<TaxonomySeed> LoadSeeds()
    {
        var seedPath = _store.SeedsPath;
        if (!File.Exists(seedPath))
        {
            CopyEmbeddedSeeds(seedPath);
        }
        List<TaxonomySeed> seeds;
        try
        {
            var json = File.ReadAllText(seedPath);
            seeds = JsonSerializer.Deserialize<List<TaxonomySeed>>(json, JsonOpts) ?? new List<TaxonomySeed>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read taxonomy_seeds.json — using empty list");
            return new List<TaxonomySeed>();
        }

        // De-duplicate by id (keep first); warn on collisions
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<TaxonomySeed>(seeds.Count);
        foreach (var s in seeds)
        {
            if (string.IsNullOrWhiteSpace(s.Id))
            {
                _logger.LogWarning("Seed with empty id skipped (title: {Title})", s.Title);
                continue;
            }
            if (!seen.Add(s.Id))
            {
                _logger.LogWarning("Duplicate seed id '{Id}' — keeping first occurrence", s.Id);
                continue;
            }
            unique.Add(s);
        }
        return unique;
    }

    private void CopyEmbeddedSeeds(string destPath)
    {
        var asm = typeof(CategoryProposer).Assembly;
        const string resourceName = "Jellyfin.Plugin.BakiPicks.Resources.taxonomy_seeds.json";
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _logger.LogWarning("Embedded taxonomy_seeds.json not found");
            return;
        }
        using var fs = File.Create(destPath);
        stream.CopyTo(fs);
        _logger.LogInformation("Copied embedded taxonomy_seeds.json to {Path}", destPath);
    }

    private Dictionary<string, int> BuildLibraryFeatureMap(IReadOnlyList<BaseItem> library)
    {
        var map = new Dictionary<string, int>();
        foreach (var item in library)
        {
            foreach (var f in _profiler.ExtractFeatures(item).Keys)
            {
                map.TryGetValue(f, out var c);
                map[f] = c + 1;
            }
        }
        return map;
    }

    private List<BaseItem> MatchFilter(
        IReadOnlyList<BaseItem> library,
        CategoryFilter filter,
        IReadOnlyDictionary<Guid, UserItemData> watchedById)
    {
        var result = new List<BaseItem>();
        foreach (var item in library)
        {
            if (Matches(item, filter, watchedById))
            {
                result.Add(item);
            }
        }
        return result;
    }

    private static bool Matches(BaseItem item, CategoryFilter f, IReadOnlyDictionary<Guid, UserItemData> watchedById)
    {
        if (f.Genres.Count > 0)
        {
            var itemGenres = new HashSet<string>(item.Genres ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var g in f.Genres)
            {
                if (!itemGenres.Contains(g))
                {
                    return false;
                }
            }
        }
        if (f.Tags.Count > 0)
        {
            var itemTags = new HashSet<string>(item.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            bool any = false;
            foreach (var t in f.Tags)
            {
                if (itemTags.Contains(t)) { any = true; break; }
            }
            if (!any)
            {
                return false;
            }
        }
        if (f.Studios.Count > 0)
        {
            var itemStudios = new HashSet<string>(item.Studios ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            bool any = false;
            foreach (var s in f.Studios)
            {
                if (itemStudios.Contains(s)) { any = true; break; }
            }
            if (!any)
            {
                return false;
            }
        }
        if (f.YearMin.HasValue && (item.ProductionYear ?? 0) < f.YearMin.Value)
        {
            return false;
        }
        if (f.YearMax.HasValue && (item.ProductionYear ?? 9999) > f.YearMax.Value)
        {
            return false;
        }
        if (f.RuntimeMinutesMin.HasValue)
        {
            long min = (item.RunTimeTicks ?? 0) / TimeSpan.TicksPerMinute;
            if (min < f.RuntimeMinutesMin.Value)
            {
                return false;
            }
        }
        if (f.RuntimeMinutesMax.HasValue)
        {
            long min = (item.RunTimeTicks ?? long.MaxValue) / TimeSpan.TicksPerMinute;
            if (min > f.RuntimeMinutesMax.Value)
            {
                return false;
            }
        }
        if (f.CommunityRatingMin.HasValue && (item.CommunityRating ?? 0f) < f.CommunityRatingMin.Value)
        {
            return false;
        }
        if (f.UnplayedOnly == true)
        {
            if (watchedById.TryGetValue(item.Id, out var ud) && ud.Played)
            {
                return false;
            }
        }
        return true;
    }

    private IEnumerable<CategoryCandidate> BuildAffinityCombos(
        IReadOnlyList<BaseItem> library,
        IReadOnlyDictionary<string, double> affinity,
        IReadOnlyDictionary<Guid, UserItemData> watchedById,
        PluginConfiguration config)
    {
        var topGenres = affinity
            .Where(kv => kv.Key.StartsWith("genre:", StringComparison.Ordinal) && kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(config.TopGenresForCombo)
            .Select(kv => kv.Key.Substring("genre:".Length))
            .ToList();

        var topDecades = affinity
            .Where(kv => kv.Key.StartsWith("decade:", StringComparison.Ordinal) && kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(config.TopDecadesForCombo)
            .Select(kv => kv.Key.Substring("decade:".Length))
            .ToList();

        foreach (var genre in topGenres)
        {
            foreach (var decadeStr in topDecades)
            {
                if (!int.TryParse(decadeStr, out var decade))
                {
                    continue;
                }
                var filter = new CategoryFilter
                {
                    Genres = new List<string> { genre },
                    YearMin = decade,
                    YearMax = decade + 9
                };
                var matches = MatchFilter(library, filter, watchedById);
                if (matches.Count < config.MinItemsPerCategory)
                {
                    continue;
                }
                yield return new CategoryCandidate
                {
                    Id = $"affinity-{Slug(genre)}-{decade}s",
                    Title = $"{decade}s {genre} for You",
                    Filter = filter,
                    SortBy = "Random",
                    Source = "affinity",
                    MatchedItemIds = matches.Select(m => m.Id).ToHashSet()
                };
            }
        }

        // Top studios with positive affinity
        var topStudios = affinity
            .Where(kv => kv.Key.StartsWith("studio:", StringComparison.Ordinal) && kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(2)
            .Select(kv => kv.Key.Substring("studio:".Length));

        foreach (var studio in topStudios)
        {
            var filter = new CategoryFilter { Studios = new List<string> { studio } };
            var matches = MatchFilter(library, filter, watchedById);
            if (matches.Count < config.MinItemsPerCategory)
            {
                continue;
            }
            yield return new CategoryCandidate
            {
                Id = $"affinity-studio-{Slug(studio)}",
                Title = $"From {studio}",
                Filter = filter,
                SortBy = "Random",
                Source = "affinity",
                MatchedItemIds = matches.Select(m => m.Id).ToHashSet()
            };
        }
    }

    private IEnumerable<CategoryCandidate> BuildExplorationCandidates(
        IReadOnlyList<BaseItem> library,
        IReadOnlyDictionary<string, double> affinity,
        Dictionary<string, int> libraryFeatures,
        IReadOnlyDictionary<Guid, UserItemData> watchedById,
        PluginConfiguration config)
    {
        var lowAffinityGenres = libraryFeatures
            .Where(kv => kv.Key.StartsWith("genre:", StringComparison.Ordinal))
            .Where(kv => kv.Value >= config.MinItemsPerCategory)
            .Where(kv => !affinity.TryGetValue(kv.Key, out var a) || a < 0.1)
            .OrderByDescending(kv => kv.Value)
            .Take(2);

        foreach (var (key, _) in lowAffinityGenres)
        {
            var genre = key.Substring("genre:".Length);
            var filter = new CategoryFilter
            {
                Genres = new List<string> { genre },
                UnplayedOnly = true
            };
            var matches = MatchFilter(library, filter, watchedById);
            if (matches.Count < config.MinItemsPerCategory)
            {
                continue;
            }
            yield return new CategoryCandidate
            {
                Id = $"explore-{Slug(genre)}",
                Title = $"Worth Exploring: {genre}",
                Filter = filter,
                SortBy = "CommunityRating",
                Source = "exploration",
                MatchedItemIds = matches.Select(m => m.Id).ToHashSet()
            };
        }
    }

    private static string Slug(string s)
    {
        var lower = (s ?? string.Empty).ToLowerInvariant();
        var sb = new System.Text.StringBuilder(lower.Length);
        bool prevDash = false;
        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }
}

public class CategoryCandidate
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public CategoryFilter Filter { get; set; } = new();

    public string SortBy { get; set; } = "Random";

    public string Source { get; set; } = string.Empty;

    public HashSet<Guid> MatchedItemIds { get; set; } = new();

    public double Score { get; set; }
}
