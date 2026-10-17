# Jellyfin 12.1 integration evidence

The integration was checked against the official **v12.1** source tag (the
NuGet version is **12.1.0**). This document distinguishes upstream guarantees
from safeguards the plugin must implement. It is not production validation.

## Supported integration and discovery

Use a bounded `IScheduledTask` for existing and subsequently downloaded artwork.
No global image-transform hook was found in the inspected provider save path:
`ProviderManager.SaveImage` directly constructs `ImageSaver`. A scheduled pass
therefore optimizes new artwork after it has been saved; it does not intercept
downloads. Do not patch methods or install a racing filesystem watcher.

The task exposes `Name`, `Key`, `Description`, `Category`,
`Task ExecuteAsync(IProgress<double>, CancellationToken)`, and
`IEnumerable<TaskTriggerInfo> GetDefaultTriggers()`. Jellyfin discovers concrete
`IScheduledTask` exports during `ApplicationHost.RunStartupTasksAsync`.
`IPluginServiceRegistrator.RegisterServices(IServiceCollection,
IServerApplicationHost)` registers shared plugin services and requires a
parameterless registrator constructor. Use one singleton coordinator to serialize
all plugin tasks and API operations; this does not lock Jellyfin itself.

Inventory APIs:

- `ILibraryManager.GetItemsResult(InternalItemsQuery)` returns
  `QueryResult<BaseItem>`; `GetItemList(InternalItemsQuery)` returns
  `IReadOnlyList<BaseItem>`.
- Query properties include `int? StartIndex`, `int? Limit`, `bool Recursive`,
  `ImageType[] ImageTypes`, `DtoOptions`, and
  `IReadOnlyList<(ItemSortBy OrderBy, SortOrder SortOrder)> OrderBy`.
- Disable `GroupByPresentationUniqueKey` and enable `IncludeOwnedItems` when
  inventorying every stored item. Otherwise owned rows and alternate versions
  can be hidden. `SortName` does not provide an ID tiebreaker. JellyP captures
  `GetItemIds` once, then hydrates bounded `ItemIds` batches with both flags,
  rather than repeatedly querying sorted offsets. Unknown types discarded
  during hydration cannot shorten the ID snapshot. The snapshot fixes membership
  for a pass; it does not lock later image changes or newly inserted items.
- `BaseItem.ImageInfos` contains `ItemImageInfo[]`; each entry has path, type,
  modification date, dimensions, and blurhash. Determine the index within its
  image type, not its position in the complete array. Skip chapter images:
  `SetImagePath` rejects them. `IsLocalFile` merely checks that the path does
  not begin with `http`; apply stronger filesystem/path checks yourself.

## Save and persistence are separate operations

Supported calls:

```csharp
Task IProviderManager.SaveImage(BaseItem item, Stream source, string mimeType,
    ImageType type, int? imageIndex, CancellationToken cancellationToken);
Task ILibraryManager.UpdateImagesAsync(BaseItem item, bool forceUpdate = false);
```

Pass `image/webp` and the explicit original image index. Passing null for a
multi-image type appends an image. The saver takes ownership of/disposes the
stream. Its exact sequence is:

1. Derive output paths from MIME and item/library settings. Internal images
   usually become `poster.webp`, `folder.webp`, `backdrop.webp`, etc. Additional
   backdrops can receive a newly allocated numbered basename. Local settings
   can produce multiple copies (including extra thumbs).
2. Write each destination using `FileMode.Create`, `FileOptions.WriteThrough`.
   **This is not an atomic rename or a database transaction.** Existing output
   paths can be truncated. Refuse ambiguous/pre-existing destination collisions.
3. Update the in-memory image path and reset its dimensions to zero.
4. Delete the previous local image when the save is local, or the old path
   contains the configured internal metadata path. An external old file can
   remain when local saving is disabled. Cleanup failures other than missing
   files propagate after the in-memory path has already changed.

`SaveImage` does **not** persist image references to the database.
`UpdateImagesAsync` refreshes dimensions/blurhash/dates, validates references,
persists the item's images, and registers the item. Default `false` still
refreshes a just-saved image because its dimensions are zero. Force true
refreshes every image. Even false can fetch existing remote image references
and validate/remove other missing images. Reject such mixed/invalid items if
strictly bounded local migration is required. Dimension/hash failures are logged
and swallowed upstream: independently verify the output and resulting values.
Live TV items can take an early return without persistence; restrict the scope
to library items. The official upload controller instead calls
`item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, CancellationToken.None)`
after saving; this is a broader metadata update.

The filename overload that accepts `saveLocallyWithMedia` **deletes its input
file in a finally block**, even on failure. Never pass the sole backup or source
artwork to that overload. It is suitable only for a disposable staging copy.

## Failure recovery and coexistence

Before save, persist a journal and an original-byte backup, then recheck item,
image index/path, source hash and file state. Keep the backup through output
validation and reference persistence. Once a save starts, complete persistence
or recovery with a non-cancelled token; honor cancellation between transactions.
After an uncertain failure, preserve the backup and journal and stop the batch.
Do not automatically overwrite an image that another actor has since changed.
Recovery must restore bytes and the image reference through supported APIs;
restoring bytes alone is insufficient. A recovery save can choose a different
basename, especially for nonzero backdrops, so verify the actual returned item
path rather than assuming the original basename.

`ILibraryManager.IsScanRunning`, `IProviderManager.GetRefreshQueue()` and
`GetRefreshProgress(Guid)` expose known conflicts. In the pinned implementation,
GetRefreshQueue returns queued IDs; active progress lives separately. Simulation
is read-only and no longer requires idle. Before applying, JellyP waits for full
scans or a queued/active refresh of the target, its parent/owner chain or collection
folder. Unrelated queue entries do not block it. The wait is cancelable and respects any native task maximum runtime. Idle checks remain immediately before writes and retirement.

No public shared per-item mutation lock was found. ProviderManager's URL image
cache lock does not cover the filename/stream SaveImage path used by uploads and
JellyP. Optimistic checks do not exclude manual uploads that start between a check
and a write. Avoid simultaneous manual image uploads; do not claim atomic native
coordination or disable all Jellyfin scheduled tasks.

Library selection resolves virtual CollectionFolder IDs to PhysicalFolderIds for
TopParentIds queries, includes descendants/owned items and credited People, and
keeps global reference counting separate from that selection. People are global
entities and can be associated with multiple selected/unselected libraries.

Count actual reclaimed bytes only when the old file is gone and any plugin
backup has been retired. Keeping originals on the same filesystem consumes
space and defeats immediate reclamation. A bounded journal/backup budget and
free-space guard are essential; dry run does not delete files. Do not remove
unreferenced artwork broadly as part of conversion.

## Runtime packaging

Jellyfin 12.1 targets .NET 10 and pins SkiaSharp and Linux native assets to
3.119.4. Its drawing project already references both. Compile against that
version and exclude runtime SkiaSharp/server assemblies from the plugin package
so it uses the host's matching managed/native stack. A startup codec probe must
fail closed if native WebP support is unavailable. Linux x64 converter tests
do not establish Windows, macOS, ARM, or installation compatibility; validate
each supported host before claiming it. Do not bundle a second native Skia
library into a platform-neutral plugin archive.

## Primary sources

- [ProviderManager](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Providers/Manager/ProviderManager.cs)
- [ImageSaver](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Providers/Manager/ImageSaver.cs)
- [LibraryManager](https://github.com/jellyfin/jellyfin/blob/v12.1/Emby.Server.Implementations/Library/LibraryManager.cs)
- [BaseItem](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Controller/Entities/BaseItem.cs)
- [ImageController](https://github.com/jellyfin/jellyfin/blob/v12.1/Jellyfin.Api/Controllers/ImageController.cs)
- [InternalItemsQuery](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Controller/Entities/InternalItemsQuery.cs)
- [IProviderManager](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Controller/Providers/IProviderManager.cs)
- [IScheduledTask](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Model/Tasks/IScheduledTask.cs)
- [ApplicationHost](https://github.com/jellyfin/jellyfin/blob/v12.1/Emby.Server.Implementations/ApplicationHost.cs)
- [IPluginServiceRegistrator](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs)
- [Drawing project](https://github.com/jellyfin/jellyfin/blob/v12.1/src/Jellyfin.Drawing.Skia/Jellyfin.Drawing.Skia.csproj)
- [Pinned package versions](https://github.com/jellyfin/jellyfin/blob/v12.1/Directory.Packages.props)
