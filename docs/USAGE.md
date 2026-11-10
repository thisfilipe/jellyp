# Using JellyP

Open **Dashboard → Plugins → JellyP → Settings**. Choose English or Português
(Brasil). After upgrading, restart Jellyfin, reload the page and check the
version shown in the page header.

The page has five areas: **Simulate**, **Execute**, **Automation**,
**History and restoration**, and **Settings**. Manual work is independent of
scheduled automation.

## Scope and image policies

Choose **Selected libraries** and check the Jellyfin libraries JellyP may process.
Their folders, seasons, episodes and owned versions are included. An empty or
deleted selection blocks execution; it never means all libraries. Legacy global
settings remain preserved for existing scheduled tasks, but the page asks you to
choose libraries before saving a legacy global selection.

For a precise test, choose **Specific items**, search by name or paste a Jellyfin
details link/ID. IDs select those exact items: selecting a show does not include
its episodes. **All libraries and global metadata** explicitly includes the whole
server. People are global Jellyfin entities; library scope includes people
credited in selected items and their photos may also appear elsewhere. Shared
image files remain protected across all libraries.

Select any of the 11 image categories. Global defaults or per-type overrides set
quality 0–100, lossy/lossless mode, optional downscaling and minimum SSIM.
Defaults are quality 85, SSIM 0.96, no downscaling, retained originals, and at
least 5% and 4 KiB saved. Quality 100 is still lossy; **Lossless** checks exact
pixels. Transparency is checked exactly. The SSIM gate is independent of encoder
quality.

## Simulate and select exact images

In **Simulate**, choose scope, image types, quality, originals and the advanced
image limit, then choose **Simulate images**. The server reads real image bytes,
checks shared references and inventory progress, and returns proposals. Nothing
is changed in this step.

The result shows evaluated and proposed counts, skip reasons, a storage bill for
the exact selection and item-named proposals across result pages. Already-WebP,
shared, remote, missing and unsupported images are explained instead of being
silently ignored.
If no item has a usable local candidate, JellyP avoids the global inventory; real
candidates still receive the shared-reference check.

Select the proposals you want across result pages, then choose **Review
selection**. Only those exact image identities can proceed. Changing scope,
policies or defaults invalidates the plan and requires a new simulation. A plan
also expires; review its validity before applying.

Manual analysis is processed in deterministic key-ordered parts. A saved manual
default and each part evaluate at most 1,000 candidates; this is not a limit on
the whole library. When **Continue to next part** appears, it reaches the next
image slots without repeating the first part. Continuing is idempotent per parent
part and is refused if expiry, saved defaults, scope or policies changed. A
consumed part can still be continued, and the previous part remains available
for reading. Automation keeps its separate 10,000-candidate limit.

## Execute and operation receipts

In **Execute**, review the frozen settings, exact selection and storage bill,
then confirm. JellyP revalidates every selected image before mutation. The
operation is persisted on the server: navigating, reloading or reconnecting does
not cancel it or create a second dispatch.

Progress, waits and per-image results remain available in **History and
restoration**. **Stop** targets the exact operation and cancels at transaction
boundaries or during metadata waits; unwritten work remains for a later run.
Conversion waits automatically for a full scan or a refresh of the target, its
parents or owning library. Unrelated metadata and scheduled tasks do not need to
be disabled. Avoid manual image uploads during conversion because Jellyfin
exposes no shared mutation lock.

## Automation

In **Automation**, configure the scope, policies, originals and whether native
JellyP tasks may analyze or apply. Save automation independently of manual
drafts and Global defaults. Saving automation does not broaden scope or enable
conversions.

JellyP exposes **Optimize library artwork** and **Synchronize artwork policies**
under Dashboard → Scheduled Tasks → JellyP. Configure triggers there; the plugin
does not create a schedule automatically. Existing task identities and
configured triggers are preserved on upgrade. Manual simulation or manual apply
does not enable or disable scheduled processing.

When a policy changes, manual analysis proposes regeneration from the retained
original. Review and apply those exact proposals. The native synchronization task
can also regenerate unchanged policies when that option is enabled in Automation.
Images whose originals were retired are reported as **OriginalUnavailable**.
Changed external artwork is not overwritten. Unchecking a category leaves its
existing conversions alone; use restoration explicitly to undo them.

## History, restoration and permanent removal

**History and restoration** lists persisted operation receipts and their
individual results. The default list is deliberately preliminary: it shows
stored originals that can be selected for verification (`CanReview`), not a
claim that every ID is restorable. Select exact IDs, open review, and read the
per-ID verification result. Invalid, shared, changed, uncertain or superseded
IDs must be removed explicitly; nothing is silently dropped or substituted.
The explicit verified `restorable`/`retirable` filters and the `Backups/Review`
action perform the authoritative check.

Restoration requires retained originals. Retirement is permanent: without the
original, restoration and original-based regeneration are unavailable. Each
action is revalidated and confirmed for the exact selected identities. Active
WebP images are not removed by original retirement. The operation queue and
apply verify the exact IDs again even after a successful review.

Retention zero means manual removal; otherwise eligible originals expire at the
start of an applying batch. Follow [recovery](RECOVERY.md) for pending recovery,
uninstall and low-space procedures.

## Settings and storage accounting

**Settings** holds Global defaults for new manual tests plus global storage
protections and savings filters. These protections apply to all operations,
including automation. Saving defaults invalidates manual analysis and review; it
does not save or broaden automation.

Keep the default 2 GiB free-space reserve and 512 MiB retained-original budget.
Retained originals initially consume more disk space. Net space reclaimed
subtracts actual backup files from verified active conversion savings; it is not
an exact prediction of `df`. Journals, databases, client caches, filesystem
blocks and later changes by other plugins affect total usage. If the disk is
already full, free known unrelated space before attempting conversion; lowering
the reserve creates no space. Do not delete metadata or the database.

## Upgrade and rollback

Back up Jellyfin's data and database before upgrading. Install the new package
through the catalog or manually, then restart during an idle window and verify
the active version. The plugin GUID, native task identities, configured triggers,
saved scope/policies and existing recovery journals are preserved. Older journals
remain usable without inventing historical settings.

A safe DLL downgrade is not promised. To roll back, use the pre-upgrade backup
and a controlled restore rather than only replacing the DLL. Resolve pending
recovery before uninstalling and keep the plugin data directory until its
originals are no longer needed.

See [supported scope and limits](../README.md#supported-scope-and-limits),
[recovery](RECOVERY.md) and [validation](VALIDATION.md). Browser validation
covered Chromium only; Firefox, Safari, screen readers and physical devices
remain unvalidated.
