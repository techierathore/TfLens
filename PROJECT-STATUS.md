---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-29
current_phase: Build — 145 Verified; build output untracked, awaiting the owner's commit
last_verified_build: PASS
last_verified_date: 2026-08-29
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

**BRD §13 parity passes again — `/export` reads QUOTABLE — and the browser suite is fully green for the first time (79/82, 0 failures, 3 skipped).** The re-run turned up four distinct causes, three of them real defects, and the most serious was not the parity mismatch itself: **155 rows in the store carried a `source_sha` that is not a commit in its repository** (`a91f3c2e…`, `e3b9d40a…` — both HTTP 422 from GitHub, both hand-typed sequential hex), inflating TechieFlow to 34 gate records against 0 upstream. `source_sha` is exactly what a quotable figure is pinned to, so those rows made published numbers unreproducible by anyone checking them. Purged, with a backup, and logged as `REQ-NFR-019` because nothing yet stops it recurring. The two code defects were both figures that described **TfLens's history rather than the user's data**: cost attribution was read from storage instead of recomputed per fix run, and `session_duplicates_collapsed` accumulated on every sync so re-reading an unchanged file grew it by a whole file (TechieFlow reached 25 against the 3 duplicates its file actually contains). A figure that changes with how many times you read the data cannot be quoted — two instances on the same repos would disagree.

## Next command to run

The untracking is **done** — `git ls-files` returns 0 paths under `bin/`/`obj/` (was 1,962). One step
left, and it is the owner's because agents cannot write to version control:

```bash
git commit -m "Untrack build output (REQ-NFR-016)"
```
Your editor is showing 1,962 entries under *Changes to be committed*: those are the **deletions**, not
the folders still being staged. `bash scripts/untrack-build-output.sh` will say so if in doubt.

Then `*handoff-phase TfLens`.

## Open requirements
- `Planned` — `REQ-NFR-019` stored provenance is real: no row may claim a `source_sha` no sync or import obtained. The 155 offending rows are purged; the hole that let them in is open, and it is the most consequential item here because `source_sha` is what a quotable figure is pinned to
- `Needs re-verify` — `REQ-FN-067` / `REQ-FN-070` Playbook-native figures — unchanged, owner-gated on a repository that emits `events.ndjson`
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **OWNER — no repository emits `events.ndjson`**, so the Playbook axis has no data source. Blocks `REQ-FN-067` / `REQ-FN-070` only. Import via **Repos → Add source → Import metric files** needs no credential.
- **UPSTREAM — TF-005 open**: `analyse_misses` averages an unrecorded `tokens_out` as zero. TfLens diverges deliberately (D-012). Latent only.
- **UPSTREAM — TF-007 raised today (High):** the framework's gate set has **no asset-integrity gate**, so a page can lose its entire stylesheet and every gate still passes — which is precisely how `/login` reached the owner. Includes the `bin/`-`obj/` scaffold check and a "no unreproducible construct" rule for `_smoke-test-policy.md`.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-28 | **`*triage-issues`** (analyse-only) | 4 owner issues → 3 reproduced, 1 could-not-reproduce; 3 REQs demoted, 3 new `Planned` rows; 6 `escaped` gate records + 6 misses | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | **`*fix-issues` r1 + verifier** | `/login` fix (+ regression test), config surface, DevGuide restructure, asset-integrity gate · 637/637 .NET · 75/80 Playwright · render+visual 465/22, 0 failures | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | **`*fix-issues` r2 + verifier — dialogs → routes** | `REQ-UI-044` built and verified; `/repos` mockup drift closed; clipped Remove button fixed; Coverage assertion fixed · Build PASS 0 warnings · **638/638 .NET** (500 Core + **95** Guardrails + 43 Integration) · **78/82 Playwright** (1 fail = the parity stamp, 3 skipped) · render+visual **465 controls / 22 screen-states, 0 render failures, 0 visual problems, 0 console errors** · **142 Verified / 3 Needs re-verify / 2 Implemented / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`REQ-NFR-016` closed + handover defect** | Owner ran `untrack-build-output.sh --run`: `git ls-files` under `bin`/`obj` **1,962 → 0**, guardrail passes, `REQ-NFR-016` **Verified** (the commit makes it permanent). Root cause located and **attributed to the day-1 agent, not to the scaffold template** — an agent that generates a file is responsible for reading it. A second miss logged (`MISS-…0829-04`): the script worked but reported success as 1,962 staged deletions, which reads as failure; it now names the repository's state and warns before you open your editor. **145 Verified / 2 Needs re-verify / 1 Planned / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **BRD §13 parity re-run + verifier** | Parity **PASS, 0 findings** (stamp recorded, `/export` QUOTABLE) after purging 155 fabricated-provenance rows and fixing two history-dependent figures · **638/638 .NET** · **79/82 Playwright, 0 failures** — first fully green suite · render+visual **464 controls / 22 screen-states, 0 failures, 0 console errors** · **144 Verified / 2 Needs re-verify / 1 Implemented / 1 Planned / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- **TrBlazeUI: 20 entries, all open** — **TR-023 added today**: `BreadcrumbLink` *throws* on an unrecognised attribute rather than ignoring it, the third component in the same family as `TabsTrigger` (TR-010) and `Typography*` (TR-013). Both new pages built cleanly and returned a 500 on first render, twice, for two different components. **TR-016** (no info/success/warning Badge variant) is why the grid's status colours had to be hand-built as `tflens-badge-*` tones. **TR-014** (AlertDialog has no Escape) is now moot here — the routes retired the workaround.
- **TechieFlow framework: 7 entries, 2 open** — **TF-007 added today** (see Known blockers) · TF-005.
- **AppManager: 2 entries, both resolved.** TechieRag: 0 — not used (ADR-003).

## Standards compliance (last check)
- `TfLens.Guardrails.Tests` **95/95**. New: build output gitignored, secrets template complete, Compose password matches the code fallback, DevGuide leads with screens, auth layout out of the scoped bundle, and **source flows are routes not dialogs**.

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- Playbook `/harness` columns and `/routing` repricing — not derivable today
- `Last successful sync` KPI has **no** sparkline by design: only the latest sync per source is stored, so there is no history to plot and inventing one is barred (BRD §1). A stored sync-history table would make it real
- **Data note:** the repro deleted and re-added `techierathore/TechieBlog` twice through the real UI. All 4 sources are connected; counts re-converge on the next poll.
