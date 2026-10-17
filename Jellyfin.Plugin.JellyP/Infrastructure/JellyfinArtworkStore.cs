using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JellyP.Infrastructure;

/// <summary>Uses supported Jellyfin save and persistence services for internal library artwork.</summary>
public sealed class JellyfinArtworkStore(ILibraryManager library, IProviderManager providers, IServerConfigurationManager configuration) : IArtworkStore
{
    /// <inheritdoc />
    public void EnsureIdle(Guid? itemId = null)
    {
        if (library.IsScanRunning || providers.GetRefreshQueue().Count != 0
            || (itemId.HasValue && providers.GetRefreshProgress(itemId.Value).HasValue))
        {
            throw new InvalidOperationException("Jellyfin is scanning or refreshing. Retry during an idle maintenance window.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken, IReadOnlyCollection<Guid>? includedItemIds = null)
    {
        EnsureIdle();
        var candidates = new List<ArtworkReference>();
        var referenceCounts = new Dictionary<string, int>(PathComparer);
        var totalReferences = 0;
        foreach (var items in InventoryBatches(cancellationToken))
        {
            foreach (var item in items)
            {
                foreach (var group in item.ImageInfos.GroupBy(i => i.Type))
                {
                    var index = 0;
                    foreach (var image in group)
                    {
                        if (image.IsLocalFile)
                        {
                            // Count all sources before restricting candidate scope, including non-library/shared paths.
                            var path = Path.GetFullPath(image.Path);
                            referenceCounts[path] = referenceCounts.GetValueOrDefault(path) + 1;
                            if (++totalReferences > 1_000_000)
                                throw new InvalidOperationException("Inventory exceeds one million image references; split the library before migration.");
                            if (includedItemIds is null || includedItemIds.Count == 0 || includedItemIds.Contains(item.Id))
                            {
                                var reference = new ArtworkReference(item.Id, group.Key, index, image.Path, item is Person);
                                if (IsEligible(item, reference)) candidates.Add(reference);
                            }
                        }

                        index++;
                    }
                }
            }

        }

        return candidates.Where(r => referenceCounts[Path.GetFullPath(r.Path)] == 1)
            .OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public ArtworkReference? GetCurrent(ArtworkReference reference)
    {
        var item = library.GetItemById(reference.ItemId);
        var current = item?.GetImageInfo(reference.Type, reference.Index);
        return current is { IsLocalFile: true } ? reference with { Path = current.Path } : null;
    }

    /// <inheritdoc />
    public bool IsPathReferenced(string path)
    {
        foreach (var items in InventoryBatches(CancellationToken.None))
        {
            if (items.Any(item => item.ImageInfos.Any(image => image.IsLocalFile
                && string.Equals(Path.GetFullPath(image.Path), Path.GetFullPath(path), PathComparison)))) return true;
        }

        return false;
    }

    private IEnumerable<IReadOnlyList<BaseItem>> InventoryBatches(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Snapshot scalar IDs once: SQL OFFSET over a large non-unique SortName order is both
        // expensive and unsafe for completeness. Include owned/alternate versions in both queries.
        var ids = library.GetItemIds(new InternalItemsQuery
        {
            Recursive = true,
            IncludeOwnedItems = true,
            GroupByPresentationUniqueKey = false,
            EnableTotalRecordCount = false,
            DtoOptions = DtoOptions.StoredColumnsOnly,
        });
        // Remains below even SQLite's legacy 999-parameter limit; no offset or repeated count.
        const int batchSize = 500;
        foreach (var batch in ids.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return library.GetItemList(new InternalItemsQuery
            {
                ItemIds = batch,
                IncludeOwnedItems = true,
                GroupByPresentationUniqueKey = false,
                EnableTotalRecordCount = false,
                DtoOptions = InventoryOptions(),
            });
            // Unknown types can disappear during hydration; continue from snapshot IDs regardless.
        }
    }

    /// <inheritdoc />
    public string GetDestination(ArtworkReference reference, string mimeType)
    {
        var item = library.GetItemById(reference.ItemId) ?? throw new InvalidOperationException("Library item no longer exists.");
        // Mirror the pinned 12.1 internal naming policy only to reject existing destinations BEFORE SaveImage truncates them.
        // The actual save path still comes from Jellyfin, and must match this prediction.
        var folder = item is MusicAlbum or MusicArtist or PhotoAlbum or Person ? "folder" : "poster";
        var name = reference.Type switch
        {
            ImageType.Primary => folder,
            ImageType.Art => "clearart",
            ImageType.BoxRear => "back",
            ImageType.Thumb => "landscape",
            ImageType.Disc => item is MusicAlbum ? "cdart" : "disc",
            ImageType.Backdrop when reference.Index == 0 => "backdrop",
            ImageType.Backdrop => NextBackdrop(item),
            _ => reference.Type.ToString().ToLowerInvariant(),
        };
        var extension = mimeType switch
        {
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => throw new NotSupportedException("Only JPEG, PNG and WebP save operations are supported."),
        };
        return Path.Combine(item.GetInternalMetadataPath(), name + extension);
    }

    /// <inheritdoc />
    public async Task<ArtworkReference> SaveAsync(ArtworkReference reference, string stagingPath, string mimeType)
    {
        var item = library.GetItemById(reference.ItemId) ?? throw new InvalidOperationException("Library item no longer exists.");
        await providers.SaveImage(item, stagingPath, mimeType, reference.Type, reference.Index,
            saveLocallyWithMedia: false, CancellationToken.None).ConfigureAwait(false);
        await library.UpdateImagesAsync(item).ConfigureAwait(false);
        var image = item.GetImageInfo(reference.Type, reference.Index)
            ?? throw new IOException("Jellyfin did not retain the saved image reference.");
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new IOException("Jellyfin could not decode the saved artwork.");
        }

        return reference with { Path = image.Path };
    }

    private bool IsEligible(BaseItem item, ArtworkReference reference)
    {
        if (reference.Type == ImageType.Chapter
            || !(Path.GetExtension(reference.Path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(reference.Path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(reference.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(reference.Path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (item.SourceType != SourceType.Library)
        {
            return false;
        }

        // Legacy People/Studio artwork can legitimately live outside the item's current canonical folder.
        // Only accept referenced files beneath Jellyfin's configured internal metadata root; shared paths stay excluded.
        var root = Path.GetFullPath(configuration.ApplicationPaths.InternalMetadataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(reference.Path).StartsWith(root, PathComparison))
        {
            return false;
        }

        // UpdateImagesAsync also validates other slots: exclude items where that could fetch or delete other references.
        return item.ImageInfos.All(i => i.IsLocalFile && File.Exists(i.Path));
    }

    private static string NextBackdrop(BaseItem item)
    {
        var names = item.GetImages(ImageType.Backdrop).Select(i => Path.GetFileNameWithoutExtension(i.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        while (names.Contains("backdrop" + index.ToString(CultureInfo.InvariantCulture)))
        {
            index++;
        }

        return "backdrop" + index.ToString(CultureInfo.InvariantCulture);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // Inventory objects stay read-only. SaveAsync reloads the complete item through GetItemById.
    private static DtoOptions InventoryOptions() => new(false)
    {
        EnableImages = true,
        EnableUserData = false,
        Fields = [ItemFields.Path],
    };
}
