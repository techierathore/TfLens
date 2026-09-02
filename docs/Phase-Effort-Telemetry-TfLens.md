# Phase-effort telemetry — TfLens (the "what did each phase cost" lens)

**Status:** the producing side is **SHIPPED** (TechieFlow, 2026-08-31). Nothing in TfLens is implemented yet.
**Target repo:** `/mnt/c/1MyCode/TfLens` (`TfLens.slnx`, .NET / Blazor Server / PostgreSQL).
**Producer contract:** `.tfcore/telemetry/SCHEMA.md` **§2**, **§2.5**, **§2.6** — read those, not this sketch, for the final field list.
**Oracle:** `bash .tfcore/telemetry/tf-metrics.sh --phases <repo> --json` (also rides in `--report --json` under `phases`). Read-only, no git, agent-safe — same standing as `--report`.
**Siblings:** `docs/Miss-Telemetry-TfLens.md` (the miss lens — same house rules, read its §0 first) · `docs/TfLens-TechieFlow-Feedback.md`.

---

## 0. The question, and the honest answer to it

> *"Using the metrics we already capture, can we identify how much effort, time and tokens were spent on each phase — with which model, how long it took, and how many subagents it spun up?"*

**Mostly yes, and the gaps are now closed.** The answer splits three ways, and it is worth being precise about which part was already true, because two of the three were.

### 0.1 Already answerable before 2026-08-31 — no new capture needed

`runs.jsonl` has carried one record per framework command run since the stream started. Each already held:

| Question | Field | Since |
|---|---|---|
| **Which phase?** | `cmd` — `build-phase` · `verify-phase` · `fix-issues` · `day1-*` · `split-brd` · `mockups` · `devguide` · `triage-issues` · `log-miss` · `amend-docs` · `handoff-phase` · `refresh-status` | §2, from day one |
| **How long did it take?** | `started` / `ended` / `duration_s` | §2 |
| **How many tokens?** | `tokens_in` · `tokens_out` · `tokens_cache_read` · `tokens_cache_write` | §2.5, 2026-08-20 |
| **Which model?** | `model` (observed dominant) · `models[]` · `tier` / `tier_model` / `routed` | §2.5 |
| **Which harness?** | `harness` — detected, never declared | §1 |
| **Build vs rework?** | `mode` (`build` / `fix`), `attempt` | §2, §2.5 |
| **How much output?** | `files_written`, `reqs_touched` / `reqs_count`, `build_result` | §2 |
| **Real dollars?** | `cost_usd` — **OpenCode only**, `null` on Claude Code and Codex, never estimated | §2.5, §4 |

So *time · tokens · model · phase* were all there. What was missing was **an aggregation** — nothing in the framework grouped by `cmd` — plus two genuine capture gaps.

### 0.2 Genuinely missing, now implemented (SCHEMA §2.6)

Three fields. Each was missing for the same reason: it was **self-reported** or **not represented at all**.

| Field | Type | Why it was needed |
|---|---|---|
| `subagent_runs` | int | **"How many subagents did it spin?" had no honest answer.** `subagents` is a list of agent *kinds* an agent types into its own emit — it carries no count when the same kind is spawned four times, and nothing checked it against reality. `subagent_runs` is **counted from the harness's own store**: Claude Code, the subagent transcripts under `<transcript-dir>/<session-id>/subagents/` with an in-window assistant message; OpenCode, distinct child sessions in `opencode.db` with in-window output. |
| `tokens_out_subagents` | int | The share of the window the subagents actually consumed. `tokens_out − tokens_out_subagents` is the main thread's own. |
| `model_tokens_out` | object | `{model_id: output_tokens}` — the per-model **split**, not just the winner's name. |

**Why `subagents` stays.** It says *which kinds* were invoked, which the emitter cannot know. `subagent_runs` says *how many actually ran*, which the agent cannot be trusted to report. Both ship; **where they disagree the measured one is right**, and the gap is itself a finding about how accurately tasks self-report. Show both.

**Why the model split matters more than the model name.** A run that spent 90% of its output on one model and 10% on another, and a run that split evenly, are different facts about cost and about routing. `model` (dominant) and `models` (the set) cannot tell them apart, so **any per-model effort figure built on `model` alone silently attributes the whole window to the winner.**

### 0.3 Deliberately still unavailable — do not build a UI that implies otherwise

- **Per-feature / per-REQ cycle time.** A standing non-goal (SCHEMA §0). The unit of work is **the run**, not the ticket. There is no per-feature timing field and there will not be one. `reqs_touched` on a run says which REQs it touched, **not how the run's minutes divided between them** — a build-phase run touching 8 REQs has one duration and one token window, and splitting it 8 ways is arithmetic, not measurement (the same distinction `cost_attribution` draws for misses, §5.5.3).
- **Per-subagent detail.** `subagent_runs` is a count and `tokens_out_subagents` a sum. Which subagent spent what is **not** carried — the transcripts are read for totals only, and per-agent attribution would need a name the transcript path does not reliably give.
- **Dollars on Claude Code or Codex.** `cost_usd` is `null` on both, permanently, and is never computed from a rate card. Report tokens.
- **Session-to-run joins.** `sessions.jsonl` is not joinable to `runs.jsonl` on anything but time (§4). Do not attribute a session's tokens to a run.

---

## 1. The oracle contract — the keys TfLens must match

`bash .tfcore/telemetry/tf-metrics.sh --phases <repo> --json` emits the block below. It also rides inside `--report --json` / `--rollup --json` under the top-level key `phases`, so **the BRD §13 parity gate covers it with no new invocation.**

```jsonc
{
  "runs_live": 13,                       // live (non-backfilled) run records considered
  "scope_coverage": {"tree": 1, "main": 5, "conversation": 3, "none": 4},
  "tokens_out_total": 177900,            // denominator for share_of_tokens_out
  "duration_s_total": 9585,              // denominator for share_of_duration
  "note": "…",
  "phases": {
    "build-phase": {
      "runs": 4,

      "duration_s": {"total": 7800, "median": 1585, "max": 4500, "n": 4},
      "share_of_duration": "81%",

      "tokens_measured_n": 4,            // runs with a usable window  -> the DIVISOR
      "tokens_unmeasured_n": 0,          // excluded, NEVER counted as zero
      "tokens": {"in": 6600000, "out": 155700, "cache_read": 27600000, "cache_write": 418600},
      "tokens_out_median": 38700,
      "tokens_out_per_run": 38925.0,     // null when tokens_measured_n < 3
      "share_of_tokens_out": "87%",

      "models": {                        // from model_tokens_out where present
        "claude-opus-5":  {"runs": 2, "tokens_out": 133300},
        "gpt-5.6-sol":    {"runs": 2, "tokens_out": 22400}
      },
      "harnesses": {"claude-code": 2, "codex": 2},
      "modes": {"build": 3, "—": 1},
      "build_result": {"pass": 3, "not-run": 1},
      "reqs_touched_total": 12,
      "files_written_total": 24,

      "subagents_declared": {"tf-builder": 3, "general-purpose": 1},   // agent-typed

      "fanout": {                        // tokens_scope == "tree" records ONLY
        "observed_n": 1,                 // <- READ THIS FIRST. It is the denominator.
        "unobserved_n": 3,
        "unobserved_not_tree": 2,        // window never read the subagent transcripts
        "unobserved_predates_field": 1,  // written before 2026-08-31
        "spawns_total": 2,
        "spawns_median": 2,
        "spawns_max": 2,
        "runs_with_fanout": 1,
        "tokens_out_subagents": 10000,
        "subagent_share_of_tokens_out": "50%"
      },

      "routing": {"routed": 1, "drifted": 2, "unknown": 1},
      "cost_usd_by_harness": {"opencode": {"usd": 0.230819, "records": 1}}
    }
  }
}
```

**Percent strings, not floats.** `share_of_*` and `subagent_share_of_tokens_out` come back as the same `"87%"` / `"—"` strings the rest of the oracle uses (`pct()`), so parity compares strings. Do not reformat before diffing.

**`null` is a real value here.** `tokens_out_per_run`, `duration_s.median`, `spawns_median` and `spawns_max` are `null` below the `MIN_N = 3` floor or with no data. Render "insufficient data (n=…)", never `0`.

### 1.1 Parity keys to add to the BRD §13 gate

```
phases.runs_live
phases.tokens_out_total
phases.duration_s_total
phases.scope_coverage.*
phases.phases.<cmd>.runs
phases.phases.<cmd>.duration_s.{total,median,max,n}
phases.phases.<cmd>.share_of_duration
phases.phases.<cmd>.tokens.{in,out,cache_read,cache_write}
phases.phases.<cmd>.tokens_measured_n
phases.phases.<cmd>.tokens_unmeasured_n
phases.phases.<cmd>.tokens_out_median
phases.phases.<cmd>.tokens_out_per_run
phases.phases.<cmd>.share_of_tokens_out
phases.phases.<cmd>.models.<model>.{runs,tokens_out}
phases.phases.<cmd>.fanout.{observed_n,unobserved_n,unobserved_not_tree,
                            unobserved_predates_field,spawns_total,spawns_median,
                            spawns_max,runs_with_fanout,tokens_out_subagents,
                            subagent_share_of_tokens_out}
phases.phases.<cmd>.routing.{routed,drifted,unknown}
phases.phases.<cmd>.cost_usd_by_harness.<harness>.{usd,records}
```

---

## 2. The three denominators — the whole design rests on these

Every figure on this page is bounded, and **the bound must be on screen next to the figure.** This is the same rule the miss lens applies (`Miss-Telemetry-TfLens.md` §0.1) for the same reason: an exclusion the reader cannot see is indistinguishable from a bug.

### 2.1 Token figures exclude runs with no window

A run whose token window could not be computed carries `tokens_scope: "none"` (or no scope at all) and **no token numbers**. It is excluded from every token figure and counted in `tokens_unmeasured_n`.

**Do not average it in as zero.** That is precisely the defect TfLens itself reported as **TF-005** on the miss stream — `or 0` cannot tell an absent field from a measured zero, and the error always runs in the direction that flatters the framework. The same mistake on the phase stream would report `*log-miss` costing half what it does, on a repo where four of nine runs happen to be unmeasured. The current data has exactly that shape.

**UI rule:** every token tile shows `measured on n of N runs`. When `tokens_unmeasured_n > 0`, that count is visible, not in a tooltip.

### 2.2 Fan-out figures use `tokens_scope: "tree"` records only

**This is the one most likely to be got wrong, and it fails silently.**

A `main`-scope window never read the subagent transcripts at all. So on such a run, `subagent_runs` is absent — and if a consumer coerces that to `0`, the phase reports "spawns no subagents" when the truth is **"we did not look."** Pooling the two produces a confident fan-out average largely composed of runs that could not have seen a subagent.

The oracle therefore restricts every fan-out figure to `tokens_scope == "tree"` **and** `subagent_runs != null`, and publishes the exclusion split **two ways, because they are two different facts**:

| Key | Means |
|---|---|
| `unobserved_not_tree` | the window was `main` / `conversation` / `none` — **we did not look** |
| `unobserved_predates_field` | tree-scope, but written before `subagent_runs` existed (2026-08-31) — **we could not have looked** |

The second is the `FIELD_SINCE` hazard TfLens already mirrors for `why_missed` (SCHEMA §3.5 / §5.5.6), arriving on a third stream. **Mirror it here too**: `subagent_runs` → `2026-08-31`, `tokens_out_subagents` → `2026-08-31`, `model_tokens_out` → `2026-08-31`.

**UI rule:** the fan-out band is **`observed_n of runs`** first and the numbers second. If `observed_n == 0`, render **"not observed"** — never `0 subagents`.

### 2.3 Dollars are per harness and never pooled

`cost_usd_by_harness` is already split. Claude Code and Codex carry `null` permanently (§4); only OpenCode measures real cost. **Never sum across harnesses, and never price tokens from a rate card** — that would be an estimate presented as a measurement, which is the one thing this whole telemetry design refuses to do.

---

## 3. Proposed UI — `/effort`

A sibling of `/misses` and `/harness`, reading `runs.jsonl` through the same ingest path.

### 3.1 KPI row

| Tile | Value | Sub-line (mandatory) |
|---|---|---|
| Runs recorded | `runs_live` | `n live records` |
| Total wall clock | `duration_s_total` as `Xh YYm` | `over n timed runs` |
| Total output tokens | `tokens_out_total` | **`measured on n of N runs`** |
| Heaviest phase | `cmd` with the largest `share_of_tokens_out` | `{pct} of all output · {pct} of all time` |
| Fan-out observed | `Σ fanout.observed_n / runs_live` | **`n of N runs could be observed`** — this tile is a *coverage* figure, not a quality one |

The last tile is deliberately a coverage figure on the KPI row rather than buried. On today's framework data it would read **1 of 13**, which is the honest headline: fan-out measurement started on 2026-08-31 and most records predate it.

### 3.2 The phase table — the main object

One row per `cmd`, sorted by `share_of_tokens_out` descending.

| Phase | Runs | Wall clock (total / median) | Output tokens | % output | % time | Measured | Fan-out |
|---|---|---|---|---|---|---|---|
| `build-phase` | 4 | 2h10m / 26m | 155.7k | 87% | 81% | 4/4 | 2 spawns over **1 observed** run |

- **`Measured` is a column, not a footnote.** `4/4` in green, `5/9` in amber — a phase whose token figures rest on half its runs is a different claim from one that rests on all of them.
- **Fan-out cells always carry `observed`.** `— (not observed)` where `observed_n == 0`.
- Row expands to §3.3.

### 3.3 Per-phase detail (expanded row)

Four bands, in this order:

1. **Time** — total · median · max, `n` timed. A single stacked bar of each phase's `share_of_duration`.
2. **Tokens** — `out` / `in` / `cache_read` / `cache_write`, plus `tokens_out_per_run` and `tokens_out_median` side by side. **Show both**: a mean far above the median means one long run dominates the phase, which is the most decision-relevant thing on this band.
3. **By model** — a horizontal bar per model from `models{}`, labelled `{output tokens} · {share} · over {runs} run(s)`. Carry the standing caveat once on the page: *this ranking is observational, not causal — which model gets the hard phases is not random.*
4. **Fan-out** — `observed_n of runs` stated first; then spawns (total / median / max), `runs_with_fanout`, `tokens_out_subagents`, `subagent_share_of_tokens_out`. Below it, the **declared-vs-measured** line whenever `Σ subagents_declared != spawns_total`:

   > *declared `tf-builder`×3, `general-purpose`×1 · measured 2 spawns — `subagents` is typed by the agent, `subagent_runs` is counted from the harness store. The measured figure is authoritative (SCHEMA §2.6).*

### 3.4 Routing band

`routing.{routed, drifted, unknown}` per phase. **Routing is observed, never enforced** — `drifted` is drift made visible, not an error, and the page must not style it as a failure.

### 3.5 What NOT to build

- **No per-REQ effort view.** Not derivable; see §0.3. If a stakeholder asks, the answer is that the framework deliberately does not measure it, and the reason is that dividing one run's window across the REQs it touched is arithmetic dressed as measurement.
- **No "phase X is more expensive than phase Y, therefore X is inefficient" framing.** `*build-phase` costing more than `*log-miss` is a fact about what those phases *are*. Effort per phase is a **budgeting and capacity** view, not a quality scoreboard — quality lives on `/misses` and `/coverage`.
- **No estimated dollars.** Ever, anywhere, on any tile.
- **No `0` where the answer is "not measured".** This is the single rule that most determines whether the page is trusted.

---

## 4. Ingest changes

`RunRecord` gains three nullable properties, all **nullable by design** — `null` distinguishes *not captured* from a measured zero, and collapsing that distinction is §2.1's defect:

| Property | Type | Source |
|---|---|---|
| `SubagentRuns` | `int?` | `subagent_runs` |
| `TokensOutSubagents` | `int?` | `tokens_out_subagents` |
| `ModelTokensOut` | `Dictionary<string,long>?` | `model_tokens_out` |

**Store the raw rows; never collapse at ingest** — the same rule the miss lens states for `miss-amend` (§0 of `Miss-Telemetry-TfLens.md`), and for the same reason: `RebuildAsync` must be able to re-derive every figure from the stream alone.

**Unknown fields stay `InvalidLines++`-free.** A `runs` record carrying a field TfLens does not know is not invalid — the producer adds fields (§2.5 in August, §2.6 now) and will again. Ignore unknown keys; only a malformed line counts.

---

## 5. Rollout

**No coordination window is needed.** The producer change is additive and backward-compatible:

- Old records simply lack the three fields, and the oracle already reports them as `unobserved_predates_field` rather than as zeros.
- `--phases` is a new mode; `--report` / `--rollup` gained a `phases` key and changed no existing one, so **the current BRD §13 parity gate keeps passing unchanged** and the new keys can be added to it when the page ships.
- Nothing in `runs.jsonl` was rewritten. The streams are append-only and no backfill was performed — a reconstructed `subagent_runs` would be a guess, and §7 already says what backfilled data is worth.

**Fan-out coverage will be thin for weeks.** Only runs recorded after 2026-08-31, under a harness whose window resolves to `tree` scope, carry it. Design the page to look correct at `observed_n = 1 of 13`, because that is what it will show first — and a page that only looks right once the data is dense is a page nobody trusts in the meantime.

---

## 6. Provenance — the same rule, a fourth time

SCHEMA §6 states three separations: live vs backfilled · across `project_type` · across attribution/cost confidence. Phase effort adds a fourth of the same shape:

> **Measured and unobserved never pool.** A run whose window was not `tree` scope has not reported "zero subagents" — it has reported nothing. It sits outside the figure, and the count of such runs is displayed beside it.

That is not a new principle. It is `PERF-UNMEASURED`, `ASSETS-UNMEASURED`, `MOCKUP-UNGRADEABLE`, `⚠ STATIC-ONLY`, `origin_confidence != "linked"`, `cost_attribution != "sole"`, and now `tokens_scope != "tree"` — seven names for one rule:

**An unmeasured thing is unmeasured, not zero, and not passed.**
