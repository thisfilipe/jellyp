# JellyP 0.4.0.1 benchmark harness

This directory contains the comparative harness used by
`docs/PATCH-0.4.0.1-VALIDATION.pt-BR.md`. It is a test/measurement harness, not
product code. It uses a fake Jellyfin `ILibraryManager` and a fake
`IArtworkStore`; it does not touch a real server, production database, or UI.

`JellyP.Benchmark` builds against the current checkout and defines `CANDIDATE`.
The same `Program.cs` is compiled against the extracted `v0.4.0.0` source with
`BASELINE`, so both binaries use identical benchmark inputs and cases.

## Candidate

```sh
dotnet run --project tests/benchmark/JellyP.Benchmark/JellyP.Benchmark.csproj \
  -c Release -- /tmp/opencode/jellyp-0.4.0.1-benchmark/candidate.json
```

## Baseline

```sh
rm -rf /tmp/opencode/jellyp-0.4.0.0-baseline /tmp/opencode/jellyp-benchmark-baseline
mkdir -p /tmp/opencode/jellyp-0.4.0.0-baseline /tmp/opencode/jellyp-benchmark-baseline
cd "$REPO"
git archive 268a1d6 Jellyfin.Plugin.JellyP | tar -x -C /tmp/opencode/jellyp-0.4.0.0-baseline
```

Create `/tmp/opencode/jellyp-benchmark-baseline/JellyP.Benchmark.csproj` with
the same package references as the candidate project, a `ProjectReference` to
`/tmp/opencode/jellyp-0.4.0.0-baseline/Jellyfin.Plugin.JellyP/Jellyfin.Plugin.JellyP.csproj`,
`<DefineConstants>$(DefineConstants);BASELINE</DefineConstants>`, and:

```xml
<Compile Include="$REPO/tests/benchmark/JellyP.Benchmark/Program.cs" Link="Program.cs" />
```

Then:

```sh
cd /tmp/opencode/jellyp-benchmark-baseline
dotnet run --project JellyP.Benchmark.csproj -c Release -- \
  /tmp/opencode/jellyp-0.4.0.1-benchmark/baseline.json
```

The candidate project defines `CANDIDATE` for `DetailedInventoryPage`; the
baseline build uses the unbounded `DetailedInventory` fallback and records the
`PlanScopeTooLarge` failures instead of inventing a time ratio. Each case runs
three times and reports the median. The harness deliberately keeps image
payloads tiny; it measures metadata scans, coordinator call counts, journal
payload size, and a synthetic 100,000-path reference scan. It is not a
production latency or throughput benchmark.

## Backup filter semantics

`GetBackups("all")` and `GetBackupKeys("all")` are the cheap preliminary
listing on the candidate: latest/not-uncertain metadata and backup-file
existence only, no global reference scan and no hash. The baseline's `all`
filter was verified and is therefore **not equivalent work**. The explicit
`restorable`/`retirable` filters, and their key variants, are the comparable
verified rows: they perform the same authoritative shared/hash/latest checks
on both builds. Do not compare the preliminary `all` row as if it were a
verified-eligibility benchmark.
