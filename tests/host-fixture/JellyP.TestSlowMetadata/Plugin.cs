using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JellyP.TestSlowMetadata;

/// <summary>Test-only plugin used to hold a synthetic metadata refresh open in a disposable fixture.</summary>
public sealed class PluginConfiguration : BasePluginConfiguration;

/// <summary>Test-only plugin entry point. This assembly is not part of the JellyP product package.</summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "JellyP Test Slow Metadata";

    public override Guid Id => Guid.Parse("7f1f8f4e-6f0e-4a0e-9c1e-6b7d8e9f0a1b");

    public override string Description => "Test-only slow local metadata provider for a disposable Jellyfin fixture.";
}

/// <summary>Registers the test provider without changing JellyP or any production service.</summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ILocalMetadataProvider<Movie>, SlowMovieMetadataProvider>();
    }
}

/// <summary>Delays only fixture items whose path contains "Slow Refresh"; all other items return immediately.</summary>
public sealed class SlowMovieMetadataProvider(ILogger<SlowMovieMetadataProvider> logger) : ILocalMetadataProvider<Movie>
{
    public string Name => "JellyP Test Slow Metadata";

    public async Task<MetadataResult<Movie>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
    {
        if (info.Path?.Contains("Slow Refresh", StringComparison.OrdinalIgnoreCase) == true)
        {
            var seconds = 60;
            var controlFile = Environment.GetEnvironmentVariable("JELLYP_TEST_SLOW_FILE");
            if (!string.IsNullOrWhiteSpace(controlFile) && File.Exists(controlFile)
                && int.TryParse(File.ReadAllText(controlFile).Trim(), out var fromFile) && fromFile is >= 0 and <= 600)
            {
                seconds = fromFile;
            }
            else if (int.TryParse(Environment.GetEnvironmentVariable("JELLYP_TEST_SLOW_SECONDS"), out var configured) && configured is > 0 and <= 600)
            {
                seconds = configured;
            }

            if (seconds > 0)
            {
                var releaseFile = Environment.GetEnvironmentVariable("JELLYP_TEST_SLOW_RELEASE");
                var deadline = DateTime.UtcNow.AddSeconds(seconds);
                logger.LogInformation("JellyP test slow metadata delaying {Path} for up to {Seconds}s", info.Path, seconds);
                while (DateTime.UtcNow < deadline
                       && !(releaseFile is not null && File.Exists(releaseFile)))
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new MetadataResult<Movie> { HasMetadata = false };
    }
}
