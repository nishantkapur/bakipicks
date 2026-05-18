using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.BakiPicks.Configuration;
using Jellyfin.Plugin.BakiPicks.Data;
using Jellyfin.Plugin.BakiPicks.Engine;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BakiPicks.ScheduledTasks;

public class RebuildCategoriesTask : IScheduledTask
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly StateStore _store;
    private readonly AffinityScorer _scorer;
    private readonly CategoryProposer _proposer;
    private readonly CategoryRanker _ranker;
    private readonly ILogger<RebuildCategoriesTask> _logger;

    public RebuildCategoriesTask(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        StateStore store,
        AffinityScorer scorer,
        CategoryProposer proposer,
        CategoryRanker ranker,
        ILogger<RebuildCategoriesTask> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _store = store;
        _scorer = scorer;
        _proposer = proposer;
        _ranker = ranker;
        _logger = logger;
    }

    public string Name => "Rebuild BakiPicks Categories";

    public string Description => "Analyses your library and watch history to regenerate Netflix-style category rows.";

    public string Category => "BakiPicks";

    public string Key => "BakiPicksRebuild";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var startedAt = DateTime.UtcNow;
        string stage = "init";

        try
        {
            if (!Guid.TryParse(config.TargetUserId, out var userGuid))
            {
                _logger.LogError("No TargetUserId configured. Open Dashboard → Plugins → BakiPicks and pick a user.");
                return Task.CompletedTask;
            }

            var user = _userManager.GetUserById(userGuid);
            if (user == null)
            {
                _logger.LogError("Target user {Id} not found.", config.TargetUserId);
                return Task.CompletedTask;
            }

            _logger.LogInformation("=== BakiPicks rebuild starting for user {Name} ===", user.Username);
            _logger.LogInformation("Config snapshot: MaxCategories={Max}, MinItems={Min}, Diversity={Div}, Exploration={Exp}",
                config.MaxCategories, config.MinItemsPerCategory, config.DiversityCoefficient, config.IncludeExploration);
            progress.Report(5);

            // Stage 1: Pull library
            stage = "library";
            var libraryQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true
            };
            var library = _libraryManager.GetItemList(libraryQuery);
            _logger.LogInformation("Library: {Count} items", library.Count);
            progress.Report(15);

            // Stage 2: Pull watch history
            stage = "history";
            var watched = new Dictionary<Guid, UserItemData>();
            var watchedItems = new List<(BaseItem Item, UserItemData UserData)>();
            foreach (var item in library)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ud = _userDataManager.GetUserData(user, item);
                if (ud == null)
                {
                    continue;
                }
                if (ud.Played || ud.PlaybackPositionTicks > 0 || ud.IsFavorite || ud.PlayCount > 0)
                {
                    watched[item.Id] = ud;
                    watchedItems.Add((item, ud));
                }
            }
            _logger.LogInformation("Watch history: {Count} items with signals", watchedItems.Count);
            progress.Report(30);

            // Stage 3: Affinity
            stage = "affinity";
            var affinity = _scorer.ComputeAffinity(watchedItems, config);
            LogTopAffinities(affinity);
            progress.Report(45);

            // Stage 4: Candidate proposal
            stage = "propose";
            var candidates = _proposer.Propose(library, affinity, watched, config);
            progress.Report(70);

            // Stage 5: Rank + select top K
            stage = "rank";
            var itemsById = library.ToDictionary(i => i.Id);
            var categories = _ranker.SelectTopK(candidates, itemsById, affinity, watched, config);
            progress.Report(90);

            // Stage 6: Persist
            stage = "persist";
            var state = new UserState
            {
                Version = 1,
                UserId = userGuid.ToString(),
                LibraryItemCount = library.Count,
                SignalCount = watchedItems.Count,
                Affinity = affinity,
                Categories = categories
            };
            _store.SaveState(state);
            progress.Report(100);

            var duration = DateTime.UtcNow - startedAt;
            _logger.LogInformation("=== BakiPicks rebuild complete in {Seconds:F1}s: {Count} categories written to {Path} ===",
                duration.TotalSeconds, categories.Count, _store.StatePath);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("BakiPicks rebuild cancelled at stage '{Stage}'", stage);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BakiPicks rebuild failed at stage '{Stage}' — state.json not updated", stage);
            throw;
        }

        return Task.CompletedTask;
    }

    private void LogTopAffinities(Dictionary<string, double> affinity)
    {
        if (affinity.Count == 0)
        {
            _logger.LogInformation("Affinity is empty (cold start — only taxonomy + exploration candidates will appear)");
            return;
        }

        var top = affinity.OrderByDescending(kv => kv.Value).Take(20).ToList();
        _logger.LogInformation("Top {Count} affinities:", top.Count);
        foreach (var (k, v) in top)
        {
            _logger.LogInformation("  {Feature} = {Score:F3}", k, v);
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        }
    };
}
