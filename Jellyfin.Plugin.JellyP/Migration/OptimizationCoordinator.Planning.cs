using System.Text.Json;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Conversion;
using Jellyfin.Plugin.JellyP.Infrastructure;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyP.Migration;

public sealed partial class OptimizationCoordinator
{
    private string PlansDirectory => Path.Combine(_root, "plans");
    private string OperationsDirectory => Path.Combine(_root, "operations");

    /// <summary>Returns the current settings split into automation, manual defaults and global limits.</summary>
    public SettingsView GetSettings() => ToSettingsView(SnapshotConfiguration());

    /// <summary>Persists only manual defaults and the explicitly global limits.</summary>
    public SettingsView SaveDefaults(SettingsDefaultsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Globals is null)
            throw new JellyPRequestException(400, "InvalidGlobals", "Global limits are required.");
        lock (_requestGate)
        {
            var current = SnapshotConfiguration();
            current.ManualDefaults = request.Defaults is null ? null : new ManualDefaults
            {
                PreserveOriginals = request.Defaults.PreserveOriginals,
                Quality = request.Defaults.Quality,
                Lossless = request.Defaults.Lossless,
                ResizeImages = request.Defaults.ResizeImages,
                MaximumDimension = request.Defaults.MaximumDimension,
                MinimumSsim = request.Defaults.MinimumSsim,
                BatchSize = request.Defaults.BatchSize,
                Policies = ClonePolicies(request.Defaults.Policies),
            };
            current.MinimumFreeSpaceBytes = request.Globals.MinimumFreeSpaceBytes;
            current.MaximumBackupBytes = request.Globals.MaximumBackupBytes;
            current.BackupRetentionDays = request.Globals.BackupRetentionDays;
            current.MinimumSavingsBytes = request.Globals.MinimumSavingsBytes;
            current.MinimumSavingsRatio = request.Globals.MinimumSavingsRatio;
            current.Validate();
            PersistConfiguration(current);
            return ToSettingsView(current);
        }
    }

    /// <summary>Persists only legacy automation fields. Defaults and global limits are not changed.</summary>
    public SettingsView SaveAutomation(SettingsAutomationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var automation = request.Automation ?? throw new JellyPRequestException(400, "InvalidAutomation", "Automation settings are required.");
        if (automation.Enabled && !automation.DryRun && !automation.PreserveOriginals && !request.AuthorizePermanentRemoval)
            throw new JellyPRequestException(409, "PermanentRemovalAuthorizationRequired",
                "Automation that applies and discards originals requires explicit permanent-removal authorization.");
        lock (_requestGate)
        {
            var current = SnapshotConfiguration();
            current.Enabled = automation.Enabled;
            current.DryRun = automation.DryRun;
            current.PreserveOriginals = automation.PreserveOriginals;
            current.Quality = automation.Quality;
            current.Lossless = automation.Lossless;
            current.ResizeImages = automation.ResizeImages;
            current.MaximumDimension = automation.MaximumDimension;
            current.MinimumSsim = automation.MinimumSsim;
            current.BatchSize = automation.BatchSize;
            current.ScopeMode = automation.ScopeMode;
            current.IncludedItemIds = automation.ItemIds?.ToArray() ?? [];
            current.IncludedLibraryIds = automation.LibraryIds?.ToArray() ?? [];
            current.ImagePolicies = ClonePolicies(automation.Policies);
            current.SyncUnchangedPolicies = automation.SyncUnchangedPolicies;
            current.Validate();
            PersistConfiguration(current);
            return ToSettingsView(current);
        }
    }

    /// <summary>Creates one immutable bounded plan. This never mutates artwork, cursors, last-run state or configuration.</summary>
    public PlanSummary AnalyzePlan(PlanAnalyzeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = NormalizeScope(request);
        var config = SnapshotConfiguration();
        // Fingerprint the saved effective defaults before request-only draft overrides
        // are applied. Apply/Review later compare the live saved defaults to this value.
        var savedDefaultsFingerprint = ManualDefaultsFingerprint(config);
        var (defaultsSource, legacyWarning) = ApplyManualProfile(config, request);
        var plan = new PlanRecord
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.Add(PlanLifetime),
            Scope = scope,
            ItemIds = request.ItemIds?.Distinct().Order().ToArray() ?? [],
            LibraryIds = request.LibraryIds?.Distinct().Order().ToArray() ?? [],
            BatchSize = config.BatchSize,
            RequestedBatchSize = request.BatchSize ?? config.BatchSize,
            PreserveOriginals = config.PreserveOriginals,
            GeneralPolicy = ToGeneralPolicy(config),
            Policies = ClonePolicies(config.ImagePolicies),
            MinimumSavingsRatio = config.MinimumSavingsRatio,
            MinimumSavingsBytes = config.MinimumSavingsBytes,
            ManualDefaultsFingerprint = savedDefaultsFingerprint,
            DefaultsSource = defaultsSource,
            LegacyDefaultsWarning = legacyWarning,
        };

        var includedItems = scope == "items" ? plan.ItemIds : null;
        var includedLibraries = scope == "libraries" ? plan.LibraryIds : null;
        IReadOnlyList<ArtworkInventoryEntry> inventory;
        try
        {
            inventory = _artwork.DetailedInventory(cancellationToken, includedItems, includedLibraries, maximumEntries: MaximumPlanImages);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "JellyP inventory failed before plan creation");
            throw new JellyPRequestException(409, "InventoryUnavailable",
                "JellyP could not read the inventory safely. Review server logs.");
        }

        if (inventory.Count > MaximumPlanImages)
            throw new JellyPRequestException(409, "PlanScopeTooLarge",
                $"The selection contains {inventory.Count} image slots; the plan limit is {MaximumPlanImages}. Narrow the scope before analyzing.");

        var managed = LatestCommittedRecords();
        var converter = new WebpImageConverter();
        var evaluated = 0;
        var notEvaluated = 0;
        var images = new List<PlanImage>(inventory.Count);
        foreach (var entry in inventory.OrderBy(item => item.Reference.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = ToPlanImage(entry);
            images.Add(image);
            switch (entry.State)
            {
                case "Remote":
                    image.Reason = "RemoteReference";
                    continue;
                case "Missing":
                    image.Reason = entry.Reason ?? "MissingFile";
                    continue;
                case "Excluded":
                    image.Reason = entry.Reason ?? "Excluded";
                    continue;
                case "Shared":
                    image.Reason = "SharedReference";
                    continue;
            }

            var reference = entry.Reference;
            var policy = config.PolicyFor(reference);
            if (policy is not { Enabled: true })
            {
                image.Reason = "CategoryDisabled";
                continue;
            }

            var fingerprint = config.Fingerprint(policy);
            var isManaged = entry.State == "AlreadyWebp" && managed.TryGetValue(reference.Key, out var record)
                && record.OutputPath is not null
                && string.Equals(Path.GetFullPath(record.OutputPath), Path.GetFullPath(reference.Path), PathComparison);
            if (entry.State == "AlreadyWebp" && !isManaged)
            {
                image.Reason = "ExternalWebpNotManaged";
                continue;
            }

            if (evaluated >= config.BatchSize)
            {
                image.Reason = "NotEvaluatedBatchLimit";
                notEvaluated++;
                continue;
            }

            evaluated++;
            if (isManaged)
            {
                record = managed[reference.Key];
                image.ManagedRecordId = record.Id;
                image.CurrentHash = record.OutputHash;
                image.CurrentBytes = entry.CurrentBytes;
                image.OriginalBytes = record.OriginalBytes;
                image.SourceMimeType = record.OriginalMimeType;
                if (record.PolicyFingerprint == fingerprint)
                {
                    image.Reason = "AlreadyUsesPolicy";
                    continue;
                }

                EvaluateRegeneration(image, record, config, policy, fingerprint, converter);
            }
            else
            {
                EvaluateConversion(image, entry, config, policy, fingerprint, converter);
            }
        }

        plan.Images = images;
        plan.TotalImages = images.Count;
        plan.Evaluated = evaluated;
        plan.NotEvaluated = notEvaluated;
        plan.Proposed = images.Count(image => image.Action is "Convert" or "Regenerate");
        plan.EstimatedSavingsBytes = images.Where(image => image.Action is "Convert" or "Regenerate")
            .Sum(image => image.SavingsBytes ?? 0);
        if (legacyWarning)
            plan.Warnings.Add(new PlanWarning
            {
                Code = "LegacyDefaultsProjected",
                Message = "Manual defaults were projected from legacy automation fields because no explicit manual defaults are saved. Read-only analysis does not write them.",
            });
        if (notEvaluated > 0)
            plan.Warnings.Add(new PlanWarning
            {
                Code = "PlanBatchLimit",
                Message = $"{notEvaluated} eligible image slots were not evaluated because the manual batch limit is {config.BatchSize}.",
            });

        SavePlan(plan);
        TryPrunePlans(plan.Id);
        return ToSummary(plan);
    }

    /// <summary>Returns one plan summary without filesystem paths.</summary>
    public PlanSummary GetPlan(Guid planId)
    {
        var plan = ReadPlan(planId);
        TryPrunePlans(planId);
        return ToSummary(plan);
    }

    /// <summary>Returns a stable page of plan image evidence.</summary>
    public PlanImagesPage GetPlanImages(Guid planId, int offset, int limit)
    {
        ValidatePage(offset, limit, 200);
        var plan = ReadPlan(planId);
        TryPrunePlans(planId);
        var images = plan.Images.Skip(offset).Take(limit).Select(ToImageView).ToArray();
        return new PlanImagesPage
        {
            PlanId = plan.Id,
            Offset = offset,
            Limit = limit,
            Total = plan.Images.Count,
            Items = images,
        };
    }

    /// <summary>Returns all keys of one explicit kind; plans are already hard-capped, so this never silently truncates.</summary>
    public PlanKeysResponse GetPlanKeys(Guid planId, string? kind)
    {
        var normalized = string.IsNullOrWhiteSpace(kind) ? "selectable" : kind.Trim().ToLowerInvariant();
        if (normalized is not ("selectable" or "all"))
            throw new JellyPRequestException(400, "InvalidKeyKind", "Key kind must be selectable or all.");
        var plan = ReadPlan(planId);
        TryPrunePlans(planId);
        var selectable = normalized == "selectable" && plan.Status == "Ready" && DateTime.UtcNow < plan.ExpiresUtc;
        var keys = plan.Images
            .Where(image => normalized == "all" || selectable && image.Action is "Convert" or "Regenerate")
            .Select(image => image.Key)
            .ToArray();
        return new PlanKeysResponse { PlanId = plan.Id, Kind = normalized, Total = keys.Length, Keys = keys };
    }

    /// <summary>Computes exact billing for an explicit server-owned selection without writing files.</summary>
    public PlanReviewResponse ReviewPlan(Guid planId, PlanReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePage(request.Offset, request.Limit, 200);
        ValidatePage(request.RetentionOffset, request.RetentionLimit, 200);
        var plan = ReadPlan(planId);
        TryPrunePlans(planId);
        EnsurePlanReady(plan);
        EnsurePlanDefaultsUnchanged(plan);
        var images = ResolveSelectedImages(plan, request.Keys);
        var config = RuntimeConfiguration(plan);
        var protectedIds = images.Where(image => image.Action == "Regenerate" && image.ManagedRecordId.HasValue)
            .Select(image => image.ManagedRecordId!.Value).ToHashSet();
        return new PlanReviewResponse
        {
            PlanId = plan.Id,
            TotalSelected = images.Count,
            Offset = request.Offset,
            Limit = request.Limit,
            Billing = BuildBilling(images, plan.PreserveOriginals),
            Retention = BuildRetentionPreview(config, protectedIds, request.RetentionOffset, request.RetentionLimit),
            Items = images.Skip(request.Offset).Take(request.Limit).Select(ToImageView).ToArray(),
            Warnings = ReviewWarnings(plan, images),
        };
    }

    /// <summary>Returns an idempotent receipt when this exact plan and key set were already applied.</summary>
    public OperationReceipt? ExistingApplyReceipt(Guid planId, IReadOnlyCollection<string> keys)
    {
        var plan = ReadPlan(planId);
        if (!plan.OperationId.HasValue) return null;
        var operation = ReadOperation(plan.OperationId.Value);
        var stored = operation.Items.Select(item => item.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var requested = NormalizeKeys(keys).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (!stored.SequenceEqual(requested, StringComparer.Ordinal))
            throw new JellyPRequestException(409, "PlanAlreadyApplied",
                "This plan was already reserved for a different exact selection. Analyze the edited selection again.");
        return ToReceipt(operation);
    }

    /// <summary>Persists an exact application intent and reserves the native task. No artwork is changed here.</summary>
    public OperationReceipt ApplyPlan(Guid planId, PlanApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var keys = NormalizeKeys(request.Keys);
        // Reservation is one critical section. Reading/validating the plan outside
        // would let two concurrent requests overwrite OperationId after a long scan.
        lock (_requestGate)
        {
            EnsureJournalWritable();
            var plan = ReadPlan(planId);
            if (plan.OperationId.HasValue)
            {
                var existing = ReadOperation(plan.OperationId.Value);
                var stored = existing.Items.Select(item => item.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                var requested = keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
                if (!stored.SequenceEqual(requested, StringComparer.Ordinal))
                    throw new JellyPRequestException(409, "PlanAlreadyApplied",
                        "This plan was already reserved for a different exact selection. Analyze the edited selection again.");
                return ToReceipt(existing);
            }

            EnsurePlanReady(plan);
            EnsurePlanDefaultsUnchanged(plan);
            var images = ResolveSelectedImages(plan, keys);
            var config = RuntimeConfiguration(plan);
            if (!plan.PreserveOriginals && !request.AuthorizePermanentRemoval)
                throw new JellyPRequestException(409, "PermanentRemovalAuthorizationRequired",
                    "This plan discards originals after verified commits. Explicit permanent-removal authorization is required.");
            var protectedIds = images.Where(image => image.Action == "Regenerate" && image.ManagedRecordId.HasValue)
                .Select(image => image.ManagedRecordId!.Value).ToHashSet();
            var retention = BuildRetentionSnapshot(config, protectedIds, LoadJournalSnapshot());
            if (retention.Eligible.Count > 0 && !request.AuthorizeRetentionPurge)
                throw new JellyPRequestException(409, "RetentionPurgeAuthorizationRequired",
                    $"Applying now would retire {retention.Eligible.Count} other originals under the current retention setting. Authorize that separate expiry explicitly.");
            if (retention.Eligible.Count > 0
                && !string.Equals(request.AuthorizeRetentionFingerprint, retention.Fingerprint, StringComparison.Ordinal))
                throw new JellyPRequestException(409, "RetentionReviewChanged",
                    "The expiry identity set changed or was not reviewed. Page through all retention identities and authorize the current fingerprint.");
            if (retention.Eligible.Count + retention.ProtectedCount > MaximumOperationBatch * 10)
                throw new JellyPRequestException(409, "RetentionReviewTooLarge",
                    "Too many originals expire during this application. Review retention before applying.");
            if (_running || _pending is not null || HasPendingOrRunningOperation() || HasQueuedOperationFiles())
                throw new JellyPRequestException(409, "OperationBusy", "Another JellyP operation is running or queued.");

            var operation = new OperationRecord
            {
                Id = Guid.NewGuid(),
                PlanId = plan.Id,
                Kind = "Apply",
                State = "Queued",
                Phase = "Queued",
                CreatedUtc = DateTime.UtcNow,
                PreserveOriginals = plan.PreserveOriginals,
                AuthorizePermanentRemoval = request.AuthorizePermanentRemoval,
                AuthorizeRetentionPurge = request.AuthorizeRetentionPurge,
                AuthorizedRetentionRecordIds = retention.Eligible.Select(record => record.Id).ToArray(),
                RetentionFingerprint = retention.Fingerprint,
                RetentionSafetyFingerprint = retention.SafetyFingerprint,
                PlannedBilling = BuildBilling(images, plan.PreserveOriginals),
                Items = images.Select(image => new OperationItem
                {
                    Key = image.Key,
                    RecordId = image.ManagedRecordId,
                    ItemId = image.ItemId,
                    Category = image.Category,
                    Type = image.Type,
                    Index = image.Index,
                    Name = image.Name,
                    Action = image.Action,
                    Path = image.Path,
                    OriginalMimeType = image.SourceMimeType,
                    OriginalBytes = image.OriginalBytes,
                    CurrentBytes = image.CurrentBytes,
                    OutputBytes = image.OutputBytes,
                    OriginalWidth = image.SourceWidth,
                    OriginalHeight = image.SourceHeight,
                    OutputWidth = image.OutputWidth,
                    OutputHeight = image.OutputHeight,
                    CurrentHash = image.CurrentHash,
                    SourceHash = image.SourceHash,
                    OutputHash = image.OutputHash,
                    PolicyFingerprint = image.PolicyFingerprint,
                }).ToList(),
            };

            DurableFiles.WriteJson(OperationPath(operation.Id), operation);
            plan.OperationId = operation.Id;
            plan.Status = "Reserved";
            try
            {
                SavePlan(plan);
            }
            catch
            {
                DurableFiles.Delete(OperationPath(operation.Id));
                throw;
            }

            _pendingOperationId = operation.Id;
            TryPruneOperations();
            return ToReceipt(operation);
        }
    }

    /// <summary>Rolls back a queued reservation when native dispatch failed before any mutation.</summary>
    public void RejectQueuedOperation(Guid operationId)
    {
        lock (_requestGate)
        {
            if (_currentOperationId == operationId) return;
            var path = OperationPath(operationId);
            if (!File.Exists(path)) return;
            var operation = ReadOperation(operationId);
            if (operation.State != "Queued" || operation.Items.Any(item => item.Status != "Pending")
                || Records().Any(record => record.ExecutionId == operationId))
                return; // Never delete evidence once a transaction could have started.

            var planReleased = true;
            try
            {
                if (operation.PlanId.HasValue && File.Exists(PlanPath(operation.PlanId.Value)))
                {
                    var plan = ReadPlan(operation.PlanId.Value);
                    if (plan.OperationId == operationId)
                    {
                        plan.OperationId = null;
                        plan.Status = "Ready";
                        SavePlan(plan);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
            {
                planReleased = false;
                _logger.LogError(ex, "JellyP could not release plan {PlanId} after dispatch failure", operation.PlanId);
            }

            if (!planReleased)
            {
                // Do not delete the only durable link while the plan still points to
                // it. Keep a terminal, retry-inspectable operation and the pending
                // marker so a later native invocation cannot fall back to legacy work.
                operation.State = "Failed";
                operation.Phase = "DispatchFailed";
                operation.Message = "Native dispatch failed and the plan reservation could not be released. Inspect server logs and recovery state.";
                operation.CompletedUtc = DateTime.UtcNow;
                try { SaveOperation(operation); }
                catch (Exception ex) { _logger.LogError(ex, "JellyP could not persist dispatch-failed operation {OperationId}", operation.Id); }
                return;
            }

            if (_pendingOperationId == operationId) _pendingOperationId = null;
            DurableFiles.Delete(path);
        }
    }

    /// <summary>Requests safe cancellation. A running image transaction reaches a safe boundary first.</summary>
    public bool RequestCancelOperation(Guid? operationId = null)
    {
        OperationRecord? operation;
        lock (_requestGate)
        {
            var id = operationId ?? _currentOperationId ?? _pendingOperationId;
            if (!id.HasValue) return false;
            if (!File.Exists(OperationPath(id.Value))) return false;
            operation = ReadOperation(id.Value);
            if (operation.State is "Completed" or "CompletedWithErrors" or "Cancelled" or "Interrupted" or "Failed") return false;
            try
            {
                operation.CancelRequested = true;
                if (operation.State == "Queued")
                {
                    operation.State = "Cancelled";
                    operation.Phase = "Cancelled";
                    operation.Message = "Cancelled before the native operation started.";
                    operation.CompletedUtc = DateTime.UtcNow;
                    if (operation.PlanId.HasValue && File.Exists(PlanPath(operation.PlanId.Value)))
                    {
                        var plan = ReadPlan(operation.PlanId.Value);
                        if (plan.OperationId == operation.Id)
                        {
                            plan.OperationId = null;
                            plan.Status = "Ready";
                            SavePlan(plan);
                        }
                    }
                }

                SaveOperation(operation);
            }
            finally
            {
                if (_currentOperationId == operation.Id)
                {
                    try { _operationCancellation?.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        return true;
    }

    private static string NormalizeScope(PlanAnalyzeRequest request)
    {
        var scope = request.Scope?.Trim().ToLowerInvariant();
        if (scope is not ("items" or "libraries" or "all"))
            throw new JellyPRequestException(400, "InvalidScope", "Scope must be items, libraries or all.");
        var items = request.ItemIds ?? [];
        var libraries = request.LibraryIds ?? [];
        if (items.Any(id => id == Guid.Empty) || libraries.Any(id => id == Guid.Empty))
            throw new JellyPRequestException(400, "InvalidScopeIds", "Scope identifiers must be non-empty GUIDs.");
        switch (scope)
        {
            case "items":
                if (items.Length is < 1 or > 10_000 || libraries.Length > 0)
                    throw new JellyPRequestException(400, "InvalidScope", "Items scope requires 1 to 10000 item IDs and no library IDs. Empty never means all.");
                break;
            case "libraries":
                if (libraries.Length is < 1 or > 1000 || items.Length > 0)
                    throw new JellyPRequestException(400, "InvalidScope", "Libraries scope requires 1 to 1000 library IDs and no item IDs. Empty never means all.");
                break;
            default:
                if (items.Length > 0 || libraries.Length > 0)
                    throw new JellyPRequestException(400, "InvalidScope", "All scope must be explicit and cannot mix item or library IDs.");
                break;
        }

        return scope;
    }

    private (string Source, bool LegacyWarning) ApplyManualProfile(PluginConfiguration config, PlanAnalyzeRequest request)
    {
        var defaults = config.ManualDefaults;
        var source = defaults is null ? "legacy" : "manual";
        var legacy = defaults is null;
        var general = request.GeneralPolicy ?? new PlanGeneralPolicy
        {
            Quality = defaults?.Quality ?? config.Quality,
            Lossless = defaults?.Lossless ?? config.Lossless,
            ResizeImages = defaults?.ResizeImages ?? config.ResizeImages,
            MaximumDimension = defaults?.MaximumDimension ?? config.MaximumDimension,
            MinimumSsim = defaults?.MinimumSsim ?? config.MinimumSsim,
        };
        var policies = request.Policies is { Length: > 0 }
            ? ClonePolicies(request.Policies)
            : ClonePolicies(defaults?.Policies ?? config.ImagePolicies);
        if (policies.Length != PluginConfiguration.Categories.Length
            || policies.Select(policy => policy.Category).Distinct(StringComparer.Ordinal).Count() != policies.Length
            || !PluginConfiguration.Categories.All(category => policies.Any(policy => policy.Category == category)))
            throw new JellyPRequestException(400, "IncompletePolicies", "Manual policies must include every writable category exactly once.");
        var batch = request.BatchSize ?? defaults?.BatchSize ?? config.BatchSize;
        var maximumBatch = Math.Min(defaults?.BatchSize ?? config.BatchSize, MaximumPlanBatch);
        if (batch is < 1 || batch > maximumBatch)
            throw new JellyPRequestException(400, "InvalidBatchSize",
                $"Batch size must be between 1 and {maximumBatch}; the plan hard limit is {MaximumPlanBatch}.");

        config.Quality = general.Quality;
        config.Lossless = general.Lossless;
        config.ResizeImages = general.ResizeImages;
        config.MaximumDimension = general.MaximumDimension;
        config.MinimumSsim = general.MinimumSsim;
        config.ImagePolicies = policies;
        config.BatchSize = batch;
        config.PreserveOriginals = request.PreserveOriginals ?? defaults?.PreserveOriginals ?? config.PreserveOriginals;
        config.Validate();
        return (source, legacy);
    }

    private PluginConfiguration RuntimeConfiguration(PlanRecord plan)
    {
        var config = SnapshotConfiguration();
        config.Quality = plan.GeneralPolicy.Quality;
        config.Lossless = plan.GeneralPolicy.Lossless;
        config.ResizeImages = plan.GeneralPolicy.ResizeImages;
        config.MaximumDimension = plan.GeneralPolicy.MaximumDimension;
        config.MinimumSsim = plan.GeneralPolicy.MinimumSsim;
        config.ImagePolicies = ClonePolicies(plan.Policies);
        config.BatchSize = plan.BatchSize;
        config.PreserveOriginals = plan.PreserveOriginals;
        config.MinimumSavingsRatio = plan.MinimumSavingsRatio;
        config.MinimumSavingsBytes = plan.MinimumSavingsBytes;
        config.Validate();
        return config;
    }

    private static PlanGeneralPolicy ToGeneralPolicy(PluginConfiguration config) => new()
    {
        Quality = config.Quality,
        Lossless = config.Lossless,
        ResizeImages = config.ResizeImages,
        MaximumDimension = config.MaximumDimension,
        MinimumSsim = config.MinimumSsim,
    };

    private void EvaluateConversion(PlanImage image, ArtworkInventoryEntry entry, PluginConfiguration config,
        ImageTypePolicy policy, string fingerprint, WebpImageConverter converter)
    {
        byte[] source;
        try
        {
            source = DurableFiles.ReadImage(entry.Reference.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            image.Reason = "SourceUnavailable";
            return;
        }

        image.CurrentHash = DurableFiles.Hash(source);
        image.SourceHash = image.CurrentHash;
        image.SourceMimeType = DetectMime(source, entry.MimeType);
        image.OriginalBytes = source.LongLength;
        image.CurrentBytes = source.LongLength;
        image.PolicyFingerprint = fingerprint;
        ApplyOptimization(image, converter.Optimize(source, config.OptionsFor(policy)), source.LongLength, false);
    }

    private void EvaluateRegeneration(PlanImage image, MigrationRecord record, PluginConfiguration config,
        ImageTypePolicy policy, string fingerprint, WebpImageConverter converter)
    {
        var backup = Backup(record);
        if (!File.Exists(backup))
        {
            image.Reason = "OriginalUnavailable";
            return;
        }

        byte[] source;
        try
        {
            source = DurableFiles.ReadImage(backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            image.Reason = "OriginalUnavailable";
            return;
        }

        if (DurableFiles.Hash(source) != record.SourceHash)
        {
            image.Reason = "OriginalCorrupt";
            return;
        }

        image.SourceHash = record.SourceHash;
        image.OriginalBytes = record.OriginalBytes > 0 ? record.OriginalBytes : source.LongLength;
        image.SourceMimeType = record.OriginalMimeType;
        image.PolicyFingerprint = fingerprint;
        ApplyOptimization(image, converter.Optimize(source, config.OptionsFor(policy)), image.CurrentBytes ?? source.LongLength, true);
    }

    private static void ApplyOptimization(PlanImage image, WebpOptimizationResult result, long currentBytes, bool regenerate)
    {
        image.SourceWidth = result.SourceWidth > 0 ? result.SourceWidth : null;
        image.SourceHeight = result.SourceHeight > 0 ? result.SourceHeight : null;
        image.OutputWidth = result.OutputWidth > 0 ? result.OutputWidth : null;
        image.OutputHeight = result.OutputHeight > 0 ? result.OutputHeight : null;
        image.Ssim = result.Ssim;
        if (!result.Success)
        {
            image.Reason = result.SkipReason.ToString();
            image.OutputBytes = result.CandidateBytes > 0 ? result.CandidateBytes : null;
            image.SavingsBytes = result.CandidateBytes > 0 ? currentBytes - result.CandidateBytes : null;
            return;
        }

        image.Action = regenerate ? "Regenerate" : "Convert";
        image.Reason = null;
        image.OutputBytes = result.OutputBytes!.LongLength;
        image.OutputHash = DurableFiles.Hash(result.OutputBytes);
        image.SavingsBytes = currentBytes - result.OutputBytes.LongLength;
    }

    private static string DetectMime(byte[] source, string? fallback) => source.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 })
        ? "image/png" : source.StartsWith(new byte[] { 0xff, 0xd8 }) ? "image/jpeg" : fallback ?? "image/webp";

    private static PlanImage ToPlanImage(ArtworkInventoryEntry entry) => new()
    {
        Key = entry.Reference.Key,
        ItemId = entry.Reference.ItemId,
        Name = entry.Name,
        ItemType = entry.ItemType,
        ProductionYear = entry.ProductionYear,
        Category = entry.Category,
        Type = entry.Reference.Type,
        TypeName = entry.Reference.Type.ToString(),
        Index = entry.Reference.Index,
        IsPerson = entry.Reference.IsPerson,
        State = entry.State,
        Path = entry.Reference.Path,
        SourceMimeType = entry.MimeType,
        CurrentBytes = entry.CurrentBytes,
    };

    private static IReadOnlyList<PlanImage> ResolveSelectedImages(PlanRecord plan, IReadOnlyCollection<string>? keys)
    {
        var normalized = NormalizeKeys(keys);
        var byKey = plan.Images.ToDictionary(image => image.Key, StringComparer.Ordinal);
        var selected = new List<PlanImage>(normalized.Count);
        foreach (var key in normalized)
        {
            if (!byKey.TryGetValue(key, out var image))
                throw new JellyPRequestException(400, "UnknownPlanKey", "Every selected key must belong to this plan.");
            if (image.Action is not ("Convert" or "Regenerate"))
                throw new JellyPRequestException(409, "PlanningKeyNotSelectable", "Only proposed conversion or regeneration images can be selected.");
            selected.Add(image);
        }

        return selected;
    }

    private static List<string> NormalizeKeys(IReadOnlyCollection<string>? keys)
    {
        var normalized = (keys ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()).ToArray();
        if (normalized.Length == 0)
            throw new JellyPRequestException(400, "EmptySelection", "Select at least one exact image key.");
        if (normalized.Length > MaximumPlanBatch)
            throw new JellyPRequestException(400, "SelectionTooLarge", $"A single exact selection is limited to {MaximumPlanBatch} images.");
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new JellyPRequestException(400, "DuplicateSelectionKey", "Duplicate keys are rejected without writing anything.");
        return normalized.ToList();
    }

    private static void ValidatePage(int offset, int limit, int maximumLimit)
    {
        if (offset < 0 || limit is < 1 || limit > maximumLimit)
            throw new JellyPRequestException(400, "InvalidPage", $"Offset must be non-negative and limit must be between 1 and {maximumLimit}.");
    }

    private static PlanBilling BuildBilling(IReadOnlyCollection<PlanImage> images, bool preserveOriginals)
    {
        var billing = new PlanBilling { SelectedImages = images.Count };
        foreach (var image in images)
        {
            var before = image.CurrentBytes ?? 0;
            var after = image.OutputBytes ?? 0;
            var original = image.OriginalBytes ?? 0;
            billing.ActiveBytesBefore += before;
            billing.ActiveBytesAfter += after;
            if (image.Action == "Regenerate")
            {
                billing.ExistingBackupBytes += original;
                billing.BackupBytesBefore += original;
                billing.BackupBytesAfter += preserveOriginals ? original : 0;
                if (!preserveOriginals) billing.RemovedBackupBytes += original;
            }
            else
            {
                billing.NewOriginalBytes += preserveOriginals ? original : 0;
                billing.BackupBytesAfter += preserveOriginals ? original : 0;
            }
        }

        billing.TotalBytesBefore = billing.ActiveBytesBefore + billing.BackupBytesBefore;
        billing.TotalBytesAfter = billing.ActiveBytesAfter + billing.BackupBytesAfter;
        billing.DeltaBytes = billing.TotalBytesBefore - billing.TotalBytesAfter;
        return billing;
    }

    private RetentionPreview BuildRetentionPreview(PluginConfiguration config, IReadOnlySet<Guid> protectedIds,
        int offset, int limit, RetentionSnapshot? snapshot = null)
    {
        var full = snapshot ?? BuildRetentionSnapshot(config, protectedIds, LoadJournalSnapshot());
        var items = full.Eligible.Skip(offset).Take(limit).Select(record => new RetentionCandidateView
        {
            RecordId = record.Id,
            ItemId = record.Source.ItemId,
            Category = record.Source.IsPerson ? "People" : record.Source.Type.ToString(),
            Type = record.Source.Type,
            Index = record.Source.Index,
            OriginalBytes = record.OriginalBytes,
            CompletedUtc = record.CompletedUtc ?? record.CreatedUtc,
        }).ToArray();
        return new RetentionPreview
        {
            EligibleCount = full.Eligible.Count,
            EligibleBytes = full.Eligible.Sum(record => record.OriginalBytes),
            ProtectedCount = full.ProtectedCount,
            ProtectedBytes = full.ProtectedBytes,
            Total = full.Eligible.Count,
            Offset = offset,
            Limit = limit,
            HasMore = offset + items.Length < full.Eligible.Count,
            Truncated = offset + items.Length < full.Eligible.Count,
            Fingerprint = full.Fingerprint,
            Items = items,
        };
    }

    private RetentionSnapshot BuildRetentionSnapshot(PluginConfiguration config, IReadOnlySet<Guid> protectedIds,
        JournalSnapshot journal)
    {
        var safetyFingerprint = DurableFiles.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            config.BackupRetentionDays,
            config.MinimumFreeSpaceBytes,
            config.MaximumBackupBytes,
            config.MinimumSavingsBytes,
            config.MinimumSavingsRatio,
        }));
        if (config.BackupRetentionDays <= 0)
            return new RetentionSnapshot([], 0, 0, string.Empty, safetyFingerprint);
        var cutoff = DateTime.UtcNow.AddDays(-config.BackupRetentionDays);
        var allCommitted = journal.All.Where(record => record.State == "Committed" && !record.BackupDeleted).ToArray();
        var protectedCount = 0;
        long protectedBytes = 0;
        foreach (var protectedId in protectedIds)
        {
            var record = allCommitted.FirstOrDefault(candidate => candidate.Id == protectedId);
            if (record is null) continue;
            protectedCount++;
            protectedBytes += record.OriginalBytes;
        }

        var candidates = allCommitted.Where(record => record.CompletedUtc.HasValue && record.CompletedUtc.Value < cutoff)
            .OrderBy(record => record.CompletedUtc).ThenBy(record => record.Id).ToArray();
        var shared = BackupSlotStates(candidates, CancellationToken.None);
        var eligible = new List<MigrationRecord>();
        foreach (var record in candidates)
        {
            if (protectedIds.Contains(record.Id)) continue;
            if (shared.GetValueOrDefault(record.Source.Key) is not { IsMutationSafe: true }) continue;
            if (!IsActiveRecord(record, verifyHash: true, journal) || File.Exists(record.Source.Path)) continue;
            eligible.Add(record);
        }

        var fingerprint = DurableFiles.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Safety = safetyFingerprint,
            Identities = eligible.Select(record => new
            {
                record.Id,
                record.SourceHash,
                record.OriginalBytes,
                CompletedUtc = record.CompletedUtc?.Ticks,
            }).ToArray(),
        }));
        return new RetentionSnapshot(eligible, protectedCount, protectedBytes, fingerprint, safetyFingerprint);
    }

    private sealed record RetentionSnapshot(
        IReadOnlyList<MigrationRecord> Eligible,
        int ProtectedCount,
        long ProtectedBytes,
        string Fingerprint,
        string SafetyFingerprint);

    private static List<PlanWarning> ReviewWarnings(PlanRecord plan, IReadOnlyCollection<PlanImage> images)
    {
        var warnings = plan.Warnings.ToList();
        if (images.Count > 0)
            warnings.Add(new PlanWarning
            {
                Code = "RevalidateAtApply",
                Message = "The server revalidates global references, paths and hashes again before each write.",
            });
        return warnings;
    }

    private void EnsurePlanDefaultsUnchanged(PlanRecord plan)
    {
        var current = SnapshotConfiguration();
        if (ManualDefaultsFingerprint(current) != plan.ManualDefaultsFingerprint)
            throw new JellyPRequestException(409, "ManualDefaultsChanged",
                "Manual defaults or global limits changed after this analysis. Analyze the selection again.");
    }

    private static void EnsurePlanReady(PlanRecord plan)
    {
        if (plan.Status is "Reserved" or "Consumed")
            throw new JellyPRequestException(409, "PlanAlreadyApplied", "This plan has already been used or reserved.");
        if (DateTime.UtcNow >= plan.ExpiresUtc)
            throw new JellyPRequestException(409, "PlanExpired", "This analysis expired after 24 hours. Analyze the selection again.");
    }

    private PlanRecord ReadPlan(Guid planId)
    {
        var path = PlanPath(planId);
        if (!File.Exists(path)) throw new JellyPRequestException(404, "PlanNotFound", "The JellyP plan was not found or expired.");
        var plan = DurableFiles.ReadJson<PlanRecord>(path);
        if (plan.Version != PlanSchemaVersion)
            throw new JellyPRequestException(409, "PlanSchemaChanged",
                "This plan used an older image-index schema. Analyze the selection again before applying.");
        return plan;
    }

    private void SavePlan(PlanRecord plan)
    {
        lock (_planStateGate) DurableFiles.WriteJson(PlanPath(plan.Id), plan);
    }

    private void MarkPlanConsumed(OperationRecord operation)
    {
        if (!operation.PlanId.HasValue) return;
        lock (_planStateGate)
        {
            var plan = ReadPlan(operation.PlanId.Value);
            if (plan.OperationId != operation.Id) return;
            plan.Status = "Consumed";
            SavePlan(plan);
        }
    }

    private void ReleasePlanReservationIfUnmutated(OperationRecord operation)
    {
        if (!operation.PlanId.HasValue) return;
        if (operation.MutationStarted || operation.RetentionPurgedCount > 0
            || operation.Items.Any(item => item.Status is "Changed" or "Restored" or "Retired"))
            return;
        try
        {
            if (Records().Any(record => record.ExecutionId == operation.Id)) return;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return;
        }

        lock (_planStateGate)
        {
            if (!File.Exists(PlanPath(operation.PlanId.Value))) return;
            var plan = ReadPlan(operation.PlanId.Value);
            if (plan.OperationId != operation.Id) return;
            plan.OperationId = null;
            plan.Status = "Ready";
            SavePlan(plan);
        }
    }

    private string PlanPath(Guid planId) => Path.Combine(PlansDirectory, planId.ToString("N") + ".json");

    private void TryPrunePlans(Guid? keep = null)
    {
        try { PrunePlans(keep); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JellyP plan retention cleanup was skipped");
        }
    }

    private void PrunePlans(Guid? keep = null)
    {
        // Same order as ApplyPlan: requestGate -> planStateGate. A plan that is
        // being accepted cannot be deleted between its durable reservation and the
        // protected-operation snapshot.
        lock (_requestGate)
        {
            if (!Directory.Exists(PlansDirectory)) return;
            var protectedPlans = new HashSet<Guid>();
            try
            {
                foreach (var operation in Operations().Where(operation => operation.PlanId.HasValue
                    && (operation.State is "Queued" or "Running" || operation.CreatedUtc > DateTime.UtcNow.Add(-PlanLifetime))))
                    protectedPlans.Add(operation.PlanId!.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                _logger.LogWarning(ex, "JellyP could not index operation plan references during retention");
            }

            foreach (var operationId in new[] { _pendingOperationId, _currentOperationId }.Where(id => id.HasValue).Select(id => id!.Value))
            {
                try
                {
                    var operation = ReadOperation(operationId);
                    if (operation.PlanId.HasValue) protectedPlans.Add(operation.PlanId.Value);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or JellyPRequestException)
                {
                    _logger.LogWarning(ex, "JellyP could not protect plan for active operation {OperationId}", operationId);
                }
            }

            lock (_planStateGate)
            {
                var files = Directory.EnumerateFiles(PlansDirectory, "*.json")
                    .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
                var total = 0L;
                for (var index = 0; index < files.Count; index++)
                {
                    total += files[index].Length;
                    var expired = files[index].LastWriteTimeUtc < DateTime.UtcNow.Add(-PlanLifetime).AddMinutes(-5);
                    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(files[index].Name), "N", out var planId)
                        && (protectedPlans.Contains(planId) || planId == keep)) continue;
                    if (index < MaximumPlanHistory && total <= MaximumPlanDiskBytes && !expired) continue;
                    try { DurableFiles.Delete(files[index].FullName); }
                    catch (IOException) { }
                }
            }
        }
    }

    private static PlanSummary ToSummary(PlanRecord plan) => new()
    {
        PlanId = plan.Id,
        Status = plan.Status == "Ready" && DateTime.UtcNow >= plan.ExpiresUtc ? "Expired" : plan.Status,
        CreatedUtc = plan.CreatedUtc,
        ExpiresUtc = plan.ExpiresUtc,
        Scope = plan.Scope,
        RequestedBatchSize = plan.RequestedBatchSize,
        TotalImages = plan.TotalImages,
        Evaluated = plan.Evaluated,
        NotEvaluated = plan.NotEvaluated,
        Proposed = plan.Proposed,
        Convert = plan.Images.Count(image => image.Action == "Convert"),
        Regenerate = plan.Images.Count(image => image.Action == "Regenerate"),
        Kept = plan.Images.Count(image => image.Action == "Keep"),
        EstimatedSavingsBytes = plan.EstimatedSavingsBytes,
        PreserveOriginals = plan.PreserveOriginals,
        DefaultsSource = plan.DefaultsSource,
        LegacyDefaultsWarning = plan.LegacyDefaultsWarning,
        OperationId = plan.OperationId,
        Warnings = plan.Warnings,
    };

    private static PlanImageView ToImageView(PlanImage image) => new()
    {
        Key = image.Key,
        ItemId = image.ItemId,
        Name = image.Name,
        ItemType = image.ItemType,
        ProductionYear = image.ProductionYear,
        Category = image.Category,
        Type = image.Type,
        TypeName = image.Type.ToString(),
        Index = image.Index,
        IsPerson = image.IsPerson,
        State = image.State,
        Action = image.Action,
        Reason = image.Reason,
        SourceMimeType = image.SourceMimeType,
        CurrentBytes = image.CurrentBytes,
        OriginalBytes = image.OriginalBytes,
        OutputBytes = image.OutputBytes,
        SourceWidth = image.SourceWidth,
        SourceHeight = image.SourceHeight,
        OutputWidth = image.OutputWidth,
        OutputHeight = image.OutputHeight,
        Ssim = image.Ssim,
        SavingsBytes = image.SavingsBytes,
        SourceHash = image.SourceHash,
        OutputHash = image.OutputHash,
        PolicyFingerprint = image.PolicyFingerprint,
        ManagedRecordId = image.ManagedRecordId,
    };

    private static SettingsView ToSettingsView(PluginConfiguration config)
    {
        var manual = config.ManualDefaults;
        var defaults = new ManualDefaultsSettings
        {
            PreserveOriginals = manual?.PreserveOriginals ?? config.PreserveOriginals,
            Quality = manual?.Quality ?? config.Quality,
            Lossless = manual?.Lossless ?? config.Lossless,
            ResizeImages = manual?.ResizeImages ?? config.ResizeImages,
            MaximumDimension = manual?.MaximumDimension ?? config.MaximumDimension,
            MinimumSsim = manual?.MinimumSsim ?? config.MinimumSsim,
            BatchSize = manual?.BatchSize ?? config.BatchSize,
            Policies = ClonePolicies(manual?.Policies ?? config.ImagePolicies),
        };
        return new SettingsView
        {
            Automation = new AutomationSettings
            {
                Enabled = config.Enabled,
                DryRun = config.DryRun,
                PreserveOriginals = config.PreserveOriginals,
                Quality = config.Quality,
                Lossless = config.Lossless,
                ResizeImages = config.ResizeImages,
                MaximumDimension = config.MaximumDimension,
                MinimumSsim = config.MinimumSsim,
                BatchSize = config.BatchSize,
                ScopeMode = config.ScopeMode,
                ItemIds = config.IncludedItemIds.ToArray(),
                LibraryIds = config.IncludedLibraryIds.ToArray(),
                Policies = ClonePolicies(config.ImagePolicies),
                SyncUnchangedPolicies = config.SyncUnchangedPolicies,
            },
            Defaults = defaults,
            Globals = new GlobalLimits
            {
                MinimumFreeSpaceBytes = config.MinimumFreeSpaceBytes,
                MaximumBackupBytes = config.MaximumBackupBytes,
                BackupRetentionDays = config.BackupRetentionDays,
                MinimumSavingsBytes = config.MinimumSavingsBytes,
                MinimumSavingsRatio = config.MinimumSavingsRatio,
            },
            DefaultsSource = manual is null ? "legacy" : "manual",
            LegacyDefaultsWarning = manual is null,
            Categories = PluginConfiguration.Categories.ToArray(),
        };
    }

    private static string ManualDefaultsFingerprint(PluginConfiguration config) => DurableFiles.Hash(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Defaults = config.ManualDefaults ?? new ManualDefaults
        {
            PreserveOriginals = config.PreserveOriginals,
            Quality = config.Quality,
            Lossless = config.Lossless,
            ResizeImages = config.ResizeImages,
            MaximumDimension = config.MaximumDimension,
            MinimumSsim = config.MinimumSsim,
            BatchSize = config.BatchSize,
            Policies = ClonePolicies(config.ImagePolicies),
        },
        config.MinimumFreeSpaceBytes,
        config.MaximumBackupBytes,
        config.BackupRetentionDays,
        config.MinimumSavingsBytes,
        config.MinimumSavingsRatio,
    }));

    private static ImageTypePolicy[] ClonePolicies(IEnumerable<ImageTypePolicy>? policies) => (policies ?? [])
        .Where(policy => policy is not null)
        .Select(policy => new ImageTypePolicy
        {
            Category = policy.Category,
            Enabled = policy.Enabled,
            OverrideGlobal = policy.OverrideGlobal,
            Quality = policy.Quality,
            Lossless = policy.Lossless,
            ResizeImages = policy.ResizeImages,
            MaximumDimension = policy.MaximumDimension,
            MinimumSsim = policy.MinimumSsim,
        })
        .ToArray();

    private void PersistConfiguration(PluginConfiguration configuration)
    {
        if (_saveConfiguration is not null)
        {
            _saveConfiguration(configuration);
            return;
        }

        var live = _configuration();
        CopyConfiguration(configuration, live);
    }

    private static void CopyConfiguration(PluginConfiguration source, PluginConfiguration target)
    {
        target.Enabled = source.Enabled;
        target.DryRun = source.DryRun;
        target.MaintenanceWindowConfirmed = source.MaintenanceWindowConfirmed;
        target.PreserveOriginals = source.PreserveOriginals;
        target.Quality = source.Quality;
        target.Lossless = source.Lossless;
        target.ResizeImages = source.ResizeImages;
        target.MaximumDimension = source.MaximumDimension;
        target.MinimumSavingsRatio = source.MinimumSavingsRatio;
        target.MinimumSavingsBytes = source.MinimumSavingsBytes;
        target.MinimumSsim = source.MinimumSsim;
        target.BatchSize = source.BatchSize;
        target.MinimumFreeSpaceBytes = source.MinimumFreeSpaceBytes;
        target.MaximumBackupBytes = source.MaximumBackupBytes;
        target.BackupRetentionDays = source.BackupRetentionDays;
        target.SyncUnchangedPolicies = source.SyncUnchangedPolicies;
        target.IncludedItemIds = source.IncludedItemIds.ToArray();
        target.ScopeMode = source.ScopeMode;
        target.IncludedLibraryIds = source.IncludedLibraryIds.ToArray();
        target.ImagePolicies = ClonePolicies(source.ImagePolicies);
        target.ManualDefaults = source.ManualDefaults is null ? null : new ManualDefaults
        {
            PreserveOriginals = source.ManualDefaults.PreserveOriginals,
            Quality = source.ManualDefaults.Quality,
            Lossless = source.ManualDefaults.Lossless,
            ResizeImages = source.ManualDefaults.ResizeImages,
            MaximumDimension = source.ManualDefaults.MaximumDimension,
            MinimumSsim = source.ManualDefaults.MinimumSsim,
            BatchSize = source.ManualDefaults.BatchSize,
            Policies = ClonePolicies(source.ManualDefaults.Policies),
        };
    }

    private JournalSnapshot LoadJournalSnapshot()
    {
        var all = Records().ToArray();
        var latestCommitted = all.Where(record => record.State == "Committed")
            .OrderByDescending(record => record.CompletedUtc ?? record.CreatedUtc).ThenBy(record => record.Id)
            .GroupBy(record => record.Source.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var latestOpen = all.Where(record => record.State == "Committed" && !record.BackupDeleted)
            .OrderByDescending(record => record.CompletedUtc ?? record.CreatedUtc).ThenBy(record => record.Id)
            .GroupBy(record => record.Source.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var uncertain = all.Where(record => record.State is "Prepared" or "Restoring" or "RecoveryRequired")
            .GroupBy(record => record.Source.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return new JournalSnapshot(all, latestCommitted, latestOpen, uncertain);
    }

    private Dictionary<string, MigrationRecord> LatestCommittedRecords(JournalSnapshot? snapshot = null) =>
        (snapshot ?? LoadJournalSnapshot()).LatestOpenCommitted.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private bool IsActiveRecord(MigrationRecord record, bool verifyHash, JournalSnapshot? snapshot = null)
    {
        if (record.State != "Committed" || record.BackupDeleted || record.OutputPath is null) return false;
        snapshot ??= LoadJournalSnapshot();
        if (snapshot.Uncertain.ContainsKey(record.Source.Key)) return false;
        var current = _artwork.GetCurrent(record.Source);
        if (current is null || !File.Exists(current.Path)
            || !string.Equals(current.Path, record.OutputPath, PathComparison)) return false;
        if (verifyHash)
        {
            try
            {
                if (DurableFiles.Hash(DurableFiles.ReadImage(current.Path)) != record.OutputHash) return false;
                var backup = Backup(record);
                if (!File.Exists(backup) || DurableFiles.Hash(DurableFiles.ReadImage(backup)) != record.SourceHash) return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return false;
            }
        }

        return snapshot.LatestCommitted.TryGetValue(record.Source.Key, out var latest) && latest.Id == record.Id;
    }

    private sealed record JournalSnapshot(
        IReadOnlyList<MigrationRecord> All,
        IReadOnlyDictionary<string, MigrationRecord> LatestCommitted,
        IReadOnlyDictionary<string, MigrationRecord> LatestOpenCommitted,
        IReadOnlyDictionary<string, MigrationRecord[]> Uncertain);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
