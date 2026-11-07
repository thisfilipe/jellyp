#pragma warning disable CS1591 // Public serialized administration DTOs.
using Jellyfin.Plugin.JellyP.Configuration;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyP.Migration;

/// <summary>Request for one read-only, server-owned manual analysis.</summary>
public sealed class PlanAnalyzeRequest
{
    /// <summary>Gets or sets the explicit scope: items, libraries or all.</summary>
    public string Scope { get; set; } = string.Empty;
    public Guid[] ItemIds { get; set; } = [];
    public Guid[] LibraryIds { get; set; } = [];
    /// <summary>Gets or sets complete category policies. Empty uses the saved manual defaults.</summary>
    public ImageTypePolicy[]? Policies { get; set; }
    /// <summary>Gets or sets the complete general encoding snapshot. Null uses the saved manual defaults.</summary>
    public PlanGeneralPolicy? GeneralPolicy { get; set; }
    public bool? PreserveOriginals { get; set; }
    public int? BatchSize { get; set; }
}

/// <summary>General manual encoding values. Savings gates remain global.</summary>
public sealed class PlanGeneralPolicy
{
    public int Quality { get; set; } = 85;
    public bool Lossless { get; set; }
    public bool ResizeImages { get; set; }
    public int MaximumDimension { get; set; } = 2400;
    public double MinimumSsim { get; set; } = 0.96;
}

public sealed class PlanWarning
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class PlanSummary
{
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "Ready";
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string Scope { get; set; } = string.Empty;
    public int RequestedBatchSize { get; set; }
    public int TotalImages { get; set; }
    public int Evaluated { get; set; }
    public int NotEvaluated { get; set; }
    public int Proposed { get; set; }
    public int Convert { get; set; }
    public int Regenerate { get; set; }
    public int Kept { get; set; }
    public long EstimatedSavingsBytes { get; set; }
    public bool PreserveOriginals { get; set; }
    public string DefaultsSource { get; set; } = "legacy";
    public bool LegacyDefaultsWarning { get; set; }
    public Guid? OperationId { get; set; }
    public IReadOnlyList<PlanWarning> Warnings { get; set; } = [];
}

public sealed class PlanImageView
{
    public string Key { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string? Name { get; set; }
    public string? ItemType { get; set; }
    public int? ProductionYear { get; set; }
    public string Category { get; set; } = string.Empty;
    public ImageType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Index { get; set; }
    public bool IsPerson { get; set; }
    public string State { get; set; } = string.Empty;
    public string Action { get; set; } = "Keep";
    public string? Reason { get; set; }
    public string? SourceMimeType { get; set; }
    public long? CurrentBytes { get; set; }
    public long? OriginalBytes { get; set; }
    public long? OutputBytes { get; set; }
    public int? SourceWidth { get; set; }
    public int? SourceHeight { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public double? Ssim { get; set; }
    public long? SavingsBytes { get; set; }
    public string? SourceHash { get; set; }
    public string? OutputHash { get; set; }
    public string? PolicyFingerprint { get; set; }
    public Guid? ManagedRecordId { get; set; }
}

public sealed class PlanImagesPage
{
    public Guid PlanId { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Total { get; set; }
    public IReadOnlyList<PlanImageView> Items { get; set; } = [];
}

public sealed class PlanKeysResponse
{
    public Guid PlanId { get; set; }
    public string Kind { get; set; } = "selectable";
    public int Total { get; set; }
    public IReadOnlyList<string> Keys { get; set; } = [];
}

public sealed class PlanReviewRequest
{
    public string[] Keys { get; set; } = [];
    public int Offset { get; set; }
    public int Limit { get; set; } = 50;
    public int RetentionOffset { get; set; }
    public int RetentionLimit { get; set; } = 50;
}

public sealed class PlanBilling
{
    public int SelectedImages { get; set; }
    public long ActiveBytesBefore { get; set; }
    public long BackupBytesBefore { get; set; }
    public long ActiveBytesAfter { get; set; }
    public long BackupBytesAfter { get; set; }
    public long TotalBytesBefore { get; set; }
    public long TotalBytesAfter { get; set; }
    /// <summary>Gets or sets bytes before minus bytes after. Negative means extra bytes in use.</summary>
    public long DeltaBytes { get; set; }
    public long NewOriginalBytes { get; set; }
    public long ExistingBackupBytes { get; set; }
    public long RemovedBackupBytes { get; set; }
}

public sealed class RetentionCandidateView
{
    public Guid RecordId { get; set; }
    public Guid ItemId { get; set; }
    public string Category { get; set; } = string.Empty;
    public ImageType Type { get; set; }
    public int Index { get; set; }
    public long OriginalBytes { get; set; }
    public DateTime CompletedUtc { get; set; }
    public bool ProtectedBySelection { get; set; }
}

public sealed class RetentionPreview
{
    public int EligibleCount { get; set; }
    public long EligibleBytes { get; set; }
    public int ProtectedCount { get; set; }
    public long ProtectedBytes { get; set; }
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public bool HasMore { get; set; }
    /// <summary>Gets or sets whether this page is shorter than the eligible set.</summary>
    public bool Truncated { get; set; }
    /// <summary>Gets or sets a stable hash of the complete authorized expiry identity set and safety settings.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    public IReadOnlyList<RetentionCandidateView> Items { get; set; } = [];
}

public sealed class PlanReviewResponse
{
    public Guid PlanId { get; set; }
    public int TotalSelected { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public PlanBilling Billing { get; set; } = new();
    public RetentionPreview Retention { get; set; } = new();
    public IReadOnlyList<PlanImageView> Items { get; set; } = [];
    public IReadOnlyList<PlanWarning> Warnings { get; set; } = [];
}

public sealed class PlanApplyRequest
{
    public string[] Keys { get; set; } = [];
    public bool AuthorizePermanentRemoval { get; set; }
    public bool AuthorizeRetentionPurge { get; set; }
    /// <summary>Gets or sets the review retention fingerprint required when any other original expires.</summary>
    public string? AuthorizeRetentionFingerprint { get; set; }
}

public sealed class OperationReceipt
{
    public Guid OperationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string State { get; set; } = "Queued";
    public string Poll { get; set; } = string.Empty;
}

public sealed class OperationBilling
{
    public long ActiveBytesBefore { get; set; }
    public long ActiveBytesAfter { get; set; }
    public long BackupBytesBefore { get; set; }
    public long BackupBytesAfter { get; set; }
    public long NewOriginalBytes { get; set; }
    public long ExistingBackupBytes { get; set; }
    public long RemovedBackupBytes { get; set; }
    public long ReleasedBackupBytes { get; set; }
}

public sealed class OperationItemView
{
    public string Key { get; set; } = string.Empty;
    public Guid? RecordId { get; set; }
    public Guid ItemId { get; set; }
    public string Category { get; set; } = string.Empty;
    public ImageType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Index { get; set; }
    public string? Name { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? Reason { get; set; }
    public long? OriginalBytes { get; set; }
    public long? OutputBytes { get; set; }
    public int? OriginalWidth { get; set; }
    public int? OriginalHeight { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public string? OriginalMimeType { get; set; }
    public string? SourceHash { get; set; }
    public string? OutputHash { get; set; }
    public string? PolicyFingerprint { get; set; }
    public string? CleanupWarning { get; set; }
}

public sealed class OperationView
{
    public Guid OperationId { get; set; }
    public Guid? PlanId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string? Message { get; set; }
    public bool CancelRequested { get; set; }
    /// <summary>Gets or sets whether a durable mutation (image/restore/backup delete) was started.</summary>
    public bool MutationStarted { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public int TotalItems { get; set; }
    public int Processed { get; set; }
    public int Changed { get; set; }
    public int Failed { get; set; }
    public int Invalidated { get; set; }
    public int NotStarted { get; set; }
    public int RetentionPurgedCount { get; set; }
    public long RetentionPurgedBytes { get; set; }
    public OperationBilling Billing { get; set; } = new();
}

public sealed class OperationDetail
{
    public OperationView Operation { get; set; } = new();
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int TotalItems { get; set; }
    public IReadOnlyList<OperationItemView> Items { get; set; } = [];
}

public sealed class OperationPage
{
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Total { get; set; }
    public IReadOnlyList<OperationView> Items { get; set; } = [];
}

public sealed class BackupView
{
    public Guid RecordId { get; set; }
    public Guid ItemId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public ImageType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Index { get; set; }
    public string State { get; set; } = string.Empty;
    public bool Restorable { get; set; }
    public bool Retirable { get; set; }
    public string? Reason { get; set; }
    public string? OriginalMimeType { get; set; }
    public long OriginalBytes { get; set; }
    public long OutputBytes { get; set; }
    public int? OriginalWidth { get; set; }
    public int? OriginalHeight { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public string? PolicyFingerprint { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string OutputHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? Error { get; set; }
    public string? CleanupWarning { get; set; }
}

public sealed class BackupPage
{
    public string Filter { get; set; } = "all";
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Total { get; set; }
    public IReadOnlyList<BackupView> Items { get; set; } = [];
}

public sealed class BackupKeysResponse
{
    public string Filter { get; set; } = "all";
    public int Total { get; set; }
    public IReadOnlyList<Guid> Keys { get; set; } = [];
}

public sealed class BackupBatchRequest
{
    public Guid[] RecordIds { get; set; } = [];
}

public sealed class BackupRetireRequest
{
    public Guid[] RecordIds { get; set; } = [];
    public bool AuthorizePermanentRemoval { get; set; }
}

public sealed class GlobalLimits
{
    public long MinimumFreeSpaceBytes { get; set; }
    public long MaximumBackupBytes { get; set; }
    public int BackupRetentionDays { get; set; }
    public int MinimumSavingsBytes { get; set; }
    public double MinimumSavingsRatio { get; set; }
}

public sealed class ManualDefaultsSettings
{
    public bool PreserveOriginals { get; set; } = true;
    public int Quality { get; set; } = 85;
    public bool Lossless { get; set; }
    public bool ResizeImages { get; set; }
    public int MaximumDimension { get; set; } = 2400;
    public double MinimumSsim { get; set; } = 0.96;
    public int BatchSize { get; set; } = 250;
    public ImageTypePolicy[] Policies { get; set; } = [];
}

public sealed class AutomationSettings
{
    public bool Enabled { get; set; }
    public bool DryRun { get; set; } = true;
    public bool PreserveOriginals { get; set; } = true;
    public int Quality { get; set; } = 85;
    public bool Lossless { get; set; }
    public bool ResizeImages { get; set; }
    public int MaximumDimension { get; set; } = 2400;
    public double MinimumSsim { get; set; } = 0.96;
    public int BatchSize { get; set; } = 250;
    public string ScopeMode { get; set; } = "legacy";
    public Guid[] ItemIds { get; set; } = [];
    public Guid[] LibraryIds { get; set; } = [];
    public ImageTypePolicy[] Policies { get; set; } = [];
    public bool SyncUnchangedPolicies { get; set; }
}

public sealed class SettingsView
{
    public AutomationSettings Automation { get; set; } = new();
    public ManualDefaultsSettings Defaults { get; set; } = new();
    public GlobalLimits Globals { get; set; } = new();
    public string DefaultsSource { get; set; } = "legacy";
    public bool LegacyDefaultsWarning { get; set; }
    public string[] Categories { get; set; } = PluginConfiguration.Categories;
}

public sealed class SettingsDefaultsRequest
{
    /// <summary>Gets or sets explicit manual defaults. Null removes them and projects the legacy fields on read.</summary>
    public ManualDefaultsSettings? Defaults { get; set; }
    public GlobalLimits Globals { get; set; } = new();
}

public sealed class SettingsAutomationRequest
{
    public AutomationSettings Automation { get; set; } = new();
    public bool AuthorizePermanentRemoval { get; set; }
}

public sealed class NativeTaskView
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

public sealed class TasksView
{
    public RunReport LastRun { get; set; } = new();
    public IReadOnlyList<NativeTaskView> Tasks { get; set; } = [];
    public Guid? ActiveOperationId { get; set; }
    public Guid? CurrentRunnerOperationId { get; set; }
    public Guid? PendingOperationId { get; set; }
}

public sealed class JellyPRequestException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

internal sealed class PlanRecord
{
    public int Version { get; set; } = OptimizationCoordinator.PlanSchemaVersion;
    public Guid Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string Status { get; set; } = "Ready";
    public string Scope { get; set; } = string.Empty;
    public Guid[] ItemIds { get; set; } = [];
    public Guid[] LibraryIds { get; set; } = [];
    public int BatchSize { get; set; }
    public bool PreserveOriginals { get; set; }
    public PlanGeneralPolicy GeneralPolicy { get; set; } = new();
    public ImageTypePolicy[] Policies { get; set; } = [];
    public double MinimumSavingsRatio { get; set; }
    public int MinimumSavingsBytes { get; set; }
    /// <summary>Gets or sets a fingerprint of explicit manual defaults and limits; empty for legacy projection.</summary>
    public string ManualDefaultsFingerprint { get; set; } = string.Empty;
    public string DefaultsSource { get; set; } = "legacy";
    public bool LegacyDefaultsWarning { get; set; }
    public int RequestedBatchSize { get; set; }
    public int TotalImages { get; set; }
    public int Evaluated { get; set; }
    public int NotEvaluated { get; set; }
    public int Proposed { get; set; }
    public long EstimatedSavingsBytes { get; set; }
    public Guid? OperationId { get; set; }
    public List<PlanImage> Images { get; set; } = [];
    public List<PlanWarning> Warnings { get; set; } = [];
}

internal sealed class PlanImage
{
    public string Key { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string? Name { get; set; }
    public string? ItemType { get; set; }
    public int? ProductionYear { get; set; }
    public string Category { get; set; } = string.Empty;
    public ImageType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Index { get; set; }
    public bool IsPerson { get; set; }
    public string State { get; set; } = string.Empty;
    public string Action { get; set; } = "Keep";
    public string? Reason { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? SourceMimeType { get; set; }
    public long? CurrentBytes { get; set; }
    public long? OriginalBytes { get; set; }
    public long? OutputBytes { get; set; }
    public int? SourceWidth { get; set; }
    public int? SourceHeight { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public double? Ssim { get; set; }
    public long? SavingsBytes { get; set; }
    public string? CurrentHash { get; set; }
    public string? SourceHash { get; set; }
    public string? OutputHash { get; set; }
    public string? PolicyFingerprint { get; set; }
    public Guid? ManagedRecordId { get; set; }
}

internal sealed record BackupSlotState(bool IsShared, bool IsMutationSafe, string? Reason);

internal sealed class OperationRecord
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; }
    public Guid? PlanId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string State { get; set; } = "Queued";
    public string Phase { get; set; } = "Queued";
    public string? Message { get; set; }
    public bool CancelRequested { get; set; }
    /// <summary>Gets or sets whether a durable mutation (image/restore/backup delete) was started.</summary>
    public bool MutationStarted { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public bool PreserveOriginals { get; set; }
    public bool AuthorizePermanentRemoval { get; set; }
    public bool AuthorizeRetentionPurge { get; set; }
    public Guid[] AuthorizedRetentionRecordIds { get; set; } = [];
    public string RetentionFingerprint { get; set; } = string.Empty;
    public string RetentionSafetyFingerprint { get; set; } = string.Empty;
    public PlanBilling? PlannedBilling { get; set; }
    public OperationBilling ActualBilling { get; set; } = new();
    public int RetentionPurgedCount { get; set; }
    public long RetentionPurgedBytes { get; set; }
    public int Processed { get; set; }
    public int Changed { get; set; }
    public int Failed { get; set; }
    public int Invalidated { get; set; }
    public int NotStarted { get; set; }
    public List<OperationItem> Items { get; set; } = [];
}

internal sealed class OperationItem
{
    public string Key { get; set; } = string.Empty;
    public Guid? RecordId { get; set; }
    public Guid ItemId { get; set; }
    public string Category { get; set; } = string.Empty;
    public ImageType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Index { get; set; }
    public string? Name { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? Reason { get; set; }
    public string? Path { get; set; }
    public string? OriginalMimeType { get; set; }
    public long? OriginalBytes { get; set; }
    public long? OutputBytes { get; set; }
    public long? CurrentBytes { get; set; }
    public int? OriginalWidth { get; set; }
    public int? OriginalHeight { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public string? CurrentHash { get; set; }
    public string? SourceHash { get; set; }
    public string? OutputHash { get; set; }
    public string? PolicyFingerprint { get; set; }
    public string? CleanupWarning { get; set; }
}
