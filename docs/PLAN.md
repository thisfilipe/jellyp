# JellyP UX refactor: handoff and authorization boundary

Status (2026-11-06): **P1 recorded; P2 accepted by the maintainer as the direction
for native implementation, after iterative use and corrections. Independent
usability testing is deferred and is not a gate for moving forward. Native host
integration and runtime/backend changes have not yet been implemented or authorized.**
P0–P1 are complete under explicit owner
authorization. P3–P8 remain pending a phase-specific instruction.
Baseline: JellyP **0.3.0.1**, Jellyfin **12.1**, .NET **10**.
Recorded: 2026-10-17; execution-plan revision: 2026-11-06.

The owner asked for evaluation and detailed documentation of a complete UX
refactor, explicitly instructing the agent not to modify the implementation yet.
The subsequent request to document the plan authorizes documentation changes;
it does not lift that implementation hold. A later explicit instruction can
authorize a defined implementation phase. Do not interpret this file, its
checklists, or an unchecked task as permission to start coding or deploying.

The subsequent 2026-11-05 instruction authorizes reworking the **synthetic P2
prototype**, local browser validation and its documentation until technically
reviewable. It does not authorize production, backend changes, runtime integration,
publishing or deployment. Automated/visual review is not human usability approval.

The 2026-11-06 instruction authorizes this **plan revision**. It accepts the P2
product direction for implementation without claiming independent usability
validation. It does not itself authorize runtime coding, deployment or a release.

## Start here

Read the complete specification and phase checklist:

**[Plano detalhado da refatoração de UX — Português (Brasil)](UX-REFACTOR-PLAN.pt-BR.md)**

P1 deliverable: **[Contrato de produto e estado](UX-P1-CONTRACT.pt-BR.md)** —
current field/button mapping, D3/D4 decisions, journeys, states and metric
semantics. It records analysis; it does not authorize implementation.

The detailed specification is in Portuguese, matching the owner's evaluation.
It is self-contained and is the authoritative product brief for this proposal.
This file is its English entry point, not a separate competing specification.

Read [ARCHITECTURE.md](ARCHITECTURE.md), [VALIDATION.md](VALIDATION.md),
[RECOVERY.md](RECOVERY.md) and [CONTRIBUTING.md](../CONTRIBUTING.md) for the
current implementation and integrity constraints. These describe existing
behavior, not the proposed UX contract.

## Why a complete refactor is proposed

The current page exposes internal operations through a long shared form.
Simulate/Apply is selected in one section, synchronization is launched in another,
and progress is displayed elsewhere. Manual execution also saves shared scope
and policies. Preview and application have independent cursors rather than a
fixed reviewed selection. Passing functional/browser checks did not establish
usability: the owner repeatedly could not discover the intended workflow.

The proposed design has five areas: **Simulate**, **Execute**, **Automation**,
**History and restoration**, and **Settings**. Manual analysis should evaluate the
desired settings for each image and explain whether it would convert a source,
regenerate from a retained original, leave an image unchanged, or skip it.
The user should not need to discover a separate synchronization button.

Execution should use a reviewed analysis and explicit selected image identities,
revalidating them before mutation. Manual drafts, saved defaults, automation,
analysis results and execution reports need separate ownership. These are target
requirements; they are not implemented in 0.3.0.1.

## Phases and dependencies

| Phase | Deliverable | Dependency / status |
|---|---|---|
| P0 | Context, baseline, constraints and this specification | Documentation recorded; no implementation started |
| P1 | Resolve product/state decisions and map the existing behavior | Delivered: [UX-P1-CONTRACT.pt-BR.md](UX-P1-CONTRACT.pt-BR.md) |
| P2 | Accepted product direction and synthetic prototype | Accepted for native implementation; 311 checks passed (20 axe); [evidence and limits](UX-P2-DESIGN.pt-BR.md); independent usability exercise deferred, not a gate |
| P3 | Map existing backend capabilities to the next native journey; define necessary contracts and tests | P1 + maintainer acceptance of P2; design review is not runtime validation |
| P4 | Minimal backend/API adaptations where the journey requires them | Per-journey P3 decisions; reuse existing services and integrity mechanisms |
| P5 | Real Jellyfin components and end-to-end manual flow | P2 direction + per-journey P3; iterate with P4, not after all backend work is complete |
| P6 | Native automation, history, restoration and explicit backup retirement | Per-journey P3/P4/P5 integration; not a separate rewrite |
| P7 | Continuous functional, integrity, accessibility and usability checks; final regression consolidation | Starts with each implemented journey; independent user feedback continues when available |
| P8 | Document and prepare an alpha after technical release gates pass | Required integrity/migration/host checks pass; disclose deferred usability evidence; publication remains separately authorized |

Detailed tasks, acceptance criteria, decision records and scenario IDs are in
the specification. Do not mark phases complete from this summary alone.

### Native integration first, in complete journeys

P3 is a bounded capability/gap review, not another standalone prototype or a
requirement to prove the unimplemented UI. The existing published backend is the
baseline to reuse, not evidence that every new interaction already has an API.
Reviewed exact selections, independent drafts/automation, pagination and batch
restoration may require adaptations; establish gaps from the actual code.

The first implementation slice should render a real plugin page in a marked
disposable Jellyfin instance using native controls, then connect selection and
analysis to the existing services. Complete that same journey with exact reviewed
application, progress and restoration, adding only necessary backend contracts.
Keep mutating actions unavailable until the slice has safe server-side support;
never disguise a global cursor endpoint as exact selected-image application.

P3/P4/P5/P7 iterate within that slice: specify the boundary, implement it, inspect
the rendered host, exercise the real API and refine. Native appearance, routing,
focus, client/backend behavior and persistence can only be validated there, not
from the synthetic prototype or design documents. P7 is not the first integration
test at the end of a frontend/backend waterfall.

An alpha can collect asynchronous task-based feedback without observed sessions.
It must identify usability limitations honestly. Alpha status does not waive
selection integrity, authorization, recovery, migration, storage or deletion gates.

## Non-negotiable handoff notes

### Explicit P2 product extension (2026-11-05)

The feedback iteration proposes **batch restoration** with explicit eligible
transaction selection, an exact reviewed list, confirmation, revalidation and
per-image results. This is a synthetic UX extension, not implemented backend/API
support. See the [P1 addendum](UX-P1-CONTRACT.pt-BR.md#8-adendo-de-produto-p2--feedback-com-contexto-05112026)
for scope, storage evidence and P3 responsibilities. Internal pages remain the
recommended default; tabs are optional bench comparison, not an immediate binary
decision required from the user. A context-informed basic trial was reported,
with discovery/comprehension friction; it does not complete no-context validation.

The second maintainer-feedback iteration keeps that boundary: a single per-type
status/disclosure, reachable manual stages with Back/history/focus, an explicit
before/after storage bill (active artwork plus the retained original), visible
global storage rules with a focused Settings detour, and one contextual restore
shortcut below completion. Merely navigating preserves the reviewed selection;
saving global changes revokes it and requires reanalysis. Consumed authorization
is never revived; active/completed reports are retained. Synthetic accounting
covers conversion/regeneration, keep/discard, mixed/subset/zero/partial results;
global expiry remains separate. Evidence: `/tmp/opencode/jellyp-p2-round2/`.
This is human feedback **from the maintainer with context**, not a new usability
approval or a backend/storage implementation. All prior 189 checks (14 axe) are
retained within the expanded suite.

- No production conversion, restore, deletion, scan, restart, installation or
  worker resumption follows from a UX documentation request. Use marked
  disposable fixtures for eventual implementation validation.
- Preserve original-based regeneration, hashes, journals, recovery, exact alpha,
  dimensions, quality/savings gates, storage budgets and global shared-reference
  checks. A selected-library filter must not narrow reference protection.
- Do not replace or reinterpret old recovery journals to support a new screen.
  Preserve plugin identity and native task keys/triggers. No version bump or
  release is part of the current documentation task.
- The browser's `image/webp` response is not evidence of the stored source format.
- An item without its own registered images is not an inventory hydration bug
  merely because it has a familiar title. Distinguish evidence from hypothesis.
- Existing tests and controlled restoration evidence are useful baselines, not
  proof that every feature, platform or UX scenario passed.
- Public documents use synthetic examples. Keep production identifiers,
  credentials, paths, artwork, logs and operational records out of the repository.

## How to resume

1. Read this file and the full specification before changing code.
2. Check applicable instructions, the working tree and the current source/version;
   the baseline above can become stale.
3. Read the new user instruction to determine which phase is actually authorized.
4. Resolve the decisions attached to that phase and produce its listed evidence.
5. Record verified outcomes separately from remaining proposals. Preserve work
   belonging to others and do not broaden the production scope.
