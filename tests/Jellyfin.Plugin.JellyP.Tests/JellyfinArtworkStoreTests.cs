using System.Reflection;
using Jellyfin.Plugin.JellyP.Infrastructure;
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

    public JellyfinArtworkStoreTests() => Directory.CreateDirectory(_root);

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
        Assert.Equal(new[] { 500, 1 }, _requestedBatches.Select(b => b.Length));
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

    private JellyfinArtworkStore CreateStore()
    {
        var paths = Proxy<IServerApplicationPaths>((method, _) => method.Name == "get_InternalMetadataPath" ? Path.Combine(_root, "metadata") : throw new NotSupportedException(method.Name));
        var configuration = Proxy<IServerConfigurationManager>((method, _) => method.Name == "get_ApplicationPaths" ? paths : throw new NotSupportedException(method.Name));
        var library = Proxy<ILibraryManager>((method, args) => method.Name switch
        {
            "get_IsScanRunning" => false,
            "GetItemIds" when args is [{ } query] => Snapshot((InternalItemsQuery)query),
            "GetItemList" when args is [{ } query] => Batch((InternalItemsQuery)query),
            _ => throw new NotSupportedException(method.Name),
        });
        var providers = Proxy<IProviderManager>((method, _) => method.Name switch
        {
            "GetRefreshQueue" => new HashSet<Guid>(),
            _ => throw new NotSupportedException(method.Name),
        });
        return new JellyfinArtworkStore(library, providers, configuration);
    }

    private IReadOnlyList<Guid> Snapshot(InternalItemsQuery query)
    {
        _snapshotCalls++;
        Assert.True(query.IncludeOwnedItems);
        Assert.False(query.GroupByPresentationUniqueKey);
        Assert.Null(query.StartIndex);
        Assert.Null(query.Limit);
        Assert.False(query.EnableTotalRecordCount);
        Assert.False(query.DtoOptions.EnableImages);
        Assert.False(query.DtoOptions.EnableUserData);
        Assert.Empty(query.DtoOptions.Fields);
        return _snapshotIds ?? _items.Select(i => i.Id).ToArray();
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
        return _items.Where(i => query.ItemIds.Contains(i.Id)).Reverse().ToArray();
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
