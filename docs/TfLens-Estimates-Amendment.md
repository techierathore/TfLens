# TfLens — Estimates against actual (amendments of 2026-11-10)

*Written by the analyst during `*amend-docs TfLens`. Documents only; no code; nothing committed.*

Words used below:
- **the reference script** — `.tfcore/telemetry/tf-metrics.sh`, the framework's report script. TfLens's
  parity check compares TfLens's figures against it key by key, and it is never edited.
- **Sample app** — a made-up project (app id `SampleApp`) that exists only as test files in this
  repository. It is not a real repository and is never connected to anything.

## First amendment — applied

Phase 4, Estimates: BRD-211 to BRD-219, nine checklist rows, `docs/mockups/estimates.html` and
`estimates-playbook.html`, Architecture §5.9, §6.x and ADR-034 / ADR-035, `appPhase: 4`, BRD-89
extended to the estimate separation. Two figures are not computed by the reference script, so they are hand-checked once: the Total
row (build + machine QA) and the per-row actuals (BRD-217).

## Second amendment — proven on the Sample app, not on a real project (applied 2026-11-10)

TfLens is public; no private project's figures or name go into it. Every figure and every mention of
the private project in the phase 4 documents, the two mockups and `PROJECT-STATUS.md` is replaced by
the Sample app. No real repository's estimates are needed: they appear on the page by themselves when a
public repository carries an `estimates.jsonl` (TfLens's own first).

### The test files (referenced from P4-BRD §3.1)

Folder: `tests/TfLens.Core.Tests/Fixtures/Estimates/`, in the fixture layout the other fixtures use.

| File | Records | Cases it covers |
|---|---|---|
| `sample__sample-app/estimates.jsonl` | 44 | Phase 1 estimated twice — by `claude-opus-5-5` and by `claude-sonnet-5-5` (**two estimating models**); phase 2 by `claude-opus-5-5`. Every row has a build, verify and fix figure; after handoff (`triage`) is `basis: none` on every row (**no precedent**). |
| `sample__sample-app/runs.jsonl` | 15 | Phase 1 build: actual **inside** the Opus range, **below** the Sonnet range. Phase 2 build and phase 1 verify: actual **above** the range. Phase 2 verify: **inside**. Fix: 2 runs on phase 1 and 1 on phase 2 (**insufficient data**). One fix run that **named no row**. One build run touching `REQ-UI-001` (phase 1) and `REQ-FN-003` (phase 2) (**apportioned**, half to each). No triage runs. |
| `sample__sample-empty/estimates.jsonl` | 0 | An **empty stream**: "no estimate yet". This is a third file, because an empty stream and a full one cannot be the same file. |

`tf-metrics.sh --estimates` was run on these files on 2026-11-10 and printed every figure below.

### Expected figures

| Card | Part | Estimate mid (low–high) | Actual | Runs |
|---|---|---|---|---|
| Phase 1 · Opus | Build | 42.0k (28.0k–56.0k) · 38m20s (23m20s–53m20s) | 52.0k · 50m00s, apportioned | 3, 1 apportioned |
| | Machine QA | 13.5k (7.5k–19.5k) · 35m00s (20m00s–50m00s) | 24.0k · 55m00s | 5 |
| | After handoff | no precedent | insufficient data (n=0) | 0 |
| | Total *(not in the reference script)* | 55.5k (35.5k–75.5k) · 1h13m (43m20s–1h43m) | 76.0k · 1h45m, apportioned | 8, 1 apportioned |
| Phase 1 · Sonnet | Build | 70.0k (55.0k–85.0k) · 56m40s (43m20s–1h10m) | 52.0k · 50m00s, apportioned | 3, 1 apportioned |
| | Machine QA | 13.5k (7.5k–19.5k) · 35m00s (20m00s–50m00s) | 24.0k · 55m00s | 5 |
| | Total *(not in the reference script)* | 83.5k (62.5k–104.5k) · 1h31m (1h03m–2h00m) | 76.0k · 1h45m, apportioned | 8, 1 apportioned |
| Phase 2 · Opus | Build | 45.0k (30.0k–60.0k) · 45m00s (30m00s–1h00m) | 65.0k · 1h05m, apportioned | 3, 1 apportioned |
| | Machine QA | 13.0k (8.0k–18.0k) · 33m20s (23m20s–43m20s) | 13.0k · 30m00s | 4 |
| | Total *(not in the reference script)* | 58.0k (38.0k–78.0k) · 1h18m (53m20s–1h43m) | 78.0k · 1h35m, apportioned | 7, 1 apportioned |

By step, tokens: phase 1 build **within** (Opus) and **below** (Sonnet); phase 1 verify 15.0k
**above** 6.0k (3.0k–9.0k); phase 1 fix `insufficient data (n=2)`; phase 2 build **above**, verify
10.0k **within** 9.0k (6.0k–12.0k), fix `insufficient data (n=1)`. Runs that named no row: build 0 ·
verify 0 · fix 1 · after handoff 0. Sample empty: `no estimate yet`.

Per row, phase 1 Opus card *(not in the reference script)*: `REQ-FN-002` machine QA 4.5k (2.5k–6.5k) beside 11.0k ·
25m00s, apportioned; `REQ-UI-001` machine QA beside 9.0k · 20m00s, apportioned; every build actual
`insufficient data (n=1 or 2)`; every after-handoff cell `no precedent` beside `insufficient data (n=0)`.

### The change-set

| # | Kind | Where | What |
|---|---|---|---|
| 1 | MODIFY | BRD-211 | Acceptance: the three test files go in; `Estimate` holds 44 rows for Sample app, 0 for Sample empty; re-import adds none. |
| 2 | MODIFY | BRD-212 | Acceptance: on the Sample app files, the engine's `estimates` block equals `tf-metrics.sh --estimates --json` key for key. |
| 3 | MODIFY | BRD-214 | Acceptance: the Sample app files in, the parts figures of the table above on the page. |
| 4 | MODIFY | BRD-215 | Acceptance: the per-row figures above on the phase 1 Opus card. |
| 5 | MODIFY | BRD-216 | Acceptance: the Sample empty file in, `no estimate yet` and no number. |
| 6 | MODIFY | BRD-217 | Acceptance: Totals 55.5k / 83.5k / 58.0k beside 76.0k / 76.0k / 78.0k. |
| 7 | MODIFY | BRD-218 | Acceptance: parity over the Sample app files exits 0 with no `estimates` finding. |
| 8 | MODIFY | P4-BRD §1, §2 | The private project removed; the page is proven on the Sample app; real estimates appear by themselves. New §3.1 holds the expected-figures table above and the file list. |
| 9 | MODIFY | P4 checklist | The nine rows' acceptance lines follow 1–7; status stays Not Started (nothing is built, so nothing needs re-verifying). |
| 10 | MODIFY | Mockups | `estimates.html` shows the three Sample app cards with the figures above; "no estimate yet" lists Sample empty. `estimates-playbook.html` needs no change. |
| 11 | MODIFY | `PROJECT-STATUS.md` | The blocker about importing the private project's files is removed. |
| 12 | MODIFY | This file | Rewritten without the private project. |

**Not changed, and flagged for you:** two older documents name the private project:
`docs/TfLens-UAT-Readiness-2026-10-12.md` (three lines saying it was not imported because it is not
public) and `docs/TfLens-UsageGuide.md` line 29. Neither carries its figures. They are outside phase 4,
so I have left them as they are.
