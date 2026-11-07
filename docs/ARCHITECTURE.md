# Execution and scope

The sections below describe the published 0.3.0.1 execution engine and legacy
endpoints. The working tree now adds the native UX integration summarized here;
its implemented DTOs are documented in
[UX-P3-INTEGRATION.pt-BR.md](UX-P3-INTEGRATION.pt-BR.md), with
[real-host evidence and limits](UX-P3-NATIVE-VALIDATION.pt-BR.md).
[PLAN.md](PLAN.md) and the [detailed plan](UX-REFACTOR-PLAN.pt-BR.md) distinguish
implementation authority from publication/production deployment, neither of which
follows from local validation.

## Native integration added in the working tree

- The embedded page uses the real Jellyfin web host, dynamically enhanced
  `emby-*` controls and theme styles. Its five task views connect to authenticated
  APIs; prototype fixture data is not shipped in the UI.
- Manual drafts never save automation. Nullable manual defaults project legacy
  values on read without migrating them; explicit saves separate manual defaults
  while preserving legacy automation fields and task triggers.
- Read-only analysis produces a bounded, expiring server-owned plan with explicit
  Convert/Regenerate/Keep actions and stable image keys. Review and billing use
  the exact selected subset across server pages, not scan cursors.
- Apply, restore and retire persist their exact intent before dispatch to the
  existing native task. Operations have individual results, safe cancellation,
  idempotent application, queued retry and conservative restart reconciliation;
  restart never automatically replays an interrupted mutation.
- The existing transaction engine still performs conversion, verified restoration,
  journaling and storage checks. Additional guards protect shared paths and items
  with unavailable sibling images before mutation; reference counts are batched.
- Detailed inventory rehydrates repeated image types through `GetItemById`, matching
  native slot order rather than the differently ordered `GetItemList` result.
- Retention review pages every affected identity and binds consent to its complete
  fingerprint. The persisted operation cannot silently expand that ID set and
  excludes originals needed by selected regenerations.
- Plans/operations are separate from legacy `last-run.json`, scan cursors and
  image journals. Old journals remain readable; optional new fields add dimensions,
  policy/evidence, execution identity and deletion intent.
- New DTOs are path-free and unknown errors sanitized. The elevated legacy
  `/Status` response retains its historical journal/path shape for compatibility;
  new APIs never accept client filesystem paths.

## Published engine / legacy execution path

JellyP uses Jellyfin's existing scheduler. It does not install another process,
filesystem watcher, patched saver or production database writer.

```mermaid
flowchart LR
    UI[Scope and policies] --> Config[Saved configuration]
    UI --> Manual[Manual request with mode override]
    Config --> Timer[Jellyfin task trigger]
    Manual --> Queue[Native task and execution ID]
    Timer --> Queue
    Queue --> Inventory[Global reference inventory]
    Inventory --> Scope[Selected libraries or item IDs]
    Scope --> Preview[Simulation and example results]
    Scope --> Wait[Wait for related metadata activity]
    Wait --> Commit[Verify, journal, save and persist]
```

## Responsibility boundaries

- **Configuration** owns explicit scope, category policies, storage budgets and
  scheduled processing mode. `legacy` preserves old item/global settings; new
  library selections are fail-closed, including deleted library IDs.
- **JellyfinArtworkStore** maps native virtual libraries to physical folder IDs,
  resolves descendants and credited people, inventories references globally and
  saves through native providers/persistence APIs. It contains host-specific query
  and conflict rules; the core never accepts filesystem paths from clients.
- **OptimizationCoordinator** serializes execution, snapshots settings, exposes
  states and counters, waits at transaction boundaries and owns durable image
  journals, recovery copies and per-mode/context scan cursors.
- **Scheduled tasks** keep permanent native keys and delegate execution. Manual
  requests use the same task manager with an execution-specific mode override.
  Their receipt includes task/execution IDs; missing/busy dispatch is explicit.
  Native optimize/synchronize triggers wait cancelably for one another without
  replacing the active report or consuming the other task's pending request.
- **Administrator API/UI** requests manual simulation/application or explicit
  restoration/retirement. Manual mode never rewrites scheduled authorization.
  Permanent storage settings still require explicit user acknowledgment.

## State and durability

One JellyP request/operation is active or pending at a time. A queued request
captures settings and has an execution ID. Progress distinguishes Queued,
Preparing, Inventory, WaitingForMetadata, Processing, Completed, Cancelled and
Failed; restoration and retirement have their own phases. Simulation counts
images that would convert and keeps a path-free sample of at most 20 results.
Inventory publishes inspected-item counts after each hydration batch.

The pending request is in memory, matching native task dispatch. It is not a
persistent job queue and is lost on server restart. Durable image journals and
context-bound preview/apply/synchronization cursors preserve committed work and
recovery. A later native trigger resumes the pass; uncertain image transactions
block applying until recovered. Cancellation is honored between transactions and
while waiting, never by interrupting an in-progress save/persistence sequence.

## Concurrency limits

A full scan blocks writes. Refresh checks concern the target, its parent/owner
chain and its library; unrelated queued metadata and scheduled jobs are allowed.
The wait continues until the conflict clears or native cancellation/runtime limits
apply. The public provider APIs expose queued IDs and active progress but no shared
mutation lock for a plugin transaction. Rechecks plus source/destination hashes
and recovery copies limit risk; they cannot make concurrent manual uploads atomic.

Global reference inventory remains necessary even for one selected library. It
captures scalar IDs once, hydrates 500 at a time and counts every local reference
before filtering candidates. Restricting that reference count to selected libraries
would allow deletion of artwork used outside the scope. People are global entities;
library scope includes credited people, not independent copies per library.

The advanced per-execution image limit bounds work. It is independent of library
selection. Subsequent triggers resume the pass, so the UI does not need to model
libraries as a manually curated sequence of batches.

## Exact-item inspection

The administrator selection endpoint reads at most 100 IDs per request using the
same persisted-item hydration as inventory. It does not render/download images,
call `UpdateImagesAsync`, or accept filesystem paths. Results expose only item
identity and per-slot category/state, with no server paths. The UI refreshes this
inspection on request and after its executions. A preliminary candidate is not a
shared-reference or conversion approval. Inventory can return immediately when
all selected items lack usable local slots; any actual candidate still requires
the complete global reference count.
