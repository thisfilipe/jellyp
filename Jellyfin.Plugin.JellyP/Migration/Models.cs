#pragma warning disable CS1591 // Public serialized administration and recovery DTOs.
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyP.Migration;

/// <summary>A stable image slot in Jellyfin, never a client-supplied file path.</summary>
public sealed record ArtworkReference(Guid ItemId, ImageType Type, int Index, string Path, bool IsPerson = false)
{
    /// <summary>Gets the stable scan ordering key.</summary>
    public string Key => $"{ItemId:N}:{(int)Type:D3}:{Index:D6}";
}

/// <summary>The only integration surface through which migration changes artwork.</summary>
public interface IArtworkStore
{
    /// <summary>Inspects exact items without downloads, saves or a whole-server inventory.</summary>
    IReadOnlyList<ItemArtworkInspection> InspectItems(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Item inspection is not available in this artwork store.");

    /// <summary>Lists image slots, excluding shared and external files.</summary>
    IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken, IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null, Action<int>? inventoryProgress = null);

    /// <summary>Returns aggregate reasons from the last inventory without private paths.</summary>
    InventorySummary? GetInventorySummary() => null;

    /// <summary>Lists scoped image slots with per-image skip evidence while still scanning global references.</summary>
    IReadOnlyList<ArtworkInventoryEntry> DetailedInventory(CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
        Action<int>? inventoryProgress = null, int? maximumEntries = null) =>
        Inventory(cancellationToken, includedItemIds, includedLibraryIds, inventoryProgress)
            .Take(maximumEntries is > 0 ? maximumEntries.Value + 1 : int.MaxValue)
            .Select(reference => new ArtworkInventoryEntry
            {
                Reference = reference,
                State = Path.GetExtension(reference.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase) ? "AlreadyWebp" : "Ready",
                Category = reference.IsPerson ? "People" : reference.Type.ToString(),
            })
            .ToArray();

    /// <summary>Lists a deterministic key-ordered page after an exclusive cursor, with at most maximumEntries + 1 rows.</summary>
    IReadOnlyList<ArtworkInventoryEntry> DetailedInventoryPage(CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
        string? afterKey = null, int? maximumEntries = null, Action<int>? inventoryProgress = null) =>
        DetailedInventory(cancellationToken, includedItemIds, includedLibraryIds, inventoryProgress, maximumEntries: null)
            .Where(entry => string.IsNullOrEmpty(afterKey)
                || string.CompareOrdinal(entry.Reference.Key, afterKey) > 0)
            .Take(maximumEntries is > 0 ? maximumEntries.Value + 1 : int.MaxValue)
            .ToArray();

    /// <summary>Checks that no scan or refresh is active.</summary>
    void EnsureIdle(Guid? itemId = null);

    /// <summary>Reads the current image slot, including its current path.</summary>
    ArtworkReference? GetCurrent(ArtworkReference reference);

    /// <summary>Predicts the pinned server's internal destination for collision checks.</summary>
    string GetDestination(ArtworkReference reference, string mimeType);

    /// <summary>Checks every library source before removing a plugin-created orphan.</summary>
    bool IsPathReferenced(string path);

    /// <summary>Counts persisted references to one exact path, including excluded and missing slots.</summary>
    int CountPathReferences(string path) => IsPathReferenced(path) ? 1 : 0;

    /// <summary>Counts several paths in one store scan when supported; the default keeps compatibility.</summary>
    IReadOnlyDictionary<string, int> CountPathReferences(IReadOnlyCollection<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return paths.Distinct(comparer).ToDictionary(path => path, CountPathReferences, comparer);
    }

    /// <summary>Checks that no sibling slot makes a save/restore unsafe. Default preserves compatibility.</summary>
    bool IsSlotSafeForMutation(ArtworkReference reference) => true;

    /// <summary>Saves staged bytes using Jellyfin and persists its reference.</summary>
    Task<ArtworkReference> SaveAsync(ArtworkReference reference, string stagingPath, string mimeType);
}

/// <summary>One scoped inventory slot with path-free client evidence. Reference.Path is internal.</summary>
public sealed class ArtworkInventoryEntry
{
    /// <summary>Gets the server-owned image reference.</summary>
    public required ArtworkReference Reference { get; init; }
    /// <summary>Gets the item display name, when available.</summary>
    public string? Name { get; init; }
    /// <summary>Gets the Jellyfin item type name.</summary>
    public string? ItemType { get; init; }
    /// <summary>Gets the production year, when available.</summary>
    public int? ProductionYear { get; init; }
    /// <summary>Gets the artwork category (People or image type).</summary>
    public string Category { get; init; } = string.Empty;
    /// <summary>Gets the inventory state.</summary>
    public string State { get; init; } = "Ready";
    /// <summary>Gets an optional non-path explanation.</summary>
    public string? Reason { get; init; }
    /// <summary>Gets the current encoded bytes from file metadata, when available.</summary>
    public long? CurrentBytes { get; init; }
    /// <summary>Gets the detected current MIME type, when available.</summary>
    public string? MimeType { get; init; }
    /// <summary>Gets whether the physical path is referenced more than once across the whole library, independent of the UI state.</summary>
    public bool IsShared { get; init; }
    /// <summary>Gets the global local-reference count for this path.</summary>
    public int ReferenceCount { get; init; } = 1;
}

/// <summary>A durable per-image recovery record.</summary>
public sealed class MigrationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required ArtworkReference Source { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string OutputHash { get; set; } = string.Empty;
    public string OriginalMimeType { get; set; } = string.Empty;
    public long OriginalBytes { get; set; }
    public long OutputBytes { get; set; }
    public string? OutputPath { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public string State { get; set; } = "Prepared";
    public string? Error { get; set; }
    public bool BackupDeleted { get; set; }
    /// <summary>Gets or sets a durable pre-delete intent for the stored original.</summary>
    public bool BackupDeletionIntent { get; set; }
    /// <summary>Gets or sets a non-fatal post-commit cleanup warning.</summary>
    public string? CleanupWarning { get; set; }
    public string PolicyFingerprint { get; set; } = string.Empty;
    public int Revision { get; set; }
    /// <summary>Gets or sets the operation that requested this transaction, for restart reconciliation. Null for legacy records.</summary>
    public Guid? ExecutionId { get; set; }
    /// <summary>Gets or sets the readable effective policy evidence. Null for legacy records.</summary>
    public string? PolicySnapshot { get; set; }
    /// <summary>Gets or sets the original encoded width. Null for legacy records.</summary>
    public int? OriginalWidth { get; set; }
    /// <summary>Gets or sets the original encoded height. Null for legacy records.</summary>
    public int? OriginalHeight { get; set; }
    /// <summary>Gets or sets the committed encoded width. Null for legacy records.</summary>
    public int? OutputWidth { get; set; }
    /// <summary>Gets or sets the committed encoded height. Null for legacy records.</summary>
    public int? OutputHeight { get; set; }
    /// <summary>Gets or sets the measured luma SSIM, when available.</summary>
    public double? Ssim { get; set; }
}

/// <summary>Persisted scan cursor and most recent run summary.</summary>
public sealed class RunReport
{
    public Guid ExecutionId { get; set; }
    public string Phase { get; set; } = "Idle";
    public int InventoryItems { get; set; }
    public InventorySummary? Inventory { get; set; }
    public int WouldConvert { get; set; }
    public List<ImageRunSample> Samples { get; set; } = [];
    public bool Running { get; set; }
    public bool DryRun { get; set; }
    public string? Cursor { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public int InventoryImages { get; set; }
    public int Examined { get; set; }
    public int Converted { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public long EstimatedSavingsBytes { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, int> SkipReasons { get; set; } = new();
}

/// <summary>A scan position bound to its selection and effective conversion policies.</summary>
internal sealed class ScanCursor
{
    public int Version { get; set; } = 1;
    public string ContextFingerprint { get; set; } = string.Empty;
    public string LastKey { get; set; } = string.Empty;
}

/// <summary>Administrator status including honest net storage accounting.</summary>
public sealed record OptimizerStatus(RunReport LastRun, long RetainedBackupBytes, long VerifiedSavingsBytes,
    long NetReclaimedBytes, IReadOnlyList<MigrationRecord> RecentRecords, int PendingRecovery)
{
    /// <summary>Gets the metadata-only savings estimate; it is not a slot/hash verification.</summary>
    public long PreliminarySavingsBytes { get; init; }
    /// <summary>Gets the metadata-only net estimate; it is not a slot/hash verification.</summary>
    public long PreliminaryNetReclaimedBytes { get; init; }
}

/// <summary>A bounded, path-free image result for administrators.</summary>
public sealed record ImageRunSample(Guid ItemId, ImageType Type, long OriginalBytes, long OutputBytes, string Reason);

/// <summary>Accepted native scheduled-task request.</summary>
public sealed record ExecutionReceipt(Guid ExecutionId, string TaskId, string State);
