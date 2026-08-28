# Miss telemetry — TechieFlow (the framework itself)

**Status:** DESIGN — nothing in this document is implemented yet.
**Audience:** the framework owner + whichever agent implements it.
**Siblings:** `docs/Miss-Telemetry-TfLens.md` (how the numbers get displayed) · `docs/Miss-Telemetry-AI-First-Playbook.md` (the team edition's version of the same idea).
**Reference:** `.tfcore/telemetry/SCHEMA.md` · `.tfcore/tasks/_metrics-emit-gate.md` · `docs/TechieFlow-Telemetry-Guide.md`

---

## 1. The problem, stated as the owner stated it

> "The AI agent in the build phase misses the requirements — not only build, even design phase or any other phase — and there is no way to document those problems, and no way to identify how many tokens or how much money is spent on fixing those misses."

Three distinct asks live inside that sentence, and they need separating before anything is designed:

1. **A miss must become a record.** Today an agent that drops a requirement leaves behind a status-cell demotion and a sentence in a Remarks column. That is prose. It cannot be counted, grouped, or trended.
2. **A miss must carry attribution.** Which phase let it through, which agent was running, on which model, under which harness.
3. **A miss must carry the cost of its repair.** Tokens always; real dollars where a real dollar figure exists and *only* there.

## 2. What the current instrumentation actually captures — and where it stops

TechieFlow is already well instrumented. The honest gap analysis matters more than the design, because three of the owner's four questions are *nearly* answered today and the fourth is not answerable at all.

| Question | Today | Verdict |
|---|---|---|
| Did REQ-X fail? | `gates.jsonl` — one record per REQ per verify run, carrying the **first failing gate** and a closed-vocabulary `failure_class` | **Answered** |
| Did a defect escape every gate? | `gates.jsonl` `gate:"escaped"`, written by `triage-issues.md` §6a | **Answered — but only as a yes/no** |
| Which phase/agent/model *introduced* the problem? | Nothing. `runs.jsonl` records who ran *this* run; no record links a failure back to the run that caused it | **Not answerable** |
| What did fixing it cost? | `runs.jsonl` carries `tokens_*` and `cost_usd` for a whole run window | **Not answerable per miss** — a `fix-issues` run touching five REQs produces one token figure for all five |

Four specific holes:

**2.1 — `gates.jsonl` only fires at verify.** It is written by `verify-phase.md` §6a and `triage-issues.md` §6a. A miss discovered during `*build-phase` — the builder reads the checklist and finds the BRD never specified the export screen — produces **no record of any kind**. The owner named design-phase misses explicitly; they are currently invisible end to end.

**2.2 — `escaped` is one axis, and it is the *detection* axis.** `gate:"escaped"` says "no gate caught it". It does not say what was missed, or which phase should have caught it. SCHEMA.md §3.2 is explicit that `escaped` deliberately shares the `gate` field so one `group by` answers questions 2 and 3 together — that design is right and stays. It simply is not an attribution field.

**2.3 — There is no unit of work called "a miss".** `gates.jsonl` is a *verdict* stream: one record per REQ per verify run, append-only, never revisited. A miss has a lifecycle — found, attributed, fixed, sometimes reopened — spanning several runs. Bolting lifecycle state onto a verdict stream would mean editing records, which SCHEMA.md §6 and constraint 5 of `_metrics-emit-gate.md` forbid outright.

**2.4 — Cost is per-run and cannot be split.** `tf-emit.sh` enriches a run record with the token window between `started` and `ended` (SCHEMA.md §2.5). That window belongs to the *run*. Nothing in the schema apportions it to the individual REQs the run touched, and nothing should silently start doing so.

## 3. The money question, answered honestly before the design

The owner's framing was right, and it should be written down so it stops being re-derived:

| Harness | Tokens | Real dollars | Source |
|---|---|---|---|
| **OpenCode** | yes | **yes — measured** | `opencode.db`, provider's own per-message cost, rolled up across child sessions by the plugin |
| **Claude Code** | yes | **no — `null`, permanently** | Transcript JSONL carries `usage` but no cost. SCHEMA.md §4 forbids computing dollars from a rate card and calling them a measurement |
| **Codex** | yes | **no — `null`** | `codex exec --json` → `turn.completed.usage` via `tf-codex-telemetry.py`. ChatGPT credits are never fabricated as `cost_usd` |

So **"how much money did this miss cost" has a real answer only on OpenCode.** On Claude Code and Codex the answer is a token count.

That is not a dead end, and TfLens already solved it: it carries an operator-editable rate card (`data/prices.json`, `RateCard.cs`) whose every output is labelled *"estimate — tokens × rate card, not measured spend"* and whose every JSON key ends in `_usd_estimate` (ADR-009, BRD-59). **Priced dollars for Claude and Codex misses are a read-time estimate produced by TfLens, never a number stored in a stream.** Nothing in this design changes that, and no miss record ever carries an estimated dollar figure.

## 4. The design — a fifth stream, `docs/metrics/misses.jsonl`

### 4.1 Why a new stream rather than new fields

- A gate record is a verdict at an instant; a miss is an object with a lifecycle. Different cardinality, different unit.
- A miss can exist with **no verify run at all** (design-phase misses), so it cannot live in a stream written only by the verifier.
- SCHEMA.md §11 already sets the precedent that two things on different axes must not share a field name. Same reasoning, one level up: they must not share a stream either.
- Append-only is preserved: the miss is opened by one record and closed by a second. **Nothing is ever edited.**

### 4.2 Two record kinds on one stream

This is the one existing rule the design changes, and it should be changed deliberately rather than drifted into. SCHEMA.md §1 currently says `kind` "must match the stream file". Amend to: **`kind` must be one of the kinds the stream file declares.** `misses.jsonl` declares two:

| `kind` | Meaning | Written by |
|---|---|---|
| `miss` | A miss is opened: what was missed, who missed it, who found it | `verify-phase`, `build-phase`, `triage-issues`, `fix-issues`, `amend-docs`, `*log-miss` |
| `miss-fix` | A miss is closed: the repair run, its outcome, its cost window | `fix-issues`, `build-phase` (FIX mode) |

### 4.3 The `miss` record

```json
{"v":1,"ts":"2026-08-28T11:04:19Z","kind":"miss",
 "app":"AstroLyfe","project_type":"app","harness":"claude-code",
 "miss_id":"MISS-AstroLyfe-20260828-03",
 "req_id":"REQ-UI-014","req_class":"UI",
 "miss_class":"partial-implementation","artifact":"src","severity":"major",
 "origin_phase":"build-phase","origin_agent":"trblazeui",
 "origin_run_id":"2026-08-26T09:12:40Z","origin_confidence":"linked",
 "origin_model":"claude-opus-5","origin_harness":"claude-code",
 "found_by":"gate","found_phase":"verify-phase","found_gate":"render",
 "found_run_id":"2026-08-28T10:41:02Z","failure_class":"blank-data"}
```

**Identity and linkage**

| Field | Type | Notes |
|---|---|---|
| `miss_id` | string | `MISS-<app>-<YYYYMMDD>-<NN>`. **Never invented by an agent** — obtained from `bash .tfcore/utils/tf-emit.sh --next-miss-id`, which counts the day's existing records. The linkage key for `miss-fix`. |
| `req_id` | string \| null | The owning REQ. `null` is legitimate and meaningful: it means *no REQ existed to miss*, which is itself the finding. |
| `req_class` | string \| null | `UI` \| `FN` \| `RAG` \| `NFR`, derived from `req_id`. |

**What was missed** — closed vocabularies only. Privacy constraint 7 of `_metrics-emit-gate.md` forbids a free-text description here as absolutely as it does in `failure_class`.

| Field | Values |
|---|---|
| `miss_class` | `missed-requirement` (in scope, never built) · `partial-implementation` (built, an acceptance bullet unmet) · `wrong-behaviour` (built, behaves other than specified) · `regression` (was `Verified`, now broken) · `unspecified-gap` (**the spec itself omitted it — the design-phase miss**) · `spec-contradiction` · `scope-creep` (built what nobody asked for) · `hallucinated-api` (used a library member that does not exist) · `standards-violation` · `other` |
| `artifact` | Which artifact was deficient: `brd` · `architecture` · `uidesign` · `checklist` · `devguide` · `src` · `tests` · `config` · `other` |
| `severity` | `blocker` · `major` · `minor` — owner-visible impact, **not** an estimate of effort |

`unspecified-gap` + `artifact:"brd"` is exactly the design-phase miss the owner named, and it is the record type that does not exist today.

**Attribution — the point of the whole exercise**

| Field | Type | Notes |
|---|---|---|
| `origin_phase` | string \| null | The `cmd` that should have produced it correctly — same enum as `runs.jsonl.cmd`. |
| `origin_agent` | string \| null | `analyst` \| `architect` \| `flow-master` \| `verifier` \| `trblazeui` \| `techierag` \| `tf-builder` \| `tf-test-writer` \| `general-purpose` \| `general`. |
| `origin_run_id` | string \| null | The `started` timestamp of that run, **found in `runs.jsonl`**, never guessed. |
| `origin_model` | string \| null | **Injected by `tf-emit.sh`** by looking up `origin_run_id` in `runs.jsonl`. |
| `origin_harness` | string \| null | Same lookup, same rule. |
| `origin_confidence` | string | `linked` (`origin_run_id` resolved to a real run record) · `inferred` (attributed by reasoning, no run record found) · `unknown`. |

**`origin_model` and `origin_harness` are never typed by an agent.** This is the same rule that already governs `harness` (SCHEMA.md §1) and for the same reason: an agent copying a literal out of a shared task template would stamp the wrong model on every record and quietly corrupt every per-model comparison built on it. The emitter resolves them or writes `null`.

**`origin_confidence` is a provenance boundary, not a hint.** A report may not pool `inferred` records with `linked` ones in any per-model, per-agent or per-phase figure. This is SCHEMA.md §6's rule applied a third time, and for the sharpest reason yet: *a per-model miss rate computed partly from guesses is exactly the kind of plausible wrong number that discredits every other figure on the page.*

**Detection**

| Field | Values |
|---|---|
| `found_by` | `gate` · `self-smoke` · `owner` (UAT / manual testing) · `production` · `agent-review` (a later agent noticed) · `library-feedback` |
| `found_phase` | The `cmd` running when it surfaced |
| `found_gate` | When `found_by=="gate"`, which gate — mirrors `gates.jsonl.gate`. `null` otherwise |
| `found_run_id` | `started` of the finding run |
| `failure_class` | The existing closed vocabulary (SCHEMA.md §3.3), reused verbatim. `null` where none applies |

`found_by ∈ {owner, production}` is the miss-stream's view of an escape.

### 4.4 The `miss-fix` record

```json
{"v":1,"ts":"2026-08-28T14:52:07Z","kind":"miss-fix",
 "app":"AstroLyfe","project_type":"app","harness":"claude-code",
 "miss_id":"MISS-AstroLyfe-20260828-03","req_id":"REQ-UI-014",
 "fix_run_id":"2026-08-28T13:58:11Z","fix_cmd":"fix-issues","fix_attempt":1,
 "verdict_after":"Verified","reopened":false,
 "cost_attribution":"shared:3",
 "tokens_in":812,"tokens_out":38104,"tokens_cache_read":286110,"tokens_cache_write":0,
 "cost_usd":null,"tokens_scope":"tree","model":"claude-opus-5"}
```

| Field | Type | Notes |
|---|---|---|
| `miss_id` | string | The link. A `miss-fix` whose `miss_id` matches no `miss` record is dropped by the report as an orphan and counted as such. |
| `fix_run_id` | string | `started` of the repair run. **This is where the cost comes from.** |
| `fix_cmd` | string | `fix-issues` \| `build-phase` \| `triage-issues` \| `amend-docs` |
| `fix_attempt` | int | How many `miss-fix` records this `miss_id` already has, plus one. From `tf-emit.sh --next-fix-attempt <miss_id>`. |
| `verdict_after` | string | `Verified` \| `Needs re-verify` \| `FAIL` \| `deferred` \| `wont-fix` |
| `reopened` | bool | `true` when this miss had already been closed and a later escape re-opened it |
| `tokens_*`, `cost_usd`, `tokens_scope`, `model` | — | **Injected by `tf-emit.sh`** from the `fix_run_id` window, by exactly the mechanism SCHEMA.md §2.5 already describes. Never typed by an agent. |
| `cost_attribution` | string | `sole` · `shared:<n>` · `none` — see below |

### 4.5 `cost_attribution` — the field that keeps the money number defensible

This is the field the whole cost story stands on, and without it the design would produce numbers that look measured and are not.

- **`sole`** — the fix run's `reqs_touched` contained exactly this miss's REQ. The whole token window belongs to this miss. This is a measurement.
- **`shared:<n>`** — the run touched *n* REQs. The window covers all of them and **cannot be split by anything the framework can observe**. The report divides equally *and says so*.
- **`none`** — no window could be computed (`tokens_scope:"none"`, no pointer, unreadable store, or the fix happened inside a session with no separate run record). No numbers at all.

**The reporting rule that follows:** a headline "cost per miss" figure may be computed **only over `cost_attribution:"sole"` records**. Apportioned figures appear in a separate, labelled column and are never summed into the headline. This is the same discipline as SCHEMA.md §6 — an equal division across a run that spent 90% of its tokens on one hard REQ and 10% on four easy ones is an *inference*, and inferences do not get to sit in the same column as measurements.

State the limitation rather than hiding it: **a miss fixed inline during a long build session, with no distinct fix run, is unattributable.** It gets `cost_attribution:"none"` and contributes to the count but not to the money. Missing beats invented.

## 5. Where this gets wired

Six front doors, ordered by volume:

| # | Task | Change |
|---|---|---|
| 1 | `verify-phase.md` §6a | Every gate record with `verdict != Verified` also emits a `miss`. `found_by:"gate"`, `found_gate` = the same first-failing gate, `origin_phase`/`origin_run_id` resolved from the most recent `build-phase` run in `runs.jsonl` whose `reqs_touched` contains this REQ. This one change alone makes the majority of misses self-recording with no agent judgement involved. |
| 2 | `triage-issues.md` §6a | Alongside each existing `gate:"escaped"` record, emit a `miss` with `found_by:"owner"` (or `"production"`). **The `escaped` record stays exactly as it is** — escape rate keeps its current definition and its current source. Miss records never feed escape rate; double-counting the same defect in two definitions is precisely the merge SCHEMA.md §6 forbids. |
| 3 | `build-phase.md` | New step: when a builder finds the specification incomplete — no acceptance criteria, a screen with no mockup, a contradiction — emit a `miss` with `miss_class:"unspecified-gap"`, `origin_phase` = the authoring command (`day1-*` / `split-brd` / `mockups`), `found_by:"agent-review"`. **This is the design-phase capture the owner asked for.** |
| 4 | `fix-issues.md` | Emits the `miss-fix` closing record for every `miss_id` it repaired, at its status gate. Where the owner's evidence folder maps to a miss already on the stream, link it; where it does not, open the `miss` first, then close it. |
| 5 | `amend-docs.md` | A doc gap found later is a design miss discovered late: `miss_class:"unspecified-gap"`, `artifact` = the doc amended. |
| 6 | **New flow-master command `*log-miss {App}`** | The manual front door. The owner says "you missed the date filter on the report screen"; the command classifies it, opens the `miss` record, adds the checklist row or demotion, and updates PROJECT-STATUS. This is the direct answer to *"there is no way to document the issues"* — it takes 20 seconds and produces a record instead of a sentence. Task `.tfcore/tasks/log-miss.md`, ANALYZE-ONLY scope like `triage-issues` (never edits `src/`). |

### 5.1 Files that change

| File | Change |
|---|---|
| `.tfcore/telemetry/SCHEMA.md` | New **§5.5 `misses.jsonl`**; amend §1's `kind` rule (§4.2 above); add the miss vocabularies; add the `cost_attribution` reporting rule to §6; add the derived metrics to §8 |
| `.tfcore/utils/tf-emit.sh` | Accept `misses` as a stream (line ~150 `case`); new `--next-miss-id`, `--next-fix-attempt`; extend the enrichment path so `miss-fix` records get the `fix_run_id` token window and `miss` records get `origin_model`/`origin_harness` looked up from `runs.jsonl` |
| `.tfcore/telemetry/install-metrics.sh` | Seed the fifth stream (line ~185, `for s in runs gates sessions commits`) and document it in the generated `docs/metrics/README.md` |
| `.tfcore/telemetry/tf-metrics.sh` | Read the stream; new report block; enforce the `cost_attribution` and `origin_confidence` separations **in code, not prose** — this is how the existing provenance rules are enforced and the new ones must match |
| `.tfcore/tasks/_metrics-emit-gate.md` | Add `misses.jsonl` to the "who writes what" table; add a tenth constraint covering `origin_model` / `origin_confidence` |
| `.tfcore/tasks/{verify-phase,build-phase,triage-issues,fix-issues,amend-docs}.md` | The emit steps above |
| `.tfcore/tasks/log-miss.md` + `.tfcore/agents/flow-master.md` | The new command, its help entry and its deps |
| `.tfcore/tasks/metrics-report.md` + `templates/v4custom/metrics-report-template.md` | The new report section |
| `WORKFLOW.html` §17, `README.md`, `docs/TechieFlow-Telemetry-Guide.md` | Human-facing documentation |
| Harness mirrors | `.claude/commands/TechieFlow/` must stay byte-identical; `opencode.jsonc` registration; `tf-codex-bind.py` regeneration for `.agents/skills/` |

**`.gitattributes` needs no change** — all three scaffold scripts already manage `docs/metrics/*.jsonl text eol=lf merge=union`, which covers the new file by glob.

### 5.2 New derived metrics (computed at report time, never stored)

| Metric | Formula | Segmentation |
|---|---|---|
| Miss rate per phase | `miss` records grouped by `origin_phase` ÷ `runs` of that `cmd` | live-only, per `project_type`, **`origin_confidence:"linked"` only** |
| Miss rate per model | grouped by `origin_model` | as above — this is the routing-decision number |
| Miss class distribution | count of `miss_class` | live-only |
| Design-miss share | `miss_class:"unspecified-gap"` ÷ all misses | the "did we specify badly or build badly" number |
| Escape share of misses | `found_by ∈ {owner, production}` ÷ all misses | reported **beside** the `gates.jsonl` escape rate, never merged with it |
| Tokens per miss fixed | Σ `tokens_out` over `miss-fix` ÷ count | `cost_attribution:"sole"` only; apportioned in a labelled column |
| Measured cost per miss fixed | Σ `cost_usd` | **OpenCode records only.** Never pooled across harness |
| Median time-to-close | `miss-fix.ts − miss.ts` | poolable |

Any figure with fewer than 3 supporting records prints as `insufficient data (n=…)`, per SCHEMA.md §8.

## 6. What this design deliberately does not do

- **It does not change `gates.jsonl`.** Not one field, not one writer. Every existing figure keeps its exact definition and its exact source. A new stream that silently altered the meaning of the first-pass rate would cost more trust than it bought.
- **It does not estimate dollars.** No miss record ever carries a rate-card figure. Pricing is TfLens's read-time job and is labelled there.
- **It does not apportion cost silently.** `cost_attribution` makes every division visible.
- **It does not let an agent name a model.** Attribution is looked up or it is `null`.
- **It does not add per-feature cycle time.** SCHEMA.md §0 rules that out permanently and this design does not reopen it. Time-to-close a *miss* is a different unit and is fine.
- **It does not gain a veto.** Every writer still exits 0. A miss that fails to record is a lost record, never a blocked phase.

## 7. Rollout order

1. **TechieFlow first** — schema, emitter, tasks, report. Streams begin filling.
2. **TfLens second.** TfLens ignores files it does not know about, so an un-updated TfLens sees a repo emitting `misses.jsonl` and simply does not read it. No crash, no wrong number, no coordination window. See `docs/Miss-Telemetry-TfLens.md`.
3. **AI-First-Playbook third**, on its own schedule — it is OpenCode-only and its stream shape differs. See `docs/Miss-Telemetry-AI-First-Playbook.md`.

Deploy to app repos with `update-framework.sh <repo>` per app per machine, as usual.

## 8. Decisions taken (owner-approved 2026-08-28)

These were raised as open questions and settled before implementation. They are decisions now, not options.

**8.1 — Verify auto-emits, and re-verify failures COLLAPSE onto the open miss.** Every failing gate becoming a miss record is the highest-signal, zero-effort source, so it is on by default. But a REQ that fails three times must not produce three misses: that would triple-count one defect in every distribution and make the miss count a measure of *retry patience* rather than of quality.

**The collapse rule, stated so it can be implemented mechanically:**

> Before emitting a `miss` for a failing REQ, look for an **open** miss on the same `req_id` in the same `app` — one with no `miss-fix` record, or whose latest `miss-fix` carries `verdict_after` other than `Verified`. If one exists **and** the `miss_class` you would record matches its `miss_class`, emit **nothing**: it is the same miss, still open. If the `miss_class` differs, emit a new `miss` — the REQ is failing for a genuinely different reason, and that is new information.

`bash .tfcore/utils/tf-emit.sh --open-miss <REQ-ID>` prints the open `miss_id` and its `miss_class`, or nothing. The check is the emitter's job, not the agent's judgement.

**8.2 — Both `origin_agent` and `origin_model` are kept.** They answer different questions. `origin_model` feeds routing; `origin_agent` tells you which persona's instructions to tighten, which for a solo owner is usually the more actionable of the two. Both are cheap and neither reconstructs the other.

**8.3 — `*log-miss` is its own command, not a mode of `*triage-issues`.** `triage-issues` boots the app and reproduces every issue before it writes anything — correct for UAT triage, and fatal for casual logging. The friction of "must boot the app" is exactly what stops a miss from being recorded at all, and an unrecorded miss is the problem this whole design exists to solve. `*log-miss` writes a record and a checklist line; it never boots, never reproduces, never touches `src/`.
