using System.Text.Json;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Conversion;
using Jellyfin.Plugin.JellyP.Infrastructure;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyP.Migration;

/// <summary>Serializes bounded scans, journaled image transactions and rollback operations.</summary>
public sealed partial class OptimizationCoordinator
{
    internal const int PlanSchemaVersion = 2;
    internal const int MaximumPlanBatch = 1000;
    internal const int MaximumPlanImages = 5000;
    internal const int MaximumOperationBatch = 200;
    internal const int MaximumOperationHistory = 200;
    internal const int MaximumPlanHistory = 20;
    internal const long MaximumPlanDiskBytes = 64L * 1024 * 1024;
    internal static readonly TimeSpan PlanLifetime = TimeSpan.FromHours(24);
    private readonly IArtworkStore _artwork;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly Action<PluginConfiguration>? _saveConfiguration;
    private readonly string _root;
    private readonly ILogger<OptimizationCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _running;
    private readonly object _requestGate = new();
    private readonly object _operationStateGate = new();
    private readonly object _planStateGate = new();
    private PendingExecution? _pending;
    private CancellationTokenSource? _manualCancellation;
    private Guid? _pendingOperationId;
    private Guid? _currentOperationId;
    private CancellationTokenSource? _operationCancellation;
    private volatile string? _journalIntegrityError;
    private sealed record PendingExecution(bool Synchronize, PluginConfiguration Configuration, RunReport Report)
    {
        public bool Cancelled { get; set; }
    }

    /// <summary>Captures one bounded manual request before dispatch through Jellyfin's scheduler.</summary>
    public Guid QueueExecution(bool synchronize, bool? dryRunOverride = null)
    {
        lock (_requestGate)
        {
            EnsureJournalWritable();
            if (_running || _pending is not null || HasPendingOrRunningOperation() || HasQueuedOperationFiles())
                throw new InvalidOperationException("A JellyP operation is already running or queued.");
            var config = SnapshotConfiguration();
            if (dryRunOverride.HasValue)
            {
                config.DryRun = dryRunOverride.Value;
                if (!config.DryRun) config.Enabled = true; // Explicit elevated manual request; persisted automation stays unchanged.
            }
            if (!config.DryRun && !config.Enabled)
                throw new InvalidOperationException("Enable artwork changes in JellyP settings first, or simulate.");
            var report = new RunReport { ExecutionId = Guid.NewGuid(), DryRun = config.DryRun, Phase = "Queued" };
            _pending = new PendingExecution(synchronize, config, report);
            return report.ExecutionId;
        }
    }

    /// <summary>Releases a request whose native dispatch failed.</summary>
    public void RejectQueuedExecution(Guid executionId)
    {
        lock (_requestGate)
            if (_pending?.Report.ExecutionId == executionId) _pending = null;
    }

    /// <summary>Cancels a queued request without allowing its eventual native invocation to mutate artwork.</summary>
    public void CancelQueuedExecution()
    {
        lock (_requestGate)
        {
            _manualCancellation?.Cancel();
            if (_pending is not null)
            {
                _pending.Cancelled = true;
                _pending.Report.Phase = "Cancelled";
                _pending.Report.FinishedUtc = DateTime.UtcNow;
                _pending.Report.Message = "Cancelled before execution.";
            }
        }

        RequestCancelOperation();
    }

    /// <summary>Creates a coordinator; this constructor does not modify artwork.</summary>
    public OptimizationCoordinator(IArtworkStore artwork, Func<PluginConfiguration> configuration,
        string root, ILogger<OptimizationCoordinator> logger, Action<PluginConfiguration>? saveConfiguration = null)
    {
        _artwork = artwork;
        _configuration = configuration;
        _root = root;
        _logger = logger;
        _saveConfiguration = saveConfiguration;
        ReconcileStartupOperations();
        TryRecoverBackupDeletionIntents();
        TryPruneOperations();
        TryPrunePlans();
    }

    /// <summary>Returns bounded administrator output and storage accounting.</summary>
    public OptimizerStatus GetStatus()
    {
        var reportPath = Path.Combine(_root, "last-run.json");
        RunReport report;
        try
        {
            report = File.Exists(reportPath) ? DurableFiles.ReadJson<RunReport>(reportPath) : new RunReport();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            report = new RunReport
            {
                Phase = "RecoveryRequired",
                Message = "Legacy JellyP report is unreadable. Review server logs.",
            };
        }

        try
        {
            var records = Records().ToArray();
            lock (_requestGate)
                if (_pending is not null) report = JsonSerializer.Deserialize<RunReport>(JsonSerializer.Serialize(_pending.Report))!;
            report.Running = _running;
            var committed = records.Where(r => r.State == "Committed")
                .GroupBy(r => r.Source.Key).Select(g => g.OrderByDescending(r => r.CompletedUtc).First())
                .Where(r => r.OutputPath is not null && File.Exists(r.OutputPath) && !File.Exists(r.Source.Path)
                    && _artwork.GetCurrent(r.Source)?.Path == r.OutputPath).ToArray();
            var backups = ActualBackupBytes();
            var savings = committed.Sum(r => r.OriginalBytes - r.OutputBytes);
            return new OptimizerStatus(report, backups, savings, savings - backups,
                records.OrderByDescending(r => r.CreatedUtc).Take(100).ToArray(),
                records.Count(r => r.State is "Prepared" or "Restoring" or "RecoveryRequired"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            // A corrupt journal must block mutations but must not prevent the plugin
            // page/status from loading and explaining that recovery is required.
            _journalIntegrityError = "JellyP journal is corrupt or unreadable. New mutations are blocked; review server logs and recovery data.";
            report.Running = _running;
            report.Phase = "RecoveryRequired";
            report.Message = _journalIntegrityError;
            return new OptimizerStatus(report, ActualBackupBytes(), 0, 0, [], 1);
        }
    }

    /// <summary>Runs the next bounded batch, honoring cancellation between transactions.</summary>
    public async Task RunAsync(IProgress<double> progress, CancellationToken cancellationToken, bool synchronize = false, bool? dryRunOverride = null, bool waitForOtherOperations = false)
    {
        PendingExecution? request = null;
        while (true)
        {
            lock (_requestGate)
            {
                if (HasPendingOrRunningOperation() || HasQueuedOperationFiles())
                    throw new InvalidOperationException("A JellyP operation is already running or queued.");
                if (!_running && (_pending is null || _pending.Synchronize == synchronize))
                {
                    request = _pending;
                    if (request is not null && (request.Cancelled || cancellationToken.IsCancellationRequested))
                    {
                        request.Report.Phase = "Cancelled";
                        request.Report.FinishedUtc = DateTime.UtcNow;
                        request.Report.Message = "Cancelled before execution.";
                        SaveReport(request.Report);
                        _pending = null;
                        return;
                    }
                    if (_gate.Wait(0, cancellationToken))
                    {
                        _pending = null;
                        _running = true;
                        break;
                    }
                }
                if (!waitForOtherOperations)
                    throw new InvalidOperationException("A JellyP operation is already running or queued.");
            }
            // Native scheduled tasks serialize without claiming or rewriting the active execution's report.
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        RunReport? report = request?.Report ?? new RunReport { ExecutionId = Guid.NewGuid() };
        try
        {
            if (request?.Cancelled == true)
            {
                report.Phase = "Cancelled";
                return;
            }
            var config = request?.Configuration ?? SnapshotConfiguration();
            if (request is null && dryRunOverride.HasValue)
            {
                config.DryRun = dryRunOverride.Value;
                if (!config.DryRun) config.Enabled = true;
            }
            report.DryRun = config.DryRun;
            report.Running = true;
            report.StartedUtc = DateTime.UtcNow;
            report.Phase = "Preparing";
            SaveReport(report);
            if (!config.DryRun && !config.Enabled)
            {
                throw new InvalidOperationException("Enable artwork changes in JellyP settings first, or use dry run.");
            }

            DurableFiles.CreateDirectory(_root);
            // Probe the host codec before inventory or any transaction, including dry run.
            using (var probe = new SkiaSharp.SKBitmap(1, 1))
            using (var image = SkiaSharp.SKImage.FromBitmap(probe))
            using (var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 85))
            {
                if (encoded is null || encoded.Size == 0)
                {
                    throw new NotSupportedException("The Jellyfin host does not provide a working WebP codec.");
                }
            }

            RecoverBackupDeletionIntents();
            if (!config.DryRun && Records().Any(r => r.State is "Prepared" or "Restoring" or "RecoveryRequired"))
            {
                throw new InvalidOperationException("Recover interrupted image transactions before continuing.");
            }

            if (!config.DryRun)
            {
                await WaitForIdleAsync(null, report, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                FinalizeRolledBackRecords();
                CleanupStaging();
            }

            if (!config.DryRun && !synchronize && config.BackupRetentionDays > 0)
            {
                await PurgeCoreAsync(DateTime.UtcNow.AddDays(-config.BackupRetentionDays), cancellationToken).ConfigureAwait(false);
            }

            var cursorPath = Path.Combine(_root, (synchronize ? "sync-" : "optimize-") + (config.DryRun ? "cursor-preview.json" : "cursor-apply.json"));
            var cursor = ReadCursor(cursorPath, CursorContext(config, synchronize));
            report.Cursor = cursor.LastKey;
            report.Phase = "Inventory";
            SaveReport(report);
            var itemIds = config.ScopeMode is "legacy" or "items" ? config.IncludedItemIds : [];
            var libraryIds = config.ScopeMode == "libraries" ? config.IncludedLibraryIds : null;
            var eligible = _artwork.Inventory(cancellationToken, itemIds, libraryIds, count =>
            {
                report.InventoryItems = count;
                SaveReport(report);
            });
            report.Inventory = _artwork.GetInventorySummary();
            var syncRecords = synchronize ? Records().Where(r => r.State == "Committed")
                .GroupBy(r => r.Source.Key).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CompletedUtc).First()) : [];
            IReadOnlyList<ArtworkReference> inventory = synchronize
                ? syncRecords.Values.Select(r => r.Source).OrderBy(r => r.Key, StringComparer.Ordinal).ToArray()
                : eligible.Where(r => !Path.GetExtension(r.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (synchronize)
            {
                var eligibleKeys = eligible.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
                inventory = inventory.Where(r => eligibleKeys.Contains(r.Key)).ToArray();
            }
            if (itemIds.Length > 0)
                inventory = inventory.Where(r => itemIds.Contains(r.ItemId)).ToArray();
            // A batch represents selected categories, rather than unrelated artwork that would only be skipped.
            inventory = inventory.Where(r => config.PolicyFor(r) is { Enabled: true })
                .OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
            report.InventoryImages = inventory.Count;
            SaveReport(report);
            var candidates = inventory.Where(r => string.CompareOrdinal(r.Key, cursor.LastKey) > 0).Take(config.BatchSize).ToArray();
            var converter = new WebpImageConverter();
            foreach (var reference in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!config.DryRun) await WaitForIdleAsync(reference.ItemId, report, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                report.Phase = "Processing";
                report.Message = null;
                SaveReport(report);
                try
                {
                    var policy = config.PolicyFor(reference);
                    string? earlySkip = null;
                    MigrationRecord? syncRecord = null;
                    if (synchronize && earlySkip is null)
                    {
                        syncRecord = syncRecords[reference.Key];
                        if (syncRecord.BackupDeleted || !File.Exists(Backup(syncRecord))) earlySkip = "OriginalUnavailable";
                        else if (!eligible.Any(r => r.Key == reference.Key)) earlySkip = "SharedOrIneligibleImage";
                        else if (!config.SyncUnchangedPolicies && syncRecord.PolicyFingerprint == config.Fingerprint(policy!)) earlySkip = "PolicyUnchanged";
                        else
                        {
                            var current = _artwork.GetCurrent(reference);
                            if (current?.Path != syncRecord.OutputPath || current is null
                                || DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) != syncRecord.OutputHash)
                                earlySkip = "ArtworkChanged";
                        }
                    }

                    if (earlySkip is not null)
                    {
                        report.Skipped++;
                        report.SkipReasons[earlySkip] = report.SkipReasons.GetValueOrDefault(earlySkip) + 1;
                        report.Examined++;
                        report.Cursor = reference.Key;
                        cursor.LastKey = reference.Key;
                        DurableFiles.WriteJson(cursorPath, cursor);
                        SaveReport(report);
                        continue;
                    }

                    var bytes = DurableFiles.ReadImage(syncRecord is null ? reference.Path : Backup(syncRecord));
                    if (syncRecord is not null && DurableFiles.Hash(bytes) != syncRecord.SourceHash)
                        throw new IOException("Preserved original checksum failed; no image was changed.");
                    var result = converter.Optimize(bytes, config.OptionsFor(policy!));
                    if (report.Samples.Count < 20)
                        report.Samples.Add(new ImageRunSample(reference.ItemId, reference.Type, bytes.LongLength,
                            result.OutputBytes?.LongLength ?? 0, result.Success ? "WouldConvert" : result.SkipReason.ToString()));
                    if (!result.Success)
                    {
                        report.Skipped++;
                        var reason = result.SkipReason.ToString();
                        report.SkipReasons[reason] = report.SkipReasons.GetValueOrDefault(reason) + 1;
                    }
                    else
                    {
                        report.WouldConvert++;
                        if (!config.DryRun)
                        {
                            await WaitForIdleAsync(reference.ItemId, report, cancellationToken).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            report.Phase = "Processing";
                            var target = reference;
                            if (syncRecord is not null)
                            {
                                // Reuse verified rollback, then normal conversion. A crash between phases leaves a valid original.
                                await RestoreCoreAsync(syncRecord, config).ConfigureAwait(false);
                                target = _artwork.GetCurrent(reference) ?? throw new IOException("Restored image slot is missing.");
                            }

                            await ConvertAsync(target, bytes, result.OutputBytes!, config, config.Fingerprint(policy!),
                                evidence: result, policySnapshot: JsonSerializer.Serialize(config.OptionsFor(policy!))).ConfigureAwait(false);
                            report.Converted++;
                        }

                        report.EstimatedSavingsBytes += bytes.Length - result.OutputBytes!.Length;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
                {
                    report.Skipped++;
                    report.SkipReasons[ex.GetType().Name] = report.SkipReasons.GetValueOrDefault(ex.GetType().Name) + 1;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    report.Failed++;
                    _logger.LogError(ex, "JellyP stopped at item {ItemId}, image {ImageType}/{Index}", reference.ItemId, reference.Type, reference.Index);
                    // Retain this cursor for a retry; any uncertain transaction requires recovery first.
                    throw;
                }

                report.Examined++;
                report.Cursor = reference.Key;
                cursor.LastKey = reference.Key;
                DurableFiles.WriteJson(cursorPath, cursor);
                SaveReport(report);
                progress.Report(100d * report.Examined / Math.Max(1, candidates.Length));
            }

            if (candidates.Length < config.BatchSize || candidates.LastOrDefault()?.Key == inventory.LastOrDefault()?.Key)
            {
                report.Cursor = string.Empty;
                cursor.LastKey = string.Empty;
                DurableFiles.WriteJson(cursorPath, cursor);
                report.Message = "Inventory pass complete. The next run starts a new pass, including newly downloaded artwork.";
            }
            else
            {
                report.Message = "Batch complete. Run again to resume at the next image.";
            }

            if (inventory.Count == 0)
                report.Message = "No eligible images match the selected scope, image types and operation. Review the inventory counts for already-WebP, shared and excluded images.";
            report.Phase = "Completed";
            progress.Report(100);
        }
        catch (OperationCanceledException)
        {
            if (report is not null)
            {
                report.Phase = "Cancelled";
                report.Message = "Cancelled between image transactions; resume with the next run.";
            }
            throw;
        }
        catch (Exception ex)
        {
            report ??= new RunReport { StartedUtc = DateTime.UtcNow, DryRun = true };
            report.Phase = "Failed";
            report.Failed = Math.Max(1, report.Failed);
            report.Message = ex.Message;
            throw;
        }
        finally
        {
            _running = false;
            try
            {
                if (report is not null)
                {
                    report.Running = false;
                    report.FinishedUtc = DateTime.UtcNow;
                    SaveReport(report);
                }
            }
            finally { _gate.Release(); }
        }
    }

    private static string CursorContext(PluginConfiguration config, bool synchronize) => DurableFiles.Hash(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            config.ScopeMode,
            IncludedLibraryIds = config.IncludedLibraryIds.Select(id => id.ToString("N")).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            IncludedItemIds = config.IncludedItemIds.Select(id => id.ToString("N")).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Policies = config.ImagePolicies.Where(policy => policy.Enabled).OrderBy(policy => policy.Category, StringComparer.Ordinal)
                .Select(policy => new { policy.Category, Fingerprint = config.Fingerprint(policy) }).ToArray(),
            SyncUnchangedPolicies = synchronize && config.SyncUnchangedPolicies,
        }));

    private static ScanCursor ReadCursor(string path, string contextFingerprint)
    {
        var fresh = new ScanCursor { ContextFingerprint = contextFingerprint };
        if (!File.Exists(path)) return fresh;
        var payload = DurableFiles.ReadJson<JsonElement>(path);
        // Old versions stored only the key, which cannot safely be associated with the current selection.
        if (payload.ValueKind == JsonValueKind.String) return fresh;
        var saved = payload.Deserialize<ScanCursor>() ?? throw new InvalidDataException("Invalid JellyP scan cursor.");
        return saved.Version == 1 && saved.ContextFingerprint == contextFingerprint && saved.LastKey is not null ? saved : fresh;
    }

    /// <summary>Restores a committed or interrupted transaction through Jellyfin services.</summary>
    public Task RollbackAsync(Guid id, CancellationToken cancellationToken, bool authorizedManual = false) =>
        RunMaintenanceAsync("Restoring", authorizedManual, cancellationToken, async (config, _) =>
        {
            FinalizeRolledBackRecords();
            var record = ReadRecord(id);
            if (record.BackupDeleted)
                throw new InvalidOperationException("This original is no longer available for rollback.");
            await RestoreCoreAsync(record, config).ConfigureAwait(false);
        });

    /// <summary>Retires only verified committed backups older than the specified cutoff.</summary>
    public Task PurgeAsync(DateTime cutoffUtc, CancellationToken cancellationToken, bool authorizedManual = false) =>
        RunMaintenanceAsync("RetiringOriginals", authorizedManual, cancellationToken, async (_, token) =>
        {
            FinalizeRolledBackRecords();
            await PurgeCoreAsync(cutoffUtc, token).ConfigureAwait(false);
        });

    private async Task RunMaintenanceAsync(string phase, bool authorizedManual, CancellationToken cancellationToken,
        Func<PluginConfiguration, CancellationToken, Task> action)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_requestGate) _manualCancellation = linkedCancellation;
        cancellationToken = linkedCancellation.Token;
        var report = new RunReport { ExecutionId = Guid.NewGuid(), StartedUtc = DateTime.UtcNow, Running = true, Phase = "Preparing" };
        try
        {
            var config = SnapshotConfiguration();
            if (!authorizedManual) RequireMaintenance(config);
            RecoverBackupDeletionIntents();
            await WaitForIdleAsync(null, report, cancellationToken).ConfigureAwait(false);
            report.Phase = phase;
            report.Message = null;
            SaveReport(report);
            cancellationToken.ThrowIfCancellationRequested();
            await action(config, cancellationToken).ConfigureAwait(false);
            report.Phase = "Completed";
            report.Message = phase == "Restoring" ? "Original restored." : "Verified stored originals retired.";
        }
        catch (OperationCanceledException)
        {
            report.Phase = "Cancelled";
            report.Message = "Cancelled before the next image transaction.";
            throw;
        }
        catch (Exception ex)
        {
            report.Phase = "Failed";
            report.Failed = 1;
            report.Message = ex.Message;
            throw;
        }
        finally
        {
            lock (_requestGate) _manualCancellation = null;
            _running = false;
            report.Running = false;
            report.FinishedUtc = DateTime.UtcNow;
            try { SaveReport(report); }
            finally { _gate.Release(); }
        }
    }

    private async Task<MigrationRecord> ConvertAsync(ArtworkReference reference, byte[] source, byte[] output, PluginConfiguration config, string policyFingerprint,
        Guid? executionId = null, WebpOptimizationResult? evidence = null, string? policySnapshot = null)
    {
        _artwork.EnsureIdle(reference.ItemId);
        var current = _artwork.GetCurrent(reference);
        if (current?.Path != reference.Path || DurableFiles.Hash(DurableFiles.ReadImage(reference.Path)) != DurableFiles.Hash(source))
        {
            throw new IOException("Artwork changed after inventory; retry in an idle window.");
        }

        var destination = _artwork.GetDestination(reference, "image/webp");
        EnsureDestinationAvailable(destination);
        var retained = ActualBackupBytes();
        if (retained + source.Length > config.MaximumBackupBytes)
        {
            throw new IOException("Rollback backup budget reached. Retire verified backups or increase the budget before resuming.");
        }

        DurableFiles.EnsureSpace(_root, checked(config.MinimumFreeSpaceBytes + source.Length + 2L * output.Length));
        DurableFiles.EnsureSpace(destination, checked(config.MinimumFreeSpaceBytes + output.Length));
        var record = new MigrationRecord
        {
            Source = reference,
            SourceHash = DurableFiles.Hash(source),
            OutputHash = DurableFiles.Hash(output),
            OriginalMimeType = DetectOriginalMime(source),
            OriginalBytes = source.Length,
            OutputBytes = output.Length,
            OutputPath = destination,
            PolicyFingerprint = policyFingerprint,
            ExecutionId = executionId,
            PolicySnapshot = policySnapshot,
            OriginalWidth = evidence?.SourceWidth,
            OriginalHeight = evidence?.SourceHeight,
            OutputWidth = evidence?.OutputWidth,
            OutputHeight = evidence?.OutputHeight,
            Ssim = evidence?.Ssim,
        };
        // Publish the original before the intent record. An interrupted backup write can never start a save.
        DurableFiles.Write(Backup(record), source);
        SaveRecord(record);
        try
        {
            _artwork.EnsureIdle(reference.ItemId);
            if (_artwork.GetCurrent(reference)?.Path != reference.Path
                || DurableFiles.Hash(DurableFiles.ReadImage(reference.Path)) != record.SourceHash)
            {
                throw new IOException("Artwork changed before commit.");
            }

            EnsureDestinationAvailable(destination);
            var staging = Staging(record.Id);
            DurableFiles.Write(staging, output);
            var saved = await _artwork.SaveAsync(reference, staging, "image/webp").ConfigureAwait(false);
            if (saved.Path != destination || DurableFiles.Hash(DurableFiles.ReadImage(saved.Path)) != record.OutputHash
                || _artwork.GetCurrent(reference)?.Path != saved.Path || File.Exists(reference.Path))
            {
                throw new IOException("Saved artwork, reference, or original removal could not be verified.");
            }

            DurableFiles.FlushParentDirectory(saved.Path);
            DurableFiles.FlushParentDirectory(reference.Path);

            record.State = "Committed";
            record.CompletedUtc = DateTime.UtcNow;
            SaveRecord(record);
        }
        catch (Exception ex)
        {
            record.State = "RecoveryRequired";
            record.Error = ex.Message;
            SaveRecord(record);
            try
            {
                await RestoreCoreAsync(record, config).ConfigureAwait(false);
            }
            catch (Exception recoveryException)
            {
                record.State = "RecoveryRequired";
                record.Error = $"Save: {ex.Message}; recovery: {recoveryException.Message}";
                SaveRecord(record);
                _logger.LogError(recoveryException, "JellyP retained backup {RecordId} for recovery", record.Id);
            }

            throw new IOException($"Artwork transaction {record.Id} failed; check its recovery status.", ex);
        }
        finally { File.Delete(Staging(record.Id)); }
        if (!config.PreserveOriginals)
        {
            try
            {
                await RetireRecordAsync(record).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The image commit is already verified. Retaining the original is the
                // safe fallback; report cleanup separately instead of failing the commit.
                record.CleanupWarning = "Committed, but original cleanup did not complete. The stored original was retained.";
                try { SaveRecord(record); }
                catch (Exception persistence)
                {
                    _logger.LogError(persistence, "JellyP could not persist cleanup warning for {RecordId}", record.Id);
                }
                _logger.LogWarning(ex, "JellyP committed {RecordId} but retained its original after cleanup failure", record.Id);
            }
        }

        return record;
    }

    private async Task RestoreCoreAsync(MigrationRecord record, PluginConfiguration config,
        bool? slotSafe = null, IReadOnlyDictionary<string, int>? referenceCounts = null)
    {
        _artwork.EnsureIdle(record.Source.ItemId);
        var original = DurableFiles.ReadImage(Backup(record));
        if (DurableFiles.Hash(original) != record.SourceHash)
        {
            throw new IOException("Original backup checksum failed; no artwork was changed.");
        }

        var current = _artwork.GetCurrent(record.Source);
        if (current is null)
        {
            throw new IOException("Image slot was removed. Recovery requires administrator review.");
        }

        var exists = File.Exists(current.Path);
        var hash = exists ? DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) : null;
        if (!(hash == record.OutputHash || hash == record.SourceHash
            || (!exists && (current.Path == record.Source.Path || current.Path == record.OutputPath))))
        {
            throw new IOException("Image slot changed since conversion; refusing to overwrite it.");
        }

        // The current reference itself is the first count; more than one means another
        // item/slot also persists this exact physical file. Never overwrite that shared
        // path, even when the other slot is currently missing or excluded by UI policy.
        if (slotSafe == false || !_artwork.IsSlotSafeForMutation(current))
            throw new IOException("A sibling image slot is unavailable; refusing to refresh metadata while restoring.");
        var referenceCount = referenceCounts is null ? _artwork.CountPathReferences(current.Path)
            : referenceCounts.GetValueOrDefault(Path.GetFullPath(current.Path));
        if (referenceCount > 1)
            throw new IOException("Image slot is shared with another item; refusing to overwrite it.");

        var destination = _artwork.GetDestination(current, record.OriginalMimeType);
        if (File.Exists(destination) && DurableFiles.Hash(DurableFiles.ReadImage(destination)) != record.SourceHash)
        {
            throw new IOException("Original destination now contains different artwork; refusing to overwrite it.");
        }

        DurableFiles.EnsureSpace(destination, checked(config.MinimumFreeSpaceBytes + 2L * original.Length));
        DurableFiles.EnsureSpace(_root, checked(config.MinimumFreeSpaceBytes + 2L * original.Length));
        var staging = Staging(record.Id);
        DurableFiles.Write(staging, original);
        record.State = "Restoring";
        SaveRecord(record);
        try
        {
            var restored = await _artwork.SaveAsync(current, staging, record.OriginalMimeType).ConfigureAwait(false);
            if (DurableFiles.Hash(DurableFiles.ReadImage(restored.Path)) != record.SourceHash
                || _artwork.GetCurrent(record.Source)?.Path != restored.Path)
            {
                throw new IOException("Original image reference could not be verified after restore.");
            }

            DurableFiles.FlushParentDirectory(restored.Path);
            DurableFiles.FlushParentDirectory(current.Path);

            // A failed save can publish bytes without changing/persisting the slot. Reconcile only our verified orphan.
            if (record.OutputPath is not null && record.OutputPath != restored.Path && File.Exists(record.OutputPath))
            {
                var outputStillReferenced = referenceCounts is null
                    ? _artwork.CountPathReferences(record.OutputPath) > 0 : false;
                if (DurableFiles.Hash(DurableFiles.ReadImage(record.OutputPath)) != record.OutputHash
                    || outputStillReferenced)
                {
                    throw new IOException("Uncertain output remains after restore; retaining the original for administrator recovery.");
                }

                DurableFiles.Delete(record.OutputPath);
            }

            record.State = "RolledBack";
            record.Error = null;
            record.CompletedUtc = DateTime.UtcNow;
            SaveRecord(record);
            // Safe after successful restore; repeated deletion following a crash is harmless.
            DurableFiles.Delete(Backup(record));
            record.BackupDeleted = true;
            SaveRecord(record);
        }
        catch (Exception ex)
        {
            record.State = "RecoveryRequired";
            record.Error = ex.Message;
            SaveRecord(record);
            throw;
        }
        finally { File.Delete(staging); }
    }

    private async Task PurgeCoreAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        _artwork.EnsureIdle();
        var records = Records().Where(r => r.State == "Committed" && !r.BackupDeleted && r.CompletedUtc < cutoffUtc).ToArray();
        var referenceCounts = _artwork.CountPathReferences(records
            .Where(record => record.OutputPath is not null && File.Exists(record.OutputPath))
            .Select(record => record.OutputPath!).ToArray());
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _artwork.EnsureIdle(record.Source.ItemId);
            var current = _artwork.GetCurrent(record.Source);
            if (current is null || current.Path != record.OutputPath || !File.Exists(current.Path)
                || DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) != record.OutputHash)
            {
                continue; // Retain uncertain backups rather than retiring the only valid copy.
            }

            if (referenceCounts.GetValueOrDefault(Path.GetFullPath(current.Path)) > 1) continue;
            if (File.Exists(record.Source.Path)) continue;
            await RetireRecordAsync(record, referenceCounts).ConfigureAwait(false);
        }

    }

    private Task RetireRecordAsync(MigrationRecord record, IReadOnlyDictionary<string, int>? referenceCounts = null,
        bool? slotSafe = null)
    {
        _artwork.EnsureIdle(record.Source.ItemId);
        var current = _artwork.GetCurrent(record.Source);
        if (record.State != "Committed" || current?.Path != record.OutputPath || current is null
            || DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) != record.OutputHash || File.Exists(record.Source.Path))
        {
            throw new IOException("Artwork changed before backup retirement; original retained.");
        }

        if (slotSafe == false || !_artwork.IsSlotSafeForMutation(current))
            throw new IOException("A sibling image slot is unavailable; original retained.");
        var referenceCount = referenceCounts is null ? _artwork.CountPathReferences(current.Path)
            : referenceCounts.GetValueOrDefault(Path.GetFullPath(current.Path));
        if (referenceCount > 1)
            throw new IOException("Artwork is shared with another item; original retained.");

        // Persist the intent before deletion. If the process dies after Delete but
        // before the completion record, recovery can prove the bytes are gone and
        // finish the marker without ever promising a restorable backup again.
        record.BackupDeletionIntent = true;
        SaveRecord(record);
        try
        {
            DurableFiles.Delete(Backup(record));
        }
        catch
        {
            record.BackupDeletionIntent = false;
            SaveRecord(record);
            throw;
        }

        record.BackupDeleted = true;
        record.BackupDeletionIntent = false;
        SaveRecord(record);
        return Task.CompletedTask;
    }

    private long ActualBackupBytes() => Directory.Exists(Path.Combine(_root, "originals"))
        ? Directory.EnumerateFiles(Path.Combine(_root, "originals")).Sum(path => new FileInfo(path).Length) : 0;

    private void CleanupStaging()
    {
        var directory = Path.Combine(_root, "staging");
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if ((name.EndsWith(".image", StringComparison.Ordinal) || name.EndsWith(".image.tmp", StringComparison.Ordinal))
                && Guid.TryParseExact(name.Split('.')[0], "N", out _)) DurableFiles.Delete(path);
        }
    }

    private void TryRecoverBackupDeletionIntents()
    {
        try { RecoverBackupDeletionIntents(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            _logger.LogError(ex, "JellyP could not reconcile backup deletion intents at startup");
        }
    }

    private void RecoverBackupDeletionIntents()
    {
        foreach (var record in Records().Where(record => record.BackupDeletionIntent))
        {
            try
            {
                if (!File.Exists(Backup(record)))
                {
                    record.BackupDeleted = true;
                    record.BackupDeletionIntent = false;
                    record.CleanupWarning = null;
                    SaveRecord(record);
                }
                else
                {
                    // The delete never happened. Keep the original restorable and clear
                    // the stale intent; a later authorized retirement can retry safely.
                    record.BackupDeletionIntent = false;
                    SaveRecord(record);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _logger.LogError(ex, "JellyP could not reconcile backup deletion intent for {RecordId}", record.Id);
            }
        }
    }

    private void FinalizeRolledBackRecords()
    {
        foreach (var record in Records().Where(r => r.State == "RolledBack" && !r.BackupDeleted))
        {
            var current = _artwork.GetCurrent(record.Source);
            if (current is null || !File.Exists(current.Path)
                || DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) != record.SourceHash) continue;
            DurableFiles.Delete(Backup(record));
            record.BackupDeleted = true;
            SaveRecord(record);
        }
    }

    private async Task WaitForIdleAsync(Guid? itemId, RunReport report, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _artwork.EnsureIdle(itemId);
                // A cancel can arrive while EnsureIdle is blocked; never start a transaction after it.
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            catch (InvalidOperationException ex)
            {
                if (report.Phase != "WaitingForMetadata" || report.Message != ex.Message)
                {
                    report.Phase = "WaitingForMetadata";
                    report.Message = ex.Message;
                    SaveReport(report);
                }
                // Native task cancellation/maximum runtime governs the wait; a daily scan can
                // legitimately take hours and must not cause every coincident trigger to fail.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task EnterAsync(CancellationToken cancellationToken)
    {
        lock (_requestGate)
        {
            EnsureJournalWritable();
            if (_pending is not null || HasPendingOrRunningOperation() || HasQueuedOperationFiles()
                || !_gate.Wait(0, cancellationToken))
                throw new InvalidOperationException("A JellyP operation is already running or queued.");
            _running = true;
        }
        return Task.CompletedTask;
    }

    private PluginConfiguration SnapshotConfiguration()
    {
        // Settings writes replace or mutate the live configuration; a plan and every
        // transaction must observe one complete snapshot, never a half-saved object.
        lock (_requestGate)
        {
            var config = JsonSerializer.Deserialize<PluginConfiguration>(JsonSerializer.Serialize(_configuration()))!;
            config.Validate();
            return config;
        }
    }

    private static void RequireMaintenance(PluginConfiguration config)
    {
        if (!config.Enabled)
            throw new InvalidOperationException("Enable artwork changes first.");
    }

    private static string DetectOriginalMime(byte[] source) => source.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 })
        ? "image/png" : "image/jpeg";

    private static void EnsureDestinationAvailable(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Destination already exists; refusing to overwrite artwork.");
        DurableFiles.EnsureSafeParent(path);
    }

    private void EnsureJournalWritable()
    {
        if (_journalIntegrityError is not null) throw new InvalidOperationException(_journalIntegrityError);
    }

    private IEnumerable<MigrationRecord> Records()
    {
        EnsureJournalWritable();
        var directory = Path.Combine(_root, "journal");
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            MigrationRecord record;
            try
            {
                record = DurableFiles.ReadJson<MigrationRecord>(path);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                _journalIntegrityError = "JellyP journal is corrupt or unreadable. New mutations are blocked; review server logs and recovery data.";
                throw new InvalidDataException(_journalIntegrityError, ex);
            }

            yield return record;
        }
    }
    private MigrationRecord ReadRecord(Guid id) => DurableFiles.ReadJson<MigrationRecord>(Path.Combine(_root, "journal", id.ToString("N") + ".json"));
    private void SaveRecord(MigrationRecord record) => DurableFiles.WriteJson(Path.Combine(_root, "journal", record.Id.ToString("N") + ".json"), record);
    private string Backup(MigrationRecord record) => Path.Combine(_root, "originals", record.Id.ToString("N") + ".original");
    private string Staging(Guid id) => Path.Combine(_root, "staging", id.ToString("N") + ".image");
    private void SaveReport(RunReport report) => DurableFiles.WriteJson(Path.Combine(_root, "last-run.json"), report);
}
