---
project: TfLens
last_updated: 2026-09-08
current_phase: Phase 3 of 3 (Depth) · Build — 12 not built, 51 of 73 verified
last_verified_build: PASS
last_verified_date: 2026-09-02
---

# TfLens — Status

## Where I am

Amended today for the framework's 2026-09-07 stream changes, **split into three phases**, then
repaired: every checklist row now has a one-behaviour acceptance line, a valid status and a resolvable
detail entry, and all three pass strict with nothing suppressed. Phase 3 is the working phase. Three
new requirements are corrections — the cross-project rate, the time totals and the first-pass rate are
each computed wrongly today, all flattering the figure. Nothing is built for them yet.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TfLens
```
OpenCode:
```
/flow-master *build-phase TfLens
```
Works phase 3 (`docs/TfLens-P3-Checklist.md`). Start with REQ-FN-111/112/113 — they change published numbers.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 12 |
| In Progress | 2 |
| Implemented | 4 |
| Needs re-verify | 11 |
| Blocked | 0 |

- [ ] REQ-FN-111 — key a requirement by `(project, req_id)`, never by the id alone (BRD-178)
- [ ] REQ-FN-112 — derive a run's `duration_s` when the record omits it (BRD-179)
- [ ] REQ-FN-113 — derive `attempt` when the record omits it (BRD-180)
- [ ] REQ-FN-106 — parse and store `sort` on a miss, closed vocabulary (BRD-170)
- [ ] REQ-FN-107 — `sort` denominator is the misses eligible to carry it (BRD-172)
- [ ] REQ-FN-108 — accept `kind: "review"` as a fourth record kind (BRD-174)
- [ ] REQ-FN-109 — state how many values an amendment completed (BRD-176)
- [ ] REQ-FN-110 — keep `req_class: "FR"` out of every application figure (BRD-177)
- [ ] REQ-FN-114 — never drop a record for an unfamiliar value (BRD-181)
- [ ] REQ-UI-052/053/054 — the `sort` band, the `what` sentence on each row, the review-cost band
- (19 more open rows in docs/TfLens-Checklist.md)

## Known blockers

- OWNER — VPS deploy not yet run: DNS A record, hand-written Caddy site file, Seq API key, and the four
  repo secrets (`SEQ_API_KEY`, `TFLENS_GITHUB_TOKEN`, `TFLENS_APPMANAGER_API_KEY`/`_SECRET`) are unset —
  `docs/TfLens-Deployment-Checklist.md` §3 is the tick list (REQ-NFR-025).
- OWNER — TechieBlog's own telemetry carries 14 records with `ended` before `started`; not TfLens's to fix
  (BRD §1). Holds REQ-FN-063's §13 diff at 4 findings.
- OWNER — a Playbook-emitting repository would close REQ-UI-050/051, REQ-FN-067/070 (built, owner-gated).
- None from the phase split or the checklist repair — both done. `docs/TfLens-Phases.md` is the map; no
  id was renumbered. BRD-182..188 now own the seven quality rules that were being graded without one.
- UPSTREAM — TF-017 (`tf-split-brd.py --add-missing` cannot read an anchored ledger line), TF-016,
  TF-015, TF-011, TF-005/007/008/009/010/013/014 — see `docs/TfLens-TechieFlow-Feedback.md`.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-08 | `*amend-docs` — stream fields, phase split, checklist repair | 19 added, 7 edited, 194 rows across 3 phases, 194 acceptance lines rewritten, 0 FAIL strict | docs/TfLens-P3-Checklist.md#requirements-status |
| 2026-09-06 | `deploy-checklist` — VPS Deployment Checklist rewritten | New `docs/TfLens-Deployment-Checklist.md` (VPS only, 0 FAIL) | docs/TfLens-Checklist.md#requirements-status |
| 2026-09-01 | `*verify` — the `/gate-outcomes` rename | 0 findings from the rename; `REQ-UI-018` held on a real colour drift | docs/TfLens-Checklist.md#requirements-status |
| 2026-08-30 | `*build-phase` (FIX) — harness repair + mockup anchor sweep | 60 anchors added to 3 mockups: `harness` 22 → 135 comparisons | docs/TfLens-Checklist.md#requirements-status |
| 2026-08-29 | `*build-phase` + `*verify all` | New `mockup-parity` gate: 44 findings → 8 UI rows demoted, then repaired to 0 | docs/TfLens-Checklist.md#requirements-status |

## Library feedback summary

- AppManager: 14 open — docs/TfLens-AppManager-Feedback.md
- TechieFlow: 61 open — docs/TfLens-TechieFlow-Feedback.md
- TrBlazeUI: 2 open — docs/TfLens-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-09-02: Guardrails 119/119, Core 647/647, Integration 49/49 (Release). 11 of 192 checklist
  rows carry an unescaped `|` in Remarks — unfixed, see the checklist Remarks.

## Deferred / future

- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- Sparklines on Coverage/Gate-outcomes tiles — deliberately not built, no stored series behind them (BRD §1)
- `REQ-UI-027`'s `models` column renders raw JSON — no gate covers it
- Chart series colours are ungraded — BRD-144 names them, no anchor exists
- Filling `sort` in on the 286 older misses — the owner's editorial call; the new band has little to show until then
- Phases 1 and 2 are complete; `appPhase: 3` in core-config is what scopes every command from here
