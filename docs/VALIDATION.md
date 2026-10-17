# Validation

The initial release targets Jellyfin 12.1 / .NET 10. Validation covers Linux x64
and a controlled Ubuntu ARM64 installation; other platforms and Jellyfin versions
remain unvalidated.

## Automated checks

The initial integrated .NET suite contains 76 passing tests. Coverage includes
baseline/progressive JPEG completeness, PNG/WebP structure, exact alpha,
lossless pixels, resizing, savings and SSIM gates, interrupted saves, recovery,
retirement, policy synchronization, shared references, owned/alternate items,
ID snapshots and canary isolation. Release builds treat warnings as errors.

Python release tests verify catalog checksums, archive layout, version alignment,
history preservation and rejection of modified existing releases. CI performs a
locked restore, Release build, both test suites, packaging and publication audit.

## Isolated Jellyfin

A disposable real Jellyfin instance on localhost was used to verify plugin
loading, administrator-only APIs, the embedded settings page, preview, migration,
policy synchronization, exact rollback and references surviving a process restart.
A browser exercise checked all 11 categories, saved global/per-category values,
original preservation and task buttons without JavaScript errors.

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
