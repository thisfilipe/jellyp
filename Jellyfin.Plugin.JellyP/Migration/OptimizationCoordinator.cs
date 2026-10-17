using System.Text.Json;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Conversion;
using Jellyfin.Plugin.JellyP.Infrastructure;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyP.Migration;

/// <summary>Serializes bounded scans, journaled image transactions and rollback operations.</summary>
public sealed class OptimizationCoordinator
{
    private readonly IArtworkStore _artwork;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly string _root;
    private readonly ILogger<OptimizationCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _running;

    /// <summary>Creates a coordinator; this constructor does not modify artwork.</summary>
    public OptimizationCoordinator(IArtworkStore artwork, Func<PluginConfiguration> configuration,
        string root, ILogger<OptimizationCoordinator> logger)
    {
        _artwork = artwork;
        _configuration = configuration;
        _root = root;
        _logger = logger;
    }

    /// <summary>Returns bounded administrator output and storage accounting.</summary>
    public OptimizerStatus GetStatus()
    {
        var records = Records().ToArray();
        var reportPath = Path.Combine(_root, "last-run.json");
        var report = File.Exists(reportPath) ? DurableFiles.ReadJson<RunReport>(reportPath) : new RunReport();
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

    /// <summary>Runs the next bounded batch, honoring cancellation between transactions.</summary>
    public async Task RunAsync(IProgress<double> progress, CancellationToken cancellationToken, bool synchronize = false)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        RunReport? report = null;
        try
        {
            var config = SnapshotConfiguration();
            if (!config.DryRun && !config.Enabled)
            {
                throw new InvalidOperationException("Enable artwork changes in JellyP settings first, or use dry run.");
            }

            if (!config.DryRun && !config.MaintenanceWindowConfirmed)
            {
                throw new InvalidOperationException("Confirm an idle maintenance window before changing artwork.");
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

            if (!config.DryRun && Records().Any(r => r.State is "Prepared" or "Restoring" or "RecoveryRequired"))
            {
                throw new InvalidOperationException("Recover interrupted image transactions before continuing.");
            }

            if (!config.DryRun)
            {
                FinalizeRolledBackRecords();
                CleanupStaging();
            }

            if (!config.DryRun && !synchronize && config.BackupRetentionDays > 0)
            {
                await PurgeCoreAsync(DateTime.UtcNow.AddDays(-config.BackupRetentionDays), cancellationToken).ConfigureAwait(false);
            }

            var cursorPath = Path.Combine(_root, (synchronize ? "sync-" : "optimize-") + (config.DryRun ? "cursor-preview.json" : "cursor-apply.json"));
            var cursor = File.Exists(cursorPath) ? DurableFiles.ReadJson<string>(cursorPath) : string.Empty;
            report = new RunReport { DryRun = config.DryRun, Running = true, StartedUtc = DateTime.UtcNow, Cursor = cursor };
            SaveReport(report);
            var eligible = _artwork.Inventory(cancellationToken, config.IncludedItemIds);
            var syncRecords = synchronize ? Records().Where(r => r.State == "Committed")
                .GroupBy(r => r.Source.Key).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CompletedUtc).First()) : [];
            IReadOnlyList<ArtworkReference> inventory = synchronize
                ? syncRecords.Values.Select(r => r.Source).OrderBy(r => r.Key, StringComparer.Ordinal).ToArray()
                : eligible.Where(r => !Path.GetExtension(r.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (config.IncludedItemIds.Length > 0)
                inventory = inventory.Where(r => config.IncludedItemIds.Contains(r.ItemId)).ToArray();
            report.InventoryImages = inventory.Count;
            var candidates = inventory.Where(r => string.CompareOrdinal(r.Key, cursor) > 0).Take(config.BatchSize).ToArray();
            var converter = new WebpImageConverter();
            foreach (var reference in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _artwork.EnsureIdle(reference.ItemId);
                try
                {
                    var policy = config.PolicyFor(reference);
                    string? earlySkip = policy is not { Enabled: true } ? "CategoryDisabled" : null;
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
                        DurableFiles.WriteJson(cursorPath, reference.Key);
                        SaveReport(report);
                        continue;
                    }

                    var bytes = DurableFiles.ReadImage(syncRecord is null ? reference.Path : Backup(syncRecord));
                    if (syncRecord is not null && DurableFiles.Hash(bytes) != syncRecord.SourceHash)
                        throw new IOException("Preserved original checksum failed; no image was changed.");
                    var result = converter.Optimize(bytes, config.OptionsFor(policy!));
                    if (!result.Success)
                    {
                        report.Skipped++;
                        var reason = result.SkipReason.ToString();
                        report.SkipReasons[reason] = report.SkipReasons.GetValueOrDefault(reason) + 1;
                    }
                    else
                    {
                        if (!config.DryRun)
                        {
                            var target = reference;
                            if (syncRecord is not null)
                            {
                                // Reuse verified rollback, then normal conversion. A crash between phases leaves a valid original.
                                await RestoreCoreAsync(syncRecord, config).ConfigureAwait(false);
                                target = _artwork.GetCurrent(reference) ?? throw new IOException("Restored image slot is missing.");
                            }

                            await ConvertAsync(target, bytes, result.OutputBytes!, config, config.Fingerprint(policy!)).ConfigureAwait(false);
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
                DurableFiles.WriteJson(cursorPath, reference.Key);
                SaveReport(report);
                progress.Report(100d * report.Examined / Math.Max(1, candidates.Length));
            }

            if (candidates.Length < config.BatchSize || candidates.LastOrDefault()?.Key == inventory.LastOrDefault()?.Key)
            {
                report.Cursor = string.Empty;
                DurableFiles.WriteJson(cursorPath, string.Empty);
                report.Message = "Inventory pass complete. The next run starts a new pass, including newly downloaded artwork.";
            }
            else
            {
                report.Message = "Batch complete. Run again to resume at the next image.";
            }

            progress.Report(100);
        }
        catch (OperationCanceledException)
        {
            if (report is not null) report.Message = "Cancelled between image transactions; resume with the next run.";
            throw;
        }
        catch (Exception ex)
        {
            report ??= new RunReport { StartedUtc = DateTime.UtcNow, DryRun = true };
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

    /// <summary>Restores a committed or interrupted transaction through Jellyfin services.</summary>
    public async Task RollbackAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = SnapshotConfiguration();
            RequireMaintenance(config);
            FinalizeRolledBackRecords();
            var record = ReadRecord(id);
            if (record.BackupDeleted)
            {
                throw new InvalidOperationException("This original is no longer available for rollback.");
            }

            await RestoreCoreAsync(record, config).ConfigureAwait(false);
        }
        finally { _running = false; _gate.Release(); }
    }

    /// <summary>Retires only verified committed backups older than the specified cutoff.</summary>
    public async Task PurgeAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireMaintenance(SnapshotConfiguration());
            FinalizeRolledBackRecords();
            await PurgeCoreAsync(cutoffUtc, cancellationToken).ConfigureAwait(false);
        }
        finally { _running = false; _gate.Release(); }
    }

    private async Task ConvertAsync(ArtworkReference reference, byte[] source, byte[] output, PluginConfiguration config, string policyFingerprint)
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
            await RetireRecordAsync(record).ConfigureAwait(false);
        }
    }

    private async Task RestoreCoreAsync(MigrationRecord record, PluginConfiguration config)
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
                if (DurableFiles.Hash(DurableFiles.ReadImage(record.OutputPath)) != record.OutputHash
                    || _artwork.IsPathReferenced(record.OutputPath))
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
        foreach (var record in Records().Where(r => r.State == "Committed" && !r.BackupDeleted && r.CompletedUtc < cutoffUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _artwork.EnsureIdle(record.Source.ItemId);
            var current = _artwork.GetCurrent(record.Source);
            if (current is null || current.Path != record.OutputPath || !File.Exists(current.Path)
                || DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) != record.OutputHash)
            {
                continue; // Retain uncertain backups rather than retiring the only valid copy.
            }

            if (File.Exists(record.Source.Path)) continue;
            await RetireRecordAsync(record).ConfigureAwait(false);
        }

    }

    private Task RetireRecordAsync(MigrationRecord record)
    {
        _artwork.EnsureIdle(record.Source.ItemId);
        var current = _artwork.GetCurrent(record.Source);
        if (record.State != "Committed" || current?.Path != record.OutputPath || current is null
            || DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) != record.OutputHash || File.Exists(record.Source.Path))
        {
            throw new IOException("Artwork changed before backup retirement; original retained.");
        }
        DurableFiles.Delete(Backup(record));
        record.BackupDeleted = true;
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

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A JellyP operation is already running.");
        }

        _running = true;
    }

    private PluginConfiguration SnapshotConfiguration()
    {
        var config = JsonSerializer.Deserialize<PluginConfiguration>(JsonSerializer.Serialize(_configuration()))!;
        config.Validate();
        return config;
    }

    private static void RequireMaintenance(PluginConfiguration config)
    {
        if (!config.Enabled || !config.MaintenanceWindowConfirmed)
            throw new InvalidOperationException("Enable artwork changes and confirm an idle maintenance window first.");
    }

    private static string DetectOriginalMime(byte[] source) => source.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 })
        ? "image/png" : "image/jpeg";

    private static void EnsureDestinationAvailable(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Destination already exists; refusing to overwrite artwork.");
        DurableFiles.EnsureSafeParent(path);
    }

    private IEnumerable<MigrationRecord> Records() => Directory.Exists(Path.Combine(_root, "journal"))
        ? Directory.EnumerateFiles(Path.Combine(_root, "journal"), "*.json").Select(DurableFiles.ReadJson<MigrationRecord>)
        : [];
    private MigrationRecord ReadRecord(Guid id) => DurableFiles.ReadJson<MigrationRecord>(Path.Combine(_root, "journal", id.ToString("N") + ".json"));
    private void SaveRecord(MigrationRecord record) => DurableFiles.WriteJson(Path.Combine(_root, "journal", record.Id.ToString("N") + ".json"), record);
    private string Backup(MigrationRecord record) => Path.Combine(_root, "originals", record.Id.ToString("N") + ".original");
    private string Staging(Guid id) => Path.Combine(_root, "staging", id.ToString("N") + ".image");
    private void SaveReport(RunReport report) => DurableFiles.WriteJson(Path.Combine(_root, "last-run.json"), report);
}
