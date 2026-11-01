# TfLens — Development Telemetry

> Generated 2026-11-01 from the append-only streams in `docs/metrics/` by
> `bash .tfcore/telemetry/tf-metrics.sh --report . --json`. Every figure below is that script's
> arithmetic, not this document's. Read `.tfcore/telemetry/SCHEMA.md` §6 before quoting anything here.
>
> **There is no "overall" number on this page, and that is deliberate.** First-pass rate, gate catch
> distribution and escape rate are never pooled across `project_type`, and never across live and
> backfilled records. The two columns below sit side by side and are never summed.

## Stream inventory

| Stream | Records | Note |
|---|---:|---|
| `gates.jsonl` | 1,340 | 0 backfilled — every record written at the moment of the event |
| `runs.jsonl` | 106 live | 1 more is voided by a `run-void` record and is in no figure |
| `misses.jsonl` | 155 misses + 123 fixes | 80 open, 75 resolved |
| `sessions.jsonl` | 44 | 4 duplicate `session_id`s collapsed |
| `commits.jsonl` | 33 | over 13 active days |

TfLens is classified `app` now, but its early records carry `docs`. Both segments below are periods of
the same project, not two projects, and they are never pooled.

---

## 1. First-pass rate — what reaches `Verified` on attempt 1

| `project_type` | REQs scored | Rate |
|---|---:|---:|
| **app** | 214 | **32%** |
| **docs** | 113 | **94%** |

A `docs` REQ is graded by reading a file; an `app` REQ is driven through acceptance, render, visual and
mockup-parity gates. The gap is how many ways each can fail, not a quality comparison.

## 2. Gate catch distribution — which gate caught each failure

| Gate | app (211 failures) | docs (8 failures) |
|---|---:|---:|
| build | 2 (1%) | — |
| acceptance | 19 (9%) | 4 (50%) |
| render | 25 (12%) | 2 (25%) |
| visual | 13 (6%) | — |
| mockup-parity | 74 (35%) | — |
| **escaped** | **34 (16%)** | — |
| unattributed | 44 (21%) | 2 (25%) |

Late gates (live `app` records only; a share above is not a catch rate): mockup-parity ran on 249
records and caught 74 → 30%; assets ran on 178, caught 0; perf ran on 3, caught 0; test-users ran on 3,
caught 0.

## 3. Escape rate — defects that reached UAT or production

| `project_type` | Escape rate |
|---|---:|
| **app** | **39%** |
| **docs** | 0% |

This run added two escapes: the owner's UAT report of 2026-11-01 (REQ-UI-076, REQ-UI-077). Both were
gaps in the spec, not in a check, and both are now fixed and Verified.

## 4. Misses and rework cost

- **155 misses logged** — 80 open, 75 resolved, 0 won't-fix. Design-miss share (the spec, not the
  build): **19%**. Found by a person (owner or production): **44%**, reported beside the escape rate
  above, never merged with it.
- **What was missed:** partial-implementation 55 · unspecified-gap 30 · wrong-behaviour 27 · regression
  24 · spec-contradiction 8 · standards-violation 7 · missed-requirement 4.
- **Why (103 of 155 assessed):** insufficient-verify-method 48 · missing-checklist-item 25 ·
  ambiguous-acceptance 14 · instruction-ignored 8 · dependency-not-declared 5 · other 3. **8 escapes carry
  no `why_missed`.**
- **Whose gap (30 of 69 sorted):** weak-check 13 · spec 10 · ignored 4 · unsaid 3. 86 misses predate
  the `sort` field.
- **Attribution (104 of 155 linked; 51 excluded as inferred or unknown):** origin phase build-phase 72,
  fix-issues 21, amend-docs 8; every linked miss came from `claude-opus-5`.
- **Rework cost — measured and shared are separate, never one number:** 5 solely-measured fixes at
  **143.0k output tokens per miss** (list price **$30.30** per miss — a published rate applied to
  measured tokens, not a bill); 89 shared fixes at 39.3k tokens per miss by equal division, which is
  **not a measurement**; 29 fixes with no usable window cannot be costed.

## 5. Effort per phase (runs, not tickets)

| Phase | Runs | Wall clock | Tokens out | Share of output |
|---|---:|---:|---:|---:|
| build-phase | 15 | 50h20m | 5.8M | 37% |
| verify-phase | 26 | 28h24m | 2.9M | 19% |
| fix-issues | 15 | 12h24m | 2.7M | 17% |
| amend-docs | 14 | 2h40m | 1.6M | 10% |
| split-brd | 3 | 3h16m | 958.7k | 6% |
| handoff-phase | 3 | 1h07m | 687.0k | 4% |
| mockups | 3 | 3h07m | 281.9k | 2% |
| triage-and-fix | 2 | 35m46s | 198.2k | 1% |
| triage-issues | 2 | 28m01s | 199.0k | 1% |
| log-miss | 15 | 23m49s | 138.4k | 1% |
| other (4 phases: rename-page, metrics-report, feedback-recheck, deploy-checklist) | 8 | 3h00m | 182.4k | <1% |

Token windows: 39 runs saw their subagents (`tree`), 59 did not look (`main`), 8 measured nothing and
are left out of every token figure. A `0` fan-out on a `main` run means *not looked*, not *none*.

## Poolable figures

Rework ratio **140%** (fix-mode runs / build-phase runs) · batch size median **10** REQs per build ·
throughput median **18.32** REQs/hour · **15.66M** total tokens over 44 sessions · list price
**$3,784.81** over 98 priced runs, **$3.38** per Verified REQ — a price, never a bill · commit cadence
**2.54** per active day.
