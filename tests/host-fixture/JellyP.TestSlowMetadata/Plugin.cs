using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
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
        RegisterImageSaveBarrier(serviceCollection);
    }

    /// <summary>
    /// Replaces the host library monitor with a pass-through decorator. The
    /// decorator can block one real image save after its bytes are written, which
    /// gives the host gate a deterministic post-save/pre-commit interruption
    /// window without a product fault endpoint. With no barrier file armed it
    /// only forwards calls.
    /// </summary>
    private static void RegisterImageSaveBarrier(IServiceCollection serviceCollection)
    {
        var registration = serviceCollection.LastOrDefault(
            descriptor => descriptor.ServiceType == typeof(ILibraryMonitor) && descriptor.ImplementationType is not null);
        if (registration?.ImplementationType is null)
        {
            throw new InvalidOperationException("Jellyfin ILibraryMonitor registration is unavailable.");
        }

        serviceCollection.AddSingleton<ILibraryMonitor>(provider =>
        {
            var inner = (ILibraryMonitor)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType);
            var logger = provider.GetRequiredService<ILogger<ImageSaveBarrierLibraryMonitor>>();
            return new ImageSaveBarrierLibraryMonitor(inner, logger);
        });
    }
}

/// <summary>
/// Test-only pass-through library monitor. When JELLYP_TEST_IMAGE_BARRIER names a
/// file whose lines are image folders, a completion for a file in one of those
/// folders writes a reached marker and waits for a release file. The real host
/// monitor still performs every operation first.
/// </summary>
public sealed class ImageSaveBarrierLibraryMonitor(ILibraryMonitor inner, ILogger<ImageSaveBarrierLibraryMonitor> logger) : ILibraryMonitor
{
    private const string BarrierVariable = "JELLYP_TEST_IMAGE_BARRIER";

    public void Start() => inner.Start();

    public void Stop() => inner.Stop();

    public void ReportFileSystemChangeBeginning(string path) => inner.ReportFileSystemChangeBeginning(path);

    public void ReportFileSystemChangeComplete(string path, bool refreshPath)
    {
        inner.ReportFileSystemChangeComplete(path, refreshPath);
        WaitIfArmed(path, logger);
    }

    public void ReportFileSystemChanged(string path) => inner.ReportFileSystemChanged(path);

    private static void WaitIfArmed(string path, ILogger logger)
    {
        var barrierFile = Environment.GetEnvironmentVariable(BarrierVariable);
        if (string.IsNullOrWhiteSpace(barrierFile) || !File.Exists(barrierFile))
        {
            return;
        }

        string[] armed;
        try
        {
            armed = File.ReadAllLines(barrierFile);
        }
        catch (IOException)
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        var matches = armed.Any(line =>
        {
            var candidate = line.Trim();
            if (candidate.Length == 0)
            {
                return false;
            }

            var fullCandidate = Path.GetFullPath(candidate);
            return string.Equals(fullPath, fullCandidate, StringComparison.Ordinal)
                || string.Equals(parent, fullCandidate, StringComparison.Ordinal);
        });
        if (!matches)
        {
            return;
        }

        var reached = barrierFile + ".reached";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.Read,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            var temporary = reached + ".tmp";
            using (var stream = new FileStream(temporary, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine(fullPath);
            }

            File.Move(temporary, reached, true);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "JellyP test image barrier could not record its reached path.");
            return;
        }

        logger.LogWarning("JellyP test image barrier reached for {Path}; waiting for release.", fullPath);
        var release = barrierFile + ".release";
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(release))
            {
                break;
            }

            Thread.Sleep(20);
        }

        logger.LogWarning("JellyP test image barrier released for {Path}.", fullPath);
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
