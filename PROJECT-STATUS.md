---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-26
current_phase: Build — 114 REQs split from the BRD, none built
last_verified_build: not-run
last_verified_date: 2026-08-26
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
Day-1 (greenfield) complete on 2026-08-26, docs amended twice the same day after owner review, and `*split-brd` run the same day. TfLens is a free, multi-user, read-only Blazor Server lens over the telemetry TechieFlow **and** the AI-First-Playbook already emit: users sign in via AppManager (App Id 1, every user `Manager`), connect their own public GitHub repos, and TfLens pulls the streams into PostgreSQL per user and renders the three questions, harness comparison, routing & repricing and a weekly snapshot export per framework — with the provenance rules (live/backfilled · project_type · framework · user) enforced in the result type and a mandatory parity diff against `tf-metrics.sh`. Approved docs: BRD (BRD-1..BRD-111; BRD-3, BRD-7 retired), Architecture (ADR-001..017), UI Design + 12 mockups, Coding Standards, UsageGuide. `docs/TfLens-Checklist.md` now holds **114 REQs** — 34 `REQ-UI-*`, 70 `REQ-FN-*`, 0 `REQ-RAG-*` (ADR-003: no AI features), 10 `REQ-NFR-*` — every active BRD ID mapped, nothing seeded as done (no prior dev plan existed). No code exists yet; nothing is built or verified. Phase order is hard: 1 → 2 → 3.

## Next command to run
```
/TechieFlow:agents:flow-master *build-phase TfLens      (OpenCode: /flow-master *build-phase TfLens)
```
Start with Phase 1: `REQ-UI-001`..`REQ-UI-013`, `REQ-FN-001`..`REQ-FN-045`, `REQ-NFR-002`, `REQ-NFR-003`, `REQ-NFR-005`..`REQ-NFR-008`, `REQ-NFR-010`.

## Open requirements
- Phase 1 — shell, auth, repos, sync, parse, ops: `REQ-UI-001`..`REQ-UI-013`, `REQ-FN-001`..`REQ-FN-011`, `REQ-FN-013`..`REQ-FN-045`, `REQ-NFR-002`, `REQ-NFR-003`, `REQ-NFR-005`, `REQ-NFR-006`, `REQ-NFR-007`, `REQ-NFR-008`, `REQ-NFR-010` — all Not Started
- Phase 2 — engine, five report pages, export, parity: `REQ-UI-014`..`REQ-UI-033`, `REQ-FN-046`..`REQ-FN-064`, `REQ-NFR-001`, `REQ-NFR-004`, `REQ-NFR-009` — all Not Started
- Phase 3 — Playbook as a first-class framework: `REQ-UI-034`, `REQ-FN-065`..`REQ-FN-070` — all Not Started
- Terminal: `REQ-FN-012` (GitHub SSO) — `N/A`, deferred to Phase 2 per BRD-94 / ADR-012

## Known blockers
- No .csproj/.sln yet — create the solution (`src/TfLens`, `src/TfLens.Core`, `tests/TfLens.Core.Tests`) and `docker-compose.yml` (postgres) before the first build phase.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-26 | split-brd | N/A — no build or verify this phase | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- TrBlazeUI: 0 major, 0 minor — docs/TfLens-TrBlazeUI-Feedback.md
- TechieRag: 0 major, 0 minor — docs/TfLens-TechieRag-Feedback.md (not used by TfLens)

## Standards compliance (last verifier check)
- Underscore fields: not yet run
- Test method underscores: not yet run
- Mis-prefixed fields: not yet run

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — Phase 2, waits for an AppManager external-login / token-exchange endpoint
- Private GitHub repos (per-user PAT) — later release
- Playbook report set (Phase 3, F-FRAMEWORK) — after the TechieFlow set passes parity; `events.ndjson` adapter waits for a real sample
