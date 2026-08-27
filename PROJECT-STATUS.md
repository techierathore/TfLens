---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-27
current_phase: Verify — 106 of 113 Verified; tail is Playbook axis + the unrun parity gate
last_verified_build: PASS
last_verified_date: 2026-08-27
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
`*verify all` ran 2026-08-27 against a Release build on `:5099` (Blazor Server + PostgreSQL 16 + the live AppManager) and graded all 113 in-scope REQs. **106 are now `Verified`, 5 `PARTIAL`, 1 `Needs re-verify`, 1 `Implemented`, 1 `N/A`.** Evidence: **337/337 .NET tests**, **38 Playwright acceptance tests (37 pass)**, and a full render + visual sweep of **19 screen-states at 1280 and 390** that found **zero blank controls and zero visual failures**. The perf gate passed — REQ-NFR-001's `p95 load <= 1500ms` measured **p95 357 ms**. Live runs also proved the recovery and isolation paths: `rebuild --user 2` replayed 14 raw files into 279 rows and left users 3 and 9001 untouched, and header **Sync now** synced the caller's repos with 2 of 7 failing in isolation.

**What is not done is the acceptance gate itself.** BRD §13 makes a passing parity diff against `tf-metrics.sh --rollup --json` the condition for any figure being quotable — **no parity run has ever been recorded, and the oracle `tf-metrics.sh` is not present anywhere in this tree**, so it could not be run here. `/export` honestly reads `NOT QUOTABLE`. Until that gate passes, no number this app renders is quotable, however green the rows above are.

**Two findings the owner should see.** A real recovery hazard: user 2's three repos held a recorded `LastSha` with **zero stream rows**, and because an unchanged SHA skips a repo entirely, the poller could never restore them — only `rebuild` did. And the demo account carries five `tflenstest/Store*` rows that are not real GitHub repos, left behind by a build-phase harness; they surface as failing repos and inflate `Connected repos` to 8.

## Next command to run
```
/TechieFlow:agents:flow-master *build-phase TfLens     (OpenCode: /flow-master *build-phase TfLens)
```
Target the open rows: `REQ-UI-013`, `REQ-UI-034`, `REQ-FN-003`, `REQ-FN-064`, `REQ-FN-067`, `REQ-FN-070` — then `*verify all` again. `REQ-FN-063` needs the owner to supply `tf-metrics.sh` and run the BRD §13 parity procedure.

## Open requirements
- `Needs re-verify` — `REQ-UI-013` Escape does not dismiss the remove-repo `AlertDialog` (Cancel does); acceptance names Escape
- `PARTIAL` — `REQ-UI-034` + `REQ-FN-070` Playbook axis state renders on `/export` only · `REQ-FN-067` no page renders `pb-phases-*` · `REQ-FN-003` forgot/reset not drivable end-to-end · `REQ-FN-064` the no-oracle extras spot-check has never been recorded in `DECISIONS.md`
- `Implemented` — `REQ-FN-063` parity record; not gradeable until a parity run passes (oracle absent from this tree)
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **No real `events.ndjson` exists anywhere reachable**, so the Playbook `PbEvent` columns stay provisional (ADR-010, DECISIONS.md §7 S-001). `REQ-FN-067`/`REQ-FN-070` cannot honestly close until the owner supplies one.
- **No parity run has ever been recorded, and `tf-metrics.sh` is not in this tree**, so the BRD §13 gate could not be run by the verifier. The export correctly reports `NOT QUOTABLE`; no figure is quotable until the owner supplies the oracle and the diff comes back empty. Blocks `REQ-FN-063` and `REQ-FN-064`.
- **The demo account holds five `tflenstest/Store*` rows that are not real GitHub repos** (plus one under user 3 and three `acme/*` under synthetic user 9001), left by build-phase store harnesses. They render as repos whose sync fails and inflate `Connected repos` to 8. Owner cleanup — the app renders them faithfully; it is the data that is wrong.
- **Sync cannot recover a repo whose rows are gone.** Hit for real this run: a recorded `LastSha` with zero stream rows makes every later poll skip the repo (REQ-FN-021, as specified), so only `rebuild` restores it. Nothing reconciles "SHA recorded" against "rows present".
- **AppManager Application 1 has no `Manager` role code** — registration returns `applicationRole: "User"`. Harmless (TfLens treats every account as Manager per BRD-95) but it is an AppManager-side config gap the owner may want to close.
- ~~The clone-and-press-F5 path crashed with an unhelpful message and there was no DevGuide~~ — **fixed 2026-08-27** (default launch profile, actionable startup errors, `docs/TfLens-DevGuide.md`, 6 guardrail tests).
- Optional `TfLensAppManagerApiKey`/`Secret` are unset; AppManager resolves the app from the request body, so this is working-as-designed (DECISIONS.md D-006).

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-26 | split-brd | N/A — no build or verify this phase | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | build-phase | Build PASS · 333/333 tests · 108 Implemented / 5 PARTIAL · not yet verified | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | verify-phase (`*verify all`) | Build PASS · 337/337 .NET · 37/38 Playwright · render+visual gates clean on 19 screen-states · perf p95 357ms vs 1500ms · **106 Verified / 5 PARTIAL / 1 Needs re-verify / 1 Implemented / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- TrBlazeUI: 13 entries — docs/TfLens-TrBlazeUI-Feedback.md. Highest impact: `DataTable` silently truncates to 5 rows even with `ShowPagination="false"`; `LucideIcon` ignores the 212 aliases its own `lucide.json` ships, so `alert-triangle`/`check-circle` render nothing; `--chart-*`/`--alert-*` tokens undefined; no responsive utility variants. **Numbering has duplicates from concurrent clusters — needs one reconciliation pass.**
- TechieRag: 0 — not used by TfLens (ADR-003).

## Standards compliance (last check)
- Underscore fields / test-method underscores / mis-prefixed fields: `TfLens.Guardrails.Tests` 52/52 pass — these are enforced as tests, not greps.

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — Phase 2, waits on an AppManager external-login endpoint
- Private GitHub repos (per-user PAT) — later release
- `BarChart` on `/routing` — the library's chart wrapper needs an `ApexPointSeries` child (solved on `/harness`); `/routing` currently draws a CSS bar row instead
