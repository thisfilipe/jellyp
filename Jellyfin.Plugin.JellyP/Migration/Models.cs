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
    /// <summary>Lists image slots, excluding shared and external files.</summary>
    IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken, IReadOnlyCollection<Guid>? includedItemIds = null);

    /// <summary>Checks that no scan or refresh is active.</summary>
    void EnsureIdle(Guid? itemId = null);

    /// <summary>Reads the current image slot, including its current path.</summary>
    ArtworkReference? GetCurrent(ArtworkReference reference);

    /// <summary>Predicts the pinned server's internal destination for collision checks.</summary>
    string GetDestination(ArtworkReference reference, string mimeType);

    /// <summary>Checks every library source before removing a plugin-created orphan.</summary>
    bool IsPathReferenced(string path);

    /// <summary>Saves staged bytes using Jellyfin and persists its reference.</summary>
    Task<ArtworkReference> SaveAsync(ArtworkReference reference, string stagingPath, string mimeType);
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
    public string PolicyFingerprint { get; set; } = string.Empty;
    public int Revision { get; set; }
}

/// <summary>Persisted scan cursor and most recent run summary.</summary>
public sealed class RunReport
{
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
    long NetReclaimedBytes, IReadOnlyList<MigrationRecord> RecentRecords, int PendingRecovery);
