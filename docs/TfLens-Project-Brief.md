# TfLens — Project Brief (v2)

> **Name note:** "Analyst" was rejected — TechieFlow already has an `analyst` agent; "TfMetrics" collides with `tf-metrics.sh`. TfLens: a read-only lens over the frameworks' telemetry.

**What it is:** A Blazor Server application (Docker on VPS — infra config supplied separately, out of scope here) that pulls the telemetry TechieFlow and AI-First-Playbook **already emit** from the GitHub repos, and reports on it. It builds **no capture layer, no ingestion API, and no per-machine agents** — capture is the frameworks' job and already works across Claude Code, OpenCode, and (via `harness: null`) any other tool that runs the tasks. TfLens is a read-only consumer.

**Sources of truth (read before writing any code — parse the real files, not an assumed schema):**
- `docs/metrics/{runs,gates,sessions,commits}.jsonl` in each TechieFlow-managed repo — canonical schema: `TechieFlow/.tfcore/telemetry/SCHEMA.md` (schema v=1). Reference implementation of every reporting rule: `.tfcore/telemetry/tf-metrics.sh`. TfLens is, functionally, that script's `--rollup` as a web UI — it must never be *less* strict than it.
- `verification/telemetry/events.ndjson` in Playbook-managed repos (phase-start / turn / phase-end events) — different shape, separate adapter, Phase 3.

**Plan context:** A-V verification vehicle — built through the full TechieFlow phase sequence, side-project hours, never a plan deliverable. Note: A0′ ("logging live, three runs") is satisfied by the frameworks' existing emission in the working repos, **not** by this app — the only machine-side task is running `update-framework.sh` on each clone so the per-clone hooks (`.git/` never clones) actually exist. TfLens can trail A0′ without blocking it.

**Timebox:** 1–2 days. Phase order is hard.

---

## Phase 1 — Pull + parse + store

1. **Repo puller** (`BackgroundService` + a manual "Sync now" button):
   - Config (env / appsettings): list of repos `{owner, name, branch, kind: techieflow|playbook}`, a fine-grained read-only GitHub PAT (contents:read — several repos are private), poll interval.
   - Per sync, per repo: read the latest commit SHA touching the telemetry path; if unchanged since last sync, skip. Otherwise fetch the stream files (they are small; whole-file fetch is fine) and store the raw text verbatim under `data/raw/<repo>/<stream>-<sha>.jsonl` before parsing. Raw files are the rebuild source — write a `rebuild` command that drops SQLite and reparses everything.
   - Never write anything back to any repo. Read-only, structurally.
2. **Parser → SQLite**, one table per stream (`runs`, `gates`, `sessions`, `commits`) plus a `sync_state` table (repo, last SHA, last sync ts, per-stream record counts). Columns follow SCHEMA.md field names exactly; unknown fields land in a JSON overflow column rather than being dropped (`v` may grow past 1).
   - **Idempotent + dedupe per SCHEMA.md:** commits dedupe on `sha` (duplicates are *expected* — union merge across machines); OpenCode `sessions` records are cumulative snapshots — keep, per `session_id`, only the record with the highest `output_tokens` (tie: latest `ts`); `runs`/`gates` dedupe on their natural identity (`ts`+`app`+`cmd` / `ts`+`app`+`req_id`+`run_id`) so re-parsing raw files never double-counts.
   - Preserve verbatim: `backfilled`, `inferred`, `project_type`, `project_type_inferred`, `harness`, `provenance`-relevant fields. These drive everything in Phase 2.

## Phase 2 — Report pages

All figures computed at render/report time from the streams — **nothing derived is ever written back into a stream table.** Enforce SCHEMA.md §6 **in code, with no flag to disable it**, exactly as `tf-metrics.sh` does:

- Live and backfilled records never pool; backfilled figures appear only in an adjacent labelled column.
- First-pass rate, gate catch distribution, and escape rate never pool across `project_type`; `project_type_inferred` records report as **unclassified**, never silently as `app`.
- Any REQ with even one backfilled record is excluded from the live first-pass rate (its live `attempt` restarts at 1) — list the excluded REQ IDs.
- Any metric with fewer than 3 supporting records renders as `insufficient data (n=…)`, never as a number.
- `cost_usd` never pools across harness (Claude Code records are `null` by design; a mixed sum silently under-reports). Tokens may compare across harness; dollars may not.
- Late-added gates (e.g. `perf`, added 2026-08-10): report catch rate against records whose `gates_run` actually contains the gate, never against the total distribution — show both numbers side by side with the coverage `n`.

**Pages:**

1. **Coverage / health (first page, always):** per repo — last sync, last commit SHA, days since newest record per stream, record counts, live-vs-backfilled counts. A repo whose newest `sessions`/`commits` records are stale means a clone isn't pushing or lacks hooks — say so on screen. Every other number on the site is suspect until this page is green.
2. **The three questions (per project_type, live-only):** first-pass rate · gate catch distribution (with `escaped` as its own row, never folded into a gate) · escape rate. This is the headline page and the B3 evidence base.
3. **Harness comparison — the portability page:** per-harness (claude-code / opencode / null) record volumes, token totals, tokens-per-verified-REQ, verdict mix. Real `cost_usd` shown for OpenCode only, clearly labelled as the only measured dollars in the system. This page *is* the B1 story rendered as data.
4. **Routing & economics:** routing drift (`routed:false` runs, tier vs observed model) from the §2.5 fields · tokens by model · **counterfactual repricing**: total tokens repriced as if every run used the most expensive model observed, vs. actual mix — prices from an editable `prices.json`, figure labelled **estimate** (tokens × rate card, not measured spend). · Rework ratio, REQ throughput, batch size, commit cadence (poolable metrics, per SCHEMA.md §8).
5. **Weekly snapshot export:** button + endpoint emitting markdown + JSON to `data/reports/<date>/` — the diffable numbers that feed the plan's Numbers table and B3. Never mixes provenances in one figure.

## Phase 3 — Playbook adapter

Parse `verification/telemetry/events.ndjson` (and the joiner output if committed) into **separate** tables — Playbook process-gates and TechieFlow assertion-gates are different axes (SCHEMA.md §11: `gate` vs `phase_gate`) and must not share columns or charts. A minimal page: phase token/cost totals, main-vs-subagent split via `parentID`. When the Playbook converges on schema v1, this adapter shrinks; don't over-build it.

## Constraints

- Blazor Server, current LTS .NET, SQLite, TrBlazeUI components where they fit (dogfood). EF Core or Dapper — pick one, record in DECISIONS.md.
- Dashboard entirely behind auth (single-user cookie auth is enough). GitHub PAT read-only, stored as secret/env, never in the repo.
- TfLens displays only what the streams carry — IDs, counts, durations, verdicts (SCHEMA.md §9 privacy holds downstream: no requirement text, no commit subjects, nothing from `src/`).
- Out of scope, recorded as such in the README: any capture/ingestion endpoint; OTLP; per-machine agents; Codex-CLI harness detection (a TechieFlow `tf-emit.sh` change — TfLens already handles it by reporting `harness: null` honestly); writing anything to any repo.

## Parity check — the mandatory acceptance test

**Principle:** two independent implementations now compute the same metrics from the same files — `tf-metrics.sh` (the existing, trusted reference: the provenance rules of SCHEMA.md §6 are enforced in its code) and TfLens (new, unproven). Correct implementations must agree exactly. Any disagreement is, by definition, a bug in TfLens. The script is never "fixed" to match the app.

**Why this test exists:** the dangerous failure mode here is not a crash — it is a *plausible wrong number*. A pooling bug (e.g. one backfilled record leaking into the live first-pass rate, or a `library` record pooled into an `app` gate distribution) produces a figure that looks completely normal, gets exported in the weekly snapshot, and ends up quoted publicly in B3. Once published, it cannot be defended. The parity diff is the only cheap way to catch that class of bug.

**Procedure (run before TfLens's export is used for any weekly Numbers row or any post, and re-run after every parser change):**

1. Pick a fixed dataset: clone the same repos TfLens is configured to pull, checked out at the exact commit SHAs TfLens's `sync_state` table shows for its last sync. Same data in, or the comparison is meaningless.
2. Run the reference: `bash .tfcore/telemetry/tf-metrics.sh --rollup <repo1> <repo2> ... --json > reference.json`.
3. Run TfLens's machine-readable export over its parsed store for the same repos → `analyst.json`.
4. Compare, figure by figure (a small compare script is part of this project — key-by-key, not a text diff, since key order and formatting may differ):
   - per-repo record counts per stream, and backfilled counts;
   - commit duplicates collapsed;
   - the tainted-REQ exclusion list (must be the identical set of REQ IDs);
   - first-pass rate, gate catch distribution, escape rate — per project_type, live and backfilled separately;
   - late-gate coverage (`ran` / `caught` per gate);
   - every poolable metric (rework ratio, throughput, batch size, tokens, cadence);
   - every `insufficient data (n=…)` marker — the *n* must match too, and a figure the reference refuses to print TfLens must also refuse to print.
5. **Zero tolerance:** any mismatch fails the test. Debug TfLens until the diff is empty. The only acceptable permanent difference is a metric TfLens adds that the script does not compute at all (e.g. counterfactual repricing, harness comparison) — those have no reference and must instead be spot-checked by hand against the raw JSONL once.
6. Record the passing run in DECISIONS.md: date, commit SHAs of the dataset, and the compare script's output. That entry is the license to trust the export.

**Standing rule after ship:** the weekly snapshot export is only quotable if the last parity run on record postdates the last parser change.

## Definition of done

- [ ] All configured repos syncing; coverage page green with real staleness numbers
- [ ] Three-questions page renders per project_type with live/backfilled separation and taint-exclusion list visible
- [ ] Harness comparison page shows claude-code vs opencode side by side, with OpenCode-only dollars
- [ ] Counterfactual repricing figure renders from `prices.json`, labelled estimate
- [ ] Weekly snapshot export produces markdown + JSON
- [ ] Parity check (section above) passed with an empty diff, recorded in DECISIONS.md
- [ ] DECISIONS.md records: storage choice, dedupe keys, anything cut for the timebox

Finish by reporting: any field observed in real files that SCHEMA.md doesn't document, any place where TfLens's numbers disagree with `tf-metrics.sh --rollup` on the same data (they must not), and what breaks first when schema v=2 appears.
