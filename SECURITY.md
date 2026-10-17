# Security

Security fixes target the latest public JellyP release and its documented
Jellyfin ABI. Other Jellyfin versions and unvalidated platforms are unsupported.

Use [GitHub private vulnerability reporting](https://github.com/thisfilipe/jellyp/security/advisories/new)
for security or data-integrity vulnerabilities. Include affected versions,
expected behavior, impact and a minimal synthetic reproduction. Do not open a
public issue containing credentials, databases, private media metadata or server
access details. No access to a production server is needed for a report.

JellyP runs with the Jellyfin service account's filesystem permissions. Only
administrators can configure or invoke its operations. Keep maintenance windows,
retained originals and an external backup appropriate to your deployment.
Deleting originals deliberately makes restoration unavailable for those images.
See [recovery](docs/RECOVERY.md) and [integration limits](docs/INTEGRATION.md).
