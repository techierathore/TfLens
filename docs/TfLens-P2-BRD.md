# TfLens — Business Requirements — Phase 2: Reports

| | |
|---|---|
| App | TfLens |
| Kind | app |
| Size | Large |
| Phase | 2 of 3 |
| Status | Target |
| Stack answer set | Blazor Server · PostgreSQL 16 · Dapper · TrBlazeUI · Docker |
| Date | 2026-09-08 |

**Phase 2 of 3 — engine, the four report pages, export, parity.** Requirement ids: **BRD-21 to BRD-21, BRD-30 to BRD-72, BRD-143 to BRD-143**. Ids run on across the three phases and are never reused or renumbered.

Other phases: [Phase 1 — Foundation](./TfLens-BRD.md) · [Phase 3 — Depth](./TfLens-P3-BRD.md) · The map is [TfLens-Phases.md](./TfLens-Phases.md).

**The whole-project context lives in phase 1 and is not repeated here:** the executive summary, business objectives, scope and out-of-scope list, stakeholders, the context and component diagrams, the constraints, the parity procedure (§13), the success metrics and the risks. This document holds what belongs to this phase: its screens and its requirements.

## 1. Summary

TfLens reads the telemetry the frameworks already emit and turns it into figures. Phase 1 got the
records into the store; **this phase is where they become numbers, and therefore where a wrong number
first becomes possible.** The metrics engine, the four report pages built on it — Coverage, Gate
outcomes, Harness comparison, Routing & economics — the weekly snapshot export, and the parity check
that is the licence to quote any of it.

The product's stated dangerous failure mode is not a crash but a **plausible wrong number** that gets
exported and cannot be defended. Every provenance rule in SCHEMA.md §6 is enforced here, in the shape
of the result types rather than as a check someone remembers to run, and the parity diff against
`tf-metrics.sh` is the acceptance gate.

## 2. Screens and flow

| Screen | Route | Role | What it answers | Fields | Feature | Requirements | Mockup |
|---|---|---|---|---|---|---|---|
| Coverage / health | `/` | Owner | **Is the telemetry itself trustworthy right now?** Per repo: whether a clone has stopped pushing, whether hooks are missing, which streams carry records and how fresh they are. It is the **landing page on purpose** — *every other number on this site is suspect until this page is green* — and it is where amendment, orphan and provenance diagnostics surface instead of being silently applied. **GREEN** means every repo synced, nothing stale and no errors. The stale-clone warning names its fix — run `update-framework.sh` on that clone — because the hook lives in `.git/`, which never clones; the 7-day threshold is configurable. Beside the unknown-fields list it shows how many `duration_s` and `attempt` values the reader derived rather than read, because a derived figure is legitimate and must still be visible as derived. Sync now and Rebuild refresh the page in place. | Summary badge; per-repo cards or grid (kind, last sync and outcome, short SHA linked to GitHub); five-row stream staleness table; unknown fields **and unrecognised values in an open vocabulary — shown, never filtered away**; escapes-missing-why + reclassification-split + orphan facts; **`sort`-missing and `what`-missing counts against eligible misses**; **derived-`duration_s` and derived-`attempt` counts**; Rebuild button; Framework switch | F-COVER, F-RAW | [BRD‑21](#brd-21) · [BRD‑39](#brd-39) · [BRD‑40](#brd-40) · [BRD‑41](#brd-41) · [BRD‑42](#brd-42) · [BRD‑43](#brd-43) · [BRD‑44](#brd-44) · [BRD‑127](#brd-127) · [BRD‑181](#brd-181) | [coverage.html](./mockups/coverage.html) · Playbook state: [playbook.html](./mockups/playbook.html) |
| Gate outcomes | `/gate-outcomes` | Owner, Author | **The headline page — and the name is a direct reference, not a slogan.** It renders *the three questions the telemetry schema exists to answer* (`.tfcore/telemetry/SCHEMA.md` §0), per `project_type`: **(1) first-pass rate** — what fraction of REQs reach `Verified` on attempt 1; **(2) gate catch distribution** — of all failures, which gate caught them; **(3) escape rate** — what fraction of defects reached UAT or production instead of being caught by a gate. Live and backfilled figures sit side by side and are **never summed**; there is no “all types” tab and no total row, by design. See [the phase-1 note](./TfLens-BRD.md#the-gate-outcomes). Renamed from “Three questions” (`/three-questions`) on 2026-09-01 by owner ruling; the feature keeps the id F-3Q. It is the evidence base for B3 (phase 1 BRD §2). Under each type it states the records, the REQs scored and the REQs excluded by backfill taint, with the excluded ids in a collapsible panel. Every figure on this page, and on every report page, comes from one metrics engine, a field-for-field port of `analyse()` in `tf-metrics.sh`: it reads each repo's streams, dedupes commits per repo, and returns one result with the same key layout as `--rollup --json`, kept in memory until the next sync or rebuild. That result holds a live and a backfilled block per project type and no total, so a pooled figure has nowhere to go; a REQ excluded by taint restarts its live `attempt` at 1. | One section (or tab) per project_type; live column + labelled backfilled column; taint list; late-gate coverage; Framework switch | F-3Q, F-ENGINE | [BRD‑45](#brd-45) · [BRD‑46](#brd-46) · [BRD‑47](#brd-47) · [BRD‑48](#brd-48) · [BRD‑49](#brd-49) · [BRD‑50](#brd-50) | [gate-outcomes.html](./mockups/gate-outcomes.html) · Playbook state: [gate-outcomes-playbook.html](./mockups/gate-outcomes-playbook.html) |
| Harness comparison | `/harness` | Owner, Author | **Does the framework behave the same whichever tool runs it?** One column per harness (`claude-code` · `opencode` · `codex`): run volumes by command, verdict mix, session counts, token totals. Tokens may be compared across harnesses; **dollars may not** — only OpenCode reports measured cost, and the page never shows a dollar total across harnesses. Per column the token totals are split into input, output, cache read and cache write, taken from both the runs' token fields and `sessions`. Codex, like Claude Code, reads “not measured (null by design)”. Below the columns, one bar chart of tokens by harness. | Columns claude-code · opencode · codex; “not detected” footnote; tokens chart; OpenCode-only cost card; Framework switch | F-HARN | [BRD‑51](#brd-51) · [BRD‑52](#brd-52) · [BRD‑53](#brd-53) · [BRD‑54](#brd-54) · [BRD‑55](#brd-55) | [harness.html](./mockups/harness.html) · Playbook state: [harness-playbook.html](./mockups/harness-playbook.html) |
| Routing & economics | `/routing` | Owner, Author | **Carries the Edit-prices dialog. Did runs land on the model they were routed to, and what would the mix have cost?** Routing drift (declared `tier`/`tier_model` vs the model actually observed), tokens by model, and the counterfactual repricing — everything repriced as if every run had used the most expensive model observed — always labelled **estimate — tokens × rate card, not measured spend**. The poolable metrics follow SCHEMA.md §8 and the reference script's rounding: whole percentages, REQ throughput to 2 decimal places, tokens per Verified REQ to 1. | Drift table; tokens-by-model chart; repricing cards (estimate); poolable metrics; prices editor dialog; Framework switch | F-ROUTE | [BRD‑56](#brd-56) · [BRD‑57](#brd-57) · [BRD‑58](#brd-58) · [BRD‑59](#brd-59) · [BRD‑60](#brd-60) · [BRD‑61](#brd-61) · [BRD‑62](#brd-62) | [routing.html](./mockups/routing.html) · Playbook state: [routing-playbook.html](./mockups/routing-playbook.html) |
| Snapshot export | `/export` | Author, Parity operator | **Turn the figures into something quotable.** Writes a dated markdown + JSON snapshot and marks it **QUOTABLE** only while parity against `tf-metrics.sh` still holds and no row carries provenance nobody obtained — because the dangerous failure of this product is not a crash, it is a plausible wrong number that gets published and cannot be defended. `snapshot.md` is sectioned exactly like the pages. In `tflens.json` the `extras` object carries harness, routing and repricing, and the `parity` object the last recorded parity run. The parity comparison works key by key, not as a text diff, because key order and formatting may differ between the two files. | Export button; list of past snapshots; quotable banner; parity status; Framework switch (one snapshot per framework) | F-EXPORT, F-PARITY | [BRD‑63](#brd-63) · [BRD‑64](#brd-64) · [BRD‑65](#brd-65) · [BRD‑66](#brd-66) · [BRD‑67](#brd-67) · [BRD‑70](#brd-70) | [export.html](./mockups/export.html) · Playbook state: [export-playbook.html](./mockups/export-playbook.html) |

## 3. Requirements

- <a id="brd-21"></a>**BRD-21** — Owner can trigger the same rebuild from the Coverage page behind a confirmation dialog. *(F-RAW)*
- <a id="brd-30"></a>**BRD-30** — System shall compute every figure at request time from the stream tables and never write a derived value into a stream table. *(F-ENGINE)*
- <a id="brd-31"></a>**BRD-31** — System shall never pool live and backfilled records for first-pass rate, gate catch distribution or escape rate; backfilled figures appear only in an adjacent labelled column, with no total row and no disabling flag. *(F-ENGINE)*
- <a id="brd-32"></a>**BRD-32** — System shall never pool first-pass rate, gate catch distribution or escape rate across `project_type`, and shall report `project_type_inferred` records as **unclassified**. *(F-ENGINE)*
- <a id="brd-33"></a>**BRD-33** — System shall exclude any REQ with at least one backfilled record from the live first-pass rate and expose the excluded REQ ID list. *(F-ENGINE)*
- <a id="brd-34"></a>**BRD-34** — System shall render any metric with fewer than 3 supporting records as `insufficient data (n=…)`, never as a number, via a `Figure` type that cannot carry a value in that case. *(F-ENGINE)*
- <a id="brd-35"></a>**BRD-35** — System shall never pool `cost_usd` across harness; `Pooled.CostUsd` is always null. *(F-ENGINE)*
- <a id="brd-36"></a>**BRD-36** — System shall report late-added gates (`perf`, since 2026-08-10) as `ran` (records whose `gates_run` contains the gate) beside `caught`, and never present their share of the raw distribution as a catch rate. *(F-ENGINE)*
- <a id="brd-37"></a>**BRD-37** — System shall compute the poolable metrics (rework ratio, batch size median, REQ throughput median in REQs/hour, tokens total, tokens per Verified REQ, commit cadence, duplicates collapsed) with the reference's formulas and rounding. *(F-ENGINE)*
- <a id="brd-38"></a>**BRD-38** — System shall include a unit test that asserts the engine's output on checked-in fixture streams equals a checked-in `reference.json` produced by `tf-metrics.sh` on the same fixtures. *(F-ENGINE)*
- <a id="brd-39"></a>**BRD-39** — User can see, per connected repo of their own, last sync time and outcome, last commit SHA, record counts per stream and live-vs-backfilled gate counts on the Coverage page at `/`. *(F-COVER)*
- <a id="brd-40"></a>**BRD-40** — Owner can see days since the newest record per stream per repo. *(F-COVER)*
- <a id="brd-41"></a>**BRD-41** — System shall flag on screen, in words, any repo whose newest `sessions` or `commits` record is older than the staleness threshold (default 7 days), stating that the clone is not pushing or lacks hooks. *(F-COVER)*
- <a id="brd-42"></a>**BRD-42** — Owner can see per repo the field names observed that SCHEMA.md does not document, and any records with `v > 1`. *(F-COVER)*
- <a id="brd-43"></a>**BRD-43** — System shall show a single GREEN / CHECK summary badge with the warning count at the top of the Coverage page. *(F-COVER)*
- <a id="brd-44"></a>**BRD-44** — System shall show the Coverage page as the landing page after login for a user with at least one connected repo (`/repos` otherwise). *(F-COVER — amended 2026-08-26)*
- <a id="brd-45"></a>**BRD-45** — Owner can see, per `project_type` (including `unclassified`), the live first-pass rate, gate catch distribution and escape rate at `/gate-outcomes`. *(F-3Q)*
- <a id="brd-46"></a>**BRD-46** — System shall show backfilled figures for the same `project_type` in an adjacent column labelled backfilled, never summed with live. *(F-3Q)*
- <a id="brd-47"></a>**BRD-47** — System shall present `escaped` as its own row in the gate catch distribution and `unattributed` for failures without a gate, in the reference's gate order. *(F-3Q)*
- <a id="brd-48"></a>**BRD-48** — Owner can see the full list of REQ IDs excluded by backfill taint. *(F-3Q)*
- <a id="brd-49"></a>**BRD-49** — Owner can see the late-gate coverage line per gate (`ran`, `caught`, rate or insufficient data, or "not yet run on this data (gate added …)"). *(F-3Q)*
- <a id="brd-50"></a>**BRD-50** — System shall show no "all types" view and no total row on the gate-outcomes page, and shall display the SCHEMA.md §6 note explaining why. *(F-3Q)*
- <a id="brd-51"></a>**BRD-51** — User can see per harness — columns **`claude-code`, `opencode`, `codex`** — run counts by command, gate verdict mix, session counts, and token totals at `/harness`. **Amended 2026-09-08:** `codex` is **retired at the producer** — the Codex adapter was removed from the framework on 2026-09-07 and nothing writes that value any more. Existing `codex` records stay valid and shall keep rendering with their own column; the value shall **not** be offered in any new filter, picker or legend built after this date. A retired harness is a column with no new rows, never a reason to drop the rows it has. *(F-HARN — amended 2026-08-26, 2026-09-08)*
- <a id="brd-52"></a>**BRD-52** — Owner can see tokens per verified REQ per harness. *(F-HARN)*
- <a id="brd-53"></a>**BRD-53** — System shall show real `cost_usd` for `opencode` only, labelled as the only measured dollars in the system, and "not measured (null by design)" for Claude Code. *(F-HARN)*
- <a id="brd-54"></a>**BRD-54** — System shall never show a dollar total across harnesses. *(F-HARN)*
- <a id="brd-55"></a>**BRD-55** — System shall never merge `harness: null` records into a named harness and shall disclose them in a footnote row ("*n* records with harness not detected — excluded from the columns above") rather than a column. *(F-HARN — amended 2026-08-26)*
- <a id="brd-56"></a>**BRD-56** — Owner can see routing drift at `/routing`: `routed:false` run count and list, and declared `tier`/`tier_model` versus observed `model`/`models`, by command. *(F-ROUTE)*
- <a id="brd-57"></a>**BRD-57** — Owner can see tokens by observed model (input, output, cache read, cache write). *(F-ROUTE)*
- <a id="brd-58"></a>**BRD-58** — Owner can see the counterfactual repricing figure: all tokens repriced at the most expensive observed model versus the actual mix, from `data/prices.json`. *(F-ROUTE)*
- <a id="brd-59"></a>**BRD-59** — System shall label the repricing figure **estimate — tokens × rate card, not measured spend** everywhere it appears, including the export. *(F-ROUTE)*
- <a id="brd-60"></a>**BRD-60** — System shall exclude runs with `tokens_scope: none` (or no token fields) from repricing and state how many were excluded. *(F-ROUTE)*
- <a id="brd-61"></a>**BRD-61** — Owner can edit `prices.json` (per model: input/output/cache-read/cache-write USD per million tokens) through a validated dialog; the file remains the source of truth. *(F-ROUTE)*
- <a id="brd-62"></a>**BRD-62** — Owner can see the poolable metrics (rework ratio, REQ throughput, batch size, commit cadence) on the routing page. *(F-ROUTE)*
- <a id="brd-63"></a>**BRD-63** — User can press Export on `/export` to write `data/reports/<userId>/<date>/snapshot.md` and `tflens.json` for their own repos. *(F-EXPORT — amended 2026-08-26)*
- <a id="brd-64"></a>**BRD-64** — Ops can run the `export` verb (`dotnet TfLens.dll export [--date]`) to produce the same files headlessly. *(F-EXPORT)*
- <a id="brd-65"></a>**BRD-65** — System shall lay out `tflens.json` with the same keys as `tf-metrics.sh --rollup --json` (`per_repo`, `tainted_reqs`, `live`, `backfilled`, `pooled`) plus `extras` and `parity` objects. *(F-EXPORT)*
- <a id="brd-66"></a>**BRD-66** — System shall never mix provenances in one figure in the snapshot and shall label every estimate in both files. *(F-EXPORT)*
- <a id="brd-67"></a>**BRD-67** — Owner can see past snapshots with download links and a quotable / not-quotable banner based on whether the last parity run postdates the last parser change. *(F-EXPORT)*
- <a id="brd-68"></a>**BRD-68** — System shall stamp a parser version into the build and into every export. *(F-PARITY)*
- <a id="brd-69"></a>**BRD-69** — Parity operator can run `tools/parity-compare.py reference.json tflens.json` and get a key-by-key diff (record counts per stream and backfilled counts, duplicates collapsed, tainted-REQ set, per-type live and backfilled figures, late-gate coverage, every poolable, every insufficient-data marker with its n) with non-zero exit on any mismatch. *(F-PARITY)*
- <a id="brd-70"></a>**BRD-70** — Parity operator can read the dataset identity for the last sync from the export and the Coverage page to pin the reference dataset: the **commit SHA** for a fetched source, the **bundle sha256** for an imported one. *(F-PARITY — amended 2026-08-28, see BRD-134)*
- <a id="brd-71"></a>**BRD-71** — System shall record each passing parity run in `data/parity-last.json` (date, dataset SHAs, script hash, parser version, compare output) and the operator records it in DECISIONS.md. *(F-PARITY)*
- <a id="brd-72"></a>**BRD-72** — Parity operator shall spot-check the metrics without a reference (harness, routing, repricing) by hand against raw JSONL once and record it in DECISIONS.md. *(F-PARITY)*

  **Why this is a requirement and not a chore.** TfLens holds no user table — identity is a live external service (BRD-90), so the authenticated half of the suite rests on mutable state that nothing in the repository could previously rebuild. On 2026-08-28 that failed exactly as the shape predicts: the accounts were removed on the AppManager side and **seven tests plus every authenticated screen became un-verifiable at once**, with no path back that did not involve a human remembering what the passwords had been. A dependency that can silently invalidate the whole verification surface, and that only a person's memory can restore, is a product defect — recorded as `MISS-TfLens-20260828-02` (`unspecified-gap` / `tests` / `blocker`) and built as `REQ-NFR-012`.

### Amendment 2026-08-29 — two gates the product was relying on and had never written down

Both clauses below were already being *enforced by hand* — the first by the parity operator noticing that a count disagreed with upstream, the second by the owner opening the mockups beside the running app. Neither was a requirement, so neither had a gate, and on 2026-08-29 both failed in the same session against a checklist that read 145 `Verified`. They are appended here so the checklist rows built for them (`REQ-NFR-019`, `REQ-NFR-020`) are **owned by the BRD rather than inferred from a finding**.
- <a id="brd-143"></a>**BRD-143** — Stored provenance shall be real: every row in every stream table shall carry a `source_sha` that a sync or an import actually recorded, and no path shall write a row with provenance nobody obtained. A seeding or fixture harness shall not write into the application's own store, or shall write only under a user id the application's own queries exclude, so demo data can never reach a published figure. The system shall ship a check that reports any `source_sha` present in the store which no `SyncState` row or import bundle accounts for — detectable without a network call and without hand-comparing counts against GitHub — and `/export` shall refuse to mark a snapshot **QUOTABLE** while such a row is present, for the same reason it refuses when the reference script has changed (BRD-67, §13). *(F-PARITY, F-PARSE, F-RAW, F-EXPORT)*


**Cross-cutting non-functional requirements that this phase owns** (moved here 2026-09-08 with the phase split, because the checklist rows that own them are this phase's):
- <a id="brd-82"></a>**BRD-82** — Performance: report pages render from the memoised analysis within a second for the expected data volume (tens of thousands of records across ≤10 repos); a full sync of 5 repos completes in under 30 s on a normal connection. Targets:
- <a id="brd-84"></a>**BRD-84** — Privacy: TfLens displays and stores only what the streams carry (IDs, counts, durations, verdicts, short SHAs); no requirement text, no commit subjects, nothing from `src/`; the `Overflow` column is never rendered, only its field names.
- <a id="brd-89"></a>**BRD-89** — Integrity: the provenance rules (BRD-31..36) have no configuration switch, no query parameter and no UI toggle that relaxes them.

## 4. Development status

Written by the status gate after every build, verify and handoff; not by hand.

**Snapshot as of 2026-09-11.** Live per-requirement status: `PROJECT-STATUS.md` and the Requirements Status table in `docs/TfLens-P2-Checklist.md`.

| Screen | Requirements | Verified | Open | Status |
|---|---|---|---|---|
| UI / Pages | 20 | 17 | 3 | Partial |
| Functional requirements | 20 | 19 | 1 | Partial |
| Non-functional | 4 | 3 | 1 | Partial |

## 5. Where the rest lives

| What | Where |
|---|---|
| Executive summary, objectives, scope, stakeholders, diagrams | [phase 1 BRD](./TfLens-BRD.md) §1–§8 |
| Constraints and assumptions | [phase 1 BRD](./TfLens-BRD.md) §12 |
| Parity check — the mandatory acceptance test | [phase 1 BRD](./TfLens-BRD.md) §13 |
| Definition of done, success metrics, risks, glossary | [phase 1 BRD](./TfLens-BRD.md) §14–§17 |
| Architecture, decisions log, data model | [TfLens-Architecture.md](./TfLens-Architecture.md) (not phased) |
| This phase's work list | [TfLens-P2-Checklist.md](./TfLens-P2-Checklist.md) |
| This phase's screens, control by control | [TfLens-P2-UIDesign.md](./TfLens-P2-UIDesign.md) |
