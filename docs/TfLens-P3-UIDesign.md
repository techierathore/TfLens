# TfLens — UI Design — Phase 3: Depth

| | |
|---|---|
| App | TfLens |
| Kind | app |
| Size | Large |
| Phase | 3 of 3 |
| Date | 2026-09-08 |

This file holds the 4 screens of phase 3; what every phase shares is listed under *Where the rest lives* at the end.

## Screens

### Screen: Misses & rework (`/misses`)

**Mockup:** [docs/mockups/misses.html](./mockups/misses.html) · Playbook state: [docs/mockups/misses-playbook.html](./mockups/misses-playbook.html) · **Role(s):** User, Owner, Author · **BRD:** BRD-118..BRD-126, BRD-171..BRD-176 (feature F-MISS, BRD-112..BRD-130) · **REQ:** REQ-UI-035..REQ-UI-038 · **Phase 3** *(added 2026-08-28; amended 2026-09-08 — the `sort` band, the `what` sentence on each row, and the review-cost band)*

**Layout (one line):** shell; page header with a period `Select` defaulting to **All history**; a standing note separating this page's *escape share* from Gate outcomes' *escape rate*; then **six** bands in the order that answers the owner's question — KPI row (two rows of `StatTile`, plus a visually distinct estimate tile on its own dashed row) · **Where misses come from** (origin phase × miss class, with the excluded-attribution count in the card footer, beside the failed-practice distribution carrying `n of N assessed` on its face) · **Whose gap was it** (the `sort` distribution in words, full width, with its `n of N sorted` badge and its predates-the-field footer — added 2026-09-08) · **Who was running** (a standing observational `Alert Variant=Warning`, then origin model and origin agent side by side) · **Cost of rework** (three cards: measured · apportioned · unattributable) · **What reviews cost** (one row per review phase, every figure copied from the record — added 2026-09-08) — closing with a per-miss `DataTable` carrying each record's own one-sentence description, whose raw record sits behind a `Collapsible`.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Page header + period filter | `TypographyH2` "Misses & rework" + `TypographyMuted` + `Select` (`SelectTrigger`/`SelectItem`: All history · 7 · 30 · 90 days) `data-testid="misses-period"` | the selected window | **defaults to All history** (BRD-125) — a default period would routinely render `insufficient data (n=…)` on a page whose job is to show a trend |
| Escape-vs-escape note | `Alert Variant=Info` | "This page's **escape share** is `found_by ∈ {owner, production}` ÷ all misses, from `misses.jsonl`. The **escape rate** on Gate outcomes keeps its own definition and its own source (`gates.jsonl`). They are adjacent, never merged." | always — two definitions of one word on one page is how a report loses its reader |
| KPI row (band 1) | `StatGroup` → 8× `StatTile` in two `grid gap-4 md:grid-cols-4` rows `data-testid="miss-kpis"`; tiles `kpi-open`, `kpi-wontfix`, misses-this-period, median-time-to-close, `kpi-design-share`, `kpi-escape-share`, `kpi-rework-tokens`, `kpi-rework-usd` | `Figure` each, via `FigureText` | **open and declined are two tiles** — `wont-fix` never inside open, `deferred` always inside it (BRD-120); `insufficient data (n=…)` italic muted when n < 3 |
| Measured-dollars tile | `StatTile` with `Badge Variant=Success` "opencode · measured" and a success-tinted border `data-testid="kpi-rework-usd"` | Σ `CostUsd`, OpenCode records only | "no OpenCode records yet" when empty |
| Rate-card estimate tile | its **own** dashed `Card` on its **own** row, different treatment from every measured tile, `Badge Variant=Outline` **estimate — tokens × rate card, not measured spend** + `RateCard.EstimateLabel` `data-testid="kpi-rework-usd-estimate"` | tokens × `prices.json` for claude-code + codex | BRD-123, decision 6.3: shown by default (the owner's question is a money question) but never the same row and never the same styling as the measured tile |
| Origin phase × miss class (band 2) | `Card` → `DataTable TData=OriginRow ShowToolbar=false ShowPagination=false` `data-testid="miss-origin"`; rows = origin phase (+ an `unattributed` row), columns = miss class | counts | `CardFooter` states **"n of N misses excluded — `origin_confidence ≠ linked`"** `data-testid="miss-taint-count"`, split by `inferred` / `unknown` |
| Failed-practice distribution (band 2) | `Card` (`CardAction` `Badge Variant=Warning` "**28 of 41 misses assessed**") → `DataTable` of the seven `why_missed` values, each with a `Progress` share bar `data-testid="miss-whymissed"` | counts over records **carrying the field** | the `n of N assessed` badge is **on the card face, never a tooltip**; `CardFooter` states how many carry no assessment and how many **predate the field** (BRD-117); a `null` never lands in a bucket, `other` included |
| Observational note (band 3) | `Alert Variant=Warning` — one line of **standing page copy, not a tooltip** | "Miss counts per model and per agent are confounded by which model gets the hard work. Read these as a description of what ran, not as a ranking." | always (BRD-124) |
| By origin model / by origin agent | 2× `Card` → `DataTable` `data-testid="miss-origin-model"` / `"miss-origin-agent"`; model card carries misses · runs · per-100-runs, agent card carries misses · share · dominant class | `linked` records only | `CardAction` `Badge Variant=Outline` "36 linked"; a row under the minimum n renders `insufficient data (n=…)` and never a rate |
| Cost of rework (band 4) | `div.grid.gap-4.md:grid-cols-3` `data-testid="miss-cost"` → 3× `Card`: **Measured** (`cost_attribution: sole`) · **Apportioned** (`shared:n`, `Badge Variant=Warning` "apportioned") · **Unattributable** (`none`) | `MissCost.Sole` / `.Apportioned` / `.NoneCount` | there is **no control on this page that can render one blended number** — `MissCost` exposes no such property (ADR-019); `none` is a count, never a divisor |
| **Whose gap was it** *(band 2b, 2026-09-08)* | `Card` (`CardAction` `Badge Variant=Warning` "**12 of 41 sorted**" `data-testid="miss-sort-denominator"`) → `DataTable ShowToolbar=false ShowPagination=false` `data-testid="miss-sort"`; columns Whose gap · share `Progress` · Count · **The fixed response** | the `sort` distribution over records **eligible** to carry it | Rendered **in words, never as the raw value** — *"There was a check and it did not catch it"*, not `weak-check` (BRD-171). `CardFooter` carries two sentences: `data-testid="miss-sort-predates"` states the records **predating the field** (never "unsorted", never pooled — BRD-172) and `data-testid="miss-sort-amended"` states how many answers arrived by a later `miss-amend` (BRD-176). The page must read correctly at **0 of N** |
| **What reviews cost** *(band 5, 2026-09-08)* | standing `Alert Variant=Info` ("the only record that prices a specification defect"), then `Card` → `DataTable` `data-testid="miss-review-cost"`; columns Review phase · Corrections · Cost to produce · Cost to correct · Correction as share | one row per review phase (`day1-review`, `build-review`, `verify-review`, `handoff-review`), every figure **copied from the record** | An absent figure renders **`not available` italic muted, never `0`** (BRD-175) — a zero here reads as a review that cost nothing. `CardAction` `Badge Variant=Outline` "copied · never computed". `CardFooter` states that a `review` record **is not a miss** and appears in no count, chart or filter on the rest of the page (BRD-174) |
| Per-miss detail | `Card` → `DataTable TData=MissRow ShowToolbar=true ShowPagination=true InitialPageSize=25` `data-testid="miss-detail"`; columns **Miss — what happened** (id, with the record's own `what` sentence as a muted sub-line, `data-testid="miss-what"`) · REQ · Class (`Badge Variant=Outline`) · **Whose gap** (in words) · Origin (phase · agent + a `linked`/`inferred`/`unknown` `Badge`) · Found by · Status (`Badge`) · Tokens (+ `sole`/`shared:n` `Badge Variant=Secondary`) | folded records | *(Severity moved into the row disclosure 2026-09-08 to hold the column count at eight — BRD-144 fails a clipped column.)* A record predating either field renders *"No description — this record predates the field"* italic muted, and `predates the field` in the Whose-gap cell — **never a blank and never a guessed category**. A table footnote states the `what` sentence is the **only prose that travels** from a stream (BRD-173, BRD-84). `InitialPageSize` is set explicitly (TR-009 would otherwise truncate); an unattributable row shows `—`, never `0` |
| Raw record | `Collapsible` → `CollapsibleTrigger` "Raw record — `MISS-…`" → `CodeBlock` | the stored JSON, including `sort` and `what` | a folded `why_missed` or `sort` states that the stored `miss` row still carries `null` — an amend completes a record, never edits it — and names the date each field began |

**Notes / interactions:** the period filter narrows every band at once and never gates the first view; switching Framework re-queries the whole page; every figure renders through `FigureText` so `insufficient data (n=…)` cannot become a number; charts (if added) load the `dataviz` skill and keep to the existing `--chart-1..5` palette so the page reads as part of the same system.

**Empty / loading / error:** no miss records → `Empty` ("No misses recorded yet — TfLens reads `misses.jsonl`; it never writes one"); loading → `Skeleton` tiles; a repo emitting misses with no `miss-fix` records at all is **not** an error here — Coverage says so as a warning (BRD-127).

**Playbook state** (`misses-playbook.html`) — *rebuilt 2026-09-01 (BRD-164..BRD-167)*: the axis is **populated**, not permanently empty. Identical layout and identical guards, four bands plus two extra cards that exist only because the Playbook carries two axes TechieFlow does not:

| Region (Playbook deltas only) | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Ingest note | `Alert Variant=Info` | the records come from the Playbook's committed `misses.ndjson`, normalized by its exporter, imported through **Import metric files** into the **same** `Miss`/`MissFix`/`MissAmend` tables — no second ingest path; upsert on the **immutable source-line hash**, not the TechieFlow natural keys | always |
| KPI row | 8× `StatTile` | lifecycle **opened / closed / reopened / backlog** as four separate figures (`kpi-reopened` is its own tile, never folded into open or closed) · design-miss share · escape share · **rework incidence** with intensity beside it · median **and p90** time-to-close | `insufficient data (n=…)` below `MIN_N` |
| Stricter-guard note | `Alert Variant=Warning` | states the three-part guard in words: `origin_confidence: linked` **and** a complete valid source window **and** a non-null observed model; headline cost additionally needs `sole` + `cost_status: complete` | always (BRD-166) — the Playbook's guards are stricter than the TechieFlow stream's and are **not relaxed to match it** |
| Found-axes card | `Card` → `DataTable` `data-testid="miss-found-axes"` | `found_phase_gate` (**process**) and `found_gate` (**assertion**) as **two side-by-side columns**, each with its own counts | never summed, never merged, never one chart (BRD-165) |
| Item-identity card | `Card` → `DataTable` `data-testid="miss-id-axes"` | `item_id` (Playbook) beside `req_id` (TechieFlow) — one axis under two names, two columns on one table | the other edition's column renders `—`, never a filled-in guess |
| By origin model / tier | 2× `Card` → `DataTable` `data-testid="miss-origin-model"` / `"miss-origin-tier"` | 36 eligible of 63 | **there is no `unknown model` row and there never will be** — a bucket named for a model is a claim about a model; the 27 non-qualifying records live in the exclusion line |
| Cost of rework | 3× `Card` | **Measured** (sole + complete + `cost_status: complete`) · **Apportioned** (`shared:n`, its USD carried on a `Badge Variant=Warning`, separately labelled) · **Unattributable** (`none`, a count) | measured and apportioned never share a series, a total or an aggregate |
| Amendment diagnostics | `Card` → `DataTable` `data-testid="miss-amend-diagnostics"` | amendments folded · **orphans** · **overwrite conflicts** · duplicate source lines skipped | surfaced here and on Coverage — never applied or discarded silently (BRD-164) |
| Empty | `Empty` `data-testid="playbook-empty"` | "No Playbook miss data yet" + Connect-a-Playbook-repo | **now a condition, not the permanent shape of this axis** — it shows only when no Playbook miss records have been imported (BRD-126, BRD-167) |

**The Framework switch is rendered, never hidden.** A zero or a dash on the Playbook stream table is absence, not a good score.

### Screen: Phase effort (`/effort`)

**Mockup:** [docs/mockups/effort.html](./mockups/effort.html) · Playbook state: [docs/mockups/effort-playbook.html](./mockups/effort-playbook.html) · **Role(s):** Owner, Author, Ops · **BRD:** BRD-145..BRD-163, BRD-168, BRD-169 (feature F-EFFORT) · **REQ:** (assigned by split-brd) · **Phase 3** *(added 2026-09-01)*

**Layout (one line):** shell; page header with a period `Select` defaulting to **All history**; a standing budgeting-not-scoreboard note; then five bands — KPI row of **five** tiles ending in **fan-out coverage** · *What is excluded, and why* (the token-window and fan-out-observation denominators, side by side) · the **Command phase** table with `Measured` as a column · expandable per-phase detail of four bands in order (Time · Tokens · By model · Fan-out) · Routing, beside measured spend by harness.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Page header + period filter | `TypographyH2` "Phase effort" + `TypographyMuted` + `Select` `data-testid="effort-period"` | the selected window | **defaults to All history**, for the same reason `/misses` does |
| Budgeting note | `Alert Variant=Info` | "`*build-phase` costing more than `*log-miss` is a fact about what those phases **are**" + links to Misses and Coverage | always (BRD-169) — the page must not frame effort as a quality ranking |
| KPI row | `StatGroup` → 5× `StatTile` in one `grid gap-4 lg:grid-cols-5` `data-testid="effort-kpis"`; tiles `kpi-runs`, `kpi-wallclock`, `kpi-tokens-out`, `kpi-heaviest`, `kpi-fanout-coverage` | runs recorded · Σ `duration_s` · Σ `tokens_out` · heaviest `cmd` by output share · `Σ observed_n / runs_live` | the token tile's sub-line is **`measured on n of N runs`** as visible text (BRD-146); the wall-clock sub-line says *never human effort*; the fan-out tile is a **coverage** figure and carries an accent border — deliberately on the KPI row rather than buried (BRD-151) |
| Token-window denominator | `Card` → `DataTable ShowToolbar=false ShowPagination=false` | one row per `tokens_scope` (`tree` / `conversation` / `main` / `none`-or-absent) | the `none`/absent row is `tokens_unmeasured_n` — excluded, **never averaged in as zero**; `CardFooter` states the `MIN_N = 3` floor |
| Fan-out denominator | `Card` `data-testid="effort-fanout-exclusions"` → `DataTable` with a `Progress` share bar per row | **Observed** (`tokens_scope == "tree"` **and** `subagent_runs != null`) · `unobserved_not_tree` (*we did not look*) · `unobserved_predates_field` (*we could not have looked*) | the two exclusions are **published two ways and never pooled** (BRD-147); `CardFooter` names the `FIELD_SINCE 2026-08-31` floor for the three §2.6 fields, in the same table and code path as `LATE_GATES` (BRD-148) |
| Phase table | `Card` → `DataTable TData=PhaseRow` `data-testid="effort-phases"`, one row per `cmd` sorted by output share | Command phase · Runs · Wall clock · Median · Share of time · Output tokens · Share of output · **Measured** · Fan-out | **`Measured` is a column, not a footnote** (BRD-151); a phase with no observed run reads **`not observed`**, never `0 subagents`; `share_of_*` are the oracle's own strings (`"87%"` / `"—"`) and are not reformatted |
| Phase-table footer | `CardFooter` + `TypographyMuted` | the dimension is the **Command phase**; a window is never split between conceptual phases by token proportion; a **whole-task total is unavailable** without an explicit cohort — a reused session is not one | always (BRD-157) |
| Per-phase detail | `Collapsible` per phase `data-testid="effort-phase-detail"`, one open by default → four `div.panel` bands **in this order** | **1 · Time** (total · median · max · share) · **2 · Tokens** (`measured on n of N` badge on the band label; mean **beside** median) · **3 · By model** (from `model_tokens_out`, with `Progress` share bars) · **4 · Fan-out** (`Alert Variant=Warning` stating **`observed_n of runs` first**, then the numbers) | mean beside median because a mean far above the median means one long run dominates the phase; the model band carries standing copy that the ranking is **observational, not causal** (BRD-150); the fan-out band carries the **declared-vs-measured** line and states in words that the measured figure is authoritative (BRD-149) |
| Routing band | `Card` `data-testid="effort-routing"` → `DataTable` (routed / drifted / unknown) with `Badge` + `Progress` | counts and shares | **`drifted` uses `Badge Variant=Info`, never destructive** — routing is observed, never enforced, and a red pill would assert a rule the framework does not have (BRD-151) |
| Measured spend | `Card` `data-testid="effort-cost"` → `DataTable` (harness · records · measured USD) | `cost_usd_by_harness` | a dash is **"no provider cost recorded"**, never `$0.00` and never "free"; **no rate-card estimate appears anywhere on this page** (BRD-160, BRD-169) — estimates stay on Routing & economics, on their own card |
| Actor note | `TypographyMuted` | "no figure on this page, in the API, in the export or in parity is grouped by `actor`" | always (BRD-168) — there is no parameter, filter or toggle that could produce such a grouping |

**Notes / interactions:** the period filter narrows every band at once; switching Framework re-queries the whole page; every figure renders through `FigureText` so `insufficient data (n=…)` cannot become a number. There is **no per-REQ or per-feature effort view** and no per-subagent cost attribution — both are standing non-goals, stated on the page itself rather than merely omitted.

**Empty / loading / error:** no live runs → `Empty` ("No runs recorded yet"); loading → `Skeleton` tiles; a phase whose runs all lack a token window renders its token cells as `—` with the phase still listed, never dropped.

**Playbook state** (`effort-playbook.html`) — **"Phase efficiency"**, the Playbook axis of the same route (BRD-162):

| Region (Playbook only) | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Filters | `Card` → 12× `Select` `data-testid="pb-effort-filters"` | repository · date range · command phase · model · tier · harness · project type · complete/incomplete · active coverage · tokens scope · verdict snapshot · has-subagents | a model filter matches **any** `models[]` member, not only the dominant one (BRD-158) |
| Summary cards | 8× `StatTile` `data-testid="pb-effort-summary"` | completed phases · median **and p90** wall clock · complete-coverage active time · `contributors / spawned` · five-part token components · **measured** provider cost · incomplete windows · quarantined | each carries **`n of N eligible` beneath it**, not in a global footer (BRD-161) |
| Rate-card estimate | its **own** dashed `Card` `data-testid="pb-cost-estimate"` with `Badge Variant=Outline` **estimate — tokens × rate card** | tokens × rate card | never the same row, series, total or styling as the measured card; the two cohorts differ and are never subtracted (BRD-160) |
| Timing note | `Alert Variant=Warning` | wall-clock elapsed vs **observed active time** (the producer's *union*, overlapping work counted once) vs **human effort** (never captured, never inferred); `assistant_elapsed_ms` + `tool_elapsed_ms` are **never added** | always (BRD-156) |
| Charts | `ChartContainer` + `BarChart` ×2 `data-testid="pb-effort-charts"`, then 2 full-width `DataTable` cards `data-testid="pb-effort-tables"` | stacked token trend by command phase · duration distribution **with the excluded count on the card face** · active-vs-wall-clock for eligible executions only · model mix by **token share and turn share** as two rankings | `coverage: partial` → explicit **lower bound**; `unavailable` → **no figure**; below `MIN_N` → `insufficient data (n=…)` |
| Execution table | `Card` → `DataTable TData=PbExecutionRow` `data-testid="pb-effort-executions"` | one row per phase execution, upserted on `(user, repo, phase_execution_id)` | an `eof` row shows `—` for elapsed and stays **visible**; a `zero-unverified` cost shows its status, never `$0` as spend; a quarantined row shows its reason |
| Expanded execution | `Collapsible` `data-testid="pb-execution-detail"` → 3× `div.panel` | **per-model usage** (turns, five token components, active, cost) · **subagent tree** by `session_id`/`parent_id` **including zero-token children**, grandchildren nested and counted once · **data quality** for each of EOF, missing active timestamps, unpaired tools, missing tier, provider-cost caveat | child usage is already inside the phase totals and is **never summed on again**; `spawned − contributors` is described as a **zero-token or non-contributing child**, never an inferred failure; an absent child agent type renders **unavailable**, never guessed as `"unknown"` (BRD-159) |
| Unsupported harness | `Alert Variant=Warning` `data-testid="pb-effort-unsupported"` | "**Phase effort telemetry unsupported for this harness** — `claude-code`" | a harness with no normalized producer is a **data gap** — never a zero, never an empty measured figure (BRD-163) |
| Five states | `Card` → `DataTable` (reference) | `NoPlaybookRepo` · `NoBundleImported` · `HarnessUnsupported` · `LegacySchemaOnly` · `NoEligibleRows` | *"nothing here"* has five different causes and the page says which one it is |

### Screen: Playbook framework state of the report pages

**Mockup:** [playbook.html](./mockups/playbook.html) (Coverage) · [gate-outcomes-playbook.html](./mockups/gate-outcomes-playbook.html) · [harness-playbook.html](./mockups/harness-playbook.html) · [routing-playbook.html](./mockups/routing-playbook.html) · [misses-playbook.html](./mockups/misses-playbook.html) · [effort-playbook.html](./mockups/effort-playbook.html) · [export-playbook.html](./mockups/export-playbook.html) — each report page's Playbook pill opens its own Playbook-state mockup · **Role(s):** User, Owner · **BRD:** BRD-73..BRD-76, BRD-108..BRD-110 · **REQ:** (assigned by split-brd) · **Phase 3**

*(amended 2026-08-26: the separate `/playbook` page is retired. Every report page has a Playbook state selected by the header Framework switch; the layouts are identical to the TechieFlow state, only the data and a few labels change. This one mockup documents the pattern on Coverage; the other four pages have their own Playbook-state mockups, linked above.)*

**Layout (one line):** the Coverage layout with the Framework switch showing **Playbook** active; a Playbook info note; repo cards for Playbook repos (stream table shows `events`, or the four v1 streams if the repo has converged); populated state (State A) and the Phase-3 empty state (State B).

**Component map (deltas from the TechieFlow state only):**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Framework switch | header `Tabs` segmented control — **Playbook** trigger active (`Badge` repo count) `data-testid="framework-switch"` | — | persisted per user |
| Note | `Alert Variant=Info` | "Playbook process-gates (phase_gate) and TechieFlow assertion-gates (gate) are different axes and never share a chart (SCHEMA.md §11). Figures are never pooled across frameworks." | always |
| Repo card ×N | `Card` (`CardTitle` owner/name, `Badge` "playbook", `Badge` SHA); stream table row `events` (or v1 streams) | — | — |
| Phase totals (Gate outcomes page, Playbook state) | `DataTable TData=PhaseRow ShowToolbar=false` `data-testid="pb-phases-{name}"` (phase_gate · events · caught · escaped · tokens · cost if present) | keyed by `phase_gate` | cost absent → `—`; n < 3 → `insufficient data (n=…)` |
| Main vs subagent (Routing page, Playbook state) | 2× `StatTile`: Main-session tokens · Sub-agent tokens (via parentID) + `Progress` share bar | split | — |
| Schema discovery (Coverage) | `Collapsible` "Observed fields" → list of `Badge` | field names from overflow | — |
| Empty | `Empty` (`EmptyTitle` "No Playbook data yet", `EmptyDescription` "Phase 3 — the report set for Playbook repos is built after the real events.ndjson is parsed", `EmptyAction` → Connect a Playbook repo) `data-testid="playbook-empty"` | — | default until Phase 3 |

**Notes / interactions:** switching framework re-queries every figure on the page; the export writes one snapshot per framework.


### Screen: Price providers (`/prices`)

**Mockup:** [docs/mockups/prices.html](./mockups/prices.html) · **Role(s):** User · **BRD:** BRD-200, BRD-201 · **REQ:** REQ-UI-072, REQ-FN-142 · *added 2026-09-11, drawn after the page was built*

**Layout (one line):** shell (Workspace › Price providers, no Framework switch); header with the rate count; a standing note; one `Card` per provider with its rates; then Add a provider and Add or change a rate side by side.

| Region | TrBlazeUI control | Shows or binds | States |
|--------|-------------------|----------------|--------|
| Header | `TypographyH2`, `Badge Variant=Outline` `prices-model-count` | rates and providers | — |
| Standing note | `Alert` `prices-standing-note` | nobody was billed these amounts: a list price, never spend | always |
| Clash warning | `Alert Variant=Warning` `prices-clashes` | models two providers price; the first wins, never averaged | only on a clash |
| Provider card | `Card` `prices-provider-{id}`: title, published-page link, endpoint or typed `Badge`, `prices-checked-{id}`, Refresh `prices-refresh-{id}` (endpoint only), Remove `prices-remove-{id}` | one provider | "never checked" instead of a date |
| Rates | `DataTable` `prices-table-{id}`: Model (mono), Input, Output, Cache read, Cache write; paged, with a filter when long | USD per 1M tokens | none: "No rates yet" |
| Add a provider | `Card` `prices-add-provider`, 3× `Input`, `Button` `prices-add` | — | — |
| Add or change a rate | `Card` `prices-add-rate`, `Select` `prices-rate-provider`, 5× `Input`, `Button` `prices-save-rate` | — | — |
| Result | `Alert` `prices-message` | the last action's outcome | after an action |

| Field | Type | Required | Validation |
|---|---|---|---|
| Provider name | text | yes | not empty, not already listed |
| Published pricing page | url | no | — |
| Endpoint | url | no | given → refreshed from it |
| Rate provider | select | yes | an existing provider |
| Model id | text | yes | matched exactly against run records |
| Four rates per 1M | number | yes | a number, not negative |

**Dialogs opened here:** none.

**States:** empty: a provider with no rates says so, and an unpriced model is counted, never priced at zero · loading: `Skeleton` cards; buttons disabled during a refresh or save · error: a failed refresh leaves the stored rates unchanged and says so.

## Where the rest lives

| What | Where |
|---|---|
| UI library, theme and the design system | [phase 1 UI design](./TfLens-UIDesign.md) |
| The click-through flow across every phase | [phase 1 UI design](./TfLens-UIDesign.md) |
| Library gaps — controls TrBlazeUI lacks, logged once for every phase | [TfLens-TrBlazeUI-Feedback.md](./TfLens-TrBlazeUI-Feedback.md) |
| This phase's requirements | [TfLens-P3-BRD.md](./TfLens-P3-BRD.md) |
| This phase's work list | [TfLens-P3-Checklist.md](./TfLens-P3-Checklist.md) |
| Every phase and its screens | [TfLens-Phases.md](./TfLens-Phases.md) |
