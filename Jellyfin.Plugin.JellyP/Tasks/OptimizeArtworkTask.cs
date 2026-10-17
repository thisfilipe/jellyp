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
    public string Description => "Preview or apply the next resumable WebP batch. Configure JellyP first.";
    /// <inheritdoc />
    public string Category => "JellyP";
    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => coordinator.RunAsync(progress, cancellationToken);
    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
