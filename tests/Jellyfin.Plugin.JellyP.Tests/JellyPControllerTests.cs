using System.Reflection;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.JellyP.Api;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Migration;
using Jellyfin.Plugin.JellyP.Tasks;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class JellyPControllerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyp-api-" + Guid.NewGuid().ToString("N"));
    private readonly PluginConfiguration _config = new() { Enabled = true, DryRun = false };
    private readonly OptimizationCoordinator _coordinator;
    private readonly JellyPController _controller;
    private TaskState _state = TaskState.Idle;
    private bool _available = true;
    private bool _failQueue;
    private int _queueCalls;
    private int _inspectionCalls;
    private Guid _planItemId;
    private string _planImagePath = string.Empty;
    private bool _throwInventoryPath;
    private int _cancelCalls;

    public JellyPControllerTests()
    {
        var store = Proxy<IArtworkStore>((method, args) => method.Name switch
        {
            "InspectItems" => Inspect((IReadOnlyCollection<Guid>)args![0]!),
            "DetailedInventory" => _throwInventoryPath
                ? throw new InvalidOperationException("/private/secret/inventory-path")
                : _planImagePath.Length > 0 && File.Exists(_planImagePath)
                ? [new ArtworkInventoryEntry
                {
                    Reference = new ArtworkReference(_planItemId, ImageType.Primary, 0, _planImagePath),
                    Name = "Synthetic",
                    ItemType = "Movie",
                    ProductionYear = 2026,
                    Category = "Primary",
                    State = "Ready",
                    CurrentBytes = new FileInfo(_planImagePath).Length,
                    MimeType = "image/png",
                }]
                : Array.Empty<ArtworkInventoryEntry>(),
            "Inventory" => Array.Empty<ArtworkReference>(),
            _ => null,
        });
        IReadOnlyList<ItemArtworkInspection> Inspect(IReadOnlyCollection<Guid> ids)
        {
            _inspectionCalls++;
            return ids.Select(id => new ItemArtworkInspection(id, "Synthetic", "Movie", 2026, true, [])).ToArray();
        }
        _coordinator = new OptimizationCoordinator(store, () => _config, _root, NullLogger<OptimizationCoordinator>.Instance);
        var task = new OptimizeArtworkTask(_coordinator);
        var worker = Proxy<IScheduledTaskWorker>((method, _) => method.Name switch
        {
            "get_ScheduledTask" => task,
            "get_Id" => "native-task-id",
            "get_State" => _state,
            _ => null,
        });
        var manager = Proxy<ITaskManager>((method, _) =>
        {
            if (method.Name == "get_ScheduledTasks") return _available ? new[] { worker } : Array.Empty<IScheduledTaskWorker>();
            if (method.Name == "QueueIfNotRunning")
            {
                _queueCalls++;
                if (_failQueue) throw new InvalidOperationException("Native queue unavailable.");
            }
            if (method.Name == "CancelIfRunning") _cancelCalls++;
            return null;
        });
        _controller = new JellyPController(_coordinator, manager, store);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void InvalidInspectionNeverReadsItemsOrQueuesWork(string? ids)
    {
        Assert.IsType<BadRequestObjectResult>(_controller.Selection(ids, default).Result);
        Assert.Equal(0, _inspectionCalls);
        Assert.Equal(0, _queueCalls);
    }

    [Fact]
    public void InspectionIsBoundedDeduplicatedAndDoesNotQueueWork()
    {
        var id = Guid.NewGuid();
        var result = Assert.IsType<OkObjectResult>(_controller.Selection($"{id},{id}", default).Result);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ItemArtworkInspection>>(result.Value));
        Assert.Equal(1, _inspectionCalls);
        Assert.Equal(0, _queueCalls);
        Assert.IsType<BadRequestObjectResult>(_controller.Selection(string.Join(',', Enumerable.Repeat(id, 101)), default).Result);
        Assert.Equal(1, _inspectionCalls);
    }

    [Fact]
    public void ManualPreviewReturnsNativeTaskAndExecutionIdentityWithoutChangingAutomation()
    {
        var result = Assert.IsType<AcceptedResult>(_controller.Run(true));
        var receipt = Assert.IsType<ExecutionReceipt>(result.Value);
        Assert.Equal("native-task-id", receipt.TaskId);
        Assert.Equal("Queued", receipt.State);
        Assert.NotEqual(Guid.Empty, receipt.ExecutionId);
        Assert.Equal(receipt.ExecutionId, _coordinator.GetStatus().LastRun.ExecutionId);
        Assert.True(_coordinator.GetStatus().LastRun.DryRun);
        Assert.False(_config.DryRun);
        Assert.Equal(1, _queueCalls);
    }

    [Fact]
    public void DuplicateRequestReturnsConflictWithoutQueuingTwice()
    {
        Assert.IsType<AcceptedResult>(_controller.Run(true));
        Assert.Equal(409, Assert.IsType<ObjectResult>(_controller.Run(true)).StatusCode);
        Assert.Equal(1, _queueCalls);
    }

    [Fact]
    public void MissingNativeTaskIsReportedBeforeAcceptingRequest()
    {
        _available = false;
        Assert.Equal(503, Assert.IsType<ObjectResult>(_controller.Run(true)).StatusCode);
        Assert.Equal(0, _queueCalls);
        Assert.Equal("Idle", _coordinator.GetStatus().LastRun.Phase);
    }

    [Fact]
    public void BusyNativeTaskCannotLeaveAnUnconsumedRequest()
    {
        _state = TaskState.Running;
        Assert.IsType<ConflictObjectResult>(_controller.Run(true));
        Assert.Equal(0, _queueCalls);
        Assert.Equal("Idle", _coordinator.GetStatus().LastRun.Phase);
    }

    [Fact]
    public void FailedNativeDispatchReleasesReservationForRetry()
    {
        _failQueue = true;
        Assert.Equal(409, Assert.IsType<ObjectResult>(_controller.Run(true)).StatusCode);
        _failQueue = false;
        Assert.IsType<AcceptedResult>(_controller.Run(true));
        Assert.Equal(2, _queueCalls);
    }

    [Fact]
    public void CancelMarksQueuedExecutionWithoutStartingAReplacement()
    {
        _controller.Run(true);
        Assert.IsType<AcceptedResult>(_controller.Cancel());
        Assert.Equal("Cancelled", _coordinator.GetStatus().LastRun.Phase);
        Assert.Equal(1, _queueCalls);
    }

    [Fact]
    public void CancelExecutionTargetsOnlyTheExactQueuedOperationAndReleasesItsPlan()
    {
        _config.MinimumSavingsBytes = 0;
        _config.MinimumSavingsRatio = 0;
        _config.MinimumSsim = 0;
        _planItemId = Guid.NewGuid();
        _planImagePath = Path.Combine(_root, "artwork", "cancel.png");
        Directory.CreateDirectory(Path.GetDirectoryName(_planImagePath)!);
        File.WriteAllBytes(_planImagePath, Png());
        var plan = Assert.IsType<PlanSummary>(Assert.IsType<OkObjectResult>(_controller.Analyze(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [_planItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default).Result).Value);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        Assert.NotEmpty(keys);
        var receipt = Assert.IsType<OperationReceipt>(Assert.IsType<AcceptedResult>(
            _controller.Apply(plan.PlanId, new PlanApplyRequest { Keys = keys }).Result).Value);
        Assert.Equal("Queued", receipt.State);

        Assert.IsType<AcceptedResult>(_controller.CancelExecution(receipt.OperationId));
        Assert.Equal("Cancelled", _coordinator.GetOperation(receipt.OperationId, 0, 10).Operation.State);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Equal(1, _cancelCalls);

        var unknown = _controller.CancelExecution(Guid.NewGuid());
        Assert.Equal(404, Assert.IsType<ObjectResult>(unknown).StatusCode);
        Assert.Equal(1, _cancelCalls);
    }

    [Fact]
    public void CancelExecutionReportsTerminalStateWhenFinalizationWinsTheRace()
    {
        _config.MinimumSavingsBytes = 0;
        _config.MinimumSavingsRatio = 0;
        _config.MinimumSsim = 0;
        _planItemId = Guid.NewGuid();
        _planImagePath = Path.Combine(_root, "artwork", "cancel-race.png");
        Directory.CreateDirectory(Path.GetDirectoryName(_planImagePath)!);
        File.WriteAllBytes(_planImagePath, Png());
        var plan = Assert.IsType<PlanSummary>(Assert.IsType<OkObjectResult>(_controller.Analyze(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [_planItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default).Result).Value);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        var receipt = Assert.IsType<OperationReceipt>(Assert.IsType<AcceptedResult>(
            _controller.Apply(plan.PlanId, new PlanApplyRequest { Keys = keys }).Result).Value);
        var operationPath = Path.Combine(_root, "operations", receipt.OperationId.ToString("N") + ".json");
        var running = JsonNode.Parse(File.ReadAllText(operationPath))!;
        running["State"] = "Running";
        running["Phase"] = "Processing";
        File.WriteAllText(operationPath, running.ToJsonString());
        var completed = JsonNode.Parse(File.ReadAllText(operationPath))!;
        completed["State"] = "Completed";
        completed["Phase"] = "Completed";
        completed["CompletedUtc"] = DateTime.UtcNow.ToString("O");
        SetCancellationHook(() => File.WriteAllText(operationPath, completed.ToJsonString()));
        try
        {
            var result = _controller.CancelExecution(receipt.OperationId);
            Assert.Equal(409, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Equal(0, _cancelCalls);
        }
        finally
        {
            SetCancellationHook(null);
        }
    }

    [Fact]
    public void NewAnalysisEndpointReturnsPathFreeResultWithoutQueueingMutation()
    {
        var result = Assert.IsType<OkObjectResult>(_controller.Analyze(new PlanAnalyzeRequest { Scope = "all" }, default).Result);
        var summary = Assert.IsType<PlanSummary>(result.Value);
        Assert.Equal("Ready", summary.Status);
        Assert.DoesNotContain("poster", System.Text.Json.JsonSerializer.Serialize(summary));
        Assert.Equal(0, _queueCalls);
    }

    [Fact]
    public void UnknownPlanApplyAndConsentlessRetirementReturnBeforeNativeDispatch()
    {
        Assert.Equal(404, Assert.IsType<ObjectResult>(_controller.Apply(Guid.NewGuid(), new PlanApplyRequest { Keys = ["x"] }).Result).StatusCode);
        Assert.Equal(0, _queueCalls);
        Assert.Equal(409, Assert.IsType<ObjectResult>(_controller.RetireSelected(
            new BackupRetireRequest { RecordIds = [Guid.NewGuid()], AuthorizePermanentRemoval = false }, default).Result).StatusCode);
        Assert.Equal(0, _queueCalls);
    }

    [Fact]
    public void QueuedReplayWithoutRunnerNeverClaimsSuccessOrFallsBackToLegacy()
    {
        _config.MinimumSavingsBytes = 0;
        _config.MinimumSavingsRatio = 0;
        _config.MinimumSsim = 0;
        _planItemId = Guid.NewGuid();
        _planImagePath = Path.Combine(_root, "artwork", "poster.png");
        Directory.CreateDirectory(Path.GetDirectoryName(_planImagePath)!);
        File.WriteAllBytes(_planImagePath, Png());
        var plan = Assert.IsType<OkObjectResult>(_controller.Analyze(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [_planItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default).Result).Value as PlanSummary;
        Assert.NotNull(plan);
        var keys = _coordinator.GetPlanKeys(plan!.PlanId, "selectable").Keys.ToArray();
        Assert.Single(keys);
        var first = Assert.IsType<AcceptedResult>(_controller.Apply(plan.PlanId, new PlanApplyRequest { Keys = keys }).Result);
        Assert.Equal(1, _queueCalls);

        // A queued operation is not a runner. The durable marker is retained when
        // dispatch cannot be repaired; it must never fall back to the legacy batch.
        _state = TaskState.Idle;
        _failQueue = true;
        var replay = _controller.Apply(plan.PlanId, new PlanApplyRequest { Keys = keys }).Result;
        Assert.Equal(409, Assert.IsType<ObjectResult>(replay).StatusCode);
        Assert.Equal("Reserved", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.NotNull(_coordinator.PendingOperationId);

        _failQueue = false;
        _available = false;
        var missing = _controller.Apply(plan.PlanId, new PlanApplyRequest { Keys = keys }).Result;
        Assert.Equal(503, Assert.IsType<ObjectResult>(missing).StatusCode);
        Assert.Equal("Reserved", _coordinator.GetPlan(plan.PlanId).Status);

        _available = true;
        _state = TaskState.Running;
        var busy = _controller.Apply(plan.PlanId, new PlanApplyRequest { Keys = keys }).Result;
        Assert.Equal(409, Assert.IsType<ObjectResult>(busy).StatusCode);
        Assert.Equal("Reserved", _coordinator.GetPlan(plan.PlanId).Status);

        _state = TaskState.Idle;
        var receipt = Assert.IsType<OperationReceipt>(first.Value);
        var retried = Assert.IsType<AcceptedResult>(_controller.RetryExecution(receipt.OperationId).Result);
        Assert.Equal(receipt.OperationId, Assert.IsType<OperationReceipt>(retried.Value).OperationId);
    }

    [Fact]
    public void AnalysisApiDoesNotLeakUnknownInventoryPaths()
    {
        _throwInventoryPath = true;
        var result = Assert.IsType<ObjectResult>(_controller.Analyze(new PlanAnalyzeRequest { Scope = "all" }, default).Result);
        Assert.Equal(409, result.StatusCode);
        Assert.DoesNotContain("/private", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public void AnalysisProgressUnknownIdentityIsBoundedAndNotFound()
    {
        Assert.Equal(404, Assert.IsType<ObjectResult>(_controller.AnalyzeProgress(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public void TasksPollingDoesNotScanJournalsOrBackups()
    {
        var journal = Path.Combine(_root, "journal");
        Directory.CreateDirectory(journal);
        File.WriteAllText(Path.Combine(journal, "unreadable.json"), "{not-json");
        var result = Assert.IsType<OkObjectResult>(_controller.NativeTasks().Result);
        Assert.DoesNotContain("journal", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public void TasksResponseSanitizesLegacyFailurePaths()
    {
        Jellyfin.Plugin.JellyP.Infrastructure.DurableFiles.WriteJson(Path.Combine(_root, "last-run.json"), new RunReport
        {
            Phase = "Failed",
            Message = "/private/customer/poster.png",
        });
        var result = Assert.IsType<OkObjectResult>(_controller.NativeTasks().Result);
        var text = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("/private", text);
        Assert.Contains("Legacy JellyP run needs attention", text);
    }

    private static void SetCancellationHook(Action? hook) =>
        ((AsyncLocal<Action?>)typeof(OptimizationCoordinator)
            .GetField("_cancellationRequestReadHook", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!).Value = hook;

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(512, 512);
        var random = new Random(42);
        for (var y = 0; y < 512; y++)
        for (var x = 0; x < 512; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(x / 2), (byte)(y / 2), (byte)(128 + random.Next(5)), 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        var result = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)result).Handler = invoke;
        return result;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
