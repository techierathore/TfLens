---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-28
current_phase: Handoff — READY FOR UAT; 140 of 143 Verified, 2 owner-gated on Playbook telemetry
last_verified_build: PASS
last_verified_date: 2026-08-28
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

**Handoff complete — TfLens is ready for UAT.** Phase 3 shipped and was verified in the same pass: the fifth telemetry stream `misses.jsonl` end to end, telemetry **import** for private and corporate repos, and the AppManager account-restore work. `*verify all` passed — **77/77 Playwright · 630/630 .NET · 450 controls across 22 screen-states with 0 render and 0 visual failures · BRD §13 parity exit 0 · perf p95 ≤ 42 ms against a 1500 ms budget**, the first perf run ever to measure authenticated pages. **140 of 143 REQs are `Verified` and no row is `Blocked`.** The UsageGuide, DevGuide, BRD §4 and all three library-feedback files are finalised; the guide's runbook, test users and smoke checklist were re-derived from the running app rather than carried forward. Two deliberate divergences are on the record: **D-012 / TF-005** (an unrecorded token count stays unmeasured here, where the reference averages it as zero) and the two Playbook rows below. **14 misses found and repaired during the session are now on `docs/metrics/misses.jsonl`** (`MISS-…-03`..`-16`), all closed — so the rework they cost is measurable rather than invisible.

## Next command to run

Manual UAT per the smoke checklist in `docs/TfLens-UsageGuide.md`.

```
# after UAT passes, the OWNER sets this by hand:
#   PROJECT-STATUS.md -> current_phase: Released
```
`REQ-FN-067` / `REQ-FN-070` stay open pending Playbook telemetry — see Known blockers.

## Open requirements
- `Needs re-verify` — `REQ-FN-067` Playbook-native figures: mechanism green (630/630) and the Playbook axis renders its empty state cleanly, but the acceptance clause needs **real** `events.ndjson` data and no connected repository emits any. Not a regression; grading it would require the fabricated fixture removed on 2026-08-27
- `Needs re-verify` — `REQ-FN-070` full Playbook report set: same single external cause. The export half holds (one snapshot per framework, axis separation structural) and all six report pages render a clean Playbook empty state; "a working Playbook state, **not** an empty state" cannot be shown without real telemetry
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **OWNER — no repository emits `events.ndjson`, so the Playbook axis has no data source.** `techierathore/AI-First-Playbook` is a real public repo but publishes neither `verification/telemetry/` nor `docs/metrics/` (both 404), and none of the four connected repos carries Playbook telemetry. Blocks `REQ-FN-067` and `REQ-FN-070` only. **Ask:** point TfLens at a repository that actually emits the Playbook stream — or, now that import has shipped, upload that telemetry through **Repos → Add source → Import metric files**, which needs no credential and no network route to the repo.
- **UPSTREAM — TF-005 open** (`docs/TfLens-TechieFlow-Feedback.md`): `analyse_misses` averages an unrecorded `tokens_out` as zero, understating rework cost. TfLens deliberately diverges (DECISIONS.md **D-012**). Latent only — every current dataset carries the field, so parity passes today. If the gate ever fails on `tokens_per_miss_*`, that is this decision surfacing and the fix is upstream.
- ~~GitHub PAT · AppManager `Manager` role · deleted test accounts.~~ **ALL CLEARED 2026-08-28 by the owner.** PAT live (5000 req/h); registration now returns `applicationRole: "Manager"` (AM-001); `/UserSvc/profile` returns 200 with the key pair instead of `403 NO_APP_ACCESS` (AM-002); both accounts re-created on the same emails as **userId 2 and 3**, so no doc, fixture or stored row changed.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-27 | build-phase (FIX, 6 clusters) + `*verify all` | Build PASS · 425/425 .NET · 55/55 Playwright · 343 controls / 19 screen-states clean · **111 Verified** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-27 | **BRD §13 parity gate PASSED** (after the upstream `tf-metrics.sh` fix) | 433/433 .NET · `parity-compare.py` exit 0 · `/export` **QUOTABLE** · **111 Verified / 2 Needs re-verify / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | build-phase (REQ-NFR-011) — verifier NOT chained (accounts blocked) | Guardrails 55/55 · full suite 429/436, all 7 failures one external cause · **111 Verified / 1 Implemented / 2 Needs re-verify** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | `*log-miss` (records only) | 2 misses opened, 1 closed same day · `REQ-NFR-012` added `Planned` · **110 Verified / 3 Needs re-verify** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | `*amend-docs` ×2 (F-MISS, F-IMPORT — docs only) | BRD +30 · Arch +5 ADRs · Checklist +27 rows, 6 demoted · mockups +2, `repos.html` remocked · **108 Verified / 9 Needs re-verify / 27 Not Started** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | **`*fix-issues` + `*build-phase` (8 clusters) + `*verify all` chained** | Build PASS 0 warnings · **630/630 .NET** (`-m:1`) · **77/77 Playwright** · render+visual **450 controls / 22 screen-states, 0 failures, 0 console errors** · BRD §13 parity **exit 0, 0 findings** at parser 1.2.0 (P-003) · perf **p95 ≤ 42 ms vs 1500 ms budget, authenticated** · **140 Verified / 2 Needs re-verify / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | **`*handoff-phase` — READY FOR UAT** | UsageGuide finalised against the running app (runbook rewritten, test users reconciled live via `provision-test-accounts` → 2 of 2 usable, smoke checklist 13 technical items → 10 user actions) · DevGuide refreshed + 5 screenshots · BRD §4 rolled up (F-AUTH/F-REPOS/F-MISS/F-PARITY → Done) · 3 feedback files consolidated · onboarding defect fixed + guardrailed · **140 Verified / 2 Needs re-verify / 1 N/A, 0 Blocked** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-28 | `*log-miss` (records only — no build, no repro, app never booted) | **14 misses logged and closed** (`MISS-…-03`..`-16`): 6 `wrong-behaviour` src · 3 `partial-implementation` · 1 `unspecified-gap` · 1 `standards-violation` · 2 in `tests` · 1 in `architecture` · 1 in `checklist`. Attribution **10 `linked` / 4 `inferred`** (model nulled where no origin run backed it). `MISS-…-01` also closed with the verifier's actual verdict — its only fix record predated the verify run and read `Needs re-verify` while the REQ was `Verified`. No status changed: every record is `--fixed`. | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- **TrBlazeUI: 19 entries, all open** (5 blockers · 8 majors · 6 minors) — docs/TfLens-TrBlazeUI-Feedback.md. Consolidated 2026-08-28: `TR-022` was a duplicate of `TR-008` and was merged (it survives as a redirect stub), and `TR-006`/`TR-007` were never allocated, so 20 headings hold 19 substantive entries. Highest impact: **TR-021** — the stylesheet's spacing scale has holes, so `<Progress Class="w-20">` renders at **zero width silently** (raised Medium→High on the file's own precedent); **TR-014** `AlertDialog` has no Escape handling; **TR-009** `DataTable` truncates to `InitialPageSize` even with pagination off; **TR-011** `BarChart` renders an empty div.
- **TechieFlow framework: 6 entries, 1 open** — docs/TfLens-TechieFlow-Feedback.md. **TF-005 open** (`analyse_misses` averages an unrecorded `tokens_out` as zero — see D-012). TF-001/002/003 fixed upstream 2026-08-27; **TF-004 closed** (verified in-repo 2026-08-28); TF-006 closed. TF-002's fix was exercised in the field for the first time today — the perf gate measured authenticated pages via `--cookie`.
- **AppManager: 2 entries, BOTH RESOLVED 2026-08-28** — docs/TfLens-AppManager-Feedback.md (AM-001 `Manager` role now honoured · AM-002 profile returns 200 with the key pair).
- TechieRag: 0 — not used by TfLens (ADR-003).

## Standards compliance (last check)
- `TfLens.Guardrails.Tests` **89/89** pass — underscore fields, test-method underscores and mis-prefixed fields enforced as tests, not greps. New pins this pass: the `SourceKind` two-vocabulary rule, the miss invariants (REQ-NFR-013, seven clauses), the import surface bounds (REQ-NFR-014), the UsageGuide↔test-account binding (REQ-NFR-012), and the local-database onboarding pair (`.env.example` must match the code's local fallback — a mismatch found at handoff started a container the app could not authenticate against).

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- Playbook `/harness` columns and `/routing` repricing — not derivable today (`events.ndjson` carries no harness field). Each says so on screen rather than inventing a number.
- `docs/mockups/coverage.html` predates REQ-UI-039 and shows no miss data-quality block — worth a refresh on the next `*mockups --update`
