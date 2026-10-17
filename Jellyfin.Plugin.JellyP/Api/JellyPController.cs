using Jellyfin.Plugin.JellyP.Migration;
using Jellyfin.Plugin.JellyP.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyP.Api;

/// <summary>Administrator-only plugin operations. No endpoint accepts filesystem paths.</summary>
[ApiController]
[Route("JellyP")]
[Authorize(Policy = "RequiresElevation")]
public sealed class JellyPController(OptimizationCoordinator coordinator, ITaskManager tasks) : ControllerBase
{
    /// <summary>Gets progress, storage accounting and the last hundred transactions.</summary>
    [HttpGet("Status")]
    public ActionResult<OptimizerStatus> Status() => coordinator.GetStatus();

    /// <summary>Queues the configured batch in Jellyfin's task manager.</summary>
    [HttpPost("Run")]
    public ActionResult Run([FromQuery] bool? dryRun = null) => Queue<OptimizeArtworkTask>(false, dryRun);

    /// <summary>Requests cancellation after the current image transaction.</summary>
    [HttpPost("Cancel")]
    public ActionResult Cancel()
    {
        coordinator.CancelQueuedExecution();
        tasks.CancelIfRunning<OptimizeArtworkTask>();
        tasks.CancelIfRunning<SynchronizePoliciesTask>();
        return Accepted();
    }

    /// <summary>Queues policy synchronization from preserved reference originals.</summary>
    [HttpPost("Sync")]
    public ActionResult Sync([FromQuery] bool? dryRun = null) => Queue<SynchronizePoliciesTask>(true, dryRun);

    private ActionResult Queue<T>(bool synchronize, bool? dryRun) where T : IScheduledTask
    {
        var task = tasks.ScheduledTasks.FirstOrDefault(worker => worker.ScheduledTask is T);
        if (task is null) return Problem("JellyP scheduled task is unavailable. Restart Jellyfin and check plugin loading.", statusCode: 503);
        if (task.State != TaskState.Idle) return Conflict("This JellyP scheduled task is already running or cancelling.");
        Guid executionId = Guid.Empty;
        try
        {
            executionId = coordinator.QueueExecution(synchronize, dryRun);
            tasks.QueueIfNotRunning<T>();
            return Accepted(new ExecutionReceipt(executionId, task.Id, "Queued"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            if (executionId != Guid.Empty) coordinator.RejectQueuedExecution(executionId);
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch
        {
            if (executionId != Guid.Empty) coordinator.RejectQueuedExecution(executionId);
            throw;
        }
    }

    /// <summary>Restores one original through Jellyfin's supported save services.</summary>
    [HttpPost("Rollback/{id:guid}")]
    public async Task<ActionResult> Rollback(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.RollbackAsync(id, cancellationToken, authorizedManual: true).ConfigureAwait(false);
            return NoContent();
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>Retires verified backups; zero days explicitly retires all committed originals.</summary>
    [HttpPost("Backups/Retire")]
    public async Task<ActionResult> Retire([FromQuery] int olderThanDays = 1, CancellationToken cancellationToken = default)
    {
        if (olderThanDays is < 0 or > 3650) return BadRequest("Age must be between 0 and 3650 days.");
        try
        {
            await coordinator.PurgeAsync(DateTime.UtcNow.AddDays(-olderThanDays), cancellationToken, authorizedManual: true).ConfigureAwait(false);
            return NoContent();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
