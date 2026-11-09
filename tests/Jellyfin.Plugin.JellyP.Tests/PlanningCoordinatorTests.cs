using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using System.Reflection;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Infrastructure;
using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class PlanningCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyp-planning-" + Guid.NewGuid().ToString("N"));
    private readonly PluginConfiguration _config = new()
    {
        Enabled = true,
        DryRun = false,
        MinimumSavingsBytes = 0,
        MinimumSavingsRatio = 0,
        MinimumSsim = 0,
        MinimumFreeSpaceBytes = 64L * 1024 * 1024,
        BackupRetentionDays = 0,
    };
    private readonly PlanningArtworkStore _store;
    private readonly OptimizationCoordinator _coordinator;

    public PlanningCoordinatorTests()
    {
        Directory.CreateDirectory(_root);
        _store = new PlanningArtworkStore(Path.Combine(_root, "artwork"));
        _coordinator = NewCoordinator();
    }

    [Fact]
    public void LegacyDefaultsProjectWithoutWritingAndSettingsSavesStayIsolated()
    {
        Assert.Null(_config.ManualDefaults);
        var initial = _coordinator.GetSettings();
        Assert.Equal("legacy", initial.DefaultsSource);
        Assert.True(initial.LegacyDefaultsWarning);
        Assert.Null(_config.ManualDefaults);

        var defaults = initial.Defaults;
        defaults.Quality = 77;
        var globals = initial.Globals;
        globals.MinimumFreeSpaceBytes = 128L * 1024 * 1024;
        globals.BackupRetentionDays = 40;
        var savedDefaults = _coordinator.SaveDefaults(new SettingsDefaultsRequest { Defaults = defaults, Globals = globals });
        Assert.Equal("manual", savedDefaults.DefaultsSource);
        Assert.NotNull(_config.ManualDefaults);
        Assert.Equal(77, _config.ManualDefaults!.Quality);
        Assert.Equal(128L * 1024 * 1024, _config.MinimumFreeSpaceBytes);
        Assert.Equal(40, _config.BackupRetentionDays);
        Assert.True(_config.Enabled); // Automation legacy fields are unchanged.
        Assert.Equal(85, _config.Quality);

        var automation = savedDefaults.Automation;
        automation.Enabled = true;
        automation.DryRun = false;
        automation.Quality = 61;
        automation.BatchSize = 12;
        var savedAutomation = _coordinator.SaveAutomation(new SettingsAutomationRequest { Automation = automation });
        Assert.Equal(61, _config.Quality);
        Assert.Equal(12, _config.BatchSize);
        Assert.Equal(77, _config.ManualDefaults!.Quality);
        Assert.Equal(128L * 1024 * 1024, _config.MinimumFreeSpaceBytes);
        Assert.Equal(61, savedAutomation.Automation.Quality);

        automation.PreserveOriginals = false;
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.SaveAutomation(new SettingsAutomationRequest { Automation = automation, AuthorizePermanentRemoval = false }));
        Assert.Equal(61, _config.Quality);
    }

    [Fact]
    public void AnalyzeClassifiesMixedImagesWithoutConfigurationCursorLastRunOrArtworkWrites()
    {
        var convert = _store.Add();
        var regenerate = _store.Add();
        var same = _store.Add();
        var external = _store.AddWebp();
        _store.AddRemote();
        _store.AddMissing();
        _store.AddExcluded();
        var sharedPath = Path.Combine(_store.Root, "shared", "folder.png");
        _store.Add(explicitPath: sharedPath);
        _store.Add(explicitPath: sharedPath);

        _config.Quality = 85;
        _config.IncludedItemIds = [regenerate.ItemId];
        _config.ScopeMode = "items";
        Assert.True(_coordinator.RunAsync(new Progress<double>(), default).IsCompleted);
        _config.Quality = 60;
        _config.IncludedItemIds = [same.ItemId];
        Assert.True(_coordinator.RunAsync(new Progress<double>(), default).IsCompleted);
        _config.ScopeMode = "legacy";
        _config.IncludedItemIds = [];

        var configBefore = JsonSerializer.Serialize(_config);
        var lastRunPath = Path.Combine(_root, "migration", "last-run.json");
        var cursorPath = Path.Combine(_root, "migration", "optimize-cursor-apply.json");
        var lastRunBefore = File.Exists(lastRunPath) ? File.ReadAllBytes(lastRunPath) : null;
        var cursorBefore = File.Exists(cursorPath) ? File.ReadAllBytes(cursorPath) : null;
        var artworkBefore = File.ReadAllBytes(convert.Path);

        var summary = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "all",
            BatchSize = 20,
            GeneralPolicy = new PlanGeneralPolicy { Quality = 60, MaximumDimension = 2400, MinimumSsim = 0 },
            PreserveOriginals = true,
        }, default);

        Assert.Equal(configBefore, JsonSerializer.Serialize(_config));
        Assert.Equal(lastRunBefore, File.Exists(lastRunPath) ? File.ReadAllBytes(lastRunPath) : null);
        Assert.Equal(cursorBefore, File.Exists(cursorPath) ? File.ReadAllBytes(cursorPath) : null);
        Assert.Equal(artworkBefore, File.ReadAllBytes(convert.Path));
        Assert.Equal(2, summary.Proposed);
        Assert.Equal(1, summary.Convert);
        Assert.Equal(1, summary.Regenerate);

        var page = _coordinator.GetPlanImages(summary.PlanId, 0, 200);
        Assert.DoesNotContain(_root, JsonSerializer.Serialize(page));
        Assert.Contains(page.Items, image => image.Key == convert.Key && image.Action == "Convert");
        Assert.Contains(page.Items, image => image.Key == regenerate.Key && image.Action == "Regenerate");
        Assert.Contains(page.Items, image => image.Key == same.Key && image.Reason == "AlreadyUsesPolicy");
        Assert.Contains(page.Items, image => image.Key == external.Key && image.Reason == "ExternalWebpNotManaged");
        Assert.Contains(page.Items, image => image.State == "Remote" && image.Reason == "RemoteReference");
        Assert.Contains(page.Items, image => image.State == "Missing" && image.Reason == "MissingFile");
        Assert.Contains(page.Items, image => image.State == "Excluded");
        Assert.Contains(page.Items, image => image.State == "Shared" && image.Reason == "SharedReference");
    }

    [Fact]
    public void SelectedItemScopeStillRejectsAReferenceSharedOutsideTheScope()
    {
        var selected = _store.Add();
        _store.Add(explicitPath: selected.Path);
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [selected.ItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default);
        var image = Assert.Single(_coordinator.GetPlanImages(plan.PlanId, 0, 10).Items);
        Assert.Equal("Shared", image.State);
        Assert.Equal("SharedReference", image.Reason);
        Assert.Equal(0, plan.Proposed);
    }

    [Fact]
    public void BatchLimitIsExplicitAndRealSsimFailureKeepsImage()
    {
        var first = _store.Add();
        _store.Add();
        var limited = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "all",
            BatchSize = 1,
            GeneralPolicy = new PlanGeneralPolicy { Quality = 60, MinimumSsim = 0 },
            PreserveOriginals = true,
        }, default);
        Assert.Equal(1, limited.Evaluated);
        Assert.Equal(1, limited.NotEvaluated);
        Assert.Contains(limited.Warnings, warning => warning.Code == "PlanBatchLimit");

        var skipped = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [first.ItemId],
            BatchSize = 1,
            GeneralPolicy = new PlanGeneralPolicy { Quality = 10, MinimumSsim = 1 },
            PreserveOriginals = true,
        }, default);
        var image = Assert.Single(_coordinator.GetPlanImages(skipped.PlanId, 0, 10).Items);
        Assert.Equal("Keep", image.Action);
        Assert.NotNull(image.Reason);
        Assert.NotEqual("None", image.Reason);
    }

    [Fact]
    public async Task SavingAutomationAfterAnalysisDoesNotChangeManualPlanSnapshot()
    {
        var image = _store.Add();
        var defaults = _coordinator.GetSettings();
        _coordinator.SaveDefaults(new SettingsDefaultsRequest { Defaults = defaults.Defaults, Globals = defaults.Globals });
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [image.ItemId],
            BatchSize = 10,
            GeneralPolicy = new PlanGeneralPolicy { Quality = 60, MaximumDimension = 2400, MinimumSsim = 0 },
            PreserveOriginals = true,
        }, default);
        var planned = Assert.Single(_coordinator.GetPlanImages(plan.PlanId, 0, 10).Items);
        var automation = _coordinator.GetSettings().Automation;
        automation.Quality = 99;
        automation.BatchSize = 7;
        _coordinator.SaveAutomation(new SettingsAutomationRequest { Automation = automation });
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);

        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = [planned.Key] });
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        Assert.Equal("Completed", _coordinator.GetOperation(receipt.OperationId, 0, 10).Operation.State);
        Assert.Equal(planned.OutputHash, DurableFiles.Hash(File.ReadAllBytes(_store.GetCurrent(image)!.Path)));
        Assert.Equal(99, _config.Quality);
    }

    [Fact]
    public void SavingExplicitDefaultsAfterAnalysisRejectsApplyUntilReanalysis()
    {
        _store.Add();
        var settings = _coordinator.GetSettings();
        _coordinator.SaveDefaults(new SettingsDefaultsRequest { Defaults = settings.Defaults, Globals = settings.Globals });
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        var changed = _coordinator.GetSettings();
        changed.Defaults.Quality = 61;
        _coordinator.SaveDefaults(new SettingsDefaultsRequest { Defaults = changed.Defaults, Globals = changed.Globals });
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = keys }));
    }

    [Fact]
    public async Task ExactSubsetApplyIsIdempotentAndDifferentSelectionConflicts()
    {
        var first = _store.Add();
        var second = _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys;
        Assert.Equal(2, keys.Count);
        var selected = new[] { first.Key };

        var review = _coordinator.ReviewPlan(plan.PlanId, new PlanReviewRequest { Keys = selected, Limit = 1 });
        Assert.Equal(1, review.TotalSelected);
        Assert.Equal(1, review.Billing.SelectedImages);
        Assert.Equal(1, review.Billing.ActiveBytesBefore > 0 ? 1 : 0);

        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = selected });
        Assert.Equal("Queued", receipt.State);
        var replay = _coordinator.ExistingApplyReceipt(plan.PlanId, selected);
        Assert.NotNull(replay);
        Assert.Equal(receipt.OperationId, replay!.OperationId);
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.ExistingApplyReceipt(plan.PlanId, new[] { second.Key }));

        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Completed", detail.Operation.State);
        Assert.Equal(1, detail.Operation.Changed);

        var after = _coordinator.ExistingApplyReceipt(plan.PlanId, selected);
        Assert.NotNull(after);
        Assert.Equal(receipt.OperationId, after!.OperationId);
        Assert.Throws<JellyPRequestException>(() => _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = new[] { second.Key } }));
        Assert.EndsWith(".webp", _store.GetCurrent(first)!.Path);
        Assert.EndsWith(".png", _store.GetCurrent(second)!.Path);
    }

    [Fact]
    public void FailedDispatchReleasesReservationWithoutMutation()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        var first = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = keys });
        _coordinator.RejectQueuedOperation(first.OperationId);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Throws<JellyPRequestException>(() => _coordinator.GetOperation(first.OperationId, 0, 10));
        var second = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = keys });
        Assert.NotEqual(first.OperationId, second.OperationId);
    }

    [Fact]
    public async Task CancelBeforeNativeStartLeavesPlanReadyAndNeverFallsBackToAutomation()
    {
        _store.Add();
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        Assert.True(_coordinator.RequestCancelOperation(receipt.OperationId));
        Assert.Equal("Cancelled", _coordinator.GetOperation(receipt.OperationId, 0, 10).Operation.State);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task ChangedSourceIsInvalidatedWithoutSubstitute()
    {
        var changed = _store.Add();
        var valid = _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        File.WriteAllBytes(changed.Path, [1, 2, 3]);
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("CompletedWithErrors", detail.Operation.State);
        Assert.Equal(1, detail.Operation.Changed);
        Assert.Equal(1, detail.Operation.Invalidated);
        Assert.Contains(detail.Items, item => item.Key == changed.Key && item.Reason == "SourceChanged");
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(changed.Path));
        Assert.EndsWith(".webp", _store.GetCurrent(valid)!.Path);
    }

    [Fact]
    public async Task SharedReferenceAtApplyInvalidatesOnlyThatChoice()
    {
        var first = _store.Add();
        var second = _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        _store.Add(explicitPath: first.Path);
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Contains(detail.Items, item => item.Key == first.Key && item.Reason == "SharedReference");
        Assert.Contains(detail.Items, item => item.Key == second.Key && item.Status == "Changed");
        Assert.Equal(1, detail.Operation.Changed);
        Assert.Equal(1, detail.Operation.Invalidated);
    }

    [Fact]
    public async Task SharedActiveOutputIsNotOfferedForRestoreOrRetirement()
    {
        var image = _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        var current = _store.GetCurrent(image)!;
        _store.Add(explicitPath: current.Path);
        var all = _coordinator.GetBackups("all", 0, 50, default);
        var row = Assert.Single(all.Items);
        Assert.False(row.Restorable);
        Assert.Equal("SharedReference", row.Reason);
        Assert.Empty(_coordinator.GetBackups("restorable", 0, 50, default).Items);
        var operations = _coordinator.GetOperations(0, 50).Total;
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.QueueRestore(new BackupBatchRequest { RecordIds = [record.Id] }, default));
        Assert.Equal(operations, _coordinator.GetOperations(0, 50).Total);
    }

    [Fact]
    public async Task UncertainBackupIsPreservedAndExcludedFromBatchSelection()
    {
        var image = _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        File.Delete(Path.Combine(_root, "migration", "originals", record.Id.ToString("N") + ".original"));
        Assert.Empty(_coordinator.GetBackups("restorable", 0, 50, default).Items);
        var operations = _coordinator.GetOperations(0, 50).Total;
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.QueueRestore(new BackupBatchRequest { RecordIds = [record.Id] }, default));
        Assert.Equal(operations, _coordinator.GetOperations(0, 50).Total);
        Assert.EndsWith(".webp", _store.GetCurrent(image)!.Path);
    }

    [Fact]
    public async Task BatchRestoreUsesOneGlobalReferenceScanForTheWholeBatch()
    {
        _store.Add();
        _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var records = _coordinator.GetBackups("restorable", 0, 50, default).Items;
        var receipt = _coordinator.QueueRestore(new BackupBatchRequest { RecordIds = records.Select(row => row.RecordId).ToArray() }, default);
        _store.ResetCountReferenceCalls();
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        Assert.Equal("Completed", _coordinator.GetOperation(receipt.OperationId, 0, 10).Operation.State);
        Assert.Equal(1, _store.CountReferenceCalls);
    }

    [Fact]
    public async Task BatchRestoreAndRetireUseExactIdsAndRejectUnknownWithoutWrites()
    {
        var first = _store.Add();
        var second = _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var restorable = _coordinator.GetBackups("restorable", 0, 50, default);
        Assert.Equal(2, restorable.Total);
        var firstRecord = restorable.Items.Single(item => item.ItemId == first.ItemId);
        var secondRecord = restorable.Items.Single(item => item.ItemId == second.ItemId);

        var operationsBefore = _coordinator.GetOperations(0, 50).Total;
        Assert.Throws<JellyPRequestException>(() => _coordinator.QueueRestore(new BackupBatchRequest
        {
            RecordIds = [firstRecord.RecordId, Guid.NewGuid()],
        }, default));
        Assert.Equal(operationsBefore, _coordinator.GetOperations(0, 50).Total);
        Assert.EndsWith(".webp", _store.GetCurrent(first)!.Path);

        var restored = _coordinator.QueueRestore(new BackupBatchRequest { RecordIds = [firstRecord.RecordId] }, default);
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var restoredDetail = _coordinator.GetOperation(restored.OperationId, 0, 10);
        Assert.Equal("Completed", restoredDetail.Operation.State);
        Assert.Equal("Restored", Assert.Single(restoredDetail.Items).Status);
        Assert.EndsWith(".png", _store.GetCurrent(first)!.Path);
        Assert.EndsWith(".webp", _store.GetCurrent(second)!.Path);

        var retirable = _coordinator.GetBackups("retirable", 0, 50, default);
        var retireRecord = Assert.Single(retirable.Items);
        Assert.Equal(secondRecord.RecordId, retireRecord.RecordId);
        var retired = _coordinator.QueueRetire(new BackupRetireRequest
        {
            RecordIds = [retireRecord.RecordId],
            AuthorizePermanentRemoval = true,
        }, default);
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var retiredDetail = _coordinator.GetOperation(retired.OperationId, 0, 10);
        Assert.Equal("Retired", Assert.Single(retiredDetail.Items).Status);
        Assert.Equal(0, _coordinator.GetStatus().RetainedBackupBytes);
    }

    [Fact]
    public async Task RegenerationUsesOriginalBytesAndNotTheCurrentWebp()
    {
        var image = _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var old = Assert.Single(_coordinator.GetStatus().RecentRecords);
        var oldWebp = File.ReadAllBytes(_store.GetCurrent(image)!.Path);
        _config.Quality = 60;
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [image.ItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default);
        var planned = Assert.Single(_coordinator.GetPlanImages(plan.PlanId, 0, 10).Items);
        Assert.Equal("Regenerate", planned.Action);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = [planned.Key] });
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        Assert.Equal("Completed", _coordinator.GetOperation(receipt.OperationId, 0, 10).Operation.State);
        var active = _coordinator.GetStatus().RecentRecords.Single(record => record.State == "Committed" && record.Source.ItemId == image.ItemId);
        Assert.Equal(old.SourceHash, active.SourceHash);
        Assert.Equal(planned.OutputHash, active.OutputHash);
        Assert.NotEqual(DurableFiles.Hash(oldWebp), active.OutputHash);
        Assert.Equal(planned.OutputHash, DurableFiles.Hash(File.ReadAllBytes(_store.GetCurrent(image)!.Path)));
    }

    [Fact]
    public async Task RetentionProtectsSelectedRegenerationAndRequiresSeparateAuthorization()
    {
        var retained = _store.Add();
        var regenerated = _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var records = _coordinator.GetStatus().RecentRecords;
        Assert.Equal(2, records.Count);
        var retainedRecord = records.Single(record => record.Source.ItemId == retained.ItemId);
        var journalPath = Path.Combine(_root, "migration", "journal", retainedRecord.Id.ToString("N") + ".json");
        retainedRecord.CompletedUtc = DateTime.UtcNow.AddDays(-30);
        DurableFiles.WriteJson(journalPath, retainedRecord);
        _config.Quality = 60;
        _config.BackupRetentionDays = 10;
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [regenerated.ItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        Assert.Single(keys);
        var review = _coordinator.ReviewPlan(plan.PlanId, new PlanReviewRequest { Keys = keys });
        Assert.Equal(1, review.Retention.ProtectedCount);
        Assert.Equal(1, review.Retention.EligibleCount);
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = keys, AuthorizeRetentionPurge = false }));
        Assert.NotEmpty(review.Retention.Fingerprint);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = keys,
            AuthorizeRetentionPurge = true,
            AuthorizeRetentionFingerprint = review.Retention.Fingerprint,
        });
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal(1, detail.Operation.Changed);
        var after = _coordinator.GetStatus().RecentRecords;
        Assert.True(after.Single(record => record.Id == retainedRecord.Id).BackupDeleted);
        Assert.Equal(0, _coordinator.GetStatus().RetainedBackupBytes - after.Single(record => record.Source.ItemId == regenerated.ItemId
            && record.State == "Committed").OriginalBytes);
    }

    [Fact]
    public async Task DiscardingPlanRequiresPermanentAuthorizationAndRetiresBackup()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = false }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = keys, AuthorizePermanentRemoval = false }));
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = keys, AuthorizePermanentRemoval = true });
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Completed", detail.Operation.State);
        Assert.Equal(0, _coordinator.GetStatus().RetainedBackupBytes);
        Assert.True(Assert.Single(_coordinator.GetStatus().RecentRecords).BackupDeleted);
    }

    [Fact]
    public void RestartInterruptionIsRecordedAndNotQueuedForAutomaticReplay()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var saves = _store.SaveCalls;

        var restarted = NewCoordinator();
        var detail = restarted.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Interrupted", detail.Operation.State);
        Assert.Contains("not replayed", detail.Operation.Message);
        Assert.Equal("Ready", restarted.GetPlan(plan.PlanId).Status);
        Assert.Equal(saves, _store.SaveCalls);
    }

    [Fact]
    public async Task ExecutionRechecksDefaultsAndReleasesAnUnmutatedPlan()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        _config.Quality = 61; // legacy projection changes the effective manual defaults
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Failed", detail.Operation.State);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Equal(0, detail.Operation.Changed);
    }

    [Fact]
    public async Task RepeatedBackdropSlotsApplyAndRestoreOnlyTheExactIndex()
    {
        _config.ImagePolicies.Single(policy => policy.Category == "Backdrop").Enabled = true;
        var itemId = Guid.NewGuid();
        var primary = _store.Add(itemId: itemId);
        var first = _store.Add(itemId: itemId, type: ImageType.Backdrop, index: 0);
        var target = _store.Add(itemId: itemId, type: ImageType.Backdrop, index: 1);
        var later = _store.Add(itemId: itemId, type: ImageType.Backdrop, index: 2);
        var firstBytes = File.ReadAllBytes(first.Path);
        var laterBytes = File.ReadAllBytes(later.Path);
        var targetBytes = File.ReadAllBytes(target.Path);

        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [itemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default);
        var targetImage = Assert.Single(_coordinator.GetPlanImages(plan.PlanId, 0, 20).Items, image => image.Key == target.Key);
        Assert.Equal("Convert", targetImage.Action);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = [targetImage.Key] });
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal(1, detail.Operation.Changed);
        Assert.Equal(0, detail.Operation.Invalidated);
        Assert.EndsWith(".webp", _store.GetCurrent(target)!.Path);
        Assert.Equal(firstBytes, File.ReadAllBytes(_store.GetCurrent(first)!.Path));
        Assert.Equal(laterBytes, File.ReadAllBytes(_store.GetCurrent(later)!.Path));
        Assert.EndsWith(".png", _store.GetCurrent(primary)!.Path); // Primary was not selected by the exact key.

        var record = Assert.Single(_coordinator.GetBackups("restorable", 0, 50, default).Items);
        var restore = _coordinator.QueueRestore(new BackupBatchRequest { RecordIds = [record.RecordId] }, default);
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        Assert.Equal("Restored", Assert.Single(_coordinator.GetOperation(restore.OperationId, 0, 10).Items).Status);
        Assert.Equal(targetBytes, File.ReadAllBytes(_store.GetCurrent(target)!.Path));
        Assert.Equal(firstBytes, File.ReadAllBytes(_store.GetCurrent(first)!.Path));
        Assert.Equal(laterBytes, File.ReadAllBytes(_store.GetCurrent(later)!.Path));
    }

    [Fact]
    public void OldPlanIndexSchemaIsRejectedBeforeApply()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var path = Path.Combine(_root, "migration", "plans", plan.PlanId.ToString("N") + ".json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        document["Version"] = 1;
        File.WriteAllText(path, document.ToJsonString());
        var error = Assert.Throws<JellyPRequestException>(() => _coordinator.GetPlan(plan.PlanId));
        Assert.Equal("PlanSchemaChanged", error.Code);
    }

    [Fact]
    public void InventoryFailureDoesNotExposeUnknownExceptionPaths()
    {
        _store.Add();
        _store.ThrowInventoryPath = true;
        var error = Assert.Throws<JellyPRequestException>(() =>
            _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default));
        Assert.Equal("InventoryUnavailable", error.Code);
        Assert.DoesNotContain("/private", error.Message);
    }

    [Fact]
    public async Task OperationFailureDoesNotExposeUnknownExceptionPaths()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        _store.ThrowSavePath = true;
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.DoesNotContain("/private", System.Text.Json.JsonSerializer.Serialize(detail));
        Assert.All(detail.Items, item => Assert.DoesNotContain("/private", item.Reason ?? string.Empty));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Remote")]
    public async Task UnsafeSiblingNeverOffersRestoreOrCallsSave(string siblingState)
    {
        var image = _store.Add();
        _store.Add(itemId: image.ItemId, state: siblingState,
            explicitPath: siblingState == "Remote" ? "https://example.invalid/sibling.jpg" : null,
            type: ImageType.Backdrop);
        var record = SeedCommitted(image, DateTime.UtcNow);
        var before = File.ReadAllBytes(image.Path);
        Assert.False(_store.IsSlotSafeForMutation(image));
        var row = Assert.Single(_coordinator.GetBackups("all", 0, 50, default).Items);
        Assert.False(row.Restorable);
        Assert.Contains("OtherSlotUnavailable", row.Reason);
        Assert.Throws<JellyPRequestException>(() =>
            _coordinator.QueueRestore(new BackupBatchRequest { RecordIds = [record.Id] }, default));
        var saves = _store.SaveCalls;
        await Assert.ThrowsAsync<IOException>(() => _coordinator.RollbackAsync(record.Id, default, authorizedManual: true));
        Assert.Equal(saves, _store.SaveCalls);
        Assert.Equal(before, File.ReadAllBytes(image.Path));
    }

    [Fact]
    public async Task SharedPathWithExcludedSiblingIsNeverRestoredByLegacyRollback()
    {
        var shared = Path.Combine(_store.Root, "shared", "poster.png");
        var image = _store.Add(explicitPath: shared);
        _store.Add(itemId: image.ItemId, state: "Missing");
        _store.Add(explicitPath: shared);
        var record = SeedCommitted(image, DateTime.UtcNow);
        var before = File.ReadAllBytes(shared);

        var backup = Assert.Single(_coordinator.GetBackups("all", 0, 50, default).Items);
        Assert.False(backup.Restorable);
        Assert.Equal("SharedReference", backup.Reason);
        await Assert.ThrowsAsync<IOException>(() => _coordinator.RollbackAsync(record.Id, default, authorizedManual: true));

        Assert.Equal(before, File.ReadAllBytes(shared));
        Assert.Equal("Committed", DurableFiles.ReadJson<MigrationRecord>(
            Path.Combine(_root, "migration", "journal", record.Id.ToString("N") + ".json")).State);
    }

    [Fact]
    public async Task CancelWhileWaitingPersistsEvenAgainstConcurrentProgressSaves()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        _store.Busy = true;
        var running = _coordinator.RunNativeAsync(new Progress<double>(), default);
        await WaitUntil(() => _coordinator.GetOperation(receipt.OperationId, 0, 1).Operation.Phase == "WaitingForMetadata");
        Assert.True(_coordinator.RequestCancelOperation(receipt.OperationId));
        await running.WaitAsync(TimeSpan.FromSeconds(15));
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 1);
        Assert.Equal("Cancelled", detail.Operation.State);
        Assert.True(detail.Operation.CancelRequested);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task CancelDuringApplyValidationLeavesPlanReadyAndNeverMutates()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _store.OnCountPathReferences = () =>
        {
            entered.Set();
            _ = release.Wait(TimeSpan.FromSeconds(10));
        };
        try
        {
            var running = Task.Run(() => _coordinator.RunNativeAsync(new Progress<double>(), default));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(_coordinator.RequestCancelOperation(receipt.OperationId));
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            release.Set();
            _store.OnCountPathReferences = null;
        }

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Cancelled", detail.Operation.State);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Empty(_coordinator.GetStatus().RecentRecords);
    }

    [Fact]
    public async Task CancelAfterMetadataWaitDoesNotStartTheNextTransaction()
    {
        var first = _store.Add();
        var second = _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var ordered = _coordinator.GetOperation(receipt.OperationId, 0, 10).Items;
        Assert.Equal(2, ordered.Count);
        var blockedItemId = ordered[1].ItemId;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _store.OnEnsureIdle = itemId =>
        {
            if (itemId == blockedItemId)
            {
                entered.Set();
                _ = release.Wait(TimeSpan.FromSeconds(10));
            }
        };
        try
        {
            var running = Task.Run(() => _coordinator.RunNativeAsync(new Progress<double>(), default));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            Assert.True(_coordinator.RequestCancelOperation(receipt.OperationId));
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            release.Set();
            _store.OnEnsureIdle = null;
        }

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Cancelled", detail.Operation.State);
        Assert.Equal("Consumed", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Equal(1, _store.SaveCalls);
        Assert.Equal("Changed", detail.Items.Single(item => item.Key == ordered[0].Key).Status);
        Assert.Equal("NotStarted", detail.Items.Single(item => item.Key == ordered[1].Key).Status);
        var changed = ordered[0].Key == first.Key ? first : second;
        var untouched = ordered[0].Key == first.Key ? second : first;
        Assert.EndsWith(".webp", _store.GetCurrent(changed)!.Path);
        Assert.EndsWith(".png", _store.GetCurrent(untouched)!.Path);
    }

    [Fact]
    public async Task CancelDuringFirstApplyMetadataWaitLeavesPlanReadyAndNeverMutates()
    {
        var image = _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _store.OnEnsureIdle = itemId =>
        {
            if (itemId == image.ItemId)
            {
                entered.Set();
                _ = release.Wait(TimeSpan.FromSeconds(10));
            }
        };
        try
        {
            var running = Task.Run(() => _coordinator.RunNativeAsync(new Progress<double>(), default));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(_coordinator.RequestCancelOperation(receipt.OperationId));
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            release.Set();
            _store.OnEnsureIdle = null;
        }

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Cancelled", detail.Operation.State);
        Assert.False(detail.Operation.MutationStarted);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Empty(_coordinator.GetStatus().RecentRecords);
    }

    [Fact]
    public async Task CancelDuringFirstRetentionWaitLeavesPlanReadyAndNoRemoval()
    {
        var retained = _store.Add();
        var regenerated = _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var records = _coordinator.GetStatus().RecentRecords;
        var retainedRecord = records.Single(record => record.Source.ItemId == retained.ItemId);
        retainedRecord.CompletedUtc = DateTime.UtcNow.AddDays(-30);
        DurableFiles.WriteJson(
            Path.Combine(_root, "migration", "journal", retainedRecord.Id.ToString("N") + ".json"), retainedRecord);
        var backupPath = Path.Combine(_root, "migration", "originals", retainedRecord.Id.ToString("N") + ".original");
        Assert.True(File.Exists(backupPath));
        _config.Quality = 60;
        _config.BackupRetentionDays = 10;
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [regenerated.ItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        var review = _coordinator.ReviewPlan(plan.PlanId, new PlanReviewRequest { Keys = keys });
        Assert.Equal(1, review.Retention.EligibleCount);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = keys,
            AuthorizeRetentionPurge = true,
            AuthorizeRetentionFingerprint = review.Retention.Fingerprint,
        });
        var savesBefore = _store.SaveCalls;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _store.OnEnsureIdle = itemId =>
        {
            if (itemId == retained.ItemId)
            {
                entered.Set();
                _ = release.Wait(TimeSpan.FromSeconds(10));
            }
        };
        try
        {
            var running = Task.Run(() => _coordinator.RunNativeAsync(new Progress<double>(), default));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(_coordinator.RequestCancelOperation(receipt.OperationId));
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            release.Set();
            _store.OnEnsureIdle = null;
        }

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Cancelled", detail.Operation.State);
        Assert.False(detail.Operation.MutationStarted);
        Assert.Equal(0, detail.Operation.RetentionPurgedCount);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(savesBefore, _store.SaveCalls);
    }

    [Fact]
    public async Task CancelBatchRestoreDuringFirstMetadataWaitLeavesBackupAndNoRestore()
    {
        var image = _store.Add();
        await _coordinator.RunAsync(new Progress<double>(), default);
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        var backupPath = Path.Combine(_root, "migration", "originals", record.Id.ToString("N") + ".original");
        var receipt = _coordinator.QueueRestore(new BackupBatchRequest { RecordIds = [record.Id] }, default);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _store.OnEnsureIdle = itemId =>
        {
            if (itemId == image.ItemId)
            {
                entered.Set();
                _ = release.Wait(TimeSpan.FromSeconds(10));
            }
        };
        try
        {
            var running = Task.Run(() => _coordinator.RunNativeAsync(new Progress<double>(), default));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(_coordinator.RequestCancelOperation(receipt.OperationId));
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            release.Set();
            _store.OnEnsureIdle = null;
        }

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Cancelled", detail.Operation.State);
        Assert.Equal("NotStarted", Assert.Single(detail.Items).Status);
        Assert.EndsWith(".webp", _store.GetCurrent(image)!.Path);
        Assert.True(File.Exists(backupPath));
        Assert.Equal("Committed", Assert.Single(_coordinator.GetStatus().RecentRecords).State);
    }

    [Fact]
    public void FinalizedOperationIsNotRevertedByCancellationSnapshot()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var operationPath = Path.Combine(_root, "migration", "operations", receipt.OperationId.ToString("N") + ".json");
        var stale = JsonNode.Parse(File.ReadAllText(operationPath))!;
        stale["State"] = "Running";
        stale["Phase"] = "Processing";
        stale["MutationStarted"] = true;
        stale["RetentionPurgedCount"] = 0;
        ((JsonArray)stale["Items"]!)[0]!["Status"] = "Pending";
        File.WriteAllText(operationPath, stale.ToJsonString());

        var completed = JsonNode.Parse(File.ReadAllText(operationPath))!;
        completed["State"] = "Completed";
        completed["Phase"] = "Completed";
        completed["Message"] = "Operation completed.";
        completed["CompletedUtc"] = DateTime.UtcNow.ToString("O");
        completed["RetentionPurgedCount"] = 2;
        ((JsonArray)completed["Items"]!)[0]!["Status"] = "Changed";
        SetCancellationHook(() => File.WriteAllText(operationPath, completed.ToJsonString()));
        try
        {
            Assert.False(_coordinator.RequestCancelOperation(receipt.OperationId));
        }
        finally
        {
            SetCancellationHook(null);
        }

        var final = JsonNode.Parse(File.ReadAllText(operationPath))!;
        Assert.Equal("Completed", final["State"]!.GetValue<string>());
        Assert.Equal("Completed", final["Phase"]!.GetValue<string>());
        Assert.NotNull(final["CompletedUtc"]);
        Assert.Equal(2, final["RetentionPurgedCount"]!.GetValue<int>());
        Assert.Equal("Changed", ((JsonArray)final["Items"]!)[0]!["Status"]!.GetValue<string>());
        Assert.False(final["CancelRequested"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ConcurrentCancellationRequestsPreserveProgress()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var operationPath = Path.Combine(_root, "migration", "operations", receipt.OperationId.ToString("N") + ".json");
        var running = JsonNode.Parse(File.ReadAllText(operationPath))!;
        running["State"] = "Running";
        running["Phase"] = "Processing";
        running["MutationStarted"] = true;
        running["RetentionPurgedCount"] = 3;
        ((JsonArray)running["Items"]!)[0]!["Status"] = "Changed";
        File.WriteAllText(operationPath, running.ToJsonString());

        var results = await Task.WhenAll(
            Task.Run(() => _coordinator.RequestCancelOperation(receipt.OperationId)),
            Task.Run(() => _coordinator.RequestCancelOperation(receipt.OperationId)));

        Assert.All(results, value => Assert.True(value));
        var final = JsonNode.Parse(File.ReadAllText(operationPath))!;
        Assert.Equal("Running", final["State"]!.GetValue<string>());
        Assert.True(final["CancelRequested"]!.GetValue<bool>());
        Assert.True(final["MutationStarted"]!.GetValue<bool>());
        Assert.Equal(3, final["RetentionPurgedCount"]!.GetValue<int>());
        Assert.Equal("Changed", ((JsonArray)final["Items"]!)[0]!["Status"]!.GetValue<string>());
    }

    [Fact]
    public async Task PrePublicationMarkerFailureReleasesPlanWithoutMutation()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        Action<bool, bool, string> hook = (mutationStarted, published, state) =>
        {
            if (mutationStarted && state == "Running" && !published)
                throw new IOException("Injected pre-publication marker failure");
        };
        SetOperationSaveHook(hook);
        try
        {
            await _coordinator.RunNativeAsync(new Progress<double>(), default);
        }
        finally
        {
            SetOperationSaveHook(null);
        }

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Failed", detail.Operation.State);
        Assert.False(detail.Operation.MutationStarted);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Empty(_coordinator.GetStatus().RecentRecords);
    }

    [Fact]
    public async Task PostPublicationMarkerFailureKeepsPlanReservedConservatively()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        Action<bool, bool, string> hook = (mutationStarted, published, state) =>
        {
            if (mutationStarted && state == "Running" && published)
                throw new IOException("Injected post-publication marker failure");
        };
        SetOperationSaveHook(hook);
        try
        {
            await _coordinator.RunNativeAsync(new Progress<double>(), default);
        }
        finally
        {
            SetOperationSaveHook(null);
        }

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Failed", detail.Operation.State);
        Assert.True(detail.Operation.MutationStarted);
        Assert.Equal("Reserved", _coordinator.GetPlan(plan.PlanId).Status);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Empty(_coordinator.GetStatus().RecentRecords);
    }

    [Fact]
    public async Task ApplyPersistenceFailureRestoresOriginalAndLeavesNoRecovery()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        _store.FailNextSaveAfterChange = true;
        await _coordinator.RunNativeAsync(new Progress<double>(), default);

        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("CompletedWithErrors", detail.Operation.State);
        Assert.Equal("Failed", Assert.Single(detail.Items).Status);
        Assert.Equal(original, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.Equal("RolledBack", Assert.Single(_coordinator.GetStatus().RecentRecords).State);
        Assert.Equal(0, _coordinator.GetStatus().PendingRecovery);
        Assert.Empty(_coordinator.GetBackups("restorable", 0, 50, default).Items);
    }

    [Fact]
    public async Task RestartDuringRestoreRequiresExplicitRecoveryAndRemovesVerifiedOutput()
    {
        var image = _store.Add();
        var original = File.ReadAllBytes(image.Path);
        await _coordinator.RunAsync(new Progress<double>(), default);
        var record = Assert.Single(_coordinator.GetStatus().RecentRecords);
        var outputPath = _store.GetCurrent(image)!.Path;
        var output = File.ReadAllBytes(outputPath);

        // Simulate Jellyfin publishing the original during RestoreCoreAsync, then
        // a crash before the journal records RolledBack and removes the WebP.
        var staging = Path.Combine(_root, "restore-staging.image");
        DurableFiles.Write(staging, original);
        await _store.SaveAsync(_store.GetCurrent(image)!, staging, "image/png");
        File.WriteAllBytes(outputPath, output);
        record.State = "Restoring";
        var journalPath = Path.Combine(_root, "migration", "journal", record.Id.ToString("N") + ".json");
        DurableFiles.WriteJson(journalPath, record);

        var restarted = NewCoordinator();
        Assert.Equal(1, restarted.GetStatus().PendingRecovery);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.RunAsync(new Progress<double>(), default));
        await restarted.RollbackAsync(record.Id, default);

        Assert.Equal(original, File.ReadAllBytes(_store.GetCurrent(image)!.Path));
        Assert.False(File.Exists(outputPath));
        var persisted = DurableFiles.ReadJson<MigrationRecord>(journalPath);
        Assert.Equal("RolledBack", persisted.State);
        Assert.True(persisted.BackupDeleted);
        Assert.False(File.Exists(Path.Combine(_root, "migration", "originals", record.Id.ToString("N") + ".original")));
        Assert.Equal(0, restarted.GetStatus().PendingRecovery);
    }

    [Fact]
    public async Task CorruptClaimedOperationReleasesTheNativeGateForTheNextRun()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        File.WriteAllText(Path.Combine(_root, "migration", "operations", receipt.OperationId.ToString("N") + ".json"), "{broken");
        await Assert.ThrowsAnyAsync<Exception>(() => _coordinator.RunNativeAsync(new Progress<double>(), default));

        await _coordinator.RunAsync(new Progress<double>(), default, dryRunOverride: true).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Completed", _coordinator.GetStatus().LastRun.Phase);
    }

    [Fact]
    public async Task RejectQueuedOperationKeepsEvidenceWhenPlanReleaseFails()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var planPath = Path.Combine(_root, "migration", "plans", plan.PlanId.ToString("N") + ".json");
        File.WriteAllText(planPath, "{broken");
        _coordinator.RejectQueuedOperation(receipt.OperationId);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Failed", detail.Operation.State);
        Assert.Equal("DispatchFailed", detail.Operation.Phase);
        Assert.True(File.Exists(Path.Combine(_root, "migration", "operations", receipt.OperationId.ToString("N") + ".json")));
        Assert.Equal(receipt.OperationId, _coordinator.PendingOperationId);
        await _coordinator.RunAsync(new Progress<double>(), default, dryRunOverride: true).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Completed", _coordinator.GetStatus().LastRun.Phase);
    }

    [Fact]
    public async Task ConcurrentApplyKeepsTheFirstExactReservation()
    {
        _store.Add();
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        Assert.Equal(2, keys.Length);
        var first = Task.Run(() => TryApply(plan.PlanId, [keys[0]]));
        var second = Task.Run(() => TryApply(plan.PlanId, [keys[1]]));
        var results = await Task.WhenAll(first, second);
        var success = Assert.Single(results, result => result.Receipt is not null);
        var conflict = Assert.Single(results, result => result.Error is JellyPRequestException);
        Assert.Equal("PlanAlreadyApplied", ((JellyPRequestException)conflict.Error!).Code);
        var detail = _coordinator.GetOperation(success.Receipt!.OperationId, 0, 10);
        Assert.Contains(detail.Items.Single().Key, keys);
        Assert.Equal(success.Receipt.OperationId, _coordinator.GetPlan(plan.PlanId).OperationId);
    }

    [Fact]
    public void LegacyDefaultsFingerprintIncludesGlobalLimits()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        _config.BackupRetentionDays = 5;
        var error = Assert.Throws<JellyPRequestException>(() =>
            _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest { Keys = keys }));
        Assert.Equal("ManualDefaultsChanged", error.Code);
        Assert.Equal("Ready", _coordinator.GetPlan(plan.PlanId).Status);
    }

    [Fact]
    public async Task RetentionPreviewPagesAllIdentitiesAndExecutionUsesOnlyAuthorizedIds()
    {
        var expired = new List<MigrationRecord>();
        for (var index = 0; index < 51; index++)
        {
            var entry = _store.Add();
            expired.Add(SeedCommitted(entry, DateTime.UtcNow.AddDays(-30)));
        }

        var pending = _store.Add();
        _config.BackupRetentionDays = 1;
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [pending.ItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default);
        var keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        var firstPage = _coordinator.ReviewPlan(plan.PlanId, new PlanReviewRequest
        {
            Keys = keys,
            RetentionOffset = 0,
            RetentionLimit = 20,
        });
        var lastPage = _coordinator.ReviewPlan(plan.PlanId, new PlanReviewRequest
        {
            Keys = keys,
            RetentionOffset = 50,
            RetentionLimit = 20,
        });
        Assert.Equal(51, firstPage.Retention.EligibleCount);
        Assert.Equal(51, firstPage.Retention.Total);
        Assert.Equal(20, firstPage.Retention.Items.Count);
        Assert.True(firstPage.Retention.HasMore);
        Assert.Single(lastPage.Retention.Items);
        Assert.False(lastPage.Retention.HasMore);
        Assert.Equal(firstPage.Retention.Fingerprint, lastPage.Retention.Fingerprint);

        Assert.Throws<JellyPRequestException>(() => _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = keys,
            AuthorizeRetentionPurge = false,
        }));
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = keys,
            AuthorizeRetentionPurge = true,
            AuthorizeRetentionFingerprint = firstPage.Retention.Fingerprint,
        });
        var later = _store.Add();
        var laterRecord = SeedCommitted(later, DateTime.UtcNow.AddDays(-30));
        await _coordinator.RunNativeAsync(new Progress<double>(), default);
        var detail = _coordinator.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal(51, detail.Operation.RetentionPurgedCount);
        Assert.True(File.Exists(Path.Combine(_root, "migration", "originals", laterRecord.Id.ToString("N") + ".original")));
        Assert.All(expired, record => Assert.False(File.Exists(Path.Combine(_root, "migration", "originals", record.Id.ToString("N") + ".original"))));
    }

    [Fact]
    public void AcceptedPlanIsProtectedFromRetentionCleanup()
    {
        var image = _store.Add();
        var plans = new List<PlanSummary>();
        for (var index = 0; index < 21; index++)
            plans.Add(_coordinator.AnalyzePlan(new PlanAnalyzeRequest
            {
                Scope = "items",
                ItemIds = [image.ItemId],
                BatchSize = 1,
                PreserveOriginals = true,
            }, default));
        var accepted = plans.Last();
        var keys = _coordinator.GetPlanKeys(accepted.PlanId, "selectable").Keys.ToArray();
        Assert.NotEmpty(keys);
        var receipt = _coordinator.ApplyPlan(accepted.PlanId, new PlanApplyRequest { Keys = keys });
        var planPath = Path.Combine(_root, "migration", "plans", accepted.PlanId.ToString("N") + ".json");
        _coordinator.GetPlan(plans[1].PlanId);
        Assert.True(File.Exists(planPath));
        Assert.Equal("Reserved", _coordinator.GetPlan(accepted.PlanId).Status);
        Assert.Equal(receipt.OperationId, _coordinator.GetPlan(accepted.PlanId).OperationId);
    }

    [Fact]
    public void PlanRetentionIsBoundedAtCreationAndAccess()
    {
        for (var index = 0; index < 22; index++)
            _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 1, PreserveOriginals = true }, default);
        var directory = Path.Combine(_root, "migration", "plans");
        Assert.InRange(Directory.EnumerateFiles(directory, "*.json").Count(), 1, 20);
    }

    [Fact]
    public void StartupReconcilesBackupDeletionIntentWhenBytesAreGone()
    {
        var image = _store.Add();
        var record = SeedCommitted(image, DateTime.UtcNow, deletionIntent: true, writeBackup: false);
        var restarted = NewCoordinator();
        var persisted = Assert.Single(restarted.GetStatus().RecentRecords);
        Assert.True(persisted.BackupDeleted);
        Assert.False(persisted.BackupDeletionIntent);
        Assert.Empty(restarted.GetBackups("restorable", 0, 50, default).Items);
    }

    [Fact]
    public void RestartReconcilesCommittedBillingWithoutDoubleCounting()
    {
        var image = _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var seeded = SeedCommitted(image, DateTime.UtcNow);
        seeded.ExecutionId = receipt.OperationId;
        DurableFiles.WriteJson(Path.Combine(_root, "migration", "journal", seeded.Id.ToString("N") + ".json"), seeded);

        var restarted = NewCoordinator();
        var detail = restarted.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Changed", Assert.Single(detail.Items).Status);
        Assert.True(detail.Operation.Billing.ActiveBytesBefore > 0);
        Assert.True(detail.Operation.Billing.ActiveBytesAfter > 0);
        Assert.True(detail.Operation.Billing.NewOriginalBytes > 0);

        var again = NewCoordinator().GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal(detail.Operation.Billing.ActiveBytesAfter, again.Operation.Billing.ActiveBytesAfter);
    }

    [Fact]
    public async Task RestartAfterRestoreBeforeNewCommitKeepsPlanConsumedAndReconcilesBilling()
    {
        var image = _store.Add();
        _config.Quality = 85;
        await _coordinator.RunAsync(new Progress<double>(), default);
        var old = Assert.Single(_coordinator.GetStatus().RecentRecords);
        _config.Quality = 60;
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "items",
            ItemIds = [image.ItemId],
            BatchSize = 10,
            PreserveOriginals = true,
        }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var operationItem = Assert.Single(_coordinator.GetOperation(receipt.OperationId, 0, 10).Items);
        Assert.Equal(old.Id, operationItem.RecordId);

        var original = File.ReadAllBytes(Path.Combine(_root, "migration", "originals", old.Id.ToString("N") + ".original"));
        File.WriteAllBytes(_store.GetCurrent(image)!.Path, original);
        var oldPath = Path.Combine(_root, "migration", "journal", old.Id.ToString("N") + ".json");
        var oldRecord = DurableFiles.ReadJson<MigrationRecord>(oldPath);
        oldRecord.State = "RolledBack";
        oldRecord.BackupDeleted = true;
        DurableFiles.WriteJson(oldPath, oldRecord);
        var operationPath = Path.Combine(_root, "migration", "operations", receipt.OperationId.ToString("N") + ".json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(operationPath))!;
        node["MutationStarted"] = true;
        File.WriteAllText(operationPath, node.ToJsonString());

        var restarted = NewCoordinator();
        Assert.Equal("Consumed", restarted.GetPlan(plan.PlanId).Status);
        var detail = restarted.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Failed", Assert.Single(detail.Items).Status);
        Assert.Equal("InterruptedAfterRestore", Assert.Single(detail.Items).Reason);
        Assert.True(detail.Operation.Billing.ActiveBytesAfter > 0);
    }

    [Fact]
    public void MutationStartedMarkerKeepsPlanConsumedAcrossRestart()
    {
        _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var operationPath = Path.Combine(_root, "migration", "operations", receipt.OperationId.ToString("N") + ".json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(operationPath))!;
        node["MutationStarted"] = true;
        File.WriteAllText(operationPath, node.ToJsonString());

        var restarted = NewCoordinator();
        Assert.Equal("Consumed", restarted.GetPlan(plan.PlanId).Status);
        Assert.Equal("Interrupted", restarted.GetOperation(receipt.OperationId, 0, 10).Operation.State);
    }

    [Fact]
    public async Task RestartDoesNotClaimChangedCommittedEvidence()
    {
        var image = _store.Add();
        var plan = _coordinator.AnalyzePlan(new PlanAnalyzeRequest { Scope = "all", BatchSize = 10, PreserveOriginals = true }, default);
        var receipt = _coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = _coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray(),
        });
        var seeded = SeedCommitted(image, DateTime.UtcNow);
        seeded.ExecutionId = receipt.OperationId;
        DurableFiles.WriteJson(Path.Combine(_root, "migration", "journal", seeded.Id.ToString("N") + ".json"), seeded);
        File.WriteAllBytes(image.Path, [9, 9, 9]);
        var restarted = NewCoordinator();
        var detail = restarted.GetOperation(receipt.OperationId, 0, 10);
        Assert.Equal("Interrupted", detail.Operation.State);
        var item = Assert.Single(detail.Items);
        Assert.Equal("Failed", item.Status);
        Assert.Equal("CommittedArtworkChanged", item.Reason);
        Assert.NotEqual("Changed", item.Status);
        await Task.CompletedTask;
    }

    [Fact]
    public void CorruptJournalLoadsIntoRecoveryAndBlocksNewMutations()
    {
        _store.Add();
        Directory.CreateDirectory(Path.Combine(_root, "migration", "journal"));
        File.WriteAllText(Path.Combine(_root, "migration", "journal", "corrupt.json"), "{broken");
        var coordinator = NewCoordinator();
        var status = coordinator.GetStatus();
        Assert.Equal(1, status.PendingRecovery);
        Assert.Contains("corrupt", status.LastRun.Message);
        Assert.Throws<InvalidOperationException>(() => coordinator.QueueExecution(false, true));
    }

    [Fact]
    public void BackupErrorEvidenceIsSanitized()
    {
        var image = _store.Add();
        SeedCommitted(image, DateTime.UtcNow, error: "/private/secret/poster.png");
        var row = Assert.Single(_coordinator.GetBackups("all", 0, 50, default).Items);
        Assert.DoesNotContain("/private", row.Error);
        Assert.Equal("Recovery required; review server logs.", row.Error);
    }

    [Fact]
    public async Task EnabledFalseStillRunsAnalyzeModeButBlocksApplying()
    {
        _store.Add();
        _config.Enabled = false;
        _config.DryRun = true;
        await _coordinator.RunAsync(new Progress<double>(), default);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Equal(1, _coordinator.GetStatus().LastRun.WouldConvert);
        Assert.Equal("Completed", _coordinator.GetStatus().LastRun.Phase);

        _config.DryRun = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _coordinator.RunAsync(new Progress<double>(), default));
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public void OldXmlWithoutManualDefaultsRemainsReadable()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using (var reader = new StringReader("""
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration>
              <Enabled>true</Enabled>
              <DryRun>false</DryRun>
              <ScopeMode>all</ScopeMode>
              <BatchSize>250</BatchSize>
            </PluginConfiguration>
            """))
        {
            var legacy = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));
            Assert.Null(legacy.ManualDefaults);
            legacy.Validate();
        }

        _config.ManualDefaults = new ManualDefaults
        {
            Quality = 71,
            BatchSize = 75,
            Policies = new ManualDefaults().Policies,
        };
        using var writer = new StringWriter();
        serializer.Serialize(writer, _config);
        using var roundTripReader = new StringReader(writer.ToString());
        var restored = Assert.IsType<PluginConfiguration>(serializer.Deserialize(roundTripReader));
        Assert.Equal(71, restored.ManualDefaults!.Quality);
        Assert.Equal(75, restored.ManualDefaults.BatchSize);
    }

    [Fact]
    public void OldJournalRecordsRemainReadableWithNullableNewEvidence()
    {
        var path = Path.Combine(_root, "journal", "legacy.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {"Id":"11111111-1111-1111-1111-111111111111","Source":{"ItemId":"22222222-2222-2222-2222-222222222222","Type":0,"Index":0,"Path":"/legacy/poster.png"},"SourceHash":"a","OutputHash":"b","OriginalMimeType":"image/png","OriginalBytes":1,"OutputBytes":1,"State":"Committed","BackupDeleted":true}
            """);
        var record = DurableFiles.ReadJson<MigrationRecord>(path);
        Assert.Null(record.ExecutionId);
        Assert.Null(record.OriginalWidth);
        Assert.Null(record.OutputWidth);
        Assert.Null(record.PolicySnapshot);
    }

    private OptimizationCoordinator NewCoordinator() =>
        new(_store, () => _config, Path.Combine(_root, "migration"), NullLogger<OptimizationCoordinator>.Instance);

    private static void SetCancellationHook(Action? hook) =>
        ((AsyncLocal<Action?>)typeof(OptimizationCoordinator)
            .GetField("_cancellationRequestReadHook", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!).Value = hook;

    private static void SetOperationSaveHook(Action<bool, bool, string>? hook) =>
        ((AsyncLocal<Action<bool, bool, string>?>)typeof(OptimizationCoordinator)
            .GetField("_operationSaveTestHook", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!).Value = hook;

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Expected coordinator state was not reached.");
            await Task.Delay(25);
        }
    }

    private (OperationReceipt? Receipt, Exception? Error) TryApply(Guid planId, string[] keys)
    {
        try { return (_coordinator.ApplyPlan(planId, new PlanApplyRequest { Keys = keys }), null); }
        catch (Exception ex) { return (null, ex); }
    }

    private MigrationRecord SeedCommitted(ArtworkReference entry, DateTime completed, string? error = null,
        bool deletionIntent = false, bool writeBackup = true)
    {
        var bytes = File.ReadAllBytes(entry.Path);
        var sourcePath = Path.Combine(_root, "absent-source", entry.ItemId.ToString("N") + ".png");
        var record = new MigrationRecord
        {
            Source = entry with { Path = sourcePath },
            SourceHash = DurableFiles.Hash(bytes),
            OutputHash = DurableFiles.Hash(bytes),
            OriginalMimeType = "image/png",
            OriginalBytes = bytes.Length,
            OutputBytes = bytes.Length,
            OutputPath = entry.Path,
            CreatedUtc = completed.AddMinutes(-1),
            CompletedUtc = completed,
            State = "Committed",
            Error = error,
            BackupDeletionIntent = deletionIntent,
        };
        if (writeBackup)
            DurableFiles.Write(Path.Combine(_root, "migration", "originals", record.Id.ToString("N") + ".original"), bytes);
        DurableFiles.WriteJson(Path.Combine(_root, "migration", "journal", record.Id.ToString("N") + ".json"), record);
        return record;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class PlanningArtworkStore(string root) : IArtworkStore
    {
        private readonly List<Entry> _entries = [];
        public string Root => root;
        public int SaveCalls { get; private set; }
        public int CountReferenceCalls { get; private set; }
        public void ResetCountReferenceCalls() => CountReferenceCalls = 0;
        private volatile bool _busy;
        public bool Busy { get => _busy; set => _busy = value; }
        public bool ThrowInventoryPath { get; set; }
        public bool ThrowSavePath { get; set; }
        public bool FailNextSaveAfterChange { get; set; }
        public Action? OnCountPathReferences { get; set; }
        public Action<Guid?>? OnEnsureIdle { get; set; }

        public ArtworkReference Add(bool isPerson = false, Guid? itemId = null, string? explicitPath = null, string? state = null,
            ImageType type = ImageType.Primary, int index = 0)
        {
            var id = itemId ?? Guid.NewGuid();
            var fileName = state is "Missing" ? $"missing-{type.ToString().ToLowerInvariant()}-{index}.png" : $"{type.ToString().ToLowerInvariant()}-{index}.png";
            var path = explicitPath ?? Path.Combine(root, id.ToString("N"), fileName);
            if (state is not "Missing" and not "Remote")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) File.WriteAllBytes(path, Png());
            }

            var reference = new ArtworkReference(id, type, index, path, isPerson);
            _entries.Add(new Entry
            {
                Reference = reference,
                State = state,
                Name = "Synthetic " + id.ToString("N")[..4],
                ItemType = isPerson ? "Person" : "Movie",
                Year = 2026,
            });
            return reference;
        }

        public ArtworkReference AddWebp()
        {
            var id = Guid.NewGuid();
            var path = Path.Combine(root, id.ToString("N"), "poster.webp");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Webp());
            var reference = new ArtworkReference(id, ImageType.Primary, 0, path);
            _entries.Add(new Entry { Reference = reference, State = "AlreadyWebp", Name = "External", ItemType = "Movie", Year = 2026 });
            return reference;
        }

        public ArtworkReference AddRemote() => Add(state: "Remote", explicitPath: "https://example.invalid/remote.jpg");
        public ArtworkReference AddMissing() => Add(state: "Missing");
        public ArtworkReference AddExcluded() => Add(state: "Excluded");

        public IReadOnlyList<ArtworkInventoryEntry> DetailedInventory(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            Action<int>? inventoryProgress = null, int? maximumEntries = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowInventoryPath) throw new InvalidOperationException("/private/secret/inventory-path");
            var counts = _entries.Where(entry => IsLocal(entry.Reference.Path))
                .GroupBy(entry => Path.GetFullPath(entry.Reference.Path), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var selected = (includedItemIds ?? []).ToHashSet();
            var rows = _entries.Where(entry => includedItemIds is null || includedItemIds.Count == 0 || selected.Contains(entry.Reference.ItemId))
                .Select(entry =>
                {
                    var state = entry.State ?? (Path.GetExtension(entry.Reference.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase) ? "AlreadyWebp" : "Ready");
                    var count = counts.GetValueOrDefault(Path.GetFullPath(entry.Reference.Path));
                    if (state is "Ready" or "AlreadyWebp" && count > 1)
                        state = "Shared";
                    return new ArtworkInventoryEntry
                    {
                        Reference = entry.Reference,
                        Name = entry.Name,
                        ItemType = entry.ItemType,
                        ProductionYear = entry.Year,
                        Category = entry.Reference.IsPerson ? "People" : entry.Reference.Type.ToString(),
                        State = state,
                        Reason = state switch
                        {
                            "Remote" => "RemoteReference",
                            "Missing" => "MissingFile",
                            "Excluded" => "UnsupportedFormat",
                            "Shared" => "SharedReference",
                            _ => null,
                        },
                        CurrentBytes = File.Exists(entry.Reference.Path) ? new FileInfo(entry.Reference.Path).Length : null,
                        IsShared = count > 1,
                        ReferenceCount = count,
                    };
                }).ToArray();
            inventoryProgress?.Invoke(rows.Length);
            return rows;
        }

        public IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            Action<int>? inventoryProgress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = (includedItemIds ?? []).ToHashSet();
            var result = _entries.Where(entry => entry.State is null or "Ready" or "AlreadyWebp")
                .Where(entry => includedItemIds is null || includedItemIds.Count == 0 || selected.Contains(entry.Reference.ItemId))
                .Select(entry => entry.Reference).ToArray();
            inventoryProgress?.Invoke(result.Length);
            return result;
        }

        public void EnsureIdle(Guid? itemId = null)
        {
            OnEnsureIdle?.Invoke(itemId);
            if (Busy) throw new InvalidOperationException("Busy metadata refresh.");
        }

        public ArtworkReference? GetCurrent(ArtworkReference reference)
        {
            var entry = _entries.FirstOrDefault(candidate => candidate.Reference.Key == reference.Key);
            return entry is null || entry.State is "Remote" or "Missing" ? null : entry.Reference;
        }

        public string GetDestination(ArtworkReference reference, string mimeType) => Path.ChangeExtension(reference.Path, mimeType switch
        {
            "image/webp" => ".webp",
            "image/png" => ".png",
            _ => ".jpg",
        });

        public bool IsPathReferenced(string path) => CountPathReferences(path) > 0;

        public int CountPathReferences(string path)
        {
            CountReferenceCalls++;
            var absolute = Path.GetFullPath(path);
            return _entries.Count(entry => string.Equals(Path.GetFullPath(entry.Reference.Path), absolute, StringComparison.Ordinal));
        }

        public IReadOnlyDictionary<string, int> CountPathReferences(IReadOnlyCollection<string> paths)
        {
            OnCountPathReferences?.Invoke();
            CountReferenceCalls++;
            var absolute = paths.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
            var counts = absolute.ToDictionary(path => path, _ => 0, StringComparer.Ordinal);
            foreach (var entry in _entries)
            {
                var path = Path.GetFullPath(entry.Reference.Path);
                if (counts.ContainsKey(path)) counts[path]++;
            }

            return counts;
        }

        public bool IsSlotSafeForMutation(ArtworkReference reference) =>
            _entries.Where(entry => entry.Reference.ItemId == reference.ItemId)
                .All(entry => entry.Reference.Key == reference.Key || entry.State is null or "Ready" or "AlreadyWebp");

        public Task<ArtworkReference> SaveAsync(ArtworkReference reference, string stagingPath, string mimeType)
        {
            SaveCalls++;
            if (ThrowSavePath) throw new InvalidOperationException("/private/secret/save-path");
            var entry = _entries.Single(candidate => candidate.Reference.Key == reference.Key);
            var destination = GetDestination(reference, mimeType);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, File.ReadAllBytes(stagingPath));
            var previous = entry.Reference.Path;
            if (previous != destination) File.Delete(previous);
            entry.Reference = entry.Reference with { Path = destination };
            if (FailNextSaveAfterChange)
            {
                FailNextSaveAfterChange = false;
                throw new IOException("Injected persistence failure");
            }

            return Task.FromResult(entry.Reference);
        }

        private static bool IsLocal(string path) => Path.IsPathRooted(path) && !path.StartsWith("http", StringComparison.OrdinalIgnoreCase);

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

        private static byte[] Webp()
        {
            using var bitmap = new SKBitmap(256, 256);
            for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)x, (byte)y, 128, 255));
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
            return encoded.ToArray();
        }

        private sealed class Entry
        {
            public ArtworkReference Reference { get; set; } = null!;
            public string? State { get; set; }
            public string? Name { get; set; }
            public string? ItemType { get; set; }
            public int? Year { get; set; }
        }
    }
}
