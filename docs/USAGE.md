# Using JellyP

Open **Dashboard → Plugins → JellyP → Settings**. The language selector changes
only this plugin page. Existing settings are preserved on upgrade; inspect the
mode and item selection before running. The version appears beside JellyP.

## First controlled batch

1. In **1. Select images**, choose **Only selected items**, search by name and
   add a result. Alternatively paste the item's Jellyfin details URL or ID.
   Items are individual slots: selecting a show does not select all episodes.
   An empty restricted selection blocks running. **Whole library** is explicit.
2. Select image types. Covers means Jellyfin's Primary image; backgrounds means
   Backdrop. People overrides ordinary image types for person items. Other types
   are under **More image types**. Only checked types count toward a batch.
3. In **2. Choose quality and storage**, start at quality 85 with downscaling off.
   Choose **Keep originals** for reversible testing and batch size 1.
   Per-type settings are optional and otherwise follow the global values.
4. Leave **3. Simulate or apply → Simulate** and click **Simulate next batch**.
   Running saves the displayed settings first. **Save settings** queues nothing.
5. Read progress and skip reasons. A simulation changes no artwork. Inventory
   checks the whole library for shared files even when the selected batch is
   small. This can take several minutes on a large library.
6. Pause uploads and metadata refreshes. Choose **Apply**, confirm that window
   and click **Convert next batch**. Inspect the item in Jellyfin afterwards.
   Retained originals can be restored under **Restore or release stored originals**.
7. Repeat to continue the pass. Return to **Simulate** and save when finished.
   Both scheduled tasks use the saved mode; this page creates no schedules.

## Low disk space

Retained originals initially use MORE space. For immediate net savings, open
**Need to free space now? Start here.** and use its shortcut. This only prepares
an unsaved simulation with 25 images and **Remove originals after verification**.
Your current scope, categories and quality stay unchanged: review them explicitly.
Start with one or a few selected items, simulate, inspect the result, then apply.
Applying this mode requires a permanence acknowledgment and confirmation.

Each original is removed only after WebP and its persisted Jellyfin reference
verify. This permanently prevents restoration and future regeneration from that
original. A temporary recovery copy is still required while verifying the image.
After validating a small batch, explicitly select **Whole library** if desired
and repeat batches of 25, progressively increasing only after inspection.

The default free-space reserve is **2 GiB**. If the disk is already full, first
free known unrelated space so Jellyfin and the conversion can work. Lowering the
reserve does not create space. Do not delete the metadata directory or database.
Stop on an error and inspect its details and recovery records before continuing.

For originals already retained, **Remove verified stored originals…** can release
space without another conversion. It checks committed outputs and retains
uncertain/recovery originals. This is permanent and requires Apply + maintenance.

## Quality, progress and storage

- **Quality** controls lossy encoding; 100 remains lossy. **Lossless** verifies
  exact pixels. Transparency is checked exactly in both modes. Downscaling is
  optional and never enlarges images.
- **Minimum similarity (SSIM)** is a separate quality gate, 0.96 by default.
  Lowering encoder quality may cause rejection by this gate. Do not lower the
  gate without inspecting quality. Unsupported formats/profiles are also skipped.
- **Image reduction in the last batch** includes simulation estimates.
  **Stored originals** measures retained/recovery bytes.
  **Net space reclaimed** subtracts those originals from verified active savings;
  a negative amount is shown as extra space used. This is not an exact `df` value.
- **Stop after current image** cancels between complete transactions. The next
  batch resumes. Changing selected items, enabled types or effective quality
  restarts that mode's pass; preview and apply have separate positions.

## Changing already converted images

Keep their originals. Change the relevant global or per-type settings and use
**Sync changed quality settings**. Start in Simulate, inspect, then Apply under
maintenance. It regenerates from the original, avoiding repeated lossy encoding.
Images without retained originals are skipped. Simply unchecking a type leaves
existing conversions intact; restore explicitly to undo them. Newly checked
categories use the regular next-batch button.

See [supported scope](../README.md#supported-scope-and-limits) and
[recovery](RECOVERY.md) for exclusions and interrupted operations.
