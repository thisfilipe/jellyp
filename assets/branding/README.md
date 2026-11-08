# JellyP approved branding

The owner-approved concept C uses the gradient P/image symbol and gradient final
P in the wordmark. These assets were supplied by the owner, not redrawn or newly
generated during integration. The project license applies. Do not replace these
with a typographic approximation or recolor the gradient.

## Original masters (unchanged copies)

The four supplied PNGs were copied, not moved or overwritten. Names describe the
**destination theme**, not whether the canvas has a background.

| Supplied filename | Repository copy under `masters/` | Dimensions | Bytes | Alpha / margins |
|---|---|---:|---:|---|
| `Logotipo JellyP em fundo azul-marinho.png` | `jellyp-catalog-navy.png` | 1672 × 941 | 1,229,115 | Opaque RGB; approved navy composition and generous clearspace |
| `JellyP_simbolo_2048_suavizado.png` | `jellyp-symbol.png` | 2048 × 2048 | 666,394 | Transparent RGBA; nonzero-alpha bounds x73–1973, y75–1971 |
| `JellyP_assinatura_horizontal_fundo_claro.png` | `jellyp-wordmark-light.png` | 3170 × 1052 | 471,341 | Transparent RGBA, navy letters; bounds x42–3127, y42–1009 |
| `JellyP_assinatura_horizontal_transparente.png` | `jellyp-wordmark-dark.png` | 3170 × 1052 | 470,324 | Transparent RGBA, white letters; same bounds |

All four masters were byte-compared with their supplied originals. Both
signatures are transparent, including the source named “fundo claro”. No canvas
was cropped. Master margins and the approved opaque catalog framing are retained.

## Optimized delivery files

| Path (relative to repository root) | Dimensions | Bytes | Purpose |
|---|---:|---:|---|
| `Jellyfin.Plugin.JellyP/Web/branding/jellyp-wordmark-light.png` | 480 × 159 | 31,343 | Navy wordmark on the real light host surface |
| `Jellyfin.Plugin.JellyP/Web/branding/jellyp-wordmark-dark.png` | 480 × 159 | 30,201 | White wordmark on the real dark host surface |
| `Jellyfin.Plugin.JellyP/Web/branding/jellyp-symbol.png` | 256 × 256 | 34,203 | Local square symbol resource |
| `assets/branding/jellyp-plugin.png` | 768 × 432 | 38,844 | Transparent 16:9 installed-plugin image, with the unchanged 256 px symbol centered |
| `assets/branding/jellyp-catalog.png` | 960 × 540 | 366,952 | Opaque RGB catalog thumbnail on navy |

Delivery copies use proportional downsampling (height rounded to a whole pixel)
with the existing SkiaSharp 3.119.4 Mitchell cubic sampler, then lossless PNG
encoding. No new dependency, palette reduction, recoloring or image generation.
The runtime wordmarks retain transparent margins of approximately 6–7 px; the
symbol approximately 9 px. Alpha still spans 0–255; the thumbnail is opaque.
The installed-plugin derivative adds only transparent padding: the existing
256 × 256 symbol is placed at x256, y88 on a 768 × 432 canvas. A pixel-by-pixel
test verifies the symbol is unchanged and every added pixel is transparent.
The publication audit accepts only the exact nine PNG SHA256 hashes, not
arbitrary binary source files. Replacements require an explicit hash review.

## Native Jellyfin 12.1 contracts

- `Plugin.GetPages()` registers the three PNG resource names, with main-menu
  visibility disabled. The existing `jellyp` page remains the first and only
  enabled menu page. Native `web/ConfigurationPage?name=jellyp-wordmark-dark.png`
  (or the light/symbol name) returns the embedded bytes as `image/png`, verified
  on the actual host. `ApiClient.getUrl` preserves Jellyfin's server/base path.
- The header follows the existing rendered-host palette detection, **not** the
  OS media preference. It has one image alternative, `JellyP`, no duplicate text,
  a reserved aspect ratio, a compact approximately 128 × 42 px maximum, and the
  existing smaller version fetched from the Plugins API. Menus, sticky layout,
  selections and history are unchanged.
- Installed external plugins must use **`imagePath: "jellyp-plugin.png"`** in
  `meta.json`, with that file in the ZIP/plugin folder. Native
  `Plugins/{id}/{version}/Image` serves it. `PluginManifest.ImageResourceName`
  exists for bundled/integrated plugins but has `[JsonIgnore]`: putting
  `imageResourceName` into external `meta.json` is silently ignored. An initial
  real-host probe exposed this distinction; a model round-trip regression test
  and exact four-member package validation now cover the supported path.
- The ZIP contains only the DLL, `meta.json`, GPL license and padded symbol PNG. Masters
  and the catalog thumbnail are not bundled. No new backend endpoint, inline
  base64, remote header image or backend operation change is needed.

## Catalog readiness, not publication

The repository and tag-based release pipeline are already established in
`scripts/package.py`, `scripts/catalog.py` and `scripts/publish-catalog.py`.
The generator prepares `imageUrl` pointing to the thumbnail in **that release's
source tag**, under `assets/branding/jellyp-catalog.png`. Publication now checks
that the public URL returns the exact source PNG before writing any catalog
change. Missing URLs, wrong MIME types or different bytes fail closed.

This is preparation for a **future, separately authorized release**. The current
version is deliberately unchanged. Its existing published tag does not contain
these new files; a locally generated reference for that old tag is **not live**
and must not be advertised as such. Do not replace an existing version, its ZIP,
tag or checksum. `build.yaml` needs no extra image field: this repository's real
catalog generator owns `imageUrl`. No generated `artifacts/` were edited.

## Completed scoped validation (2026-11-07)

- Release build passed without warnings; 186 .NET tests and 16 release tests
  passed. The release checks include the exact image member, thumbnail metadata,
  hash allowlist and fail-closed public-image validation (mocked network only).
- Real Jellyfin 12.1 served the exact embedded page and all three PNG resources
  with `image/png`. Header checks passed for dark/light desktop/mobile: natural
  size, theme-correct local URL, aspect ratio, bounded height, alternative text,
  no duplicated wordmark and a smaller dynamic version.
- The final focused native run passed **122 checks, including 7 axe checks**, with
  desktop/mobile/tablet navigation and sticky-layout checks. Its screenshots
  were visually inspected in both themes: no oversized header, menu jump,
  stretching, clipped version or horizontal overflow.
- After the owner's explicit authorization, the disposable process was verified
  against its README command, root marker and real `System/Info.ProgramDataPath`
  before each stop/update. Only its metadata and padded image were updated;
  the installed DLL was byte-identical to the final source build. The real host
  now returns `HasImage: true`, and its anonymous native Image endpoint returns
  `image/png` with exactly the packaged bytes. No API or final HTML was mocked.
- Real grid inspection corrected the initial aspect-ratio assumption: the list
  uses a **fixed-height cover background**, while the desktop detail view uses
  a 16:9 image container. A preliminary narrower padding canvas clipped the
  symbol at some widths; the final 768 × 432 canvas keeps **every nonzero-alpha
  pixel** inside the native list image at 1600, 950, 768 and 390 px viewports, in
  light and dark themes. Geometry checks calculate the actual cover scaling and
  visible alpha bounds; the resulting screenshots were visually inspected.
  The desktop detail shows the complete 16:9 canvas. On mobile, the unmodified
  host deliberately hides its large detail image; the list card remains visible.
- `installed-card-{light,dark}-{desktop,wide-tablet,tablet,mobile}.png`, their
  `-detail.png` crops and `.json` geometry, and
  `installed-plugin-detail-{light,dark}-{desktop,mobile}.png` record that evidence
  inside `branding-evidence/`. The native detail still shows a repository-details
  warning in this fixture, independently of the successfully loaded image. Catalog
  metadata/public installation was not exercised or represented as published.
- Evidence is outside the repository in the marked fixture's `branding-evidence/`,
  `branding-targeted-ui.log`, `branding-build.log`, `branding-dotnet-tests.log`,
  `branding-release-tests.log` and `branding-package/`. No production access,
  commit, publication, deployment or version bump occurred. Full mutating journey
  and planning suites were not rerun for this asset-only change.
