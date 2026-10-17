using System.Reflection;
using Jellyfin.Plugin.JellyP.Api;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Migration;
using Jellyfin.Plugin.JellyP.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
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

    public JellyPControllerTests()
    {
        var store = Proxy<IArtworkStore>((method, args) =>
        {
            if (method.Name != "InspectItems") return null;
            _inspectionCalls++;
            return ((IReadOnlyCollection<Guid>)args![0]!).Select(id => new ItemArtworkInspection(id, "Synthetic", "Movie", 2026, true, [])).ToArray();
        });
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
