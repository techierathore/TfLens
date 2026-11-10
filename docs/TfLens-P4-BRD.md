# TfLens — Business Requirements — Phase 4: Estimates

| | |
|---|---|
| App | TfLens |
| Kind | app |
| Size | Large |
| Phase | 4 of 4 |
| Status | Target |
| Stack answer set | Blazor Server · PostgreSQL 16 · Dapper · TrBlazeUI · Docker |
| Date | 2026-11-10 |

**Phase 4 of 4 — estimates against actual, from the framework's new stream.** Requirement ids: **BRD-211 to BRD-219**. Ids run on across the four phases and are never reused or renumbered.

Other phases: [Phase 1 — Foundation](./TfLens-BRD.md) · [Phase 2 — Reports](./TfLens-P2-BRD.md) · [Phase 3 — Depth](./TfLens-P3-BRD.md) · The map is [TfLens-Phases.md](./TfLens-Phases.md).

**The whole-project context lives in phase 1 and is not repeated here:** the executive summary, business objectives, scope and out-of-scope list, stakeholders, the context and component diagrams, the constraints, the parity procedure (§13), the success metrics and the risks. This document holds what belongs to this phase: its screen and its requirements.

## 1. Summary

TechieFlow v1.3.0 (deployed here on 2026-11-08) adds a sixth stream, `docs/metrics/estimates.jsonl`
(SCHEMA.md §2.8): what a phase **should** cost in output tokens and wall time, written by
`*estimate-phase` before the build. One record per checklist row and one per phase total, per step —
build, verify, fix, and triage (after handoff) — each a low, mid and high, with its basis, its run
count and the estimating model.

This phase reads that stream the way `runs.jsonl` is read and adds one page, **Estimates**, setting
each phase's estimate beside what the live runs measured.

One rule governs it, SCHEMA.md §6's **fourth separation: an estimate never pools with a
measurement** — two columns, never one number. Every number is code over the two streams; owner UAT
time prints **not measured**. On 2026-11-10 no connected public repository carries estimates, so every
one reads **no estimate yet**, which is correct; real ones appear by themselves when a public repository
writes `estimates.jsonl`, TfLens's own first. The page is proven on the **Sample app** test files (§3.1).

**Parity.** `tf-metrics.sh --estimates` is the oracle and is never edited. The **Total** row and the
**per-row** actuals have no oracle and are hand-checked once (BRD-217).

## 2. Screens and flow

| Screen | Route | Role | What it answers | Fields | Feature | Requirements | Mockup |
|---|---|---|---|---|---|---|---|
| Estimates | `/estimates` | Owner, Author | **What did the framework expect each phase to cost, and what did it cost?** Per repository, per phase and per estimating model, the latest estimate beside the actual from the live runs, in three parts — build, machine QA (verify + fix), after handoff (triage) — and a total of build + machine QA. Sits in the Reports group of the sidebar after Phase effort, under the same repo picker and framework switch as the other report pages. A budgeting view: it says how far the forecast and the measurement lie apart and never scores either. | Repo picker (All repos, or one repo) · Framework switch · one card per (repo, phase, estimating model) with its header line (estimated by, at, rows, "estimated after the fact" when a hindsight batch) · the parts table: Part · Estimate mid (low–high), tokens and time · Actual, tokens and time · Runs (n, apportioned, with no tokens, main-thread only) · Rows covered `[n/m]` · By step disclosure with the position (within / above / below) · Owner UAT time row reading "not measured" · runs that named no row, per step · per-row drill-down (collapsed) · empty state "no estimate yet" · standing note that estimate and actual never pool | F-ESTIMATE | [BRD‑211](#brd-211) · [BRD‑212](#brd-212) · [BRD‑213](#brd-213) · [BRD‑214](#brd-214) · [BRD‑215](#brd-215) · [BRD‑216](#brd-216) · [BRD‑217](#brd-217) · [BRD‑218](#brd-218) · [BRD‑219](#brd-219) · [BRD‑89](./TfLens-P2-BRD.md#brd-89) | [estimates.html](./mockups/estimates.html) · Playbook state: [estimates-playbook.html](./mockups/estimates-playbook.html) |

**Flow.** Sign in → any report page → sidebar **Estimates** → pick a repository (or All repos) →
read each phase card → expand **By row** under a phase for the per-row figures. The framework switch
moves to the Playbook state, which reads **no estimate yet** (the Playbook writes no estimate stream).

## 3. Requirements

### Amendment 2026-11-10 — estimates against actual, from the framework's new stream

- <a id="brd-211"></a>**BRD-211** — System shall read `docs/metrics/estimates.jsonl` from every connected TechieFlow repository exactly as it reads `runs.jsonl`: fetched on the ordinary sync and on Sync now, archived raw under `data/raw/<userId>/`, recognised by **Import metric files** as a sixth stream name, and parsed into its own `Estimate` table — known fields (`v`, `ts`, `kind`, `app`, `project_type`, `phase`, `req_id`, `cmd`, `tokens_out` low/mid/high, `duration_s` low/mid/high, `basis`, `basis_runs`, `model`, `harness`, `basis_note`, `rows_n`, `rows_no_precedent_n`) as typed columns, anything else in the `Overflow` JSON column. Dedupe key `(UserId, Repo, Ts, App, Phase, ReqId, Cmd, Model)`, first wins. A missing file is an empty stream, never an error. Removing a repository and Rebuild from raw cover the table like every other stream. `UserId` is a parameter of every read. *(F-ESTIMATE, F-SYNC, F-IMPORT)*
  - *Acceptance:* When the three Sample app test files are imported on /repos, then Estimate holds 44 Sample app rows and 0 Sample empty rows, and re-importing adds none.
- <a id="brd-212"></a>**BRD-212** — System shall compute the reconciliation as a field-for-field port of `analyse_estimates` in `tf-metrics.sh`: per repository, phase and estimating model, the **latest forecast batch** (one `ts`); a batch carrying `basis_note: "retro"` is used only when that phase and model has no forecast, and is labelled **estimated after the fact**. The batch's rows are the phase's rows. Per step, the rows the actual is measured over are those whose estimate for that step had a figure. The actual is the **live** runs (never backfilled, never voided by a `run-void`) of that step (`triage-and-fix` and `triage-issues` both count as `triage`) whose `reqs_touched` meet those rows; a run counts the share `rows hit ÷ reqs_count` of its `tokens_out` and duration (its timestamps win, as in every reader), capped at one, and is counted **apportioned** when that share is below one. Tokens count only from runs whose `tokens_scope` is set and not `none`; the others are counted as *with no tokens*, and `main`-scope runs are counted as *main-thread only*. Fewer than three runs: `insufficient data (n=…)`. Where both an estimate and a numeric actual exist, the actual's position is **within**, **above** or **below** the low–high range. Runs of each step that named no row are counted over the whole repository and **never shared out** into any phase. The three parts are build (`build-phase`), machine QA (`verify-phase` + `fix-issues`, over the rows both had a figure for) and after handoff (`triage`). *(F-ESTIMATE, F-ENGINE)*
  - *Acceptance:* When the engine reconciles the Sample app test files on /estimates, then its estimates block equals tf-metrics.sh --estimates --json on those files, key for key.
- <a id="brd-213"></a>**BRD-213** — System shall never pool an estimate with a measurement (SCHEMA.md §6, fourth separation). No figure on any page, in the export or in the API adds, averages, subtracts or divides an `Estimate` value with a `Run` value; there is no ratio, no difference, no accuracy percentage and no single blended number. The estimate's mid and range sit in one column and the actual in a second. The `Estimate` table is read only by the reconciliation (BRD-212, BRD-217) and the export (BRD-218); no other figure in TfLens reads it, and the reconciliation never reads an estimate into a run figure or a run into an estimate figure. *(F-ESTIMATE, F-ENGINE)*
  - *Acceptance:* When a test scans the result types on /estimates, then no member combines an estimate with an actual, and only EstimateMetrics, the store and the exporter read Estimate.
- <a id="brd-214"></a>**BRD-214** — User can open **Estimates** (`/estimates`) from the Reports group of the sidebar, after Phase effort, and see one card per repository, phase and estimating model, under the same repo picker as BRD-210 (All repos or one repository, the choice carried across the report pages for the session) and the header framework switch. The card's header says the repository, the phase, the estimating model, when the estimate was made and how many rows it covers, and **estimated after the fact** for a hindsight batch. Its table has one line per part — **Build**, **Machine QA (verify + fix)**, **After handoff (triage)**, **Total (build + machine QA)** — and a last line **Owner UAT time** reading **not measured**. Each line shows the estimate's mid with its low–high range in one column (tokens, and time beneath), the actual in a second column (tokens, and time beneath) with **apportioned** beside it when any run counted was apportioned, the run counts, and `[n/m]` where the part covers fewer than all the phase's rows. Beneath the table, the count of runs that named no row, per step, labelled "in the repository, not shared out". A collapsed **By step** disclosure gives the same two columns for each of the four steps separately, with the actual's position — **within**, **above** or **below** the range, or a dash — as `tf-metrics.sh` reports it per step. *(F-UI, F-ESTIMATE — REQ-UI-078)*
  - *Acceptance:* When the Sample app test files are loaded on /estimates, then each card's Build, Machine QA, After handoff and Owner UAT lines show exactly the figures in §3.1.
- <a id="brd-215"></a>**BRD-215** — User can expand **By row** under each phase card and see one line per checklist row of the estimate batch: the row id, and for each of build, machine QA and after handoff the estimate's mid (low–high) with its basis (`repo`, `family`, `none`) and `basis_runs`, beside the row's actual — the live runs of that step that touched the row, each counting `1 ÷ reqs_count` of its tokens and duration, labelled **apportioned** when that share is below one, `insufficient data (n=…)` under three runs. The list is paged and sortable by row id; nothing on it is summed into a per-row total. *(F-UI, F-ESTIMATE — REQ-UI-079)*
  - *Acceptance:* When By row is expanded on the Sample app phase 1 Opus card on /estimates, then each row shows exactly the per-row figures in §3.1.
- <a id="brd-216"></a>**BRD-216** — User can see the page's empty and partial states in words, never as zeros: **no estimate yet** — run `*estimate-phase` before `*build-phase` — when the picked repository's estimate stream is empty or absent (on 2026-11-10 every connected repository), and in the Playbook state of the page, which has no estimate stream; with **All repos** picked, one card per repository that has estimates and one line listing the repositories that have none — never a card that adds repositories together; **no precedent** for a step whose estimate is null (`basis: none`); `insufficient data (n=…)` for an actual under three runs. *(F-UI, F-ESTIMATE — REQ-UI-080)*
  - *Acceptance:* When the Sample empty test file is loaded on /estimates, then the page shows `no estimate yet` and no card, table, number or `0`.
- <a id="brd-217"></a>**BRD-217** — System shall compute the two figures `tf-metrics.sh` does not, by stated rules, and label both as TfLens additions with no parity oracle. **Total (build + machine QA):** over the rows that build, verify and fix all had a figure for, the estimate is the sum of those rows' low, mid and high for the three steps (the rule `tf-estimate.sh` prints as "build + machine QA"), and the actual is the reconciliation's part rule applied to the three steps over the same rows; after handoff is **never** inside the total, so a phase with no triage history still has one. Where those rows are none, the total reads **not one figure: the steps cover different rows**. **Per-row actual:** the rule in BRD-215. Both are hand-checked once against the raw JSONL and recorded in DECISIONS.md, and both stand where `extras` stands. *(F-ESTIMATE, F-ENGINE)*
  - *Acceptance:* When the Sample app test files are loaded on /estimates, then the three Totals read 55.5k, 83.5k and 58.0k beside 76.0k, 76.0k and 78.0k.
- <a id="brd-218"></a>**BRD-218** — System shall write the reconciliation into the snapshot export's `tflens.json` as an `estimates` block in the **exact key layout** of `tf-metrics.sh --rollup --json` (`phases[]`, `repos_with_estimates`, and under each phase `cmds`, `parts`, `actual`, `actual_in_range`), so the parity check diffs it key for key; the total and per-row figures (BRD-217) are written beside it under keys listed in `tools/parity-compare.py`'s `ADDED_KEYS` with their reason. The Snapshot export page itself is unchanged. *(F-EXPORT, F-ESTIMATE)*
  - *Acceptance:* When parity-compare.py diffs tf-metrics.sh --rollup --json and tflens.json over the Sample app test files on /export, then it exits 0 with no `estimates` finding.
- <a id="brd-219"></a>**BRD-219** — Every number on the Estimates page is computed by code over the `Estimate` and `Run` tables at request time: no model or AI call anywhere in the request path, no stored derived value (BRD-30), no configuration key, query parameter or toggle that changes a figure. **Owner UAT time** is a fixed text, **not measured**; no field, input, setting or stream column accepts a value for it. *(NFR — REQ-NFR-027)*
  - *Acceptance:* When a source scan covers EstimateMetrics and the page on /estimates, then it finds no HTTP or AI client, and Owner UAT time reads exactly `not measured`.

### 3.1 The Sample app — the test files every acceptance line uses

The Sample app (app id `SampleApp`) is made up and exists only as test files in
`tests/TfLens.Core.Tests/Fixtures/Estimates/`. No real repository's data is needed or used.

| File | Records | Cases |
|---|---|---|
| `sample__sample-app/estimates.jsonl` | 44 | phase 1 estimated by `claude-opus-5-5` and by `claude-sonnet-5-5` (two estimating models); phase 2 by `claude-opus-5-5`; after handoff `basis: none` on every row (no precedent) |
| `sample__sample-app/runs.jsonl` | 15 | actual inside, above and below the range; fix with 2 and 1 runs (insufficient data); one fix run that named no row; one build run on `REQ-UI-001` (phase 1) and `REQ-FN-003` (phase 2), apportioned half to each; no triage runs |
| `sample__sample-empty/estimates.jsonl` | 0 | an empty stream: no estimate yet |

**Expected figures** — tokens, then time; estimate mid (low–high) beside actual. `tf-metrics.sh --estimates` prints every figure here except the Totals and the per-row actuals (BRD-217).

| Card | Part | Estimate | Actual | Runs |
|---|---|---|---|---|
| Phase 1 · Opus | Build | 42.0k (28.0k–56.0k) · 38m20s (23m20s–53m20s) | 52.0k · 50m00s, apportioned | 3, 1 apportioned |
| | Machine QA | 13.5k (7.5k–19.5k) · 35m00s (20m00s–50m00s) | 24.0k · 55m00s | 5 |
| | Total | 55.5k (35.5k–75.5k) · 1h13m (43m20s–1h43m) | 76.0k · 1h45m, apportioned | 8, 1 apportioned |
| Phase 1 · Sonnet | Build | 70.0k (55.0k–85.0k) · 56m40s (43m20s–1h10m) | 52.0k · 50m00s, apportioned | 3, 1 apportioned |
| | Machine QA | 13.5k (7.5k–19.5k) · 35m00s (20m00s–50m00s) | 24.0k · 55m00s | 5 |
| | Total | 83.5k (62.5k–104.5k) · 1h31m (1h03m–2h00m) | 76.0k · 1h45m, apportioned | 8, 1 apportioned |
| Phase 2 · Opus | Build | 45.0k (30.0k–60.0k) · 45m00s (30m00s–1h00m) | 65.0k · 1h05m, apportioned | 3, 1 apportioned |
| | Machine QA | 13.0k (8.0k–18.0k) · 33m20s (23m20s–43m20s) | 13.0k · 30m00s | 4 |
| | Total | 58.0k (38.0k–78.0k) · 1h18m (53m20s–1h43m) | 78.0k · 1h35m, apportioned | 7, 1 apportioned |

On every card: After handoff `no precedent` beside `insufficient data (n=0)`; Owner UAT time `not measured`; runs that named no row: build 0 · verify 0 · fix 1 · after handoff 0. By step, tokens: phase 1 build **within** (Opus) and **below** (Sonnet), phase 1 verify 15.0k **above** 6.0k (3.0k–9.0k), phase 1 fix `insufficient data (n=2)`; phase 2 build **above**, verify 10.0k **within** 9.0k (6.0k–12.0k), fix `insufficient data (n=1)`.

| Per row, phase 1 · Opus card | Build | Machine QA | After handoff |
|---|---|---|---|
| `REQ-FN-001` | 15.0k (10.0k–20.0k) · insufficient data (n=2) | 4.5k (2.5k–6.5k) · insufficient data (n=2) | no precedent · insufficient data (n=0) |
| `REQ-FN-002` | 15.0k (10.0k–20.0k) · insufficient data (n=1) | 4.5k (2.5k–6.5k) · 11.0k, 25m00s, apportioned | no precedent · insufficient data (n=0) |
| `REQ-UI-001` | 12.0k (8.0k–16.0k) · insufficient data (n=2) | 4.5k (2.5k–6.5k) · 9.0k, 20m00s, apportioned | no precedent · insufficient data (n=0) |

## 4. Development status

Written by the status gate after every build, verify and handoff; not by hand.

**Snapshot as of 2026-11-10.** Live per-requirement status: `PROJECT-STATUS.md` and the Requirements Status table in `docs/TfLens-P4-Checklist.md`.

| Screen | Requirements | Verified | Open | Status |
|---|---|---|---|---|
| Functional requirements | 5 | 0 | 5 | Planned |
| UI / Pages | 3 | 0 | 3 | Planned |
| Non-functional | 1 | 0 | 1 | Planned |

## 5. Where the rest lives

| What | Where |
|---|---|
| Executive summary, objectives, scope, stakeholders, diagrams | [phase 1 BRD](./TfLens-BRD.md) §1–§8 |
| Constraints and assumptions | [phase 1 BRD](./TfLens-BRD.md) §12 |
| Parity check — the mandatory acceptance test | [phase 1 BRD](./TfLens-BRD.md) §13 |
| The provenance rules this phase extends (BRD-30..36, BRD-89) | [phase 2 BRD](./TfLens-P2-BRD.md) |
| The repo picker this page reuses (BRD-210) | [phase 3 BRD](./TfLens-P3-BRD.md) |
| Architecture, decisions log, data model | [TfLens-Architecture.md](./TfLens-Architecture.md) (not phased) |
| This phase's work list | [TfLens-P4-Checklist.md](./TfLens-P4-Checklist.md) |
| This phase's screen, control by control | [TfLens-P4-UIDesign.md](./TfLens-P4-UIDesign.md) |
| The stream's definition | `.tfcore/telemetry/SCHEMA.md` §2.8 and §6 |
