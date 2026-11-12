# TfLens — Checklist (Phase 4)

<!-- AGENT WORKING DOCUMENT — markdown only, never rendered to HTML.
     Phase 4 of 4. Added by *amend-docs on 2026-11-10 (Estimates); phase 3 already held 127 rows against a cap of 100.
     REQ ids run on across phases and are NEVER reused or renumbered. -->

| | |
|---|---|
| App | TfLens |
| Size | Large |
| Phase | 4 of 4 |

## Goal

Read the estimate stream TechieFlow v1.3.0 added on 2026-11-08 (`docs/metrics/estimates.jsonl`, SCHEMA.md §2.8) the way `runs.jsonl` is read, and add one page, **Estimates**, that sets each phase's estimate beside what the live runs measured — two columns, never one number. An estimate never pools with a measurement (SCHEMA.md §6, fourth separation). Every number is code over the two streams; owner UAT time is `not measured`.

**Phase 4 — Estimates.** The other phases are `docs/TfLens-Checklist.md`, `docs/TfLens-P2-Checklist.md`, `docs/TfLens-P3-Checklist.md`; `docs/TfLens-Phases.md` is the map. The oracle for the phase figures is `tf-metrics.sh --estimates` (never edited); the Total row and the per-row actuals have no oracle (BRD-217).

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-FN-147 | Estimate stream — fetch, archive, import and parse `estimates.jsonl` into its own `Estimate` table (BRD-211, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-FN-147 — importing the three Sample app test files on /r` | [view](#d-req-fn-147) |
| REQ-FN-148 | Reconciliation — field-for-field port of `analyse_estimates` (latest forecast, apportioned actual, rowless count, MIN_N, range position, three parts) (BRD-212, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-FN-148 — no precedent, insufficient data and rowless run` | [view](#d-req-fn-148) |
| REQ-FN-149 | The fourth separation — an estimate never pools with a measurement; no ratio, no difference, no blended figure (BRD-213, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-FN-149 — no result type can hold a ratio, difference or ` | [view](#d-req-fn-149) |
| REQ-UI-078 | Estimates page — `/estimates` under the repo picker and framework switch; one card per repo, phase and estimating model; Build · Machine QA · After handoff · Total · Owner UAT time (BRD-214, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-UI-078 — each Sample app card shows exactly the Build, M`; Estimates renders and looks right @1280/390 in light and dark mode; matches its mockup | [view](#d-req-ui-078) |
| REQ-UI-079 | Estimates page — the per-row drill-down under each phase card (BRD-215, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-UI-079 — By row on the Sample app phase 1 Opus card show`; Estimates renders and looks right @1280/390 in light and dark mode; matches its mockup | [view](#d-req-ui-079) |
| REQ-UI-080 | Estimates page — empty and partial states in words: `no estimate yet`, `no precedent`, `insufficient data (n=…)`, repos with none (BRD-216, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-UI-080 — with the Sample empty test file loaded, the pag`; Estimates renders and looks right @1280/390 in light and dark mode; matches its mockup | [view](#d-req-ui-080) |
| REQ-FN-150 | The two figures with no oracle — Total (build + machine QA) and the per-row actual, by stated rules, hand-checked once (BRD-217, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-FN-150 — the three Totals read 55.5k, 83.5k and 58.0k be` | [view](#d-req-fn-150) |
| REQ-FN-151 | Export — the `estimates` block in `tflens.json` in the oracle's exact key layout, so parity diffs it (BRD-218, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-FN-151 — parity-compare.py covers every estimates figure` | [view](#d-req-fn-151) |
| REQ-NFR-027 | Estimates page — every number is code over the two streams; no model call; owner UAT time is fixed text `not measured` (BRD-219, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-NFR-027 — on /estimates Owner UAT time reads exactly not` | [view](#d-req-nfr-027) |
| REQ-FN-152 | Parity gaps after phase 3 — discovery cost and `per_miss_cost` in the export; an empty repository's app name from its checklist (BRD-220, Phase 4) | Verified | 100% | 2026-11-12 verify: PASS — test `REQ-FN-152 — the app is named from exactly one docs checklis` | [view](#d-req-fn-152) |

**Status values:** `Not Started` · `In Progress` · `Implemented` · `Verified` · `Done (pre-existing)` · `Needs re-verify` · `PARTIAL` · `FAIL` · `Blocked` · `Owner-UAT` · `N/A`.

## UI / Pages

### Page: Estimates (`/estimates`)

<a id="d-req-ui-078"></a>
- **REQ-UI-078** — Estimates page: `/estimates` in the Reports group after Phase effort, under the BRD-210 repo picker and the framework switch; one card per repository, phase and estimating model, its header naming the model, when, how many rows, and `estimated after the fact` on a hindsight batch; the parts table Build · Machine QA (verify + fix) · After handoff (triage) · Total (build + machine QA) · Owner UAT time, each with the estimate's mid (low–high) in one column and the actual in a second, `apportioned` where it applies, run counts and `[n/m]` rows; the runs-that-named-no-row line; a collapsed By step disclosure with the within / above / below position. *BRD:* BRD-214. *Mockup:* docs/mockups/estimates.html · docs/mockups/estimates-playbook.html.
  - *Acceptance:* When the Sample app test files are loaded on /estimates, then each card's Build, Machine QA, After handoff and Owner UAT lines show exactly the figures in §3.1.

<a id="d-req-ui-079"></a>
- **REQ-UI-079** — Estimates page: By row under each phase card — one line per row of the estimate batch, build, machine QA and after handoff each with the estimate's mid (low–high), basis and run count (for a step with no precedent, the basis `none` alone, as the mockup draws it — amended 2026-11-12) beside the row's own actual (each run counting 1 ÷ its row count, `apportioned` when below one, `insufficient data (n=…)` under three runs); paged, sortable by row id, no per-row total. *BRD:* BRD-215. *Mockup:* docs/mockups/estimates.html.
  - *Acceptance:* When By row is expanded on the Sample app phase 1 Opus card on /estimates, then each row shows exactly the per-row figures in §3.1.

<a id="d-req-ui-080"></a>
- **REQ-UI-080** — Estimates page: the empty and partial states in words, never zeros — `no estimate yet` when the picked repository has no estimate records and always in the Playbook state; with All repos, one card per repository with estimates and a line naming those without, never a card that adds repositories together; `no precedent` for a null estimate; `insufficient data (n=…)` for an actual under three runs. *BRD:* BRD-216. *Mockup:* docs/mockups/estimates.html · docs/mockups/estimates-playbook.html.
  - *Acceptance:* When the Sample empty test file is loaded on /estimates, then the page shows `no estimate yet` and no card, table, number or `0`.

## Functional requirements

### Phase 4 — the estimate stream and its reconciliation (F-ESTIMATE, added 2026-11-10)

<a id="d-req-fn-147"></a>
- **REQ-FN-147** — Estimate stream: fetched on sync and Sync now, archived raw, recognised by Import metric files, parsed into its own `Estimate` table (typed columns for every SCHEMA.md §2.8 field, the rest in `Overflow`), deduped on `(UserId, Repo, Ts, App, Phase, ReqId, Cmd, Model)` first-wins; a missing file is an empty stream; remove-repo and Rebuild cover it. *BRD:* BRD-211.
  - *Acceptance:* When the three Sample app test files are imported on /repos, then Estimate holds 44 Sample app rows and 0 Sample empty rows, and re-importing adds none.

<a id="d-req-fn-148"></a>
- **REQ-FN-148** — Reconciliation: a field-for-field port of `analyse_estimates` in `tf-metrics.sh` — latest forecast batch per repo, phase and model (retro only when no forecast, labelled); per step, the covered rows are those with a figure; the actual from live, non-voided runs of the step (`triage-and-fix` + `triage-issues` = `triage`) meeting those rows, each counting `rows hit ÷ reqs_count` (capped at 1, apportioned below 1); tokens only where `tokens_scope` is set and not `none`; `insufficient data (n=…)` under three runs; within / above / below per step; rowless runs counted per repo and never shared out; the three parts build, machine QA, after handoff. *BRD:* BRD-212.
  - *Acceptance:* When the engine reconciles the Sample app test files on /estimates, then its estimates block equals tf-metrics.sh --estimates --json on those files, key for key.

<a id="d-req-fn-149"></a>
- **REQ-FN-149** — The fourth separation: no figure anywhere adds, averages, subtracts or divides an `Estimate` value with a `Run` value; no ratio, difference, accuracy percentage or blended number; `Estimate` is read only by `EstimateMetrics`, the store and the exporter. *BRD:* BRD-213 (held to BRD-89: no switch relaxes it).
  - *Acceptance:* When a test scans the result types on /estimates, then no member combines an estimate with an actual, and only EstimateMetrics, the store and the exporter read Estimate.

<a id="d-req-fn-150"></a>
- **REQ-FN-150** — The two figures with no oracle: **Total** = build + verify + fix over the rows all three had a figure for (estimate: the sum of those rows' low, mid, high; actual: the part rule over the same rows), after handoff never inside, and `not one figure: the steps cover different rows` when there are none; **per-row actual** = the BRD-215 rule. Both labelled TfLens additions, hand-checked once against raw JSONL and recorded in DECISIONS.md. *BRD:* BRD-217.
  - *Acceptance:* When the Sample app test files are loaded on /estimates, then the three Totals read 55.5k, 83.5k and 58.0k beside 76.0k, 76.0k and 78.0k.

<a id="d-req-fn-151"></a>
- **REQ-FN-151** — Export: `tflens.json` carries `estimates` in the exact key layout of `tf-metrics.sh --rollup --json`; the Total and per-row figures sit beside it under keys added to `ADDED_KEYS` in `tools/parity-compare.py` with their reason; the Snapshot export page is unchanged. *BRD:* BRD-218.
  - *Acceptance:* When parity-compare.py diffs tf-metrics.sh --rollup --json and tflens.json over the Sample app test files on /export, then every estimates figure is compared and none differs.

<a id="d-req-fn-152"></a>
- **REQ-FN-152** — Parity gaps opened after phase 3: the export's `misses` block carries the discovery cost figures and the `per_miss_cost` list exactly as `analyse_misses` computes them (SCHEMA.md §5.5.12); a repository with no records is named from its single `docs/*-Checklist.md` (one GET of the `docs/` listing on sync, or a bundle entry on import), else its repository name. *BRD:* BRD-220.
  - *Acceptance:* When parity-compare.py diffs tf-metrics.sh --rollup --json and tflens.json over the Sample app test files on /export, then it exits 0.

## Non-functional

<a id="d-req-nfr-027"></a>
- **REQ-NFR-027** — Every number on the Estimates page is computed by code over the `Estimate` and `Run` tables at request time: no model or AI call in the request path, no stored derived value, no key, parameter or toggle that changes a figure; Owner UAT time is fixed text and nothing accepts a value for it. *BRD:* BRD-219.
  - *Acceptance:* When a source scan covers EstimateMetrics and the page on /estimates, then it finds no HTTP or AI client, and Owner UAT time reads exactly `not measured`.

