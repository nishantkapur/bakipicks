using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.BakiPicks.Api.Dto;

public class CategoryListDto
{
    public DateTime LastBuiltAt { get; set; }

    public List<CategoryDto> Categories { get; set; } = new();
}
