# JellyP test-only slow metadata provider

This project is an isolated fixture helper, not part of the JellyP product package.
It registers a local metadata provider that delays only items whose path contains
`Slow Refresh`. The host gate harness uses it to keep a real Jellyfin library
refresh open long enough to observe `WaitingForMetadata` and related versus
unrelated metadata behavior without sleeps or a fault endpoint.

Build it explicitly when preparing a disposable fixture, with the committed lock
file:

```sh
dotnet restore tests/host-fixture/JellyP.TestSlowMetadata/JellyP.TestSlowMetadata.csproj --locked-mode
dotnet build tests/host-fixture/JellyP.TestSlowMetadata/JellyP.TestSlowMetadata.csproj -c Release --no-restore
```

The harness installs the resulting `JellyP.TestSlowMetadata.dll` and a matching
`meta.json` under the fixture's plugin directory. The provider reads
`JELLYP_TEST_SLOW_FILE` (a file containing the delay in seconds) or
`JELLYP_TEST_SLOW_SECONDS`; `JELLYP_TEST_SLOW_RELEASE` is an optional release
file that ends a delay early. It never changes JellyP, its build, or a release
artifact. Do not install this plugin on a non-disposable server.
