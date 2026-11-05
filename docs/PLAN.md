# JellyP UX refactor: handoff and authorization boundary

Status: **P1 product/state contract recorded; P2 prototype visually/behaviorally
revised and browser-tested on 2026-11-05; the P2 usability exercise still awaits
a person without context. Plugin runtime/backend implementation is not authorized.**
P0–P1 are complete under explicit owner
authorization. P3–P8 remain pending a phase-specific instruction.
Baseline: JellyP **0.3.0.1**, Jellyfin **12.1**, .NET **10**.
Recorded: 2026-10-17; P2 revision: 2026-11-05.

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
**History and originals**, and **Settings**. Manual analysis should evaluate the
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
| P2 | Reviewable screen flows and prototype usability exercises | Prototype revised; 96 browser/audit checks passed; [evidence and limits](UX-P2-DESIGN.pt-BR.md); human exercise pending |
| P3 | Analysis/execution/state contracts and migration design | P1 + validated P2 |
| P4 | Backend support for reviewed selections and independent state | P3 |
| P5 | Jellyfin-native UI, navigation and contextual progress | P2 + P4 |
| P6 | Automation, history, restoration and explicit backup retirement | P4 + P5 |
| P7 | Functional, integrity, accessibility and usability validation | P4–P6 |
| P8 | Documentation, migration proof and release preparation | P7; deployment remains separately scoped |

Detailed tasks, acceptance criteria, decision records and scenario IDs are in
the specification. Do not mark phases complete from this summary alone.

## Non-negotiable handoff notes

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
