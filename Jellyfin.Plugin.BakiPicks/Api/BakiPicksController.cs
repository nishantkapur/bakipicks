using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.BakiPicks.Api.Dto;
using Jellyfin.Plugin.BakiPicks.Data;
using Jellyfin.Plugin.BakiPicks.ScheduledTasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BakiPicks.Api;

[ApiController]
[Authorize]
[Route("BakiPicks")]
[Produces("application/json")]
public class BakiPicksController : ControllerBase
{
    private readonly StateStore _store;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IDtoService _dtoService;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<BakiPicksController> _logger;

    public BakiPicksController(
        StateStore store,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IDtoService dtoService,
        ITaskManager taskManager,
        ILogger<BakiPicksController> logger)
    {
        _store = store;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _dtoService = dtoService;
        _taskManager = taskManager;
        _logger = logger;
    }

    [HttpGet("Categories")]
    public ActionResult<CategoryListDto> GetCategories()
    {
        var state = _store.LoadState();
        var dto = new CategoryListDto
        {
            LastBuiltAt = state.LastBuiltAt,
            Categories = state.Categories
                .OrderBy(c => c.Rank)
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Rank = c.Rank,
                    ItemCount = c.ItemCount,
                    Source = c.Source,
                    SortBy = c.SortBy,
                    MatchedItemIds = c.MatchedItemIds.ToList()
                })
                .ToList()
        };
        return dto;
    }

    [HttpGet("Categories/{id}/Items")]
    public ActionResult<QueryResult<BaseItemDto>> GetCategoryItems(
        [FromRoute] string id,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit)
    {
        var state = _store.LoadState();
        var category = state.Categories.FirstOrDefault(c => c.Id == id);
        if (category == null)
        {
            return NotFound();
        }

        if (!Guid.TryParse(state.UserId, out var userGuid))
        {
            return BadRequest("Target user not configured.");
        }
        var user = _userManager.GetUserById(userGuid);
        if (user == null)
        {
            return NotFound("Target user not found.");
        }

        int reqStart = startIndex ?? 0;
        int reqLimit = limit ?? 20;

        // Resolve live BaseItems from the persisted id list. Items removed from
        // the library between rebuild and now drop out cleanly via the null filter.
        var items = new List<BaseItem>(category.MatchedItemIds.Count);
        foreach (var itemId in category.MatchedItemIds)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item != null)
            {
                items.Add(item);
            }
        }

        // Apply UnplayedOnly at serve time so the row updates as the user watches things.
        if (category.Filter.UnplayedOnly == true)
        {
            items = items.Where(i =>
            {
                var ud = _userDataManager.GetUserData(user, i);
                return ud == null || !ud.Played;
            }).ToList();
        }

        items = SortItems(items, category.SortBy, category.Id);

        var paged = items.Skip(reqStart).Take(reqLimit).ToList();
        var options = new DtoOptions(true);
        var dtos = paged
            .Select(i => _dtoService.GetBaseItemDto(i, options, user))
            .ToArray();

        return new QueryResult<BaseItemDto>
        {
            TotalRecordCount = items.Count,
            StartIndex = reqStart,
            Items = dtos
        };
    }

    private static List<BaseItem> SortItems(List<BaseItem> items, string sortBy, string categoryId)
    {
        return sortBy switch
        {
            "CommunityRating" => items.OrderByDescending(i => i.CommunityRating ?? 0f).ToList(),
            "DateCreated"     => items.OrderByDescending(i => i.DateCreated).ToList(),
            "PremiereDate"    => items.OrderByDescending(i => i.PremiereDate ?? DateTime.MinValue).ToList(),
            "SortName"        => items.OrderBy(i => i.SortName ?? i.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),
            _                 => ShuffleStable(items, categoryId)
        };
    }

    /// <summary>
    /// Deterministic shuffle seeded by (category id + UTC date) so paged requests
    /// within the same day return a consistent order, but the row "feels fresh"
    /// across days.
    /// </summary>
    private static List<BaseItem> ShuffleStable(List<BaseItem> items, string categoryId)
    {
        unchecked
        {
            int seed = (categoryId?.GetHashCode(StringComparison.Ordinal) ?? 0)
                       ^ DateTime.UtcNow.Date.GetHashCode();
            var rng = new Random(seed);
            return items.OrderBy(_ => rng.Next()).ToList();
        }
    }

    /// <summary>Reset taxonomy_seeds.json on disk to the embedded default. Admin only.</summary>
    [HttpPost("Seeds/Reset")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult ResetSeeds()
    {
        var path = _store.SeedsPath;
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
            _logger.LogInformation("Deleted taxonomy_seeds.json — will be re-extracted on next rebuild");
        }
        return NoContent();
    }

    [HttpPost("Rebuild")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult Rebuild()
    {
        _logger.LogInformation("Manual rebuild requested via API");
        _taskManager.CancelIfRunningAndQueue<RebuildCategoriesTask>();
        return NoContent();
    }
}
