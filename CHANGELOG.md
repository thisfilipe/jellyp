# Changelog

Versions use Jellyfin's four-part format. Published versions and their assets
are immutable; corrections receive a new version.

## [0.3.0.1] - 2026-10-17

- Inspect exact selected items before execution, showing registered image types,
  preliminary local candidates, already-WebP, remote, missing and excluded images.
  Explain items with no artwork and checked types absent from an item.
- Identify search/selected items by year, type and short ID to distinguish variants.
- Skip the global inventory when selected items have no usable local images;
  real conversion/synchronization candidates still receive global shared-file checks.
- Replace the queued banner with the completed, cancelled or failed execution result.
  Explain image-less items and untouched remote references in inventory outcomes.

## [0.3.0.0] - 2026-10-17

- Select native Jellyfin libraries, including descendants and credited people,
  or individual items. Empty/deleted library selections fail closed. Shared-file
  checks still include references outside selected libraries.
- Give every manual execution an ID and native task receipt, with observable
  queue/inventory/waiting/completion states, inventory progress, simulated image
  counts and up to 20 example results. Empty selections now explain already-WebP,
  shared and excluded images instead of silently reporting success with zero work.
- Separate manual Simulate/Apply from saved scheduled processing. Manual previews,
  explicit restoration and original retirement no longer change automation.
- Allow read-only simulation while metadata is busy. Applying waits automatically
  for scans and refreshes related to the target, its parents
  or library; unrelated queued metadata and scheduled tasks do not block it.
- Remove the manual maintenance-confirmation requirement. Keep transaction
  verification, recovery copies and conservative checks before writes; concurrent
  manual uploads remain a limitation of Jellyfin's public APIs.
- Reject conflicting/undispatchable requests visibly; cancel queued work and
  metadata waits. Preserve existing configuration and native task identities.
- Make execution limits advanced settings and expose native task status and
  direct scheduling access in the dashboard.

## [0.2.0.1] - 2026-10-17

- Let Jellyfin initialize select labels and dropdown arrows; preserve translated
  labels without duplicating the native markup.
- Extend the browser exercise to verify native arrows and translated select labels.

## [0.2.0.0] - 2026-10-17

- Rebuilt the dashboard around selection, quality/storage, and execution steps,
  with native Jellyfin form controls and responsive layouts.
- Added Brazilian Portuguese/English, item search, optional per-type settings,
  understandable units and explanations for quality gates and skipped images.
- Replaced the two execution flags with one Simulate/Apply selector. Running
  saves the displayed settings; status refreshes automatically. Permanent removal
  has an explicit acknowledgment and confirmation.
- Added an emergency setup shortcut, named recovery records and clear separation
  of image reduction, retained originals and net space reclaimed.
- Unselected categories no longer consume batch slots. Scope or effective policy
  changes reset the relevant cursor; preview/apply remain independent. Legacy
  cursors safely restart once, retaining all recovery records.

## [0.1.0.0] - 2026-10-17

- Initial public release for Jellyfin 12.1 and .NET 10.
- Global and per-category WebP policies for 11 artwork categories, with exact
  alpha verification, optional lossless encoding/downscaling and SSIM gates.
- Resumable preview/apply tasks, original-based policy synchronization,
  optional retained originals, verified rollback and backup retirement.
- Shared-reference checks, owned-item inventory, recovery journals and storage
  budgets. Changes start disabled, with dry run on and no default schedule.
- Jellyfin dashboard configuration and administrator-only operations.
