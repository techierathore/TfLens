# TfLens — UI Design Spec (Mockups)

> **What this is.** The approved visual design for TfLens, produced at day-1 (greenfield) before any UI is built. Each screen has a **rendered mockup** (`docs/mockups/{screen}.html`, styled to look like TrBlazeUI) and a **component map** that ties every region to a real **TrBlazeUI control**, so the build (`/trblazeui`) reproduces it 1:1 and the verifier's visual-truth gate (`verify-phase.md §4b`) can diff the live screen against it. This is a HUMAN document → rendered to HTML. The owner APPROVES it (alongside the BRD + Architecture) before build.

## Table of Contents

1. [How to use](#how-to-use)
2. [Design system (TrBlazeUI)](#design-system-trblazeui)
3. [Screens](#screens)
   - [Screen: Login (`/login`)](#screen-login-login)
   - [Screen: Coverage / health (`/`)](#screen-coverage-health)
   - [Screen: Three questions (`/three-questions`)](#screen-three-questions-three-questions)
   - [Screen: Harness comparison (`/harness`)](#screen-harness-comparison-harness)
   - [Screen: Routing & economics (`/routing`)](#screen-routing-economics-routing)
   - [Screen: Snapshot export (`/export`)](#screen-snapshot-export-export)
   - [Screen: Playbook (`/playbook`)](#screen-playbook-playbook)
4. [Library gaps](#library-gaps)

## How to use

- Every screen below links to its rendered mockup in `docs/mockups/`. Open those `.html` files in a browser to see the intended layout.
- The **Component map** is the build contract: `region → TrBlazeUI control`. Only controls that actually exist in the TrBlazeUI library are used (the analyst read the component catalog first — `TrBlazeUI-AI-Reference.md`, TrBlazeUI.Components 1.0.7). If a screen needs something the library lacks, it is flagged in §Library gaps and logged to `docs/TfLens-TrBlazeUI-Feedback.md` at build time.
- To change a screen after approval: run `*mockups TfLens --update` (or `*amend-docs` for a requirement change that adds screens).

## Design system (TrBlazeUI)

- **Source:** TrBlazeUI component library (`TrBlazeUI.Primitives` + `TrBlazeUI.Components` + `TrBlazeUI.Icons.Lucide`; Tailwind v4 utilities; shadcn/ui design language with OKLCH tokens `--background`, `--foreground`, `--card`, `--muted`, `--muted-foreground`, `--primary`, `--border`, `--destructive`, `--alert-success/info/warning/danger`, `--chart-1..5`, `--radius: 0.625rem`, `--sidebar*`).
- **Layout shell:** `SidebarProvider` (`DefaultOpen=true`, `HeightClass="h-screen"`) → `Sidebar` (`Collapsible=true`) with `SidebarHeader` / `SidebarContent` / `SidebarFooter` → `SidebarInset` holding a hand-rolled `<header class="flex h-16 items-center gap-4 border-b bg-background px-4">` (with `SidebarTrigger`, page title, **Sync now** `Button`, last-sync `Badge`, sign-out `Button Variant=Ghost`) and the page container `<div class="flex-1 overflow-auto p-6 md:p-8">@Body</div>`. Plus `<ToastProvider Position="BottomRight" />` and `<PortalHost />` in the layout.
- **Navigation:** `SidebarGroup` → `SidebarGroupLabel "Reports"` → `SidebarMenu` → six `SidebarMenuItem` / `SidebarMenuButton Href=… Tooltip=…` with `LucideIcon` (`activity`, `help-circle`, `git-compare`, `route`, `download`, `book-open`). Order is fixed: Coverage, Three questions, Harness, Routing & economics, Snapshot export, Playbook.
- **Theme:** light default (TrBlazeUI `:root`), dark via `class="dark"` on `<html>` (no toggle component — a `Switch` in the sidebar footer flips the class).
- **KPI pattern:** TrBlazeUI has no stat card; every KPI is the library's documented composition — `Card` → `CardHeader class="flex flex-row items-center justify-between pb-2"` (`CardTitle class="text-sm font-medium"` + `LucideIcon`) → `CardContent` (`div.text-2xl.font-bold` + `p.text-xs.text-muted-foreground`). Grid `grid gap-4 md:grid-cols-2 lg:grid-cols-4`.
- **Figures:** a `Figure` renders as a number, or as the literal text `insufficient data (n=…)` in `text-muted-foreground italic`, or as `—`. Estimates always carry a `Badge Variant=Outline` reading **estimate**. Backfilled columns carry a `Badge Variant=Secondary` reading **backfilled**.
- **Controls inventory used:** `SidebarProvider`, `Sidebar`, `SidebarHeader`, `SidebarContent`, `SidebarFooter`, `SidebarGroup`, `SidebarGroupLabel`, `SidebarMenu`, `SidebarMenuItem`, `SidebarMenuButton`, `SidebarInset`, `SidebarTrigger`, `Card` (+ `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`, `CardFooter`), `DataTable` + `DataTableColumn`, `Badge`, `Alert` (+ `AlertTitle`, `AlertDescription`), `Tabs` (+ `TabsList`, `TabsTrigger`, `TabsContent`), `Button`, `Input`, `Label`, `Field` (+ `FieldLabel`, `FieldContent`, `FieldError`), `Dialog` (+ `DialogTrigger`, `DialogContent`, `DialogHeader`, `DialogTitle`, `DialogDescription`, `DialogFooter`, `DialogClose`), `AlertDialog` (rebuild confirm), `Collapsible` (+ `CollapsibleTrigger`, `CollapsibleContent`), `Progress`, `Tooltip`, `Skeleton`, `Spinner`, `Empty` (+ `EmptyIcon`, `EmptyTitle`, `EmptyDescription`, `EmptyAction`), `ToastProvider` / `ToastService`, `Switch`, `Separator`, `ChartContainer` + `BarChart`, `LucideIcon`, `TypographyH2`/`TypographyMuted`.

## Screens

### Screen: Login (`/login`)

**Mockup:** [docs/mockups/login.html](./mockups/login.html) · **Role(s):** anonymous → Owner · **BRD:** BRD-1, BRD-2, BRD-3 · **REQ:** (assigned by split-brd)

**Layout (one line):** centred single `Card` (max-width 24rem) on the plain background; no sidebar; product name + one-line purpose above the form.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Page background | plain `div.min-h-screen.flex.items-center.justify-center.bg-muted` | — | — |
| Card | `Card` → `CardHeader` (`CardTitle` "TfLens", `CardDescription` "A read-only lens over TechieFlow telemetry") | — | — |
| Username | `Field` → `FieldLabel` + `Input Type=Text` `data-testid="login-user"` | binds `Username` | required; `IsInvalid` on error |
| Password | `Field` → `FieldLabel` + `Input Type=Password` `data-testid="login-pass"` | binds `Password` | required |
| Error | `Alert Variant=Danger` (only when a failed attempt) | "Sign-in failed." (generic) | hidden by default |
| Submit | `Button Type=Submit` full width `data-testid="login-submit"` | "Sign in" | `Disabled` + `Spinner Size=Small` while verifying |
| Footer | `TypographyMuted` | "Single-user dashboard · read-only" | — |

**Notes / interactions:** Enter submits; return URL preserved; no links to register/reset.

**Empty / loading / error:** loading = button spinner; error = generic `Alert`, fields keep values; never reveals which field was wrong.

### Screen: Coverage / health (`/`)

**Mockup:** [docs/mockups/coverage.html](./mockups/coverage.html) · **Role(s):** Owner · **BRD:** BRD-39..BRD-44, BRD-21, BRD-6 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; summary strip (status badge + 4 KPI cards); one `Card` per repo in a 2-column grid, each with a stream staleness table and warnings; unknown-fields `Collapsible`; Rebuild-from-raw card at the bottom.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Header (shell) | `SidebarTrigger`, `TypographyH2` "Coverage / health", `Button` **Sync now** `data-testid="sync-now"`, `Badge Variant=Outline` "last sync 12 min ago", `Button Variant=Ghost` "Sign out" | — | Sync button shows `Spinner` while running; toast per repo outcome |
| Status strip | `Alert Variant=Success` "GREEN — 5 repos synced, nothing stale" **or** `Alert Variant=Warning` "CHECK — 2 warnings" `data-testid="coverage-status"` | summary | — |
| KPI row | 4× `Card` (KPI pattern): Repos synced · Gate records (live / backfilled) · Newest record age · Sync errors | counts | zero-state "0" |
| Repo card ×N | `Card` → `CardHeader` (`CardTitle` owner/name, `Badge` kind, `Badge` short SHA linked) → `CardContent` | last sync, outcome, per-stream table | error state: `Alert Variant=Danger` with LastError text |
| Per-stream table | `DataTable TData=StreamRow ShowToolbar=false ShowPagination=false` `data-testid="repo-streams-{name}"`; columns Stream · Records · Backfilled · Newest · Days since | 4 rows (runs, gates, sessions, commits) | stale row → `Badge Variant=Destructive` "stale" |
| Staleness warning | `Alert Variant=Warning AccentBorder` | "sessions/commits stale ≥ 7 days — this clone isn't pushing or lacks hooks; run update-framework.sh on it" | only when stale |
| Unknown fields | `Collapsible` → `CollapsibleTrigger` "Fields observed that SCHEMA.md doesn't document (n)" → `CollapsibleContent` list of `Badge Variant=Outline` per field, grouped by repo/stream; `Alert Variant=Info` if any `v > 1` | field names only | empty: "none" |
| Rebuild | `Card` → `CardTitle` "Rebuild from raw" → `AlertDialog` (`AlertDialogTrigger` `Button Variant=Destructive` "Rebuild…" `data-testid="rebuild"`, `AlertDialogAction` "Drop and reparse") | last rebuild report | `Progress` while replaying |

**Notes / interactions:** Sync now and Rebuild refresh the page in place; SHA badge opens the GitHub commit in a new tab; cards stack to one column under `md`.

**Empty / loading / error:** first run before any sync → `Empty` (`EmptyTitle` "No sync yet", `EmptyAction` Sync now); loading → `Skeleton` cards; per-repo error → danger alert inside that card, others unaffected.

### Screen: Three questions (`/three-questions`)

**Mockup:** [docs/mockups/three-questions.html](./mockups/three-questions.html) · **Role(s):** Owner, Author · **BRD:** BRD-45..BRD-50 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; standing SCHEMA §6 note; `Tabs` — one tab per project_type present (`app`, `library`, `docs`, `framework`, `unclassified`) with **no "all" tab**; inside each tab: 3 KPI cards (live) with the backfilled value beneath as a labelled secondary line, the gate distribution table with live and backfilled columns, late-gate coverage lines, and the taint list collapsible.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Standing note | `Alert Variant=Info` | "Figures are never combined across project_type or across live/backfilled (SCHEMA.md §6). There is no total." | always |
| Type tabs | `Tabs DefaultValue="app"` → `TabsList` → `TabsTrigger` per type (label + `Badge` record count) `data-testid="type-tab-{type}"` | — | only types with records appear; `unclassified` labelled "unclassified (project_type inferred)" |
| Question cards | 3× `Card` (KPI): First-pass rate · Escape rate · Failures scored — big live value; secondary line `Badge Variant=Secondary` "backfilled" + value | `Figure` | `insufficient data (n=…)` italic muted text when n < 3 |
| Segment facts | `TypographyMuted` line | "live: 41 records, 17 REQs scored, 3 excluded by backfill taint" | — |
| Gate distribution | `DataTable TData=GateRow ShowToolbar=false ShowPagination=false` `data-testid="gate-dist-{type}"`; columns Gate · Live count · Live share · Backfilled count · Backfilled share; rows in order build, acceptance, render, visual, perf, standards, **escaped**, unattributed | counts + `%` | `escaped` row has `Badge Variant=Destructive` "no gate caught it"; `perf` row has `Badge Variant=Outline` "see coverage" |
| Late-gate coverage | `Card` → `CardContent` one line per late gate | "perf gate: ran on 6 records, caught 1 → insufficient data (n=6)" / "not yet run on this data (gate added 2026-08-10)" | — |
| Taint list | `Collapsible` → trigger "3 REQs excluded from the live first-pass rate" → `CollapsibleContent` `Badge Variant=Outline` per REQ ID `data-testid="taint-list"` | REQ IDs | empty: "none" |

**Notes / interactions:** tab choice persisted per session; every figure has a `Tooltip` with its formula (SCHEMA.md §8 wording).

**Empty / loading / error:** no gate records → `Empty` "No gate records yet — run *verify on a managed repo"; loading → `Skeleton`.

### Screen: Harness comparison (`/harness`)

**Mockup:** [docs/mockups/harness.html](./mockups/harness.html) · **Role(s):** Owner, Author · **BRD:** BRD-51..BRD-55 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; explanatory note; one `Card` column per harness side by side (claude-code · opencode · not detected), each with the same rows; below, a tokens-by-harness bar chart and the OpenCode-only dollars card.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Note | `Alert Variant=Info` | "Tokens may be compared across harness; dollars may not. Claude Code cost is null by design." | always |
| Harness columns | `div.grid.gap-4.md:grid-cols-3` → `Card` per harness (`CardTitle` harness name + `Badge` "not detected" for null) `data-testid="harness-col-{name}"` | — | a harness with 0 records still renders with `—` |
| Column rows | `DataTable TData=KeyValueRow ShowToolbar=false ShowPagination=false` inside each card: Runs · Runs by cmd (top 3) · Gate records · Verdict mix · Sessions · Tokens in / out / cache read / cache write · Tokens per Verified REQ | values | `insufficient data (n=…)` text |
| Tokens chart | `ChartContainer Class="h-[280px]"` → `BarChart TItem=HarnessTokens XValue=harness YValue=tokens` | total tokens per harness | empty: `Empty` "no token data" |
| Dollars | `Card` (`CardTitle` "Measured dollars (OpenCode only)", `Badge Variant=Secondary` "the only measured dollars in the system") → `CardContent` big `$` value + `TypographyMuted` "Claude Code: not measured (null by design)" `data-testid="opencode-cost"` | Σ `cost_usd` for `opencode` | no OpenCode records → "no OpenCode records yet" |

**Notes / interactions:** no total dollar row anywhere; columns wrap to one per row under `md`.

**Empty / loading / error:** no runs/sessions → `Empty`; loading → `Skeleton` columns.

### Screen: Routing & economics (`/routing`)

**Mockup:** [docs/mockups/routing.html](./mockups/routing.html) · **Role(s):** Owner, Author · **BRD:** BRD-56..BRD-62 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; `Tabs` Routing drift · Tokens by model · Repricing (estimate) · Poolable metrics; the Repricing tab has two big cards (actual mix vs all-at-max) with the estimate badge and an "Edit prices" dialog button.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Tabs | `Tabs DefaultValue="drift"` → `TabsTrigger` drift / models / repricing / poolable | — | — |
| Drift summary | 3× `Card` (KPI): Runs with routing fields · `routed:false` runs · Distinct observed models | counts | — |
| Drift table | `DataTable TData=DriftRow` `data-testid="drift-table"`; columns cmd · tier · tier_model · observed model · models · routed · ts | rows for `routed:false` first | `routed:false` → `Badge Variant=Destructive` "drift"; empty → `Empty` "no routing fields captured yet" |
| Tokens by model | `DataTable TData=ModelTokens` (model · in · out · cache read · cache write · total) + `ChartContainer` → `BarChart` totals | Σ per model | — |
| Repricing cards | 2× `Card`: "Actual mix" and "All runs at most expensive model (`claude-opus-…`)"; each `Badge Variant=Outline` **estimate — tokens × rate card, not measured spend**; big `$` value; `TypographyMuted` "n runs excluded (tokens_scope none)" `data-testid="repricing-actual"` / `"repricing-max"` | computed from `prices.json` | missing price for a model → `Alert Variant=Warning` naming the model; figure shows `—` |
| Delta | `Card` "Counterfactual delta" | max − actual, `%` | — |
| Edit prices | `Dialog` (`DialogTrigger` `Button Variant=Outline` "Edit prices.json" `data-testid="edit-prices"`) → `DialogContent` with a `DataTable` of editable rows (`Input Type=Number` per cell) + `Button` Save / `DialogClose` Cancel | model · input · output · cache read · cache write USD per 1M | validation `FieldError` on non-numeric; toast on save |
| Poolable metrics | 5× `Card` (KPI): Rework ratio · Batch size (median) · REQ throughput (REQs/hour) · Tokens per Verified · Commit cadence (+ duplicates collapsed) | from engine `Pooled` | `insufficient data (n=…)` |

**Notes / interactions:** the most expensive model is chosen by output price; the label names it; saving prices recomputes immediately.

**Empty / loading / error:** no §2.5 fields at all → drift tab `Empty`; prices file missing → warning alert + "create default" button.

### Screen: Snapshot export (`/export`)

**Mockup:** [docs/mockups/export.html](./mockups/export.html) · **Role(s):** Author, Parity operator · **BRD:** BRD-63, BRD-65, BRD-66, BRD-67, BRD-70 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; quotable / not-quotable banner; Export card with the button and the dataset SHAs; past snapshots table with download links; parity record card.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Quotable banner | `Alert Variant=Success` "QUOTABLE — last parity run 2026-08-30 postdates parser v3" **or** `Alert Variant=Warning` "NOT QUOTABLE — parser changed after the last parity run; re-run §13" `data-testid="quotable-banner"` | parity vs parser version | — |
| Export card | `Card` → `CardTitle` "Weekly snapshot" → `CardContent` `Button` **Export snapshot** `data-testid="export-now"` + `TypographyMuted` "writes data/reports/<date>/snapshot.md + tflens.json" | — | `Spinner` while writing; toast with the folder |
| Dataset SHAs | `DataTable TData=RepoSha ShowToolbar=false ShowPagination=false` (repo · branch · SHA · synced) | from `sync_state` | copy button per SHA (`Button Size=IconSmall`) |
| Past snapshots | `DataTable TData=SnapshotRow` `data-testid="snapshots"` (date · parser version · parity status · snapshot.md · tflens.json links) | folders under `data/reports/` | empty → `Empty` "no snapshots yet" |
| Parity record | `Card` "Last parity run" → date, dataset SHAs, script hash, parser version, compare output (`pre`) | `data/parity-last.json` | none → `Alert Variant=Warning` "no parity run recorded" |

**Notes / interactions:** links open the files (served from `data/reports/` behind auth).

**Empty / loading / error:** covered above.

### Screen: Playbook (`/playbook`)

**Mockup:** [docs/mockups/playbook.html](./mockups/playbook.html) · **Role(s):** Owner · **BRD:** BRD-73..BRD-76 · **REQ:** (assigned by split-brd)

**Layout (one line):** shell; note that this page reads a different schema; per Playbook repo a `Card` with phase totals table and a main-vs-subagent split; empty state until Phase 3 lands.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Note | `Alert Variant=Info` | "Playbook process-gates (phase_gate) and TechieFlow assertion-gates (gate) are different axes and never share a chart (SCHEMA.md §11)." | always |
| Repo card ×N | `Card` (`CardTitle` owner/name, `Badge` "playbook", `Badge` SHA) | — | — |
| Phase totals | `DataTable TData=PhaseRow ShowToolbar=false` `data-testid="pb-phases-{name}"` (phase · events · tokens · cost (if present)) | grouped by phase | cost absent → `—` |
| Main vs subagent | 2× `Card` (KPI): Main-session tokens · Sub-agent tokens (via parentID) + `Progress` share bar | split | — |
| Schema discovery | `Collapsible` "Observed fields" → list of `Badge` | field names from overflow | — |
| Empty | `Empty` (`EmptyTitle` "No Playbook data yet", `EmptyDescription` "Phase 3 — adapter is built after the real events.ndjson is parsed") | — | default at day-1 |

**Notes / interactions:** deliberately minimal; shrinks when the Playbook converges on schema v1.

**Empty / loading / error:** covered above.

## Library gaps

Observed while designing (to be logged as `TR-NNN` in `docs/TfLens-TrBlazeUI-Feedback.md` at build if still true):

- No KPI / stat card component — composed from `Card` + Tailwind per the library's own documented pattern (acceptable, not blocking).
- No plain `Table` primitives — small key/value tables use `DataTable` with toolbar and pagination off.
- No theme toggle component — a `Switch` in the sidebar footer flips `class="dark"`.
- Chart API documented only as `TItem`/`Items`/`XValue`/`YValue` — no axis/legend control; charts are supplementary, every figure also has a text table.
