using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BakiPicks.Engine;

public class LibraryProfiler
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryProfiler> _logger;

    public LibraryProfiler(ILibraryManager libraryManager, ILogger<LibraryProfiler> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public Dictionary<string, double> ExtractFeatures(BaseItem item)
    {
        var features = new Dictionary<string, double>();

        foreach (var g in item.Genres ?? Array.Empty<string>())
        {
            features[$"genre:{g}"] = 1.0;
        }

        foreach (var t in item.Tags ?? Array.Empty<string>())
        {
            features[$"tag:{NormalizeTag(t)}"] = 1.0;
        }

        foreach (var s in item.Studios ?? Array.Empty<string>())
        {
            features[$"studio:{s}"] = 1.0;
        }

        if (item.ProductionYear.HasValue)
        {
            int decade = (item.ProductionYear.Value / 10) * 10;
            features[$"decade:{decade}"] = 1.0;
        }

        if (item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0)
        {
            long minutes = item.RunTimeTicks.Value / TimeSpan.TicksPerMinute;
            string bucket = minutes switch
            {
                < 60 => "short",
                < 100 => "medium",
                < 150 => "long",
                _ => "epic"
            };
            features[$"runtime:{bucket}"] = 1.0;
        }

        if (item.CommunityRating.HasValue)
        {
            float rating = item.CommunityRating.Value;
            string bucket = rating switch
            {
                >= 8.0f => "high",
                >= 6.5f => "good",
                >= 5.0f => "average",
                _ => "low"
            };
            features[$"rating:{bucket}"] = 1.0;
        }

        // People: 1 director + top 5 actors. Keyed by normalized name —
        // PersonInfo carries Name/Type, not a stable person Id, in 10.10.
        try
        {
            var people = _libraryManager.GetPeople(new InternalPeopleQuery { ItemId = item.Id });
            int actorCount = 0;
            foreach (var p in people)
            {
                var key = NormalizePersonName(p.Name);
                if (key == null)
                {
                    continue;
                }
                if (p.Type == PersonKind.Director)
                {
                    features[$"person:{key}"] = 1.0;
                }
                else if (p.Type == PersonKind.Actor && actorCount < 5)
                {
                    features[$"person:{key}"] = 1.0;
                    actorCount++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetPeople failed for {ItemId}; skipping person features", item.Id);
        }

        return features;
    }

    private static string? NormalizePersonName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var lower = name.Trim().ToLowerInvariant();
        var chars = new char[lower.Length];
        int j = 0;
        bool prevDash = false;
        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars[j++] = c;
                prevDash = false;
            }
            else if (!prevDash)
            {
                chars[j++] = '-';
                prevDash = true;
            }
        }
        return new string(chars, 0, j).Trim('-');
    }

    private static string NormalizeTag(string tag) => tag.ToLowerInvariant().Trim();
}
