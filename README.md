# JellyP — Jellyfin Image Optimizer

JellyP is an independent Jellyfin 12.1 plugin for reducing internal library
artwork storage with configurable WebP policies. It includes a Jellyfin dashboard
page, resumable optimization and policy synchronization tasks, optional original
preservation, and verified rollback.

## Install from the Jellyfin catalog

Requires **Jellyfin 12.1.x**. In **Dashboard → Plugins → Repositories**, add
**JellyP** with this repository URL:

```text
https://raw.githubusercontent.com/thisfilipe/jellyp/catalog/manifest.json
```

Open **Catalog**, choose **JellyP**, install the compatible version, then restart
Jellyfin during an idle window. Open **Dashboard → Plugins → JellyP → Settings**.
The page starts in **Simulate** mode, with retained originals and no task schedule.
Choose **Português (Brasil)** or **English** using its language selector.
[Usage guide](docs/USAGE.md) · [Guia em português](docs/USAGE.pt-BR.md).

[Releases](https://github.com/thisfilipe/jellyp/releases) include the ZIP and SHA256
for manual installation. This is a third-party repository; adding it does not
add JellyP to Jellyfin's official repository. See Jellyfin's
[plugin documentation](https://jellyfin.org/docs/general/server/plugins/).

## Choose what changes

- Select **People, Primary, Thumb, Logo, Backdrop, Art, Banner, Disc, Box,
  BoxRear, and Menu** independently. People is an item-category override for
  person artwork. Primary, Thumb, Logo and People start selected; other
  categories start disabled.
- Use global encoding defaults or override each category's quality **0–100**,
  lossy/lossless mode, optional downscaling and SSIM gate. Quality 100 is still
  lossy unless **Lossless** is selected.
- Dimensions are preserved by default. Downscaling never upscales. Alpha samples
  must match exactly at the chosen output dimensions; lossless mode also checks
  every RGB sample.
- Preserve originals for restoration and future policy changes, or disable
  preservation to permanently remove each original **after** the output and
  Jellyfin reference verify. A transient recovery copy is always required until
  verification succeeds.
- Select **Only selected items**, search by name or paste Jellyfin details links,
  and start with a batch of one. An empty selection in this mode blocks running;
  **Whole library** must be selected explicitly. Selection is by individual item,
  not recursively by every descendant of a selected series or folder.

Defaults are deliberately inactive: **dry run on, artwork changes off, no task
schedule, original preservation on, no resizing**. Global quality is 85; minimum
windowed luma SSIM is 0.96, with at least 5% and 4096 bytes saved per image. These
are independent controls: lowering encoder quality does not disable the SSIM
gate. A rejected candidate leaves its source unchanged.

## Dashboard workflow

After installing, open **Dashboard → Plugins → JellyP → Settings**.

1. **Select images:** choose individual items or the whole library, then check
   image types. Expand **Settings for this type** only for individual overrides.
2. **Choose quality and storage:** keep dimensions unless downscaling is wanted.
   Retained originals support restoration and policy changes but initially use
   additional space. Removing originals is permanent after verification.
3. **Simulate or apply:** start in **Simulate**, then **Simulate next batch**.
   The button saves current settings before queuing; **Save settings** only saves.
4. Review automatic progress, savings and skip reasons. Choose **Apply**, pause
   uploads/metadata refreshes, confirm the maintenance window and, when removing
   originals, acknowledge permanence. **Convert next batch** applies a small batch.
5. Repeat the batch button to continue. **Stop after current image** finishes
   the current transaction safely. Changing selected items, enabled types or
   effective encoding settings restarts that mode's pass. Preview and apply
   positions are independent. Disabled types do not consume the batch limit.
6. Inspect images. **Restore original** reverses a retained transaction;
   **Remove verified stored originals…** permanently releases backup storage.
   Expiration can also be configured in advanced settings; zero days keeps them
   until manual removal. Expiration runs at the start of an applying batch.
7. Return to **Simulate** and save when maintenance ends. No schedule is created
   by the page; Jellyfin Scheduled Tasks uses the saved execution settings.

Both the optimizer and synchronization task process existing and subsequently
saved artwork. They do not intercept a download before Jellyfin saves it.

## Synchronizing changed policies

Change a selected category's policy, save, then run **Sync changed quality
settings**. The selected Simulate/Apply mode applies to synchronization too.

JellyP compares a fingerprint of the effective encoding settings, verifies that
current artwork still matches its journal and reads the preserved original. It
restores that original through Jellyfin services, then applies the new policy
through the normal verified conversion. A crash between the two phases leaves
a valid original for the next optimization pass. This avoids successive lossy
recompression. The optional “also regenerate unchanged policies” setting forces
regeneration from originals.

Images whose originals were retired are reported as **OriginalUnavailable**.
Changed external artwork is not overwritten. Unselecting a category leaves its
existing conversions alone; use restoration explicitly to undo them. Newly
selected categories are processed by the ordinary optimization task.

## Supported scope and limits

The migration currently accepts **static baseline/progressive JPEG and PNG** images already
referenced by Jellyfin library items and stored in Jellyfin's configured internal metadata
directory, including legacy People/Studio paths. The converter also understands static WebP; policy synchronization
uses the preserved JPEG/PNG, rather than recompressing a generated WebP. Existing
untracked WebP files are left alone.

Shared image paths, media-folder artwork, remote references, items with missing
or remote images, and symbolic links are excluded. Chapter extraction images,
user avatars, client image caches and unreferenced orphan directories use
separate Jellyfin stores and are outside this migration. It does not prune them.
Arithmetic/12-bit/CMYK JPEG, animation, non-default orientation, unsupported
color profiles and malformed/truncated images are skipped. EXIF/XMP/text metadata
is not copied into generated output. An input with an ICC profile is conservatively
skipped, even when its profile might be sRGB. Some lossless transparent images
are skipped when the encoder would normalize invisible RGB values.

Hard bounds are 32 MiB encoded input/output, 8192 pixels per source edge and
40 million decoded pixels. Processing is serial. Inventory captures library item
IDs, including owned/alternate versions, then reads images in 500-item batches
and rejects more than one million image references; this is an inventory-memory
bound, separate from the per-run candidate limit. Inventory is repeated each run
to detect shared references. Its duration scales with library size.

Jellyfin exposes no shared image mutation lock. Known scans and refreshes stop
writes; **simultaneous manual uploads remain a residual race**. Run applying
operations during an idle maintenance window. No watcher, method patch, database
edit or direct rename of a referenced artwork file is used. Jellyfin's save is
not atomic; durable recovery records and backups bridge save/persistence failures.
See [integration evidence](docs/INTEGRATION.md) and [recovery](docs/RECOVERY.md).

## Storage accounting

Converting with retained originals initially consumes additional disk space.
The dashboard deducts actual backup files, including orphan recovery copies,
from verified conversion savings for active image slots. This is artwork-byte
accounting, not an exact prediction of `df`: journals, databases, client caches,
filesystem blocks and subsequent changes by other plugins affect total usage.
No savings percentage is promised for a complete library.
The default free-space reserve is 2 GiB; the retained-original budget is 512 MiB.

## Build, test and package

Requires .NET SDK 10.0. The pinned runtime target is Jellyfin 12.1.x.

```sh
dotnet restore JellyP.slnx --locked-mode
dotnet build JellyP.slnx --configuration Release --no-restore
dotnet test JellyP.slnx --configuration Release --no-build --no-restore
python3 scripts/package.py
```

The local archive is `artifacts/JellyP_0.2.0.1.zip` with a SHA256 file. It contains
only the plugin DLL, `meta.json` and license. Jellyfin supplies its matching
SkiaSharp 3.119.4 managed/native stack. No server or duplicate native dependencies
are bundled. A WebP codec probe runs before any scan. The package script packages the local Release build; the trusted tag workflow publishes releases
and updates the catalog after checking their assets. See
[releasing](docs/RELEASING.md).

Stop Jellyfin during installation, extract the archive to
`<Jellyfin data>/plugins/JellyP_0.2.0.1/`, ensure ownership matches the server,
then restart and verify the plugin is active. Back up the server first. Do not
restart during playback or another maintenance task. For uninstall and retained
originals, follow [recovery instructions](docs/RECOVERY.md).

Validation scope and reproducible checks are recorded in
[VALIDATION.md](docs/VALIDATION.md). A Linux x64 test does not establish Windows,
macOS or ARM runtime behavior. Linux uses directory fsync for recovery publication;
other platforms currently have process-crash recovery only and are unvalidated.

Contributions: [CONTRIBUTING.md](CONTRIBUTING.md). Security reports:
[SECURITY.md](SECURITY.md). Changes: [CHANGELOG.md](CHANGELOG.md).

Licensed GPL-3.0-only; see [LICENSE](LICENSE).
