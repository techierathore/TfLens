---
project: TfLens
last_updated: 2026-09-06
current_phase: Build — 2 not built, 170 of 180 verified
last_verified_build: PASS
last_verified_date: 2026-09-02
---

# TfLens — Status

<!--
  ============================================================================
  THIS FILE IS A CRISP, FIXED-SHAPE SNAPSHOT — OVERWRITE IT, NEVER APPEND TO IT.
  It has exactly the sections below and NO others. It should stay well under
  ~60 lines — a human reads it in ten seconds.
  See .tfcore/tasks/_status-update-gate.md §"CRISP, FIXED-SHAPE snapshot".
  ============================================================================
-->

## Where I am

Build — 170 of 180 Verified, 2 In Progress, 4 Implemented, 4 Needs re-verify, none Blocked. This pass wrote a
VPS-only `docs/TfLens-Deployment-Checklist.md` from `docs/claude-code-deployment-brief-v3.2.md` (the old,
mixed-target document archived), and added Q9/Q10 Stack-decision rows to the Architecture, citing the brief.
Nothing has been deployed; REQ-NFR-025's one-time owner setup is still open.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TfLens
```
OpenCode:
```
/flow-master *build-phase TfLens
```
Why: 2 rows are not built yet: REQ-NFR-024, REQ-NFR-025.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 2 |
| Implemented | 4 |
| Needs re-verify | 4 |
| Blocked | 0 |

- [ ] REQ-NFR-024 — no per-developer/machine state tracked in version control (In Progress)
- [ ] REQ-NFR-025 — VPS production deploy is a pipeline, one-time human setup documented once (In Progress)
- [ ] REQ-NFR-019 — stored provenance is real: no row claims a `source_sha` outside its repo (Implemented)
- [ ] REQ-NFR-020 — a built screen is graded against its approved mockup, mechanically (Implemented)
- [ ] REQ-UI-050 — Playbook axis of `/effort` (Phase 3) (Implemented)
- [ ] REQ-UI-051 — `/misses` Playbook axis (Phase 3) (Implemented)
- [ ] REQ-FN-063 — `parity-last.json` + DECISIONS.md record of each pass (Needs re-verify)
- [ ] REQ-FN-067 — Playbook-native report data (Phase 3) (Needs re-verify)
- [ ] REQ-FN-070 — full Playbook report set incl. snapshot export (Phase 3) (Needs re-verify)
- [ ] REQ-UI-033 — quotable banner + last-parity card (Needs re-verify)

## Known blockers

- OWNER — VPS deploy not yet run: DNS A record, hand-written Caddy site file, Seq API key, and the four
  repo secrets (`SEQ_API_KEY`, `TFLENS_GITHUB_TOKEN`, `TFLENS_APPMANAGER_API_KEY`/`_SECRET`) are unset —
  `docs/TfLens-Deployment-Checklist.md` §3 is the tick list (REQ-NFR-025).
- OWNER — TechieBlog's own telemetry carries 14 records with `ended` before `started`; not TfLens's to fix
  (BRD §1). Holds REQ-FN-063's §13 diff at 4 findings.
- OWNER — a Playbook-emitting repository would close REQ-UI-050/051, REQ-FN-067/070 (built, owner-gated).
- UPSTREAM — TF-015 (`tf-emit.sh` accepts `ended` before `started`), TF-011 (parity gate blind spots),
  TF-005/007/008/009/010/013/014 — see `docs/TfLens-TechieFlow-Feedback.md`.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-01 | `*fix-issues` — the 2026-08-30 UAT drift, closed | Eight REQs → `Verified`. `mockup-parity` 39 → 18 findings. Root cause: `Harness.razor.css` had not parsed for two days | docs/TfLens-Checklist.md#requirements-status |
| 2026-09-01 | `*verify` — the `/gate-outcomes` rename | 0 findings from the rename; `REQ-UI-018` held on a real colour drift | docs/TfLens-Checklist.md#requirements-status |
| 2026-08-30 | `*build-phase` (FIX) — harness repair + mockup anchor sweep | 60 anchors added to 3 mockups: `harness` 22 → 135 comparisons | docs/TfLens-Checklist.md#requirements-status |
| 2026-08-29 | `*build-phase` + `*verify all` | New `mockup-parity` gate: 44 findings → 8 UI rows demoted, then repaired to 0 | docs/TfLens-Checklist.md#requirements-status |
| 2026-09-06 | `deploy-checklist` — VPS Deployment Checklist rewritten | New `docs/TfLens-Deployment-Checklist.md` (VPS only, 0 FAIL), Architecture Q9/Q10 added, old checklist archived | docs/TfLens-Checklist.md#requirements-status |

## Library feedback summary

- AppManager: 14 open of 14 — docs/TfLens-AppManager-Feedback.md
- TechieFlow: 61 open of 61 — docs/TfLens-TechieFlow-Feedback.md
- TrBlazeUI: 2 open of 2 — docs/TfLens-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-09-02: Guardrails 119/119, Core 647/647, Integration 49/49 (Release). 11 of 180 checklist
  rows carry an unescaped `|` in Remarks — unfixed, see the checklist Remarks.

## Deferred / future

- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- Sparklines on Coverage/Gate-outcomes tiles — deliberately not built, no stored series behind them (BRD §1)
- `REQ-UI-027`'s `models` column renders raw JSON — no gate or acceptance criterion covers it
- Chart series colours are ungraded — BRD-144 names them, no anchor exists on either side
