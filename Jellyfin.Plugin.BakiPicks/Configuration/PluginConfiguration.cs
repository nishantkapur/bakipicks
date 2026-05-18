using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BakiPicks.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string TargetUserId { get; set; } = string.Empty;

    public int MaxCategories { get; set; } = 8;

    public int MinItemsPerCategory { get; set; } = 15;

    public bool IncludeExploration { get; set; } = true;

    public double DiversityCoefficient { get; set; } = 0.5;

    public double FullWatchWeight { get; set; } = 1.0;

    public double PartialWatchWeight { get; set; } = 0.5;

    public double LightWatchWeight { get; set; } = -0.3;

    public double SkipWeight { get; set; } = -0.6;

    public double FavoriteWeight { get; set; } = 1.5;

    public double ReplayMultiplier { get; set; } = 1.5;

    public int SmoothingMinSamples { get; set; } = 5;

    public int TopGenresForCombo { get; set; } = 3;

    public int TopDecadesForCombo { get; set; } = 2;

    public bool VerboseLogging { get; set; } = false;
}
