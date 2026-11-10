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

## Libraries, individual items and image policies

Choose **Selected libraries** and check native Jellyfin libraries. Their folders,
seasons, episodes and owned versions are included. An empty/deleted library
selection never expands to the entire server. **Only selected items** supports
name search, item IDs and details links; this selects those exact items, not
children of a selected show. **All libraries and global metadata** explicitly includes all libraries
and global metadata. Old configurations retain their previous scope for native
tasks; the dashboard asks legacy global selections to choose libraries before saving.

People are global Jellyfin entities. Library selection includes people credited
in selected items; their photos can also appear in other libraries. Shared image
files referenced by multiple entities remain excluded, including references from
unselected libraries. Unrelated global artists/studios are available through
individual-item or explicit whole-server scope rather than inferred membership.

Select any of the 11 image categories independently. Global encoding defaults or
per-type overrides control quality 0–100, lossy/lossless mode, optional downscaling
and SSIM. Quality 100 remains lossy; use **Lossless** for exact pixels. Dimensions
stay unchanged by default, and alpha samples are checked exactly at output size.

Keep originals for restoration and future policy changes, or permanently remove
them after output and reference verification. A temporary recovery copy is always
required. Retained originals initially consume additional space.

## Dashboard areas

The native page is organized into five areas. The same saved scope, task
identities and recovery records remain the source of truth.

1. **Simulate.** Choose selected libraries, exact items or explicit all-server
   scope, then image types, quality and originals. Defaults are quality 85,
   SSIM 0.96, no resizing, retained originals, and at least 5% and 4 KiB saved.
   The server reads real bytes, checks shared references and shows proposals
   without changing artwork. The SSIM gate is independent of encoder quality.
2. **Execute.** Select the exact proposals to apply across result pages, review
   the frozen plan and confirm only that selection. The plan is revalidated
   before mutation. Execution is persisted on the server; navigating,
   reloading or reconnecting does not cancel it. Stop targets the exact
   operation and cancels at transaction boundaries or during metadata waits.
3. **Automation.** Configure scheduled scope, policies, originals and whether
   native tasks may analyze or apply. Saving automation does not change manual
   drafts or Global defaults. Open Jellyfin Scheduled Tasks to configure
   triggers; no schedule is created automatically. Manual simulation and manual
   apply do not enable or disable automation.
4. **History and restoration.** Open persisted operation receipts and
   per-image results, restore selected originals in a batch, or review permanent
   retirement separately. Restoration requires retained originals; retirement
   is permanent. Uncertain recovery records never enter either batch.
5. **Settings.** Global defaults seed new manual tests. Global storage
   protections and savings filters apply to all operations, including
   automation. An advanced image limit per batch bounds each run (default 250);
   repeated triggers resume the pass, and a changed scope or policy resets that
   mode's position.

JellyP exposes **Optimize library artwork** and **Synchronize artwork policies**
under Dashboard → Scheduled Tasks → JellyP. Both process artwork after Jellyfin
has saved it and use the saved automation settings. Conversion waits for a full
library scan or a refresh of the target, its parents or owning library, with
visible wait state and cancel support. It does not block on unrelated metadata
or scheduled tasks; a maximum runtime configured in Jellyfin still applies.
Manual image uploads still need to be avoided during conversion because
Jellyfin exposes no shared mutation lock.

When a policy changes, the manual analysis proposes regeneration from the
retained original; review and apply those exact proposals. The native
synchronization task can also regenerate unchanged policies when that option is
enabled in Automation. Images whose originals were retired are reported as
**OriginalUnavailable**. Changed external artwork is not overwritten.
Unselecting a category leaves existing conversions alone; use restoration
explicitly to undo them.

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

Jellyfin exposes no shared image mutation lock. Known scans and related refreshes pause
writes automatically; **simultaneous manual uploads remain a residual race**. Avoid simultaneous manual uploads to items being converted. No watcher, method patch, database
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

## Release status and validation limits

JellyP is pre-alpha software. Version 0.4.0.0 is the current release; it is not
claimed to be stable or universally certified. Browser validation
covered Chromium only, so Firefox, Safari, screen readers and physical devices
remain unvalidated; automated accessibility checks are not WCAG certification.
The declared 5000-image plan, 1000-image batch and 200-original batch caps are
enforced but were not stress-tested. The largest real fixture used 52 images.
Linux x64 is the exercised platform; Windows, macOS and ARM behavior is not
established by that run.

The implemented interface and its evidence are summarized in [PLAN.md](docs/PLAN.md),
with the detailed Portuguese specification in
[UX-REFACTOR-PLAN.pt-BR.md](docs/UX-REFACTOR-PLAN.pt-BR.md) and validation in
[VALIDATION.md](docs/VALIDATION.md).

## Build, test and package

Requires .NET SDK 10.0. The pinned runtime target is Jellyfin 12.1.x.

```sh
dotnet restore JellyP.slnx --locked-mode
dotnet build JellyP.slnx --configuration Release --no-restore
dotnet test JellyP.slnx --configuration Release --no-build --no-restore
python3 scripts/package.py
```

The local archive is `artifacts/JellyP_0.4.0.0.zip` with a SHA256 file. It contains
exactly four members: the plugin DLL, `meta.json`, the GPL license and
`jellyp-plugin.png`. Jellyfin supplies its matching SkiaSharp 3.119.4
managed/native stack. No server or duplicate native dependencies are bundled. A
WebP codec probe runs before any scan. The package script packages the local
Release build; the trusted tag workflow publishes releases and updates the
catalog after checking their assets. See [releasing](docs/RELEASING.md).

Back up Jellyfin's data and database before upgrading. Stop Jellyfin, install the
new package through the catalog or extract it to
`<Jellyfin data>/plugins/JellyP_0.4.0.0/`, ensure ownership matches the server,
then restart and verify the plugin is active. The GUID, native task identities,
configured triggers, saved scope/policies and existing recovery journals are
preserved on upgrade. Older journals remain usable without inventing historical
settings. A safe DLL downgrade is not promised: to roll back, use the pre-upgrade
backup and a controlled restore rather than only replacing the DLL. Do not
restart during playback or another maintenance task. For uninstall and retained
originals, follow [recovery instructions](docs/RECOVERY.md).

Validation scope and reproducible checks are recorded in
[VALIDATION.md](docs/VALIDATION.md). A Linux x64 test does not establish Windows,
macOS or ARM runtime behavior. Linux uses directory fsync for recovery publication;
other platforms currently have process-crash recovery only and are unvalidated.

Contributions: [CONTRIBUTING.md](CONTRIBUTING.md). Security reports:
[SECURITY.md](SECURITY.md). Changes: [CHANGELOG.md](CHANGELOG.md).

Licensed GPL-3.0-only; see [LICENSE](LICENSE).
