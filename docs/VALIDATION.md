# Validation

The initial release targets Jellyfin 12.1 / .NET 10. Validation covers Linux x64
and a controlled Ubuntu ARM64 installation; other platforms and Jellyfin versions
remain unvalidated.

## Automated checks

The 0.3.0.0 integrated .NET suite contains 116 passing tests. Coverage includes
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
The 0.3.0.0 browser exercise uses native Jellyfin controls at desktop and mobile
widths, with no horizontal overflow or JavaScript errors. It checks all 11
categories, saved global/per-category values, byte-unit conversions, reload
persistence, language changes retaining unsaved edits, conditional fields,
numeric validation, empty library/item selection, library descendants, item search, apply guards,
canceling permanence confirmation, simulation auto-save/polling, and the emergency
shortcut's unsaved behavior. It also verifies separate manual/scheduled modes,
execution identity, simulated counts and named examples, plus the native scheduling link. An optional controlled case converts the single
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

## Library isolation and native scheduling (0.3.0.0)

The disposable instance contains two generated libraries. A real interval trigger
in Jellyfin's scheduler ran JellyP in saved applying mode without a maintenance
confirmation. Only the selected library changed, including its own cover where
eligible; the excluded library's image metadata stayed identical. A manual preview
preserved applying automation and explained already-WebP images. Explicit restore
worked after automation returned to preview and restored the exact source checksum.

Run `scripts/smoke-scope.py --root <marked-fixture-root>` after `smoke-local.py`.
It creates the second synthetic library, installs a temporary native interval
trigger, waits for its actual firing and restores the previous triggers/settings.
Allow about two minutes: Jellyfin 12.1's
[interval trigger](https://github.com/jellyfin/jellyfin/blob/v12.1/Emby.Server.Implementations/ScheduledTasks/Triggers/IntervalTrigger.cs)
adds a minimum minute even for a short configured interval.

Unit regression coverage includes manual mode overrides, captured configuration,
queued cancellation, busy/missing/failed task dispatch, concurrent requests,
cancelable metadata waits, serialized native optimize/sync with cancellation, library isolation, associated people, protected shared
files outside the selected scope and unrelated refresh coexistence. These checks
do not claim a shared native lock against simultaneous manual uploads.

## Empty selected-item regression (0.3.0.1)

The suite contains 124 passing .NET tests. Added coverage verifies path-free,
bounded administrator inspection; image-less, remote-only, missing, external and
already-WebP items; and avoiding global inventory only when no local candidate
exists. Existing shared-reference checks still inspect references outside scope.
The browser fixture removes only its synthetic Primary, checks the warning before
execution, runs a zero-candidate simulation, verifies one inspected item and the
completed banner, restores the generated image and verifies inspection refresh and
absent checked types. Conversion, synchronization and exact restoration remain
covered by the disposable functional exercise.

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

The `v0.2.0.1` workflow also passed. Its public archive and SHA256 were
downloaded independently, its MD5 matched the raw manifest, and previous catalog
entries were preserved unchanged. A disposable instance running the public
`0.1.0.0` release upgraded through Jellyfin's native installer to `0.2.0.1`.
After restart, the plugin was active with its new embedded page, no pending
recovery and no task triggers. Non-default global quality, batch size, item
allowlist and per-type settings matched the pre-upgrade configuration exactly.

The `v0.3.0.0` build and release workflows passed with all 116 .NET and nine
release checks. The public archive/SHA256 and canonical raw catalog were downloaded
independently: ZIP layout, SHA256, catalog MD5 and versioned URL matched, and all
previous catalog entries remained unchanged. A fresh disposable instance installed
public `0.2.0.1`, saved non-default global quality, execution limit, individual-item
scope and a per-type override, then upgraded natively through the same catalog to
`0.3.0.0`. After restart all prior configuration values survived, the new scope
remained `legacy` with no selected libraries, both native tasks stayed unscheduled,
the embedded library/automation UI loaded and recovery status was clear.

Reproduce with [smoke-catalog.py](../scripts/smoke-catalog.py), following the
marked-root and two-phase instructions in its docstring and
[RELEASING.md](RELEASING.md).

## Baseline upgrade harness (12.1)

[smoke-upgrade.py](../scripts/smoke-upgrade.py) recreates and manages only a
marked disposable fixture on `127.0.0.1:18096`. It completes the 12.1 wizard,
then checks the authenticated `ProgramDataPath` after login before media/library
creation, creates a synthetic library, installs the local baseline, saves an exact
legacy item scope with real interval/daily task triggers, and generates a
journal and retained original through the baseline API. After the candidate is
installed, it verifies byte-identical XML/journal/backup/output, unchanged
scope/safe mode/triggers, and restores the old journal through the current API
with an exact SHA256 check. It also refuses unmarked/symlinked roots, checks the
authenticated `ProgramDataPath` after login, and stops only the exact process it
started.

```sh
python3 scripts/smoke-upgrade.py \
  --root /tmp/opencode/jellyp-upgrade-harness \
  --phase all \
  --candidate /tmp/opencode/jellyp-alpha-check/upgrade-candidate/JellyP_0.3.0.1.zip \
  --expected-dll-sha256 <candidate-dll-sha256>
```

The hardened 08/11/2026 run passed 33 harness checks. In the same validation
round, separate disposable fixtures passed 121 native UI checks without the
journey, 819 with the journey, and 20 planning checks; the installed DLL
matched the local build and differed from the baseline. The harness now
requires `--expected-dll-sha256` for `upgrade`/`all`/`verify`, checks the effective
loopback socket and process owner, refuses an unmarked/symlinked root before
stop/install, proves the loaded assembly path and current embedded page, and
refuses a second restore of a consumed backup. The release suite gained 13
harness tests (30 total in this round). This is a local compatibility gate, not
a new release identity. See
[UPGRADE-VALIDATION.pt-BR.md](UPGRADE-VALIDATION.pt-BR.md) for the fixture
layout, official package hashes, exact observed hashes and limitations.

## Deterministic V08/V10/V15 gates (08/11/2026)

This round did **not** start a Jellyfin host, fill a disk, alter OS permissions
or expose a fault-injection endpoint. It exercised real product classes against
real temporary files and used fake adapters only at host/IO boundaries where a
signalled barrier makes the test deterministic. The full .NET suite passed with
**204 tests**, the release suite with **32**, the Release build had zero warnings
and errors, Python/CJS syntax checks passed, and the publication audit passed.
The historical 121/819/20 UI/planning runs were not repeated because only backend
cancellation/wait/recovery code changed.

Reproduce the targeted gate tests with:

```sh
dotnet test JellyP.slnx -c Release --no-restore --filter \
  "FullyQualifiedName~CancelDuring|\
FullyQualifiedName~CancelBatchRestore|\
FullyQualifiedName~CancelAfterMetadataWait|\
FullyQualifiedName~FinalizedOperationIsNotReverted|\
FullyQualifiedName~ConcurrentCancellationRequests|\
FullyQualifiedName~CancellationAfterLegacyMetadataWait|\
FullyQualifiedName~CancelExecution|\
FullyQualifiedName~NativeTaskDoesNotWait|\
FullyQualifiedName~NativeTaskWaitsForRelated|\
FullyQualifiedName~UnrelatedRefreshProgress|\
FullyQualifiedName~ConfiguredFreeSpaceReserve|\
FullyQualifiedName~PrePublicationMarkerFailure|\
FullyQualifiedName~PostPublicationMarkerFailure|\
FullyQualifiedName~ApplyPersistenceFailure|\
FullyQualifiedName~RestartDuringRestore"
```

Exact layers:

- Real product code: `OptimizationCoordinator`, `OptimizeArtworkTask`,
  `SynchronizePoliciesTask`, `JellyfinArtworkStore.EnsureIdle`, journals,
  `DurableFiles`, WebP conversion and explicit recovery.
- Deterministic fake edges: an `IArtworkStore` test double with `EnsureIdle` and
  `SaveAsync` barriers, plus fake `ILibraryManager`/`IProviderManager` services
  for refresh queue/progress and library scan. No HTTP API responses are mocked.
- Controlled test seams: private hooks set by reflection simulate concurrent
  finalization and marker-write failure before/after publication. There is no
  public fault endpoint or release fault behavior.
- Not exercised: Jellyfin scheduler dispatch, real SIGKILL of a fixture process,
  physically full disk, OS permission failures, and browser disconnect/focus.

V08 cancellation evidence:

- `CancelDuringApplyValidationLeavesPlanReadyAndNeverMutates` cancels an exact
  `operationId` after validation and before mutation; the plan stays `Ready`,
  zero saves occur and no journal is written. Before the fix the plan became
  `Consumed` without a mutation.
- `CancelDuringFirstApplyMetadataWaitLeavesPlanReadyAndNeverMutates` cancels the
  first item's metadata wait: no `MutationStarted`, no save and the plan returns
  to `Ready`. Before the fix the plan was consumed before the wait.
- `CancelDuringFirstRetentionWaitLeavesPlanReadyAndNoRemoval` cancels the first
  retention wait: no backup removal, no mutation marker and a `Ready` plan.
- `CancelBatchRestoreDuringFirstMetadataWaitLeavesBackupAndNoRestore` cancels a
  batch restore during its first metadata wait: the item stays `NotStarted`, the
  WebP and stored original remain, and the operation is `Cancelled`.
- `CancelAfterMetadataWaitDoesNotStartTheNextTransaction` cancels while the
  second image waits for metadata; the first stays `Changed`, the second becomes
  `NotStarted`, the operation is `Cancelled`, only one save occurs and the plan
  stays `Consumed` after the first real mutation. Before the fix the second
  transaction ran and the operation completed.
- `FinalizedOperationIsNotRevertedByCancellationSnapshot` simulates a
  finalization write between the cancellation read and persistence: the terminal
  state, `MutationStarted` and progress must survive, and the cancellation
  request returns false. Before the fix the stale `Running` snapshot overwrote
  the completed record.
- `CancelExecutionReportsTerminalStateWhenFinalizationWinsTheRace` covers the
  endpoint after the pre-check: it re-reads the current record and returns
  `409 OperationTerminal` without requesting native cancellation.
- `ConcurrentCancellationRequestsPreserveProgress` sends two cancellation
  requests concurrently and preserves `Running`, `CancelRequested`,
  `MutationStarted` and persisted progress.
- `CancellationAfterLegacyMetadataWaitDoesNotStartTransaction` covers the legacy
  native batch wait: no save, original intact, report `Cancelled`.
- `CancelExecutionTargetsOnlyTheExactQueuedOperationAndReleasesItsPlan` covers
  the controller endpoint: the exact queued operation becomes `Cancelled`, its
  plan returns to `Ready`, the native task cancellation is requested, and an
  unknown ID returns 404 without cancelling another operation.
- Existing queue/wait regressions remain:
  `CancelBeforeNativeStartLeavesPlanReadyAndNeverFallsBackToAutomation`,
  `CancelWhileWaitingPersistsEvenAgainstConcurrentProgressSaves`, and
  `CancelQueuedRequestNeverFallsBackToApplyingSavedSettings`.

V10 scheduler/metadata evidence:

- Layer distinction: the `OptimizeArtworkTask` class and coordinator are real, but
  the native-task test uses the deterministic `IArtworkStore` double. The
  `EnsureIdle` tests exercise real `JellyfinArtworkStore` code over fake
  `ILibraryManager`/`IProviderManager` service interfaces. Neither mocks HTTP.
- `NativeTaskDoesNotWaitForUnrelatedMetadataRefresh` runs the real native task
  class while another item's metadata refresh is busy; the target converts and
  the unrelated item remains untouched.
- `NativeTaskWaitsForRelatedMetadataAndResumes` observes `WaitingForMetadata`
  with zero saves for a related refresh, then resumes and completes after the
  barrier is released.
- `UnrelatedRefreshProgressDoesNotBlockButRelatedProgressDoes` and
  `UnrelatedRefreshDoesNotBlockButTargetLibraryAncestorAndScanDo` exercise the
  real `JellyfinArtworkStore.EnsureIdle`: unrelated refresh queue/progress is
  accepted; the item, its parent, its library or a running scan is rejected.
- Existing native-task serialization/cancellation coverage remains in
  `NativeOptimizeAndSyncSerializeWithoutFailingEachOther` and
  `CancellingNativeWaiterLeavesActiveReportAndGateIntact`.

V15 budget/persistence/interruption evidence:

- `ConfiguredFreeSpaceReserveDeniesBeforeBackupOrSave` uses a configured reserve
  above available space, not a full disk: denial happens before backup or save,
  the original remains, zero saves occur and no journal/backup is created.
- `OrphanOriginalFilesCountAgainstBudget` keeps orphan originals inside the
  backup budget instead of deleting them silently.
- `PrePublicationMarkerFailureReleasesPlanWithoutMutation` injects a failure
  before the durable mutation marker is published: the plan stays `Ready`,
  `MutationStarted` stays false and no save occurs. The marker is written from a
  cloned record before the shared in-memory state is changed.
- `PostPublicationMarkerFailureKeepsPlanReservedConservatively` simulates a
  failure after publication: the durable record is re-read; when it already says
  `MutationStarted=true`, the plan stays `Reserved` and the operation fails
  conservatively instead of releasing the reservation under uncertainty.
- `ApplyPersistenceFailureRestoresOriginalAndLeavesNoRecovery` injects a
  persistence failure after the save: the original is restored, the journal is
  `RolledBack`, pending recovery is zero and no restorable backup remains.
- `RestartDuringRestoreRequiresExplicitRecoveryAndRemovesVerifiedOutput` uses a
  state barrier to simulate a crash between publishing the original and writing
  the `RolledBack` journal. A new coordinator blocks new mutations; explicit
  recovery restores the bytes, removes the verified WebP output, deletes the
  backup and leaves zero pending recovery.
- Existing prepared-journal, partial-output, orphan-cleanup and rolled-back
  deletion-intent regressions remain.

The real-host layer for V08/V10/V15 was exercised separately on 09/11/2026; see
the next section. Browser disconnect/navigation/focus remains outside this
backend round.

## Real-host V08/V10/V15 gates (09/11/2026)

A new marked disposable fixture (`/tmp/opencode/jellyp-host-gates-proof5-20261109`)
ran **80 host checks** against official Jellyfin 12.1 on `127.0.0.1:18096`.
The fixture used new credentials, verified the authenticated `ProgramDataPath`
after login and before library creation/API mutations (synthetic media and the
test plugin were already confined to the marked root), and stopped at the end
with the port free. The source revision was
`e21328af68d17e3df427ae7398125a21dee981aa`; the installed candidate and local
build DLL SHA256 was
`3AE44BA2A35FAFC08EDF03D91D0BCF5E9FE0B3F981EC578089E0E5330FBC3D82`.
No production, SSH, system service, package installation, publication or
`artifacts/` change was involved. `ffmpeg` and `inotifywait` are explicit
fixture dependencies; the harness preflights them and never runs a package
manager.

Reproduce with `scripts/smoke-host-gates.py`; it reuses the guards from
`scripts/smoke-upgrade.py` and installs the test-only
`tests/host-fixture/JellyP.TestSlowMetadata` provider under the fixture. The
provider delays only paths containing `Slow Refresh`; it is not a product fault
endpoint and is not packaged with JellyP.

Exact layers:

- Real product: `OptimizationCoordinator`, `OptimizeArtworkTask`,
  `JellyfinArtworkStore.EnsureIdle`, durable journal, WebP conversion, the
  `Executions/{id}/Cancel` and `Rollback/{id}` endpoints.
- Real host: Jellyfin interval scheduler, `RefreshLibrary`, real metadata refresh
  through `ProviderManager`, loopback bind and verified PID.
- Test-only edge: the slow local metadata provider holds a real library refresh
  open. No HTTP responses are mocked.
- Not exercised: browser navigation/disconnect/focus, a physically full disk,
  OS permission failures, hardware faults, and the historical 121/819/20 UI and
  planning runs.

Results:

- V08 before mutation: `WaitingForMetadata`, `MutationStarted=false`, exact
  `operationId` cancelled, terminal `Cancelled`, `Changed=0`, `NotStarted=1`,
  plan back to `Ready`, zero journal and byte-identical synthetic PNG.
- V08 between transactions: first item `Changed=1` with a `Committed` journal
  and plan `Consumed`; second waited in `WaitingForMetadata`; cancellation
  produced `Cancelled`, `NotStarted=1`, no second journal, plan stayed
  `Consumed`, second image byte-identical.
- V10 related: refresh was observed `Active` at
  `2026-11-09T20:40:13.313984Z`, the interval trigger was installed after it
  (`20:40:13.329161Z`), and the scheduler started at
  `2026-11-09T20:41:14.3415031Z`. The task observed `WaitingForMetadata` with no
  mutation/journal while the related library refresh was active; after explicit
  provider release and observed Idle, the same execution resumed, converted one
  image and wrote a `Committed` journal.
- V10 unrelated: refresh was `Active` at `2026-11-09T20:41:16.004048Z`, the
  trigger was installed after it, and the scheduler started at
  `2026-11-09T20:42:17.0115903Z`. The committed journal was observed while the
  unrelated refresh was still `Active`; only then was the provider released. It
  never observed `WaitingForMetadata`, and the witness item stayed untouched.
- V15: `MutationStarted=true` and a `Prepared` journal were observed; only the
  verified PID (argv, cwd, UID, starttime and loopback socket) received
  `SIGSTOP`/SIGKILL through a Linux pidfd. Restart of the same DLL produced
  `Interrupted`, `MutationStarted=true`, `PendingRecovery=1`, journal count
  `1→1` and operation count `3→3` (no replay). The observed case was
  **Prepared-before-save, no output remains**: the source PNG and retained
  backup existed before kill, but no WebP output existed yet, so
  `outputRemovalProven=false` and output removal was not claimed. Explicit
  recovery restored the exact synthetic PNG
  (`FE84D7D80047B1696D72D46EE606B5B711457DB9DCC1E5EB5B4E0D1EB6BD4C97`),
  left the journal `RolledBack`, `BackupDeleted=true`, `PendingRecovery=0`, and
  no output at the recorded path.

## Targeted V15 output-exists rerun (09/11/2026)

The earlier host round could only interrupt before the WebP save. A targeted
rerun on the same marked disposable fixture
(`/tmp/opencode/jellyp-host-gates-proof5-20261109`) closed that gap without
changing product code. The source revision was
`d097fb69f5a88e6d3dac89c80d6dd322c29e13de`; the installed candidate DLL remained
`3AE44BA2A35FAFC08EDF03D91D0BCF5E9FE0B3F981EC578089E0E5330FBC3D82` and the
host was official Jellyfin 12.1 on `127.0.0.1:18096`.

The test-only fixture plugin gained a pass-through `ILibraryMonitor` decorator.
The harness arms it with the exact item metadata folders; the decorator blocks
the real image save in `ReportFileSystemChangeComplete` immediately after the
WebP bytes are written, records the completed path atomically in
`test-image-barrier.reached`, and waits for `test-image-barrier.release`. With
no barrier file the decorator only forwards calls. This is a fixture seam, not
a product fault endpoint; no JellyP code was added or changed.

Observed V15 state before `SIGKILL`:

- Journal `4d96a819ace94d6ab410d26c3b2b8228` was `Prepared`; the operation had
  `MutationStarted=true`; the retained original and the still-present source PNG
  both had SHA256
  `FE84D7D80047B1696D72D46EE606B5B711457DB9DCC1E5EB5B4E0D1EB6BD4C97`.
- The output WebP already existed at
  `.../metadata/library/62/62a4947a71a685717abcdc7db1b4382d/poster.webp` with
  SHA256 `66E7862D125ABB0FE527A3186960F3463D010176D8DAD513135B8AEBA6AF127B`
  (81,016 bytes), exactly the journal `OutputHash`, before the verified PID
  `994224`/starttime `11405136` was frozen and killed through `pidfd`. The
  inotify watcher also observed the `Prepared` journal rename.
- Restart of the same DLL produced PID `994926`/starttime `11407519`, marked the
  operation `Interrupted` with `PendingRecovery=1`, left the WebP untouched, and
  did not replay: journal count `1→1`, operation count `6→6`.
- Explicit recovery removed the WebP that existed before kill
  (`outputRemovalProven=true`), restored the exact synthetic PNG, left the
  journal `RolledBack`, `BackupDeleted=true`, `PendingRecovery=0`, and did not
  rewrite the interrupted operation. The harness ended with safe automation
  (`Enabled=false`, `DryRun=true`, no triggers) and the fixture stopped with the
  port free.

The targeted rerun passed **37 V15 checks**. The release suite gained three
proof-condition/barrier helper tests; **59 release tests** passed. V08/V10 were
not rerun because product code and their harness paths were unchanged; the
historical 80-check round remains the reference for those scenarios.

The release suite gained host-gate helper and stop-guard tests; **56 release
tests** passed. The product Release build had zero warnings/errors. The 204-test
.NET suite was not repeated because product code did not change in this round.
See [ALPHA-CLOSURE.pt-BR.md](ALPHA-CLOSURE.pt-BR.md) section 7 for the Portuguese
evidence and exact commands.
