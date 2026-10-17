using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyP.Tasks;

/// <summary>Supported post-save integration for both existing and newly downloaded artwork.</summary>
public sealed class OptimizeArtworkTask(OptimizationCoordinator coordinator) : IScheduledTask
{
    /// <inheritdoc />
    public string Name => "Optimize library artwork";
    /// <inheritdoc />
    public string Key => "JellyPOptimizeArtwork";
    /// <inheritdoc />
    public string Description => "Process the selected libraries or items using saved automation settings and a resumable work limit. Applying waits for metadata activity; configure a schedule here.";
    /// <inheritdoc />
    public string Category => "JellyP";
    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => coordinator.RunAsync(progress, cancellationToken, waitForOtherOperations: true);
    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
