---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-27
current_phase: Verify — all 113 in-scope REQs built; none verified yet
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
`*build-phase` ran 2026-08-26→27 and built the whole app from an empty repo: solution, PostgreSQL 16 store, AppManager identity, GitHub sync, the metrics engine, the exporter, and all twelve screens. **108 REQs are `Implemented`, 5 are `PARTIAL`, 1 is `N/A`; nothing is `Not Started`.** The build is green and **333/333 tests pass** (263 Core · 52 Guardrails · 18 Integration). The app boots, signs in against the live AppManager, and every report page renders real data with no blank cells; the mobile visual gate passes on all seven authenticated pages.

**Correction, 2026-08-27:** this was first reported complete while the clone-and-press-F5 path was still broken. Every smoke had been run with `TfLensDbConnection` exported in a shell, so nobody exercised the entry point a developer actually uses — opening the solution and pressing F5 crashed on a bare "supply it as a PascalCase environment variable", and no DevGuide existed to look it up in. Fixed: the default launch profile supplies the connection string (F5 now works with no setup), both startup failure messages are actionable and platform-aware, `docs/TfLens-DevGuide.md` exists, and six guardrail tests pin that first-run path so it cannot regress silently. Running the Docker path and the DevGuide trace for the first time then turned up three more never-exercised defects: **sign-out was completely broken** (the menu pointed at an unmapped route, the cookie survived, the user stayed signed in), the Profile table was one field from silently dropping a row, and `src/TfLens/data/` — holding the **Data Protection key ring** — was not covered by the root-anchored `/data/` ignore rule. All three fixed and pinned by tests.

**Nothing is `Verified` yet — that is correct, not an omission.** A self-smoke's ceiling is `Implemented`; `Verified` exists only downstream of an executed `*verify` run, which has not happened (`docs/.last-verify.json` does not exist). Three clusters wrote `Verified` from their own smokes and those rows were demoted.

## Next command to run
```
/TechieFlow:agents:verifier *verify all TfLens      (OpenCode: /flow-verifier *verify all TfLens)
```

## Open requirements
- `PARTIAL` — `REQ-UI-034` Playbook state wired on `/export` only, not on the other four report pages · `REQ-FN-003` forgot/reset password not driven end-to-end (needs a real reset email) · `REQ-FN-018` poller/Sync-now halves not jointly exercised · `REQ-FN-067` + `REQ-FN-070` Playbook report set, blocked on a real `events.ndjson`
- `Implemented` (108) — awaiting the verifier
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **No real `events.ndjson` exists anywhere reachable**, so the Playbook `PbEvent` columns stay provisional (ADR-010, DECISIONS.md §7 S-001). `REQ-FN-067`/`REQ-FN-070` cannot honestly close until the owner supplies one.
- **No parity run has ever been recorded**, so the export correctly reports `NOT QUOTABLE`. Running the BRD §13 procedure is the gate before any figure is quotable.
- **AppManager Application 1 has no `Manager` role code** — registration returns `applicationRole: "User"`. Harmless (TfLens treats every account as Manager per BRD-95) but it is an AppManager-side config gap the owner may want to close.
- ~~The clone-and-press-F5 path crashed with an unhelpful message and there was no DevGuide~~ — **fixed 2026-08-27** (default launch profile, actionable startup errors, `docs/TfLens-DevGuide.md`, 6 guardrail tests).
- Optional `TfLensAppManagerApiKey`/`Secret` are unset; AppManager resolves the app from the request body, so this is working-as-designed (DECISIONS.md D-006).

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-26 | split-brd | N/A — no build or verify this phase | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | build-phase | Build PASS · 333/333 tests · 108 Implemented / 5 PARTIAL · not yet verified | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- TrBlazeUI: 13 entries — docs/TfLens-TrBlazeUI-Feedback.md. Highest impact: `DataTable` silently truncates to 5 rows even with `ShowPagination="false"`; `LucideIcon` ignores the 212 aliases its own `lucide.json` ships, so `alert-triangle`/`check-circle` render nothing; `--chart-*`/`--alert-*` tokens undefined; no responsive utility variants. **Numbering has duplicates from concurrent clusters — needs one reconciliation pass.**
- TechieRag: 0 — not used by TfLens (ADR-003).

## Standards compliance (last check)
- Underscore fields / test-method underscores / mis-prefixed fields: `TfLens.Guardrails.Tests` 52/52 pass — these are enforced as tests, not greps.

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — Phase 2, waits on an AppManager external-login endpoint
- Private GitHub repos (per-user PAT) — later release
- `BarChart` on `/routing` — the library's chart wrapper needs an `ApexPointSeries` child (solved on `/harness`); `/routing` currently draws a CSS bar row instead
