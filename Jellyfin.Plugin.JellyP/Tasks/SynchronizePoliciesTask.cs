using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyP.Tasks;

/// <summary>Reapplies current category policies using retained originals instead of lossy derivatives.</summary>
public sealed class SynchronizePoliciesTask(OptimizationCoordinator coordinator) : IScheduledTask
{
    /// <inheritdoc />
    public string Name => "Synchronize artwork policies";
    /// <inheritdoc />
    public string Key => "JellyPSynchronizePolicies";
    /// <inheritdoc />
    public string Description => "Use saved automation settings to synchronize selected artwork from preserved originals. Applying waits for metadata activity. Configure a schedule here.";
    /// <inheritdoc />
    public string Category => "JellyP";
    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => coordinator.RunAsync(progress, cancellationToken, synchronize: true, waitForOtherOperations: true);
    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
