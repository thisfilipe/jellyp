using Jellyfin.Plugin.JellyP.Migration;
using Jellyfin.Plugin.JellyP.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyP.Api;

public sealed partial class JellyPController
{
    /// <summary>Creates one read-only bounded analysis plan. Configuration, cursors and artwork are not written.</summary>
    [HttpPost("Plan/Analyze")]
    public ActionResult<PlanSummary> Analyze([FromBody] PlanAnalyzeRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return ApiProblem(400, "InvalidRequest", "An analysis request is required.");
        try { return Ok(coordinator.AnalyzePlan(request, cancellationToken)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Returns one plan summary.</summary>
    [HttpGet("Plan/{id:guid}")]
    public ActionResult<PlanSummary> Plan(Guid id)
    {
        try { return Ok(coordinator.GetPlan(id)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Returns a stable page of path-free plan image evidence.</summary>
    [HttpGet("Plan/{id:guid}/Images")]
    public ActionResult<PlanImagesPage> PlanImages(Guid id, [FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        try { return Ok(coordinator.GetPlanImages(id, offset, limit)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Returns every selectable key, or every key, for the explicit selection actions.</summary>
    [HttpGet("Plan/{id:guid}/Keys")]
    public ActionResult<PlanKeysResponse> PlanKeys(Guid id, [FromQuery] string? kind = null)
    {
        try { return Ok(coordinator.GetPlanKeys(id, kind)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Computes exact reviewed billing and separate retention consequences for the selected keys.</summary>
    [HttpPost("Plan/{id:guid}/Review")]
    public ActionResult<PlanReviewResponse> Review(Guid id, [FromBody] PlanReviewRequest request)
    {
        if (request is null) return ApiProblem(400, "InvalidRequest", "A review request is required.");
        try { return Ok(coordinator.ReviewPlan(id, request)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Reserves an exact reviewed selection and queues it on the native JellyP task.</summary>
    [HttpPost("Plan/{id:guid}/Apply")]
    public ActionResult<OperationReceipt> Apply(Guid id, [FromBody] PlanApplyRequest request)
    {
        if (request is null) return ApiProblem(400, "InvalidRequest", "An apply request is required.");
        try
        {
            var replay = coordinator.ExistingApplyReceipt(id, request.Keys);
            if (replay is not null)
            {
                // A crash/HTTP abort can occur after durable reservation but before the
                // native dispatch. Re-issuing Apply repairs that gap or releases the
                // reservation; never return 202 with no possible runner.
                if (replay.State == "Queued" && coordinator.CurrentRunnerOperationId != replay.OperationId)
                {
                    var task = tasks.ScheduledTasks.FirstOrDefault(worker => worker.ScheduledTask is OptimizeArtworkTask);
                    if (task is null)
                        return ApiProblem(503, "NativeTaskUnavailable",
                            "JellyP scheduled task is unavailable; the durable queued operation was kept for retry.");

                    if (task.State != TaskState.Idle)
                    {
                        return coordinator.CurrentRunnerOperationId == replay.OperationId
                            ? Accepted(replay)
                            : ApiProblem(409, "NativeQueueUnavailable",
                                "The native task did not attach this queued operation; the durable queued operation was kept for retry.");
                    }

                    try
                    {
                        tasks.QueueIfNotRunning<OptimizeArtworkTask>();
                    }
                    catch
                    {
                        return coordinator.CurrentRunnerOperationId == replay.OperationId
                            ? Accepted(replay)
                            : ApiProblem(409, "NativeQueueUnavailable",
                                "The native task could not be queued; the durable queued operation was kept for retry.");
                    }
                }

                return Accepted(replay);
            }
        }
        catch (Exception ex) { return ApiFailure(ex); }

        return QueueOperation(() => coordinator.ApplyPlan(id, request), coordinator.RejectQueuedOperation);
    }

    /// <summary>Returns persisted operations, newest first.</summary>
    [HttpGet("Executions")]
    public ActionResult<OperationPage> Executions([FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        try { return Ok(coordinator.GetOperations(offset, limit)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Returns one operation and a page of individual image or backup results.</summary>
    [HttpGet("Executions/{id:guid}")]
    public ActionResult<OperationDetail> Execution(Guid id, [FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        try { return Ok(coordinator.GetOperation(id, offset, limit)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Requests cancellation of one queued or running operation without rolling back completed transactions.</summary>
    [HttpPost("Executions/{id:guid}/Cancel")]
    public ActionResult CancelExecution(Guid id)
    {
        try
        {
            var detail = coordinator.GetOperation(id, 0, 1);
            if (OptimizationCoordinator.IsTerminalOperationState(detail.Operation.State))
                return ApiProblem(409, "OperationTerminal", "The operation already reached a terminal state.");
            if (!coordinator.RequestCancelOperation(id))
            {
                var current = coordinator.GetOperation(id, 0, 1);
                if (OptimizationCoordinator.IsTerminalOperationState(current.Operation.State))
                    return ApiProblem(409, "OperationTerminal", "The operation already reached a terminal state.");
            }

            tasks.CancelIfRunning<OptimizeArtworkTask>();
            return Accepted();
        }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Retries a durable queued operation without creating a second reservation.</summary>
    [HttpPost("Executions/{id:guid}/Retry")]
    public ActionResult<OperationReceipt> RetryExecution(Guid id)
    {
        try
        {
            var detail = coordinator.GetOperation(id, 0, 1);
            if (detail.Operation.State != "Queued")
                return ApiProblem(409, "OperationTerminal", "Only a durable queued operation can be retried.");
            if (coordinator.CurrentRunnerOperationId == id)
                return Accepted(Receipt(detail.Operation));

            var task = tasks.ScheduledTasks.FirstOrDefault(worker => worker.ScheduledTask is OptimizeArtworkTask);
            if (task is null)
                return ApiProblem(503, "NativeTaskUnavailable", "JellyP scheduled task is unavailable; the queued operation was kept.");
            if (task.State != TaskState.Idle)
                return ApiProblem(409, "NativeQueueUnavailable", "The native task is busy; the queued operation was kept for retry.");
            try
            {
                tasks.QueueIfNotRunning<OptimizeArtworkTask>();
            }
            catch
            {
                return coordinator.CurrentRunnerOperationId == id
                    ? Accepted(Receipt(detail.Operation))
                    : ApiProblem(409, "NativeQueueUnavailable", "The native task could not be queued; the durable operation was kept.");
            }

            return Accepted(Receipt(detail.Operation));
        }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    private static OperationReceipt Receipt(OperationView operation) => new()
    {
        OperationId = operation.OperationId,
        Kind = operation.Kind,
        State = operation.State,
        Poll = "/JellyP/Executions/" + operation.OperationId.ToString("N"),
    };

    /// <summary>Lists active committed backups that are preliminarily restorable or retirable.</summary>
    [HttpGet("Backups")]
    public ActionResult<BackupPage> Backups([FromQuery] string? filter = null, [FromQuery] int offset = 0,
        [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        try { return Ok(coordinator.GetBackups(filter, offset, limit, cancellationToken)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Returns all keys for one explicit backup filter, failing instead of silently truncating above the hard cap.</summary>
    [HttpGet("Backups/Keys")]
    public ActionResult<BackupKeysResponse> BackupKeys([FromQuery] string? filter = null, CancellationToken cancellationToken = default)
    {
        try { return Ok(coordinator.GetBackupKeys(filter, cancellationToken)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Queues an exact batch restoration. Unknown, duplicate, shared or uncertain records are rejected before any write.</summary>
    [HttpPost("Backups/Restore")]
    public ActionResult<OperationReceipt> Restore([FromBody] BackupBatchRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return ApiProblem(400, "InvalidRequest", "A restore request is required.");
        return QueueOperation(() => coordinator.QueueRestore(request, cancellationToken), coordinator.RejectQueuedOperation);
    }

    /// <summary>Queues exact permanent retirement after explicit authorization and server-side eligibility checks.</summary>
    [HttpPost("Backups/RetireSelected")]
    public ActionResult<OperationReceipt> RetireSelected([FromBody] BackupRetireRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return ApiProblem(400, "InvalidRequest", "A retirement request is required.");
        return QueueOperation(() => coordinator.QueueRetire(request, cancellationToken), coordinator.RejectQueuedOperation);
    }

    /// <summary>Returns the automation settings, manual defaults projection and explicit global limits.</summary>
    [HttpGet("Settings")]
    public ActionResult<SettingsView> Settings()
    {
        try { return Ok(coordinator.GetSettings()); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Persists only manual defaults and explicit global limits; automation is not modified.</summary>
    [HttpPut("Settings/Defaults")]
    public ActionResult<SettingsView> SaveDefaults([FromBody] SettingsDefaultsRequest request)
    {
        if (request is null) return ApiProblem(400, "InvalidRequest", "A defaults request is required.");
        try { return Ok(coordinator.SaveDefaults(request)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Persists only automation fields; manual defaults and explicit global limits are not modified.</summary>
    [HttpPut("Settings/Automation")]
    public ActionResult<SettingsView> SaveAutomation([FromBody] SettingsAutomationRequest request)
    {
        if (request is null) return ApiProblem(400, "InvalidRequest", "An automation request is required.");
        try { return Ok(coordinator.SaveAutomation(request)); }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    /// <summary>Returns native JellyP task state, the legacy last report and the active operation identity.</summary>
    [HttpGet("Tasks")]
    public ActionResult<TasksView> NativeTasks()
    {
        try
        {
            var views = tasks.ScheduledTasks
                .Where(worker => worker.ScheduledTask is OptimizeArtworkTask or SynchronizePoliciesTask)
                .Select(worker => new NativeTaskView
                {
                    Id = worker.Id,
                    Key = worker.ScheduledTask.Key,
                    Name = worker.ScheduledTask.Name,
                    State = worker.State.ToString(),
                }).ToArray();
            return Ok(new TasksView
            {
                LastRun = SanitizeLegacyReport(coordinator.GetStatus().LastRun),
                Tasks = views,
                ActiveOperationId = coordinator.ActiveOperationId,
                CurrentRunnerOperationId = coordinator.CurrentRunnerOperationId,
                PendingOperationId = coordinator.PendingOperationId,
            });
        }
        catch (Exception ex) { return ApiFailure(ex); }
    }

    private ActionResult<OperationReceipt> QueueOperation(Func<OperationReceipt> reserve, Action<Guid> rollback)
    {
        var task = tasks.ScheduledTasks.FirstOrDefault(worker => worker.ScheduledTask is OptimizeArtworkTask);
        if (task is null)
            return ApiProblem(503, "NativeTaskUnavailable", "JellyP scheduled task is unavailable. Restart Jellyfin and check plugin loading.");
        if (task.State != TaskState.Idle)
            return ApiProblem(409, "NativeTaskBusy", "JellyP native task is already running or cancelling.");

        OperationReceipt receipt;
        try
        {
            receipt = reserve();
        }
        catch (Exception ex)
        {
            return ApiFailure(ex);
        }

        try
        {
            tasks.QueueIfNotRunning<OptimizeArtworkTask>();
            return Accepted(receipt);
        }
        catch
        {
            try { rollback(receipt.OperationId); }
            catch { /* The queued operation remains visible as Interrupted/failed rather than hiding evidence. */ }
            return ApiProblem(409, "NativeQueueUnavailable", "The native JellyP task could not be queued; the reservation was released before any artwork changed.");
        }
    }

    private ObjectResult ApiFailure(Exception exception) => exception switch
    {
        JellyPRequestException request => ApiProblem(request.StatusCode, request.Code, request.Message),
        ArgumentException => ApiProblem(400, "InvalidRequest", "The request is invalid. Check the documented fields and limits."),
        FileNotFoundException => ApiProblem(404, "NotFound", "The requested JellyP resource was not found."),
        InvalidOperationException operation => LoggedConflict(exception),
        IOException or UnauthorizedAccessException => ApiProblem(409, "IoConflict",
            "The operation could not proceed safely. Review server logs and recovery state; no filesystem details are exposed."),
        System.Text.Json.JsonException or InvalidDataException => ApiProblem(409, "StoredDataInvalid",
            "Stored JellyP data is invalid. Review server logs before continuing."),
        _ => ApiProblem(500, "InternalError", "JellyP could not complete the request. Review server logs."),
    };

    private ObjectResult LoggedConflict(Exception exception)
    {
        _logger?.LogError(exception, "JellyP request failed with a non-public operation error");
        return ApiProblem(409, "Conflict", "The JellyP operation conflicts with current server state. Review server logs and recovery state.");
    }

    private ObjectResult ApiProblem(int status, string code, string message) =>
        StatusCode(status, new { code, message });

    private static RunReport SanitizeLegacyReport(RunReport report)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<RunReport>(
            System.Text.Json.JsonSerializer.Serialize(report)) ?? new RunReport();
        if (copy.Phase == "Failed" || copy.Message?.IndexOfAny(['/', '\\']) >= 0)
            copy.Message = "Legacy JellyP run needs attention. Review server logs for paths and technical details.";
        return copy;
    }
}
