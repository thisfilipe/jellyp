# Releasing

The source of the four-part version is the plugin `.csproj`. Keep `build.yaml`
aligned and add the version's entry to `CHANGELOG.md`. Packaging rejects mismatches.
The plugin GUID is permanent across versions.

## Publish a version

1. Review changes and run the commands in [CONTRIBUTING.md](../CONTRIBUTING.md).
   Run `python3 scripts/audit-publication.py` against the Git index before pushing.
2. Merge the reviewed source to `main`; wait for **Build and validate** to pass.
3. Create and push a version tag, for example `v0.2.0.1`. The tag must equal `v`
   plus the project version. Do not move or replace an existing tag.
4. The **Publish release and catalog** workflow builds/tests the tagged source,
   validates the package and publishes its ZIP and SHA256 file on GitHub Releases.
5. It downloads both public assets and compares them to the tested build. Only
   then does it update `manifest.json` on the `catalog` branch, preserving older
   versions. Existing version metadata/checksums cannot be replaced silently.
6. Verify the workflow, public release download and raw catalog URL. Exercise an
   installation through Jellyfin's repository/catalog UI on a disposable server.

The catalog URL is:

```text
https://raw.githubusercontent.com/thisfilipe/jellyp/catalog/manifest.json
```

Jellyfin requires MD5 in the catalog's `checksum`; the release additionally
publishes SHA256. The catalog is derived from the exact uploaded ZIP, rather
than a separate rebuild. Sources: Jellyfin's
[package model](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Model/Updates/PackageInfo.cs),
[version model](https://github.com/jellyfin/jellyfin/blob/v12.1/MediaBrowser.Model/Updates/VersionInfo.cs)
and [installer](https://github.com/jellyfin/jellyfin/blob/v12.1/Emby.Server.Implementations/Updates/InstallationManager.cs).

The repeatable catalog exercise is `scripts/smoke-catalog.py`. Against a fresh,
marked disposable server on localhost:18096, run `--root <fixture-root> --phase
install`, restart only that disposable process, then run the same command with
`--phase verify`. It registers the public repository, invokes Jellyfin's native
installer and checks the installed plugin after restart. Test credentials stay
private under the disposable root and are never printed. For a fixture with an older
JellyP release, use `--phase upgrade` instead of `install`, restart and verify.
The upgrade exercise snapshots the saved configuration and checks exact preservation.

## Failure and retry

A failure before the catalog update leaves its previous entries intact. A
published release can exist without a catalog entry; fix and rerun the job.
The workflow never uploads over existing assets. A rebuilt ZIP differing from
an existing release is rejected: investigate the build environment, retain the
published artifact and issue a new version for changed code. Do not repair a
checksum by replacing the asset behind the same URL.

To retry only catalog publication from a trusted checkout of the release tag,
download the existing release ZIP and SHA256, verify them, then run:

```sh
python3 scripts/publish-catalog.py --repository thisfilipe/jellyp --archive artifacts/JellyP_0.2.0.1.zip
```

This requires authenticated `gh` with repository contents write access. Normal
CI/PR builds are read-only; only the trusted tag workflow has publishing access.
No custom long-lived token is required by the workflow.

## Publication boundaries

Commit source, synthetic tests, documentation and workflows. Exclude local
operational notes, databases, backups, journals, original artwork, credentials,
logs, build directories and test credentials. `.local/` and `artifacts/` are
ignored. The release archive has an explicit three-file allowlist: plugin DLL,
`meta.json` and GPL license. Public release notes must describe product behavior,
not a maintainer's library, host layout or incident history.
