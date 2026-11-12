# TfLens — Development Telemetry

> Generated 2026-11-12 (refreshed after the repo-picker fix) from the append-only streams in `docs/metrics/` by
> `bash .tfcore/telemetry/tf-metrics.sh --report . --json`. Every figure below is that script's
> arithmetic, not this document's. Read `.tfcore/telemetry/SCHEMA.md` §6 before quoting anything here.
>
> **There is no "overall" number on this page, and that is deliberate.** First-pass rate, gate catch
> distribution and escape rate are never pooled across `project_type`, and never across live and
> backfilled records. The two columns below sit side by side and are never summed.

## Stream inventory

| Stream | Records | Note |
|---|---:|---|
| `gates.jsonl` | 1,355 | 0 backfilled — every record written at the moment of the event |
| `runs.jsonl` | 120 live | 1 more is voided by a `run-void` record and is in no figure |
| `misses.jsonl` | 162 misses + 132 fixes | 78 open, 84 resolved |
| `sessions.jsonl` | 47 | 6 duplicate `session_id`s collapsed |
| `commits.jsonl` | 35 | over 14 active days |
| `estimates.jsonl` | phase 4 | one forecast batch, 2026-11-11 |

TfLens is classified `app` now, but its early records carry `docs`. Both segments below are periods of
the same project, not two projects, and they are never pooled.

---

## 1. First-pass rate — what reaches `Verified` on attempt 1

| `project_type` | REQs scored | Rate |
|---|---:|---:|
| **app** | 224 | **35%** |
| **docs** | 113 | **94%** |

A `docs` REQ is graded by reading a file; an `app` REQ is driven through acceptance, render, visual and
mockup-parity gates. The gap is how many ways each can fail, not a quality comparison.

## 2. Gate catch distribution — which gate caught each failure

| Gate | app (214 failures) | docs (8 failures) |
|---|---:|---:|
| build | 2 (1%) | — |
| test-users | 2 (1%) | — |
| acceptance | 19 (9%) | 4 (50%) |
| render | 25 (12%) | 2 (25%) |
| visual | 13 (6%) | — |
| mockup-parity | 74 (35%) | — |
| **escaped** | **35 (16%)** | — |
| unattributed | 44 (21%) | 2 (25%) |

Late gates (live `app` records only; a share above is not a catch rate): mockup-parity ran on 252
records and caught 74 → 29%; assets ran on 181, caught 0; test-users ran on 16, caught 2 → 12%; perf ran
on 3, caught 0. The visual and mockup-parity findings of 2026-11-12 on `/estimates` were found and fixed
inside one triage-and-fix before its verify wrote a gate record, so they appear as misses (§4), not here.

## 3. Escape rate — defects that reached UAT or production

| `project_type` | Escape rate |
|---|---:|
| **app** | **39%** |
| **docs** | 0% |

## 4. Misses and rework cost

- **162 misses logged** — 78 open, 84 resolved, 0 won't-fix. Design-miss share (the spec, not the
  build): **20%**. Found by a person (owner or production): **44%**, reported beside the escape rate
  above, never merged with it.
- **What was missed:** partial-implementation 56 · unspecified-gap 32 · wrong-behaviour 28 · regression
  24 · spec-contradiction 10 · standards-violation 7 · missed-requirement 4.
- **Why (105 of 161 assessed):** insufficient-verify-method 48 · missing-checklist-item 26 ·
  ambiguous-acceptance 15 · instruction-ignored 8 · dependency-not-declared 5 · other 3. **8 escapes carry
  no `why_missed`.**
- **Whose gap (36 of 75 sorted):** spec 16 · weak-check 13 · ignored 4 · unsaid 3. 86 misses predate the
  `sort` field.
- **Attribution (104 of 161 linked; 57 excluded as inferred or unknown):** origin phase build-phase 72,
  fix-issues 21, amend-docs 8; every linked miss came from `claude-opus-5`. Which model gets which work is
  not random, so this is an observation, not a cause.
- **Discovery cost per miss (new, SCHEMA.md §5.5.12):** insufficient data — 1 measured, 6 not measured,
  155 written before the field existed. The one measured: the owner's UAT report of the repo picker
  (MISS-TfLens-20261112-07, REQ-UI-077) cost **13.0k tokens and 6.1 minutes** to find, beside **39.7k
  tokens** to fix — two columns, never added. The six of 2026-11-12 are all `not measured`: two were found by the
  owner, and the run that found the other four set no mark when each investigation began. Their fixes sit
  beside them at **45.9k tokens each, apportioned 1/5** — a share of one window, not a measurement.
- **Rework cost — measured and shared are separate, never one number:** 5 solely-measured fixes at
  **143.0k output tokens per miss** (list price **$30.30** per miss — a published rate applied to
  measured tokens, not a bill); 95 shared fixes at 38.7k tokens per miss by equal division, which is
  **not a measurement**; 31 fixes with no usable window cannot be costed.

## 5. Effort per phase (runs, not tickets)

| Phase | Runs | Wall clock | Tokens out | Share of output |
|---|---:|---:|---:|---:|
| build-phase | 16 | 51h28m | 6.1M | 37% |
| verify-phase | 28 | 29h25m | 3.0M | 18% |
| fix-issues | 15 | 12h24m | 2.7M | 16% |
| amend-docs | 16 | 5h11m | 1.9M | 11% |
| split-brd | 3 | 3h16m | 958.7k | 6% |
| handoff-phase | 3 | 1h07m | 687.0k | 4% |
| triage-and-fix | 4 | 1h10m | 439.3k | 3% |
| mockups | 3 | 3h07m | 281.9k | 2% |
| triage-issues | 2 | 28m01s | 199.0k | 1% |
| log-miss | 15 | 23m49s | 138.4k | 1% |
| other (5 phases: rename-page, deploy-checklist, feedback-recheck, metrics-report, estimate-phase) | 9 | 3h03m | 185.6k | <1% |

Token windows: 44 runs saw their subagents (`tree`), 62 did not look (`main`), 8 measured nothing and
are left out of every token figure. A `0` fan-out on a `main` run means *not looked*, not *none*.
`build-phase` costing more than `log-miss` is what those phases are, not a finding about either.

## 6. Estimate against actual — two columns, never one number

Phase 4 was estimated on 2026-11-11 by `opencode-go/qwen3.8-max`, over its 9 rows. Every actual reads
**insufficient data**: the build and verify of phase 4 were each one run against TfLens's own rows, and a
figure needs three.

| Part | Estimate — mid (low–high) | Actual |
|---|---|---|
| Build | 289.0k tok (173.2k–431.6k) · 1h23m (58m03s–3h18m) | insufficient data (n=1) |
| Machine QA | 345.5k tok (230.1k–621.1k) · 2h08m (1h33m–3h08m) | insufficient data (n=1) |
| After handoff [3/9 rows] | 55.5k tok (32.5k–126.4k) · 8m36s (4m48s–22m09s) | insufficient data (n=1) |

Runs that named no row are counted in the repository and shared into no phase: verify 3, fix 2, after
handoff 2.

## Poolable figures

Rework ratio **144%** (fix-mode runs / build-phase runs) · batch size median **10** REQs per build ·
throughput median **15.28** REQs/hour · **16.24M** total tokens over 47 sessions · list price
**$3,887.65** over 106 priced runs, **$3.43** per Verified REQ — a price, never a bill · commit cadence
**2.5** per active day.
