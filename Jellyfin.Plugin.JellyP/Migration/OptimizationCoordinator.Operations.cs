using System.Text.Json;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Conversion;
using Jellyfin.Plugin.JellyP.Infrastructure;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyP.Migration;

public sealed partial class OptimizationCoordinator
{
    /// <summary>Runs one queued persisted operation, or the legacy native batch when none is queued.</summary>
    public async Task RunNativeAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        bool hadPendingOperation;
        lock (_requestGate) hadPendingOperation = _pendingOperationId.HasValue;
        var operation = TryClaimQueuedOperation(cancellationToken);
        if (operation is not null)
        {
            await ExecuteOperationAsync(operation, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The native invocation belonged to a reservation that was cancelled or rejected.
        // It must not silently fall back to the saved automation batch, but a distinct
        // legacy request queued after the cancellation still needs this invocation.
        if (hadPendingOperation)
        {
            lock (_requestGate)
                if (_pending is null) return;
        }

        await RunAsync(progress, cancellationToken, waitForOtherOperations: true).ConfigureAwait(false);
    }

    private bool HasPendingOrRunningOperation()
    {
        if (_currentOperationId.HasValue) return true;
        if (!_pendingOperationId.HasValue) return false;
        var path = OperationPath(_pendingOperationId.Value);
        return File.Exists(path) && DurableFiles.ReadJson<OperationRecord>(path).State == "Queued";
    }

    /// <summary>Returns the operation currently claimed by the native runner, never a merely queued reservation.</summary>
    public Guid? ActiveOperationId
    {
        get
        {
            lock (_requestGate) return _currentOperationId;
        }
    }

    /// <summary>Explicit alias used by dispatch/retry code to distinguish a runner from a queued marker.</summary>
    public Guid? CurrentRunnerOperationId => ActiveOperationId;

    /// <summary>Returns the durable pending dispatch marker, if any, for diagnostics.</summary>
    public Guid? PendingOperationId
    {
        get
        {
            lock (_requestGate) return _pendingOperationId;
        }
    }

    /// <summary>Returns a stable page of persisted operations, newest first.</summary>
    public OperationPage GetOperations(int offset, int limit)
    {
        ValidatePage(offset, limit, 100);
        var operations = Operations().OrderByDescending(operation => operation.CreatedUtc).ThenBy(operation => operation.Id).ToArray();
        return new OperationPage
        {
            Offset = offset,
            Limit = limit,
            Total = operations.Length,
            Items = operations.Skip(offset).Take(limit).Select(ToView).ToArray(),
        };
    }

    /// <summary>Returns one operation and a page of its individual results.</summary>
    public OperationDetail GetOperation(Guid operationId, int offset, int limit)
    {
        ValidatePage(offset, limit, 200);
        var operation = ReadOperation(operationId);
        return new OperationDetail
        {
            Operation = ToView(operation),
            Offset = offset,
            Limit = limit,
            TotalItems = operation.Items.Count,
            Items = operation.Items.Skip(offset).Take(limit).Select(ToItemView).ToArray(),
        };
    }

    /// <summary>Lists active committed backups that are preliminarily safe to restore or retire.</summary>
    public BackupPage GetBackups(string? filter, int offset, int limit, CancellationToken cancellationToken)
    {
        ValidatePage(offset, limit, 200);
        var normalized = NormalizeFilter(filter);
        var snapshot = LoadJournalSnapshot();
        var records = ActiveBackupRecords(snapshot);
        var shared = BackupSlotStates(records, cancellationToken);
        var rows = records.Select(record => ToBackupView(record, shared)).ToArray();
        var filtered = rows.Where(row => normalized == "all" || normalized == "restorable" && row.Restorable || normalized == "retirable" && row.Retirable)
            .OrderByDescending(row => row.CompletedUtc ?? row.CreatedUtc).ThenBy(row => row.RecordId).ToArray();
        return new BackupPage
        {
            Filter = normalized,
            Offset = offset,
            Limit = limit,
            Total = filtered.Length,
            Items = filtered.Skip(offset).Take(limit).ToArray(),
        };
    }

    /// <summary>Returns every active backup key for the explicit filter, or fails if the hard cap is exceeded.</summary>
    public BackupKeysResponse GetBackupKeys(string? filter, CancellationToken cancellationToken)
    {
        var normalized = NormalizeFilter(filter);
        var snapshot = LoadJournalSnapshot();
        var records = ActiveBackupRecords(snapshot);
        var shared = BackupSlotStates(records, cancellationToken);
        var rows = records.Select(record => ToBackupView(record, shared))
            .Where(row => normalized == "all" || normalized == "restorable" && row.Restorable || normalized == "retirable" && row.Retirable)
            .OrderByDescending(row => row.CompletedUtc ?? row.CreatedUtc).ThenBy(row => row.RecordId).ToArray();
        if (rows.Length > 10_000)
            throw new JellyPRequestException(409, "SelectionTooLarge", "More than 10000 backup keys match. Narrow the filter before selecting all.");
        return new BackupKeysResponse { Filter = normalized, Total = rows.Length, Keys = rows.Select(row => row.RecordId).ToArray() };
    }

    /// <summary>Queues an exact batch restoration after server-side preliminary eligibility checks.</summary>
    public OperationReceipt QueueRestore(BackupBatchRequest request, CancellationToken cancellationToken)
        => QueueBackupOperation(request?.RecordIds, "Restore", false, cancellationToken);

    /// <summary>Queues an exact batch retirement after explicit permanent-removal authorization.</summary>
    public OperationReceipt QueueRetire(BackupRetireRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.AuthorizePermanentRemoval)
            throw new JellyPRequestException(409, "PermanentRemovalAuthorizationRequired",
                "Retiring stored originals permanently requires explicit authorization.");
        return QueueBackupOperation(request.RecordIds, "Retire", true, cancellationToken);
    }

    private OperationRecord? TryClaimQueuedOperation(CancellationToken cancellationToken)
    {
        lock (_requestGate)
        {
            if (_running || _pending is not null) return null;
            var operationId = _pendingOperationId;
            if (!operationId.HasValue)
            {
                operationId = Operations().Where(operation => operation.State == "Queued")
                    .OrderBy(operation => operation.CreatedUtc).ThenBy(operation => operation.Id)
                    .Select(operation => (Guid?)operation.Id).FirstOrDefault();
            }

            if (!operationId.HasValue) return null;
            if (!_gate.Wait(0, cancellationToken))
                throw new InvalidOperationException("A JellyP operation is already running or queued.");
            var claimed = false;
            try
            {
                if (!File.Exists(OperationPath(operationId.Value)))
                {
                    if (_pendingOperationId == operationId) _pendingOperationId = null;
                    return null;
                }

                var operation = ReadOperation(operationId.Value);
                if (operation.State != "Queued")
                {
                    if (_pendingOperationId == operation.Id) _pendingOperationId = null;
                    return null;
                }

                _pendingOperationId = null;
                _currentOperationId = operation.Id;
                _running = true;
                claimed = true;
                return operation;
            }
            catch
            {
                if (_pendingOperationId == operationId) _pendingOperationId = null;
                throw;
            }
            finally
            {
                if (!claimed) _gate.Release();
            }
        }
    }

    private async Task ExecuteOperationAsync(OperationRecord operation, IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_requestGate) _operationCancellation = linked;
        var token = linked.Token;
        try
        {
            // A cancel can land between reservation and the native task claiming it.
            var persisted = ReadOperation(operation.Id);
            if (persisted.CancelRequested) operation.CancelRequested = true;
            if (operation.CancelRequested || token.IsCancellationRequested)
            {
                operation.State = "Cancelled";
                operation.Phase = "Cancelled";
                operation.Message = "Cancelled before the operation started.";
                operation.CompletedUtc = DateTime.UtcNow;
                SaveOperation(operation);
                return;
            }

            operation.State = "Running";
            operation.StartedUtc = DateTime.UtcNow;
            operation.Phase = "Preparing";
            operation.Message = null;
            SaveOperation(operation);

            RecoverBackupDeletionIntents();
            if (Records().Any(record => record.State is "Prepared" or "Restoring" or "RecoveryRequired"))
                throw new InvalidOperationException("Recover interrupted image transactions through the explicit recovery endpoint before starting a new batch.");
            await WaitForIdleOperationAsync(operation, null, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (operation.CancelRequested) throw new OperationCanceledException();
            switch (operation.Kind)
            {
                case "Apply":
                    await ExecuteApplyAsync(operation, progress, token).ConfigureAwait(false);
                    break;
                case "Restore":
                    await ExecuteRestoreBatchAsync(operation, progress, token).ConfigureAwait(false);
                    break;
                case "Retire":
                    await ExecuteRetireBatchAsync(operation, progress, token).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("Unknown persisted JellyP operation kind.");
            }

            operation.State = operation.Failed > 0 || operation.Invalidated > 0 || operation.NotStarted > 0
                ? "CompletedWithErrors" : "Completed";
            operation.Phase = "Completed";
            operation.Message = operation.Failed > 0
                ? "The operation stopped safely. Review individual results and recovery state."
                : "Operation completed.";
        }
        catch (OperationCanceledException)
        {
            operation.State = "Cancelled";
            operation.Phase = "Cancelled";
            operation.Message = "Cancelled between image transactions; the current transaction reached a safe boundary.";
            MarkRemainingNotStarted(operation, "CancelledBeforeStart");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyP operation {OperationId} ({Kind}) failed", operation.Id, operation.Kind);
            operation.State = "Failed";
            operation.Phase = "Failed";
            operation.Message = SafeFailureMessage(ex);
            MarkRemainingNotStarted(operation, "StoppedAfterFailure");
        }
        finally
        {
            operation.CompletedUtc = DateTime.UtcNow;
            operation.Processed = operation.Items.Count(item => item.Status is not "Pending" and not "NotStarted");
            operation.Changed = operation.Items.Count(item => item.Status is "Changed" or "Restored" or "Retired");
            operation.Failed = operation.Items.Count(item => item.Status == "Failed");
            operation.Invalidated = operation.Items.Count(item => item.Status == "Invalidated");
            operation.NotStarted = operation.Items.Count(item => item.Status == "NotStarted");
            try
            {
                SaveOperation(operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyP could not persist final state for operation {OperationId}", operation.Id);
            }
            finally
            {
                try
                {
                    if (operation.Kind == "Apply" && operation.PlanId.HasValue)
                        ReleasePlanReservationIfUnmutated(operation);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "JellyP could not release plan reservation for operation {OperationId}", operation.Id);
                }
                finally
                {
                    lock (_requestGate)
                    {
                        _operationCancellation = null;
                        _currentOperationId = null;
                        _running = false;
                    }

                    _gate.Release();
                    progress.Report(100);
                }
            }
        }
    }

    private async Task ExecuteApplyAsync(OperationRecord operation, IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!operation.PlanId.HasValue || !File.Exists(PlanPath(operation.PlanId.Value)))
            throw new InvalidOperationException("The persisted plan is unavailable; no artwork was changed.");
        var plan = ReadPlan(operation.PlanId.Value);
        if (plan.OperationId != operation.Id)
            throw new InvalidOperationException("The plan reservation was released or belongs to another operation; no artwork was changed.");
        EnsurePlanDefaultsUnchanged(plan);
        var config = RuntimeConfiguration(plan);
        var planImages = plan.Images.ToDictionary(image => image.Key, StringComparer.Ordinal);
        operation.Phase = "Validating";
        SaveOperation(operation);
        var selectedItems = operation.Items.Select(item => item.ItemId).Distinct().ToArray();
        var fresh = _artwork.DetailedInventory(cancellationToken, selectedItems, null)
            .ToDictionary(entry => entry.Reference.Key, StringComparer.Ordinal);
        var journal = LoadJournalSnapshot();
        var managedKeys = journal.LatestOpenCommitted.Keys.ToHashSet(StringComparer.Ordinal);
        var prepared = new List<PreparedApply>();
        foreach (var item in operation.Items.Where(item => item.Status == "Pending"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!planImages.TryGetValue(item.Key, out var planImage))
            {
                Reject(item, "PlanEvidenceMissing");
                continue;
            }

            if (!fresh.TryGetValue(item.Key, out var entry) || !IsMutationSafe(entry))
            {
                Reject(item, entry?.IsShared == true ? "SharedReference" : entry?.Reason ?? "SourceChanged");
                continue;
            }

            var reference = new ArtworkReference(planImage.ItemId, planImage.Type, planImage.Index, planImage.Path, planImage.IsPerson);
            var current = _artwork.GetCurrent(reference);
            if (current is null || current.Path != planImage.Path || !File.Exists(current.Path))
            {
                Reject(item, "SourceChanged");
                continue;
            }

            byte[] currentBytes;
            try
            {
                currentBytes = DurableFiles.ReadImage(current.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Reject(item, "SourceUnavailable");
                continue;
            }

            if (item.CurrentHash is null || DurableFiles.Hash(currentBytes) != item.CurrentHash)
            {
                Reject(item, "SourceChanged");
                continue;
            }

            byte[] source;
            WebpOptimizationResult optimized;
            MigrationRecord? managed = null;
            if (planImage.Action == "Regenerate")
            {
                if (!planImage.ManagedRecordId.HasValue || !File.Exists(Path.Combine(_root, "journal", planImage.ManagedRecordId.Value.ToString("N") + ".json")))
                {
                    Reject(item, "OriginalUnavailable");
                    continue;
                }

                managed = ReadRecord(planImage.ManagedRecordId.Value);
                if (!IsActiveRecord(managed, verifyHash: true, journal) || managed.OutputHash != item.CurrentHash)
                {
                    Reject(item, "OriginalUnavailable");
                    continue;
                }

                try
                {
                    source = DurableFiles.ReadImage(Backup(managed));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Reject(item, "OriginalUnavailable");
                    continue;
                }

                if (DurableFiles.Hash(source) != managed.SourceHash)
                {
                    Reject(item, "OriginalCorrupt");
                    continue;
                }
            }
            else
            {
                if (managedKeys.Contains(item.Key))
                {
                    Reject(item, "SlotManagedSinceAnalysis");
                    continue;
                }

                source = currentBytes;
            }

            var policy = config.PolicyFor(reference);
            if (policy is not { Enabled: true } || planImage.PolicyFingerprint != config.Fingerprint(policy))
            {
                Reject(item, "PolicyChanged");
                continue;
            }

            try
            {
                optimized = new WebpImageConverter().Optimize(source, config.OptionsFor(policy));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or InvalidDataException or NotSupportedException)
            {
                Reject(item, "ConversionRejected");
                continue;
            }

            if (!optimized.Success || optimized.OutputBytes is null
                || item.OutputHash is null || DurableFiles.Hash(optimized.OutputBytes) != item.OutputHash)
            {
                Reject(item, optimized.SkipReason == WebpSkipReason.None ? "ConversionChanged" : optimized.SkipReason.ToString());
                continue;
            }

            prepared.Add(new PreparedApply(item, planImage, current, source, optimized, managed, policy));
        }

        if (operation.Items.All(item => item.Status != "Pending"))
        {
            operation.Phase = "Validated";
            return;
        }

        var protectedIds = prepared.Where(pair => pair.Record is not null).Select(pair => pair.Record!.Id).ToHashSet();
        var applyReferenceCounts = _artwork.CountPathReferences(prepared
            .Where(pair => pair.Record?.OutputPath is not null)
            .Select(pair => pair.Record!.OutputPath!).ToArray());
        // Authorization, exact expiry IDs and global-safety fingerprints are checked
        // again before any mutation. A mismatch stops without consuming the plan.
        var authorizedRetention = ValidateRetentionForApply(operation, config, protectedIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.CancelRequested) throw new OperationCanceledException();
        await PurgeRetentionForApplyAsync(operation, authorizedRetention, cancellationToken).ConfigureAwait(false);
        operation.Phase = "Processing";
        SaveOperation(operation);
        var total = Math.Max(1, prepared.Count);
        var index = 0;
        foreach (var pair in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.CancelRequested) break;
            await WaitForIdleOperationAsync(operation, pair.Item.ItemId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.CancelRequested) throw new OperationCanceledException();
            MarkMutationStartedAndConsumePlan(operation);
            try
            {
                MigrationRecord committed;
                if (pair.Record is not null)
                {
                    await RestoreCoreAsync(pair.Record, config, slotSafe: true,
                        referenceCounts: applyReferenceCounts).ConfigureAwait(false);
                    var target = _artwork.GetCurrent(pair.Current) ?? throw new IOException("Restored image slot is missing.");
                    if (DurableFiles.Hash(DurableFiles.ReadImage(target.Path)) != pair.Record.SourceHash)
                        throw new IOException("Restored original checksum failed.");
                    committed = await ConvertAsync(target, pair.Source, pair.Optimized.OutputBytes!, config,
                        config.Fingerprint(pair.Policy), operation.Id, pair.Optimized, PolicySnapshot(config, pair.Policy)).ConfigureAwait(false);
                }
                else
                {
                    if (!fresh.TryGetValue(pair.Item.Key, out _))
                        throw new IOException("The image slot disappeared before the write.");
                    committed = await ConvertAsync(pair.Current, pair.Source, pair.Optimized.OutputBytes!, config,
                        config.Fingerprint(pair.Policy), operation.Id, pair.Optimized, PolicySnapshot(config, pair.Policy)).ConfigureAwait(false);
                }

                pair.Item.Status = "Changed";
                pair.Item.RecordId = committed.Id;
                pair.Item.Reason = null;
                pair.Item.OriginalMimeType = committed.OriginalMimeType;
                pair.Item.OriginalBytes = committed.OriginalBytes;
                pair.Item.OutputBytes = committed.OutputBytes;
                pair.Item.OriginalWidth = committed.OriginalWidth;
                pair.Item.OriginalHeight = committed.OriginalHeight;
                pair.Item.OutputWidth = committed.OutputWidth;
                pair.Item.OutputHeight = committed.OutputHeight;
                pair.Item.SourceHash = committed.SourceHash;
                pair.Item.OutputHash = committed.OutputHash;
                pair.Item.CleanupWarning = committed.CleanupWarning;
                ApplyActualBilling(operation, pair);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                pair.Item.Status = "Failed";
                pair.Item.Reason = SafeFailureMessage(ex);
                _logger.LogError(ex, "JellyP apply stopped at {Key} in operation {OperationId}", pair.Item.Key, operation.Id);
                MarkRemainingNotStarted(operation, "StoppedAfterUncertainTransaction");
                SaveOperation(operation);
                break;
            }

            SaveOperation(operation);
            progress.Report(100d * ++index / total);
        }

        if (operation.CancelRequested)
            MarkRemainingNotStarted(operation, "CancelledBeforeStart");
    }

    private async Task ExecuteRestoreBatchAsync(OperationRecord operation, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = SnapshotConfiguration();
        var selected = operation.Items.Select(item => item.ItemId).Distinct().ToArray();
        var fresh = _artwork.DetailedInventory(cancellationToken, selected, null)
            .ToDictionary(entry => entry.Reference.Key, StringComparer.Ordinal);
        var journal = LoadJournalSnapshot();
        var batchRecords = operation.Items.Where(item => item.RecordId.HasValue)
            .Select(item => TryReadRecord(item.RecordId!.Value))
            .Where(record => record is not null).Select(record => record!).ToArray();
        var referenceCounts = _artwork.CountPathReferences(batchRecords
            .Where(record => record.OutputPath is not null).Select(record => record.OutputPath!).ToArray());
        var index = 0;
        foreach (var item in operation.Items.Where(item => item.Status == "Pending"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.CancelRequested) break;
            if (!item.RecordId.HasValue || !File.Exists(Path.Combine(_root, "journal", item.RecordId.Value.ToString("N") + ".json")))
            {
                Reject(item, "BackupNotFound");
                continue;
            }

            var record = batchRecords.FirstOrDefault(candidate => candidate.Id == item.RecordId.Value);
            if (record is null)
            {
                Reject(item, "BackupNotFound");
                continue;
            }

            if (!fresh.TryGetValue(item.Key, out var entry))
            {
                Reject(item, "ActiveArtworkChanged");
                continue;
            }

            if (!IsMutationSafe(entry))
            {
                Reject(item, entry.IsShared ? "SharedReference" : entry.Reason ?? "OtherSlotUnavailable");
                continue;
            }

            if (!IsActiveRecord(record, verifyHash: true, journal))
            {
                Reject(item, "BackupChanged");
                continue;
            }

            await WaitForIdleOperationAsync(operation, record.Source.ItemId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.CancelRequested) throw new OperationCanceledException();
            MarkMutationStartedAndConsumePlan(operation);
            try
            {
                await RestoreCoreAsync(record, config, slotSafe: true,
                    referenceCounts: referenceCounts).ConfigureAwait(false);
                item.Status = "Restored";
                item.Reason = null;
                item.OriginalMimeType = record.OriginalMimeType;
                item.OriginalBytes = record.OriginalBytes;
                item.OutputBytes = record.OutputBytes;
                item.OriginalWidth = record.OriginalWidth;
                item.OriginalHeight = record.OriginalHeight;
                item.OutputWidth = record.OutputWidth;
                item.OutputHeight = record.OutputHeight;
                operation.ActualBilling.ActiveBytesBefore += record.OutputBytes;
                operation.ActualBilling.ActiveBytesAfter += record.OriginalBytes;
                operation.ActualBilling.BackupBytesBefore += record.OriginalBytes;
                operation.ActualBilling.RemovedBackupBytes += record.OriginalBytes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                item.Status = "Failed";
                item.Reason = SafeFailureMessage(ex);
                _logger.LogError(ex, "JellyP batch restore failed at {Key} in operation {OperationId}", item.Key, operation.Id);
                MarkRemainingNotStarted(operation, "StoppedAfterUncertainTransaction");
                SaveOperation(operation);
                break;
            }

            SaveOperation(operation);
            progress.Report(100d * ++index / Math.Max(1, operation.Items.Count));
        }

        if (operation.CancelRequested) MarkRemainingNotStarted(operation, "CancelledBeforeStart");
    }

    private async Task ExecuteRetireBatchAsync(OperationRecord operation, IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!operation.AuthorizePermanentRemoval)
            throw new InvalidOperationException("Permanent-removal authorization is missing.");
        var selected = operation.Items.Select(item => item.ItemId).Distinct().ToArray();
        var fresh = _artwork.DetailedInventory(cancellationToken, selected, null)
            .ToDictionary(entry => entry.Reference.Key, StringComparer.Ordinal);
        var journal = LoadJournalSnapshot();
        var batchRecords = operation.Items.Where(item => item.RecordId.HasValue)
            .Select(item => TryReadRecord(item.RecordId!.Value))
            .Where(record => record is not null).Select(record => record!).ToArray();
        var referenceCounts = _artwork.CountPathReferences(batchRecords
            .Where(record => record.OutputPath is not null).Select(record => record.OutputPath!).ToArray());
        var index = 0;
        foreach (var item in operation.Items.Where(item => item.Status == "Pending"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.CancelRequested) break;
            if (!item.RecordId.HasValue || !File.Exists(Path.Combine(_root, "journal", item.RecordId.Value.ToString("N") + ".json")))
            {
                Reject(item, "BackupNotFound");
                continue;
            }

            var record = batchRecords.FirstOrDefault(candidate => candidate.Id == item.RecordId.Value);
            if (record is null)
            {
                Reject(item, "BackupNotFound");
                continue;
            }

            if (!fresh.TryGetValue(item.Key, out var entry))
            {
                Reject(item, "ActiveArtworkChanged");
                continue;
            }

            if (!IsMutationSafe(entry))
            {
                Reject(item, entry.IsShared ? "SharedReference" : entry.Reason ?? "OtherSlotUnavailable");
                continue;
            }

            if (!IsActiveRecord(record, verifyHash: true, journal) || File.Exists(record.Source.Path))
            {
                Reject(item, "BackupChanged");
                continue;
            }

            await WaitForIdleOperationAsync(operation, record.Source.ItemId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.CancelRequested) throw new OperationCanceledException();
            MarkMutationStartedAndConsumePlan(operation);
            try
            {
                await RetireRecordAsync(record, referenceCounts, slotSafe: true).ConfigureAwait(false);
                item.Status = "Retired";
                item.Reason = null;
                operation.ActualBilling.BackupBytesBefore += record.OriginalBytes;
                operation.ActualBilling.ReleasedBackupBytes += record.OriginalBytes;
                operation.RetentionPurgedCount++;
                operation.RetentionPurgedBytes += record.OriginalBytes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                item.Status = "Failed";
                item.Reason = SafeFailureMessage(ex);
                _logger.LogError(ex, "JellyP batch retirement failed at {Key} in operation {OperationId}", item.Key, operation.Id);
            }

            SaveOperation(operation);
            progress.Report(100d * ++index / Math.Max(1, operation.Items.Count));
        }

        if (operation.CancelRequested) MarkRemainingNotStarted(operation, "CancelledBeforeStart");
    }

    private IReadOnlyList<MigrationRecord> ValidateRetentionForApply(OperationRecord operation, PluginConfiguration config,
        IReadOnlySet<Guid> protectedIds)
    {
        var journal = LoadJournalSnapshot();
        var snapshot = BuildRetentionSnapshot(config, protectedIds, journal);
        if (!operation.AuthorizeRetentionPurge)
        {
            if (snapshot.Eligible.Count > 0)
                throw new InvalidOperationException("Other originals now expire under the current retention setting; this application requires that separate authorization and no image was changed.");
            return [];
        }

        if (!string.Equals(operation.RetentionSafetyFingerprint, snapshot.SafetyFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Global storage/retention limits changed after the expiry review; no image was changed. Review and apply again.");
        var authorized = operation.AuthorizedRetentionRecordIds.ToHashSet();
        var eligibleById = snapshot.Eligible.ToDictionary(record => record.Id);
        if (authorized.Count == 0 && snapshot.Eligible.Count > 0)
            throw new InvalidOperationException("The authorized expiry identity snapshot is empty or unavailable; no original was removed.");
        foreach (var id in authorized)
        {
            if (!eligibleById.TryGetValue(id, out var current)
                || !IsActiveRecord(current, verifyHash: true, journal))
                throw new InvalidOperationException("An authorized expiry identity changed after review; no original was removed. Review and apply again.");
        }

        return authorized.Select(id => eligibleById[id])
            .OrderBy(record => record.CompletedUtc).ThenBy(record => record.Id).ToArray();
    }

    private async Task PurgeRetentionForApplyAsync(OperationRecord operation,
        IReadOnlyCollection<MigrationRecord> authorized, CancellationToken cancellationToken)
    {
        if (authorized.Count == 0) return;
        var referenceCounts = _artwork.CountPathReferences(authorized
            .Where(record => record.OutputPath is not null).Select(record => record.OutputPath!).ToArray());
        operation.Phase = "RetiringExpiredOriginals";
        SaveOperation(operation);
        foreach (var record in authorized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForIdleOperationAsync(operation, record.Source.ItemId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.CancelRequested) throw new OperationCanceledException();
            MarkMutationStartedAndConsumePlan(operation);
            try
            {
                await RetireRecordAsync(record, referenceCounts, slotSafe: true).ConfigureAwait(false);
                operation.RetentionPurgedCount++;
                operation.RetentionPurgedBytes += record.OriginalBytes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JellyP retained uncertain original {RecordId} during retention", record.Id);
            }
        }
    }

    private void ApplyActualBilling(OperationRecord operation, PreparedApply pair)
    {
        var original = pair.Item.OriginalBytes ?? 0;
        operation.ActualBilling.ActiveBytesBefore += pair.Item.CurrentBytes ?? 0;
        operation.ActualBilling.ActiveBytesAfter += pair.Item.OutputBytes ?? 0;
        if (pair.Record is not null)
        {
            operation.ActualBilling.BackupBytesBefore += original;
            operation.ActualBilling.BackupBytesAfter += operation.PreserveOriginals ? original : 0;
            operation.ActualBilling.ExistingBackupBytes += original;
            if (!operation.PreserveOriginals) operation.ActualBilling.RemovedBackupBytes += original;
        }
        else if (operation.PreserveOriginals)
        {
            operation.ActualBilling.NewOriginalBytes += original;
            operation.ActualBilling.BackupBytesAfter += original;
        }
    }

    private static void Reject(OperationItem item, string reason)
    {
        item.Status = "Invalidated";
        item.Reason = reason;
    }

    private static void MarkRemainingNotStarted(OperationRecord operation, string reason)
    {
        foreach (var item in operation.Items.Where(item => item.Status == "Pending"))
        {
            item.Status = "NotStarted";
            item.Reason = reason;
        }
    }

    private async Task WaitForIdleOperationAsync(OperationRecord operation, Guid? itemId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _artwork.EnsureIdle(itemId);
                // A cancel can arrive while EnsureIdle is blocked; never start a transaction after it.
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.CancelRequested) throw new OperationCanceledException();
                if (operation.Phase == "WaitingForMetadata")
                {
                    operation.Phase = operation.Kind == "Apply" ? "Processing" : "Processing";
                    operation.Message = null;
                    SaveOperation(operation);
                }

                return;
            }
            catch (InvalidOperationException ex)
            {
                if (operation.Phase != "WaitingForMetadata" || operation.Message != ex.Message)
                {
                    operation.Phase = "WaitingForMetadata";
                    operation.Message = ex.Message;
                    SaveOperation(operation);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private OperationReceipt QueueBackupOperation(IReadOnlyCollection<Guid>? recordIds, string kind, bool permanent, CancellationToken cancellationToken)
    {
        var ids = (recordIds ?? []).ToArray();
        if (ids.Length == 0 || ids.Length > MaximumOperationBatch)
            throw new JellyPRequestException(400, "InvalidBackupSelection",
                $"Select between 1 and {MaximumOperationBatch} exact backup records.");
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new JellyPRequestException(400, "InvalidBackupSelection", "Backup IDs must be non-empty and unique; nothing was written.");

        var snapshot = LoadJournalSnapshot();
        var records = ActiveBackupRecords(snapshot);
        var shared = BackupSlotStates(records, cancellationToken);
        var byId = records.ToDictionary(record => record.Id);
        var operation = new OperationRecord
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            State = "Queued",
            Phase = "Queued",
            CreatedUtc = DateTime.UtcNow,
            AuthorizePermanentRemoval = permanent,
            Items = new List<OperationItem>(),
        };
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var record))
                throw new JellyPRequestException(409, "BackupNotEligible", "Every exact backup must still be an active committed transaction; nothing was queued.");
            var view = ToBackupView(record, shared);
            if (kind == "Restore" ? !view.Restorable : !view.Retirable)
                throw new JellyPRequestException(409, "BackupNotEligible", $"A selected backup is not currently {(kind == "Restore" ? "restorable" : "retirable")}; nothing was queued.");
            operation.Items.Add(new OperationItem
            {
                Key = record.Source.Key,
                RecordId = record.Id,
                ItemId = record.Source.ItemId,
                Category = record.Source.IsPerson ? "People" : record.Source.Type.ToString(),
                Type = record.Source.Type,
                Index = record.Source.Index,
                Action = kind,
                OriginalMimeType = record.OriginalMimeType,
                OriginalBytes = record.OriginalBytes,
                OutputBytes = record.OutputBytes,
                OriginalWidth = record.OriginalWidth,
                OriginalHeight = record.OriginalHeight,
                OutputWidth = record.OutputWidth,
                OutputHeight = record.OutputHeight,
                CurrentBytes = record.OutputBytes,
                CurrentHash = record.OutputHash,
                SourceHash = record.SourceHash,
                OutputHash = record.OutputHash,
                PolicyFingerprint = record.PolicyFingerprint,
            });
        }

        lock (_requestGate)
        {
            EnsureJournalWritable();
            if (_running || _pending is not null || HasPendingOrRunningOperation() || HasQueuedOperationFiles())
                throw new JellyPRequestException(409, "OperationBusy", "Another JellyP operation is running or queued.");
            DurableFiles.WriteJson(OperationPath(operation.Id), operation);
            _pendingOperationId = operation.Id;
        }

        TryPruneOperations();
        return ToReceipt(operation);
    }

    private void TryPruneOperations()
    {
        try { PruneOperations(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JellyP operation retention cleanup was skipped; the queued intent remains durable");
        }
    }

    private List<MigrationRecord> ActiveBackupRecords(JournalSnapshot? snapshot = null)
    {
        snapshot ??= LoadJournalSnapshot();
        var active = new List<MigrationRecord>();
        foreach (var record in snapshot.All.Where(record => record.State == "Committed" && !record.BackupDeleted)
            .OrderByDescending(record => record.CompletedUtc ?? record.CreatedUtc).ThenBy(record => record.Id))
        {
            if (IsActiveRecord(record, verifyHash: true, snapshot)) active.Add(record);
        }

        return active.GroupBy(record => record.Source.Key, StringComparer.Ordinal).Select(group => group.First()).ToList();
    }

    private bool IsMutationSafe(ArtworkInventoryEntry entry) =>
        !entry.IsShared && entry.State is ("Ready" or "AlreadyWebp") && _artwork.IsSlotSafeForMutation(entry.Reference);

    private Dictionary<string, BackupSlotState> BackupSlotStates(IReadOnlyCollection<MigrationRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0) return new Dictionary<string, BackupSlotState>(StringComparer.Ordinal);
        var items = records.Select(record => record.Source.ItemId).Distinct().ToArray();
        var entries = _artwork.DetailedInventory(cancellationToken, items, null);
        return entries.GroupBy(entry => entry.Reference.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group =>
            {
                var entry = group.First();
                var safe = IsMutationSafe(entry);
                return new BackupSlotState(entry.IsShared, safe,
                    entry.IsShared ? "SharedReference" : safe ? null : entry.Reason ?? "OtherSlotUnavailable");
            }, StringComparer.Ordinal);
    }

    private static BackupView ToBackupView(MigrationRecord record, IReadOnlyDictionary<string, BackupSlotState> shared)
    {
        var slot = shared.GetValueOrDefault(record.Source.Key);
        var active = record.State == "Committed" && !record.BackupDeleted;
        var restorable = active && slot?.IsMutationSafe == true;
        var retirable = restorable && !File.Exists(record.Source.Path);
        return new BackupView
        {
            RecordId = record.Id,
            ItemId = record.Source.ItemId,
            Key = record.Source.Key,
            Category = record.Source.IsPerson ? "People" : record.Source.Type.ToString(),
            Type = record.Source.Type,
            TypeName = record.Source.Type.ToString(),
            Index = record.Source.Index,
            State = record.State,
            Restorable = restorable,
            Retirable = retirable,
            Reason = slot is null ? "ActiveArtworkChanged"
                : !slot.IsMutationSafe ? slot.Reason
                : !active ? "NotActiveCommittedTransaction"
                : restorable && !retirable ? "OriginalStillPresent" : null,
            OriginalMimeType = record.OriginalMimeType,
            OriginalBytes = record.OriginalBytes,
            OutputBytes = record.OutputBytes,
            OriginalWidth = record.OriginalWidth,
            OriginalHeight = record.OriginalHeight,
            OutputWidth = record.OutputWidth,
            OutputHeight = record.OutputHeight,
            PolicyFingerprint = string.IsNullOrEmpty(record.PolicyFingerprint) ? null : record.PolicyFingerprint,
            SourceHash = record.SourceHash,
            OutputHash = record.OutputHash,
            CreatedUtc = record.CreatedUtc,
            CompletedUtc = record.CompletedUtc,
            Error = string.IsNullOrWhiteSpace(record.Error) ? null : "Recovery required; review server logs.",
            CleanupWarning = record.CleanupWarning,
        };
    }

    private static string NormalizeFilter(string? filter)
    {
        var normalized = string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim().ToLowerInvariant();
        if (normalized is not ("all" or "restorable" or "retirable"))
            throw new JellyPRequestException(400, "InvalidBackupFilter", "Backup filter must be all, restorable or retirable.");
        return normalized;
    }

    private void ReconcileStartupOperations()
    {
        JournalSnapshot journal;
        try
        {
            journal = LoadJournalSnapshot();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            _journalIntegrityError = "JellyP journal is corrupt or unreadable. New mutations are blocked; review server logs and recovery data.";
            journal = new JournalSnapshot([], new Dictionary<string, MigrationRecord>(StringComparer.Ordinal),
                new Dictionary<string, MigrationRecord>(StringComparer.Ordinal),
                new Dictionary<string, MigrationRecord[]>(StringComparer.Ordinal));
            _logger.LogError(ex, "JellyP could not index journals at startup");
        }

        if (!Directory.Exists(OperationsDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(OperationsDirectory, "*.json"))
        {
            OperationRecord operation;
            try { operation = DurableFiles.ReadJson<OperationRecord>(path); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "JellyP skipped one unreadable operation file at startup");
                continue;
            }

            if (operation.State is not ("Queued" or "Running")) continue;
            var hadCommitted = journal.All.Any(record => record.ExecutionId == operation.Id && record.State == "Committed");
            var changed = false;
            var uncertain = false;
            foreach (var item in operation.Items.Where(item => item.Status == "Pending"))
            {
                var candidates = journal.All.Where(record => record.ExecutionId == operation.Id && record.Source.Key == item.Key).ToArray();
                var committed = candidates.Where(record => record.State == "Committed").OrderByDescending(record => record.CompletedUtc).FirstOrDefault();
                if (committed is not null && committed.OutputPath is not null)
                {
                    try
                    {
                        var current = _artwork.GetCurrent(committed.Source);
                        if (current is not null && string.Equals(current.Path, committed.OutputPath, PathComparison)
                            && File.Exists(current.Path)
                            && DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) == committed.OutputHash)
                        {
                            item.Status = "Changed";
                            item.RecordId = committed.Id;
                            item.OriginalMimeType = committed.OriginalMimeType;
                            item.OriginalBytes = committed.OriginalBytes;
                            item.OutputBytes = committed.OutputBytes;
                            item.OriginalWidth = committed.OriginalWidth;
                            item.OriginalHeight = committed.OriginalHeight;
                            item.OutputWidth = committed.OutputWidth;
                            item.OutputHeight = committed.OutputHeight;
                            item.SourceHash = committed.SourceHash;
                            item.OutputHash = committed.OutputHash;
                            item.PolicyFingerprint = committed.PolicyFingerprint;
                            operation.ActualBilling.ActiveBytesBefore += item.CurrentBytes ?? 0;
                            operation.ActualBilling.ActiveBytesAfter += committed.OutputBytes;
                            if (item.Action == "Regenerate")
                            {
                                operation.ActualBilling.BackupBytesBefore += committed.OriginalBytes;
                                operation.ActualBilling.ExistingBackupBytes += committed.OriginalBytes;
                                operation.ActualBilling.BackupBytesAfter += operation.PreserveOriginals ? committed.OriginalBytes : 0;
                                if (!operation.PreserveOriginals) operation.ActualBilling.RemovedBackupBytes += committed.OriginalBytes;
                            }
                            else if (operation.PreserveOriginals)
                            {
                                operation.ActualBilling.NewOriginalBytes += committed.OriginalBytes;
                                operation.ActualBilling.BackupBytesAfter += committed.OriginalBytes;
                            }

                            changed = true;
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        _logger.LogError(ex, "JellyP could not verify committed evidence for operation {OperationId}", operation.Id);
                    }

                    item.Status = "Failed";
                    item.Reason = "CommittedArtworkChanged";
                    uncertain = true;
                    continue;
                }

                if (candidates.Any(record => record.State is "Prepared" or "Restoring" or "RecoveryRequired"))
                {
                    item.Status = "Failed";
                    item.Reason = "RecoveryRequired";
                    uncertain = true;
                    continue;
                }

                if (operation.MutationStarted && item.RecordId.HasValue)
                {
                    var previous = journal.All.FirstOrDefault(record => record.Id == item.RecordId.Value);
                    if (previous is not null)
                    {
                        try
                        {
                            var current = _artwork.GetCurrent(previous.Source);
                            if (previous.State == "RolledBack" && current is not null && File.Exists(current.Path)
                                && DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) == previous.SourceHash)
                            {
                                item.Status = "Failed";
                                item.Reason = "InterruptedAfterRestore";
                                operation.ActualBilling.ActiveBytesBefore += item.CurrentBytes ?? 0;
                                operation.ActualBilling.ActiveBytesAfter += previous.OriginalBytes;
                                operation.ActualBilling.BackupBytesBefore += previous.OriginalBytes;
                                operation.ActualBilling.RemovedBackupBytes += previous.OriginalBytes;
                                continue;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                        {
                            _logger.LogError(ex, "JellyP could not verify restore interruption for operation {OperationId}", operation.Id);
                        }

                        item.Status = "Failed";
                        item.Reason = "InterruptedAfterRestoreUncertain";
                        uncertain = true;
                    }
                }
            }

            operation.State = "Interrupted";
            operation.Phase = "Interrupted";
            operation.Message = uncertain
                ? "Jellyfin restarted with uncertain image evidence. New mutations remain blocked until explicit recovery."
                : "Jellyfin restarted before the operation completed. It was not replayed automatically; review individual results.";
            operation.CompletedUtc = DateTime.UtcNow;
            foreach (var item in operation.Items.Where(item => item.Status == "Pending"))
            {
                item.Status = "NotStarted";
                item.Reason = changed ? "InterruptedBeforeCompletion" : "InterruptedBeforeMutation";
            }

            try
            {
                SaveOperation(operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyP could not persist interrupted operation {OperationId}", operation.Id);
            }

            try
            {
                if (!operation.Kind.Equals("Apply", StringComparison.Ordinal) || !operation.PlanId.HasValue) continue;
                if (hadCommitted || operation.MutationStarted || operation.RetentionPurgedCount > 0)
                    MarkPlanConsumed(operation);
                else
                    ReleasePlanReservationIfUnmutated(operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyP could not reconcile interrupted plan reservation {OperationId}", operation.Id);
            }
        }
    }

    private bool HasQueuedOperationFiles() => Directory.Exists(OperationsDirectory)
        && Directory.EnumerateFiles(OperationsDirectory, "*.json").Any(path =>
        {
            try { return DurableFiles.ReadJson<OperationRecord>(path).State == "Queued"; }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { return false; }
        });

    private IEnumerable<OperationRecord> Operations() => Directory.Exists(OperationsDirectory)
        ? Directory.EnumerateFiles(OperationsDirectory, "*.json").Select(DurableFiles.ReadJson<OperationRecord>)
        : [];

    private OperationRecord ReadOperation(Guid operationId)
    {
        var path = OperationPath(operationId);
        if (!File.Exists(path)) throw new JellyPRequestException(404, "OperationNotFound", "The JellyP operation was not found.");
        return DurableFiles.ReadJson<OperationRecord>(path);
    }

    private string OperationPath(Guid operationId) => Path.Combine(OperationsDirectory, operationId.ToString("N") + ".json");

    private void MarkMutationStarted(OperationRecord operation)
    {
        if (operation.MutationStarted) return;
        var updated = JsonSerializer.Deserialize<OperationRecord>(JsonSerializer.Serialize(operation))!;
        updated.MutationStarted = true;
        try
        {
            SaveOperation(updated);
        }
        catch
        {
            // The marker may have been published before the write failed. Only a
            // readable record that still says false proves no durable mutation marker.
            operation.MutationStarted = HasDurableMutationStarted(operation.Id);
            throw;
        }

        operation.MutationStarted = true;
        operation.CancelRequested |= updated.CancelRequested;
    }

    private bool HasDurableMutationStarted(Guid operationId)
    {
        try
        {
            var path = OperationPath(operationId);
            return File.Exists(path) && DurableFiles.ReadJson<OperationRecord>(path).MutationStarted;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            return true; // Unknown durability is treated as a started mutation; keep the reservation.
        }
    }

    // Test-only seams: production never sets these. They let regression tests
    // simulate a finalization or persistence failure at a controlled boundary.
    private static readonly AsyncLocal<Action?> _cancellationRequestReadHook = new();
    private static readonly AsyncLocal<Action<bool, bool, string>?> _operationSaveTestHook = new();

    private void MarkMutationStartedAndConsumePlan(OperationRecord operation)
    {
        if (operation.MutationStarted) return;
        MarkMutationStarted(operation);
        MarkPlanConsumed(operation);
    }

    private MigrationRecord? TryReadRecord(Guid recordId)
    {
        var path = Path.Combine(_root, "journal", recordId.ToString("N") + ".json");
        if (!File.Exists(path)) return null;
        try { return DurableFiles.ReadJson<MigrationRecord>(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "JellyP could not read journal record {RecordId}", recordId);
            return null;
        }
    }

    private void SaveOperation(OperationRecord operation)
    {
        lock (_operationStateGate)
        {
            var path = OperationPath(operation.Id);
            // Cancellation is monotonic: a progress write from the executor must not
            // erase a concurrent cancel flag persisted by the request thread.
            if (File.Exists(path))
            {
                try
                {
                    var persisted = DurableFiles.ReadJson<OperationRecord>(path);
                    operation.CancelRequested |= persisted.CancelRequested;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    // Keep the in-memory evidence; the caller still owns the gate and
                    // will report a persistence failure instead of silently mutating.
                }
            }

            operation.Processed = operation.Items.Count(item => item.Status is not "Pending" and not "NotStarted");
            operation.Changed = operation.Items.Count(item => item.Status is "Changed" or "Restored" or "Retired");
            operation.Failed = operation.Items.Count(item => item.Status == "Failed");
            operation.Invalidated = operation.Items.Count(item => item.Status == "Invalidated");
            operation.NotStarted = operation.Items.Count(item => item.Status == "NotStarted");
            _operationSaveTestHook.Value?.Invoke(operation.MutationStarted, false, operation.State);
            DurableFiles.WriteJson(path, operation);
            _operationSaveTestHook.Value?.Invoke(operation.MutationStarted, true, operation.State);
        }
    }

    private void PruneOperations()
    {
        if (!Directory.Exists(OperationsDirectory)) return;
        var protectedOperations = Directory.Exists(PlansDirectory)
            ? Directory.EnumerateFiles(PlansDirectory, "*.json")
                .Select(path =>
                {
                    try { return DurableFiles.ReadJson<PlanRecord>(path).OperationId; }
                    catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { return null; }
                })
                .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet()
            : new HashSet<Guid>();
        var files = Directory.EnumerateFiles(OperationsDirectory, "*.json").Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        foreach (var file in files.Skip(MaximumOperationHistory))
        {
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out var id)
                && (_currentOperationId == id || _pendingOperationId == id || protectedOperations.Contains(id))) continue;
            try { DurableFiles.Delete(file.FullName); }
            catch (IOException) { }
        }
    }

    private static OperationReceipt ToReceipt(OperationRecord operation) => new()
    {
        OperationId = operation.Id,
        Kind = operation.Kind,
        State = operation.State,
        Poll = "/JellyP/Executions/" + operation.Id.ToString("N"),
    };

    private static OperationView ToView(OperationRecord operation) => new()
    {
        OperationId = operation.Id,
        PlanId = operation.PlanId,
        Kind = operation.Kind,
        State = operation.State,
        Phase = operation.Phase,
        Message = operation.Message,
        CancelRequested = operation.CancelRequested,
        MutationStarted = operation.MutationStarted,
        CreatedUtc = operation.CreatedUtc,
        StartedUtc = operation.StartedUtc,
        CompletedUtc = operation.CompletedUtc,
        TotalItems = operation.Items.Count,
        Processed = operation.Items.Count(item => item.Status is not "Pending" and not "NotStarted"),
        Changed = operation.Items.Count(item => item.Status is "Changed" or "Restored" or "Retired"),
        Failed = operation.Items.Count(item => item.Status == "Failed"),
        Invalidated = operation.Items.Count(item => item.Status == "Invalidated"),
        NotStarted = operation.Items.Count(item => item.Status == "NotStarted"),
        RetentionPurgedCount = operation.RetentionPurgedCount,
        RetentionPurgedBytes = operation.RetentionPurgedBytes,
        Billing = operation.ActualBilling,
    };

    private static OperationItemView ToItemView(OperationItem item) => new()
    {
        Key = item.Key,
        RecordId = item.RecordId,
        ItemId = item.ItemId,
        Category = item.Category,
        Type = item.Type,
        TypeName = item.Type.ToString(),
        Index = item.Index,
        Name = item.Name,
        Action = item.Action,
        Status = item.Status,
        Reason = item.Reason,
        OriginalBytes = item.OriginalBytes,
        OutputBytes = item.OutputBytes,
        OriginalWidth = item.OriginalWidth,
        OriginalHeight = item.OriginalHeight,
        OutputWidth = item.OutputWidth,
        OutputHeight = item.OutputHeight,
        OriginalMimeType = item.OriginalMimeType,
        SourceHash = item.SourceHash,
        OutputHash = item.OutputHash,
        PolicyFingerprint = item.PolicyFingerprint,
        CleanupWarning = item.CleanupWarning,
    };

    private static string PolicySnapshot(PluginConfiguration config, ImageTypePolicy policy) =>
        JsonSerializer.Serialize(config.OptionsFor(policy));

    private static string SafeFailureMessage(Exception exception) => exception switch
    {
        JellyPRequestException request => request.Message,
        _ => "The operation stopped safely. Review individual results, stored originals and recovery state.",
    };

    private sealed record PreparedApply(OperationItem Item, PlanImage PlanImage, ArtworkReference Current,
        byte[] Source, WebpOptimizationResult Optimized, MigrationRecord? Record, ImageTypePolicy Policy);
}
