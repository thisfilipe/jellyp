# Validation

The initial release targets Jellyfin 12.1 / .NET 10. Validation covers Linux x64
and a controlled Ubuntu ARM64 installation; other platforms and Jellyfin versions
remain unvalidated.

## Automated checks

The 0.2.0.1 integrated .NET suite contains 87 passing tests. Coverage includes
baseline/progressive JPEG completeness, PNG/WebP structure, exact alpha,
lossless pixels, resizing, savings and SSIM gates, interrupted saves, recovery,
retirement, policy synchronization, shared references, owned/alternate items,
ID snapshots, canary isolation, category-aware batch limits, scope/policy cursor
resets, independent modes and legacy cursor upgrades. Release builds treat warnings as errors.

Nine Python release tests verify catalog checksums, archive layout, version alignment,
history preservation and rejection of modified existing releases. CI performs a
locked restore, Release build, both test suites, packaging and publication audit.

## Isolated Jellyfin

A disposable real Jellyfin instance on localhost was used to verify plugin
loading, administrator-only APIs, the embedded settings page, preview, migration,
policy synchronization, exact rollback and references surviving a process restart.
The 0.2.0.1 browser exercise uses native Jellyfin controls at desktop and mobile
widths, with no horizontal overflow or JavaScript errors. It checks all 11
categories, saved global/per-category values, byte-unit conversions, reload
persistence, language changes retaining unsaved edits, conditional fields,
numeric validation, empty restricted selection, item search, apply guards,
canceling permanence confirmation, simulation auto-save/polling, and the emergency
shortcut's unsaved behavior. An optional controlled case converts the single
synthetic image through the UI and restores its exact SHA256.

Reproduce after `smoke-local.py` with Node.js, Playwright and an installed browser:

```sh
node scripts/smoke-ui.cjs --root <marked-fixture-root> --apply-fixture
```

Set `JELLYP_PLAYWRIGHT_MODULE` if Playwright is installed outside Node's module
search path, and `JELLYP_BROWSER_CHANNEL` for an installed Edge/Chrome channel.
Omit `--apply-fixture` to exercise settings and simulation only. Optional
`--screenshots <directory>` writes synthetic desktop/mobile screenshots. These
browser integration checks require a disposable server; they are not run by CI.

The generated 512×512 PNG fixture was 471893 bytes; at quality 60 its WebP was
2648 bytes. This synthetic gradient is not a whole-library savings forecast.
Run [smoke-local.py](../scripts/smoke-local.py) only against a marked disposable
server and generated video fixture, as described in its docstring.

## Controlled ARM64 validation

Three singly referenced Primary images were converted individually with retained
originals. Output files, dimensions, checksums and persisted image references were
verified. The first image was restored byte-for-byte and converted again. No
recovery remained pending. Detailed operational records are private and excluded
from the repository.

Whole-library quality, performance and savings are not established by that test.
Inventory duration scales with library size. Preserving originals initially uses
additional storage; retirement is required to reclaim their space.

## Public catalog installation

The `v0.1.0.0` release workflow passed in GitHub Actions. Its public ZIP, SHA256
sidecar and raw catalog entry were independently downloaded and cross-checked.
The native Jellyfin installer on a fresh disposable Linux x64 instance loaded the
public repository, downloaded the versioned release and passed its MD5 check.
After restarting that instance, JellyP loaded active with its embedded settings
page, all 11 policies, changes disabled, dry run enabled, retained originals and
both tasks unscheduled. No production library was involved in this exercise.

Reproduce with [smoke-catalog.py](../scripts/smoke-catalog.py), following the
marked-root and two-phase instructions in its docstring and
[RELEASING.md](RELEASING.md).
