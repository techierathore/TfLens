# TfLens — Development telemetry

**Generated:** 2026-08-29 (fourth pass, after the build-output untracking) · **Source:** `docs/metrics/*.jsonl` via `.tfcore/telemetry/tf-metrics.sh --report . --json`
**Every figure below is the tool's.** Nothing is recomputed by hand, and nothing is pooled across a provenance boundary.

> **How to read this page.** Three separations are load-bearing and are never crossed:
> **live vs backfilled** (this repo has **0 backfilled** gate records — everything below is live),
> **`project_type`** (`app` and `docs` are reported side by side, never summed), and
> **attribution/cost confidence** (a miss whose origin is not `linked` never enters a per-model
> figure; a fix whose cost is not `sole` never enters a headline cost figure).
> An `n` is printed beside every rate. Rates from `n < 3` are reported as *insufficient data*.

---

## 1. The headline: a human still found more than the gates did

| | `app` (live) | `docs` (live) |
|---|---|---|
| REQs scored | 146 | 113 |
| **First-pass rate** | **21%** (n=31) | **94%** (n=106) |
| **Escape rate** | **67%** (n=13 attributed failures) | **0%** |

**These two columns must not be averaged.** A docs REQ has no screen, so it can never fail the visual
gate; an app REQ can. Summing them produces a number that describes nothing.

**The `app` escape rate of 67% is still the finding of this report.** Two in three attributable
app-side failures were found by a **human running the application**, not by a gate — the direct record
of the 2026-08-28 UAT session, in which four defects were found on a build every gate had just passed.

It was 75% a day ago. It fell because the gates caught three defects on 2026-08-29, **not** because
fewer escaped: the numerator is unchanged and the denominator grew. A falling escape rate driven by
gates finding more is the only kind worth having, and it is worth saying which kind it is.

### Where app-side failures were caught

| Caught by | n | Note |
|---|---|---|
| **escaped — no gate fired** | **6** | UAT; the whole finding |
| acceptance | 4 | found by the gates themselves — three of them by the BRD §13 parity gate on 2026-08-29 |
| visual | 1 | the `/repos` mockup drift |
| unattributed | 2 | |

`late_gate_coverage`: the **perf** gate (added 2026-08-10) has run **once** and caught **0**. One run
is not evidence either way; it is printed so a low catch count is not misread as a low defect rate.

---

## 2. Misses — what was missed, and which practice let it through

29 miss records · 29 fixes · **3 still open** · 0 `wont-fix` · 0 orphans.

### Which practice failed (`why_missed`, n=28 of 29 eligible; 0 predate the field)

| Practice that failed | n | What it means |
|---|---|---|
| `missing-checklist-item` | 10 | **Nothing in the spec covered the behaviour at all** |
| `insufficient-verify-method` | 8 | Acceptance existed; no gate could have caught this class |
| `ambiguous-acceptance` | 5 | Two honest readings of the clause |
| `dependency-not-declared` | 2 | |
| `instruction-ignored` | 2 | A written framework rule existed and was not honoured |
| `other` | 1 | |

**`missing-checklist-item` is the largest bucket, and it grew this pass.** The dominant failure mode
on this project is not sloppy building — it is **things nobody wrote down**. Four of today's records
are exactly that, and all four are now REQs with acceptance clauses and guardrail tests:
`REQ-NFR-015` (fail loudly when an asset 404s), `REQ-NFR-016` (build output is not source),
`REQ-NFR-017` (the DevGuide leads with screens), and `REQ-UI-044` (**a UI construct whose failure
mode the harness cannot reproduce is one the harness cannot sign off**).

### What kind of defect

| `miss_class` | n |
|---|---|
| `wrong-behaviour` | 13 |
| `unspecified-gap` (**design miss**) | 6 |
| `partial-implementation` | 5 |
| `standards-violation` | 4 |

**Design-miss share 21%** · **Escape share 32%**.

> The 32% escape *share* and the 67% escape *rate* in §1 measure different populations and are
> reported side by side, never merged (SCHEMA.md §5.5.5).

### Who found them

| Found by | n |
|---|---|
| `agent-review` | 12 |
| **`owner`** | **10** |
| `gate` | 6 |
| `self-smoke` | 1 |

**A human found ten; gates found six** — up from three, entirely because the BRD §13 parity gate
caught all three of the 2026-08-29 defects before anyone published a number from them. That is what a
gate is worth, and it is the argument TF-007 makes for adding the one the framework still lacks.

### Attribution — 17 of 29 usable

`attributed_n` **17**, `attribution_excluded` **12**. Only records whose origin run was actually found
in `runs.jsonl` carry a model; the other twelve were written `null` by the emitter rather than guessed.

| Origin phase | n | | Origin agent | n |
|---|---|---|---|---|
| `build-phase` | 13 | | `general-purpose` | 8 |
| `fix-issues` | 1 | | `flow-master` | 6 |
| `handoff-phase` | 1 | | `trblazeui` | 2 |
| `split-brd` | 1 | | | |

`by_origin_model`: **`claude-opus-5` — 16**. Every attributed miss came from one model, so this table
ranks phases and agents but **cannot compare models**: there is nothing to compare against.

---

## 3. Cost of rework — apportioned, not measured

| Figure | Value |
|---|---|
| `tokens_per_miss_measured` | **null — no fix run touched exactly one miss** |
| `tokens_per_miss_apportioned` | 47,160 |
| Fixes costed `sole` / `shared` / unattributable | **2** / 26 / 1 |
| `cost_recovered_n` | **4** — windows the old derivation had written off |
| `cost_usd` | **null — Claude Code's transcript carries no cost** |

**The 47,160 figure is apportioned and must never be quoted as measured.** Twenty-six of 28 fixes are
`shared`: each fix run closed several misses at once, so its token window divides across them rather
than measuring any one. No dollar figure is produced — a rate-card estimate printed beside measured
token counts would be an estimate wearing a measurement's clothes.

**`cost_recovered_n` is new, and it is 4.** These are windows the stream had stamped `none` and the
recomputed divisor got back. Until 2026-08-29 TfLens read the stored `cost_attribution` string; the
reference now recomputes it per fix run, because the stored value is written one record at a time (a
run closing four misses stamps `shared:1..shared:4` and only the last is right) and pre-2026-08-28
records carry `none` from the empty-`reqs_touched` bug. BRD §13 caught the divergence. The figure is
reported separately so a jump in rework cost reads as a corrected derivation rather than as the work
having become more expensive.

---

## 4. Throughput

| | |
|---|---|
| Runs | 26 — `log-miss` 5 · `fix-issues` 5 · `build-phase` 4 · `amend-docs` 2 · `mockups` 2 · `metrics-report` 2 · `verify-phase` 2 · `handoff-phase` 1 · `split-brd` 1 · `triage-issues` 1 |
| **Rework ratio** | **150%** |
| Throughput (median) | 20.93 REQs/hour |
| Batch size (median) | 21.5 REQs |
| Tokens total | 3,329,747 |
| Tokens per Verified REQ | 9,073 |
| Sessions | 9 (2 duplicates collapsed) |
| Commits | 12 over 3 active days — 4.0/day |

**Rework ratio is 150%**: there have now been half again as many rework passes as REQs — the average
REQ has been revisited more than once, and some several times. Read it with the 21% app first-pass rate — the same fact stated twice.
Median batch size 21.5 is part of the picture: a pass carrying twenty REQs cannot fail small.

---

## 5. What is missing, and what these numbers cannot tell you

- **The `app` gate-distribution sample is n=13.** The escape bucket dominates it and is corroborated
  independently by the miss stream (9 owner-found). Treat the *shape* as real, the *percentages* as
  provisional.
- **`docs` records are stale** (`stale_types: ["docs"]`) — the 94% first-pass rate describes a period
  that has ended.
- **No model comparison is possible.** One model produced every attributed miss.
- **No measured cost per miss exists**, and none will until a fix run touches exactly one REQ. The
  honest way to get one is smaller fix batches, which is a real trade against throughput.
- **`why_missed` covers 24 of 25**; none predate the field. **`escapes_missing_why` is 0** — every
  owner-found record says why nothing caught it, which is the most valuable field in the stream.
- **Three misses remain open:** `MISS-…0828-21` (build output still tracked in git — blocked on one
  owner-run command, `scripts/untrack-build-output.sh`), `MISS-…0828-25` (the Coverage assertion —
  now fixed and `Verified`, awaiting its close record) and **`MISS-…0829-01`** (`REQ-NFR-019`: nothing
  stops a row entering the store with a `source_sha` no sync ever obtained — 155 such rows were found
  and purged on 2026-08-29, and that is the single most consequential open item on this list, because
  `source_sha` is what a quotable figure is pinned to).
- **A tenth owner-found miss was added on 2026-08-29 (`MISS-…0829-04`) and it is worth reading**, because
  it is not a defect in the app: the handover script for `REQ-NFR-016` did its job correctly and
  *reported* it in a way that read as failure. Removing 1,962 files from the index stages 1,962
  deletions, so the editor then lists them all under "Changes to be committed" — indistinguishable from
  "nothing happened" unless you read the change type. Nobody had run it end to end and looked at what
  the operator would see. **A deliverable that works and cannot be seen to work has not been delivered**,
  and this stream is the only place that fact is written down.
- **The one number worth acting on** is still not a rate: the largest `why_missed` bucket is
  `missing-checklist-item`, and it grew. The gates were not failing to run — they were running against
  acceptance clauses that never mentioned the thing that broke. Every fix this session added the
  missing clause *and* a test for it, which is the only move that changes this figure next time.
