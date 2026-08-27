# Test fixtures — TechieFlow telemetry streams

These files are the shared fixture data for every TfLens test and, later, for the seeded screens the
UsageGuide walks. They are **derived from real telemetry**: the shapes come from this repository's own
`docs/metrics/*.jsonl` (a live TechieFlow stream) and from the worked examples in
`.tfcore/telemetry/SCHEMA.md`. No field appears here that SCHEMA.md does not document, except the
deliberate unknown field described below.

Layout mirrors the raw archive the fetcher writes (`{owner}__{name}/{stream}.jsonl`), so a test can
copy a directory into `data/raw/{userId}/` and rebuild from it.

```
techierathore__TrSetup/     project_type app       — the busy repo, freshly synced
techierathore__TrBlazeUI/   project_type library   — the STALE repo (see below)
```

**Fixture reference date: 2026-08-26.** Every timestamp is fixed, so "days since" figures are relative
to that date, not to the day a test runs.

## What each fixture deliberately contains

| Expectation | Where |
|---|---|
| A malformed line (counted, skipped, never fatal — REQ-FN-032) | `TrSetup/runs.jsonl`, `gates.jsonl`, `sessions.jsonl`, `commits.jsonl` |
| An unknown field — `routed_reason` (UsageGuide §Coverage) | `TrSetup/runs.jsonl` line 4 |
| A `v: 2` record (whole record to `Overflow`, REQ-FN-031) | `TrSetup/runs.jsonl`, `TrSetup/gates.jsonl` |
| Duplicate commits sharing a `sha` (REQ-FN-033) | `TrSetup/commits.jsonl`, `TrBlazeUI/commits.jsonl` |
| The **same short sha in two repos** — both must survive | `a1b2c3d` in both `commits.jsonl` files |
| Duplicate sessions, different `output_tokens` (REQ-FN-034) | `TrSetup/sessions.jsonl` session `7b31…` (three cumulative OpenCode snapshots) |
| Duplicate sessions, equal `output_tokens`, different `ts` (tie-break) | `TrSetup/sessions.jsonl` session `c2d9…` |
| Duplicate run (`ts+app+cmd`) and duplicate gate (`ts+app+req_id+run_id`) (REQ-FN-035) | `TrSetup/runs.jsonl`, `TrSetup/gates.jsonl` |
| Backfilled records with `inferred` | `TrSetup/gates.jsonl` |
| Tainted REQs — exactly `REQ-UI-004`, `REQ-FN-011`, `REQ-UI-009` carry a backfilled record | `TrSetup/gates.jsonl` |
| `project_type_inferred: true` (records segment as *unclassified*) | `TrSetup/runs.jsonl`, `TrSetup/gates.jsonl` |
| Absent optional vs present zero (REQ-FN-036) | `TrSetup/runs.jsonl`: the `triage-issues` run has `files_written: 0` and **no** token fields at all |
| `gate: "escaped"` — no gate caught it | `TrSetup/gates.jsonl` (`REQ-UI-021`) |
| `perf` in `gates_run` so its coverage denominator is non-zero (SCHEMA.md §3.5) | `TrSetup/gates.jsonl` |
| A harness of each kind, plus `harness: null` (the footnote, never a column) | `TrSetup/runs.jsonl`, `sessions.jsonl` |
| Real `cost_usd` on OpenCode only; `null` on Claude Code | `TrSetup/runs.jsonl`, `sessions.jsonl` |
| `tokens_scope: "none"` — excluded from repricing | `TrSetup/runs.jsonl` |
| **A repo whose sessions/commits are 11 days stale** (UsageGuide §Coverage) | `TrBlazeUI/sessions.jsonl` and `commits.jsonl` are dated 2026-08-15; its runs/gates are dated 2026-08-24 |
