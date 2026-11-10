# Changelog

Versions use Jellyfin's four-part format. Published versions and their assets
are immutable; corrections receive a new version.

## [0.4.0.0] - 2026-11-09

- Reorganize the native dashboard into five areas: Simulate, Execute,
  Automation, History and restoration, and Settings.
- Analyze the selected scope, choose exact image identities across pages, review
  the frozen plan and apply only that reviewed selection. Stale or changed
  selections are revalidated instead of silently replaced.
- Persist plans and operation receipts on the server; navigation, reloads and
  reconnects keep the same operation and per-image results.
- Restore selected originals in batches, or review and permanently retire
  originals in a separate action. Uncertain recovery records never enter a
  batch.
- Keep manual drafts, Global defaults and scheduled automation independent.
  Saving defaults or automation does not broaden scope or enable conversions.
- Preserve existing configuration, task identities, triggers and journals on
  upgrade. Older journals remain usable, and applying remains disabled and in
  preview by default.
- Close cancellation races around validation, metadata waits and transaction
  boundaries. Stop targets the exact operation and leaves unwritten work for a
  later run.
- Add the approved JellyP branding, theme-correct wordmarks, native plugin card
  artwork and a direct JellyP entry in the Jellyfin main menu.
- Keep the verified WebP conversion, exact alpha checks, SSIM and savings gates,
  retained originals, durable recovery and storage protections from earlier
  releases.

Limitations: this release is pre-alpha and is not claimed to be stable or
universally certified. Browser validation covered Chromium only; Firefox,
Safari, screen readers and physical devices remain unvalidated, and axe is not
WCAG certification. The declared 5000-image plan, 1000-image batch and
200-original batch caps were not stress-tested; the largest real fixture used 52
images. Simultaneous manual image uploads remain a residual race because
Jellyfin exposes no shared mutation lock. Back up Jellyfin before upgrading.
Configuration and journals are preserved, but a safe DLL downgrade is not
promised.

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
