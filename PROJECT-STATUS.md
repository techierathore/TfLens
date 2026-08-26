---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-26
current_phase: Discovery
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
Day-1 (greenfield) complete on 2026-08-26; docs amended twice the same day after owner review (round 1: AppManager identity, per-user public-repo management, shell rework, GitHub SSO deferred to Phase 2; round 2: both frameworks get the full report set via a Framework switch, harness columns claude-code/opencode/codex, PostgreSQL replaces SQLite, F-CFG retired into F-OPS, mockup links in the BRD). TfLens is a free, multi-user, read-only Blazor Server lens over the telemetry TechieFlow **and** the AI-First-Playbook already emit: users sign in via AppManager (App Id 1, every user `Manager`), connect their own public GitHub repos, and TfLens pulls the streams, stores them in PostgreSQL (Dapper + Npgsql) per user, and renders the three questions, harness comparison, routing & repricing and a weekly snapshot export per framework — with the provenance rules (live/backfilled · project_type · framework · user) enforced in the result type and a mandatory parity diff against `tf-metrics.sh`. Stack: .NET 10 LTS, Blazor Server + TrBlazeUI (dark-first), PostgreSQL 16, Serilog, docker compose. Drafted: Architecture (target, ADR-001..017), BRD (BRD-1..BRD-111, 16 active features across Phases 1–3; BRD-3, BRD-7 retired), Coding Standards (`obj` prefix, quoted identifiers), UsageGuide, UI Design spec + 12 mockups. Mockups regenerated 2026-08-26 (round 2): docs/TfLens-UIDesign.md (+ .html) + docs/mockups/ (12 screens). No code exists yet; nothing is built or verified.

## Next command to run
```
/TechieFlow:agents:analyst *split-brd TfLens      (OpenCode: /flow-analyst *split-brd TfLens)
```
After you approve docs/TfLens-BRD.md, docs/TfLens-Architecture.md and docs/mockups/*.html.

## Open requirements
- (populated by `*split-brd TfLens` — none yet)

## Known blockers
- No .csproj/.sln yet — create the solution (`src/TfLens`, `src/TfLens.Core`, `tests/TfLens.Core.Tests`) and `docker-compose.yml` (postgres) before the first build phase.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|

## Library feedback summary
- TrBlazeUI: 0 major, 0 minor — docs/TfLens-TrBlazeUI-Feedback.md
- TechieRag: 0 major, 0 minor — docs/TfLens-TechieRag-Feedback.md (not used by TfLens)

## Standards compliance (last verifier check)
- Underscore fields: not yet run
- Test method underscores: not yet run
- Mis-prefixed fields: not yet run

## Deferred / future
- GitHub SSO (BRD-94) — Phase 2, waits for an AppManager external-login / token-exchange endpoint
- Private GitHub repos (per-user PAT) — later release
- Playbook report set (Phase 3, F-FRAMEWORK) — after the TechieFlow set passes parity; `events.ndjson` adapter waits for a real sample
