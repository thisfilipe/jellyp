# Changelog

Versions use Jellyfin's four-part format. Published versions and their assets
are immutable; corrections receive a new version.

## [0.1.0.0] - 2026-10-17

- Initial public release for Jellyfin 12.1 and .NET 10.
- Global and per-category WebP policies for 11 artwork categories, with exact
  alpha verification, optional lossless encoding/downscaling and SSIM gates.
- Resumable preview/apply tasks, original-based policy synchronization,
  optional retained originals, verified rollback and backup retirement.
- Shared-reference checks, owned-item inventory, recovery journals and storage
  budgets. Changes start disabled, with dry run on and no default schedule.
- Jellyfin dashboard configuration and administrator-only operations.
