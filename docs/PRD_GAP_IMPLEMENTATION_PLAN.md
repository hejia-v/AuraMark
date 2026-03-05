# AuraMark PRD Gap Implementation Plan (v1.0 -> v1.1)

Updated: 2026-03-05

## Scope

This checklist tracks the remaining gaps against `AuraMark_Final_PRD_TDD_v1.2.md` and the execution order for closure.

## P0 (v1.0 release blockers)

| ID | Gap | Current | Action | Acceptance |
|---|---|---|---|---|
| P0-1 | `WindowChrome` parameter mismatch (`CaptionHeight`) | `CaptionHeight=32` (legacy) | Align to PRD constraint and keep drag behavior | Borderless window still draggable/resizable |
| P0-2 | Automated acceptance coverage incomplete | E2E harness only captures Case1 checkpoints | Extend harness for PRD 6.3 Case2-Case5 checkpoints | `run-loop` evidence contains checkpoints for Case1-Case5 |
| P0-3 | Baseline strategy not established | Screenshot diff runs without persisted baseline | Manually seed baseline after visual confirmation | `artifacts/ui-baseline` exists and diff uses it |

## P1 (v1.1 committed items)

| ID | Gap | Current | Action | Acceptance |
|---|---|---|---|---|
| P1-1 | External change conflict decision UI (`覆盖/合并`) | Dirty + external change currently auto snapshot + hot reload | Add conflict prompt and decision path | Conflict path can choose keep local/accept external |
| P1-2 | Startup snapshot recovery | Snapshot write exists; startup recovery missing | Restore latest file-scoped snapshot on startup | Startup can recover latest snapshot and show soft hint |
| P1-3 | PDF export | Only HTML export implemented | Add PDF export entry and pipeline | Exported PDF opens with expected content |
| P1-4 | Structured sync error code (`E_SYNC_CONFLICT`) | Save errors implemented; sync conflict code path missing | Add conflict error payload and soft UI hint | Conflict emits code and visible hint |

## P2 (post-v1.1 optimization)

| ID | Gap | Current | Action | Acceptance |
|---|---|---|---|---|
| P2-1 | Rx-based save debounce alignment | `DispatcherTimer` debounce works | Evaluate migration only if needed | No regression; complexity justified |
| P2-2 | Bundle size warning | Vite chunk warning > 500KB | Split or tune chunks | Build warning removed or documented |

## Iteration Order

1. P0-1, P0-2
2. P1-1, P1-2
3. P1-3, P1-4
4. P2 optional hardening

## Current Round Status

- Completed in this round: `P0-1`, `P0-2`, `P1-1`, `P1-2`, `P1-3`, `P1-4`.
- Optional follow-up: harden PDF export styling and add dedicated automated assertions for conflict decision branch.
