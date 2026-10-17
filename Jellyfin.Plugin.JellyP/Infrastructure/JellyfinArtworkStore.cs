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
    private InventorySummary? _inventorySummary;

    /// <inheritdoc />
    public InventorySummary? GetInventorySummary() => _inventorySummary;

    /// <inheritdoc />
    public IReadOnlyList<ItemArtworkInspection> InspectItems(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        var ids = itemIds.Distinct().ToArray();
        var items = new Dictionary<Guid, BaseItem>();
        foreach (var batch in ids.Chunk(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in ReadItems(batch)) items[item.Id] = item;
        }
        var results = new List<ItemArtworkInspection>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items.GetValueOrDefault(id);
            var images = new List<ArtworkImageInspection>();
            if (item is not null)
            {
                foreach (var group in item.ImageInfos.GroupBy(image => image.Type))
                {
                    var index = 0;
                    foreach (var image in group)
                    {
                        var reference = new ArtworkReference(id, group.Key, index, image.Path, item is Person);
                        var state = !image.IsLocalFile ? "Remote" : !File.Exists(image.Path) ? "Missing"
                            : !IsEligible(item, reference) ? "Excluded"
                            : Path.GetExtension(image.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase) ? "AlreadyWebp" : "Ready";
                        images.Add(new ArtworkImageInspection(group.Key, index++, item is Person ? "People" : group.Key.ToString(), state));
                    }
                }
            }
            results.Add(new ItemArtworkInspection(id, item?.Name, item?.GetType().Name, item?.ProductionYear, item is not null, images));
        }
        return results;
    }

    /// <inheritdoc />
    public void EnsureIdle(Guid? itemId = null)
    {
        if (library.IsScanRunning)
            throw new InvalidOperationException("Jellyfin library scan is running. Waiting for it to finish.");
        if (!itemId.HasValue) return; // Read-only inventory does not conflict with another item's queued refresh.
        var related = new HashSet<Guid> { itemId.Value };
        var item = library.GetItemById(itemId.Value);
        if (item is not null)
        {
            related.UnionWith(library.GetCollectionFolders(item).Select(folder => folder.Id));
            var current = item;
            while (current is not null)
            {
                var parent = current.ParentId != Guid.Empty ? current.ParentId : current.OwnerId;
                if (parent == Guid.Empty || !related.Add(parent)) break;
                current = library.GetItemById(parent);
            }
        }
        var queue = providers.GetRefreshQueue();
        if (related.Any(id => queue.Contains(id) || providers.GetRefreshProgress(id).HasValue))
            throw new InvalidOperationException("Jellyfin is refreshing this item or its library. Waiting for metadata to finish.");
    }

    /// <inheritdoc />
    public IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken, IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null, Action<int>? inventoryProgress = null)
    {
        // Read-only inventory and simulation are allowed while native tasks are active.
        // Apply rechecks/waits at transaction boundaries in the coordinator.
        var scope = includedLibraryIds is null ? null : ResolveLibraryItems(includedLibraryIds, cancellationToken);
        var summary = new InventorySummary();
        _inventorySummary = summary;
        // A selected item without usable local images cannot participate in a write.
        // Avoid a global reference pass in that case; any candidate still requires the full pass below.
        if (includedItemIds is { Count: > 0 })
        {
            var inspection = InspectItems(includedItemIds, cancellationToken)
                .Where(item => scope is null || scope.Contains(item.ItemId)).ToArray();
            if (inspection.All(item => item.Images.All(image => image.State is not ("Ready" or "AlreadyWebp"))))
            {
                summary.TotalItems = inspection.Length;
                summary.MatchedItems = inspection.Count(item => item.Found);
                summary.ItemsWithoutImages = inspection.Count(item => item.Found && item.Images.Count == 0);
                summary.RemoteImages = inspection.Sum(item => item.Images.Count(image => image.State == "Remote"));
                summary.LocalImages = inspection.Sum(item => item.Images.Count(image => image.State != "Remote"));
                summary.ExcludedImages = summary.LocalImages;
                inventoryProgress?.Invoke(inspection.Length);
                return [];
            }
        }
        var candidates = new List<ArtworkReference>();
        var referenceCounts = new Dictionary<string, int>(PathComparer);
        var totalReferences = 0;
        var inspectedItems = 0;
        foreach (var items in InventoryBatches(cancellationToken))
        {
            foreach (var item in items)
            {
                summary.TotalItems++;
                var inScope = (scope is null || scope.Contains(item.Id))
                    && (includedItemIds is null || includedItemIds.Count == 0 || includedItemIds.Contains(item.Id));
                if (inScope) summary.MatchedItems++;
                if (inScope && item.ImageInfos.Length == 0) summary.ItemsWithoutImages++;
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
                            if (inScope)
                            {
                                summary.LocalImages++;
                                var reference = new ArtworkReference(item.Id, group.Key, index, image.Path, item is Person);
                                if (IsEligible(item, reference)) candidates.Add(reference);
                                else summary.ExcludedImages++;
                            }
                        }
                        else if (inScope) summary.RemoteImages++;

                        index++;
                    }
                }
            }
            inspectedItems += items.Count;
            inventoryProgress?.Invoke(inspectedItems);
        }

        var result = candidates.Where(r => referenceCounts[Path.GetFullPath(r.Path)] == 1)
            .OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
        summary.SharedImages = candidates.Count - result.Length;
        summary.EligibleImages = result.Length;
        summary.AlreadyWebpImages = result.Count(image => Path.GetExtension(image.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private HashSet<Guid> ResolveLibraryItems(IReadOnlyCollection<Guid> libraryIds, CancellationToken cancellationToken)
    {
        var selected = new HashSet<Guid>();
        if (libraryIds.Count == 0) return selected; // A missing selection must never mean the whole server.
        var known = library.GetVirtualFolders().Where(folder => Guid.TryParse(folder.ItemId, out _))
            .Select(folder => Guid.Parse(folder.ItemId)).ToHashSet();
        var physicalIds = new HashSet<Guid>();
        foreach (var id in libraryIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!known.Contains(id) || library.GetItemById(id) is not CollectionFolder folder)
                throw new InvalidOperationException("A selected library no longer exists. Review the JellyP library selection.");
            selected.Add(id); // Include the library's own cover.
            physicalIds.UnionWith(folder.PhysicalFolderIds);
        }
        if (physicalIds.Count == 0) return selected;
        // Native library IDs are virtual CollectionFolders; TopParentIds must use their physical IDs.
        selected.UnionWith(library.GetItemIds(new InternalItemsQuery
        {
            TopParentIds = physicalIds.ToArray(), Recursive = true, IncludeOwnedItems = true,
            GroupByPresentationUniqueKey = false, EnableTotalRecordCount = false, DtoOptions = DtoOptions.StoredColumnsOnly,
        }));
        // People live outside the folder hierarchy. Associate only people credited in selected items.
        // Their entities/photos are global in Jellyfin, including when shared with another library.
        var people = new HashSet<Guid>();
        foreach (var batch in selected.ToArray().Chunk(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var credits in library.GetPeopleByItems(batch).Values)
                foreach (var person in credits)
                    if (!string.IsNullOrWhiteSpace(person.Name)) people.Add(library.GetPersonId(person.Name));
        }
        selected.UnionWith(people);
        return selected;
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
            yield return ReadItems(batch);
            // Unknown types can disappear during hydration; continue from snapshot IDs regardless.
        }
    }

    private IReadOnlyList<BaseItem> ReadItems(Guid[] ids) => library.GetItemList(new InternalItemsQuery
    {
        ItemIds = ids,
        IncludeOwnedItems = true,
        GroupByPresentationUniqueKey = false,
        EnableTotalRecordCount = false,
        DtoOptions = InventoryOptions(),
    });

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
