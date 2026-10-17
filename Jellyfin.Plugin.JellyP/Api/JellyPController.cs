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
    public ActionResult Run()
    {
        tasks.QueueIfNotRunning<OptimizeArtworkTask>();
        return Accepted();
    }

    /// <summary>Requests cancellation after the current image transaction.</summary>
    [HttpPost("Cancel")]
    public ActionResult Cancel()
    {
        tasks.CancelIfRunning<OptimizeArtworkTask>();
        tasks.CancelIfRunning<SynchronizePoliciesTask>();
        return Accepted();
    }

    /// <summary>Queues policy synchronization from preserved reference originals.</summary>
    [HttpPost("Sync")]
    public ActionResult Sync()
    {
        tasks.QueueIfNotRunning<SynchronizePoliciesTask>();
        return Accepted();
    }

    /// <summary>Restores one original through Jellyfin's supported save services.</summary>
    [HttpPost("Rollback/{id:guid}")]
    public async Task<ActionResult> Rollback(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.RollbackAsync(id, cancellationToken).ConfigureAwait(false);
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
            await coordinator.PurgeAsync(DateTime.UtcNow.AddDays(-olderThanDays), cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
