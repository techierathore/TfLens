# TfLens — Checklist

<!-- AGENT WORKING DOCUMENT — markdown only, never rendered to HTML.
     Produced by *split-brd on 2026-08-26 from docs/TfLens-BRD.md (BRD-1..BRD-111).
     No prior development/phase plan existed, so nothing is seeded as done —
     every REQ starts Not Started / 0%. Phase tags come from BRD §4. -->

## Table of Contents

1. [Goal](#goal)
2. [Requirements Status](#requirements-status)
3. [UI / Pages](#ui--pages)
   - [Page: Login (`/login`)](#page-login-login)
   - [Page: Register (`/register`)](#page-register-register)
   - [Page: Forgot password (`/forgot-password`)](#page-forgot-password-forgot-password)
   - [Page: Reset password (`/reset-password`)](#page-reset-password-reset-password)
   - [Page: Profile (`/profile`)](#page-profile-profile)
   - [Layout: App shell (sidebar + header)](#layout-app-shell-sidebar--header)
   - [Page: Repos (`/repos`)](#page-repos-repos)
   - [Page: Coverage / health (`/`)](#page-coverage--health-)
   - [Page: Three questions (`/three-questions`)](#page-three-questions-three-questions)
   - [Page: Harness comparison (`/harness`)](#page-harness-comparison-harness)
   - [Page: Routing & economics (`/routing`)](#page-routing--economics-routing)
   - [Page: Snapshot export (`/export`)](#page-snapshot-export-export)
   - [Pages: Playbook framework state (Phase 3)](#pages-playbook-framework-state-phase-3)
4. [Functional requirements](#functional-requirements)
5. [RAG / AI requirements (→ /techierag)](#rag--ai-requirements--techierag)
6. [Non-functional](#non-functional)

## Goal

Build TfLens: a free, multi-user, **read-only** Blazor Server lens over the telemetry TechieFlow and the AI-First-Playbook already emit (BRD §1). Users sign in through AppManager (App Id 1, every user `Manager`), connect their own **public** GitHub repos, and TfLens pulls the JSONL streams, archives them raw, parses them into PostgreSQL per user, and renders five report pages — Coverage, Three questions, Harness comparison, Routing & economics, Snapshot export — each with a **Framework switch** (TechieFlow | Playbook). The product's dangerous failure mode is a *plausible wrong number*, so the SCHEMA.md §6 provenance rules (live/backfilled · `project_type` · framework · user) are enforced in the shape of the result type with no switch to relax them, and a **mandatory parity diff against `tf-metrics.sh --rollup --json`** (BRD §13) is the acceptance gate before any figure is quotable. This single checklist is the whole app's work list — UI, functional and NFR requirements live together, distinguished only by their REQ prefix. Phase order is hard: **1 → 2 → 3**.

## Requirements Status

<!-- ============================================================
     SINGLE SOURCE OF TRUTH for the WHOLE app (UI + functional +
     RAG + NFR). Build, self-smoke, and the verifier ALL write
     their outcomes into THIS table — never into a separate dated
     results file. Update Status + % + Remarks every time work
     (implementation, smoke, or verify) touches the REQ. Bugs and
     change notes live in the Remarks column, NOT in docs/qa/*.md.
     ============================================================ -->

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-UI-001 | Login page (BRD-1, BRD-90, BRD-94, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Auth/Login.razor` + `AuthLayout.razor`; smoked: `/login` 200 anonymously with login-email/login-pass/login-submit, Enter submits, real sign-in as userId 2 lands authenticated; no GitHub button in the DOM (only the muted 'coming in a later release' line). | [view](#d-req-ui-001) |
| REQ-UI-002 | Register page (BRD-91, BRD-95, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Auth/Register.razor` with `PasswordStrengthMeter.razor`; local password rules render before any API call. | [view](#d-req-ui-002) |
| REQ-UI-003 | Forgot-password page (BRD-92, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Auth/ForgotPassword.razor` — enumeration-safe success alert replaces the form. | [view](#d-req-ui-003) |
| REQ-UI-004 | Reset-password page (BRD-92, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Auth/ResetPassword.razor` — reads `?token=`, never displays it. | [view](#d-req-ui-004) |
| REQ-UI-005 | Profile page (BRD-107, BRD-95, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Auth/Profile.razor`; profile-values 5/5 non-empty rows, Role badge reads Manager. Mobile overflow fixed (grid minmax(0,1fr) track + item min-width:0 + fixed table layout + shrinkable identity row); page container now 390=390 at 390px. ⚠ Its DataTable was the only one in the app with no `InitialPageSize` and had exactly 5 rows — one field from silent truncation; fixed and pinned by `DataTablePagingTests`. | [view](#d-req-ui-005) |
| REQ-UI-006 | Collapsible icon sidebar + nav order (BRD-5, BRD-105, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Layout/MainLayout.razor` + `Services/Ui/ShellNavigation.cs` — six items in the fixed order; no /playbook nav item; cookie renamed to `tflens-sidebar` so the collapsed state can actually reach the server. | [view](#d-req-ui-006) |
| REQ-UI-007 | Header Sync now + last-sync badge (BRD-6, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Shared/SyncNowButton.razor` in `ShellHeader.razor`. | [view](#d-req-ui-007) |
| REQ-UI-008 | Header user menu (BRD-4, BRD-106, Phase 1) | Implemented | 75% | 2026-08-27 Header user menu renders and the three items navigate. ⚠ Its Sign out item was wired to a dead route and did nothing — fixed 2026-08-27 (hidden antiforgery form posting `/auth/logout`) and covered by `SignOutTests`. Still the only sign-out control in the app. | [view](#d-req-ui-008) |
| REQ-UI-009 | Dark-first theme toggle, persisted (BRD-85, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Shared/ThemeToggle.razor` + `App.razor` first-byte resolution. Verified 2026-08-27: `tflens-theme=light` -> `class=""`, absent/dark -> `class="dark"`. Was silently broken until the cookie was renamed off `tflens:theme` (ASP.NET Core drops colon-named request cookies). | [view](#d-req-ui-009) |
| REQ-UI-010 | Header Framework switch (BRD-108, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Shared/FrameworkSwitch.razor` + `ShellPreferences.SyncFrameworkFromBrowserAsync`. Verified 2026-08-27: selecting Playbook persists `tflens-framework=playbook` and stays active after navigating to another report page. | [view](#d-req-ui-010) |
| REQ-UI-011 | Repos page — grid, KPIs, empty state (BRD-98, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Repos.razor`; smoked: repos-table 8/8 non-empty rows. Mobile toolbar overflow fixed 2026-08-27. | [view](#d-req-ui-011) |
| REQ-UI-012 | Connect-repo dialog with Validate (BRD-99, BRD-100, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Repos.razor` Connect dialog with Validate; registry proven live against GitHub (public repo detected as techieflow/main; a repo without telemetry refused). | [view](#d-req-ui-012) |
| REQ-UI-013 | Remove-repo confirm dialog (BRD-101, Phase 1) | Implemented | 75% | 2026-08-27 `Components/Pages/Repos.razor` Remove `AlertDialog`; purge proven at the store level. | [view](#d-req-ui-013) |
| REQ-UI-014 | Coverage page — badge, KPIs, repo cards, stream table (BRD-39, BRD-40, BRD-43, BRD-44, Phase 2) | Implemented | 75% | `Components/Pages/Coverage.razor` owns route `/` (scaffold `Home.razor` deleted). Smoked on the live app as user 2: `coverage-status` = "CHECK — 3 warnings", KPIs 2/2 · 69 gates (live 49 / backfilled 20) · 1 hour · 0 errors; one card per repo with short SHA linked to the GitHub commit and a 4-row `repo-streams-{name}` table with non-empty cells; Playbook axis renders the single `events` row. Needed a new store read — `ITelemetryStore.ReadCoverageFactsAsync` (see Remarks on REQ-FN-030). **Not smoked:** the no-repos → `/repos` redirect (no repo-less account to hand). Shell defect found: `ShellPreferences` reads the framework cookie from `IHttpContextAccessor`, which is null in an interactive circuit, so the switch never took effect — worked around inside this page via `Coverage.razor.js`; the fix belongs in REQ-UI-010. ⚠ status corrected 2026-08-27: `Built` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-ui-014) |
| REQ-UI-015 | Coverage staleness warning in words (BRD-41, Phase 2) | Implemented | 75% | `Alert Variant=Warning AccentBorder` renders verbatim "sessions/commits stale ≥ 7 days — this clone isn't pushing or lacks hooks; run update-framework.sh on it", naming only the streams actually stale; each stale row carries `Badge Variant=Destructive` "stale". Threshold is `TfLensOptions.StalenessDays` (default 7) and the sentence prints the configured value. Smoked with sessions 11 days / commits 9 days old. ⚠ status corrected 2026-08-27: `Built` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-ui-015) |
| REQ-UI-016 | Coverage unknown-fields + `v > 1` panel (BRD-42, Phase 2) | Implemented | 75% | `Collapsible` trigger "Fields observed that SCHEMA.md doesn't document (3)" listing names only as `Badge Variant=Outline`, grouped repo · stream; reads "none" when there are none (verified on the Playbook axis). Names come from `jsonb_object_keys("Overflow")` filtered against `StreamParser.IsDocumented`, so no field value or `Overflow` payload is ever rendered. `Alert Variant=Info` names the repo and stream carrying the `v = 2` record. ⚠ status corrected 2026-08-27: `Built` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-ui-016) |
| REQ-UI-017 | Coverage guarded Rebuild-from-raw button (BRD-21, Phase 2) | Implemented | 75% | `data-testid="rebuild"` opens an `AlertDialog` ("Drop and reparse" / Cancel); nothing runs until the confirm action. Drives `ITelemetryStore.RebuildAsync(userId)` — the same call the `rebuild` verb makes — shows a `Progress` while replaying and then the report. Smoked live: 9 files replayed, 200 records stored, 0 duplicates, 0 invalid lines, per-stream commits 28 · gates 69 · runs 36 · sessions 22, page refreshed in place. ⚠ status corrected 2026-08-27: `Built` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-ui-017) |
| REQ-UI-018 | Three questions — per-type tabs, no total, standing note (BRD-45, BRD-50, Phase 2) | Implemented | 75% | 2026-08-27 built ThreeQuestions.razor. Self-smoke on live DB (user 2, techieflow): tabs app/framework/library only — no "all" tab, no total row in the DOM; standing §6 Alert always visible; KPIs render live value + labelled backfilled line; n<3 renders `insufficient data (n=…)`. Desktop 1440x900 + mobile 390x844, body scrollWidth == viewport, no overlap. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-018) |
| REQ-UI-019 | Three questions — labelled backfilled column (BRD-46, Phase 2) | Implemented | 75% | 2026-08-27 backfilled value is a `Badge Variant=Secondary` "backfilled" secondary line under each live KPI and its own Backfilled count / Backfilled share columns; nothing sums live+backfilled. Note: it sits in StatTile's value slot, not its description slot — a Badge renders a `<div>` which the parser hoists out of the description `<p>`. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-019) |
| REQ-UI-020 | Gate catch distribution table (BRD-47, Phase 2) | Implemented | 75% | 2026-08-27 `gate-dist-{type}` DataTable renders all 8 rows in GateOrder (build, acceptance, render, visual, perf, standards, escaped, unattributed) with non-empty cells; escaped carries `Badge Variant=Destructive` "no gate caught it", perf carries `Badge Variant=Outline` "see coverage". Needed `InitialPageSize="32"` — DataTable still caps rows at InitialPageSize (default 5) with ShowPagination="false". Added a per-provenance footnote when failures name a gate outside GateOrder (the engine drops those rows). ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-020) |
| REQ-UI-021 | Tainted-REQ list (BRD-48, Phase 2) | Implemented | 75% | 2026-08-27 `taint-list` Collapsible, open by default, renders every REQ from AnalysisResult.TaintedReqs as `Badge Variant=Outline` (smoke saw REQ-UI-010..REQ-UI-019, 10 of 10, trigger reads "10 REQs excluded from the live first-pass rate"); reads "none" when empty. Rendered once outside the tabs — TaintedReqs is per-analysis, not per project_type, and the id must be unique. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-021) |
| REQ-UI-022 | Late-gate coverage lines (BRD-49, Phase 2) | Implemented | 75% | 2026-08-27 late-gate Card renders one line per MetricsConstants.LateGates entry; smoke saw "perf — not yet run on this data (gate added 2026-08-10)" (no record carries perf in gates_run). The "ran on N records, caught K → rate" branch reads Ran/Caught/CatchRate straight off LateGateCoverage — no distribution share is used as a catch rate. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-022) |
| REQ-UI-023 | Harness page — three harness columns (BRD-51, Phase 2) | Implemented | 75% | 2026-08-27 — `Components/Pages/Harness.razor` (+ `.razor.css`) owns route `/harness`. `Alert Variant=Info` `harness-note`, then three `Card` columns `harness-col-{claude-code ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. |opencode|codex}` in the fixed detected order, each holding an 11-row key/value `DataTable` (toolbar + pager off, `InitialPageSize=50` — see TR-009). Smoked on the live app as user 2 at 1440×900 and 390×844: column order asserted `claude-code, opencode, codex`; every column rendered 11/11 rows with **0 blank cells** (claude-code Runs 4 · build-phase 4 · 0 gates · Verdict mix `—` · 2 sessions · in 1,390 / out 417,386 / cache read 12,699,642 / cache write 836,444; opencode Runs 2 · 16 gates · FAIL 12 · Verified 4; codex Runs 2 · Implemented 2). The zero-record case renders `—` throughout — driven live by switching the header Framework switch to Playbook, where all three columns render with `—` rather than disappearing. Supplementary `BarChart` renders (16.8 KB of SVG, one bar per harness in `--chart-1..3`) with an `Empty` "No token data" state, and every charted value also appears as text in `tokens-table` (13,954,862 · 4,809,552 · 1,983,780). Columns side by side at desktop (all three tops = 283) and one per row at mobile; no sibling overlap, no zero-sized control, and the shell's page pane does not scroll horizontally at 390 (paneScrollWidth == clientWidth == 390). Library gaps hit and worked around: TR-005 (chart has no `XValue`/`YValue`), TR-009 (pager off still truncates to 5 rows), TR-012 (`DataTable` header cannot be hidden), TR-001 (`--chart-*` / `--alert-*` undefined — declared on the page root, otherwise the bars paint black). Deviation from the mockup: an 11th row, **Measured dollars**, was added per REQ-UI-025's acceptance. | [view](#d-req-ui-023) |
| REQ-UI-024 | Tokens per verified REQ per harness (BRD-52, Phase 2) | Implemented | 75% | 2026-08-27 — `Tokens per Verified REQ` row inside each column, `harness-{h}-tokens-per-verified`, rendered only through `FigureText` so the figure cannot be printed as a number when it refuses to be one. Live values on user 2/techieflow: opencode `26865.0` (1 dp, matching the reference), claude-code and codex `insufficient data (n=0)`; a later data state also produced codex `35825.0`. The figure is per harness — the engine computes it from that harness's own runs and gate verdicts — never pooled. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-024) |
| REQ-UI-025 | OpenCode-only dollars card, no cross-harness total (BRD-53, BRD-54, Phase 2) | Implemented | 75% | 2026-08-27 — `opencode-cost` card: `CardTitle` "Measured dollars (OpenCode only)", `Badge Variant=Secondary` "the only measured dollars in the system", `opencode-cost-value` `$0.84` with `opencode-cost-basis` "Σ cost_usd over 2 opencode runs", and `opencode-cost-note` "Claude Code and Codex: not measured (null by design)". With no OpenCode measurement the value reads "no OpenCode records yet" (smoked on the Playbook axis). **No cross-harness dollar total exists anywhere on the page**: the smoke walks every text node in the rendered DOM for `$`/`USD` figures and for total/combined/across-harness money wording — the only match on the whole page was `$0.84`, inside `opencode-cost`. The claude-code and codex columns carry a `Measured dollars` row reading "not measured (null by design)" rather than `0`; opencode's row points at the card instead of repeating the figure, and reads "no cost_usd captured yet" when nothing measured it, so it can never contradict the card. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-025) |
| REQ-UI-026 | `harness: null` footnote row (BRD-55, Phase 2) | Implemented | 75% | 2026-08-27 — `harness-null-footnote` under the columns, rendered live as "14 records with harness not detected — excluded from the columns above" and hidden (element absent) at n = 0 on the Playbook axis. Undetected records are never merged into a named column: the engine counts them across runs, gates and sessions and the page only ever renders the count. TR-013: `TypographyMuted` captures no unmatched attributes, so the test id sits on a wrapping `div`. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-026) |
| REQ-UI-027 | Routing drift tab + table (BRD-56, Phase 2) | Implemented | 75% | 2026-08-27 `Components/Pages/Routing.razor` — `Tabs` drift/models/repricing/poolable with `routing-tab-{key}` (on the trigger label span: TR-010, `TabsTrigger` captures no unmatched attributes), 3 `StatTile`s and `drift-table` in an `overflow-x-auto` box. Unrouted-first preserved from the service, routed remainder grouped by cmd; `routed:false` carries `Badge Variant=Destructive` "drift". Empty state "no routing fields captured yet". TR-009 guard: the row total is stated in the card description (`drift-row-count`) and the pager is asserted. Smoke: 6 of 12 runs carry routing fields, 2 unrouted (33%), all unrouted rows before every routed row, 0 blank cells, no overlap at 1440x900 / 390x844. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-027) |
| REQ-UI-028 | Tokens by observed model (BRD-57, Phase 2) | Implemented | 75% | 2026-08-27 `model-tokens` `DataTable` (model · in · out · cache read · cache write · total) with `InitialPageSize=100` (TR-009) in an `overflow-x-auto` box, plus a totals bar row whose every bar is labelled with the exact figure the table states. **`BarChart` was removed — TR-011: it renders an empty `<div>`, the ApexCharts runtime the package wraps is never loaded.** Smoke: 3 observed models (claude-opus-5 11,272,548 · anthropic/claude-opus-5 4,809,552 · claude-sonnet-4-6 2,682,314), 0 blank cells, 3 bars drawn 56x108/65/36 px. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-028) |
| REQ-UI-029 | Counterfactual repricing cards, labelled estimate (BRD-58, BRD-59, BRD-60, Phase 2) | Implemented | 75% | 2026-08-27 `repricing-actual` / `repricing-max` / `repricing-delta`, each with `Badge Variant=Outline` carrying `RateCard.EstimateLabel` verbatim and a muted "n runs excluded (tokens_scope none)" line — **the delta card carries the label too**, since it is also a rate-card figure. `MissingPriceModels` raises `Alert Variant=Warning` naming the model and saying its tokens are left out rather than priced at zero. `PooledMetrics.CostUsd` is never rendered. Smoke: actual $26.73 / max $28.62 / delta −$1.89 (6.6% saved against all-anthropic/claude-opus-5), 3 money nodes and 3 estimate labels at both widths; the unpriced-model warning was seen naming `claude-opus-4` on the earlier dataset. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-029) |
| REQ-UI-030 | Edit-prices dialog (BRD-61, Phase 2) | Implemented | 75% | 2026-08-27 `edit-prices` opens a `Dialog` with an editable `DataTable` (`InitialPageSize=100` — TR-009 would otherwise truncate to 5 rows and then save the truncated card back over prices.json). Observed models the card does not price are listed **blank** with a "observed · not priced" badge, never as zero; a wholly blank row is skipped on save. Negative or blank rates raise `FieldError` and disable Save. **Shared change: added `RateCard.SaveAsync` to `src/TfLens.Core/Metrics/RateCard.cs`** — it rewrites the note/units/estimate_only/estimate_label banner every time and refuses a negative rate. Smoke: −5 blocked, blank cell blocked, then claude-opus-5 repriced 5→25 → actual $26.73→$122.35, max $28.62→$143.11; a second edit 25→250 → $123.03. Cancel writes nothing. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-030) |
| REQ-UI-031 | Poolable-metrics cards (BRD-62, Phase 2) | Implemented | 75% | 2026-08-27 five `StatTile`s bound straight to `AnalysisResult.Pooled` and rendered through `FigureText`, so `insufficient data (n=…)` cannot become a number. Commit-cadence sub-line states the collapsed duplicates. Smoke: rework 50% · batch 1.5 · throughput 4.53 · tokens/Verified 72095.7 · cadence 5 (0 duplicate shas collapsed); 5-up at desktop, stacked at 390px. ⚠ status corrected 2026-08-27 by the orchestrator: a self-smoke's ceiling is `Implemented`; `Verified` requires an executed verify-phase run (docs/.last-verify.json), which has not happened yet. | [view](#d-req-ui-031) |
| REQ-UI-032 | Export page — button, dataset SHAs, past snapshots (BRD-63, BRD-70, Phase 2) | Implemented | 75% | 2026-08-27 `Components/Pages/Export.razor` + `Export/ExportSurface.razor`; export actually wrote data/reports/2/<date>/{techieflow,playbook}/snapshot.md + tflens.json and the dataset-SHA table matched SyncState row-for-row. | [view](#d-req-ui-032) |
| REQ-UI-033 | Quotable banner + last-parity card (BRD-67, BRD-71, Phase 2) | Implemented | 75% | 2026-08-27 `Export/ExportSurface.razor` quotable banner + parity card; renders the honest NOT QUOTABLE / never-run state (no parity run exists). | [view](#d-req-ui-033) |
| REQ-UI-034 | Playbook state of the five report pages (BRD-75, BRD-108, BRD-110, Phase 3) | PARTIAL | 50% | 2026-08-27 Playbook state built as reusable components (`Components/Shared/Playbook/`) and wired end-to-end on `/export` only; the Playbook state of `/`, `/three-questions`, `/harness` and `/routing` is not wired. Populated `pb-phases-*` rendering unproven — PbEvent was emptied by a concurrent cluster during the smoke. | [view](#d-req-ui-034) |
| REQ-FN-001 | AppManagerClient — public-key cache, RSA-OAEP-256, login (BRD-90, Phase 1) | Implemented | 75% | 2026-08-26 — `src/TfLens.Core/AppManager/AppManagerClient.cs`: typed HttpClient, thread-safe cached public key (refetched once on `DECRYPTION_FAILED`), RSA-OAEP-**SHA256** on every password field, `applicationId` in every body, error `code` -> `AppManagerException.Code`. Verified live: `INVALID_CREDENTIALS` (401), `EXPIRED_REFRESH_TOKEN` (401) returned by the real API; codes are logged, never returned to the browser (`AuthEndpoints` maps them to opaque reason tokens). 27 offline + 6 live xUnit tests green. | [view](#d-req-fn-001) |
| REQ-FN-002 | Registration with `applicationRoleCode: "Manager"` (BRD-91, Phase 1) | Implemented | 75% | 2026-08-26 — `RegisterAsync` posts `applicationRoleCode: "Manager"` as a constant with no caller override; `PasswordRules` (8+/upper/digit/special) rejects locally before any HTTP call (4 theory cases assert no request is made). Registration reuses the same `IssueSessionAsync` path as sign-in. **Observation:** AppManager returned `applicationRole: ""` (empty) for user 2 on the live login — Application 1 appears to define no `Manager` role code. TfLens ignores the server role and always issues the constant `"Manager"` (BRD-95), so behaviour is unaffected. | [view](#d-req-fn-002) |
| REQ-FN-003 | Forgot / reset password via AppManager (BRD-92, Phase 1) | PARTIAL | 50% | 2026-08-26 — client + `/auth/forgot-password` and `/auth/reset-password` endpoints written; `ForgotPasswordAsync` swallows every error so known and unknown addresses are indistinguishable, and `INVALID_RESET_TOKEN` / `APP_ID_MISMATCH` both collapse to the single `error=expired` reason. **Missing:** cannot be exercised end to end — verified live that AppManager answers `400 APPLICATION_ID_REQUIRED` for both endpoints unless `X-Api-Key`/`X-Api-Secret` are configured (body `applicationId`, `?aApplicationId`, `?applicationId` and `X-App-Id` were all tried and all refused). Needs the owner to supply `TfLensAppManagerApiKey`/`TfLensAppManagerApiSecret`. | [view](#d-req-fn-003) |
| REQ-FN-004 | Session — own cookie, server-side tokens, refresh/validate/logout (BRD-93, Phase 1) | Implemented | 75% | 2026-08-26 — `Services/Auth/AuthService.cs` + `AuthSessionStore.cs`. Random 256-bit session id; cookie claims are `tflens:sid`, `tflens:uid`, `ClaimTypes.Email/Name/Role=Manager` (the shape `CurrentUser` reads). Tokens stay in `"AuthSession"`, encrypted with Data Protection purpose `TfLens.AuthSession`. Refresh inside 5 min of `TokenExpiresAt` rotates the stored refresh token; a refresh failure deletes the row and signs out. Validate at most hourly. **Live smoke (AppManager + Postgres:5433) all green:** cookie contains neither token, stored columns differ from plaintext (`CfDJ8...`), round-trip decrypts, refresh rotates, logout revokes (reuse -> `EXPIRED_REFRESH_TOKEN`) and deletes the row. | [view](#d-req-fn-004) |
| REQ-FN-005 | Cookie auth gate on every non-anonymous page (BRD-2, Phase 1) | Implemented | 75% | 2026-08-26 — implemented as a **fallback policy** post-configured in `AddTfLensAuth` (Program.cs untouched), so a new page is protected unless it is listed in `AnonymousRoutes`. Exceptions are the five BRD-2 routes plus the `/auth/*` posts and the Blazor runtime prefixes (`/_blazor`, `/_framework`, `/_content`) without which an anonymous page cannot be interactive. `ReturnUrlParameter` set to `returnUrl`. | [view](#d-req-fn-005) |
| REQ-FN-006 | Sign-in redirect and landing rules (BRD-1, Phase 1) | Implemented | 75% | 2026-08-26 — `AuthEndpoints.LandingUrlAsync`: validated local return URL first, else `/repos` when `ReadUserReposAsync` returns none, else `/`. `LocalReturnUrl` rejects `//host` and `/\host`, so there is no open redirect. The store is resolved optionally so the auth area works before the storage area registers. | [view](#d-req-fn-006) |
| REQ-FN-007 | Sign out — AppManager logout + cookie clear (BRD-4, Phase 1) | Implemented | 75% | 2026-08-27 ⚠ WAS BROKEN, now fixed and pinned. The user menu navigated to `GET /signout`, which nothing mapped (only `POST /auth/logout` exists) — the cookie survived and the user stayed signed in. Sign-out now posts the antiforgery-protected form; verified live: cookie cleared, redirect to /login, /repos bounces to login. Regression pinned by `SignOutTests` (2 integration tests). | [view](#d-req-fn-007) |
| REQ-FN-008 | Manager-only; never call LicenseSvc/FeatureSvc/PaymentSvc/IssueSvc (BRD-95, Phase 1) | Implemented | 75% | 2026-08-26 — no LicenseSvc/FeatureSvc/PaymentSvc/IssueSvc path anywhere. Enforced by `tests/TfLens.Core.Tests/AppManager/ForbiddenServiceTests.cs`, which scans every `.cs` file under `src/` for `/LicenseSvc`, `/FeatureSvc`, `/PaymentSvc`, `/IssueSvc` and asserts the only role code sent is `Manager` — green over the whole tree. | [view](#d-req-fn-008) |
| REQ-FN-009 | Demo user `TfLensDemo` (BRD-96, Phase 1) | Implemented | 75% | 2026-08-26 — account confirmed live: `tflensdemo@techierathore.com` is AppManager **userId 2** and signs in, refreshes, validates, reads its profile and logs out through the real client. UsageGuide test user #1 row is `Created? ✅`. No demo seed exists in `appsettings*.json`. **Remaining for Repos cluster:** the demo repos must be connected through the Repos screen, and `TfLensOptions.DemoSeedRepos` (still present) is the configuration seed BRD-96 forbids — it should be removed or left permanently empty. | [view](#d-req-fn-009) |
| REQ-FN-010 | AppManager connection from configuration only (BRD-97, Phase 1) | Implemented | 75% | 2026-08-26 — `AddTfLensAuth` reads only `TfLensOptions` (base URL default `https://appmgrapi.techierathore.com`, app id default 1); the key/secret are never read there — `AppManagerClient` asks `HasAppManagerApiCredentials` per request and sends the pair whole or not at all. Verified live that a bogus pair returns `401 INVALID_API_KEY` on every call, which is why `TfLensOptions.Validate()` refuses a half pair at startup. No secret value appears in any committed file. | [view](#d-req-fn-010) |
| REQ-FN-011 | Profile read + change password via UserSvc (BRD-107, Phase 1) | Implemented | 75% | 2026-08-26 — `GetProfileAsync` reads `GET /UserSvc/profile` with the bearer token (live data, not cookie claims) and `ChangePasswordAsync` posts `encryptedCurrentPassword` + `encryptedNewPassword`, both RSA-OAEP-256 with the cached key. Live: profile returns userId 2 / `createdDate`; a wrong current password returns `INVALID_CURRENT_PASSWORD` and changes nothing (re-login afterwards still succeeds). `/profile` page itself is the UI wave. | [view](#d-req-fn-011) |
| REQ-FN-012 | GitHub SSO — deferred to Phase 2 (BRD-94) | N/A | 0% | Deferred by BRD-94 / ADR-012 — AppManager v1.4 has no external-login or token-exchange endpoint. Do NOT build in this release; login screen shows no GitHub button. | [view](#d-req-fn-012) |
| REQ-FN-013 | Per-user repo list read with counts (BRD-98, Phase 1) | Implemented | 75% | 2026-08-26 `RepoRegistry.ListAsync` + `IRepoListReader.ListWithCountsAsync` (joins `"SyncState"`); `RepoListItem` carries RecordCount/Status/LastSync/LastSha. Live smoke printed `records=1 status=synced sha=smokesha`. Awaiting REQ-UI-011 for the render gate. | [view](#d-req-fn-013) |
| REQ-FN-014 | Connect validation — exists, public, telemetry path, kind (BRD-99, Phase 1) | Implemented | 75% | 2026-08-26 `RepoRegistry.ValidateAsync`/`ConnectAsync` + `RepoInputParser` (URL/`owner/name`, `.git`, trailing slash, `www.`, query). Three checks reported separately in `RepoValidation`; kind auto-detected `docs/metrics`→techieflow, `verification/telemetry`→playbook. Live smoke: TrBlazeUI→`docs/metrics`/techieflow/main; octocat/Hello-World refused (no telemetry). Kind override implemented as a 4-arg overload on the concrete `RepoRegistry` (narrows the probe, refuses when unsatisfiable, rejects an unknown kind) — **the shared `IRepoRegistry` still carries no kind parameter**, so REQ-UI-012's Kind select must inject `RepoRegistry` or the contract must gain `string? aKind`. | [view](#d-req-fn-014) |
| REQ-FN-015 | Refuse private repos; optional PAT raises rate limit only (BRD-100, Phase 1) | Implemented | 75% | 2026-08-26 `RepoRegistry.PrivateRepoMessage` = "Private repos aren't supported in this release"; `private == true` refused before any contents probe. Test `ServerTokenNeverReachesAPrivateRepo` asserts a configured PAT does not let a private repo through and that no path probe is issued. No per-user token is accepted or stored anywhere. | [view](#d-req-fn-015) |
| REQ-FN-016 | Remove repo = stop sync + purge rows and raw (BRD-101, Phase 1) | Implemented | 75% | 2026-08-26 `RepoRegistry.RemoveAsync` → `ITelemetryStore.DeleteRepoDataAsync(userId, repo)` + `Directory.Delete({DataRoot}/raw/{userId}/{owner}__{name})`. Live smoke: user 2 Run rows 1→0, SyncState 1→0, archive dir True→False, user 3's copy untouched (Run rows still 1). Sync stops because the `"UserRepo"` row is gone — the poller's work list is `ReadAllUserReposAsync`. | [view](#d-req-fn-016) |
| REQ-FN-017 | `UserId` scoping on every read, write, cache and path (BRD-102, Phase 1) | Implemented | 75% | 2026-08-26 Registry side done: every `RepoRegistry` method takes `aUserId`, there is no overload without it, and it flows into every store call, the duplicate check and `TfLensOptions.RawPath(userId)`. Tests `ListNeverCrossesUsers`, `RemoveCannotReachAnotherUsersRepo`, `ValidateDoesNotReportAnotherUsersRepoAsConnected` + live smoke (f): user 2 naming user 3's repo gets "is not connected to your account" — the same answer as for a repo nobody has. Engine/cache-key half is Phase 2 (REQ-FN-046). | [view](#d-req-fn-017) |
| REQ-FN-018 | Poller syncs all users; Sync now syncs only the caller's repos (BRD-103, Phase 1) | PARTIAL | 25% | 2026-08-26 Registry half only: it never polls, and the shapes the poller and Sync now need already line up — poller work list = `ITelemetryStore.ReadAllUserReposAsync()`, Sync now = `IRepoSyncRunner.SyncAsync(userId)`, per-user list = `IRepoRegistry.ListAsync(userId)`. **Missing:** the `BackgroundService` poller, the Sync-now handler and per-user error isolation — all Cluster C (REQ-FN-020..023). | [view](#d-req-fn-018) |
| REQ-FN-019 | Duplicate `owner/name` rejected per user, allowed across users (BRD-104, Phase 1) | Implemented | 75% | 2026-08-26 `RepoValidation.AlreadyConnected` set from a `userId`-scoped read; `ConnectAsync` throws "…is already connected to your account." and writes no row. Live smoke: user 2 connect OK → second connect REFUSED (still 1 row) → user 3 connect OK (2 independent rows, own raw dirs). | [view](#d-req-fn-019) |
| REQ-FN-020 | `BackgroundService` poll on the configured interval (BRD-12, Phase 1) | Implemented | 75% | 2026-08-27 `Services/Sync/RepoSyncService.cs` — BackgroundService + PeriodicTimer; log shows 'Repository poller started; interval 15 minutes'. | [view](#d-req-fn-020) |
| REQ-FN-021 | Latest-SHA lookup and skip-when-unchanged (BRD-13, Phase 1) | Implemented | 75% | 2026-08-27 `Core/GitHub/GitHubStreamFetcher.LatestShaAsync` + SHA-skip in `RepoSyncRunner`. | [view](#d-req-fn-021) |
| REQ-FN-022 | Whole-file fetch at the exact SHA; 404 = stream absent (BRD-14, Phase 1) | Implemented | 75% | 2026-08-27 `GitHubStreamFetcher.FetchFileAsync` — whole-file fetch at the exact SHA; 404 returns null. | [view](#d-req-fn-022) |
| REQ-FN-023 | Per-repo error isolation with redacted `LastError` (BRD-15, Phase 1) | Implemented | 75% | 2026-08-27 `Services/Sync/SyncErrorRedactor.cs` + per-repo isolation in `RepoSyncRunner`. | [view](#d-req-fn-023) |
| REQ-FN-024 | Structurally read-only against GitHub — GET only (BRD-16, Phase 1) | Implemented | 75% | 2026-08-27 `GitHubStreamFetcher` is GET-only; asserted by `tests/TfLens.Core.Tests/GitHub`. | [view](#d-req-fn-024) |
| REQ-FN-025 | `sync_state` update per user and repo (BRD-17, Phase 1) | Implemented | 75% | 2026-08-27 `RepoSyncRunner` writes SyncState per user and repo. | [view](#d-req-fn-025) |
| REQ-FN-026 | Analysis cache invalidation after sync or rebuild (BRD-18, Phase 1) | Implemented | 75% | 2026-08-27 `Services/Sync/AnalysisCacheInvalidator.cs` + `Core/Metrics/MemoryAnalysisCache.cs`. | [view](#d-req-fn-026) |
| REQ-FN-027 | Raw archive written verbatim before parsing (BRD-19, Phase 1) | Implemented | 75% | 2026-08-27 `RepoSyncRunner` writes the raw archive before parsing. | [view](#d-req-fn-027) |
| REQ-FN-028 | `rebuild` command verb (BRD-20, Phase 1) | Implemented | 75% | 2026-08-27 `Services/Commands/CommandRunner.RunRebuildAsync` + `PostgresStore.RebuildAsync`; a real rebuild replayed 9 raw files / 200 rows via the Coverage page, which calls the same method as the verb. | [view](#d-req-fn-028) |
| REQ-FN-029 | Rebuild report and count-identity with live sync (BRD-22, Phase 1) | Implemented | 75% | 2026-08-27 Rebuild report and count-identity covered by `tests/TfLens.Core.Tests/Storage`. | [view](#d-req-fn-029) |
| REQ-FN-030 | Stream tables + `SyncState` with SCHEMA.md-exact columns (BRD-23, Phase 1) | Implemented | 100% | 2026-08-26: `PostgresStore` (Dapper/Npgsql, every identifier double-quoted, `INSERT … ON CONFLICT DO NOTHING`); the SCHEMA.md→column mapping table is the class doc on `StreamParser`. Smoked against the live DB: 74 rows across 2 users × 2 repos. | [view](#d-req-fn-030) |
| REQ-FN-031 | JSON `Overflow` column for unknown fields and `v > 1` (BRD-24, Phase 1) | Implemented | 100% | 2026-08-26: unmapped properties and the whole body of any `v > 1` record go to `Overflow` (jsonb); distinct unknown names land in `ParseResult.UnknownFields`, `v > 1` in `RecordsAboveSchemaV1`. Smoke read back `{"routed_reason": "tier-unavailable"}`. | [view](#d-req-fn-031) |
| REQ-FN-032 | Count and skip invalid JSON lines (BRD-25, Phase 1) | Implemented | 100% | 2026-08-26: ports `read_stream` — a malformed line increments `InvalidLines` and is skipped, never fatal. All four fixture streams carry one; rebuild reported `invalidLines=4` over 8 files. | [view](#d-req-fn-032) |
| REQ-FN-033 | Dedupe `commits` on `sha` per repo (BRD-26, Phase 1) | Implemented | 100% | 2026-08-26: `Dedupe.Commits`, first wins, keyed `(UserId, Repo, Sha)` to match `UcCommitUserRepoSha`; sha `a1b2c3d` in two fixture repos keeps both rows. A record with no `sha` is kept, as the reference does. | [view](#d-req-fn-033) |
| REQ-FN-034 | Dedupe `sessions` — highest `output_tokens`, tie latest `ts` (BRD-27, Phase 1) | Implemented | 100% | 2026-08-26: `Dedupe.Sessions` keeps the largest OpenCode cumulative snapshot; proven order-independent by test. 7 fixture session records collapse to 4. | [view](#d-req-fn-034) |
| REQ-FN-035 | Dedupe `runs` and `gates` on their natural keys (BRD-28, Phase 1) | Implemented | 100% | 2026-08-26: `ts+app+cmd` and `ts+app+req_id+run_id`, matching `UcRunIdentity` / `UcGateIdentity`. Storing the same fixtures twice wrote **0** rows the second time. | [view](#d-req-fn-035) |
| REQ-FN-036 | Preserve provenance fields verbatim; absent optionals `NULL` (BRD-29, Phase 1) | Implemented | 100% | 2026-08-26: absent optionals stay `null` and are never coerced; round trip through Postgres printed `FilesWritten=0 TokensIn=NULL CostUsd=NULL Routed=NULL` on the same record. | [view](#d-req-fn-036) |
| REQ-FN-037 | Secrets only from the PascalCase env-var provider (BRD-8, Phase 1) | Implemented | 75% | 2026-08-27 `Configuration/PascalCaseEnvironmentConfigurationSource.cs`; guardrail test asserts no `Environment.GetEnvironmentVariable` call. | [view](#d-req-fn-037) |
| REQ-FN-038 | Refuse to start on missing secret or unreachable DB (BRD-9, Phase 1) | Implemented | 75% | 2026-08-27 `TfLensOptions.Validate()` + the startup DB ping in `Program.cs`. Note the recorded decision (DECISIONS.md D-006): DbConnection is unconditionally required, the AppManager key/secret pair whole-or-not-at-all. | [view](#d-req-fn-038) |
| REQ-FN-039 | `DataRoot` override (BRD-11, Phase 1) | Implemented | 75% | 2026-08-27 `TfLensOptions.DataRoot` + `RawPath`/`ReportsPath`. | [view](#d-req-fn-039) |
| REQ-FN-040 | One multi-stage Docker image with `data/` + `logs/` volumes (BRD-77, Phase 1) | Implemented | 75% | 2026-08-27 `Dockerfile` — multi-stage, data/ + logs/ volumes. VERIFIED by actually building and running it: `docker build` succeeds, `docker compose up -d` brings up both services healthy, /healthz returns database up on :8080, anonymous / redirects to /login, and `docker exec tflens dotnet TfLens.dll rebuild` runs. Added `libgssapi-krb5-2` — Npgsql probes for GSSAPI and without it every boot printed a spurious error line. | [view](#d-req-fn-040) |
| REQ-FN-041 | `/healthz` anonymous endpoint (BRD-78, Phase 1) | Implemented | 75% | 2026-08-27 `Program.cs` /healthz; verified live: {"status":"ok","database":"up"} anonymously. | [view](#d-req-fn-041) |
| REQ-FN-042 | README with the out-of-scope list verbatim (BRD-79, Phase 1) | Implemented | 75% | 2026-08-27 `README.md` at the repo root. | [view](#d-req-fn-042) |
| REQ-FN-043 | `DECISIONS.md` (BRD-80, Phase 1) | Implemented | 75% | 2026-08-27 `DECISIONS.md` at the repo root (D-001..D-011, plus the parity and schema-discovery sections). | [view](#d-req-fn-043) |
| REQ-FN-044 | `sync` command verb (BRD-81, Phase 1) | Implemented | 75% | 2026-08-27 `CommandRunner.RunSyncAsync` — the `sync` verb. | [view](#d-req-fn-044) |
| REQ-FN-045 | `docker compose` with PostgreSQL 16 + idempotent schema script (BRD-111, Phase 1) | Implemented | 75% | 2026-08-27 `docker-compose.yml` + `docker-compose.override.yml` + `database/001-schema.sql`; PostgreSQL 16.15 running and the idempotent script applied cleanly. | [view](#d-req-fn-045) |
| REQ-FN-046 | Figures computed at request time; nothing derived written back (BRD-30, Phase 2) | Implemented | 90% | 2026-08-26 `MetricsEngine.AnalyseAsync` computes every figure from the stream tables at request time; no engine path writes to any stream table. Memoised by `CachingMetricsEngine` + `MemoryAnalysisCache` (`IMemoryCache`) on `(userId, framework, syncVersion)` where syncVersion is built from `SyncState.LastSha`/`LastSyncTs`; `IAnalysisCache.Invalidate(aUserId)` / `InvalidateAll()` is the hook REQ-FN-026 calls. Tests `SecondRequestIsServedFromMemory`, `InvalidateForcesARecompute`, `InvalidateTouchesOnlyTheNamedUser`. Smoke (E): engine resolved from the real DI graph against PostgreSQL 16 returned `CachingMetricsEngine` and the full figure set. | [view](#d-req-fn-046) |
| REQ-FN-047 | Live and backfilled never pool (BRD-31, Phase 2) | Implemented | 90% | 2026-08-26 `AnalysisResult.Live`/`.Backfilled` are two dictionaries with no `Total` slot and no merge path; `Provenance` is an enum selecting rules inside a bucket, never merging two. Test `LiveAndBackfilledNeverPool`; parity test confirms the two blocks match tf-metrics.sh separately. | [view](#d-req-fn-047) |
| REQ-FN-048 | Never pool across `project_type`; inferred → `unclassified` (BRD-32, Phase 2) | Implemented | 90% | 2026-08-26 `Segment.ByProjectType` / `Segment.KeyFor` port `seg()`: `project_type_inferred: true` → `unclassified`, declared type otherwise, `app` only when absent. No 'all types' aggregation exists. Tests `InferredProjectTypeSegmentsAsUnclassified`, `InferredAndDeclaredRecordsNeverShareABucket`, `InferredProjectTypeLandsInUnclassified`; parity fixture `beta` is an inferred repo and lands under `unclassified` in both implementations. | [view](#d-req-fn-048) |
| REQ-FN-049 | Backfill-taint exclusion and excluded-REQ list (BRD-33, Phase 2) | Implemented | 90% | 2026-08-26 `TaintSet.FromBackfilled` collects every REQ with a backfilled gate record; the live segment drops them from BOTH the numerator and the denominator, counts them in `ReqsExcludedBackfillTaint`, and lists them in `AnalysisResult.TaintedReqs`. Test `TaintedReqLeavesBothSidesOfTheLiveFirstPassRateAndIsListed`; parity: excluded set `REQ-FN-008, REQ-FN-020, REQ-FN-021` matches the oracle exactly. | [view](#d-req-fn-049) |
| REQ-FN-050 | `Figure` type with `InsufficientData(n)` below min n = 3 (BRD-34, Phase 2) | Implemented | 90% | 2026-08-26 Every figure below `MetricsConstants.MinN` = 3 is `Figure.InsufficientData(n)`; `Figure.Value` throws below the floor so no code path can leak a number. `MinN` is a `const` with no configuration key. Tests `TwoRecordSegmentYieldsInsufficientDataNotANumber`, `ValueBelowTheFloorCannotBeConstructed`, `PercentOnAZeroDenominatorIsAnEmDash`. Deliberate stricter-than-reference deviation: commit cadence also honours the floor (the reference prints it from any non-zero day count) — see Notes in `Pooled.Cadence`. | [view](#d-req-fn-050) |
| REQ-FN-051 | `Pooled.CostUsd` always null (BRD-35, Phase 2) | Implemented | 90% | 2026-08-26 `PooledMetrics.CostUsd` is a computed `=> null` with no setter; `Pooled.Compute` never touches it. Tests `PooledCostIsAlwaysNull` (asserts the property is not writable) and the parity comparison, which fails if either side is non-null. | [view](#d-req-fn-051) |
| REQ-FN-052 | Late-added gates reported as `ran` beside `caught` (BRD-36, Phase 2) | Implemented | 90% | 2026-08-26 `LateGateCoverageCalculator` reports `ran` (records whose `gates_run` contains the gate, parsed by `GatesRun.Contains`) beside `caught`, and derives `CatchRate` from `ran` only — never from the distribution total. `LATE_GATES = {perf: 2026-08-10}` mirrored in `MetricsConstants.LateGates`. Tests `RanCountsGatesRunMembershipNotFailures`, `CatchRateIsNeverAShareOfTheDistribution`, `GateThatNeverRanIsNotApplicableRatherThanZero`, `GateRunTooFewTimesRefusesARate`. Parity: perf ran 3 / caught 1 in the live `app` segment on both sides. | [view](#d-req-fn-052) |
| REQ-FN-053 | Poolable metrics with the reference's formulas and rounding (BRD-37, Phase 2) | Implemented | 90% | 2026-08-26 `Pooled.Compute` ports the reference's formulas and rounding: rework `%.0f%%`, throughput median REQs/hour 2 dp (ToEven, matching Python `round`), batch median unrounded, tokens per Verified 1 dp, cadence 2 dp, tokens = Σ(input+output). Tests in `PooledMetricsTests`. Parity on the fixture set: rework 50%, throughput 5.0, batch 3.5, tokens 238000, tokens/Verified 17000.0, cadence 1.5 — digit for digit with the oracle. | [view](#d-req-fn-053) |
| REQ-FN-054 | Fixture-driven engine parity unit test (BRD-38, Phase 2) | Implemented | 95% | 2026-08-26 `tests/TfLens.Core.Tests/Fixtures/Engine/reference.json` was produced by RUNNING the oracle (`make-reference.sh` → `tf-metrics.sh --rollup alpha beta gamma --json`), never hand-written. `MetricsEngineParityTests.EngineMatchesReferenceJsonKeyForKey` compares every shared key; `ReferenceKeysWithNoTfLensCounterpartAreNamed` and `TfLensKeysBeyondTheReferenceAreNamed` name the non-shared keys explicitly (`per_repo.commit_hook` reference-only; `UserId`/`Framework`/`ParserVersion`/`Events`/`CatchRate`/`Share` TfLens-only) so nothing is skipped silently. Fixtures cover live+backfilled, inferred type, taint, a second project type, a late gate and a cross-repo duplicate SHA. 43/43 tests green. | [view](#d-req-fn-054) |
| REQ-FN-055 | Framework as a stored provenance axis, never pooled (BRD-108, Phase 2) | Implemented | 85% | 2026-08-26 `IMetricsEngine.AnalyseAsync(aUserId, aFramework)` has no framework-less overload; every store read passes it, `PerRepo` is filtered to the framework, and the cache key includes it. Tests `FrameworksNeverPool`, `FrameworksGetSeparateEntries`. Connect-time assignment of `UserRepo.Framework` is the Repos area's half. | [view](#d-req-fn-055) |
| REQ-FN-056 | Snapshot writer — `snapshot.md` + `tflens.json` per user and date (BRD-63, Phase 2) | Implemented | 75% | 2026-08-27 `Core/Export/SnapshotExporter.cs` — snapshot.md + tflens.json per user, framework and date; real files written. | [view](#d-req-fn-056) |
| REQ-FN-057 | `export` command verb (BRD-64, Phase 2) | Implemented | 75% | 2026-08-27 `CommandRunner.RunExportAsync` — the `export` verb. | [view](#d-req-fn-057) |
| REQ-FN-058 | `tflens.json` key layout matches `--rollup --json` + extras/parity (BRD-65, Phase 2) | Implemented | 75% | 2026-08-27 `Core/Export/SnapshotJson.cs` — key layout mirrors --rollup --json plus extras/parity. | [view](#d-req-fn-058) |
| REQ-FN-059 | Export never mixes provenances; every estimate labelled (BRD-66, Phase 2) | Implemented | 75% | 2026-08-27 Export carries the segmented shape; every repricing figure carries `RateCard.EstimateLabel`. | [view](#d-req-fn-059) |
| REQ-FN-060 | Parser version stamped into build and every export (BRD-68, Phase 2) | Implemented | 75% | 2026-08-27 `ParserVersion.Current` stamped into the build and every export. | [view](#d-req-fn-060) |
| REQ-FN-061 | `tools/parity-compare.py` key-by-key diff (BRD-69, Phase 2) | Implemented | 75% | 2026-08-27 `tools/parity-compare.py` — key-by-key diff, non-zero exit on any difference. | [view](#d-req-fn-061) |
| REQ-FN-062 | Dataset SHAs readable from export and Coverage (BRD-70, Phase 2) | Implemented | 75% | 2026-08-27 Dataset SHAs read from SyncState into the export and the Coverage page. | [view](#d-req-fn-062) |
| REQ-FN-063 | `data/parity-last.json` + DECISIONS.md record of each pass (BRD-71, Phase 2) | Implemented | 75% | 2026-08-27 `Core/Export/ParityRecord.cs` + DECISIONS.md §6. No parity run has been recorded yet, so the export correctly reports NOT QUOTABLE. | [view](#d-req-fn-063) |
| REQ-FN-064 | Hand spot-check of the no-oracle extras, recorded (BRD-72, Phase 2) | Implemented | 75% | 2026-08-27 DECISIONS.md §8 X-001 — the repricing hand spot-check, which found and corrected two real defects (per-run rounding in the code; a dropped row in the test's expectation). Verified figures: actual $5.08 / max $6.85 / delta $1.77. | [view](#d-req-fn-064) |
| REQ-FN-065 | `events.ndjson` fetch, archive and `PbEvent` tables (BRD-73, Phase 3) | Implemented | 75% | PlaybookAdapter: fetch -> raw archive (before parse) -> schema probe -> StreamParser -> "PbEvent". Reuses IGitHubStreamFetcher + ITelemetryStore; PbEvent physically separate from the four TF tables; unknown fields overflow. Tests: PlaybookAdapterTests (7). Smoke: 7 rows into live Postgres, re-ingest wrote 0 (upsert, no duplication). ⚠ status corrected 2026-08-27: `Done` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-fn-065) |
| REQ-FN-066 | `phase_gate` never shares a table, column or chart with `gate` (BRD-74, Phase 3) | Implemented | 75% | Structural, not conventional: PhaseGateKey struct (no string conversion) keys every Playbook gate member; TF gate members stay string. "PbEvent" has no Gate column, "Gate" has no PhaseGate column. Tests: PlaybookAxisSeparationTests (7) - reflection over both result graphs, DDL scan, and a source scan proving no SQL joins the two tables. ⚠ status corrected 2026-08-27: `Done` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-fn-066) |
| REQ-FN-067 | Playbook-native report data — phase totals, parentID split (BRD-75, Phase 3) | PARTIAL | 70% | Phase totals per phase_gate, main-vs-subagent split via parentID (chain-resolved, joiner fallback mirrored) and tokens-by-model all computed and smoke-verified. The three questions are NOT computable: events.ndjson carries no verdict field at all (DECISIONS.md S-001) - they render as em dash with the reason. Needs the joiner output (BRD-73 "if committed"). | [view](#d-req-fn-067) |
| REQ-FN-068 | Schema discovery recorded in DECISIONS.md before columns are fixed (BRD-76, Phase 3) | Implemented | 75% | No events.ndjson exists anywhere reachable (machine + web + the owner's own AI-First-Playbook repo tree). Recorded in DECISIONS.md S-001 with everything searched, BEFORE the columns changed. Columns then fixed from the authoritative emitter source (harness/opencode/plugin/telemetry.ts), not the brief: status EmitterSourceDerived, not Discovered. ⚠ status corrected 2026-08-27: `Done` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-fn-068) |
| REQ-FN-069 | Schema-v1 Playbook repos reuse parser, engine and pages (BRD-109, Phase 3) | Implemented | 75% | PlaybookRouting: route from the telemetry LAYOUT (UserRepo.Kind), provenance from the TAG (UserRepo.Framework). A converged Playbook repo (Framework=playbook, Kind=techieflow) gets the four TF streams, docs/metrics, the shared parser and engine, no adapter - and keeps its tag so figures never pool. Tests: PlaybookRoutingTests (7). ⚠ status corrected 2026-08-27: `Done` is not a value in this table's vocabulary; recorded as `Implemented` pending verification. | [view](#d-req-fn-069) |
| REQ-FN-070 | Full Playbook report set incl. snapshot export (BRD-110, Phase 3) | PARTIAL | 50% | Data side done: PlaybookAnalysis.ToExportPayload() gives ISnapshotExporter a coherent playbook.*-scoped key set (smoke-printed). Cluster F must consume IPlaybookReportBuilder when framework==playbook. Report PAGES are a later wave; the three-questions page state is em dash until the joiner output lands (see REQ-FN-067). | [view](#d-req-fn-070) |
| REQ-NFR-001 | Report-page and sync performance (BRD-82, Phase 2) | Implemented | 75% | 2026-08-27 Measured on the running app at concurrency 1, 5 samples per page: worst-case render / (123ms) · three-questions 86 · harness 85 · routing 104 · export 111 · repos 135 — all well inside the 1500ms p95 budget. HONEST CAVEAT: measured against the current dataset (~20 runs / 52 gates / 14 sessions / 18 commits), NOT the 50k-record cold-analysis clause, which remains unmeasured for want of a dataset that size. | [view](#d-req-nfr-001) |
| REQ-NFR-002 | Security posture — cookies, antiforgery, PAT scope, forwarded headers (BRD-83, Phase 1) | Implemented | 75% | 2026-08-27 Cookie hardening, antiforgery on every form, forwarded headers in `Program.cs`; `TfLens.Guardrails.Tests` 45/45 pass. | [view](#d-req-nfr-002) |
| REQ-NFR-003 | Secrets and tokens never logged, displayed or exported (BRD-10, Phase 1) | Implemented | 75% | 2026-08-27 Guardrail tests assert no secret or token reaches a log, a rendered value or an export. | [view](#d-req-nfr-003) |
| REQ-NFR-004 | Privacy — only stream-carried data stored and shown (BRD-84, Phase 2) | Implemented | 75% | 2026-08-27 Structural: the unknown-fields panel selects `jsonb_object_keys("Overflow")` in `PostgresStore`, so no Overflow VALUE can leave the database; commits store `SubjectPrefix`, never the full subject. Scanned the rendered /, /three-questions and /export: 0 JSON fragments and no harvested prose (the only long lines are TfLens's own SCHEMA.md §6 explanatory notes). | [view](#d-req-nfr-004) |
| REQ-NFR-005 | Accessibility and theme (BRD-85, Phase 1) | Implemented | 75% | 2026-08-27 Dark-first verified end to end (see REQ-UI-009); mobile visual gate passes on all seven pages. | [view](#d-req-nfr-005) |
| REQ-NFR-006 | Observability — Serilog rolling file + console (BRD-86, Phase 1) | Implemented | 75% | 2026-08-27 Serilog wired first in `Program.cs`, rolling file under `logs/` + console. | [view](#d-req-nfr-006) |
| REQ-NFR-007 | Reliability — failure containment and rebuildability (BRD-87, Phase 1) | Implemented | 75% | 2026-08-27 Per-repo sync isolation + rebuild-from-raw; covered by Core and Integration tests. | [view](#d-req-nfr-007) |
| REQ-NFR-008 | Testability — web-free Core, fixtures, `data-testid` (BRD-88, Phase 1) | Implemented | 75% | 2026-08-27 `TfLens.Core` has no web dependency (guardrail test); fixtures under tests/; `data-testid` on every asserted control. | [view](#d-req-nfr-008) |
| REQ-NFR-009 | Integrity — no switch relaxes the provenance rules (BRD-89, Phase 2) | Implemented | 85% | 2026-08-26 No boolean, option, configuration key or query parameter in the engine can merge two segments: `AddTfLensMetrics` reads no configuration, `MinN`/`GateOrder`/`LateGates` are constants, and the only enum (`Provenance`) selects rules inside a bucket. Covered by the shape tests above; a UI/query-parameter sweep belongs to the pages area. | [view](#d-req-nfr-009) |
| REQ-NFR-010 | Cross-user isolation proven by a two-user integration test (BRD-102, BRD-83, Phase 1) | Implemented | 75% | 2026-08-27 `tests/TfLens.Integration.Tests/CrossUserIsolationTests` — the two-user over-HTTP proof now genuinely runs (it was a deliberate Assert.Fail placeholder): two real accounts sign in, each sees only its own repository in the rendered markup. 16/16 Integration tests pass. | [view](#d-req-nfr-010) |

**Status values:** `Not Started` · `In Progress` · `Implemented` (code done, not yet verified) · `Verified` (self-smoke or verifier PASS — acceptance AND data-render AND visual gates all pass) · `Done (pre-existing)` (migrated from an earlier dev plan as already complete — build agents must NOT rebuild; terminal like `Verified`) · `Needs re-verify` (a defect or change was logged — must be re-run before it can return to `Verified`) · `PARTIAL` (some acceptance unmet — say what in Remarks) · `FAIL` (verifier ran and failed — bug in Remarks) · `Blocked` (external/library gap — link the TR-/TR-RAG- entry in Remarks) · `N/A`.

**% guide:** `0` not started · `25` scaffolded · `50` in progress · `75` implemented-unverified · `100` verified.

**Remarks:** date + what was done / what is missing / bug or library reference. This is the home for bugs and change notes — do not spawn a separate file. Visual-gate failures are prefixed `⚠ visual:`; security findings `⚠ SECURITY`.

## UI / Pages

<!-- UI REQs are built by /trblazeui from the approved mockups (docs/TfLens-UIDesign.md
     + docs/mockups/*.html); every REQ cites the mockup screen it realizes. The mockups
     are a click-through — start at docs/mockups/login.html. Control names below come
     from the UIDesign component maps; §Library gaps there mandates StatTile/StatGroup
     for KPI cards, PasswordStrength for password meters, CodeBlock for compare output
     and CenteredPanel for the auth cards — use those, not the fallbacks. -->

### Page: Login (`/login`)

<a id="d-req-ui-001"></a>
- **REQ-UI-001** — Anonymous split-layout login page: left brand panel, right `CenteredPanel` + `Card` with email and password `Field`s, full-width Sign in button, links to Register and Forgot password, and a muted "GitHub sign-in: coming in a later release" line with **no GitHub button** in this release (BRD-1, BRD-90, BRD-94). *Mockup:* docs/mockups/login.html.
  - *Acceptance:* page renders anonymously at `/login` with `data-testid` `login-email`, `login-pass`, `login-submit`; Enter submits; the submit button shows a `Spinner` and disables while the AppManager call is in flight; a failed sign-in shows the generic `Alert Variant=Danger` "Sign-in failed. Check your email and password." with the fields' values kept and the AppManager error code never rendered; `ACCOUNT_LOCKED` is the only specific message ("Account locked — try again later"); no GitHub/SSO button exists in the DOM; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Register (`/register`)

<a id="d-req-ui-002"></a>
- **REQ-UI-002** — Anonymous registration page on the same split layout: first name, last name (2-col grid), email, password with a `PasswordStrength` meter and the rule description, confirm password, an always-visible `Alert Variant=Info AccentBorder` "Every TfLens account is a Manager — no licence, no subscription.", Create account button and a link back to Sign in (BRD-91, BRD-95). *Mockup:* docs/mockups/register.html.
  - *Acceptance:* `data-testid` `reg-first`, `reg-last`, `reg-email`, `reg-pass`, `reg-confirm`, `reg-submit` present; the AppManager password rules (8+ chars, uppercase, digit, special) are checked locally and shown as per-rule `FieldError`s **before** any API call; mismatched confirm shows "passwords differ"; an AppManager duplicate email shows `FieldError` "already registered"; `VALIDATION_ERROR` / `DECRYPTION_FAILED` render as one generic danger `Alert`; the Manager note is always visible; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Forgot password (`/forgot-password`)

<a id="d-req-ui-003"></a>
- **REQ-UI-003** — Anonymous forgot-password page: one email `Field`, Send reset link button, and a success `Alert Variant=Success` that **replaces the form**, plus a "Back to sign in" link (BRD-92). *Mockup:* docs/mockups/forgot-password.html.
  - *Acceptance:* `data-testid` `forgot-email`, `forgot-submit`; after submit the page **always** shows "If that address exists, a reset link is on its way." regardless of whether the address exists (enumeration-safe — no timing or wording difference); the form is replaced, not merely disabled; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Reset password (`/reset-password`)

<a id="d-req-ui-004"></a>
- **REQ-UI-004** — Anonymous reset-password page reading `?token=…`: new password with `PasswordStrength`, confirm, Reset button; an invalid/expired token replaces the form with `Alert Variant=Danger`; success shows `Alert Variant=Success` "Password updated." plus a Sign in button (BRD-92). *Mockup:* docs/mockups/reset-password.html.
  - *Acceptance:* `data-testid` `reset-pass`, `reset-confirm`, `reset-submit`; the same local password rules as Register are enforced inline; `INVALID_RESET_TOKEN` and `APP_ID_MISMATCH` both render "This reset link is invalid or has expired."; the token is read from the query string and never displayed; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Profile (`/profile`)

<a id="d-req-ui-005"></a>
- **REQ-UI-005** — Authenticated profile page inside the shell: two side-by-side `Card`s — a read-only AppManager profile (`Avatar` initials, Email, Name, Role `Badge` Manager, Member since, Identity provider) and a change-password form (current / new / confirm with `PasswordStrength`) — plus a standing `Alert Variant=Info` "TfLens stores no passwords — your account lives in AppManager." (BRD-107, BRD-95). *Mockup:* docs/mockups/profile.html (user menu shown open).
  - *Acceptance:* `data-testid` `pw-current`, `pw-new`, `pw-confirm`, `pw-submit`; profile values come from `GET /UserSvc/profile` and show `Skeleton` while loading; `INVALID_CURRENT_PASSWORD` renders as a `FieldError` on the current-password field; success raises a toast; the Role badge always reads `Manager`; cards stack to one column under `md` and controls do not overlap at desktop + mobile widths (visual gate).

### Layout: App shell (sidebar + header)

<a id="d-req-ui-006"></a>
- **REQ-UI-006** — `SidebarProvider` + `Sidebar Collapsible` shell with `SidebarHeader` (brand), `SidebarRail` and `SidebarTrigger`; navigation in the fixed working order **Repos** (`git-branch`, with `SidebarMenuBadge` repo count) then the Reports group — Coverage / health (`activity`), Three questions (`help-circle`), Harness comparison (`git-compare`), Routing & economics (`route`), Snapshot export (`download`); no separate Playbook item (BRD-5, BRD-105). *Mockup:* docs/mockups/coverage.html (shell visible on every report mockup).
  - *Acceptance:* the six items appear in exactly that order with those Lucide icons; `SidebarTrigger` collapses to an icon-only rail where each item shows its `Tooltip`; the collapsed/expanded state survives a reload via `CookieKey="tflens:sidebar"`; `IsActive` marks the current route; no `/playbook` nav item exists; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-007"></a>
- **REQ-UI-007** — Header **Sync now** `Button` (`LucideIcon refresh-cw`) with an adjacent `Badge Variant=Outline` last-sync badge ("synced 12 min ago"), which runs the signed-in user's sync and reports the per-repo outcome as a toast (BRD-6). *Mockup:* docs/mockups/coverage.html (header).
  - *Acceptance:* pressing Sync now disables the button and shows progress while running, then raises a toast listing each repo as updated / skipped / error; the last-sync badge updates in place without a full page reload; the badge reads a relative time from `sync_state`; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-008"></a>
- **REQ-UI-008** — Header **user menu**: `DropdownMenu` whose trigger is `Avatar` (initials) + display name + `chevron-down`, containing `DropdownMenuLabel` (email), items Profile (`user`), Manage repos (`git-branch`), a `DropdownMenuSeparator`, and Sign out (`log-out`); there is **no bare sign-out button** anywhere (BRD-4, BRD-106). *Mockup:* docs/mockups/profile.html (menu open) · docs/mockups/coverage.html (header).
  - *Acceptance:* the signed-in user's display name and email render from the cookie claims; the three items navigate to `/profile`, `/repos` and the sign-out handler respectively; the menu is keyboard-reachable and closes on Escape; no sign-out control exists outside this menu; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-009"></a>
- **REQ-UI-009** — Theme toggle (`Switch` with sun/moon `LucideIcon`) that flips `<html class="dark">`; the app **starts in dark mode** on first visit and the user's choice is persisted per user (BRD-85). *Mockup:* docs/mockups/coverage.html (header / sidebar footer).
  - *Acceptance:* a first visit with no stored preference renders dark; toggling switches to the TrBlazeUI `:root` light token set with no custom colours; the choice survives sign-out/sign-in for the same user; the toggle is keyboard-reachable and labelled for screen readers; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-010"></a>
- **REQ-UI-010** — Header **Framework switch**: `Tabs` used as a segmented control (`TabsList` + `TabsTrigger` "TechieFlow" and "Playbook", each with a `Badge` repo count), `data-testid="framework-switch"`, persisted per user; selecting Playbook re-queries every figure on the page and, until Phase 3, shows the "No Playbook data yet" empty state (BRD-108). *Mockup:* docs/mockups/coverage.html (header) · Playbook state: docs/mockups/playbook.html.
  - *Acceptance:* the switch renders on all five report pages and nowhere else; the selection persists per user across navigation and reload; switching re-queries the page rather than filtering client-side; each trigger's badge shows that framework's connected-repo count for this user; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Repos (`/repos`)

<a id="d-req-ui-011"></a>
- **REQ-UI-011** — Repos page: header row (title + primary **Connect repo** button), a `StatGroup`/`StatTile` KPI row (Connected repos · Records synced · Last successful sync), and a `DataTable` of the user's repos with columns Repo (owner/name + GitHub link) · Branch · Kind `Badge` · Visibility `Badge` public · Status `Badge` · Last sync · Records · Actions (row Sync and Remove icon buttons), plus an `Empty` state for a new user (BRD-98). *Mockup:* docs/mockups/repos.html.
  - *Acceptance:* `data-testid` `repos-table`, `connect-repo`, `repos-empty`, `repo-sync-{name}`, `repo-remove-{name}`; the table shows only the signed-in user's repos; a repo in error shows a `Tooltip` carrying its `LastError` text; the row Sync button shows a `Spinner` while running; a user with no repos sees the `Empty` component with a working Connect action instead of an empty grid; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-012"></a>
- **REQ-UI-012** — Connect-repo `Dialog`: GitHub URL or `owner/name` input, optional branch, Kind `Select` (Auto-detect / techieflow / playbook), a **Validate** button, a three-line validation result (Repository exists · Public · Telemetry path found: …), and a Connect button enabled **only after a green validation** (BRD-99, BRD-100). *Mockup:* docs/mockups/repos.html (dialog panel).
  - *Acceptance:* `data-testid` `connect-input`, `connect-validate`, `connect-submit`; Connect stays disabled until all three checks pass; a private repo shows `Alert Variant=Warning` "Private repos aren't supported in this release"; a missing telemetry path shows `Alert Variant=Danger`; a GitHub rate-limit response shows `Alert Variant=Warning` "GitHub rate limit reached — try again in N minutes"; on success the dialog shows a "Connecting…" `Progress`, the first sync runs, a toast reports the outcome and the sidebar Repos badge updates; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-013"></a>
- **REQ-UI-013** — Remove-repo `AlertDialog` titled "Remove owner/name?" whose description states plainly that the parsed rows and raw archive for this repo are deleted and the GitHub repo is untouched, with a destructive Remove action (BRD-101). *Mockup:* docs/mockups/repos.html (dialog panel).
  - *Acceptance:* removal only proceeds through the confirm action (Escape/Cancel aborts with no data change); the description names the repo being removed; after confirmation the row disappears and the KPI counts update; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Coverage / health (`/`)

<a id="d-req-ui-014"></a>
- **REQ-UI-014** — Coverage landing page: a single summary `Alert` reading **GREEN — n repos synced, nothing stale** or **CHECK — n warnings**, a four-`StatTile` KPI row (Repos synced · Gate records live/backfilled · Newest record age · Sync errors), and one `Card` per connected repo (owner/name, kind `Badge`, short SHA `Badge` linked to the GitHub commit, last sync time and outcome) each holding a per-stream `DataTable` with columns Stream · Records · Backfilled · Newest · **Days since** (BRD-39, BRD-40, BRD-43, BRD-44). *Mockup:* docs/mockups/coverage.html.
  - *Acceptance:* `data-testid` `coverage-status`, `repo-streams-{name}`; the page is the landing route after sign-in **only** for a user with at least one connected repo — a user with none is redirected to `/repos`; the stream table has four rows (runs, gates, sessions, commits) for a techieflow repo and an `events` row for a playbook repo; the short SHA opens the GitHub commit; a per-repo error renders as a danger `Alert` inside that repo's card with the other cards unaffected; cards stack to one column under `md` and controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-015"></a>
- **REQ-UI-015** — Staleness warning: for any repo whose newest `sessions` or `commits` record is older than the threshold (default 7 days), an `Alert Variant=Warning AccentBorder` stating in words "sessions/commits stale ≥ 7 days — this clone isn't pushing or lacks hooks; run update-framework.sh on it", and the stale row carries a `Badge Variant=Destructive` "stale" (BRD-41). *Mockup:* docs/mockups/coverage.html.
  - *Acceptance:* the warning appears only when the threshold is exceeded and names the affected streams; the wording is prose, not a bare number; the threshold is configurable and the rendered text reflects the configured value; each stale stream row is badged; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-016"></a>
- **REQ-UI-016** — Unknown-fields panel: a `Collapsible` "Fields observed that SCHEMA.md doesn't document (n)" listing the observed field **names only** as `Badge Variant=Outline`, grouped by repo and stream, plus an `Alert Variant=Info` when any record has `v > 1` (BRD-42). *Mockup:* docs/mockups/coverage.html.
  - *Acceptance:* only field names are shown — no field values and no `Overflow` payload is ever rendered; the count in the trigger matches the number of distinct names listed; the panel reads "none" when there are none; the `v > 1` alert states which repo and stream carried the record; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-017"></a>
- **REQ-UI-017** — Rebuild-from-raw `Card` at the bottom of Coverage with an `AlertDialog`-guarded destructive button ("Rebuild…" → "Drop and reparse"), a `Progress` while replaying, and the resulting report (files replayed, records stored, duplicates collapsed per stream) (BRD-21). *Mockup:* docs/mockups/coverage.html.
  - *Acceptance:* `data-testid="rebuild"`; nothing is dropped until the confirm action is taken (Escape/Cancel aborts); the same code path as the `rebuild` verb (REQ-FN-028) runs; the report renders per stream after completion and the page refreshes in place; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Three questions (`/three-questions`)

<a id="d-req-ui-018"></a>
- **REQ-UI-018** — Three-questions page: a standing `Alert Variant=Info` "Figures are never combined across project_type or across live/backfilled (SCHEMA.md §6). There is no total.", then `Tabs` with **one tab per `project_type` present** (`app`, `library`, `docs`, `framework`, and `unclassified` labelled "unclassified (project_type inferred)") each with a record-count `Badge`, and inside each tab three KPI `StatTile`s — First-pass rate · Escape rate · Failures scored — plus a `TypographyMuted` segment-facts line (BRD-45, BRD-50). *Mockup:* docs/mockups/three-questions.html.
  - *Acceptance:* `data-testid="type-tab-{type}"`; there is **no "all types" tab and no total row anywhere on the page**; only types with records get a tab; any figure with n < 3 renders as the literal italic muted text `insufficient data (n=…)` and never as a number; the segment-facts line states live records, REQs scored and REQs excluded by taint; the standing note is always visible; no gate records at all renders the `Empty` state; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-019"></a>
- **REQ-UI-019** — Backfilled figures for the same `project_type` shown as a clearly labelled secondary line under each live KPI, carrying `Badge Variant=Secondary` "backfilled", and as their own columns in the distribution table — never summed with live (BRD-46). *Mockup:* docs/mockups/three-questions.html.
  - *Acceptance:* every backfilled value is visually and textually labelled backfilled; no rendered element combines a live and a backfilled value into one number; there is no control that merges the two; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-020"></a>
- **REQ-UI-020** — Gate catch distribution `DataTable` per type: columns Gate · Live count · Live share (with a `Progress` bar) · Backfilled count · Backfilled share, rows in the reference's `GATE_ORDER` — build, acceptance, render, visual, perf, standards, **escaped**, unattributed (BRD-47). *Mockup:* docs/mockups/three-questions.html.
  - *Acceptance:* `data-testid="gate-dist-{type}"`; `escaped` is its own row (never folded into a gate) and carries `Badge Variant=Destructive` "no gate caught it"; failures with no gate appear as `unattributed`; row order matches the reference's gate order exactly; the `perf` row carries `Badge Variant=Outline` "see coverage"; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-021"></a>
- **REQ-UI-021** — Taint list: a `Collapsible` "n REQs excluded from the live first-pass rate" listing every excluded REQ ID in full as `Badge Variant=Outline` (BRD-48). *Mockup:* docs/mockups/three-questions.html.
  - *Acceptance:* `data-testid="taint-list"`; the list is complete (not truncated) and its count matches the engine's excluded set and the segment-facts line; it reads "none" when empty; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-022"></a>
- **REQ-UI-022** — Late-gate coverage `Card`: one line per late-added gate reading "perf gate: ran on n records, caught k → rate", or `insufficient data (n=…)`, or "not yet run on this data (gate added 2026-08-10)" (BRD-49). *Mockup:* docs/mockups/three-questions.html.
  - *Acceptance:* `ran` counts records whose `gates_run` contains the gate and is shown beside `caught`; the gate's share of the raw distribution is **never** presented as a catch rate; the "not yet run" wording carries the gate's addition date; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Harness comparison (`/harness`)

<a id="d-req-ui-023"></a>
- **REQ-UI-023** — Harness page: a standing `Alert Variant=Info` "Tokens may be compared across harness; dollars may not…", then a three-column grid of `Card`s — **claude-code · opencode · codex** — each holding the same key/value rows (Runs · Runs by cmd top 3 · Gate records · Verdict mix · Sessions · Tokens in/out/cache read/cache write), plus a tokens-by-harness `BarChart` (BRD-51). *Mockup:* docs/mockups/harness.html.
  - *Acceptance:* `data-testid="harness-col-{claude-code|opencode|codex}"`; all three columns render even when a harness has zero records (showing `—`); token totals draw from both the `runs` §2.5 fields and `sessions`; the chart is supplementary — every value also appears as text; columns wrap to one per row under `md` and controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-024"></a>
- **REQ-UI-024** — Tokens-per-verified-REQ row inside each harness column (BRD-52). *Mockup:* docs/mockups/harness.html.
  - *Acceptance:* the value is computed per harness (never pooled) and renders as `insufficient data (n=…)` below the minimum n; the rounding matches the reference's 1 dp for tokens-per-Verified; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-025"></a>
- **REQ-UI-025** — Measured-dollars `Card` titled "Measured dollars (OpenCode only)" with `Badge Variant=Secondary` "the only measured dollars in the system", the summed `cost_usd` for `opencode`, and the muted line "Claude Code and Codex: not measured (null by design)" (BRD-53, BRD-54). *Mockup:* docs/mockups/harness.html.
  - *Acceptance:* `data-testid="opencode-cost"`; a dollar figure appears **only** in this card and only for `opencode`; **no dollar total across harnesses exists anywhere on the page**; the claude-code and codex columns state "not measured (null by design)" rather than `0`; with no OpenCode records the card reads "no OpenCode records yet"; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-026"></a>
- **REQ-UI-026** — `harness: null` footnote row under the columns: "*n* records with harness not detected — excluded from the columns above" (BRD-55). *Mockup:* docs/mockups/harness.html.
  - *Acceptance:* `data-testid="harness-null-footnote"`; undetected records are never merged into a named harness column and never silently dropped; the footnote is hidden only when n = 0; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Routing & economics (`/routing`)

<a id="d-req-ui-027"></a>
- **REQ-UI-027** — Routing page `Tabs` (Routing drift · Tokens by model · Repricing (estimate) · Poolable metrics) with the drift tab holding three KPI `StatTile`s (Runs with routing fields · `routed:false` runs · Distinct observed models) and a drift `DataTable` with columns cmd · tier · tier_model · observed model · models · routed · ts (BRD-56). *Mockup:* docs/mockups/routing.html.
  - *Acceptance:* `data-testid` `routing-tab-{key}`, `drift-table`; `routed:false` rows sort first and carry `Badge Variant=Destructive` "drift"; the table lists declared `tier`/`tier_model` beside the observed `model` (and `models` when more than one), grouped by command; with no routing fields captured the tab shows `Empty` "no routing fields captured yet"; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-028"></a>
- **REQ-UI-028** — Tokens-by-model `DataTable` (model · in · out · cache read · cache write · total) with a supplementary totals `BarChart` (BRD-57). *Mockup:* docs/mockups/routing.html.
  - *Acceptance:* all four token classes are summed per **observed** model; every charted value also appears in the table as text; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-029"></a>
- **REQ-UI-029** — Repricing tab: two `Card`s — "Actual mix" and "All runs at most expensive model (…)" — each carrying `Badge Variant=Outline` **estimate — tokens × rate card, not measured spend**, a big dollar value and a muted "n runs excluded (tokens_scope none)" line, plus a "Counterfactual delta" card showing max − actual and the percentage (BRD-58, BRD-59, BRD-60). *Mockup:* docs/mockups/routing.html.
  - *Acceptance:* `data-testid` `repricing-actual`, `repricing-max`; the **estimate** label appears on every repricing figure without exception; runs with `tokens_scope: none` (or no token fields) are excluded and the excluded count is stated on screen; a model missing from `prices.json` raises `Alert Variant=Warning` naming that model rather than silently pricing it at zero; the estimate label is text, not colour alone; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-030"></a>
- **REQ-UI-030** — Edit-prices `Dialog` with a `DataTable` of editable rows (model · input · output · cache read · cache write USD per 1M) and Save / Cancel (BRD-61). *Mockup:* docs/mockups/routing.html.
  - *Acceptance:* `data-testid="edit-prices"`; non-numeric or negative values raise a `FieldError` and block Save; Save writes `data/prices.json` (the file remains the source of truth), raises a toast and the repricing cards recompute; Cancel discards without writing; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-031"></a>
- **REQ-UI-031** — Poolable-metrics tab: five KPI `StatTile`s — Rework ratio · Batch size (median) · REQ throughput (REQs/hour) · Tokens per Verified · Commit cadence (with duplicates collapsed) — read straight from the engine's `Pooled` block (BRD-62). *Mockup:* docs/mockups/routing.html.
  - *Acceptance:* values match `AnalysisResult.Pooled` exactly with the reference's rounding; any metric below the minimum n renders `insufficient data (n=…)`; controls do not overlap at desktop + mobile widths (visual gate).

### Page: Snapshot export (`/export`)

<a id="d-req-ui-032"></a>
- **REQ-UI-032** — Export page: an Export `Card` with the **Export snapshot** button and the muted target path, a dataset-SHA `DataTable` (repo · branch · SHA · synced) with per-row copy buttons, and a past-snapshots `DataTable` (date · parser version · parity status · `snapshot.md` and `tflens.json` download links) (BRD-63, BRD-70). *Mockup:* docs/mockups/export.html.
  - *Acceptance:* `data-testid` `export-now`, `snapshots`; pressing Export shows a `Spinner`, writes the two files for the signed-in user and the selected framework, raises a toast and refreshes the list; the SHA table reproduces `sync_state`'s last-sync SHAs so the reference dataset can be pinned; with no snapshots the list shows `Empty` "no snapshots yet"; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-033"></a>
- **REQ-UI-033** — Quotable banner (`Alert Variant=Success` "QUOTABLE — …" or `Alert Variant=Warning` "NOT QUOTABLE — parser changed after the last parity run; re-run the parity procedure") and a "Last parity run" `Card` showing date, dataset SHAs, script hash, parser version and the compare output in a `CodeBlock` (BRD-67, BRD-71). *Mockup:* docs/mockups/export.html.
  - *Acceptance:* `data-testid="quotable-banner"`; the banner reads QUOTABLE **only** when the recorded parity run postdates the last parser change (comparing the recorded parser version with the build's); with no parity record on file the card shows `Alert Variant=Warning` and the banner reads NOT QUOTABLE; the compare output renders in `CodeBlock`, not a raw `<pre>`; controls do not overlap at desktop + mobile widths (visual gate).

### Pages: Playbook framework state (Phase 3)

<a id="d-req-ui-034"></a>
- **REQ-UI-034** — The Playbook state of all five report pages, selected by the header Framework switch: identical layouts to the TechieFlow state with a standing `Alert Variant=Info` ("Playbook process-gates (`phase_gate`) and TechieFlow assertion-gates (`gate`) are different axes and never share a chart (SCHEMA.md §11). Figures are never pooled across frameworks."), Coverage showing the `events` stream and an observed-fields `Collapsible`, Three questions keyed by `phase_gate` with phase totals, Routing showing the main-vs-subagent token split, and an `Empty` "No Playbook data yet" state until Phase 3 lands (BRD-75, BRD-108, BRD-110). *Mockups:* docs/mockups/playbook.html · three-questions-playbook.html · harness-playbook.html · routing-playbook.html · export-playbook.html.
  - *Acceptance:* `data-testid` `playbook-empty`, `pb-phases-{name}`; no chart, column or table on any Playbook-state page mixes `phase_gate` with TechieFlow `gate` data; switching framework re-queries every figure; before Phase 3 every report page's Playbook state shows the `Empty` component with a Connect-a-Playbook-repo action; costs absent from `events.ndjson` render `—`, and n < 3 renders `insufficient data (n=…)`; controls do not overlap at desktop + mobile widths (visual gate).

## Functional requirements

<!-- Built directly by build-phase (flow-master). Phase order is hard: 1 → 2 → 3. -->

### Phase 1 — identity, repos, sync, storage, ops

<a id="d-req-fn-001"></a>
- **REQ-FN-001** — `AppManagerClient` sends `X-Api-Key` / `X-Api-Secret` on every call (Application Id 1), caches `GET /AuthSvc/public-key`, RSA-OAEP-256-encrypts every password field, and calls `POST /AuthSvc/login`; documented error codes map to a typed `AppManagerException` (BRD-90).
  - *Acceptance:* no password ever leaves the process in clear; the public key is fetched once and reused until invalidated; `INVALID_CREDENTIALS`, `ACCOUNT_LOCKED`, `ACCOUNT_DISABLED`, `DECRYPTION_FAILED` and `NO_APP_ACCESS` each map to a distinct typed error; the raw code is logged but never returned to the browser.

<a id="d-req-fn-002"></a>
- **REQ-FN-002** — `RegisterAsync` posts `POST /AuthSvc/register` always with `applicationRoleCode: "Manager"`, after the AppManager password rules (8+, uppercase, digit, special) are validated locally (BRD-91).
  - *Acceptance:* a password failing any rule is rejected before the API call; the role code is a constant with no caller-supplied override; a successful registration issues the same session as login (REQ-FN-004) and lands the user on `/repos`.

<a id="d-req-fn-003"></a>
- **REQ-FN-003** — `/forgot-password` calls `POST /AuthSvc/forgot-password` and `/reset-password` calls `POST /AuthSvc/reset-password`, with the API-key header supplying the app scope (BRD-92).
  - *Acceptance:* the forgot response is identical for known and unknown addresses (enumeration-safe); `INVALID_RESET_TOKEN` and `APP_ID_MISMATCH` both surface as the single "invalid or expired" outcome; the reset token is never logged.

<a id="d-req-fn-004"></a>
- **REQ-FN-004** — `AuthService` issues TfLens's own auth cookie (claims: session id, AppManager `userId`, email, display name, role) as sliding 12 h / HttpOnly / Secure, stores the AppManager access and refresh tokens **server-side** in `AuthSession`, refreshes via `POST /AuthSvc/refresh` before `tokenExpiresAt` (rotating the stored refresh token), revalidates a resumed cookie via `POST /AuthSvc/validate`, and calls `POST /AuthSvc/logout` on sign-out (BRD-93).
  - *Acceptance:* AppManager tokens never reach the browser in any form (cookie, storage or markup); a refresh within the pre-expiry window succeeds transparently; a refresh failure signs the user out rather than serving a stale session; the session row is deleted on sign-out.

<a id="d-req-fn-005"></a>
- **REQ-FN-005** — Every page except `/login`, `/register`, `/forgot-password`, `/reset-password` and `/healthz` requires the auth cookie; an unauthenticated request redirects to `/login` with the return URL preserved (BRD-2).
  - *Acceptance:* each of the five anonymous routes returns 200 without a cookie; every other route redirects to `/login?returnUrl=…`; the authorization requirement is applied by default (fallback policy), not route-by-route opt-in.

<a id="d-req-fn-006"></a>
- **REQ-FN-006** — After a successful sign-in the user is redirected to the return URL if one was preserved, otherwise to `/repos` when they have no connected repos, otherwise to `/` (BRD-1).
  - *Acceptance:* all three branches are exercised; the return URL is validated as a local path (no open redirect).

<a id="d-req-fn-007"></a>
- **REQ-FN-007** — Sign out calls AppManager `POST /AuthSvc/logout` with the refresh token, deletes the session row, clears the cookie and redirects to `/login` (BRD-4).
  - *Acceptance:* the cookie is gone after the round trip and the back button cannot resume the session; an AppManager logout failure still clears the local session and logs the reason.

<a id="d-req-fn-008"></a>
- **REQ-FN-008** — Every user is `Manager` for Application 1; no code path calls LicenseSvc, FeatureSvc, PaymentSvc or IssueSvc (BRD-95).
  - *Acceptance:* the codebase contains no reference to those services; a role other than `Manager` is never requested or persisted.

<a id="d-req-fn-009"></a>
- **REQ-FN-009** — The demo account `TfLensDemo` (`tflensdemo@techierathore.com`) is registered in AppManager during development and its public demo repos are connected **through the Repos screen**, not seeded from configuration (BRD-96).
  - *Acceptance:* the demo user signs in and shows a populated dashboard; no repo list or demo seed exists in `appsettings`; the account is listed as UsageGuide test user #1.

<a id="d-req-fn-010"></a>
- **REQ-FN-010** — The AppManager connection comes from configuration only: `TfLensAppManagerBaseUrl` (default `https://appmgrapi.techierathore.com`), `TfLensAppManagerAppId` (default 1), `TfLensAppManagerApiKey`, `TfLensAppManagerApiSecret` (BRD-97).
  - *Acceptance:* the key and secret appear nowhere in the repository, logs or UI; a missing key or secret fails startup (REQ-FN-038); the base URL and app id have working defaults.

<a id="d-req-fn-011"></a>
- **REQ-FN-011** — `/profile` reads `GET /UserSvc/profile` and changes the password through `POST /UserSvc/change-password` with both passwords RSA-encrypted (BRD-107).
  - *Acceptance:* the profile renders live AppManager data (not cookie claims alone); `encryptedCurrentPassword` and `encryptedNewPassword` are both encrypted with the cached public key; `INVALID_CURRENT_PASSWORD` surfaces as a field error.

<a id="d-req-fn-012"></a>
- **REQ-FN-012** — *(Deferred — Phase 2, not in this release)* GitHub SSO with the user record living in AppManager, blocked until AppManager offers an external-login / token-exchange endpoint (BRD-94, ADR-012).
  - *Acceptance:* **do not build.** This release must contain no GitHub sign-in button, no external-login code path and no TfLens-held per-user bridge credential. Revisit only when AppManager ships an SSO endpoint.

<a id="d-req-fn-013"></a>
- **REQ-FN-013** — `RepoRegistry` returns the signed-in user's `UserRepo` rows with owner/name, branch, kind, visibility, sync status, last sync and per-stream record counts (BRD-98).
  - *Acceptance:* the read takes `userId` as a mandatory parameter; counts come from the stream tables scoped to that user and repo.

<a id="d-req-fn-014"></a>
- **REQ-FN-014** — `ConnectAsync(userId, input)` parses a GitHub URL or `owner/name`, calls `GET /repos/{owner}/{name}` (must exist), resolves the branch (default branch unless specified), probes `contents/docs/metrics` then `contents/verification/telemetry` at that branch to detect the kind (`techieflow` / `playbook`, overridable), saves the `UserRepo` and queues the first sync (BRD-99).
  - *Acceptance:* each of the three validations (exists, public, telemetry path) is reported separately to the UI; kind auto-detection picks `techieflow` for `docs/metrics` and `playbook` for `verification/telemetry`; an explicit kind override is honoured; a repo with neither path is refused with the reason.

<a id="d-req-fn-015"></a>
- **REQ-FN-015** — A repo with `private == true` is refused with an explicit "public repos only in this release" message; the optional server PAT (`TfLensGitHubToken`) is used solely to raise the rate limit for public reads (BRD-100).
  - *Acceptance:* no per-user token is ever accepted or stored; the PAT is fine-grained contents-read and its absence only lowers the rate limit (60/h per IP) without changing behaviour; the refusal message names the reason.

<a id="d-req-fn-016"></a>
- **REQ-FN-016** — `RemoveAsync` stops the repo's sync, deletes that user's rows in every stream table and `SyncState` for the repo, and removes `data/raw/<userId>/<owner>__<name>/` (BRD-101).
  - *Acceptance:* after removal no row and no raw file for that (user, repo) remains; another user's copy of the same public repo is untouched; the GitHub repository is not contacted for anything but reads.

<a id="d-req-fn-017"></a>
- **REQ-FN-017** — `UserId` is a **mandatory parameter** (not an optional filter) of every store read and write, of the raw-archive path, of the reports path and of the memoised analysis cache key (BRD-102, ADR-013).
  - *Acceptance:* no store method exposes an overload without `userId`; the analysis cache is keyed by `(userId, framework)`; a missing filter is a compile-time absence rather than a runtime oversight; proven by REQ-NFR-010.

<a id="d-req-fn-018"></a>
- **REQ-FN-018** — The background poller iterates **every** user's repos; header **Sync now** syncs **only the signed-in user's** repos; errors stay scoped per user and repo (BRD-103).
  - *Acceptance:* one user's failing repo never affects another user's sync or served pages; the Sync now report lists only the caller's repos.

<a id="d-req-fn-019"></a>
- **REQ-FN-019** — A duplicate `owner/name` for the same user is rejected; different users may connect the same public repo independently, each with their own rows and raw archive (BRD-104).
  - *Acceptance:* the duplicate attempt returns a clear message and creates no row; two users connecting the same repo produce two independent `UserRepo` rows and two raw archive folders.

<a id="d-req-fn-020"></a>
- **REQ-FN-020** — A `BackgroundService` polls every connected repo of every user every `PollIntervalMinutes` (default 15) (BRD-12).
  - *Acceptance:* the service starts with the host and stops cleanly on shutdown; the interval is configuration-driven; a tick that overruns does not stack with the next.

<a id="d-req-fn-021"></a>
- **REQ-FN-021** — Per repo, read the newest commit SHA touching the telemetry path on the configured branch (`GET /repos/{o}/{r}/commits?sha={branch}&path=…&per_page=1`) and skip the repo entirely when it equals `sync_state.LastSha` (BRD-13).
  - *Acceptance:* an unchanged repo costs exactly one API call and fetches no file bytes; only `LastSyncTs` is updated on a skip.

<a id="d-req-fn-022"></a>
- **REQ-FN-022** — Each stream file is fetched whole at that exact SHA (`Accept: application/vnd.github.raw`); a 404 on a stream file means "stream absent" and is recorded as zero records, not an error (BRD-14).
  - *Acceptance:* the fetch pins the SHA (never the branch tip); a repo missing one stream syncs successfully with that stream at zero and no error in `LastError`.

<a id="d-req-fn-023"></a>
- **REQ-FN-023** — Errors are isolated per repo (401 / 403 / 404 / network): a redacted status-code-plus-short-reason lands in `sync_state.LastError` and the remaining repos continue (BRD-15).
  - *Acceptance:* one failing repo never aborts the sync loop; the recorded reason never contains a token, secret or URL credential; the Coverage page surfaces it per repo.

<a id="d-req-fn-024"></a>
- **REQ-FN-024** — The GitHub client is structurally read-only: it holds no write scopes and no code path issues anything but `GET` (BRD-16).
  - *Acceptance:* no `POST`/`PUT`/`PATCH`/`DELETE` call against `api.github.com` exists in the codebase; the fetcher's type surface exposes no write method to call.

<a id="d-req-fn-025"></a>
- **REQ-FN-025** — `sync_state` is updated after each repo sync with, per user and repo: last SHA, last sync timestamp, per-stream record counts and last error (BRD-17).
  - *Acceptance:* a successful sync clears `LastError` to null; the counts match what the parser reported; the row is keyed by `(UserId, Repo)`.

<a id="d-req-fn-026"></a>
- **REQ-FN-026** — The memoised analysis is invalidated after every completed sync or rebuild (BRD-18).
  - *Acceptance:* a report page opened after a sync recomputes rather than serving the pre-sync figures; invalidation is scoped to the affected `(userId, framework)`.

<a id="d-req-fn-027"></a>
- **REQ-FN-027** — Every fetched stream file is written byte-for-byte to `data/raw/<userId>/<owner>__<name>/<stream>-<sha>.jsonl` **before** it is parsed (BRD-19).
  - *Acceptance:* the archived bytes are identical to the response body (no re-serialization, no normalization); a parser exception leaves the archive intact so `rebuild` can replay it.

<a id="d-req-fn-028"></a>
- **REQ-FN-028** — `dotnet TfLens.dll rebuild` truncates every stream table, re-applies `database/001-schema.sql` and replays every archived raw file in `(user, repo, sha-fetch-order)` (BRD-20).
  - *Acceptance:* the rebuild reads only from `data/raw/`, never from the GitHub API; `sync_state` counts are recomputed from the newest SHA per repo; the verb shares its implementation with the Coverage button (REQ-UI-017).

<a id="d-req-fn-029"></a>
- **REQ-FN-029** — A rebuild reports files replayed, records stored and duplicates collapsed per stream, and produces record counts **identical** to those from live syncing (BRD-22).
  - *Acceptance:* a rebuild run immediately after a live sync yields the same per-stream counts (idempotent parsing, REQ-FN-033..035); the report is returned by both the verb and the button.

<a id="d-req-fn-030"></a>
- **REQ-FN-030** — One PostgreSQL table per stream (`Run`, `Gate`, `Session`, `Commit`) plus `SyncState`, accessed with Dapper over Npgsql, with column names exactly the SCHEMA.md field names in PascalCase and every identifier double-quoted in SQL (BRD-23).
  - *Acceptance:* the SCHEMA.md → column mapping table exists in the parser and Architecture §6; quoted identifiers survive Postgres's lower-casing (`"Gate"."ReqId"`); upserts use `INSERT … ON CONFLICT DO NOTHING` against the dedupe unique indexes.

<a id="d-req-fn-031"></a>
- **REQ-FN-031** — Any property the parser does not know for that stream, and every property of a record with `v > 1`, is preserved in a JSON `Overflow` column rather than dropped; the distinct unknown field names are reported per repo and stream (BRD-24).
  - *Acceptance:* no field is silently lost; the overflow report drives REQ-UI-016; the `Overflow` payload itself is never rendered (REQ-NFR-004).

<a id="d-req-fn-032"></a>
- **REQ-FN-032** — Lines that are not valid JSON are counted and skipped, exactly as the reference's `read_stream` does (BRD-25).
  - *Acceptance:* a malformed line does not abort the file; the invalid-line count is returned in the parse result and matches the reference on the same fixture.

<a id="d-req-fn-033"></a>
- **REQ-FN-033** — `commits` dedupe on `sha` **per repo**, keeping the first and counting the collapsed duplicates (BRD-26).
  - *Acceptance:* two repos sharing a short sha keep both records; re-parsing the same raw file collapses to the same count; the collapsed count is reported per stream.

<a id="d-req-fn-034"></a>
- **REQ-FN-034** — `sessions` dedupe per `session_id`, keeping the record with the highest `output_tokens` and, on a tie, the latest `ts` (OpenCode records are cumulative snapshots) (BRD-27).
  - *Acceptance:* replaying an earlier snapshot of the same session never lowers the stored figure; the tie-break is deterministic.

<a id="d-req-fn-035"></a>
- **REQ-FN-035** — `runs` dedupe on `ts+app+cmd` and `gates` on `ts+app+req_id+run_id`, so re-parsing never double-counts (BRD-28).
  - *Acceptance:* parsing the same file twice inserts nothing the second time; the unique indexes encode exactly these keys.

<a id="d-req-fn-036"></a>
- **REQ-FN-036** — `backfilled`, `inferred`, `project_type`, `project_type_inferred`, `harness`, `tokens_scope` and every §2.5 optional field are preserved verbatim and typed; an absent optional is stored as `NULL`, never `0` or `false` (BRD-29).
  - *Acceptance:* "not captured" is distinguishable from "zero" for every optional at the column level; a round-trip through the store returns the original value or null.

<a id="d-req-fn-037"></a>
- **REQ-FN-037** — `TfLensAppManagerApiKey`, `TfLensAppManagerApiSecret`, `TfLensDbConnection` (required) and `TfLensGitHubToken` (optional) are read only from environment / user-secrets via the PascalCase env-var provider, never from files in the repository (BRD-8).
  - *Acceptance:* no secret value exists in `appsettings*.json` or any committed file; the non-secret settings (`TfLensAppManagerBaseUrl`, `TfLensAppManagerAppId`, `PollIntervalMinutes`, `DataRoot`) have documented defaults.

<a id="d-req-fn-038"></a>
- **REQ-FN-038** — Startup validates the configuration, applies the schema script, and refuses to run when a required secret is missing or the database is unreachable, logging a **redacted** reason (BRD-9).
  - *Acceptance:* the process exits non-zero rather than serving a degraded app; the log names which setting is missing without printing its value or the connection string.

<a id="d-req-fn-039"></a>
- **REQ-FN-039** — `DataRoot` (default `data/`) can be overridden and governs the raw archive, the reports folder and `prices.json` (BRD-11).
  - *Acceptance:* all three paths derive from the configured root; no hard-coded `data/` remains in the code paths that write.

<a id="d-req-fn-040"></a>
- **REQ-FN-040** — A multi-stage .NET 10 Dockerfile produces one image, run with `data/` and `logs/` volumes and env-var secrets (BRD-77).
  - *Acceptance:* the image builds from a clean clone; the container starts with only the documented environment variables set; both volumes are declared and writable.

<a id="d-req-fn-041"></a>
- **REQ-FN-041** — `/healthz` is anonymous and reports database reachability and the age of the last successful sync — **nothing else** (BRD-78).
  - *Acceptance:* it returns 200 with those two facts when healthy and a non-200 when the database is unreachable; it discloses no version, no configuration, no repo names and no user data.

<a id="d-req-fn-042"></a>
- **REQ-FN-042** — A README states the out-of-scope list from BRD §3 **verbatim** and documents the run / rebuild / sync / export commands (BRD-79).
  - *Acceptance:* the out-of-scope bullets match BRD §3 word for word; all four commands are shown with their real invocation (`dotnet TfLens.dll …` / `docker exec …`).

<a id="d-req-fn-043"></a>
- **REQ-FN-043** — `DECISIONS.md` is created at day-1 build and records the storage choice (Dapper + PostgreSQL, superseding SQLite), the dedupe keys, the parser version scheme, anything cut for the timebox, and every parity run (BRD-80).
  - *Acceptance:* all five categories are present from the first commit that ships code; each parity pass appends an entry (REQ-FN-063).

<a id="d-req-fn-044"></a>
- **REQ-FN-044** — `dotnet TfLens.dll sync` runs a one-off headless sync (BRD-81).
  - *Acceptance:* the verb runs the same `SyncAllAsync` code path as the poller and exits with a status reflecting per-repo outcomes.

<a id="d-req-fn-045"></a>
- **REQ-FN-045** — `docker compose` runs TfLens beside a **PostgreSQL 16** service with the Postgres data directory volumed; the app applies `database/001-schema.sql` **idempotently** at startup and reads its connection string from `TfLensDbConnection` (BRD-111, ADR-015).
  - *Acceptance:* `docker compose up` from a clean state produces a working app and schema without manual steps; re-running the schema script on an existing database is a no-op; there is no migration framework.

### Phase 2 — engine, reports, export, parity

<a id="d-req-fn-046"></a>
- **REQ-FN-046** — Every figure is computed at request time from the stream tables; no derived value is ever written back into a stream table (BRD-30).
  - *Acceptance:* the stream tables contain only parsed source fields plus `Overflow`; the memoised `AnalysisResult` lives in process memory keyed by `(userId, framework)` and is discarded on invalidation.

<a id="d-req-fn-047"></a>
- **REQ-FN-047** — Live and backfilled records never pool for first-pass rate, gate catch distribution or escape rate: the result exposes `Live[projectType]` and `Backfilled[projectType]` and has **no `Total` slot** and no disabling flag (BRD-31, ADR-007).
  - *Acceptance:* the result type cannot express a combined figure — it is a shape constraint, not a runtime check; no configuration, query parameter or UI control produces one.

<a id="d-req-fn-048"></a>
- **REQ-FN-048** — First-pass rate, gate catch distribution and escape rate never pool across `project_type`; records with `project_type_inferred: true` are segmented as **`unclassified`**, never silently as `app` (BRD-32).
  - *Acceptance:* an inferred record appears only under `unclassified`; there is no "all types" aggregation anywhere in the engine.

<a id="d-req-fn-049"></a>
- **REQ-FN-049** — Any `req_id` with at least one backfilled record is excluded from the live first-pass rate (its live `attempt` restarts at 1), and the excluded REQ ID list is returned for display (BRD-33).
  - *Acceptance:* the excluded set matches `tf-metrics.sh` exactly on the same dataset; the list is exposed in full to the UI and the export.

<a id="d-req-fn-050"></a>
- **REQ-FN-050** — Any metric with fewer than 3 supporting records is `InsufficientData(n)` — a distinct case of the `Figure` type that **cannot carry a value** (BRD-34).
  - *Acceptance:* the type makes rendering a number in that case impossible; every consumer (pages and export) renders it as the literal text `insufficient data (n=…)` with the correct n.

<a id="d-req-fn-051"></a>
- **REQ-FN-051** — `cost_usd` never pools across harness; `Pooled.CostUsd` is always `null` (BRD-35).
  - *Acceptance:* the field is null on every code path, matching the reference's contract; real dollars exist only in the per-harness `opencode` figure.

<a id="d-req-fn-052"></a>
- **REQ-FN-052** — Late-added gates (`perf`, since 2026-08-10) report `ran` — records whose `gates_run` contains the gate — beside `caught`, and their share of the raw distribution is never presented as a catch rate (BRD-36).
  - *Acceptance:* `ran` and `caught` are separate values in the result; no code path divides `caught` by the total distribution for a late gate.

<a id="d-req-fn-053"></a>
- **REQ-FN-053** — The poolable metrics (rework ratio, batch size median, REQ throughput median in REQs/hour, tokens total, tokens per Verified REQ, commit cadence, duplicates collapsed) follow SCHEMA.md §8 and the reference's rounding (`%.0f%%`, 2 dp throughput, 1 dp tokens per Verified) (BRD-37).
  - *Acceptance:* each formula and its rounding match `tf-metrics.sh` digit for digit on the fixture set and on the parity dataset.

<a id="d-req-fn-054"></a>
- **REQ-FN-054** — A unit test feeds the checked-in fixture streams to the engine and asserts equality with a checked-in `reference.json` produced by `tf-metrics.sh` on the same fixtures (BRD-38).
  - *Acceptance:* the test runs on every build and fails on any divergence; the fixtures and `reference.json` are committed under `tests/`; provenance-separation cases (live/backfilled, inferred type, taint) are among the fixtures.

<a id="d-req-fn-055"></a>
- **REQ-FN-055** — Framework is a stored, mandatory provenance axis: `UserRepo.Framework` (`techieflow` | `playbook`) is set at connect time from the telemetry path, every engine read takes `(UserId, Framework)`, and no figure pools across frameworks (BRD-108, ADR-016).
  - *Acceptance:* no engine method can be called without a framework; the header switch is the only way to change it; a cross-framework total cannot be expressed by the result type.

<a id="d-req-fn-056"></a>
- **REQ-FN-056** — `SnapshotExporter` writes `data/reports/<userId>/<date>/snapshot.md` (human, sectioned exactly like the pages) and `tflens.json` (machine) for the signed-in user and selected framework (BRD-63).
  - *Acceptance:* both files are written atomically for the same analysis; the markdown's sections mirror the report pages; one snapshot is produced per framework.

<a id="d-req-fn-057"></a>
- **REQ-FN-057** — `dotnet TfLens.dll export [--date yyyy-MM-dd]` produces the identical files headlessly (BRD-64).
  - *Acceptance:* the verb and the button share the exporter, so the parity run uses exactly the code the pages use; `--date` controls the output folder name.

<a id="d-req-fn-058"></a>
- **REQ-FN-058** — `tflens.json` uses the same key layout as `tf-metrics.sh --rollup --json` (`per_repo`, `tainted_reqs`, `live`, `backfilled`, `pooled`) plus an `extras` object (harness, routing, repricing) and a `parity` object carrying the last recorded parity run (BRD-65).
  - *Acceptance:* `tools/parity-compare.py` can walk the reference's keys against it without a mapping layer; `extras` and `parity` are the only additional top-level keys.

<a id="d-req-fn-059"></a>
- **REQ-FN-059** — The snapshot never mixes provenances in one figure and labels every estimate in **both** files (BRD-66).
  - *Acceptance:* no markdown or JSON value combines live with backfilled, or one `project_type` with another, or one framework with another; the repricing figure carries the "estimate — tokens × rate card, not measured spend" wording in the markdown and an explicit estimate marker in the JSON.

<a id="d-req-fn-060"></a>
- **REQ-FN-060** — A parser version is stamped into the build and into every export (BRD-68).
  - *Acceptance:* the version is emitted in `tflens.json` and in the markdown header; it changes when the parser or engine changes, which is what makes the quotable banner (REQ-UI-033) meaningful.

<a id="d-req-fn-061"></a>
- **REQ-FN-061** — `tools/parity-compare.py reference.json tflens.json` compares **key by key** (not a text diff) — per-repo record counts per stream and backfilled counts, commit duplicates collapsed, the tainted-REQ set, first-pass rate / gate catch distribution / escape rate per `project_type` live and backfilled separately, late-gate coverage (`ran` / `caught` per gate), every poolable metric, and every `insufficient data (n=…)` marker with its n — exiting non-zero on any mismatch (BRD-69).
  - *Acceptance:* key order and formatting differences do not produce a diff; a figure the reference refuses to print must also be refused by TfLens or the compare fails; exit code is 0 only on an empty diff.

<a id="d-req-fn-062"></a>
- **REQ-FN-062** — The dataset SHAs for the last sync are readable from both the export (`per_repo`) and the Coverage page, so the reference dataset can be pinned (BRD-70).
  - *Acceptance:* the SHAs in the export match `sync_state` and the Coverage page for the same moment; they are full enough to `git checkout` the exact dataset.

<a id="d-req-fn-063"></a>
- **REQ-FN-063** — Each passing parity run is recorded in `data/parity-last.json` (date, dataset SHAs, `tf-metrics.sh` hash, parser version, compare output) and by the operator in `DECISIONS.md` (BRD-71, BRD §13 step 6).
  - *Acceptance:* the file is written only on an empty diff; the quotable banner reads it; a reference-script change invalidates the stamp because the script hash is part of the record.

<a id="d-req-fn-064"></a>
- **REQ-FN-064** — The metrics with no oracle (harness comparison, routing drift, repricing) are spot-checked by hand against raw JSONL **once** and the check is recorded in `DECISIONS.md` (BRD-72).
  - *Acceptance:* the record names the repo, the SHA, the figures checked and the raw values they were checked against; these are the only permitted permanent differences from the reference (`extras`).

### Phase 3 — Playbook as a first-class framework

<a id="d-req-fn-065"></a>
- **REQ-FN-065** — For Playbook repos that carry it, fetch `verification/telemetry/events.ndjson` (and the joiner output if committed), archive it raw, and parse it into separate `PbEvent` tables with overflow (BRD-73).
  - *Acceptance:* the raw-before-parse rule (REQ-FN-027) applies unchanged; the Playbook tables are physically separate from the four TechieFlow stream tables; unknown fields land in overflow.

<a id="d-req-fn-066"></a>
- **REQ-FN-066** — Playbook `phase_gate` data lives in separate tables and separate charts from TechieFlow `gate` data — never a shared column or chart (BRD-74, SCHEMA.md §11).
  - *Acceptance:* no query joins the two axes; no rendered chart or table has a column fed by both; the separation is structural, not a display convention.

<a id="d-req-fn-067"></a>
- **REQ-FN-067** — Produce the Playbook-native equivalents that the report pages render: the three questions per `phase_gate` (plan review · verify · gap report · post-verification bugs), phase token and cost totals, and the main-vs-subagent split via `parentID`, plus routing/tokens by model where present (BRD-75).
  - *Acceptance:* each figure obeys the same minimum-n and never-pool rules as the TechieFlow engine; a `parentID` chain resolves sub-agent sessions to their main session; cost renders `—` where the events carry none.

<a id="d-req-fn-068"></a>
- **REQ-FN-068** — Schema discovery first: the adapter's **first** task parses the real `events.ndjson` and records the observed field names in `DECISIONS.md` **before** any column or chart is fixed (BRD-76, ADR-010).
  - *Acceptance:* the DECISIONS.md entry predates the adapter's schema commit; no column is declared from the brief's description alone (no sample existed at day-1).

<a id="d-req-fn-069"></a>
- **REQ-FN-069** — A Playbook repo emitting schema-v1 streams (`docs/metrics/*.jsonl`) flows through the **same** parser, engine and pages as a TechieFlow repo, tagged `framework: playbook` at connect time — no new code beyond the tag and the switch (BRD-109).
  - *Acceptance:* such a repo produces the full report set with no adapter involved; its figures never pool with TechieFlow figures (REQ-FN-055).

<a id="d-req-fn-070"></a>
- **REQ-FN-070** — The full report set for `events.ndjson` repos — Coverage, three questions, harness, routing and the snapshot export — is produced from the separate Playbook tables (BRD-110).
  - *Acceptance:* every report page has a working Playbook state (not an empty state) once the adapter lands; the export writes one snapshot per framework; the retired `/playbook` page is not resurrected.

## RAG / AI requirements (→ /techierag)

**None.** TfLens has no AI, LLM, embedding, vector-search or chat feature — see ADR-003 ("No vector store / RAG — TfLens has no AI features") and BRD §3, which lists no such capability in scope. `/techierag` is **not** invoked by any build phase of this app, and `PROJECT-STATUS.md` records TechieRag as not used. If a future amendment introduces one, `*amend-docs` will add `REQ-RAG-001` onward here.

## Non-functional

<a id="d-req-nfr-001"></a>
- **REQ-NFR-001** — Report pages render from the memoised analysis within a second for the expected data volume (tens of thousands of records across ≤10 repos), and a full sync of 5 repos completes in under 30 s on a normal connection (BRD-82).
  - *Acceptance:* page render from cached analysis, cold analysis ≤ 3 s for 50k records, sync of 5 unchanged repos ≤ 5 s (SHA lookup only), rebuild of 5 repos × 20 SHAs ≤ 60 s; perf-budget: p95 load <= 1500ms @ concurrency 1

<a id="d-req-nfr-002"></a>
- **REQ-NFR-002** — Security: cookie auth on every page (HttpOnly, Secure, SameSite=Lax); antiforgery on forms; secrets only via environment; the PAT is fine-grained contents-read; no inbound API; HTTPS terminated by the VPS proxy with `ForwardedHeaders` configured accordingly (BRD-83).
  - *Acceptance:* the auth cookie carries all three flags; every form posts with an antiforgery token; the app exposes no ingestion or capture endpoint of any kind; forwarded-headers middleware is registered before authentication so the scheme is correct behind the proxy.

<a id="d-req-nfr-003"></a>
- **REQ-NFR-003** — The AppManager secret, the database connection string, the GitHub PAT and every AppManager token are never logged, displayed or exported (BRD-10).
  - *Acceptance:* startup and sync logs redact them; `sync_state.LastError` carries a status code and short reason only; no exception page, health response, export file or UI surface reveals any of them; a grep of the log output after a full sync + failed sync finds none of the four.

<a id="d-req-nfr-004"></a>
- **REQ-NFR-004** — Privacy: TfLens displays and stores only what the streams carry (IDs, counts, durations, verdicts, short SHAs) — no requirement text, no commit subjects, nothing from `src/`; the `Overflow` column is never rendered, only its field names (BRD-84).
  - *Acceptance:* no page or export contains prose harvested from a connected repo; the unknown-fields panel lists names only (REQ-UI-016); commit records carry the sha and metadata, not the subject line.

<a id="d-req-nfr-005"></a>
- **REQ-NFR-005** — Accessibility and theme: TrBlazeUI components with semantic markup; every figure has a text equivalent (charts are supplementary); `insufficient data` and `estimate` are text labels, never colour alone; Sync / Export / Rebuild / user menu are keyboard-reachable; dark mode is the default on first visit with the choice persisted per user (BRD-85).
  - *Acceptance:* every chart on the site is accompanied by a table or key/value list carrying the same numbers; the four named controls are reachable and operable by keyboard alone; no meaning is conveyed by colour alone; first visit renders dark.

<a id="d-req-nfr-006"></a>
- **REQ-NFR-006** — Observability: Serilog wired at startup **before the host builds**, with a rolling file sink under `logs/` (`logs/tflens-.log`, daily, 14 files retained) plus console; unhandled exceptions logged at the boundary; `Log.CloseAndFlush()` on exit; sync outcomes logged per repo with counts and SHAs only (BRD-86, Coding Standards §Logging).
  - *Acceptance:* logs appear in the mounted `logs/` volume with daily rolling and 14-file retention; a crash during startup is still captured; sync log lines contain no repo content, no token and no user email.

<a id="d-req-nfr-007"></a>
- **REQ-NFR-007** — Reliability: a failing repo never fails a sync; a failing sync never affects served pages (the last good analysis stays); the database can be rebuilt from `data/raw/` at any time with identical counts (BRD-87).
  - *Acceptance:* with one repo returning 403 the other repos still sync and the pages still render; the cached analysis is replaced only on a successful sync; rebuild-count identity is proven by REQ-FN-029.

<a id="d-req-nfr-008"></a>
- **REQ-NFR-008** — Testability: the engine and parser live in `TfLens.Core` with no web dependency; fixture JSONL is checked in under `tests/`; Blazor screens carry stable `data-testid` ids for Playwright (BRD-88, ADR-006).
  - *Acceptance:* `TfLens.Core` references no ASP.NET or Blazor package and is driven by the CLI verbs and unit tests without a browser; every `data-testid` named in the UI REQs above exists in the rendered DOM.

<a id="d-req-nfr-009"></a>
- **REQ-NFR-009** — Integrity: the provenance rules (REQ-FN-047..052, from BRD-31..36) have **no** configuration switch, **no** query parameter and **no** UI toggle that relaxes them (BRD-89).
  - *Acceptance:* no setting, flag, route parameter or control anywhere in the app changes pooling, taint exclusion, minimum-n or the null pooled cost; the rules are enforced by result-type shape (ADR-007) so relaxing them would require a type change, not a config change.

<a id="d-req-nfr-010"></a>
- **REQ-NFR-010** — Cross-user isolation is proven, not assumed: an integration test signs in two users, connects the same public repo to both, and asserts that neither can see the other's repos, rows, raw archive, reports or cached figures (BRD-102, BRD-83; BRD §16 risk "Cross-user data leak through a missed `UserId` filter").
  - *Acceptance:* the test fails if any store read, cache key, raw path or reports path omits `userId`; it runs in CI alongside the engine tests.
