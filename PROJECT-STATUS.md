---
project: TfLens
last_updated: 2026-11-10
current_phase: Phase 4 of 4 (Estimates) · Build — 9 not built, 0 of 9 verified
last_verified_build: PASS
last_verified_date: 2026-11-01
---

# TfLens — Status

## Where I am

Phase 4 of 4 (Estimates), added 2026-11-10 by *amend-docs: TfLens reads TechieFlow v1.3.0's estimates.jsonl and shows each phase's estimate beside the measured runs on a new /estimates page, never as one number. BRD-211..BRD-219, nine rows Not Started; proven on the made-up Sample app test files. appPhase is now 4. Phase 3 stays kept-whole with five rows not measurable. REQ-NFR-009 needs re-verify (BRD-89 now covers the estimate rule).

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TfLens
```
OpenCode:
```
/flow-master *build-phase TfLens
```
Why: 9 rows are not built yet: REQ-FN-147, REQ-FN-148, REQ-FN-149, REQ-UI-078, REQ-UI-079, REQ-UI-080 …; after this build, 1 built rows of other phases wait for a verify — phase 2: 1 (REQ-NFR-009); working docs/TfLens-P4-Checklist.md.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 9 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 0 |
| Blocked | 0 |

- [ ] REQ-FN-147 — Estimate stream into its own table (BRD-211) (Not Started)
- [ ] REQ-FN-148 — Reconciliation, port of `analyse_estimates` (BRD-212) (Not Started)
- [ ] REQ-FN-149 — An estimate never pools with a measurement (BRD-213) (Not Started)
- [ ] REQ-FN-150 — Total and per-row actual, no oracle (BRD-217) (Not Started)
- [ ] REQ-FN-151 — `estimates` block in `tflens.json` (BRD-218) (Not Started)
- [ ] REQ-NFR-027 — Code-only figures; UAT `not measured` (BRD-219) (Not Started)
- [ ] REQ-UI-078 — Estimates page and parts table (BRD-214) (Not Started)
- [ ] REQ-UI-079 — Per-row drill-down (BRD-215) (Not Started)
- [ ] REQ-UI-080 — Empty and partial states (BRD-216) (Not Started)

## Known blockers

- The parity check very likely reports the oracle's new `estimates` block missing since 2026-11-08, until REQ-FN-151 is built.
- No estimate exists for TfLens's own phase 4; run `*estimate-phase TfLens` before the build if one is wanted.
- Phase 3: REQ-FN-065, 068, 069, 104 need a connected public Playbook repo; REQ-FN-084 needs an imported TechieFlow source.
- Phase 1/2: REQ-UI-032 test compares a 2026-10-12 parity record to re-synced data; REQ-NFR-019 test needs a Release build.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-11-01 | amend-docs | 90/94 Verified, 31 N/A | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-11-01 | build-phase | 88/94 Verified, 31 N/A | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-11-01 | triage-and-fix | 91/96 Verified, 31 N/A | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-11-10 | amend-docs | 0/9 Verified | docs/TfLens-P4-Checklist.md#requirements-status |
| 2026-11-10 | amend-docs | 0/9 Verified | docs/TfLens-P4-Checklist.md#requirements-status |

## Library feedback summary

- AppManager: 0 open · 2 closed — docs/TfLens-AppManager-Feedback.md
- TechieFlow: 0 open · 20 fixed upstream, not yet re-checked (TF-018, TF-021, TF-020 …) · 36 closed — docs/TfLens-TechieFlow-Feedback.md
- TrBlazeUI: 0 open · 38 closed — docs/TfLens-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-11-01: 0 findings, see the checklist Remarks.

## Deferred / future

- Phase 1: REQ-NFR-024 stays In Progress; its remaining clause is upstream (TF-014).
- Show an imported source as public when GitHub says it is public (Chatur).
- Draw the drift-table pager in docs/mockups/routing.html, or decide it should not page.
- Put the sidebar subtitle back beneath the lockup if the owner wants it (BRD-209 retired it).
- Make the REQ-FN-084 test seed its own imported TechieFlow source, so it never depends on another spec's leftovers.
- Keep the repo choice across a page refresh (a cookie, like the Framework switch), if the owner wants it.
