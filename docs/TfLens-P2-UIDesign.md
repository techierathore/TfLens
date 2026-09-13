# TfLens — UI Design — Phase 2: Reports

| | |
|---|---|
| App | TfLens |
| Kind | app |
| Size | Large |
| Phase | 2 of 3 |
| Date | 2026-09-08 |

This file holds the 5 screens of phase 2; what every phase shares is listed under *Where the rest lives* at the end.

## Screens

### Screen: Coverage / health (`/`)

**Mockup:** [docs/mockups/coverage.html](./mockups/coverage.html) · **Role(s):** User, Owner · **BRD:** BRD-39..BRD-44, BRD-21, BRD-6, BRD-127, BRD-181 · **REQ:** (assigned by split-brd) *(amended 2026-09-08 — the Data quality section)*

**Layout (one line):** shell; summary strip (status alert + 4 KPI cards); one `Card` per connected repo in a 2-column grid, each with a stream staleness table and warnings; a **Data quality** section (four cards in a 2×2 grid — field completeness, derived values, unrecognised values, miss-stream diagnostics — added 2026-09-08); unknown-fields `Collapsible`; Rebuild-from-raw card at the bottom.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Status strip | `Alert Variant=Success` "GREEN — 5 repos synced, nothing stale" **or** `Alert Variant=Warning` "CHECK — 2 warnings" `data-testid="coverage-status"` | summary | — |
| KPI row | 4× `Card` (KPI): Repos synced · Gate records (live / backfilled) · Newest record age · Sync errors | counts | zero-state "0" |
| Repo card ×N | `Card` → `CardHeader` (`CardTitle` owner/name, `Badge` kind, `Badge Variant=Outline` short SHA linked) → `CardContent` | last sync, outcome, per-stream table | error state: `Alert Variant=Danger` with LastError text |
| Per-stream table | `DataTable TData=StreamRow ShowToolbar=false ShowPagination=false` `data-testid="repo-streams-{name}"`; columns Stream · Records · Backfilled · Newest · Days since | **5 rows** (runs, gates, sessions, commits, **misses** — amended 2026-08-28); a playbook repo shows one `events` row | stale row → `Badge Variant=Destructive` "stale" |
| Miss data quality *(2026-08-28)* | `Card` → `DataTable ShowToolbar=false ShowPagination=false` `data-testid="escapes-missing-why"` / `"orphan-misses"`: escapes with no `why_missed` · orphan `miss-fix` · orphan `miss-amend` | counts only | these are **data-quality** facts, not quality figures — they live here and never on the `/misses` KPI row |
| Reclassification split *(2026-08-28)* | `Alert Variant=Warning AccentBorder` `data-testid="reclass-split"` | "This repo is classified `app` today but carries 225 records written as `docs`. §6 forbids pooling across `project_type`, so it appears as two segments — each a *period* of the project, not the whole of it." | only when the current classification disagrees with the stored records |
| Misses without fixes *(2026-08-28)* | `Alert Variant=Warning` | "n misses recorded and no `miss-fix` records at all — the fix path may not be wired up yet." | a **warning, never an error**; the health badge does not fail on it |
| Source badge *(2026-08-28 r2)* | `Badge Variant=Info` **Synced** / `Badge Variant=Secondary` **Imported** on each repo card header `data-testid="repo-source-badge-{name}"` | `UserRepo.SourceKind` | always — origin is shown everywhere and pools nowhere (BRD-136, ADR-021) |
| Staleness warning | `Alert Variant=Warning AccentBorder` | **fetched:** "sessions/commits stale ≥ 7 days — this clone isn't pushing or lacks hooks; run update-framework.sh on it" · **imported** *(2026-08-28 r2)*: "last imported 12 days ago — this source can't refresh itself; re-import to update" | only when stale; the hook diagnosis is **never** shown on an imported source — it would be advice about a clone TfLens cannot see (BRD-137) |
| Unknown fields | `Collapsible` → `CollapsibleTrigger` "Fields observed that SCHEMA.md doesn't document (n)" → list of `Badge Variant=Outline` per field grouped by repo/stream; `Alert Variant=Info` if any `v > 1` | field names only | empty: "none" |
| Field completeness *(2026-09-08)* | `Card` → `DataTable ShowToolbar=false ShowPagination=false` `data-testid="cov-field-completeness"`; columns Repo · Misses · No `sort` · No `what` · Eligible, with an all-repos total row | per-repo counts of misses carrying neither field, against that repo's eligible misses | **Never a health failure.** Both fields began 2026-09-07 (BRD-117 `FIELD_SINCE`); a repo whose records all predate them is not unhealthy — TfLens's own repository is exactly that case. Footer states the total in words: *"286 of 359 misses predate `sort`"* |
| Derived values *(2026-09-08)* | `Card` → `DataTable` `data-testid="cov-derived"`; columns Field · Derived · How | `duration_s` derived from `ended − started`; `attempt` derived from prior live records for the same `(project, req_id)` | Always shown when non-zero. Footer states that nothing is written back and `Rebuild` re-derives the same values — **deriving is not backfilling** (BRD-179, BRD-180) |
| Unrecognised values *(2026-09-08)* | `Card` → `DataTable` `data-testid="cov-unrecognised-values"`; columns Where · Value (`Badge Variant=Outline`, mono) · Records | any `cmd`, `verdict` or `harness` value outside the expected list | **Shown and counted, never filtered.** Sits beside the unknown-*fields* list for the same reason (BRD-181, ADR-032); empty state is "none" |
| Miss-stream diagnostics *(2026-09-08 — supersedes the two rows above it)* | `Card` → `DataTable kv` `data-testid="cov-miss-diagnostics"` | escapes with no `why_missed` · orphan `miss-fix` · orphan `miss-amend` · **amendments folded** · **`review` records read (not misses)** · **framework's own `req_class: FR` verdicts, kept separate** | counts only; the reclassification split and the misses-without-fixes warning render as footer sentences on this card |
| Rebuild | `Card` → `CardTitle` "Rebuild from raw" → `AlertDialog` (`AlertDialogTrigger` `Button Variant=Destructive` "Rebuild…" `data-testid="rebuild"`, `AlertDialogAction` "Drop and reparse") | last rebuild report | `Progress` while replaying |

**Notes / interactions:** Sync now and Rebuild refresh in place; SHA badge opens the GitHub commit; cards stack to one column under `md`; a user with no repos is redirected to `/repos`.

**Empty / loading / error:** first run before any sync → `Empty` ("No sync yet", action Sync now); loading → `Skeleton` cards; per-repo error → danger alert inside that card, others unaffected.

### Screen: Gate outcomes (`/gate-outcomes`)

**Mockup:** [docs/mockups/gate-outcomes.html](./mockups/gate-outcomes.html) · **Role(s):** User, Owner, Author · **BRD:** BRD-45..BRD-50 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; standing SCHEMA §6 note; `Tabs` — one tab per project_type present (`app`, `library`, `docs`, `framework`, `unclassified`) with **no "all" tab**; inside each tab: 3 KPI cards (live) with the backfilled value beneath as a labelled secondary line, the gate distribution table with live and backfilled columns and a horizontal bar per row, late-gate coverage lines, and the taint list collapsible.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Standing note | `Alert Variant=Info` | "Figures are never combined across project_type or across live/backfilled (SCHEMA.md §6). There is no total." | always |
| Type tabs | `Tabs DefaultValue="app"` → `TabsList` → `TabsTrigger` per type (label + `Badge` record count) `data-testid="type-tab-{type}"` | — | only types with records appear; `unclassified` labelled "unclassified (project_type inferred)" |
| Question cards | 3× `Card` (KPI): First-pass rate · Escape rate · Failures scored — big live value; secondary line `Badge Variant=Secondary` "backfilled" + value | `Figure` | `insufficient data (n=…)` italic muted text when n < 3 |
| Segment facts | `TypographyMuted` line | "live: 41 records, 17 REQs scored, 3 excluded by backfill taint" | — |
| Gate distribution | `DataTable TData=GateRow ShowToolbar=false ShowPagination=false` `data-testid="gate-dist-{type}"`; columns Gate · Live count · Live share (with `Progress` bar) · Backfilled count · Backfilled share; rows in order build, acceptance, render, visual, perf, standards, **escaped**, unattributed | counts + `%` | `escaped` row has `Badge Variant=Destructive` "no gate caught it"; `perf` row has `Badge Variant=Outline` "see coverage" |
| Late-gate coverage | `Card` → `CardContent` one line per late gate | "perf gate: ran on 6 records, caught 1 → insufficient data (n=6)" / "not yet run on this data (gate added 2026-08-10)" | — |
| Taint list | `Collapsible` → trigger "3 REQs excluded from the live first-pass rate" → `Badge Variant=Outline` per REQ ID `data-testid="taint-list"` | REQ IDs | empty: "none" |

**Notes / interactions:** tab choice persisted per session; every figure has a `Tooltip` with its formula (SCHEMA.md §8 wording).

**Empty / loading / error:** no gate records → `Empty` "No gate records yet — run *verify on a connected repo"; loading → `Skeleton`.

### Screen: Harness comparison (`/harness`)

**Mockup:** [docs/mockups/harness.html](./mockups/harness.html) · **Role(s):** User, Owner, Author · **BRD:** BRD-51..BRD-55 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; explanatory note; one `Card` column per detected harness side by side (**claude-code · opencode · codex**), each with the same rows; a footnote row for `harness: null`; below, a tokens-by-harness bar chart and the OpenCode-only dollars card. *(amended 2026-08-26: Codex CLI column; null is a footnote, never a column — BRD-51, BRD-55, ADR-017)*

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Note | `Alert Variant=Info` | "Tokens may be compared across harness; dollars may not. Claude Code and Codex cost is null by design." | always |
| Harness columns | `div.grid.gap-4.md:grid-cols-3` → `Card` per harness (`CardTitle` harness name + `LucideIcon`) `data-testid="harness-col-{claude-code|opencode|codex}"` | — | a harness with 0 records still renders with `—` |
| Not-detected footnote | `TypographyMuted` row under the columns `data-testid="harness-null-footnote"` | "*n* records with harness not detected — excluded from the columns above" | hidden when n = 0 |
| Column rows | `DataTable TData=KeyValueRow ShowToolbar=false ShowPagination=false` inside each card: Runs · Runs by cmd (top 3) · Gate records · Verdict mix · Sessions · Tokens in / out / cache read / cache write · Tokens per Verified REQ | values | `insufficient data (n=…)` text |
| Tokens chart | `ChartContainer Class="h-[280px]"` → `BarChart TItem=HarnessTokens XValue=harness YValue=tokens` (`--chart-1..3`) | total tokens per harness | empty: `Empty` "no token data" |
| Dollars | `Card` (`CardTitle` "Measured dollars (OpenCode only)", `Badge Variant=Secondary` "the only measured dollars in the system") → big `$` value + `TypographyMuted` "Claude Code and Codex: not measured (null by design)" `data-testid="opencode-cost"` | Σ `cost_usd` for `opencode` | no OpenCode records → "no OpenCode records yet" |

**Notes / interactions:** no total dollar row anywhere; columns wrap to one per row under `md`.

### Screen: Routing & economics (`/routing`)

**Mockup:** [docs/mockups/routing.html](./mockups/routing.html) · **Role(s):** User, Owner, Author · **BRD:** BRD-56..BRD-62 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; `Tabs` Routing drift · Tokens by model · Repricing (estimate) · Poolable metrics; the Repricing tab has two big cards (actual mix vs all-at-max) with the estimate badge and an "Edit prices" dialog button.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Tabs | `Tabs DefaultValue="drift"` → `TabsTrigger` drift / models / repricing / poolable `data-testid="routing-tab-{key}"` | — | — |
| Drift summary | 3× `Card` (KPI): Runs with routing fields · `routed:false` runs · Distinct observed models | counts | — |
| Drift table | `DataTable TData=DriftRow` `data-testid="drift-table"`; columns cmd · tier · tier_model · observed model · models · routed · ts | rows for `routed:false` first | `routed:false` → `Badge Variant=Destructive` "drift"; empty → `Empty` "no routing fields captured yet" |
| Tokens by model | `DataTable TData=ModelTokens` (model · in · out · cache read · cache write · total) + `ChartContainer` → `BarChart` totals | Σ per model | — |
| Repricing cards | 2× `Card`: "Actual mix" and "All runs at most expensive model (`claude-opus-…`)"; each `Badge Variant=Outline` **estimate — tokens × rate card, not measured spend**; big `$` value; `TypographyMuted` "n runs excluded (tokens_scope none)" `data-testid="repricing-actual"` / `"repricing-max"` | computed from `prices.json` | missing price → `Alert Variant=Warning` naming the model |
| Delta | `Card` "Counterfactual delta" | max − actual, `%` | — |
| Edit prices | `Dialog` (`DialogTrigger` `Button Variant=Outline` "Edit prices.json" `data-testid="edit-prices"`) → `DialogContent` with a `DataTable` of editable rows (`Input Type=Number` per cell) + `Button` Save / `DialogClose` Cancel | model · input · output · cache read · cache write USD per 1M | validation `FieldError`; toast on save |
| Poolable metrics | 5× `Card` (KPI): Rework ratio · Batch size (median) · REQ throughput (REQs/hour) · Tokens per Verified · Commit cadence (+ duplicates collapsed) | from engine `Pooled` | `insufficient data (n=…)` |

### Screen: Snapshot export (`/export`)

**Mockup:** [docs/mockups/export.html](./mockups/export.html) · **Role(s):** User, Author, Parity operator · **BRD:** BRD-63, BRD-65, BRD-66, BRD-67, BRD-70 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; quotable / not-quotable banner; Export card with the button and the dataset SHAs; past snapshots table with download links; parity record card.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Quotable banner | `Alert Variant=Success` "QUOTABLE — …" **or** `Alert Variant=Warning` "NOT QUOTABLE — parser changed after the last parity run; re-run the parity procedure" `data-testid="quotable-banner"` | parity vs parser version | — |
| Export card | `Card` → `Button` **Export snapshot** (`LucideIcon download`) `data-testid="export-now"` + `TypographyMuted` "writes data/reports/<you>/<date>/snapshot.md + tflens.json" | — | `Spinner`; toast |
| Dataset SHAs | `DataTable ShowToolbar=false ShowPagination=false` (repo · branch · SHA · synced) with copy `Button Size=IconSmall` | from `sync_state` | — |
| Past snapshots | `DataTable TData=SnapshotRow` `data-testid="snapshots"` (date · parser version · parity status · snapshot.md · tflens.json links) | user's folders | empty → `Empty` "no snapshots yet" |
| Parity record | `Card` "Last parity run" → date, dataset SHAs, script hash, parser version, compare output (`pre`) | `data/parity-last.json` | none → `Alert Variant=Warning` |


## Where the rest lives

| What | Where |
|---|---|
| UI library, theme and the design system | [phase 1 UI design](./TfLens-UIDesign.md) |
| The click-through flow across every phase | [phase 1 UI design](./TfLens-UIDesign.md) |
| Library gaps — controls TrBlazeUI lacks, logged once for every phase | [TfLens-TrBlazeUI-Feedback.md](./TfLens-TrBlazeUI-Feedback.md) |
| This phase's requirements | [TfLens-P2-BRD.md](./TfLens-P2-BRD.md) |
| This phase's work list | [TfLens-P2-Checklist.md](./TfLens-P2-Checklist.md) |
| Every phase and its screens | [TfLens-Phases.md](./TfLens-Phases.md) |
