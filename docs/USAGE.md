# Using JellyP

Open **Dashboard → Plugins → JellyP → Settings**. Choose English or Português
(Brasil). After upgrading, restart Jellyfin, reload the page and check its version.

## Select libraries first

Choose **Selected libraries** and check the ones JellyP should process. Their
folders, seasons and episodes are included. An empty or deleted selection blocks
execution; it never means all libraries. Legacy global settings are preserved for
existing scheduled tasks, but the new page asks you to select libraries before
saving them.

For a precise test, choose **Only selected items**, search by name or paste its
Jellyfin details link/ID. IDs select those exact items: selecting a show does not
include its episodes. **All libraries and global metadata** explicitly includes all libraries and
global metadata.

People photos belong to global Jellyfin entities. In library scope, people credited
in selected items are included and can also appear elsewhere. Shared image files
referenced by multiple entities remain protected across all libraries.

Check image types and choose global or individual settings. Start at quality 85,
without downscaling. Quality 100 is still lossy; Lossless checks exact pixels.
Minimum similarity (SSIM) is a separate gate, normally 0.96. Do not lower it without
reviewing quality. Transparency is checked exactly.

## Simulate now

Under **3. Test or run now**, choose Simulate and click **Simulate selection**.
The button saves scope, image settings and the separate automation selection, then
requests a simulation with its own ID. It does not change the saved automation mode.

The page shows inventory progress, how many images would convert, estimated
reduction, skip reasons and up to 20 named examples. The inventory checks all
references even for a small selected scope. Zero work is explained: already-WebP
images are not repeatedly recompressed; shared/outside-metadata/unsupported images
remain excluded. If no items matched, review the scope/IDs.

The advanced **Image limit per execution** defaults to 250. It is a bound on work,
not a substitute for library selection. Each later execution resumes the pass;
preview/apply positions are independent. Changing effective scope or policies
restarts that mode's pass.

## Apply or automate

Choose Apply under Run now for a manual conversion. It does not enable automatic
conversions. Conversion waits automatically for full scans and metadata refreshes
of the target, its parents or its library, with cancel support. The wait continues until the conflict clears or the
native task is cancelled; any native task runtime limit still applies. Unrelated scheduled tasks do not need to be disabled. Avoid manual
image uploads during conversion: the public Jellyfin API provides no shared lock.

Under **4. Scheduled processing**, independently choose **Simulate only** or
**Apply conversions**, then save. Open Jellyfin Scheduled Tasks and configure
triggers for JellyP's Optimize and Synchronize tasks. No triggers are created by
the plugin. Existing triggers are preserved on upgrade. Later triggers resume the
saved scope using its policies and execution limit, without maintenance checkboxes.
Cancellation leaves the current image for a later run; inspect any displayed error.

## Low disk space and originals

Retained originals initially use MORE disk space. For reversible testing, keep
originals, set the advanced limit to 1 and inspect the item; Restore original undoes
that transaction without changing scheduled processing.

For immediate savings, choose **Remove originals after verification**. The quick
emergency guide prepares an unsaved 25-image simulation with this setting, keeping
scope/quality/automation unchanged. Review them before running. Applying without
originals requires acknowledgment and confirmation; saved applying automation with
this storage policy is also permanent.

Each source is removed only after converted bytes and the persisted reference
verify. Without the retained original, restoration and future original-based
regeneration are unavailable. A temporary recovery copy remains necessary until
verification completes. Remove verified stored originals can reclaim previous
backup space without converting again; it preserves uncertain/recovery copies.

Keep the default 2 GiB free-space reserve. If the disk is already full, free known
unrelated space before attempting conversion; lowering the reserve creates no
space. Do not delete metadata or the database. Net space reclaimed subtracts retained
originals from verified active conversion savings and is not an exact `df` value.

To change existing converted images, keep their originals, change quality/size and
run **Sync changed quality settings**, first simulating. It uses the original to
avoid repeated lossy encoding. Unchecking a type does not restore old conversions;
newly selected types use normal optimization.

See [supported scope](../README.md#supported-scope-and-limits) and
[recovery](RECOVERY.md).

## Registered images before execution

Exact item selections now show year/type/short ID and a read-only inspection of
registered image types. Items without artwork, absent checked types, remote
references, already-WebP, missing and excluded local files are explained before
running. Preliminary candidates still need global shared-reference, quality and
savings checks. JellyP does not download missing artwork. Refresh status to repeat
inspection after external image changes. If no selected item has a usable local
candidate, the run avoids global inventory; actual candidates retain that check.
