using System.Reflection;
using Jellyfin.Plugin.JellyP.Infrastructure;
using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class JellyfinArtworkStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyp-inventory-" + Guid.NewGuid().ToString("N"));
    private readonly List<BaseItem> _items = [];
    private Guid[]? _snapshotIds;
    private readonly List<Guid[]> _requestedBatches = [];
    private int _snapshotCalls;
    private readonly Dictionary<Guid, CollectionFolder> _libraries = [];
    private readonly Dictionary<Guid, Guid[]> _libraryMembers = [];
    private readonly Dictionary<Guid, IReadOnlyList<PersonInfo>> _people = [];
    private readonly Dictionary<string, Guid> _personIds = [];
    private bool _scanning;
    private bool _sortListImages;
    private Action? _onSnapshot;
    private readonly HashSet<Guid> _refreshQueue = [];
    private readonly HashSet<Guid> _activeRefreshes = [];

    public JellyfinArtworkStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SelectedItemWithoutImagesCompletesWithoutGlobalInventory()
    {
        var item = new Person { Id = Guid.NewGuid(), Name = "Synthetic empty item" };
        _items.Add(item);
        var store = CreateStore();
        Assert.Empty(store.Inventory(default, [item.Id]));
        Assert.Equal(0, _snapshotCalls);
        Assert.Single(_requestedBatches);
        Assert.Equal(1, store.GetInventorySummary()!.MatchedItems);
        Assert.Equal(1, store.GetInventorySummary()!.ItemsWithoutImages);
        Assert.Equal(0, store.GetInventorySummary()!.LocalImages);
        Assert.Empty(Assert.Single(store.InspectItems([item.Id], default)).Images);
    }

    [Fact]
    public void RemoteOnlySelectionIsExplainedWithoutDownloadsOrGlobalInventory()
    {
        var item = new Person { Id = Guid.NewGuid(), ImageInfos = [new ItemImageInfo
            { Type = ImageType.Primary, Path = "https://example.invalid/synthetic.jpg" }] };
        _items.Add(item);
        var store = CreateStore();
        Assert.Empty(store.Inventory(default, [item.Id]));
        Assert.Equal(0, _snapshotCalls);
        Assert.Equal(1, store.GetInventorySummary()!.RemoteImages);
        Assert.Equal(0, store.GetInventorySummary()!.ItemsWithoutImages);
        var image = Assert.Single(Assert.Single(store.InspectItems([item.Id], default)).Images);
        Assert.Equal("Remote", image.State);
        Assert.Equal("People", image.Category);
    }

    [Fact]
    public void InspectionDistinguishesReadyWebpMissingExternalAndAbsentItems()
    {
        var ready = PersonWithImage(Path.Combine(_root, "metadata", "ready.jpg"));
        var webp = PersonWithImage(Path.Combine(_root, "metadata", "current.webp"));
        var missing = new Person { Id = Guid.NewGuid(), ImageInfos = [new ItemImageInfo
            { Type = ImageType.Primary, Path = Path.Combine(_root, "absent.jpg") }] };
        _items.Add(missing);
        var external = PersonWithImage(Path.Combine(_root, "media", "external.png"));
        var absent = Guid.NewGuid();
        var result = CreateStore().InspectItems([ready.Id, webp.Id, missing.Id, external.Id, absent], default);
        Assert.Equal(new[] { "Ready", "AlreadyWebp", "Missing", "Excluded" }, result.Take(4).Select(item => Assert.Single(item.Images).State));
        Assert.False(result.Last().Found);
        Assert.Empty(result.Last().Images);
        Assert.Equal(0, _snapshotCalls);
    }

    [Fact]
    public void RepeatedImageTypesUseNativeOrderAcrossInventoryDetailedAndInspection()
    {
        var paths = new[]
        {
            Path.Combine(_root, "metadata", "slots", "primary.jpg"),
            Path.Combine(_root, "metadata", "slots", "backdrop-a.jpg"),
            Path.Combine(_root, "metadata", "slots", "backdrop-b.jpg"),
            Path.Combine(_root, "metadata", "slots", "backdrop-c.jpg"),
        };
        foreach (var path in paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
        }

        var item = new Person
        {
            Id = Guid.NewGuid(),
            ImageInfos =
            [
                new ItemImageInfo { Type = ImageType.Primary, Path = paths[0] },
                new ItemImageInfo { Type = ImageType.Backdrop, Path = paths[1] },
                new ItemImageInfo { Type = ImageType.Backdrop, Path = paths[2] },
                new ItemImageInfo { Type = ImageType.Backdrop, Path = paths[3] },
            ],
        };
        _items.Add(item);
        _snapshotIds = [item.Id];
        _sortListImages = true;
        var store = CreateStore();

        foreach (var type in new[] { ImageType.Primary, ImageType.Backdrop })
        {
            var native = item.ImageInfos.Where(image => image.Type == type).Select((image, index) =>
                new ArtworkReference(item.Id, type, index, image.Path)).ToArray();
            var inventory = store.Inventory(default, [item.Id]).Where(reference => reference.Type == type).ToArray();
            var detailed = store.DetailedInventory(default, [item.Id]).Where(entry => entry.Reference.Type == type)
                .Select(entry => entry.Reference).ToArray();
            var inspected = store.InspectItems([item.Id], default).Single().Images
                .Where(image => image.Type == type).Select(image => image.Index).ToArray();

            Assert.Equal(native.Select(reference => reference.Path), inventory.Select(reference => reference.Path));
            Assert.Equal(native.Select(reference => reference.Index), inventory.Select(reference => reference.Index));
            Assert.Equal(native.Select(reference => reference.Path), detailed.Select(reference => reference.Path));
            Assert.Equal(native.Select(reference => reference.Index), detailed.Select(reference => reference.Index));
            Assert.Equal(native.Select(reference => reference.Index), inspected);
            foreach (var reference in inventory)
            {
                var current = store.GetCurrent(reference);
                Assert.NotNull(current);
                Assert.Equal(reference.Path, current!.Path);
                Assert.Equal(reference.Index, current.Index);
            }
        }
    }

    [Fact]
    public void LegacyPeopleArtworkIsEligibleOutsideTheCurrentCanonicalItemDirectory()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "People", "E", "Example Person", "folder.jpg"));
        var result = CreateStore().Inventory(default);
        var image = Assert.Single(result);
        Assert.Equal(item.Id, image.ItemId);
        Assert.Equal(item.ImageInfos[0].Path, image.Path);
        Assert.True(image.IsPerson);
    }

    [Fact]
    public void MetadataPrefixSiblingAndMediaFolderRemainExcluded()
    {
        PersonWithImage(Path.Combine(_root, "metadata-external", "folder.jpg"));
        PersonWithImage(Path.Combine(_root, "media", "folder.jpg"));
        Assert.Empty(CreateStore().Inventory(default));
    }

    [Fact]
    public void SharedReferenceOutsideTheCanaryStillBlocksDeletion()
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "People", "E", "Example Person", "folder.jpg"));
        var other = new ChannelFixture
        {
            Id = Guid.NewGuid(), ImageInfos = [new ItemImageInfo { Path = selected.ImageInfos[0].Path, Type = ImageType.Primary }],
        };
        _items.Add(other);
        Assert.Empty(CreateStore().Inventory(default, [selected.Id]));
    }

    [Fact]
    public void MissingSiblingReferenceKeepsTheEntireItemIneligible()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "People", "E", "Example Person", "folder.jpg"));
        item.ImageInfos = [.. item.ImageInfos, new ItemImageInfo { Path = Path.Combine(_root, "missing.jpg"), Type = ImageType.Backdrop }];
        Assert.Empty(CreateStore().Inventory(default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(499)]
    public void UnknownTypesDiscardedDuringHydrationDoNotHideLaterImages(int materializedFirstBatch)
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "People", "E", "Example Person", "folder.jpg"));
        var first = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray();
        _items.AddRange(first.Take(materializedFirstBatch).Select(id => new ChannelFixture { Id = id }));
        _snapshotIds = [.. first, selected.Id];
        var store = CreateStore();
        Assert.Equal(selected.Id, Assert.Single(store.Inventory(default)).ItemId);
        Assert.Equal(1, _snapshotCalls);
        Assert.Equal(new[] { 500, 1 }, _requestedBatches.Select(b => b.Length));
        _requestedBatches.Clear();
        Assert.True(store.IsPathReferenced(selected.ImageInfos[0].Path));
        Assert.Equal(2, _snapshotCalls);
        Assert.Equal(new[] { 500, 1 }, _requestedBatches.Select(b => b.Length));
    }

    [Fact]
    public void OwnedReferenceInLaterBatchStillBlocksTheSelectedImage()
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "People", "E", "Example Person", "folder.jpg"));
        var owned = new ChannelFixture
        {
            Id = Guid.NewGuid(), OwnerId = selected.Id,
            ImageInfos = [new ItemImageInfo { Path = selected.ImageInfos[0].Path, Type = ImageType.Primary }],
        };
        _items.Add(owned);
        _snapshotIds = [selected.Id, .. Enumerable.Range(0, 499).Select(_ => Guid.NewGuid()), owned.Id];
        Assert.Empty(CreateStore().Inventory(default, [selected.Id]));
        Assert.Equal(new[] { 1, 500, 1 }, _requestedBatches.Select(b => b.Length));
    }

    [Fact]
    public void SnapshotOrderAndHydrationOrderDoNotChangeCompleteness()
    {
        for (var i = 0; i < 503; i++)
        {
            var item = PersonWithImage(Path.Combine(_root, "metadata", "People", i.ToString(), "folder.jpg"));
            item.Name = "Identical name";
        }
        _snapshotIds = _items.Select(i => i.Id).Reverse().ToArray();
        var result = CreateStore().Inventory(default);
        Assert.Equal(503, result.Count);
        Assert.Equal(_snapshotIds.Order(), result.Select(i => i.ItemId).Order());
        Assert.Equal(_snapshotIds, _requestedBatches.SelectMany(b => b));
        Assert.Equal(result.OrderBy(r => r.Key, StringComparer.Ordinal), result);
    }

    [Fact]
    public void CancellationBeforeSnapshotDoesNotQueryLibrary()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CreateStore().Inventory(cancellation.Token));
        Assert.Equal(0, _snapshotCalls);
    }

    [Fact]
    public void LibraryScopeIncludesDescendantsOwnedItemsAndAssociatedPeopleButNotOtherLibraries()
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "selected", "folder.jpg"));
        var related = PersonWithImage(Path.Combine(_root, "metadata", "related", "folder.jpg"));
        var other = PersonWithImage(Path.Combine(_root, "metadata", "other", "folder.jpg"));
        var folder = AddLibrary([selected.Id]);
        _people[selected.Id] = new[] { new PersonInfo { Name = "Related Person" } };
        _personIds["Related Person"] = related.Id;
        var result = CreateStore().Inventory(default, includedLibraryIds: [folder.Id]);
        Assert.Equal(new[] { selected.Id, related.Id }.Order(), result.Select(i => i.ItemId).Order());
        Assert.DoesNotContain(result, image => image.ItemId == other.Id);
    }

    [Fact]
    public void DetailedInventoryAndPageHonorMaximumWithOneOverLimitMarker()
    {
        for (var index = 0; index < 12; index++)
            _items.Add(PersonWithImage(Path.Combine(_root, "metadata", "bounded", index.ToString(), "folder.jpg")));
        var store = CreateStore();
        var bounded = store.DetailedInventory(default, maximumEntries: 10);
        Assert.Equal(11, bounded.Count);
        var cursor = bounded[4].Reference.Key;
        var page = store.DetailedInventoryPage(default, afterKey: cursor, maximumEntries: 3);
        Assert.Equal(4, page.Count);
        Assert.All(page, entry => Assert.True(string.CompareOrdinal(entry.Reference.Key, cursor) > 0));
    }

    [Fact]
    public void PagedInventoryUnionIsCompleteOrderedAndHasNoSkipsWhenHydrationOrderDiffers()
    {
        var ids = new[]
        {
            new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        };
        foreach (var id in ids)
        {
            var root = Path.Combine(_root, "metadata", "ordered", id.ToString("N"));
            Directory.CreateDirectory(root);
            var paths = new[] { "primary.jpg", "backdrop-0.jpg", "backdrop-1.jpg" };
            foreach (var name in paths) File.WriteAllBytes(Path.Combine(root, name), [1, 2, 3]);
            _items.Add(new Person
            {
                Id = id,
                Path = root,
                ImageInfos =
                [
                    new ItemImageInfo { Type = ImageType.Primary, Path = Path.Combine(root, paths[0]) },
                    new ItemImageInfo { Type = ImageType.Backdrop, Path = Path.Combine(root, paths[1]) },
                    new ItemImageInfo { Type = ImageType.Backdrop, Path = Path.Combine(root, paths[2]) },
                ],
            });
        }

        _snapshotIds = ids; // deliberately not key order
        var store = CreateStore();
        var full = store.DetailedInventory(default).OrderBy(entry => entry.Reference.Key, StringComparer.Ordinal)
            .Select(entry => entry.Reference.Key).ToArray();
        var seen = new List<string>();
        string? cursor = null;
        for (var part = 0; part < 20; part++)
        {
            var page = store.DetailedInventoryPage(default, afterKey: cursor, maximumEntries: 2);
            if (page.Count == 0) break;
            seen.AddRange(page.Select(entry => entry.Reference.Key));
            cursor = page[^1].Reference.Key;
            if (page.Count <= 2) break;
        }

        Assert.Equal(full, seen);
        Assert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PageCutsAfterOrderingTypesSoLaterLowerKeyDoesNotDisappear()
    {
        var id = Guid.NewGuid();
        var root = Path.Combine(_root, "metadata", "type-order", id.ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new[] { "backdrop.jpg", "logo.png", "primary.jpg" };
        foreach (var name in paths) File.WriteAllBytes(Path.Combine(root, name), [1, 2, 3]);
        _items.Add(new Person
        {
            Id = id,
            Path = root,
            ImageInfos =
            [
                new ItemImageInfo { Type = ImageType.Backdrop, Path = Path.Combine(root, paths[0]) },
                new ItemImageInfo { Type = ImageType.Logo, Path = Path.Combine(root, paths[1]) },
                new ItemImageInfo { Type = ImageType.Primary, Path = Path.Combine(root, paths[2]) },
            ],
        });
        _snapshotIds = [id];
        var store = CreateStore();
        var first = store.DetailedInventoryPage(default, maximumEntries: 1);
        Assert.Equal(2, first.Count);
        Assert.Equal("Primary", first[0].Reference.Type.ToString());
        var second = store.DetailedInventoryPage(default, afterKey: first[^1].Reference.Key, maximumEntries: 1);
        Assert.Single(second);
        Assert.Equal("Logo", second[0].Reference.Type.ToString());
        var all = first.Concat(second).Select(entry => entry.Reference.Key).ToArray();
        Assert.Equal(all.OrderBy(key => key, StringComparer.Ordinal), all);
    }

    [Fact]
    public void AnalysisCacheHitDoesNotRenewItsFixedTimestamp()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "cache", "folder.jpg"));
        var store = CreateStore();
        Assert.Single(store.DetailedInventoryPage(default, maximumEntries: 10));
        var snapshots = _snapshotCalls;
        var captured = CachedSnapshotCapturedUtc(store);
        Assert.NotNull(captured);
        Assert.Single(store.DetailedInventoryPage(default, maximumEntries: 10));
        Assert.Equal(snapshots, _snapshotCalls);
        Assert.Equal(captured, CachedSnapshotCapturedUtc(store));
        Assert.Equal(item.Id, Assert.Single(store.DetailedInventoryPage(default, maximumEntries: 10)).Reference.ItemId);
    }

    [Fact]
    public void AnalysisPageDoesNotPublishAPassThatObservedAScanStart()
    {
        PersonWithImage(Path.Combine(_root, "metadata", "scan", "folder.jpg"));
        var store = CreateStore();
        _onSnapshot = () => _scanning = true;
        // The current read-only pass may complete during a scan, but it must not
        // become an advisory cache entry that survives the scan.
        Assert.Single(store.DetailedInventoryPage(default, maximumEntries: 10));
        _scanning = false;
        _onSnapshot = null;
        var snapshots = _snapshotCalls;
        Assert.Single(store.DetailedInventoryPage(default, maximumEntries: 10));
        Assert.True(_snapshotCalls > snapshots);
    }

    [Fact]
    public void AnalysisPageRebuildsWhenTheAdvisoryCountsAreMissingAKey()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "missing-key", "folder.jpg"));
        var store = CreateStore();
        Assert.Single(store.DetailedInventoryPage(default, maximumEntries: 10));
        var snapshots = _snapshotCalls;
        RemoveCachedPath(store, item.ImageInfos[0].Path);
        Assert.Single(store.DetailedInventoryPage(default, maximumEntries: 10));
        Assert.True(_snapshotCalls > snapshots);
    }

    [Fact]
    public void AnalysisPageHydratesOnlyTheRequestedRemainderAfterTheFirstReferencePass()
    {
        var ids = Enumerable.Range(0, 100_000).Select(index => new Guid(index, 0, 0, new byte[8])).ToArray();
        _items.AddRange(ids.Select(id => new Person
        {
            Id = id,
            ImageInfos = [new ItemImageInfo
            {
                Type = ImageType.Primary,
                Path = Path.Combine(_root, "metadata", "large", id.ToString("N"), "missing.png"),
            }],
        }));
        _snapshotIds = ids;
        var store = CreateStore();
        var first = store.DetailedInventoryPage(default, maximumEntries: 1000);
        Assert.Equal(1001, first.Count);
        _requestedBatches.Clear();
        var second = store.DetailedInventoryPage(default, afterKey: first[^1].Reference.Key, maximumEntries: 1000);
        Assert.Equal(1001, second.Count);
        var hydrated = _requestedBatches.Sum(batch => batch.Length);
        Assert.InRange(hydrated, 1, 1500);
    }

    [Fact]
    public void DetailedInventoryExplainsSharedReferenceAndKeepsLegacyAggregatesEquivalent()
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "selected", "folder.webp"));
        var outside = new ChannelFixture { Id = Guid.NewGuid(), ImageInfos = selected.ImageInfos };
        _items.Add(outside);
        var store = CreateStore();
        var detailed = store.DetailedInventory(default, [selected.Id]);
        var entry = Assert.Single(detailed);
        Assert.Equal("Shared", entry.State);
        Assert.Equal("SharedReference", entry.Reason);
        var summary = store.GetInventorySummary()!;
        Assert.Equal(1, summary.LocalImages);
        Assert.Equal(0, summary.ExcludedImages);
        Assert.Equal(1, summary.SharedImages);
        Assert.Empty(store.Inventory(default, [selected.Id]));
    }

    [Fact]
    public void SlotSafetyRejectsMissingAndRemoteSiblings()
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "selected", "folder.jpg"));
        var store = CreateStore();
        var reference = new ArtworkReference(selected.Id, ImageType.Primary, 0, selected.ImageInfos[0].Path);
        Assert.True(store.IsSlotSafeForMutation(reference));
        selected.ImageInfos = [.. selected.ImageInfos, new ItemImageInfo
        {
            Path = Path.Combine(_root, "metadata", "selected", "missing.jpg"),
            Type = ImageType.Backdrop,
        }];
        Assert.False(store.IsSlotSafeForMutation(reference));
        selected.ImageInfos = [.. selected.ImageInfos.Take(1), new ItemImageInfo
        {
            Path = "https://example.invalid/remote.jpg",
            Type = ImageType.Backdrop,
        }];
        Assert.False(store.IsSlotSafeForMutation(reference));
    }

    [Fact]
    public void DetailedInventorySharedFlagIsIndependentOfEligibilityClassification()
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "selected", "folder.jpg"));
        selected.ImageInfos = [.. selected.ImageInfos, new ItemImageInfo
        {
            Path = Path.Combine(_root, "metadata", "selected", "missing-backdrop.jpg"),
            Type = ImageType.Backdrop,
        }];
        var outside = new ChannelFixture { Id = Guid.NewGuid(), ImageInfos = [selected.ImageInfos[0]] };
        _items.Add(outside);
        var entries = CreateStore().DetailedInventory(default, [selected.Id]);
        var entry = Assert.Single(entries, row => row.Reference.Path == selected.ImageInfos[0].Path);
        Assert.Equal("Excluded", entry.State);
        Assert.True(entry.IsShared);
        Assert.Equal(2, entry.ReferenceCount);
    }

    [Fact]
    public void DetailedInventoryCountsMissingLocalImagesAsExcludedLikeLegacyInventory()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "selected", "missing.png"));
        File.Delete(item.ImageInfos[0].Path);
        var store = CreateStore();
        var detailed = store.DetailedInventory(default, [item.Id]);
        Assert.Equal("Missing", Assert.Single(detailed).State);
        var summary = store.GetInventorySummary()!;
        Assert.Equal(1, summary.LocalImages);
        Assert.Equal(1, summary.ExcludedImages);
    }

    [Fact]
    public void SharingAcrossExcludedLibrariesStillProtectsTheImage()
    {
        var selected = PersonWithImage(Path.Combine(_root, "metadata", "selected", "folder.jpg"));
        var outside = new ChannelFixture { Id = Guid.NewGuid(), ImageInfos = selected.ImageInfos };
        _items.Add(outside);
        var folder = AddLibrary([selected.Id]);
        var store = CreateStore();
        Assert.Empty(store.Inventory(default, includedLibraryIds: [folder.Id]));
        Assert.Equal(1, store.GetInventorySummary()!.SharedImages);
    }

    [Fact]
    public void EmptyOrDeletedLibrarySelectionNeverExpandsToTheWholeServer()
    {
        PersonWithImage(Path.Combine(_root, "metadata", "sample", "folder.jpg"));
        Assert.Empty(CreateStore().Inventory(default, includedLibraryIds: []));
        Assert.Throws<InvalidOperationException>(() => CreateStore().Inventory(default, includedLibraryIds: [Guid.NewGuid()]));
        var empty = AddLibrary([]);
        Assert.Empty(CreateStore().Inventory(default, includedLibraryIds: [empty.Id]));
    }

    [Fact]
    public void ReadOnlyInventoryReportsProgressAndReasonsWhileMetadataIsBusy()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "sample", "folder.webp"));
        _scanning = true; _refreshQueue.Add(Guid.NewGuid());
        var progress = new List<int>();
        var store = CreateStore();
        Assert.Single(store.Inventory(default, [item.Id], inventoryProgress: progress.Add));
        Assert.Equal(new[] { 1 }, progress);
        var summary = store.GetInventorySummary()!;
        Assert.Equal(1, summary.TotalItems);
        Assert.Equal(1, summary.MatchedItems);
        Assert.Equal(1, summary.AlreadyWebpImages);
        Assert.Throws<InvalidOperationException>(() => store.EnsureIdle());
    }

    [Fact]
    public void ScopeValidationFailsClosedAndLegacyPreservesExistingCanaries()
    {
        var config = new Jellyfin.Plugin.JellyP.Configuration.PluginConfiguration { ScopeMode = "libraries" };
        Assert.Throws<ArgumentException>(config.Validate);
        config.IncludedLibraryIds = [Guid.NewGuid()]; config.Validate();
        config.ScopeMode = "items"; Assert.Throws<ArgumentException>(config.Validate);
        config.IncludedItemIds = [Guid.NewGuid()]; config.Validate();
        config.ScopeMode = "legacy"; config.IncludedLibraryIds = []; config.Validate();
    }

    [Fact]
    public void UnrelatedRefreshDoesNotBlockButTargetLibraryAncestorAndScanDo()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "target", "folder.jpg"));
        var folder = AddLibrary([item.Id]);
        var store = CreateStore();
        _refreshQueue.Add(Guid.NewGuid());
        store.EnsureIdle(item.Id);
        _refreshQueue.Add(folder.Id);
        Assert.Throws<InvalidOperationException>(() => store.EnsureIdle(item.Id));
        _refreshQueue.Remove(folder.Id);
        _activeRefreshes.Add(item.Id);
        Assert.Throws<InvalidOperationException>(() => store.EnsureIdle(item.Id));
        _activeRefreshes.Clear();
        item.ParentId = Guid.NewGuid(); _refreshQueue.Add(item.ParentId);
        Assert.Throws<InvalidOperationException>(() => store.EnsureIdle(item.Id));
        _refreshQueue.Clear(); _scanning = true;
        Assert.Throws<InvalidOperationException>(() => store.EnsureIdle(item.Id));
    }

    [Fact]
    public void UnrelatedRefreshProgressDoesNotBlockButRelatedProgressDoes()
    {
        var item = PersonWithImage(Path.Combine(_root, "metadata", "target-progress", "folder.jpg"));
        var store = CreateStore();
        _activeRefreshes.Add(Guid.NewGuid());
        store.EnsureIdle(item.Id);
        _activeRefreshes.Add(item.Id);
        Assert.Throws<InvalidOperationException>(() => store.EnsureIdle(item.Id));
    }

    private CollectionFolder AddLibrary(Guid[] members)
    {
        var folder = new CollectionFolder { Id = Guid.NewGuid(), PhysicalFolderIds = [Guid.NewGuid()] };
        _libraries[folder.Id] = folder; _libraryMembers[folder.PhysicalFolderIds[0]] = members;
        return folder;
    }

    private Person PersonWithImage(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]); // Inventory only checks identity/location/existence; decoding is a later bounded gate.
        var item = new Person
        {
            Id = Guid.NewGuid(), Path = Path.GetDirectoryName(path)!,
            ImageInfos = [new ItemImageInfo { Path = path, Type = ImageType.Primary }],
        };
        _items.Add(item);
        return item;
    }

    private static DateTime? CachedSnapshotCapturedUtc(JellyfinArtworkStore store)
    {
        var snapshot = typeof(JellyfinArtworkStore)
            .GetField("_analysisSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store);
        return snapshot?.GetType().GetProperty("CapturedUtc")!.GetValue(snapshot) as DateTime?;
    }

    private static void RemoveCachedPath(JellyfinArtworkStore store, string path)
    {
        var snapshot = typeof(JellyfinArtworkStore)
            .GetField("_analysisSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store)!;
        var counts = (Dictionary<string, int>)snapshot.GetType().GetProperty("Counts")!.GetValue(snapshot)!;
        counts.Remove(Path.GetFullPath(path));
    }

    private JellyfinArtworkStore CreateStore()
    {
        var paths = Proxy<IServerApplicationPaths>((method, _) => method.Name == "get_InternalMetadataPath" ? Path.Combine(_root, "metadata") : throw new NotSupportedException(method.Name));
        var configuration = Proxy<IServerConfigurationManager>((method, _) => method.Name == "get_ApplicationPaths" ? paths : throw new NotSupportedException(method.Name));
        var library = Proxy<ILibraryManager>((method, args) => method.Name switch
        {
            "get_IsScanRunning" => _scanning,
            "GetVirtualFolders" => _libraries.Values.Select(folder => new VirtualFolderInfo { ItemId = folder.Id.ToString("N"), Name = "Synthetic library" }).ToList(),
            "GetItemById" => _items.FirstOrDefault(item => item.Id == (Guid)args![0]!) ?? (BaseItem?)_libraries.GetValueOrDefault((Guid)args![0]!),
            "GetCollectionFolders" => _libraries.Values.Where(folder => _libraryMembers[folder.PhysicalFolderIds[0]].Contains(((BaseItem)args![0]!).Id)).Cast<Folder>().ToList(),
            "GetPeopleByItems" => _people.Where(pair => ((IReadOnlyList<Guid>)args![0]!).Contains(pair.Key)).ToDictionary(),
            "GetPersonId" => _personIds[(string)args![0]!],
            "GetItemIds" when args is [{ } query] => Snapshot((InternalItemsQuery)query),
            "GetItemList" when args is [{ } query] => Batch((InternalItemsQuery)query),
            _ => throw new NotSupportedException(method.Name),
        });
        var providers = Proxy<IProviderManager>((method, args) => method.Name switch
        {
            "GetRefreshQueue" => _refreshQueue,
            "GetRefreshProgress" => _activeRefreshes.Contains((Guid)args![0]!) ? (double?)0.5 : null,
            _ => throw new NotSupportedException(method.Name),
        });
        return new JellyfinArtworkStore(library, providers, configuration);
    }

    private IReadOnlyList<Guid> Snapshot(InternalItemsQuery query)
    {
        _snapshotCalls++;
        _onSnapshot?.Invoke();
        Assert.True(query.IncludeOwnedItems);
        Assert.False(query.GroupByPresentationUniqueKey);
        Assert.Null(query.StartIndex);
        Assert.Null(query.Limit);
        Assert.False(query.EnableTotalRecordCount);
        Assert.False(query.DtoOptions.EnableImages);
        Assert.False(query.DtoOptions.EnableUserData);
        Assert.Empty(query.DtoOptions.Fields);
        return query.TopParentIds.Length > 0
            ? query.TopParentIds.SelectMany(id => _libraryMembers.GetValueOrDefault(id) ?? []).ToArray()
            : _snapshotIds ?? _items.Select(i => i.Id).ToArray();
    }

    private IReadOnlyList<BaseItem> Batch(InternalItemsQuery query)
    {
        Assert.True(query.IncludeOwnedItems);
        Assert.False(query.GroupByPresentationUniqueKey);
        Assert.Null(query.StartIndex);
        Assert.Null(query.Limit);
        Assert.False(query.EnableTotalRecordCount);
        Assert.InRange(query.ItemIds.Length, 1, 500);
        Assert.True(query.DtoOptions.EnableImages);
        Assert.False(query.DtoOptions.EnableUserData);
        Assert.Equal(ItemFields.Path, Assert.Single(query.DtoOptions.Fields));
        _requestedBatches.Add(query.ItemIds);
        // SQL hydration order need not match snapshot order, and unknown types yield no object.
        var selected = _items.Where(i => query.ItemIds.Contains(i.Id)).Reverse().ToArray();
        if (!_sortListImages) return selected;
        return selected.Select(item => new Person
        {
            Id = item.Id,
            Name = item.Name,
            ProductionYear = item.ProductionYear,
            ImageInfos = item.ImageInfos.Reverse().ToArray(),
        }).ToArray();
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class CallbackProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler!(targetMethod!, args);
    }

    private sealed class ChannelFixture : BaseItem
    {
        public override SourceType SourceType => SourceType.Channel;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
