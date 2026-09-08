# TfLens — what to add so the framework's problems show up

| | |
|---|---|
| Purpose | Hand this to whoever works on TfLens. It says exactly what changed in the telemetry between 2026-08-31 and 2026-09-07, what TfLens must add to show it, and the three reader-side rules that change numbers TfLens already publishes. |
| Audience | The TfLens team. No knowledge of TechieFlow's internals is assumed. |
| Source of truth | `.tfcore/telemetry/SCHEMA.md` in any repository carrying the framework. Where this document and the schema disagree, the schema wins and this document is wrong. |
| Written | 2026-09-07, at the close of the TechieFlow reset. |

---

## 1. Why this exists

TfLens reads the framework's telemetry and turns it into pages. Between 2026-08-31 and 2026-09-07 the framework was rewritten, and the streams gained fields that answer a question the dashboard cannot answer today: **not "what broke" but "whose fault was it, and would a check have caught it".** Everything below is already being written into `docs/metrics/*.jsonl` in every project. None of it needs a framework change; it needs a reader.

One number is also wrong today and will stay wrong until a reader-side rule changes. That is §4, and it matters more than any new page.

---

## 2. The one new field that changes what the dashboard is for

### `sort` on a miss record — whose gap was it

Every miss now carries the answer to four questions asked in order, stored in one field:

| `sort` | Means | The fixed response |
|---|---|---|
| `spec` | the project's own specification did not say it | fix the checklist line; the framework is untouched |
| `unsaid` | the framework never said it anywhere | add one requirement line, plus a check |
| `weak-check` | there was a check and it did not catch it | fix the check, not the prose |
| `ignored` | it was written down and ignored anyway | make it a hook or a script, or delete the rule |

**Why it is the important one.** Across the framework's own 126 misses, roughly half sorted `weak-check`, a quarter `unsaid` and a quarter `ignored`. That distribution says the dominant failure is not "nobody wrote the rule" but "the check was too weak to catch it" — which is a completely different remedy from the one a class-only chart implies. A dashboard that shows `miss_class` but not `sort` shows what broke and hides what to do about it.

**What to build.** A distribution of `sort` beside the existing class distribution, per project and combined, and a filter. On a project page, the sentence for each bucket in plain words rather than the raw value, because the raw values mean nothing to a reader who has not read the schema.

**Honesty rule.** The field arrived on 2026-09-07 and older records do not carry it. Report the count that carries it as the denominator — "33 of 65 sorted" — never as a percentage of all misses. A miss written before the field existed is not "unsorted by choice", and the two must not be pooled.

---

## 3. Four more things now in the streams

### 3.1 `what` — the sentence a human can read

Every miss carries a one-sentence description in the owner's words. The framework also rebuilds `docs/<App>-Misses.md` and its HTML from the stream after every write, so a readable list already exists in each project. TfLens should show the sentence on the miss row: a miss identified only by class and phase cannot be recognised a month later.

**Never store or re-publish requirement text** from a checklist. The `what` sentence is written for this purpose and is the only prose that travels.

### 3.2 `kind: "review"` — what an owner review cost

A new record kind on the misses stream (schema §5.5.9). One per phase that ends in an owner review, carrying the phase (`day1-review`, `build-review`, `verify-review`, `handoff-review`), how many corrections came back, and the cost of producing the reviewed output and of applying the corrections — both **copied by the emitter** from the two runs the record names, never typed.

This is the only record that prices a specification defect. A dashboard that shows build and verify costs but not review costs tells the reader that documents are free.

### 3.3 `kind: "miss-amend"` — a field completed later

Also on the misses stream. It fills a field that was left `null` and **never overwrites** one that is not. A reader that ignores amendments sees nothing false, but it sees less: the framework's own stream has 53 of them, mostly completing `sort` on older records. Fold them in when computing a distribution, and say how many were folded.

### 3.4 `req_class: "FR"` on a gate record

The framework now grades itself against its own 63 requirement lines and writes one gate record per line, with `req_class: "FR"`. These are **not** application requirements and must never pool with `UI`, `FN`, `RAG` or `NFR`: a framework rule and a screen are different units. Segment them, or exclude them, but do not average them together.

---

## 4. Three reader-side rules that change numbers TfLens already shows

These are not new fields. They are arithmetic the reader must do, and TfLens is currently doing two of them differently. Each was a real defect in the framework's own report before it was fixed there.

### 4.1 Key a requirement by project **and** id

Every project has a `REQ-UI-001`. A rollup keyed on `req_id` alone counts TfLens's and TechieBlog's as one requirement. The framework's own combined first-pass rate read **72%** under that bug and **48%** once keyed on `(project, req_id)`. If TfLens publishes any cross-project figure keyed on the id alone, that figure is wrong by roughly the same margin.

### 4.2 Derive a run's duration when the record omits it

`duration_s` was added after some records were written. Those records carry `started` and `ended` but no duration, and a reader that sums `duration_s` counts them as **zero time while still counting their tokens** — so a phase's time is a sum over some of its runs and its tokens a sum over others. The framework's reset reported **16h49m** under that bug against a true **55h57m**. Derive it from `started` and `ended`; where `ended` is absent too, the schema defines it as the moment the record was written, which is `ts`. Print how many were derived.

### 4.3 Derive `attempt` when the record omits it

First-pass rate counts records with `attempt == 1`. `attempt` is *defined* as 1 plus the number of prior live records for the same requirement in the same project — a count over the stream, not a judgement. A record without it drops out of the rate entirely: the framework's own 12 requirement verdicts, all passing, reported a first-pass rate of **0%**. Derive it in stream order for records that lack it, and never touch one that carries it.

---

## 5. Two vocabulary changes

- **`harness: "codex"` is retired.** The Codex adapter was removed on 2026-09-07. Nothing writes that value any more; records that carry it stay valid and must still render. Do not drop them, and do not add Codex to any new filter.
- **The `cmd` vocabulary grew**: `metrics-report`, `generate-html`, `render-workflow-docs`, `triage-and-fix`, and `framework-reset` for framework maintenance. A dashboard that whitelists command names will silently hide 27 records that already exist across the estate. Prefer showing an unknown value to dropping it.

---

## 6. What must not be done

These are the framework's own standing rules for any report built on these streams, and they apply to TfLens.

- **Provenance never merges.** Live records never pool with backfilled ones; `app`, `library`, `docs` and `framework` project types never pool with each other; a miss whose attribution is inferred never enters a per-model or per-phase rate. Data on the wrong side of a boundary may sit in an adjacent labelled column, never summed with the figure beside it.
- **Every exclusion is printed with the figure it bounds.** An exclusion the reader cannot see is indistinguishable from a bug.
- **Fewer than three supporting records is `insufficient data`**, not a number.
- **No estimated dollar figure is ever presented as a measurement.** Real measured dollars exist only on OpenCode records; Claude Code carries `cost_usd: null` permanently, so report tokens and say why. **No stream ever stores a priced figure** — nothing in the framework multiplies tokens by a price list on the way in. A **read-time** estimate is a different thing and is allowed, on the terms TfLens already meets: labelled an estimate everywhere it appears, on its own card, its JSON key ending `_usd_estimate`, and never added to a measured cost. That is TfLens's own path (BRD-62, BRD-123, BRD-128, BRD-160) and the framework's own design record blesses it in as many words (`docs/Miss-Telemetry-TechieFlow.md` §"What a miss costs"). **Correction, 2026-09-08:** this bullet previously read *"No rate card, ever"*, which turned a producer rule into a consumer ban and contradicted both the schema and the sentence just cited. TfLens's repricing panel and price-list editor stay exactly as they are; no requirement is struck.
- **A per-model rate is observational, not causal.** Which model gets the hard work is not random, and the page must say so once.

---

## 7. How the team proves each item

Nothing here is done until it is shown against real data. Every project under `/mnt/c/1MyCode` and `/mnt/c/3AIGenCode` carries live streams; TfLens itself has 161 miss records and 809 gate records.

| Item | The proof |
|---|---|
| `sort` distribution | The three buckets appear with their denominator, and the count of records predating the field is stated separately. |
| `what` sentence | A miss row shows the sentence, and no requirement text appears anywhere on the page. |
| `review` records | A phase with a review shows corrections given, cost to produce and cost to correct, all copied not computed. |
| `miss-amend` | A distribution states how many fields were completed by an amendment. |
| `req_class: FR` | Framework requirement verdicts appear in their own segment and in no combined application figure. |
| Rollup keying | The combined first-pass rate is recomputed; if it moves by roughly 20 points, the old figure was keyed on the id alone. |
| Duration derivation | A phase's total time and its token total cover the same set of runs, and the page says how many durations were derived. |
| `attempt` derivation | A set of records that all passed reports a first-pass rate of 100%, not 0%. |

---

## 8. If anything here is wrong

Say so, with the record that disproves it. The framework's own report was wrong about three of these until someone checked, and each correction came from reading the records rather than the documentation. `.tfcore/telemetry/SCHEMA.md` is the contract; this page is a summary of it written on 2026-09-07 and will age.
