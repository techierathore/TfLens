# TfLens — Development Telemetry

> Generated 2026-08-29 from the append-only streams in `docs/metrics/` by
> `bash .tfcore/telemetry/tf-metrics.sh --report . --json`. Every figure below is that script's
> arithmetic, not this document's. Read `.tfcore/telemetry/SCHEMA.md` §6 before quoting anything here.
>
> **There is no "overall" number on this page, and that is deliberate.** First-pass rate, gate catch
> distribution and escape rate are never pooled across `project_type`, and never across live and
> backfilled records. A `docs` requirement has no screen to fail a visual gate on, so pooling it with an
> `app` requirement would understate that gate by construction. The two columns below sit side by side
> and are never summed.

## Stream inventory

| Stream | Records | Note |
|---|---:|---|
| `gates.jsonl` | 405 | 0 backfilled — every record written at the moment of the event |
| `runs.jsonl` | 30 | |
| `misses.jsonl` | 47 misses + 46 fixes | 0 orphan fixes, 0 orphan amends |
| `sessions.jsonl` | 10 | written by the SessionEnd hook, never by an agent |
| `commits.jsonl` | 15 | `pre-commit` hook present on this clone |

`stale_types: ["docs"]` — the `docs` figures below are not being added to; they describe a stream that
has stopped moving, not current practice.

---

## 1. First-pass rate — what reaches `Verified` on attempt 1

**Live records only. No backfilled records exist in this repository, so there is no reconstructed
arithmetic anywhere on this page.**

| `project_type` | REQs scored | First-pass | Rate |
|---|---:|---:|---:|
| **app** | 147 | 31 | **21%** |
| **docs** | 113 | 106 | **94%** |

These two numbers must not be averaged. The gap is real but it is not a quality comparison: a `docs`
REQ is graded by reading a file, an `app` REQ by driving a browser through acceptance, data-render and
visual-truth gates, so the `app` column has three more ways to fail on attempt 1 and a far longer tail
of re-verification. `0 REQs excluded by backfill taint` in both columns.

## 2. Gate catch distribution — which gate caught each failure

| Gate | app | docs |
|---|---:|---:|
| acceptance | 4 | 4 |
| render | — | 2 |
| visual | 1 | — |
| **escaped** | **22** | — |
| unattributed | 2 | 2 |
| **n (failures scored)** | **29** | **8** |

`escaped` is not a gate. It is the record written when **no gate fired and a human found the defect**,
and on the `app` axis it is 22 of 29 — larger than every real gate combined. That single row is the
finding of this report.

**Late-gate coverage.** `perf` entered the enum on 2026-08-10, long after this stream started, so its
raw share is structurally understated. Its honest denominator: **ran 1, caught 0**. No share is printed
for it, because one run cannot support one.

## 3. Escape rate — defects that reached a human instead of a gate

| `project_type` | Escape rate |
|---|---:|
| **app** | **91%** |
| **docs** | 0% |

**91% of scored `app` failures were found by a person, not by a gate.** The 2026-08-29 mockup-parity
UAT is most of that number: 16 `escaped` records in a single sitting, every one of them on a REQ that
already read `Verified` and had passed acceptance, data-render and visual-truth.

`found_by` across all 47 misses: **owner 28 · agent-review 12 · gate 6 · self-smoke 1.** A gate is the
fourth most common way a defect is discovered in this project.

---

## 4. Misses — what was missed, who let it through, what repair cost

47 misses, 46 fixes, **21 still open**, 26 resolved, 0 wont-fix. 0 orphan fixes and 0 orphan amends, so
the lifecycle joins are intact.

### 4.1 Which practice failed (`why_missed`)

Answered on 46 of 47 eligible records; **0 escapes are missing a reason**.

| `why_missed` | n |
|---|---:|
| **insufficient-verify-method** | **24** |
| missing-checklist-item | 11 |
| ambiguous-acceptance | 6 |
| dependency-not-declared | 2 |
| instruction-ignored | 2 |
| other | 1 |

**This is the report's headline.** `insufficient-verify-method` means *the acceptance existed and no
gate could have caught this class of defect* — it is more than half the stream, and more than twice
`missing-checklist-item`. The specification is not the weak link here; the verification is. That is the
same conclusion the escape rate reaches from the other direction, and it is now written down as
`REQ-NFR-020` (no gate compares a built screen to its approved mockup) and raised upstream as **TF-008**.

### 4.2 What kind of defect

| `miss_class` | n |
|---|---:|
| partial-implementation | 19 |
| wrong-behaviour | 15 |
| **unspecified-gap** | **7** |
| standards-violation | 4 |
| regression | 1 |
| spec-contradiction | 1 |

**Design-miss share 15%** — 7 of 47 are things the specification itself omitted, caught by a human
months after the phase that made them. Those are the records nothing else in the framework can produce.

### 4.3 Attribution — and what it excludes

**34 of 47 misses carry `origin_confidence: linked`. 13 are excluded** from every per-phase, per-agent
and per-model figure below, because their origin could not be resolved to a real `runs.jsonl` record.
The figures are computed over 34, not 47, and no attempt is made to distribute the other 13.

| Origin phase | n | | Origin agent | n |
|---|---:|---|---|---:|
| **build-phase** | **28** | | trblazeui | 17 |
| fix-issues | 4 | | flow-master | 9 |
| handoff-phase | 1 | | general-purpose | 8 |
| split-brd | 1 | | | |

Per-model: `claude-opus-5` 34 — the whole attributed set. **With one model in the data there is no
comparison to make**, and none is drawn; the column exists so that it becomes answerable once a second
model appears, not so that a single-model figure can be read as a verdict.

`build-phase` owning 28 of 34 is what you would expect from where code is written, and says nothing on
its own about whether that phase is unusually error-prone — it is also by far the largest phase.

### 4.4 What repair cost

| | n |
|---|---:|
| `cost_attribution: sole` | 2 |
| `cost_attribution: shared` | 43 |
| `cost_unattributable` | 1 |
| `cost_recovered` | 4 |

- **Tokens per miss (measured, sole-attributed only): insufficient data (n=2).** No headline token
  figure is printed. A `sole` attribution means one fix run closed exactly one miss, which is the only
  shape where the run's token window *is* that miss's repair cost.
- Tokens per miss (apportioned, `shared` included): **31,383** — shown here **as an adjacent figure, not
  as the cost of a miss**. 43 of 46 fixes were batched, and dividing a batch evenly assumes every miss in
  it cost the same, which is not something this data knows.
- **Measured USD: none. 0 records carry `cost_usd`.** Only OpenCode reports real spend and no OpenCode
  fix record in this project carries it yet. Every dollar figure elsewhere in the product is a rate-card
  estimate and is labelled as one.

---

## 5. Throughput and rework (pooled — these are exempt)

Volume and cadence are comparable across project types, so these are the only pooled figures here.

| Measure | Value |
|---|---:|
| Runs | 30 |
| **Rework ratio** | **150%** |
| Throughput (median) | 16.33 REQs/hour |
| Batch size (median) | 21.5 REQs |
| Sessions | 10 |
| Tokens (total) | 3,822,984 |
| Tokens per Verified REQ | 10,388.5 |
| Cost USD | not measured |
| Commits | 15 over 4 active days (3.75/day) |

`runs_by_cmd`: build-phase 4 · fix-issues 6 · log-miss 6 · metrics-report 4 · triage-issues 2 ·
verify-phase 2 · amend-docs 2 · mockups 2 · split-brd 1 · handoff-phase 1.

**Rework ratio 150%** — half again as many repair runs as build runs (`fix-issues` 6 against
`build-phase` 4). Read it beside §3: work is not reaching `Verified` on the first pass and is coming
back through the fix door, and it is coming back because a person found it, not because a gate did.

---

## 6. What is missing, and what would change these numbers

- **No measured dollars anywhere (0 `cost_usd` records).** The cost of rework is only quotable in
  tokens, and even then only over 2 sole-attributed fixes. Fixing one miss per fix run — or recording
  `cost_usd` on OpenCode runs — is what would make §4.4 answerable.
- **13 of 47 misses are unattributed.** Naming `origin_run_id` when a miss is logged is what moves a
  record into §4.3.
- **One model in the attributed set**, so no per-model comparison is possible yet.
- **`perf` has run once.** Its catch rate is not knowable from one run.
- **The `docs` stream is stale** — its 94% first-pass rate describes a stream that has stopped, and
  should not be read as current practice.
- **The gate set itself is the biggest gap.** 22 escaped against 5 real gate catches on the `app` axis,
  and `insufficient-verify-method` on 24 of 46 answered records, both say the same thing: the gates
  measure whether a screen is *alive*, not whether it is *right*. `REQ-NFR-020` is the proposed fix and
  it is `Planned`, not built — so nothing on this page should be expected to improve until it is.
