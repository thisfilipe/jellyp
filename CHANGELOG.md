# Changelog

Versions use Jellyfin's four-part format. Published versions and their assets
are immutable; corrections receive a new version.

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
