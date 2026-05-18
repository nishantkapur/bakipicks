using Jellyfin.Plugin.BakiPicks.Data;
using Jellyfin.Plugin.BakiPicks.Engine;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BakiPicks;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<StateStore>();
        serviceCollection.AddSingleton<LibraryProfiler>();
        serviceCollection.AddSingleton<AffinityScorer>();
        serviceCollection.AddSingleton<CategoryProposer>();
        serviceCollection.AddSingleton<CategoryRanker>();
    }
}
