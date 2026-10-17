# Recovery and safe maintenance

## Data layout

The plugin uses `Plugin.DataFolderPath/migration` (Jellyfin reports its plugin
data directory; do not assume it is the directory containing the DLL):

- `journal/<transaction-id>.json`: source slot/path, SHA256 values, output path,
  effective policy fingerprint, sizes and durable transaction state.
- `originals/<transaction-id>.original`: exact original encoded bytes.
- `staging/<transaction-id>.image`: disposable copy consumed by Jellyfin's saver.
- `last-run.json`, `optimize-cursor-*.json`, `sync-cursor-*.json`: progress and
  independent preview/apply cursors. Removing a cursor only restarts a scan;
  never remove recovery records to force a failed batch to continue.

`Prepared`, `Restoring` or `RecoveryRequired` blocks further applying batches.
The dashboard lists the latest hundred transactions. Restore the affected
transaction with **Restore original** after confirming that specific restore action.
Restoration verifies both current image identity and original checksum and uses
Jellyfin's save/persistence services. An interrupted restore can be retried.

Never overwrite an image that another actor changed just to satisfy a checksum.
Never delete a `Prepared`, `Restoring` or `RecoveryRequired` original. If output
is partial or no longer identifiable, the plugin retains the original and stops
for review. A complete hash-matching unreferenced output created by its own
transaction can be removed after successful restore; unknown output is retained.

If a process died after publishing an original but before publishing its journal,
an untracked original may remain. These bytes count against the backup quota.
Do not broadly remove the originals directory: preserve it for administrator
review. Temporary files from an interrupted atomic write also count toward quota.

## Reversing a canary

1. Keep the canary item allowlist and original preservation enabled. Pause
   manual image uploads. Keep the desired scope and policies saved; restoration
   is independently authorized and does not change the scheduled mode.
2. In the JellyP settings page locate the canary transaction and select
   **Restore original**. Refresh status, then reload the item's images in
   Jellyfin. A successful restore records `RolledBack` and releases the backup.
3. Keep scheduled processing in its intended mode after the canary. Do not
   clear the allowlist until deliberately approving a broader migration.

An authenticated administrator can also call
`POST /JellyP/Rollback/{transactionId}`. Transactions are IDs, never supplied
filesystem paths. API keys and administrator credentials must not be embedded
in repository scripts or pasted into public reports.

Retiring backups is permanent. If preservation was disabled, or a backup was
retired, recovery requires an external backup or a new image download. Policy
synchronization cannot reconstruct the original quality or dimensions.

## Before uninstalling

Disable schedules for both JellyP tasks and cancel/finish the current batch.
Resolve pending recovery. Restore any originals you want back **before** removing
the plugin. Stop Jellyfin, remove only `plugins/JellyP_<installed-version>/`, restart and check
health. Retain the plugin data directory and its originals until recovery is no
longer needed; deleting the DLL folder does not undo conversions.

## Low free space

JellyP allows a minimum reserve of 64 MiB and defaults to 2 GiB, matching the
observed Jellyfin 12.1 startup requirement. It checks backup and target volumes,
and budgets the transient source/staging/destination peak. A quota or
space failure stops the batch without advancing the failing image. Free known
unrelated space or retire **verified committed** backups, then resume. Do not
blindly delete Jellyfin's metadata directory, database, original backups, or
live transcodes. Client caches may retain old variants after an image changes.

The plugin serializes its own operations but cannot block Jellyfin uploads.
Applying waits for scans and related metadata; manual uploads must still be avoided. Linux journal publication fsyncs
files and directories; power loss or storage faults still require checking
filesystem/database health before attempting recovery.
