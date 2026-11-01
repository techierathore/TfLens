---
project: TfLens
last_updated: 2026-11-01
current_phase: Phase 3 of 3 (Depth) · Verify — 5 not measurable, 91 of 96 verified, 31 not applicable
last_verified_build: PASS
last_verified_date: 2026-11-01
---

# TfLens — Status

## Where I am

Phase 3 of 3 (Depth). Both owner UAT defects of 2026-11-01 are fixed and Verified. REQ-UI-076: Repos shows Framework and Kind (codebase type) as separate columns. REQ-UI-077: a repo picker narrows Gate outcomes, Harness, Routing, Misses and Phase effort to one repo. REQ-UI-072 now passes parity and is Verified. 91 of 96 verified, 31 not applicable. Five Playbook and import rows still cannot be measured without data only the owner can supply.

## Next command to run

Claude Code:
```
(owner) 5 row(s) cannot be measured here — create the data, change the acceptance line, or mark the row N/A: REQ-FN-065, REQ-FN-068, REQ-FN-069, REQ-FN-084, REQ-FN-104
```
OpenCode:
```
(owner) 5 row(s) cannot be measured here — create the data, change the acceptance line, or mark the row N/A: REQ-FN-065, REQ-FN-068, REQ-FN-069, REQ-FN-084, REQ-FN-104
```
Why: every test carrying these ids was skipped, so another verify run changes nothing: REQ-FN-065, REQ-FN-068, REQ-FN-069, REQ-FN-084, REQ-FN-104; working docs/TfLens-P3-Checklist.md.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 5 |
| Blocked | 0 |

- [ ] REQ-FN-065 — events.ndjson` fetch, archive and `PbEvent` tables (BRD-73, Phase 3) (Needs re-verify)
- [ ] REQ-FN-068 — Schema discovery recorded in DECISIONS.md before columns are fixed (BRD-76, Phase 3) (Needs re-verify)
- [ ] REQ-FN-069 — Schema-v1 Playbook repos reuse parser, engine and pages (BRD-109, Phase 3) (Needs re-verify)
- [ ] REQ-FN-084 — Bundle sha256 as dataset identity wherever a commit SHA is used, incl. parity pinning (BRD-134, BRD-70, Phase 3) (Needs re-verify)
- [ ] REQ-FN-104 — Cross-edition axes preserved — `ItemId` beside `ReqId`, `FoundPhaseGate` beside `FoundGate`; the second pair never shares a column or a chart (BRD-165, Phase 3) (Needs re-verify)

## Known blockers

- REQ-FN-065, 068, 069, 104: not measured. They need a connected public Playbook repo that emits these streams, which only the owner can provide.
- REQ-FN-084: not measured. No imported TechieFlow source exists now, and its test does not seed one.
- REQ-UI-077 in the Playbook state was not exercised with a real repo: the demo user has no Playbook repos, so the picker there offers only All repos.
- The repo choice lasts for the browser session; a page refresh or a typed address resets it to All repos.
- Phase 1/2: REQ-UI-032 test compares a 2026-10-12 parity record to re-synced data; REQ-NFR-019 test needs a Release build.
- Chatur, created by an import, is badged `private` on Repos although it is public — owner decision.
- Sevak is not public on GitHub, so it was not imported (owner chose to skip it).

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-15 | log-miss | 117/117 Verified | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-10-12 | fix-issues | 90/90 Verified, 31 N/A | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-11-01 | amend-docs | 90/94 Verified, 31 N/A | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-11-01 | build-phase | 88/94 Verified, 31 N/A | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-11-01 | triage-and-fix | 91/96 Verified, 31 N/A | docs/TfLens-P3-Checklist.md#requirements-status |

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
