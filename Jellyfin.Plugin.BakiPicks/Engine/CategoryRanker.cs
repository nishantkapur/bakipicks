using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.BakiPicks.Configuration;
using Jellyfin.Plugin.BakiPicks.Data;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BakiPicks.Engine;

public class CategoryRanker
{
    private readonly AffinityScorer _scorer;
    private readonly ILogger<CategoryRanker> _logger;

    public CategoryRanker(AffinityScorer scorer, ILogger<CategoryRanker> logger)
    {
        _scorer = scorer;
        _logger = logger;
    }

    public List<CategoryDefinition> SelectTopK(
        List<CategoryCandidate> candidates,
        IReadOnlyDictionary<Guid, BaseItem> itemsById,
        IReadOnlyDictionary<string, double> affinity,
        IReadOnlyDictionary<Guid, UserItemData> watchedById,
        PluginConfiguration config)
    {
        foreach (var c in candidates)
        {
            c.Score = ComputeRawScore(c, itemsById, affinity, watchedById);
        }

        // De-duplicate by id (keep highest-scoring). Affinity/taxonomy/exploration sources
        // can collide on slug-based ids; ToDictionary below would throw on duplicates.
        var deduped = candidates
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .ToList();
        if (deduped.Count < candidates.Count)
        {
            _logger.LogWarning("Dropped {N} duplicate candidate ids before ranking",
                candidates.Count - deduped.Count);
        }

        var remaining = new List<CategoryCandidate>(deduped);
        var penalties = remaining.ToDictionary(c => c.Id, _ => 1.0);
        var selected = new List<CategoryCandidate>();

        while (selected.Count < config.MaxCategories && remaining.Count > 0)
        {
            CategoryCandidate? best = null;
            double bestAdjusted = double.MinValue;
            foreach (var c in remaining)
            {
                double adjusted = c.Score * penalties[c.Id];
                if (adjusted > bestAdjusted)
                {
                    bestAdjusted = adjusted;
                    best = c;
                }
            }
            if (best == null || bestAdjusted <= 0)
            {
                break;
            }

            selected.Add(best);
            remaining.Remove(best);

            foreach (var c in remaining)
            {
                double overlap = Jaccard(c.MatchedItemIds, best.MatchedItemIds);
                penalties[c.Id] *= (1.0 - config.DiversityCoefficient * overlap);
            }
        }

        var defs = new List<CategoryDefinition>();
        for (int i = 0; i < selected.Count; i++)
        {
            var c = selected[i];
            defs.Add(new CategoryDefinition
            {
                Id = c.Id,
                Title = c.Title,
                Filter = c.Filter,
                SortBy = c.SortBy,
                Source = c.Source,
                Score = c.Score,
                Rank = i,
                ItemCount = c.MatchedItemIds.Count,
                MatchedItemIds = c.MatchedItemIds.ToList()
            });
            _logger.LogInformation(
                "  #{Rank} [{Source}] {Title} — score={Score:F2}, items={Count}",
                i, c.Source, c.Title, c.Score, c.MatchedItemIds.Count);
        }

        _logger.LogInformation("Selected {Count} categories (cap {Cap})", defs.Count, config.MaxCategories);
        return defs;
    }

    private double ComputeRawScore(
        CategoryCandidate c,
        IReadOnlyDictionary<Guid, BaseItem> itemsById,
        IReadOnlyDictionary<string, double> affinity,
        IReadOnlyDictionary<Guid, UserItemData> watchedById)
    {
        if (c.MatchedItemIds.Count == 0)
        {
            return 0.1;
        }

        var predictions = c.MatchedItemIds
            .Where(id => itemsById.ContainsKey(id))
            .Select(id => _scorer.PredictScore(itemsById[id], affinity))
            .OrderByDescending(p => p)
            .Take(20)
            .ToList();

        if (predictions.Count == 0)
        {
            return 0.1;
        }

        double avg = predictions.Average();
        double sizeFactor = Math.Log10(Math.Max(c.MatchedItemIds.Count, 1)) + 0.5;

        int unwatched = c.MatchedItemIds.Count(id =>
            !watchedById.TryGetValue(id, out var ud) || !ud.Played);
        double novelty = (double)unwatched / Math.Max(c.MatchedItemIds.Count, 1);

        // Bias toward non-negative; even neutral affinity should produce some signal
        double sourceBoost = c.Source switch
        {
            "affinity" => 1.2,
            "exploration" => 0.7,
            _ => 1.0
        };

        return (avg + 0.5) * sizeFactor * (0.5 + 0.5 * novelty) * sourceBoost;
    }

    private static double Jaccard(HashSet<Guid> a, HashSet<Guid> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }
        int inter = 0;
        foreach (var x in a)
        {
            if (b.Contains(x)) { inter++; }
        }
        int union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }
}
