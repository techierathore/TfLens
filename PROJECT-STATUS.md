---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI / SQLite (Dapper) / Serilog
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
Day-1 (greenfield) complete on 2026-08-26. TfLens is a read-only Blazor Server lens over the telemetry TechieFlow and the AI-First-Playbook already emit: it pulls `docs/metrics/*.jsonl` from GitHub, stores it in SQLite (Dapper), and renders the three questions, harness comparison, routing & repricing and a weekly snapshot export — with SCHEMA.md §6 provenance rules enforced in the result type and a mandatory parity diff against `tf-metrics.sh`. Stack fixed: .NET 10 LTS, Blazor Server + TrBlazeUI, Dapper + Microsoft.Data.Sqlite, Serilog, single-user cookie auth, one Docker image. Drafted: Architecture (target), BRD (BRD-1..BRD-89, 14 features across Phases 1–3), Coding Standards (`obj` prefix), UsageGuide, UI Design spec + 7 mockups. Mockups generated 2026-08-26: docs/TfLens-UIDesign.md (+ .html) + docs/mockups/ (7 screens). No code exists yet; nothing is built or verified.

## Next command to run
```
/TechieFlow:agents:analyst *split-brd TfLens      (OpenCode: /flow-analyst *split-brd TfLens)
```
After you approve docs/TfLens-BRD.md, docs/TfLens-Architecture.md and docs/mockups/*.html.

## Open requirements
- (populated by `*split-brd TfLens` — none yet)

## Known blockers
- No .csproj/.sln yet — create the solution (`src/TfLens`, `src/TfLens.Core`, `tests/TfLens.Core.Tests`) before the first build phase.

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
- Codex-CLI harness detection (a `tf-emit.sh` change, out of scope — TfLens reports `harness: null` honestly)
- Playbook adapter (Phase 3) waits for a real `events.ndjson` sample
