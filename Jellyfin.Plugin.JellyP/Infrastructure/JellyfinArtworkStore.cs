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
    private const int AnalysisSnapshotSeconds = 30;
    private readonly object _analysisCacheGate = new();
    private InventorySummary? _inventorySummary;
    private AnalysisReferenceSnapshot? _analysisSnapshot;
    private long _mutationVersion;

    private sealed record AnalysisReferenceSnapshot(
        Dictionary<string, int> Counts,
        IReadOnlyList<Guid> SortedItemIds,
        DateTime CapturedUtc,
        long MutationVersion,
        bool BuiltDuringScan);

    private void InvalidateAnalysisSnapshot()
    {
        lock (_analysisCacheGate)
        {
            _mutationVersion++;
            _analysisSnapshot = null;
        }
    }

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
                // Sibling safety is an item-level fact and only matters for in-scope
                // items. Computing it for every out-of-scope item was a global stat pass.
                var siblingsSafe = inScope && item.ImageInfos.All(image => image.IsLocalFile && File.Exists(image.Path));
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
                                if (IsEligible(item, reference, siblingsSafe)) candidates.Add(reference);
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

    /// <inheritdoc />
    public IReadOnlyList<ArtworkInventoryEntry> DetailedInventory(CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
        Action<int>? inventoryProgress = null, int? maximumEntries = null) =>
        DetailedInventoryCore(cancellationToken, includedItemIds, includedLibraryIds, afterKey: null,
                maximumEntries: null, inventoryProgress)
            .Take(maximumEntries is > 0 ? maximumEntries.Value + 1 : int.MaxValue)
            .ToArray();

    /// <inheritdoc />
    public IReadOnlyList<ArtworkInventoryEntry> DetailedInventoryPage(CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
        string? afterKey = null, int? maximumEntries = null, Action<int>? inventoryProgress = null)
    {
        var snapshot = GetAnalysisSnapshot(cancellationToken, inventoryProgress);
        if (!AnalysisSnapshotFresh(snapshot))
        {
            InvalidateAnalysisSnapshot();
            snapshot = GetAnalysisSnapshot(cancellationToken, inventoryProgress, force: true);
            if (!AnalysisSnapshotFresh(snapshot))
                throw new InvalidOperationException("Library inventory changed during analysis; retry after the scan finishes.");
        }

        var page = CollectAnalysisPage(snapshot, cancellationToken, includedItemIds, includedLibraryIds,
            afterKey, maximumEntries);
        // A missing key means the advisory snapshot cannot prove the count. Rebuild
        // once; if it is still absent, fail closed instead of assuming zero references.
        if (page.Any(entry => IsLocalPath(entry.Reference.Path)
                && !snapshot.Counts.ContainsKey(Path.GetFullPath(entry.Reference.Path))))
        {
            InvalidateAnalysisSnapshot();
            snapshot = GetAnalysisSnapshot(cancellationToken, inventoryProgress, force: true);
            page = CollectAnalysisPage(snapshot, cancellationToken, includedItemIds, includedLibraryIds,
                afterKey, maximumEntries);
            if (page.Any(entry => IsLocalPath(entry.Reference.Path)
                    && !snapshot.Counts.ContainsKey(Path.GetFullPath(entry.Reference.Path))))
                throw new InvalidOperationException("Inventory reference counts changed during analysis; retry the analysis.");
        }

        return page;
    }

    private IReadOnlyList<ArtworkInventoryEntry> CollectAnalysisPage(AnalysisReferenceSnapshot snapshot,
        CancellationToken cancellationToken, IReadOnlyCollection<Guid>? includedItemIds,
        IReadOnlyCollection<Guid>? includedLibraryIds, string? afterKey, int? maximumEntries)
    {
        var scope = includedLibraryIds is null ? null : ResolveLibraryItems(includedLibraryIds, cancellationToken);
        var itemFilter = includedItemIds is { Count: > 0 } ? includedItemIds.ToHashSet() : null;
        var ordered = snapshot.SortedItemIds
            .Where(id => (scope is null || scope.Contains(id)) && (itemFilter is null || itemFilter.Contains(id)))
            .ToArray();
        var afterItemKey = CursorItemKey(afterKey);
        var entries = new List<ArtworkInventoryEntry>();
        var maximum = maximumEntries is > 0 ? maximumEntries.Value + 1 : int.MaxValue;
        var collecting = true;
        var startOffset = afterItemKey is null
            ? 0
            : Array.FindIndex(ordered, id => string.CompareOrdinal(id.ToString("N"), afterItemKey) >= 0);
        if (startOffset < 0) return [];
        for (var offset = startOffset; offset < ordered.Length && collecting; offset += 500)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ordered.Skip(offset).Take(500).ToArray();
            var hydrated = ReadItems(batch).GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
            foreach (var id in batch)
            {
                if (!hydrated.TryGetValue(id, out var item)) continue;
                var siblingsSafe = item.ImageInfos.All(image => image.IsLocalFile && File.Exists(image.Path));
                // Native index order inside each type, but types must be key-ordered
                // before the page cut. GroupBy alone preserves ImageInfos order, which
                // is not guaranteed to match ArtworkReference.Key.
                var slots = item.ImageInfos
                    .GroupBy(image => image.Type)
                    .SelectMany(group => group.Select((image, index) => (Type: group.Key, Index: index, Image: image)))
                    .OrderBy(slot => (int)slot.Type).ThenBy(slot => slot.Index);
                foreach (var slot in slots)
                {
                    var reference = new ArtworkReference(id, slot.Type, slot.Index, slot.Image.Path, item is Person);
                    if (IsAfterCursor(reference, afterKey)
                        && (afterItemKey is null || string.CompareOrdinal(id.ToString("N"), afterItemKey) >= 0))
                    {
                        var (state, classifiedReason) = slot.Image.IsLocalFile
                            ? Classify(item, reference, siblingsSafe)
                            : ("Remote", "RemoteReference");
                        var reason = classifiedReason;
                        var path = Path.GetFullPath(reference.Path);
                        var count = slot.Image.IsLocalFile ? snapshot.Counts.GetValueOrDefault(path) : 1;
                        var shared = slot.Image.IsLocalFile && count > 1;
                        entries.Add(new ArtworkInventoryEntry
                        {
                            Reference = reference,
                            Name = item.Name,
                            ItemType = item.GetType().Name,
                            ProductionYear = item.ProductionYear,
                            Category = item is Person ? "People" : slot.Type.ToString(),
                            State = shared && state is "Ready" or "AlreadyWebp" ? "Shared" : state,
                            Reason = shared && state is "Ready" or "AlreadyWebp" ? "SharedReference" : reason,
                            CurrentBytes = slot.Image.IsLocalFile && File.Exists(path) ? new FileInfo(path).Length : null,
                            MimeType = slot.Image.IsLocalFile ? MimeType(path) : null,
                            IsShared = shared,
                            ReferenceCount = count,
                        });
                        if (entries.Count >= maximum)
                        {
                            collecting = false;
                            break;
                        }
                    }
                }

                if (!collecting) break;
            }
        }

        return entries.OrderBy(entry => entry.Reference.Key, StringComparer.Ordinal).ToArray();
    }

    private static string? CursorItemKey(string? afterKey)
    {
        if (string.IsNullOrEmpty(afterKey)) return null;
        if (afterKey.Length < 32 || !Guid.TryParseExact(afterKey[..32], "N", out var id))
            throw new InvalidOperationException("The plan cursor is invalid; analyze the selection again.");
        return id.ToString("N");
    }

    private static bool IsLocalPath(string path) => Path.IsPathRooted(path)
        && !path.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    // A current-call snapshot may be used while a scan is active, but it must
    // never be published to the advisory cache. Mutation version still guards
    // against a plugin save racing the reference pass.
    private bool AnalysisSnapshotFresh(AnalysisReferenceSnapshot snapshot) =>
        snapshot.MutationVersion == _mutationVersion;

    private bool AnalysisSnapshotCacheable(AnalysisReferenceSnapshot snapshot) =>
        !snapshot.BuiltDuringScan && !library.IsScanRunning
        && snapshot.MutationVersion == _mutationVersion;

    private AnalysisReferenceSnapshot GetAnalysisSnapshot(CancellationToken cancellationToken,
        Action<int>? inventoryProgress, bool force = false)
    {
        if (!force)
        {
            lock (_analysisCacheGate)
            {
                var cached = _analysisSnapshot;
                if (cached is not null
                    && cached.MutationVersion == _mutationVersion
                    && !library.IsScanRunning
                    && cached.CapturedUtc > DateTime.UtcNow.AddSeconds(-AnalysisSnapshotSeconds))
                    return cached; // Do not renew CapturedUtc on a hit.
            }
        }

        var startVersion = _mutationVersion;
        var startScan = library.IsScanRunning;
        var ids = library.GetItemIds(new InternalItemsQuery
        {
            Recursive = true,
            IncludeOwnedItems = true,
            GroupByPresentationUniqueKey = false,
            EnableTotalRecordCount = false,
            DtoOptions = DtoOptions.StoredColumnsOnly,
        }).OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        var counts = new Dictionary<string, int>(PathComparer);
        var totalReferences = 0;
        var inspected = 0;
        foreach (var batch in ids.Chunk(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in ReadItems(batch))
            {
                foreach (var image in item.ImageInfos)
                {
                    if (!image.IsLocalFile) continue;
                    var path = Path.GetFullPath(image.Path);
                    counts[path] = counts.GetValueOrDefault(path) + 1;
                    if (++totalReferences > 1_000_000)
                        throw new InvalidOperationException("Inventory exceeds one million image references; split the library before migration.");
                }
            }

            inspected += batch.Length;
            inventoryProgress?.Invoke(inspected);
        }

        var builtDuringScan = startScan || library.IsScanRunning;
        var snapshot = new AnalysisReferenceSnapshot(counts, ids, DateTime.UtcNow, startVersion, builtDuringScan);
        lock (_analysisCacheGate)
        {
            // Never publish a pass that began or ended during a scan, or whose
            // mutation version changed while it was being built.
            if (AnalysisSnapshotCacheable(snapshot))
                _analysisSnapshot = snapshot;
        }

        return snapshot;
    }

    private IReadOnlyList<ArtworkInventoryEntry> DetailedInventoryCore(CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? includedItemIds, IReadOnlyCollection<Guid>? includedLibraryIds,
        string? afterKey, int? maximumEntries, Action<int>? inventoryProgress)
    {
        var scope = includedLibraryIds is null ? null : ResolveLibraryItems(includedLibraryIds, cancellationToken);
        var itemFilter = includedItemIds is { Count: > 0 } ? includedItemIds.ToHashSet() : null;
        var summary = new InventorySummary();
        _inventorySummary = summary;
        var entries = new List<ArtworkInventoryEntry>();
        var referenceCounts = new Dictionary<string, int>(PathComparer);
        var totalReferences = 0;
        var inspectedItems = 0;
        var collectAll = maximumEntries is not > 0;
        var collecting = true;
        foreach (var items in InventoryBatches(cancellationToken))
        {
            foreach (var item in items)
            {
                summary.TotalItems++;
                var inScope = (scope is null || scope.Contains(item.Id))
                    && (itemFilter is null || itemFilter.Contains(item.Id));
                if (inScope) summary.MatchedItems++;
                if (inScope && item.ImageInfos.Length == 0) summary.ItemsWithoutImages++;
                var siblingsSafe = inScope && item.ImageInfos.All(image => image.IsLocalFile && File.Exists(image.Path));
                foreach (var group in item.ImageInfos.GroupBy(i => i.Type))
                {
                    var index = 0;
                    foreach (var image in group)
                    {
                        if (image.IsLocalFile)
                        {
                            var path = Path.GetFullPath(image.Path);
                            referenceCounts[path] = referenceCounts.GetValueOrDefault(path) + 1;
                            if (++totalReferences > 1_000_000)
                                throw new InvalidOperationException("Inventory exceeds one million image references; split the library before migration.");
                            if (inScope)
                            {
                                summary.LocalImages++;
                                var reference = new ArtworkReference(item.Id, group.Key, index, image.Path, item is Person);
                                if (!IsEligible(item, reference, siblingsSafe)) summary.ExcludedImages++;
                                if (collecting && IsAfterCursor(reference, afterKey))
                                {
                                    var (state, reason) = Classify(item, reference, siblingsSafe);
                                    entries.Add(new ArtworkInventoryEntry
                                    {
                                        Reference = reference,
                                        Name = item.Name,
                                        ItemType = item.GetType().Name,
                                        ProductionYear = item.ProductionYear,
                                        Category = item is Person ? "People" : group.Key.ToString(),
                                        State = state,
                                        Reason = reason,
                                        CurrentBytes = File.Exists(path) ? new FileInfo(path).Length : null,
                                        MimeType = MimeType(path),
                                    });
                                    if (!collectAll && entries.Count > maximumEntries!.Value) collecting = false;
                                }
                            }
                        }
                        else if (inScope)
                        {
                            summary.RemoteImages++;
                            var reference = new ArtworkReference(item.Id, group.Key, index, image.Path, item is Person);
                            if (collecting && IsAfterCursor(reference, afterKey))
                            {
                                entries.Add(new ArtworkInventoryEntry
                                {
                                    Reference = reference,
                                    Name = item.Name,
                                    ItemType = item.GetType().Name,
                                    ProductionYear = item.ProductionYear,
                                    Category = item is Person ? "People" : group.Key.ToString(),
                                    State = "Remote",
                                    Reason = "RemoteReference",
                                });
                                if (!collectAll && entries.Count > maximumEntries!.Value) collecting = false;
                            }
                        }

                        index++;
                    }
                }
            }

            inspectedItems += items.Count;
            inventoryProgress?.Invoke(inspectedItems);
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!referenceCounts.TryGetValue(Path.GetFullPath(entry.Reference.Path), out var count)) continue;
            var shared = count > 1;
            var state = shared && entry.State is ("Ready" or "AlreadyWebp") ? "Shared" : entry.State;
            var reason = shared && state == "Shared" ? "SharedReference" : entry.Reason;
            entries[index] = new ArtworkInventoryEntry
            {
                Reference = entry.Reference,
                Name = entry.Name,
                ItemType = entry.ItemType,
                ProductionYear = entry.ProductionYear,
                Category = entry.Category,
                State = state,
                Reason = reason,
                CurrentBytes = entry.CurrentBytes,
                MimeType = entry.MimeType,
                IsShared = shared,
                ReferenceCount = count,
            };
        }

        var eligible = entries.Where(entry => entry.State is "Ready" or "AlreadyWebp").ToArray();
        summary.EligibleImages = eligible.Length;
        summary.SharedImages = entries.Count(entry => entry.State == "Shared");
        summary.AlreadyWebpImages = eligible.Count(entry => Path.GetExtension(entry.Reference.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase));
        return entries.OrderBy(entry => entry.Reference.Key, StringComparer.Ordinal).ToArray();
    }

    private static bool IsAfterCursor(ArtworkReference reference, string? afterKey) =>
        string.IsNullOrEmpty(afterKey) || string.CompareOrdinal(reference.Key, afterKey) > 0;

    private (string State, string? Reason) Classify(BaseItem item, ArtworkReference reference, bool siblingsSafe)
    {
        if (!File.Exists(reference.Path)) return ("Missing", "MissingFile");
        var extension = Path.GetExtension(reference.Path);
        if (reference.Type == ImageType.Chapter
            || !(extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
            return ("Excluded", "UnsupportedFormat");
        if (item.SourceType != SourceType.Library) return ("Excluded", "NotLibrary");
        var root = Path.GetFullPath(configuration.ApplicationPaths.InternalMetadataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(reference.Path).StartsWith(root, PathComparison)) return ("Excluded", "ExternalPath");
        if (!siblingsSafe) return ("Excluded", "OtherSlotUnavailable");
        return extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ? ("AlreadyWebp", null) : ("Ready", null);
    }

    private static string? MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => null,
    };

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
    public bool IsPathReferenced(string path) => CountPathReferences(path) > 0;

    /// <inheritdoc />
    public int CountPathReferences(string path)
    {
        var absolute = Path.GetFullPath(path);
        return CountPathReferences([absolute]).GetValueOrDefault(absolute);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> CountPathReferences(IReadOnlyCollection<string> paths)
    {
        var absolutePaths = paths.Select(Path.GetFullPath).ToHashSet(PathComparer);
        var counts = absolutePaths.ToDictionary(path => path, _ => 0, PathComparer);
        if (absolutePaths.Count == 0) return counts;
        foreach (var items in InventoryBatches(CancellationToken.None))
        {
            foreach (var image in items.SelectMany(item => item.ImageInfos))
            {
                if (!image.IsLocalFile) continue;
                var absolute = Path.GetFullPath(image.Path);
                if (counts.ContainsKey(absolute)) counts[absolute]++;
            }
        }

        return counts;
    }

    /// <inheritdoc />
    public bool IsSlotSafeForMutation(ArtworkReference reference)
    {
        var item = library.GetItemById(reference.ItemId);
        if (item is null || item.SourceType != SourceType.Library) return false;
        var target = Path.GetFullPath(reference.Path);
        foreach (var image in item.ImageInfos)
        {
            // A remote sibling cannot be safely refreshed/persisted by SaveImage.
            if (!image.IsLocalFile) return false;
            // The current slot may be absent only for explicit recovery; any other
            // absent sibling makes UpdateImagesAsync potentially mutate another slot.
            if (!File.Exists(image.Path) && !string.Equals(Path.GetFullPath(image.Path), target, PathComparison)) return false;
        }

        return true;
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

    private IReadOnlyList<BaseItem> ReadItems(Guid[] ids)
    {
        var items = library.GetItemList(new InternalItemsQuery
        {
            ItemIds = ids,
            IncludeOwnedItems = true,
            GroupByPresentationUniqueKey = false,
            EnableTotalRecordCount = false,
            DtoOptions = InventoryOptions(),
        }).ToArray();
        // Jellyfin's GetItemList applies an include-order (by ImageInfo Id) that
        // differs from GetItemById/GetImageInfo and SaveImage. For items with
        // multiple slots of one type, rehydrate through the same loader used by
        // GetCurrent before assigning any slot index. Never sort by filename.
        for (var index = 0; index < items.Length; index++)
        {
            if (!HasDuplicateImageType(items[index].ImageInfos)) continue;
            var complete = library.GetItemById(items[index].Id);
            if (complete is not null) items[index] = complete;
        }

        return items;
    }

    private static bool HasDuplicateImageType(IEnumerable<ItemImageInfo?> images)
    {
        var seen = new HashSet<ImageType>();
        foreach (var image in images)
            if (image is not null && !seen.Add(image.Type)) return true;
        return false;
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
        // Invalidate before the attempt and again in finally, including failed saves.
        InvalidateAnalysisSnapshot();
        try
        {
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
        finally
        {
            InvalidateAnalysisSnapshot();
        }
    }

    private bool IsEligible(BaseItem item, ArtworkReference reference, bool? siblingsSafe = null)
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
        return siblingsSafe ?? item.ImageInfos.All(i => i.IsLocalFile && File.Exists(i.Path));
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
