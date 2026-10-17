using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Conversion;
using Jellyfin.Plugin.JellyP.Infrastructure;
using Jellyfin.Plugin.JellyP.Tasks;
using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class OptimizationCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyp-test-" + Guid.NewGuid().ToString("N"));
    private readonly PluginConfiguration _config = new()
    {
        Enabled = true, DryRun = false, MaintenanceWindowConfirmed = true,
        MinimumSavingsBytes = 0, MinimumSavingsRatio = 0, MinimumSsim = .90,
        MinimumFreeSpaceBytes = 64L * 1024 * 1024,
    };
    private readonly FakeArtworkStore _store;
    private readonly OptimizationCoordinator _coordinator;

    public OptimizationCoordinatorTests()
    {
        Directory.CreateDirectory(_root);
        _store = new FakeArtworkStore(Path.Combine(_root, "artwork"));
        _coordinator = new OptimizationCoordinator(_store, () => _config, Path.Combine(_root, "data"), NullLogger<OptimizationCoordinator>.Instance);
    }

    [Fact]
    public async Task DryRunNeverChangesArtworkOrCreatesBackups()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        _config.DryRun = true;
        _config.Enabled = false;
        _config.MaintenanceWindowConfirmed = false;
        await Run();
        Assert.Equal(original, File.ReadAllBytes(image.Path));
        Assert.Equal(0, _store.SaveCalls);
        Assert.Equal(0, _coordinator.GetStatus().RetainedBackupBytes);
        Assert.True(_coordinator.GetStatus().LastRun.EstimatedSavingsBytes > 0);
    }

    [Fact]
    public async Task ConvertThenRollbackRestoresExactOriginalAndReference()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        await Run();
        var status = _coordinator.GetStatus();
        var record = Assert.Single(status.RecentRecords);
        Assert.Equal("Committed", record.State);
        Assert.False(File.Exists(image.Path));
        Assert.EndsWith(".webp", _store.GetCurrent(image)!.Path);
        Assert.Equal(original.Length, status.RetainedBackupBytes);
        Assert.True(status.NetReclaimedBytes < 0); // Backups have not been retired.
        await _coordinator.RollbackAsync(record.Id, default);
        Assert.Equal(original, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.Equal("RolledBack", Assert.Single(_coordinator.GetStatus().RecentRecords).State);
        Assert.Equal(0, _coordinator.GetStatus().RetainedBackupBytes);
    }

    [Fact]
    public async Task RetireOnlyDeletesVerifiedOriginalsAndUpdatesNetSavings()
    {
        _store.Add();
        await Run();
        var before = _coordinator.GetStatus();
        await _coordinator.PurgeAsync(DateTime.UtcNow.AddSeconds(1), default);
        var after = _coordinator.GetStatus();
        Assert.Equal(0, after.RetainedBackupBytes);
        Assert.Equal(before.VerifiedSavingsBytes, after.NetReclaimedBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _coordinator.RollbackAsync(after.RecentRecords[0].Id, default));
    }

    [Fact]
    public async Task FailedPersistenceRestoresOriginalBeforeStopping()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        _store.FailNextSaveAfterChange = true;
        await Assert.ThrowsAsync<IOException>(Run);
        Assert.Equal(original, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.Equal("RolledBack", Assert.Single(_coordinator.GetStatus().RecentRecords).State);
    }

    [Fact]
    public async Task DestinationCollisionNeverOverwritesExistingFile()
    {
        var image = _store.Add();
        var destination = _store.GetDestination(image, "image/webp");
        File.WriteAllBytes(destination, [4, 5, 6]);
        await Assert.ThrowsAsync<IOException>(Run);
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(destination));
        Assert.Equal(0, _store.SaveCalls);
        Assert.True(File.Exists(image.Path));
    }

    [Fact]
    public async Task ChangedArtworkBlocksRollbackAndBackupRetirement()
    {
        var image = _store.Add();
        await Run();
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        var current = _store.GetCurrent(image)!;
        File.WriteAllBytes(current.Path, [1, 2, 3]);
        await Assert.ThrowsAsync<IOException>(() => _coordinator.RollbackAsync(record.Id, default));
        await _coordinator.PurgeAsync(DateTime.UtcNow.AddSeconds(1), default);
        Assert.True(_coordinator.GetStatus().RetainedBackupBytes > 0);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(current.Path));
    }

    [Fact]
    public async Task ResumeAdvancesOnlyAfterCompletedImages()
    {
        _store.Add(); _store.Add(); _store.Add();
        _config.BatchSize = 1;
        await Run(); await Run(); await Run();
        Assert.Equal(3, _store.SaveCalls);
        Assert.All(_coordinator.GetStatus().RecentRecords, r => Assert.Equal("Committed", r.State));
        Assert.Equal(string.Empty, _coordinator.GetStatus().LastRun.Cursor);
    }

    [Fact]
    public async Task CancelBeforeRunNeverWrites()
    {
        var image = _store.Add();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _coordinator.RunAsync(new Progress<double>(), cancellation.Token));
        Assert.Equal(0, _store.SaveCalls);
        Assert.True(File.Exists(image.Path));
    }

    [Fact]
    public async Task BusyServerWaitsAndCancellationPreservesOriginal()
    {
        var image = _store.Add();
        _store.Busy = true;
        using var cancellation = new CancellationTokenSource();
        var run = _coordinator.RunAsync(new Progress<double>(), cancellation.Token);
        Assert.Equal("WaitingForMetadata", _coordinator.GetStatus().LastRun.Phase);
        Assert.Equal(0, _store.SaveCalls);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal("Cancelled", _coordinator.GetStatus().LastRun.Phase);
        Assert.True(File.Exists(image.Path));
    }

    [Fact]
    public async Task ApplyResumesAfterConflictWithoutManualMaintenanceFlag()
    {
        _store.Add();
        _store.Busy = true;
        _config.MaintenanceWindowConfirmed = false;
        var run = Run();
        Assert.Equal("WaitingForMetadata", _coordinator.GetStatus().LastRun.Phase);
        _store.Busy = false;
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, _store.SaveCalls);
        Assert.Equal("Completed", _coordinator.GetStatus().LastRun.Phase);
    }

    [Fact]
    public async Task ManualPreviewDoesNotChangeSavedAutomationOrRequireIdle()
    {
        _store.Add();
        _store.Busy = true;
        await _coordinator.RunAsync(new Progress<double>(), default, dryRunOverride: true);
        var report = _coordinator.GetStatus().LastRun;
        Assert.True(report.DryRun);
        Assert.Equal(1, report.WouldConvert);
        Assert.Single(report.Samples);
        Assert.Equal(0, _store.SaveCalls);
        Assert.False(_config.DryRun);
        Assert.True(_config.Enabled);
    }

    [Fact]
    public async Task ExplicitRestoreWorksWithPreviewAutomationWithoutChangingFlags()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        await Run();
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        _config.Enabled = false;
        _config.DryRun = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _coordinator.RollbackAsync(record.Id, default));
        await _coordinator.RollbackAsync(record.Id, default, authorizedManual: true);
        Assert.Equal(original, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.False(_config.Enabled);
        Assert.True(_config.DryRun);
    }

    [Fact]
    public async Task ManualRestoreWaitCanBeCancelledWithoutChangingConvertedImage()
    {
        var image = _store.Add();
        await Run();
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        _store.Busy = true;
        using var cancellation = new CancellationTokenSource();
        var restore = _coordinator.RollbackAsync(record.Id, cancellation.Token, authorizedManual: true);
        Assert.Equal("WaitingForMetadata", _coordinator.GetStatus().LastRun.Phase);
        _coordinator.CancelQueuedExecution();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore);
        Assert.EndsWith(".webp", _store.GetCurrent(image)!.Path);
        Assert.Equal("Committed", Assert.Single(_coordinator.GetStatus().RecentRecords).State);
    }

    [Fact]
    public async Task ExplicitManualApplyLeavesAutomationInPreviewMode()
    {
        _store.Add();
        _config.Enabled = false;
        _config.DryRun = true;
        _coordinator.QueueExecution(false, false);
        await Run();
        Assert.Equal(1, _store.SaveCalls);
        Assert.False(_coordinator.GetStatus().LastRun.DryRun);
        Assert.False(_config.Enabled);
        Assert.True(_config.DryRun);
    }

    [Fact]
    public async Task CancelledNativeTokenConsumesCancelledRequestWithoutApplying()
    {
        _store.Add();
        _coordinator.QueueExecution(false, false);
        _coordinator.CancelQueuedExecution();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await _coordinator.RunAsync(new Progress<double>(), cancellation.Token);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Equal("Cancelled", _coordinator.GetStatus().LastRun.Phase);
        Assert.NotEqual(Guid.Empty, _coordinator.QueueExecution(false, true));
    }

    [Theory]
    [InlineData("legacy", true, false)]
    [InlineData("items", true, false)]
    [InlineData("all", false, false)]
    [InlineData("libraries", false, true)]
    public async Task ScopeSelectionPassesOnlyItsOwnIdentifiersToInventory(string mode, bool items, bool libraries)
    {
        var image = _store.Add();
        _config.ScopeMode = mode;
        _config.IncludedItemIds = [image.ItemId];
        _config.IncludedLibraryIds = [Guid.NewGuid()];
        await _coordinator.RunAsync(new Progress<double>(), default, dryRunOverride: true);
        Assert.Equal(items ? 1 : 0, _store.LastIncludedItemIds?.Count ?? 0);
        Assert.Equal(libraries, _store.LastIncludedLibraryIds is not null);
        if (libraries) Assert.Equal(_config.IncludedLibraryIds, _store.LastIncludedLibraryIds);
        Assert.Equal(1, _coordinator.GetStatus().LastRun.InventoryItems);
    }

    [Fact]
    public async Task EmptyScopeResultExplainsWhyThereWasNothingToProcess()
    {
        _config.DryRun = true;
        await Run();
        var report = _coordinator.GetStatus().LastRun;
        Assert.Equal("Completed", report.Phase);
        Assert.Equal(0, report.Examined);
        Assert.Contains("No eligible images", report.Message);
    }

    [Fact]
    public async Task QueuedPreviewKeepsIdentityAndSnapshotAcrossSettingsChanges()
    {
        _store.Add(); _store.Add();
        _config.BatchSize = 1;
        var id = _coordinator.QueueExecution(false, true);
        Assert.Equal("Queued", _coordinator.GetStatus().LastRun.Phase);
        Assert.Throws<InvalidOperationException>(() => _coordinator.QueueExecution(true, true));
        _config.BatchSize = 2;
        await Run();
        var report = _coordinator.GetStatus().LastRun;
        Assert.Equal(id, report.ExecutionId);
        Assert.True(report.DryRun);
        Assert.Equal(1, report.Examined);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task CancelQueuedRequestNeverFallsBackToApplyingSavedSettings()
    {
        _store.Add();
        var id = _coordinator.QueueExecution(false, false);
        _coordinator.CancelQueuedExecution();
        await Run();
        Assert.Equal(0, _store.SaveCalls);
        Assert.Equal(id, _coordinator.GetStatus().LastRun.ExecutionId);
        Assert.Equal("Cancelled", _coordinator.GetStatus().LastRun.Phase);
    }

    [Fact]
    public async Task NativeOptimizeAndSyncSerializeWithoutFailingEachOther()
    {
        _store.Add();
        _store.Busy = true;
        var optimize = new OptimizeArtworkTask(_coordinator).ExecuteAsync(new Progress<double>(), default);
        var firstId = _coordinator.GetStatus().LastRun.ExecutionId;
        var synchronize = new SynchronizePoliciesTask(_coordinator).ExecuteAsync(new Progress<double>(), default);
        Assert.False(synchronize.IsCompleted);
        Assert.Equal(firstId, _coordinator.GetStatus().LastRun.ExecutionId);
        _store.Busy = false;
        await Task.WhenAll(optimize, synchronize).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, _store.InventoryCalls);
        Assert.Equal(1, _store.SaveCalls);
        Assert.Equal("Completed", _coordinator.GetStatus().LastRun.Phase);
        Assert.NotEqual(firstId, _coordinator.GetStatus().LastRun.ExecutionId);
    }

    [Fact]
    public async Task CancellingNativeWaiterLeavesActiveReportAndGateIntact()
    {
        _store.Add();
        _store.Busy = true;
        var optimize = new OptimizeArtworkTask(_coordinator).ExecuteAsync(new Progress<double>(), default);
        var firstId = _coordinator.GetStatus().LastRun.ExecutionId;
        using var cancellation = new CancellationTokenSource();
        var synchronize = new SynchronizePoliciesTask(_coordinator).ExecuteAsync(new Progress<double>(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synchronize);
        var status = _coordinator.GetStatus();
        Assert.True(status.LastRun.Running);
        Assert.Equal(firstId, status.LastRun.ExecutionId);
        Assert.Equal("WaitingForMetadata", status.LastRun.Phase);
        Assert.Throws<InvalidOperationException>(() => _coordinator.QueueExecution(false, true));
        _store.Busy = false;
        await optimize.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(1, _store.InventoryCalls);
        Assert.Equal(1, _store.SaveCalls);
    }

    [Fact]
    public async Task NativeWaiterDoesNotConsumeOtherTaskPendingRequest()
    {
        _store.Add();
        var id = _coordinator.QueueExecution(false, true);
        using var cancellation = new CancellationTokenSource();
        var synchronize = new SynchronizePoliciesTask(_coordinator).ExecuteAsync(new Progress<double>(), cancellation.Token);
        Assert.False(synchronize.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synchronize);
        Assert.Equal(id, _coordinator.GetStatus().LastRun.ExecutionId);
        Assert.Equal("Queued", _coordinator.GetStatus().LastRun.Phase);
        await new OptimizeArtworkTask(_coordinator).ExecuteAsync(new Progress<double>(), default);
        Assert.Equal(id, _coordinator.GetStatus().LastRun.ExecutionId);
        Assert.True(_coordinator.GetStatus().LastRun.DryRun);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task ConcurrentNativeTasksCannotOverwriteActiveExecution()
    {
        _store.Add(); _store.Busy = true;
        using var cancellation = new CancellationTokenSource();
        var run = _coordinator.RunAsync(new Progress<double>(), cancellation.Token);
        var id = _coordinator.GetStatus().LastRun.ExecutionId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _coordinator.RunAsync(new Progress<double>(), default, true));
        Assert.Equal(id, _coordinator.GetStatus().LastRun.ExecutionId);
        Assert.Throws<InvalidOperationException>(() => _coordinator.QueueExecution(false, true));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task RestartWithPreparedJournalRequiresExplicitRecovery()
    {
        var image = _store.Add();
        var bytes = File.ReadAllBytes(image.Path);
        var record = new MigrationRecord
        {
            Source = image, SourceHash = DurableFiles.Hash(bytes), OriginalMimeType = "image/png",
            OriginalBytes = bytes.Length, OutputHash = "missing",
        };
        var data = Path.Combine(_root, "data");
        DurableFiles.Write(Path.Combine(data, "originals", record.Id.ToString("N") + ".original"), bytes);
        DurableFiles.WriteJson(Path.Combine(data, "journal", record.Id.ToString("N") + ".json"), record);
        await Assert.ThrowsAsync<InvalidOperationException>(Run);
        Assert.Equal(1, _coordinator.GetStatus().PendingRecovery);
        await _coordinator.RollbackAsync(record.Id, default);
        Assert.Equal(bytes, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.Equal(0, _coordinator.GetStatus().PendingRecovery);
    }

    [Fact]
    public async Task FailedSaveBeforeReferenceMutationRemovesVerifiedOrphan()
    {
        var image = _store.Add();
        var bytes = File.ReadAllBytes(image.Path);
        var output = _store.GetDestination(image, "image/webp");
        _store.FailNextSaveBeforeReference = true;
        await Assert.ThrowsAsync<IOException>(Run);
        Assert.Equal(bytes, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.False(File.Exists(output));
        Assert.Equal("RolledBack", Assert.Single(_coordinator.GetStatus().RecentRecords).State);
        await Run(); // Orphan cleanup must permit a subsequent conversion.
        Assert.EndsWith(".webp", _store.GetCurrent(image)!.Path);
    }

    [Fact]
    public async Task PartialUnknownOutputRetainsBackupAndBlocksRetry()
    {
        var image = _store.Add();
        _store.FailNextSaveBeforeReference = true;
        _store.WritePartialOutput = true;
        await Assert.ThrowsAsync<IOException>(Run);
        var status = _coordinator.GetStatus();
        Assert.Equal("RecoveryRequired", Assert.Single(status.RecentRecords).State);
        Assert.True(status.RetainedBackupBytes > 0);
        Assert.True(File.Exists(image.Path));
        Assert.True(File.Exists(_store.GetDestination(image, "image/webp")));
        var calls = _store.SaveCalls;
        await Assert.ThrowsAsync<InvalidOperationException>(Run);
        Assert.Equal(calls, _store.SaveCalls);
    }

    [Fact]
    public async Task CrashWithOldDatabaseReferenceAndDeletedSourceRestoresAndRemovesOutput()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        var output = new WebpImageConverter().Optimize(original, _config.OptionsFor(_config.PolicyFor(image)!)).OutputBytes!;
        var record = Prepared(image, original, output);
        File.WriteAllBytes(record.OutputPath!, output);
        File.Delete(image.Path); // Persisted slot still points at source: crash before DB update.
        await _coordinator.RollbackAsync(record.Id, default);
        Assert.Equal(original, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.False(File.Exists(record.OutputPath));
        Assert.Equal(0, _coordinator.GetStatus().PendingRecovery);
    }

    [Fact]
    public async Task PermanentModeRetiresOriginalOnlyAfterVerifiedCommit()
    {
        var image = _store.Add();
        _config.PreserveOriginals = false;
        await Run();
        var status = _coordinator.GetStatus();
        var record = Assert.Single(status.RecentRecords);
        Assert.Equal("Committed", record.State);
        Assert.True(record.BackupDeleted);
        Assert.Equal(0, status.RetainedBackupBytes);
        Assert.True(status.NetReclaimedBytes > 0);
        Assert.True(File.Exists(_store.GetCurrent(image)!.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _coordinator.RollbackAsync(record.Id, default));
    }

    [Fact]
    public async Task DisabledCategoryAndPeopleOverrideAreIndependent()
    {
        _store.Add();
        _store.Add(isPerson: true);
        _config.ImagePolicies.Single(p => p.Category == "Primary").Enabled = false;
        await Run();
        Assert.Equal(1, _store.SaveCalls);
        Assert.True(Assert.Single(_coordinator.GetStatus().RecentRecords).Source.IsPerson);
        Assert.Equal(1, _coordinator.GetStatus().LastRun.InventoryImages);
        Assert.Equal(1, _coordinator.GetStatus().LastRun.Examined);
        Assert.Empty(_coordinator.GetStatus().LastRun.SkipReasons);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledCategoriesNeverConsumeSelectedBatch(bool synchronize)
    {
        var unselected = _store.Add(itemId: ItemId(1));
        var selected = _store.Add(isPerson: true, itemId: ItemId(2));
        _store.Add(isPerson: true, itemId: ItemId(3));
        if (synchronize) await Run();
        var untouched = File.ReadAllBytes(_store.GetCurrent(unselected)!.Path);
        _config.ImagePolicies.Single(p => p.Category == "Primary").Enabled = false;
        _config.Quality = 60;
        _config.MinimumSsim = 0;
        _config.BatchSize = 1;

        await _coordinator.RunAsync(new Progress<double>(), default, synchronize);

        var report = _coordinator.GetStatus().LastRun;
        Assert.Equal(2, report.InventoryImages);
        Assert.Equal(1, report.Examined);
        Assert.Equal(1, report.Converted);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(selected.Key, report.Cursor);
        Assert.Equal(untouched, File.ReadAllBytes(_store.GetCurrent(unselected)!.Path));
    }

    [Fact]
    public async Task ChangingIncludedItemsRestartsBeforePreviousCursor()
    {
        var first = _store.Add(itemId: ItemId(1));
        var second = _store.Add(itemId: ItemId(2));
        var third = _store.Add(itemId: ItemId(3));
        _config.DryRun = true;
        _config.BatchSize = 1;
        _config.IncludedItemIds = [second.ItemId, third.ItemId];
        await Run();
        Assert.Equal(second.Key, _coordinator.GetStatus().LastRun.Cursor);

        _config.IncludedItemIds = [third.ItemId, first.ItemId, second.ItemId];
        await Run();
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task EnablingCategoryRestartsBeforePreviousCursor()
    {
        var first = _store.Add(itemId: ItemId(1));
        var second = _store.Add(isPerson: true, itemId: ItemId(2));
        _store.Add(isPerson: true, itemId: ItemId(3));
        _config.DryRun = true;
        _config.BatchSize = 1;
        var primary = _config.ImagePolicies.Single(p => p.Category == "Primary");
        primary.Enabled = false;
        await Run();
        Assert.Equal(second.Key, _coordinator.GetStatus().LastRun.Cursor);

        primary.Enabled = true;
        await Run();
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingEffectivePolicyRestartsCursor(bool categoryOverride)
    {
        var first = _store.Add(itemId: ItemId(1));
        _store.Add(itemId: ItemId(2));
        _store.Add(itemId: ItemId(3));
        _config.DryRun = true;
        _config.BatchSize = 1;
        var policy = _config.ImagePolicies.Single(p => p.Category == "Primary");
        policy.OverrideGlobal = categoryOverride;
        await Run();
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);

        if (categoryOverride) policy.Quality = 60;
        else _config.Quality = 60;
        await Run();
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
    }

    [Fact]
    public async Task BatchSizeAndAuthorizationChangesKeepCursor()
    {
        _store.Add(itemId: ItemId(1));
        _store.Add(itemId: ItemId(2));
        var third = _store.Add(itemId: ItemId(3));
        _store.Add(itemId: ItemId(4));
        _config.DryRun = true;
        _config.BatchSize = 1;
        await Run();

        _config.Enabled = false;
        _config.MaintenanceWindowConfirmed = false;
        _config.BatchSize = 2;
        await Run();
        Assert.Equal(2, _coordinator.GetStatus().LastRun.Examined);
        Assert.Equal(third.Key, _coordinator.GetStatus().LastRun.Cursor);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task ReorderedSelectionAndUnusedGlobalQualityKeepCursor()
    {
        var first = _store.Add(itemId: ItemId(1));
        var second = _store.Add(itemId: ItemId(2));
        var third = _store.Add(itemId: ItemId(3));
        _config.DryRun = true;
        _config.BatchSize = 1;
        _config.IncludedItemIds = [first.ItemId, second.ItemId, third.ItemId];
        foreach (var policy in _config.ImagePolicies) policy.Enabled = policy.Category == "Primary";
        _config.ImagePolicies.Single(p => p.Category == "Primary").OverrideGlobal = true;
        await Run();

        _config.IncludedItemIds = [third.ItemId, first.ItemId, second.ItemId, first.ItemId];
        _config.ImagePolicies = _config.ImagePolicies.Reverse().ToArray();
        _config.Quality = 60;
        await Run();
        Assert.Equal(second.Key, _coordinator.GetStatus().LastRun.Cursor);
    }

    [Fact]
    public async Task PreviewAndApplyMaintainIndependentCursors()
    {
        var first = _store.Add(itemId: ItemId(1));
        var second = _store.Add(itemId: ItemId(2));
        _store.Add(itemId: ItemId(3));
        _config.BatchSize = 1;
        _config.DryRun = true;
        await Run();
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
        _config.DryRun = false;
        await Run();
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
        Assert.Equal(first.Key, Assert.Single(_coordinator.GetStatus().RecentRecords).Source.Key);
        _config.DryRun = true;
        await Run();
        Assert.Equal(second.Key, _coordinator.GetStatus().LastRun.Cursor);
    }

    [Fact]
    public async Task LegacyStringCursorRestartsAndMigratesWithoutLosingJournal()
    {
        var converted = _store.Add(itemId: ItemId(1));
        await Run();
        var journal = Assert.Single(_coordinator.GetStatus().RecentRecords);
        var first = _store.Add(itemId: ItemId(2));
        var last = _store.Add(itemId: ItemId(3));
        _config.DryRun = true;
        _config.BatchSize = 1;
        var cursorPath = Path.Combine(_root, "data", "optimize-cursor-preview.json");
        DurableFiles.WriteJson(cursorPath, last.Key);

        await Run();

        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
        Assert.Equal(journal.Id, Assert.Single(_coordinator.GetStatus().RecentRecords).Id);
        Assert.EndsWith(".webp", _store.GetCurrent(converted)!.Path);
        var cursor = DurableFiles.ReadJson<System.Text.Json.JsonElement>(cursorPath);
        Assert.Equal(1, cursor.GetProperty("Version").GetInt32());
        Assert.Equal(first.Key, cursor.GetProperty("LastKey").GetString());
        Assert.NotEmpty(cursor.GetProperty("ContextFingerprint").GetString()!);
    }

    [Fact]
    public async Task SyncPolicyAndUnchangedSettingChangesRestartSyncCursor()
    {
        var first = _store.Add(itemId: ItemId(1));
        var second = _store.Add(itemId: ItemId(2));
        _store.Add(itemId: ItemId(3));
        await Run();
        _config.DryRun = true;
        _config.BatchSize = 1;
        await _coordinator.RunAsync(new Progress<double>(), default, synchronize: true);
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
        Assert.Equal(1, _coordinator.GetStatus().LastRun.SkipReasons["PolicyUnchanged"]);

        _config.SyncUnchangedPolicies = true;
        await _coordinator.RunAsync(new Progress<double>(), default, synchronize: true);
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
        Assert.Empty(_coordinator.GetStatus().LastRun.SkipReasons);
        await _coordinator.RunAsync(new Progress<double>(), default, synchronize: true);
        Assert.Equal(second.Key, _coordinator.GetStatus().LastRun.Cursor);

        _config.Quality = 60;
        await _coordinator.RunAsync(new Progress<double>(), default, synchronize: true);
        Assert.Equal(first.Key, _coordinator.GetStatus().LastRun.Cursor);
        Assert.Equal(3, _store.SaveCalls);
    }

    [Fact]
    public void EffectiveFingerprintTracksOverrideButIgnoresUnusedGlobalSettings()
    {
        var policy = _config.ImagePolicies.Single(p => p.Category == "People");
        policy.OverrideGlobal = true;
        policy.Quality = 60;
        policy.ResizeImages = false;
        var fingerprint = _config.Fingerprint(policy);
        _config.Quality = 99;
        _config.MaximumDimension = 4000;
        Assert.Equal(fingerprint, _config.Fingerprint(policy));
        Assert.Equal(60, _config.OptionsFor(policy).Quality);
        Assert.Equal(WebpImageConverter.MaximumDimension, _config.OptionsFor(policy).MaximumDimension);
        policy.Lossless = true;
        Assert.NotEqual(fingerprint, _config.Fingerprint(policy));
    }

    [Fact]
    public async Task PolicySyncReencodesImmutableOriginalAndDoesNotDoubleCount()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        await Run();
        var first = Assert.Single(_coordinator.GetStatus().RecentRecords);
        _config.Quality = 60;
        _config.MinimumSsim = 0;
        var expected = new WebpImageConverter().Optimize(original, _config.OptionsFor(_config.PolicyFor(image)!));
        Assert.True(expected.Success);
        await _coordinator.RunAsync(new Progress<double>(), default, synchronize: true);
        var status = _coordinator.GetStatus();
        Assert.Equal(2, status.RecentRecords.Count);
        Assert.Single(status.RecentRecords, r => r.State == "RolledBack");
        var active = Assert.Single(status.RecentRecords, r => r.State == "Committed");
        Assert.NotEqual(first.Id, active.Id);
        Assert.Equal(DurableFiles.Hash(original), active.SourceHash);
        Assert.Equal(expected.OutputBytes!, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.Equal(original.Length - expected.OutputBytes!.Length, status.VerifiedSavingsBytes);
        Assert.Equal(original.Length, status.RetainedBackupBytes);
    }

    [Fact]
    public async Task SyncWithoutOriginalReportsUnavailableAndDoesNotTranscodeCurrentWebp()
    {
        var image = _store.Add();
        _config.PreserveOriginals = false;
        await Run();
        var optimized = File.ReadAllBytes(_store.GetCurrent(image)!.Path);
        _config.Quality = 60;
        await _coordinator.RunAsync(new Progress<double>(), default, synchronize: true);
        Assert.Equal(1, _store.SaveCalls);
        Assert.Equal(optimized, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.Equal(1, _coordinator.GetStatus().LastRun.SkipReasons["OriginalUnavailable"]);
    }

    [Fact]
    public async Task OrphanOriginalFilesCountAgainstBudget()
    {
        _store.Add();
        _config.MaximumBackupBytes = 32L * 1024 * 1024;
        var path = Path.Combine(_root, "data", "originals", "orphan.original");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = File.Create(path)) file.SetLength(_config.MaximumBackupBytes);
        Assert.Equal(_config.MaximumBackupBytes, _coordinator.GetStatus().RetainedBackupBytes);
        await Assert.ThrowsAsync<IOException>(Run);
        Assert.Equal(0, _store.SaveCalls);
        Assert.True(File.Exists(path)); // Unknown originals must not be silently destroyed.
    }

    [Fact]
    public async Task InterruptedRolledBackBackupDeletionIsFinalizedIdempotently()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        var record = Prepared(image, original, [1, 2, 3]);
        record.State = "RolledBack";
        WriteRecord(record);
        _config.ImagePolicies.Single(p => p.Category == "Primary").Enabled = false;
        await Run();
        Assert.Equal(0, _coordinator.GetStatus().RetainedBackupBytes);
        Assert.True(Assert.Single(_coordinator.GetStatus().RecentRecords).BackupDeleted);
        await Run();
        Assert.Equal(original, File.ReadAllBytes(image.Path));
        Assert.Equal(0, _store.SaveCalls);
    }

    private MigrationRecord Prepared(ArtworkReference image, byte[] original, byte[] output)
    {
        var record = new MigrationRecord
        {
            Source = image, SourceHash = DurableFiles.Hash(original), OutputHash = DurableFiles.Hash(output),
            OriginalMimeType = "image/png", OriginalBytes = original.Length, OutputBytes = output.Length,
            OutputPath = _store.GetDestination(image, "image/webp"),
        };
        DurableFiles.Write(Path.Combine(_root, "data", "originals", record.Id.ToString("N") + ".original"), original);
        WriteRecord(record);
        return record;
    }

    private void WriteRecord(MigrationRecord record) => DurableFiles.WriteJson(
        Path.Combine(_root, "data", "journal", record.Id.ToString("N") + ".json"), record);

    [Fact]
    public async Task FailedExplicitRollbackLeavesDurableRecoveryIntent()
    {
        _store.Add();
        await Run();
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        _store.FailNextSaveAfterChange = true;
        await Assert.ThrowsAsync<IOException>(() => _coordinator.RollbackAsync(record.Id, default));
        Assert.Equal(1, _coordinator.GetStatus().PendingRecovery);
        await Assert.ThrowsAsync<InvalidOperationException>(Run);
        await _coordinator.RollbackAsync(record.Id, default);
        Assert.Equal(0, _coordinator.GetStatus().PendingRecovery);
        Assert.Equal(0, _coordinator.GetStatus().RetainedBackupBytes);
    }

    [Fact]
    public async Task CanaryAllowlistLeavesOtherItemsUntouched()
    {
        var selected = _store.Add();
        var untouched = _store.Add();
        var original = File.ReadAllBytes(untouched.Path);
        _config.IncludedItemIds = [selected.ItemId];
        await Run();
        Assert.Equal(1, _store.SaveCalls);
        Assert.Equal(original, File.ReadAllBytes(untouched.Path));
        Assert.Equal(selected.ItemId, Assert.Single(_coordinator.GetStatus().RecentRecords).Source.ItemId);
    }

    [Fact]
    public async Task RestoreCrashWithStaleMissingWebpReferenceCanRecover()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        await Run();
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        File.Delete(_store.GetCurrent(image)!.Path);
        File.WriteAllBytes(image.Path, original);
        record.State = "Restoring";
        DurableFiles.WriteJson(Path.Combine(_root, "data", "journal", record.Id.ToString("N") + ".json"), record);
        Assert.Equal(1, _coordinator.GetStatus().PendingRecovery);
        await _coordinator.RollbackAsync(record.Id, default);
        Assert.Equal(original, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.Equal(0, _coordinator.GetStatus().PendingRecovery);
    }

    [Fact]
    public void AdministrationRequiresElevatedPolicy()
    {
        var auth = Assert.Single(typeof(Api.JellyPController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("RequiresElevation", auth.Policy);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(101)]
    public void UnsafeQualityIsRejected(int quality)
    {
        _config.Quality = quality;
        Assert.Throws<ArgumentException>(_config.Validate);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
    private Task Run() => _coordinator.RunAsync(new Progress<double>(), default);
    private static Guid ItemId(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    private sealed class FakeArtworkStore(string root) : IArtworkStore
    {
        private readonly List<ArtworkReference> _images = new();
        public int SaveCalls { get; private set; }
        public bool FailNextSaveAfterChange { get; set; }
        public bool Busy { get; set; }
        public int InventoryCalls { get; private set; }
        public IReadOnlyCollection<Guid>? LastIncludedItemIds { get; private set; }
        public IReadOnlyCollection<Guid>? LastIncludedLibraryIds { get; private set; }
        public bool FailNextSaveBeforeReference { get; set; }
        public bool WritePartialOutput { get; set; }
        public ArtworkReference Add(bool isPerson = false, Guid? itemId = null)
        {
            var reference = new ArtworkReference(itemId ?? Guid.NewGuid(), ImageType.Primary, 0, Path.Combine(root, Guid.NewGuid().ToString("N"), "poster.png"), isPerson);
            Directory.CreateDirectory(Path.GetDirectoryName(reference.Path)!);
            using var bitmap = new SKBitmap(512, 512);
            var random = new Random(42);
            for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)(x / 2), (byte)(y / 2), (byte)(128 + random.Next(5)), 255));
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(reference.Path, encoded.ToArray());
            _images.Add(reference);
            return reference;
        }
        public IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken, IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null, Action<int>? inventoryProgress = null)
        {
            InventoryCalls++;
            LastIncludedItemIds = includedItemIds;
            LastIncludedLibraryIds = includedLibraryIds;
            inventoryProgress?.Invoke(_images.Count);
            return _images.OrderBy(i => i.Key, StringComparer.Ordinal).ToArray();
        }
        public bool IsPathReferenced(string path) => _images.Any(i => i.Path == path);
        public void EnsureIdle(Guid? itemId = null)
        {
            if (Busy) throw new InvalidOperationException("Busy");
        }
        public ArtworkReference? GetCurrent(ArtworkReference reference) => _images.SingleOrDefault(i => i.Key == reference.Key);
        public string GetDestination(ArtworkReference reference, string mimeType) => Path.ChangeExtension(reference.Path, mimeType switch
        {
            "image/webp" => ".webp", "image/png" => ".png", _ => ".jpg",
        });
        public Task<ArtworkReference> SaveAsync(ArtworkReference reference, string stagingPath, string mimeType)
        {
            SaveCalls++;
            var current = GetCurrent(reference)!;
            var path = GetDestination(reference, mimeType);
            var bytes = File.ReadAllBytes(stagingPath);
            if (FailNextSaveBeforeReference)
            {
                FailNextSaveBeforeReference = false;
                File.WriteAllBytes(path, WritePartialOutput ? bytes[..Math.Max(1, bytes.Length / 2)] : bytes);
                File.Delete(stagingPath);
                throw new IOException("Injected write failure before reference mutation");
            }
            File.WriteAllBytes(path, bytes);
            if (current.Path != path) File.Delete(current.Path);
            File.Delete(stagingPath);
            var saved = current with { Path = path };
            _images[_images.FindIndex(i => i.Key == current.Key)] = saved;
            if (FailNextSaveAfterChange)
            {
                FailNextSaveAfterChange = false;
                throw new IOException("Injected persistence failure");
            }
            return Task.FromResult(saved);
        }
    }
}
