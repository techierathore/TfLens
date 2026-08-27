---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-27
current_phase: Build — 111 of 114 Verified; the two Playbook rows await real telemetry
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
`*build-phase` ran in FIX mode and chained `*verify all`; the owner then supplied the AppManager credentials and the four real repositories, and the TechieFlow team shipped fixes for all three framework defects this project raised. **111 of 114 REQs are `Verified`, 2 `Needs re-verify`, 1 `N/A`.** Evidence: **433/433 .NET tests** (`-m:1`), **54/54 Playwright acceptance tests**, and a render + visual sweep of **325 controls across 19 screen-states** with zero blank controls and zero visual failures.

**The BRD §13 parity gate PASSES — the app's figures are quotable.** Re-run end to end at **parser 1.1.0**: `parity-compare.py` exits 0 — *"0 finding(s), 19 allowed difference(s). PASS — the two implementations agree key for key."* Every headline figure agrees exactly: sessions 56, tokens_total 14,846,715, tokens_per_verified_req 65,985.4, commits 181, session_duplicates_collapsed 2. `data/parity-last.json` is recorded against oracle `sha256:960d12b4…` (DECISIONS.md P-002) and `/export` reads **QUOTABLE** on a bare boot. Nothing was added to the compare script's allow-lists and `.tfcore/` was never edited.

**The app runs on real data.** All four owner repositories are connected through the Repos UI and validated live: TechieBlog **377 records**, TrBlazeUI 148, TechieFlow 86, TechieRag 0 (publishes `docs/metrics`, no stream files yet) — **611 real records**, with 2 duplicate sessions collapsed.

**Two fabrications were removed.** REQ-UI-025's `$0.84` came from test rows left in the shared database (real figure: **$0.04**), and the 45 Playbook `PbEvent` rows behind `$12.69` were written by a build harness — `techierathore/AI-First-Playbook` publishes neither telemetry path. Both are gone.

## Next command to run
```
/TechieFlow:agents:flow-master *build-phase TfLens     (OpenCode: /flow-master *build-phase TfLens)
```
Target `REQ-FN-067` and `REQ-FN-070` once a repository emits `events.ndjson`. Handoff waits on the three owner items below.

## Open requirements
- `Needs re-verify` — `REQ-FN-067` Playbook-native figures: mechanism covered by unit tests and a seeded dataset, ungradeable against real data — no repository emits `events.ndjson`
- `Needs re-verify` — `REQ-FN-070` full Playbook report set: the export half holds (one snapshot per framework, axis separation structural), but "a working Playbook state, not an empty state" cannot be shown without real telemetry
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- ~~FRAMEWORK BUG — `tf-metrics.sh` never dedupes sessions.~~ **FIXED UPSTREAM 2026-08-27.** All three framework defects TfLens raised were fixed the same day and delivered by `update-framework.sh`: TF-001 (`dedupe_sessions` now ships, with a `session_duplicates_collapsed` count), TF-002 (`tf-perf.sh` gained `--header`/`--cookie` and an auth-wall exit code) and TF-003 (`tf-render-html.sh` ships; `*generate-html` calls it instead of hand-authoring). TF-001's fix is what let the parity gate pass. See `docs/TfLens-TechieFlow-Feedback.md`.
- **OWNER — create a `Manager` role for Application 1 in AppManager.** Registering with `applicationRoleCode: "Manager"` (plus the API-key pair and `applicationId: 1`, exactly as the guide specifies) returns **200 with `applicationRole: 'User'`** — silently substituting the app default, which per the guide means Application 1 defines no `Manager` role. Reproduced on a fresh account this pass (userId 4). `GET /UserSvc/profile` also answers `403 NO_APP_ACCESS` for every account whenever an app context is resolved. TfLens behaves correctly either way (BRD-95 issues its own `Manager` claim; the key pair is scoped to `/AuthSvc/*`), but the server does not honour the documented contract. Logged as AM-001 / AM-002 in `docs/TfLens-AppManager-Feedback.md`.
- **OWNER — supply `TfLensGitHubToken`.** Unauthenticated GitHub allows **60 requests/hour**; connecting and syncing four repos exhausted it (three finished `GitHub rate limit reached — try again in 15 minutes`, and the quota hit 0/60). A contents-read-only PAT raises it to 5,000/h. The BRD calls this token *optional*, which is misleading — a single sync pass over four repos cannot complete without it.
- **OWNER — no repository emits `events.ndjson`, so the Playbook axis has no data source.** `techierathore/AI-First-Playbook` is a real public repo but publishes neither `verification/telemetry/` nor `docs/metrics/` (both 404), and none of the four named repos carries Playbook telemetry either. The 45 `PbEvent` rows that were present came from a build harness and have been removed (fixture kept at `tests/.artifacts/removed-fixtures/`). The Playbook pages now honestly render their empty state. Blocks `REQ-FN-067` and `REQ-FN-070`.
- ~~The shared database is polluted by the test suites.~~ **FIXED 2026-08-27.** `PostgresStoreTests` ran destructively against the owner's real account: it called `RebuildAsync(2)`, which drops every row for that user and replays from the test's temp archive, so **every `dotnet test` silently wiped the live telemetry**, and it left `tflenstest/*` repo rows behind (8 connected repos instead of 3) plus `cost_usd` rows that inflated `/harness` to ~$1.02. Now the class uses reserved ids `90002`/`90003` — above anything AppManager issues, so a real account can never be touched — and its teardown purges every `(user, repo)` pair it wrote, tracked automatically rather than hard-coded. Proven: 425/425 green, and user 2's rows are 122 before and 122 after a full run, with 0 phantom repos and 0 leftovers.
- **`tf-perf.sh` cannot grade an authenticated app** (TF-002). It sends no cookie and has no auth option, so every report route answered 302 and the §4c gate is `PERF-UNMEASURED`. The budget is met on the project's own authenticated spec (p95 439 ms vs 1500 ms, n=60, Release) — framework gap, not an app defect.
- **Sync cannot recover a repo whose rows are gone** — a recorded `LastSha` with zero stream rows makes every later poll skip the repo (REQ-FN-021, as specified); only `rebuild` restores it. Nothing reconciles "SHA recorded" against "rows present".

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-26 | split-brd | N/A — no build or verify this phase | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | build-phase | Build PASS · 333/333 tests · 108 Implemented / 5 PARTIAL · not yet verified | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | verify-phase (`*verify all`) | Build PASS · 337/337 .NET · 37/38 Playwright · **106 Verified / 5 PARTIAL / 1 Needs re-verify / 1 Implemented / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | build-phase (FIX, 6 clusters) + `*verify all` chained | Build PASS 0 warnings · 425/425 .NET (`-m:1`) · 55/55 Playwright · render+visual 343 controls / 19 screen-states clean · BRD §13 parity executed (4 findings) · **111 Verified / 1 PARTIAL / 1 Implemented / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | follow-up: AppManager credentials + test-isolation fix | 427/427 .NET · 21/21 auth+reset Playwright · `/AuthSvc/forgot-password` **200 live** (was 400) · API-key pair scoped to `/AuthSvc/*` after it broke `/UserSvc/profile` · store tests moved to reserved ids and now self-clean · **112 Verified / 1 Implemented / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | follow-up: four real repos connected + fabricated data removed | 54/54 Playwright · render+visual 325 controls / 19 screen-states clean · 611 real records from the owner's repos · synthetic `PbEvent` fixture and the `AI-First-Playbook` connection removed · AM-001/AM-002 raised · **110 Verified / 2 Needs re-verify / 1 Implemented / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | **BRD §13 parity gate PASSED** (after the upstream `tf-metrics.sh` fix) | 433/433 .NET · `parity-compare.py` exit 0 — 0 findings, 19 allowed · `data/parity-last.json` written vs oracle `960d12b4…` · `/export` reads **QUOTABLE** on a bare boot · **111 Verified / 2 Needs re-verify / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | parity re-run at parser **1.1.0** + deployment checklist | 433/433 .NET · parser bumped for the added `session_duplicates_collapsed` metric (D-005), which un-quoted the export until parity was re-recorded — the invalidation clause demonstrated end to end · compare exit 0, **QUOTABLE** restored (DECISIONS.md P-002) · `docs/TfLens-Deployment-Checklist.md` added · **111 Verified / 2 Needs re-verify / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- **TechieFlow framework: 4 entries, TF-001/002/003 ✅ fixed upstream 2026-08-27; TF-004 open** — docs/TfLens-TechieFlow-Feedback.md (TF-001 sessions never de-duplicated · TF-002 `tf-perf.sh` cannot measure an authenticated app · TF-003 `*generate-html` shipped no renderer · **TF-004** `tf-render-html` refuses any `*-Checklist.md`, including a human deployment runbook). The first three were raised and fixed the same day.
- **AppManager: 2 entries** — docs/TfLens-AppManager-Feedback.md (AM-001 `Manager` role code silently ignored · AM-002 `403 NO_APP_ACCESS` on profile).
- TrBlazeUI: 16 entries — docs/TfLens-TrBlazeUI-Feedback.md. **Numbering reconciled 2026-08-27** (four numbers were doubly allocated by concurrent clusters; the duplicates became TR-015…TR-018 and every citation now resolves). Highest impact: `AlertDialog` has no Escape handling at all (TR-014); `DataTable` truncates to `InitialPageSize` even with `ShowPagination="false"` (TR-009); `BarChart` renders an empty div (TR-011); a closed `CollapsibleContent` still occupies and overlaps its space (TR-018).
- TechieRag: 0 — not used by TfLens (ADR-003).

## Standards compliance (last check)
- `TfLens.Guardrails.Tests` 52/52 pass — underscore fields, test-method underscores and mis-prefixed fields are enforced as tests, not greps.

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — Phase 2, waits on an AppManager external-login endpoint
- Private GitHub repos (per-user PAT) — later release
- Playbook `/harness` columns and `/routing` repricing — not derivable today (`events.ndjson` carries no harness field; no repriced figure on `IPlaybookReportBuilder`). Each says so on screen rather than inventing a number.
