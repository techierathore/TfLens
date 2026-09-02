# Phase efficiency telemetry - TfLens contract from AIFP

**Status:** PLAYBOOK PRODUCER IMPLEMENTED; TFLENS CONSUMER TO IMPLEMENT.
**Audience:** TfLens ingestion, API, analytics and UI teams.
**Producer:** AI-First Playbook schema-2 phase metrics.
**Related:** [`Telemetry-Guide.md`](Telemetry-Guide.md) · [`Telemetry-Hooks.md`](Telemetry-Hooks.md) · [`Miss-Telemetry-TfLens-From-AIFP.md`](Miss-Telemetry-TfLens-From-AIFP.md).

## 1. Answer to the product question

The current Playbook can now answer, for every instrumented OpenCode phase execution:

| Question | Field | Reporting rule |
|---|---|---|
| How long did the phase take? | `elapsed_ms` | Show only when `complete:true`; this is wall-clock time |
| How much measured agent activity occurred? | `observed_active_effort.observed_active_ms` | Show as observed active time only; require `complete:true` and `coverage:"complete"` for comparisons |
| How many tokens were spent? | `tokens` plus `tokens_in` / `tokens_out` | Use the five-part breakdown for analysis and compatibility totals for headlines |
| Which models actually ran? | `models[]` | Use the full array; `model` is only the dominant-model compatibility label |
| How many subagents were launched? | `subagents.spawned` | Includes zero-token and early-failed child sessions |
| How many subagents contributed tokens? | `subagents.contributors` | Compatibility `subagents.count` has this same meaning |
| What did each subagent do? | `subagents.sessions[]` | Lifecycle time, tokens, cost, models, turns, and optional harness-provided agent type |
| What did the phase cost? | `cost_usd` | Provider-reported measured cost only; never blend with estimates |

The answer was previously partial: tokens, one dominant model, cost, and token-contributing child sessions existed, but elapsed time, active effort, mixed-model detail, stable execution identity, and zero-token spawned children did not. Schema 2 closes those OpenCode gaps. Claude Code parity remains unavailable until a Claude adapter emits the normalized schema; TfLens must show that as unsupported, not as zero.

### 1.1 Plain-language scope

These terms answer different questions and must not be substituted for one another:

| Term | What it means | Example |
|---|---|---|
| Phase execution | One instrumented slash-command invocation | One `/implement` or one `/verify` run |
| Wall-clock time | How long that command window remained open | 12 minutes from command start to observed idle |
| Observed active time | Time covered by observed assistant/tool activity, with overlaps counted once | Two parallel subagents active during the same minute contribute one busy minute, not two |
| Human effort | Time a person spent reading, deciding, waiting, reviewing or editing | Not captured by OpenCode and therefore always unavailable |
| Conceptual phase | One of the ten Playbook lifecycle stages | Build is Phase 3; self-review is Phase 4 |
| Command phase | The measured slash command, which can contain multiple conceptual stages | `/implement` contains build and self-review, so their tokens cannot be split truthfully |
| Task | The end-to-end feature/checklist outcome across multiple commands | Plan, implement, verify, fix and re-verify for one checklist |

The producer emits a stable `phase_execution_id` for each command, but not a trustworthy
cross-command `task_execution_id`. A TfLens task page therefore needs an explicit cohort supplied
by the ingestion workflow: repository, checklist identity, and the exact phase execution IDs or
time boundary belonging to that task. A reused OpenCode `session_id` is not sufficient because one
session may execute multiple tasks. Without that cohort, TfLens can show phase rows but must label
the whole-task total unavailable rather than silently grouping unrelated work.

## 2. Inputs and invocation

### 2.1 Phase metrics

Run from the target repository root:

```bash
node scripts/playbook-telemetry.mjs \
  --checklist=verification/<Feature>-Implementation-Checklist.md
```

The command reads the transient `verification/telemetry/events.ndjson` file and emits one `phase-metric` NDJSON row per phase execution on stdout. TfLens must ingest stdout, not parse plugin internals. It may pass `--events=<path>` and `--tiers=<path>` when repository discovery supplies explicit paths.

### 2.2 Checkpointing and retention

- Upsert phase rows by `(repository_id, phase_execution_id)`.
- Re-imports are expected because the phase CLI emits every currently readable window.
- Checkpoint completed phase rows before `events.ndjson` is rotated. The Playbook event file is intentionally transient.
- Never treat file absence, EOF, malformed input, or an unsupported harness as a zero-valued run.
- Keep `source_schema`, `source_harness`, importer version, repository identity, and import timestamp on every normalized row.

## 3. Schema-2 phase metric

Canonical example:

```json
{
  "schema": 2,
  "kind": "phase-metric",
  "phase_execution_id": "25ed3b54-5f8a-4b11-a3a8-8d2812102254",
  "phase": "verify",
  "started_at": "2026-08-31T09:10:00.000Z",
  "ended_at": "2026-08-31T09:12:00.000Z",
  "elapsed_ms": 120000,
  "complete": true,
  "end_reason": "idle",
  "model": "anthropic/claude-sonnet-5",
  "models": [
    {
      "model": "anthropic/claude-sonnet-5",
      "turns": 12,
      "tokens": {"input": 31203, "output": 7900, "reasoning": 1220, "cache_read": 16000, "cache_write": 1010},
      "tokens_in": 48213,
      "tokens_out": 9120,
      "cost_usd": 0.41,
      "cost_status": "complete",
      "active_ms": 78000
    }
  ],
  "tokens": {"input": 31203, "output": 7900, "reasoning": 1220, "cache_read": 16000, "cache_write": 1010},
  "tokens_in": 48213,
  "tokens_out": 9120,
  "cost_usd": 0.41,
  "attempt": 2,
  "gate_verdict": "FAIL",
  "project_type": "dotnet-react",
  "timestamp": "2026-08-31T09:12:00.000Z",
  "session_id": "ses_123",
  "harness": "opencode",
  "granularity": "message",
  "turns": 14,
  "observed_active_effort": {
    "assistant_elapsed_ms": 78000,
    "tool_elapsed_ms": 31000,
    "observed_active_ms": 84000,
    "coverage": "complete"
  },
  "data_quality": {"valid": true, "issues": [], "token_status": "complete", "cost_status": "complete"},
  "tokens_scope": "tree",
  "subagents": {
    "count": 2,
    "spawned": 3,
    "contributors": 2,
    "tokens": {"input": 6100, "output": 1510, "reasoning": 330, "cache_read": 2100, "cache_write": 900},
    "tokens_in": 9100,
    "tokens_out": 1840,
    "cost_usd": 0.06,
    "cost_status": "complete",
    "sessions": []
  }
}
```

### 3.1 Required invariants

- `tokens_in = tokens.input + tokens.cache_read + tokens.cache_write`.
- `tokens_out = tokens.output + tokens.reasoning`.
- Phase totals include main and recursively linked child sessions.
- `subagents.count = subagents.contributors` for compatibility.
- `subagents.spawned >= subagents.contributors`.
- `observed_active_ms` is the union of valid assistant and tool intervals; overlapping and nested work is counted once.
- `assistant_elapsed_ms` and `tool_elapsed_ms` are diagnostic sums and may overlap; never add them.
- For a complete phase with valid boundaries, `0 <= observed_active_ms <= elapsed_ms`.
- `complete:false` requires `end_reason:"eof"`, `ended_at:null`, and `elapsed_ms:null`.
- `end_reason:"superseded"` is complete because the next phase start supplies an observed boundary.
- `models[]` is authoritative for mixed-model phases. `model` is selected by finalized turn count with lexical tie-break.
- A schema-2 phase with no finalized assistant turn is invalid/incomplete, not a valid zero-usage run.
- `data_quality.valid:false` quarantines the complete phase row from numeric aggregates. `cost_status` can be `unavailable`, `partial`, or `zero-unverified` without invalidating otherwise valid token/time data.
- Missing optional agent type is an absent field, not `"unknown"` inferred by TfLens.

### 3.2 Timing semantics

TfLens must keep three concepts separate:

| Concept | Definition | Can be summed? |
|---|---|---|
| Wall-clock elapsed | End boundary minus start boundary | Yes across non-overlapping executions; otherwise label as summed phase duration |
| Observed active time | Union of assistant-message and tool intervals across main and child sessions | Yes across non-overlapping executions; overlap inside a phase is removed |
| Human effort | Time spent by a person | Not captured; never infer from wall or agent time |

`coverage:"complete"` means every retained assistant turn had a valid interval and every observed tool start had a valid matching end. `partial` is a lower bound. `unavailable` means no active interval was observable. Coverage does not describe token completeness or event-delivery reliability. Assistant envelopes can contain tool execution, which is why the producer unions intervals rather than adding their component sums.

### 3.3 Phase namespace

The `phase` field is currently the slash command, not always one of the ten conceptual framework phases. Important combinations:

| Command | Conceptual work included |
|---|---|
| `feature-plan` | plan |
| `implement` | build and self-review |
| `verify` | verify and verification-results gate |
| `fix` | fix |
| `analyze-fix` | post-verification or production bug analysis |

TfLens should label the dimension **Command phase** until a future producer emits an authoritative conceptual-phase field. Do not split one command window between conceptual phases by token proportion. The producer also has no canonical cross-phase task/checklist execution ID; TfLens may show a task cohort only when its ingestion job supplies an explicit repository, checklist and time/execution-ID boundary. Never infer that boundary from a reused session ID.

## 4. Normalized TfLens storage

Recommended logical tables or equivalent documents:

### `phase_execution`

`repository_id`, `phase_execution_id`, `source_schema`, `phase`, `session_id`, `harness`, `granularity`, `started_at`, `ended_at`, `elapsed_ms`, `complete`, `end_reason`, `dominant_model`, `tier`, five token columns, compatibility token totals, `cost_usd`, `turns`, `assistant_elapsed_ms`, `tool_elapsed_ms`, `observed_active_ms`, `active_coverage`, `data_quality_valid`, `data_quality_issues`, `token_status`, `cost_status`, `tokens_scope`, `subagents_spawned`, `subagents_contributors`, `attempt_snapshot`, `gate_verdict_snapshot`, `project_type`, `imported_at`.

### `phase_model_usage`

`repository_id`, `phase_execution_id`, `model`, `turns`, five token columns, `tokens_in`, `tokens_out`, `cost_usd`, `cost_status`, `active_ms`.

### `phase_subagent`

`repository_id`, `phase_execution_id`, `session_id`, `parent_session_id`, nullable `agent`, `started_at`, `ended_at`, `elapsed_ms`, `complete`, `turns`, five token columns, `tokens_in`, `tokens_out`, `cost_usd`, `cost_status`. Model detail may use a child table keyed by child session and model.

Store integers in 64-bit types. Store provider cost as fixed precision decimal, not binary float. Preserve source nulls. Reject or quarantine rows with `data_quality.valid:false` or failed invariants. The producer may retain zero-valued compatibility totals on an invalid row, so those values must never enter aggregates.

## 5. UI specification

Add a **Phase Efficiency** page and link it to the existing miss/rework page. Do not merge active effort and miss cost into one unexplained score.

### 5.1 Summary cards

- Completed phases.
- Median and p90 wall-clock duration.
- Complete-window, complete-coverage observed active time, with `n of N eligible` beneath it.
- Input, output, reasoning and cache tokens.
- Measured provider cost; rate-card estimates in a separate card labelled `estimate - tokens x rate card`.
- Subagents spawned and token contributors, shown as `contributors / spawned`.
- Incomplete windows and partial/unavailable active coverage as data-quality cards.

### 5.2 Charts

- Stacked token trend by command phase: input, output, reasoning, cache read, cache write.
- Wall-clock duration distribution by command phase, excluding incomplete windows and showing excluded count.
- Observed active time versus wall-clock by execution; only complete windows with complete active coverage in comparisons.
- Model mix by token share and turn share. Never group a mixed-model execution solely under its dominant model for model-efficiency analysis.
- Subagent fan-out: spawned versus contributors, plus child token share.
- Cost and token trend by model/tier with measured and estimated dollars separated.
- Optional miss/rework correlation from the separate [miss telemetry contract](Miss-Telemetry-TfLens-From-AIFP.md).

### 5.3 Execution table

Columns: start, command phase, completion badge, elapsed, observed active time and coverage badge, model mix, five token components, cost type/value/status, turns, contributors/spawned, attempt snapshot, verdict snapshot, project type.

Expanding a row shows:

- Per-model usage and active time.
- Subagent tree using `session_id` / `parent_id`.
- Per-child lifecycle, token/cost/model detail, and zero-token children.
- Data-quality explanations for EOF, missing active timestamps, unpaired tools, missing tier, or provider cost caveat.

### 5.4 Filters

Repository, date range, command phase, model, tier, harness, project type, complete/incomplete, active coverage, tokens scope, verdict snapshot, and has-subagents. A model filter matches any `models[]` member, not only the dominant model.

### 5.5 Empty and unsupported states

- No event file: “Telemetry not enabled or already rotated.”
- Open EOF window: “Phase end not observed; elapsed time unavailable.”
- Partial active coverage: “Observed active time is a lower bound.”
- Claude Code without normalized adapter: “Phase effort telemetry unsupported for this harness.”
- Zero provider cost with non-zero tokens: show `zero-unverified` and the engine caveat, not “free” or measured `$0`.
- Fewer than three records in a comparative cohort: `insufficient data (n=<n>)`.

## 6. Aggregation rules

- Use completed rows only for duration aggregates.
- Use `complete:true` and `active_coverage:"complete"` only for active-time averages, percentiles, ratios, or model comparisons.
- Show partial active effort only per execution or as a separately labelled lower-bound total.
- For token totals, require `complete:true`, `data_quality.valid:true` and `token_status:"complete"`. `legacy-unverified` and incomplete windows remain available for drill-down but not comparisons. For measured-cost totals also require `cost_status:"complete"`. None of these statuses proves best-effort event-delivery completeness.
- Calculate child token share as `subagents.tokens_out / tokens_out` only when the denominator is positive.
- For mixed-model attribution, aggregate `phase_model_usage`; do not assign all phase tokens/cost to `model`.
- Mixed measured cost is valid only where each contributing model row has `cost_status:"complete"`; one `zero-unverified` model makes the phase cost partial.
- Do not sum child usage onto phase totals again; child usage is already included.
- Do not infer a failed child from `spawned - contributors`; label it “zero-token/non-contributing child.”
- Use UTC for storage and filtering; localize display only.
- Preserve command-level executions. Do not manufacture conceptual-phase allocations.

## 7. Known limitations and data-quality flags

| Limitation | Required TfLens behavior |
|---|---|
| Event writes are best-effort | Report ingestion/invariant diagnostics; never silently repair |
| Event file is transient | Upsert promptly and show last successful checkpoint |
| EOF can leave an open window | Keep null end/duration and show incomplete |
| Active time is observed busy wall time | Never label it human effort, CPU time, utilization, or additive compute |
| Assistant/tool/child intervals can overlap | Use the producer union; never add diagnostic component sums |
| OpenCode provider cost can be hardcoded zero on the v2 engine | Store `zero-unverified`, exclude it from measured-cost aggregates and keep estimates separate |
| Claude normalized phase producer is not implemented | Show unsupported/data gap |
| `attempt` and `gate_verdict` are current checklist snapshots applied during export | Do not use them as historically authoritative per-execution outcomes |
| Command phases combine conceptual phases | Label them command phases |
| Token status validates observed numeric values but best-effort delivery has no end-to-end completeness proof | Do not claim event-delivery completeness from token or active coverage |
| Sparse schema-1 events are normalized for backward compatibility | Label `legacy-unverified` and exclude from schema-2 token comparisons |
| Child agent type is optional | Display unavailable; do not infer from titles or model |
| No canonical cross-phase task ID | Require an explicit ingestion cohort; never group a reused session into a task implicitly |

## 8. API/export contract

TfLens API names should remain snake_case and preserve producer semantics. Suggested response:

```json
{
  "schema": 1,
  "filters": {},
  "quality": {
    "phase_records": 120,
    "completed": 116,
    "incomplete": 4,
    "active_complete": 98,
    "active_partial": 12,
    "active_unavailable": 10,
    "invalid": 0
  },
  "phase_summary": {
    "elapsed_ms_median": 92000,
    "elapsed_ms_p90": 310000,
    "observed_active_ms_complete_records": 8840000,
    "tokens": {"input": 1, "output": 1, "reasoning": 1, "cache_read": 1, "cache_write": 1},
    "cost_usd_measured": 12.34,
    "subagents_spawned": 42,
    "subagents_contributors": 35
  }
}
```

Every estimated money key must end in `_usd_estimate`. Measured keys retain `_usd` or `_usd_measured`. Include supporting `n`, exclusions and quality counts beside every ratio/percentile rather than only in a global footer.

## 9. Acceptance tests for TfLens

1. Re-importing the same NDJSON does not duplicate a phase execution.
2. A complete two-second phase displays two seconds; an EOF phase displays no elapsed value.
3. Overlapping assistant/tool/child intervals are unioned once; diagnostic component sums are not added.
4. A partial-coverage execution is excluded from active-effort comparison charts but remains visible in the table.
5. A mixed-model phase contributes each model's own tokens/cost to model charts.
6. Three spawned children with one token contributor display `1 / 3`, and all three appear in detail.
7. A recursive grandchild renders beneath its parent and remains included exactly once in phase totals.
8. Token component sums satisfy compatibility totals; invalid rows are quarantined.
9. Measured and estimated dollars never appear in one aggregate.
10. Missing Claude phase telemetry renders unsupported, not zero.
11. No UI/API permits actor-grouped effort, token, or cost reporting.
12. A schema-2 start/end window with no assistant turn is quarantined rather than displayed as a free run.
13. Non-zero-token zero provider cost is excluded from measured-cost aggregates with `zero-unverified` status.

## 10. Rollout

1. Add schema-2 fixture ingestion and invariant validation.
2. Add normalized phase/model/subagent storage and idempotent re-import.
3. Build data-quality surfaces before comparison charts.
4. Ship the Phase Efficiency page behind a repository capability check.
5. Validate against OpenCode fixtures containing mixed models, parallel recursive children, zero-token children, partial timing, EOF, and provider cost zero.
6. Enable scheduled checkpointing before event rotation.

## YOLO Decisions

- Implemented schema 2 rather than documenting the old partial answer because elapsed time, observable active effort, complete model mix, stable phase identity, and spawned child count were absent.
- Kept wall-clock and observed active time separate, and unioned overlapping intervals because assistant envelopes can contain tool/child work. Reverse by removing active-time capture while retaining phase boundaries.
- Kept `spawned`, `contributors`, and compatibility `count` separate because token-bearing sessions are not the same as sessions launched. Reverse by hiding the additional UI dimensions, not by rewriting source events.
- Removed raw command arguments from new phase-start events because they can contain prompts, paths, or secrets and are unnecessary for these metrics. There is no recommended reversal.
- Retained command phase naming because no trustworthy event currently separates combined conceptual phases. Reverse when the producer emits an authoritative conceptual-phase field.
