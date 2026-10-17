using Jellyfin.Plugin.JellyP.Conversion;
using Jellyfin.Plugin.JellyP.Infrastructure;
using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Model.Plugins;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyP.Configuration;

/// <summary>Global defaults, category selection and optional per-category encoding overrides.</summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets whether artwork mutations are permitted.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets whether tasks only estimate savings.</summary>
    public bool DryRun { get; set; } = true;
    /// <summary>Gets or sets confirmation that uploads and refreshes are paused.</summary>
    public bool MaintenanceWindowConfirmed { get; set; }
    /// <summary>Gets or sets whether originals remain available after successful verification.</summary>
    public bool PreserveOriginals { get; set; } = true;
    /// <summary>Gets or sets global lossy quality; 100 is still lossy unless Lossless is enabled.</summary>
    public int Quality { get; set; } = 85;
    /// <summary>Gets or sets lossless WebP encoding.</summary>
    public bool Lossless { get; set; }
    /// <summary>Gets or sets whether selected images may be downscaled.</summary>
    public bool ResizeImages { get; set; }
    /// <summary>Gets or sets the longest edge when downscaling is enabled.</summary>
    public int MaximumDimension { get; set; } = 2400;
    /// <summary>Gets or sets minimum fractional storage savings.</summary>
    public double MinimumSavingsRatio { get; set; } = 0.05;
    /// <summary>Gets or sets minimum absolute savings.</summary>
    public int MinimumSavingsBytes { get; set; } = 4096;
    /// <summary>Gets or sets the minimum windowed luminance SSIM.</summary>
    public double MinimumSsim { get; set; } = 0.96;
    /// <summary>Gets or sets the maximum candidates per run.</summary>
    public int BatchSize { get; set; } = 250;
    /// <summary>Gets or sets the free-space reserve.</summary>
    public long MinimumFreeSpaceBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    /// <summary>Gets or sets the retained-original budget.</summary>
    public long MaximumBackupBytes { get; set; } = 512L * 1024 * 1024;
    /// <summary>Gets or sets days before automatic retirement; zero retains originals indefinitely.</summary>
    public int BackupRetentionDays { get; set; }
    /// <summary>Gets or sets whether policy sync also regenerates unchanged policies.</summary>
    public bool SyncUnchangedPolicies { get; set; }
    /// <summary>Gets or sets an optional item allowlist for canaries; empty selects the library.</summary>
    public Guid[] IncludedItemIds { get; set; } = [];
    /// <summary>Gets or sets scope: legacy preserves pre-0.3 item/global selection; libraries never expands an empty selection.</summary>
    public string ScopeMode { get; set; } = "legacy";
    /// <summary>Gets or sets the selected native Jellyfin library IDs.</summary>
    public Guid[] IncludedLibraryIds { get; set; } = [];
    /// <summary>Gets or sets category selection and individual overrides.</summary>
    public ImageTypePolicy[] ImagePolicies { get; set; } = DefaultPolicies();

    /// <summary>Resolves person artwork before ordinary image types.</summary>
    public ImageTypePolicy? PolicyFor(ArtworkReference image) => ImagePolicies.FirstOrDefault(p =>
        p.Category == (image.IsPerson ? "People" : image.Type.ToString()));

    /// <summary>Resolves one category into a complete immutable converter configuration.</summary>
    public WebpOptimizationOptions OptionsFor(ImageTypePolicy policy) => new()
    {
        Quality = policy.OverrideGlobal ? policy.Quality : Quality,
        Lossless = policy.OverrideGlobal ? policy.Lossless : Lossless,
        MaximumDimension = (policy.OverrideGlobal ? policy.ResizeImages : ResizeImages)
            ? (policy.OverrideGlobal ? policy.MaximumDimension : MaximumDimension) : WebpImageConverter.MaximumDimension,
        MinimumSsim = policy.OverrideGlobal ? policy.MinimumSsim : MinimumSsim,
        MinimumSavingsRatio = MinimumSavingsRatio,
        MinimumSavingsBytes = MinimumSavingsBytes,
    };

    /// <summary>Produces a deterministic fingerprint of the effective policy.</summary>
    public string Fingerprint(ImageTypePolicy policy) => DurableFiles.Hash(JsonSerializer.SerializeToUtf8Bytes(OptionsFor(policy)));

    /// <summary>Validates settings before inventory or mutation.</summary>
    public void Validate()
    {
        if (Quality is < 0 or > 100 || MaximumDimension is < 256 or > 8192
            || !double.IsFinite(MinimumSavingsRatio) || MinimumSavingsRatio is < 0 or > 0.9
            || !double.IsFinite(MinimumSsim) || MinimumSsim is < 0 or > 1
            || MinimumSavingsBytes < 0 || BatchSize is < 1 or > 10000
            || MinimumFreeSpaceBytes < 64L * 1024 * 1024
            || MaximumBackupBytes < 32L * 1024 * 1024
            || BackupRetentionDays is < 0 or > 3650 || ImagePolicies is null || IncludedItemIds is null
            || IncludedItemIds.Length > 10000 || IncludedItemIds.Any(id => id == Guid.Empty)
            || ImagePolicies.Any(p => p is null || !Categories.Contains(p.Category)
                || p.Quality is < 0 or > 100 || p.MaximumDimension is < 256 or > 8192
                || !double.IsFinite(p.MinimumSsim) || p.MinimumSsim is < 0 or > 1)
            || ImagePolicies.Select(p => p.Category).Distinct(StringComparer.Ordinal).Count() != ImagePolicies.Length
            || ScopeMode is not ("legacy" or "all" or "libraries" or "items")
            || IncludedLibraryIds is null || IncludedLibraryIds.Length > 1000 || IncludedLibraryIds.Any(id => id == Guid.Empty)
            || (ScopeMode == "libraries" && IncludedLibraryIds.Length == 0)
            || (ScopeMode == "items" && IncludedItemIds.Length == 0))
        {
            throw new ArgumentException("Invalid JellyP settings. Check quality, dimensions, limits and category policies.");
        }
    }

    /// <summary>Gets all writable library artwork categories. Chapter/user avatars use different Jellyfin stores.</summary>
    public static string[] Categories => ["People", "Primary", "Thumb", "Logo", "Backdrop", "Art", "Banner", "Disc", "Box", "BoxRear", "Menu"];
    private static ImageTypePolicy[] DefaultPolicies() => Categories.Select(category => new ImageTypePolicy
    {
        Category = category,
        Enabled = category is "People" or "Primary" or "Thumb" or "Logo",
    }).ToArray();
}

/// <summary>Selection and optional full encoding override for a category.</summary>
public sealed class ImageTypePolicy
{
    /// <summary>Gets or sets the category (People overrides the ordinary image type for person items).</summary>
    public string Category { get; set; } = "Primary";
    /// <summary>Gets or sets whether this category is processed.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets whether this row replaces the global encoding defaults.</summary>
    public bool OverrideGlobal { get; set; }
    /// <summary>Gets or sets lossy quality.</summary>
    public int Quality { get; set; } = 85;
    /// <summary>Gets or sets lossless encoding.</summary>
    public bool Lossless { get; set; }
    /// <summary>Gets or sets optional downscaling.</summary>
    public bool ResizeImages { get; set; }
    /// <summary>Gets or sets the maximum output edge when downscaling is enabled.</summary>
    public int MaximumDimension { get; set; } = 2400;
    /// <summary>Gets or sets the quality gate independently of encoder quality.</summary>
    public double MinimumSsim { get; set; } = 0.96;
}
