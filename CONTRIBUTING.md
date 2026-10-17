# Contributing

Bug reports, documentation fixes and focused pull requests are welcome. Use English
for repository discussions and primary documentation; localized usage guides and UI
translations are welcome. Be respectful, explain disagreements
with evidence, and avoid harassment or personal attacks.

## Before opening an issue

Search existing issues. Include the JellyP/Jellyfin versions, operating system,
architecture, selected settings and a minimal reproduction. Prefer a generated
image and disposable library. Redact credentials, hostnames, media paths, item
IDs and personal metadata from logs or screenshots. Do not upload databases,
original artwork, migration journals or backups to an issue.

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## Development

Install .NET SDK 10 and Python 3. The plugin targets Jellyfin 12.1. Linux tests
use the matching SkiaSharp native library from the test project's locked packages.

```sh
dotnet restore JellyP.slnx --locked-mode
dotnet build JellyP.slnx -c Release --no-restore
dotnet test JellyP.slnx -c Release --no-build --no-restore
python3 -m unittest discover -s tests/release -v
python3 scripts/package.py
python3 scripts/audit-publication.py --working-tree
```

Follow `.editorconfig`. Keep changes scoped and explain the behavior, relevant
tests and limitations in the PR. Add regression coverage for conversion,
integrity, recovery, inventory and release behavior. UI copy changes do not need
artificial unit tests. Integration exercises must use a marked disposable server;
see [validation](docs/VALIDATION.md).

## Integrity rules

- Preserve image slots, exact alpha and verified output dimensions. Do not
  silently relax quality/format checks to make more images pass.
- Use Jellyfin's supported image saver and persistence APIs. Do not patch its
  methods, write its database or rename referenced artwork directly.
- Keep journaling, checksum checks, rollback and space budgets intact. Test
  failures between saving bytes and persisting references.
- Respect shared references, owned/alternate items, exclusions and the item
  allowlist. Use source originals for policy synchronization.
- Keep changes disabled, dry run enabled and schedules empty by default.

## License and publication

Contributions are provided under GPL-3.0-only, the project's existing license.
Do not submit assets without redistribution rights. Synthetic fixtures are
preferred. The publication audit scans indexed files for common leakage patterns;
it complements manual review and cannot prove a tree contains no secrets.

Maintainers: follow [RELEASING.md](docs/RELEASING.md). Never replace a published
version's assets or overwrite a release tag.
