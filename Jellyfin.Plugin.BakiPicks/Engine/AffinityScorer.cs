using System;
using System.Collections.Generic;
using Jellyfin.Plugin.BakiPicks.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BakiPicks.Engine;

public class AffinityScorer
{
    private readonly LibraryProfiler _profiler;
    private readonly ILogger<AffinityScorer> _logger;

    public AffinityScorer(LibraryProfiler profiler, ILogger<AffinityScorer> logger)
    {
        _profiler = profiler;
        _logger = logger;
    }

    public Dictionary<string, double> ComputeAffinity(
        IEnumerable<(BaseItem Item, UserItemData UserData)> watchedItems,
        PluginConfiguration config)
    {
        var featureSums = new Dictionary<string, double>();
        var featureCounts = new Dictionary<string, int>();
        int totalSignals = 0;

        foreach (var (item, userData) in watchedItems)
        {
            double signal = ComputeSignal(item, userData, config);
            if (signal == 0)
            {
                continue;
            }
            totalSignals++;

            var features = _profiler.ExtractFeatures(item);
            foreach (var f in features.Keys)
            {
                featureSums.TryGetValue(f, out var sum);
                featureSums[f] = sum + signal;

                featureCounts.TryGetValue(f, out var cnt);
                featureCounts[f] = cnt + 1;
            }
        }

        var affinity = new Dictionary<string, double>();
        foreach (var f in featureSums.Keys)
        {
            int denom = Math.Max(featureCounts[f], config.SmoothingMinSamples);
            affinity[f] = featureSums[f] / denom;
        }

        _logger.LogInformation("Affinity computed: {Features} features from {Signals} signals", affinity.Count, totalSignals);
        return affinity;
    }

    private double ComputeSignal(BaseItem item, UserItemData userData, PluginConfiguration config)
    {
        double signal = 0;
        long runtime = item.RunTimeTicks ?? 0;

        if (runtime > 0 && userData.PlaybackPositionTicks > 0)
        {
            double pct = (double)userData.PlaybackPositionTicks / runtime;
            if (userData.Played)
            {
                signal += config.FullWatchWeight;
            }
            else if (pct >= 0.30)
            {
                signal += config.PartialWatchWeight;
            }
            else if (pct >= 0.05)
            {
                signal += config.LightWatchWeight;
            }
            else
            {
                signal += config.SkipWeight;
            }
        }
        else if (userData.Played)
        {
            signal += config.FullWatchWeight;
        }

        if (userData.IsFavorite)
        {
            signal += config.FavoriteWeight;
        }

        if (userData.PlayCount > 1 && signal > 0)
        {
            signal *= config.ReplayMultiplier;
        }

        return signal;
    }

    /// <summary>
    /// Predicts a relevance score for an arbitrary library item by summing matched feature affinities.
    /// </summary>
    public double PredictScore(BaseItem item, IReadOnlyDictionary<string, double> affinity)
    {
        var features = _profiler.ExtractFeatures(item);
        double score = 0;
        foreach (var f in features.Keys)
        {
            if (affinity.TryGetValue(f, out var a))
            {
                score += a;
            }
        }
        return score;
    }
}
