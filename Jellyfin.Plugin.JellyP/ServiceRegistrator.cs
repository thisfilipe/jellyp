using Jellyfin.Plugin.JellyP.Infrastructure;
using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyP;

/// <summary>Registers a shared coordinator for scheduled tasks and administration.</summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IArtworkStore, JellyfinArtworkStore>();
        serviceCollection.AddSingleton(services =>
        {
            var plugin = Plugin.Instance ?? throw new InvalidOperationException("JellyP is not loaded.");
            return new OptimizationCoordinator(
                services.GetRequiredService<IArtworkStore>(),
                () => plugin.Configuration,
                Path.Combine(plugin.DataFolderPath, "migration"),
                services.GetRequiredService<ILogger<OptimizationCoordinator>>(),
                configuration => plugin.UpdateConfiguration(configuration));
        });
    }
}
