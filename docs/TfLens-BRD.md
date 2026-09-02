# TfLens — Business Requirements

<!-- AGENT-ONLY AUTHORING NOTES — never render as visible text.
  STABLE IDS: every requirement has a BRD-{N} ID; append-only across revisions.
  DEPTH MANDATE: human document; §9 Feature catalog is the heart; one-liners only in §10.
  MERMAID MANDATE: html-render-shell.md §5.5 — quote every label; never use `end` as a node id.
-->

## Table of Contents

1. [Executive summary](#executive-summary)
2. [Business objectives](#business-objectives)
3. [Scope](#scope)
4. [Development status](#development-status)
5. [Stakeholders / users](#stakeholders-users)
6. [Context diagram](#context-diagram)
7. [User journey — primary use case](#user-journey-primary-use-case)
8. [Component sketch](#component-sketch)
9. [Feature catalog](#feature-catalog)
   - [F-SHELL: App shell and navigation](#f-shell-app-shell-and-navigation)
   - [F-AUTH: AppManager identity — login, registration, sessions](#f-auth-appmanager-identity-login-registration-sessions)
   - [F-REPOS: Source management — fetch public repos, or import metric files](#f-repos-source-management-fetch-public-repos-or-import-metric-files)
   - [Screen inventory — every screen, what it is for, and its mockup](#screen-inventory-every-screen-what-it-is-for-and-its-mockup)
     - [What "Gate outcomes" shows — and why it is no longer called "Three questions"](#what-gate-outcomes-shows-and-why-it-is-no-longer-called-three-questions)
   - [F-CFG: Configuration and secrets (retired 2026-08-26)](#f-cfg-configuration-and-secrets-retired-2026-08-26)
   - [F-SYNC: Repo puller — background sync and Sync now](#f-sync-repo-puller-background-sync-and-sync-now)
   - [F-RAW: Raw archive and rebuild](#f-raw-raw-archive-and-rebuild)
   - [F-PARSE: Parser to PostgreSQL with dedupe and overflow](#f-parse-parser-to-postgresql-with-dedupe-and-overflow)
   - [F-ENGINE: Metrics engine with provenance rules](#f-engine-metrics-engine-with-provenance-rules)
   - [F-COVER: Coverage / health page](#f-cover-coverage-health-page)
   - [F-3Q: Gate outcomes page](#f-3q-gate-outcomes-page)
   - [F-HARN: Harness comparison page](#f-harn-harness-comparison-page)
   - [F-ROUTE: Routing and economics page](#f-route-routing-and-economics-page)
   - [F-MISS: Misses and rework economics](#f-miss-misses-and-rework-economics)
   - [F-EFFORT: Phase effort and efficiency — what each phase cost](#f-effort-phase-effort-and-efficiency-what-each-phase-cost)
   - [F-EXPORT: Weekly snapshot export](#f-export-weekly-snapshot-export)
   - [F-PARITY: Parity check against tf-metrics.sh](#f-parity-parity-check-against-tf-metrics-sh)
   - [F-FRAMEWORK: Playbook as a first-class framework — the full report set (was F-PB)](#f-framework-playbook-as-a-first-class-framework-the-full-report-set-was-f-pb)
   - [F-OPS: Container, configuration, health, docs and decisions](#f-ops-container-configuration-health-docs-and-decisions)
10. [Functional requirements (BRD ledger)](#functional-requirements-brd-ledger)
11. [Non-functional requirements](#non-functional-requirements)
12. [Constraints & assumptions](#constraints-assumptions)
13. [Parity check — the mandatory acceptance test](#parity-check-the-mandatory-acceptance-test)
14. [Definition of done](#definition-of-done)
15. [Success metrics](#success-metrics)
16. [Risks](#risks)
17. [Glossary](#glossary)

## 1. Executive summary

TfLens is a read-only lens over the development telemetry that **both** of the owner's frameworks — TechieFlow and the AI-First-Playbook — **already emit**. Every TechieFlow-managed repository carries five append-only JSONL streams under `docs/metrics/` (`runs`, `gates`, `sessions`, `commits` and — since 2026-08-28 — `misses`; schema v=1, defined in `.tfcore/telemetry/SCHEMA.md`); Playbook-managed repositories emit `verification/telemetry/events.ndjson` today and will converge on the same schema. Today the only consumer of those streams is a shell script, `tf-metrics.sh --rollup`, which prints a segmented text report. TfLens pulls the streams from GitHub, stores them in PostgreSQL (amended 2026-08-26), and renders the same figures — plus a few the script does not compute — as an authenticated Blazor Server dashboard with a **Framework switch** (TechieFlow | Playbook) on every report page, and a weekly snapshot export whose numbers can be quoted in public writing. The Playbook report set is Phase 3, as is the miss / rework report (F-MISS, amended 2026-08-28) and the phase-effort report (F-EFFORT, amended 2026-09-01).

**Both frameworks now publish an effort contract** (amended 2026-09-01). TechieFlow's `runs.jsonl` gained three §2.6 fields on 2026-08-31 — `subagent_runs`, `tokens_out_subagents`, `model_tokens_out` — and an oracle mode, `tf-metrics.sh --phases`, that groups every run by `cmd`; the AI-First-Playbook shipped a normalized **schema-2 `phase-metric`** record carrying wall-clock elapsed, observed active time, a full per-model breakdown, stable phase identity and spawned-child counts, alongside a normalized miss export. Together they answer *"what did each phase cost — in time, tokens, models and subagents?"*, and TfLens renders that as a seventh report page, `/effort`. The answer is bounded in three places at once, and **every bound is on screen beside its figure**: a run whose token window was never computed is not a zero, a run whose window never read the subagent transcripts has not reported "no subagents", and a Playbook execution whose window ended at EOF has no duration at all. Reporting any of those as `0` would be the same defect this product exists to prevent, arriving on a third stream.

TfLens is free and open source, and it is **multi-user by design** (amended 2026-08-26): anyone who uses TechieFlow or the Playbook can sign in, connect their own **public** GitHub repos, and see the reports for their data. Identity is delegated to the owner's **AppManager** service (`https://appmgrapi.techierathore.com`, Application Id 1) — TfLens stores no passwords; every user is an AppManager `Manager` for this application and no licensing, feature or payment capability is used. Each user's repos, raw archive, parsed rows and reports are isolated from every other user's.

It builds **no capture layer, no ingestion API, and no per-machine agents**. Capture is the frameworks' job and already works across Claude Code, OpenCode, and (via `harness: null`) anything else that runs the tasks. TfLens never writes to any repository; it reads with a fine-grained, contents-only token.

The name is deliberate: "Analyst" collides with TechieFlow's analyst agent and "TfMetrics" collides with `tf-metrics.sh`. A *lens* changes nothing it looks at. The dangerous failure mode of this product is not a crash but a **plausible wrong number** — a backfilled record leaking into a live rate, a `library` record pooled into an `app` gate distribution — that gets exported, quoted, and cannot be defended. The provenance rules of SCHEMA.md §6 are therefore enforced in code with no switch to disable them, and a mandatory parity test against `tf-metrics.sh` is the acceptance gate (§13).

Plan context (from `docs/ravi-90day-positioning-plan-v2.4.2.md`): TfLens is the A-V verification vehicle — a real side project built through the full TechieFlow phase sequence, funded from side-project hours, never a plan deliverable. Its exported numbers feed the plan's Numbers table, the B1 portability story (harness comparison), and the B3 token-economics post (counterfactual repricing).

## 2. Business objectives

- Turn the existing telemetry into a dashboard the owner can open in a browser, within a 1–2 day timebox, without adding any capture surface to the frameworks.
- Produce **quotable numbers**: a weekly snapshot (markdown + JSON) that never mixes provenances and that has passed an exact parity diff against `tf-metrics.sh --rollup` on the same dataset.
- Make the telemetry's own health visible: a Coverage page that says, per repo, whether a clone has stopped pushing or lacks hooks — before any other figure is trusted.
- Render the B1 story as data (per-harness volumes, tokens, verdict mix, OpenCode-only dollars) and the B3 claim basis (tokens repriced as if every run used the most expensive observed model, labelled *estimate*).
- Serve as the A-V verification build: every TechieFlow phase runs on TfLens itself, gates enforced, so the framework's telemetry records its own dashboard being built.

## 3. Scope

**In scope**

- Pulling `docs/metrics/{runs,gates,sessions,commits,misses}.jsonl` (and, for Playbook repos, `verification/telemetry/events.ndjson`) from the **public** GitHub repos each signed-in user connects on the `/repos` screen, on a poll interval and on demand; raw archive; PostgreSQL store (Dapper); rebuild from raw; per-user data isolation. *(`misses` added 2026-08-28 — F-MISS.)*
- The same report set for both frameworks, selected by a Framework switch and never pooled across frameworks (Playbook set: Phase 3).
- **Two ways to add a source** (amended 2026-08-28, F-IMPORT): **Fetch via API** for public repos, or **Import metric files** — the user uploads the telemetry their framework already writes to disk, so **private and corporate repositories are reachable without TfLens ever holding a credential for them**. Both land in the same tables through the same parser; the Repos screen shows which is which.
- AppManager-backed identity: email/password login, self-registration, forgot/reset password, session cookie with server-side token refresh; every user is `Manager`; demo user `TfLensDemo`.
- Parser with SCHEMA.md-exact columns, JSON overflow for unknown fields, and idempotent dedupe on the streams' natural keys — including the one stream (`misses`) whose records do not all share a shape.
- **Phase-effort telemetry from both frameworks** (amended 2026-09-01, F-EFFORT): TechieFlow's three new `runs.jsonl` §2.6 fields and the `--phases` oracle block; the Playbook's normalized **schema-2 `phase-metric`** NDJSON (delivered through the existing **Import metric files** mode — the exporter reads a transient event file TfLens cannot reach, so its stdout is uploaded like any other bundle) and its normalized miss export. Both land on one page, `/effort`, under the Framework switch.
- Seven report pages (Coverage, Gate outcomes, Harness comparison, Routing & economics, **Misses & rework**, **Phase effort**, Snapshot export) computed at request time with the provenance rules enforced structurally. *(Misses & rework added 2026-08-28; Phase effort added 2026-09-01; both Phase 3.)*
- Weekly snapshot export to `data/reports/<date>/` as markdown + JSON.
- Parity tooling: machine-readable export in the reference's key layout, a compare script, and the DECISIONS.md record of each passing run.
- Phase 3: a separate Playbook adapter for `verification/telemetry/events.ndjson` with its own tables and a minimal page.
- Single-user cookie auth; Serilog file logging; Dockerfile; `/healthz`.

**Out of scope (explicit — recorded in the README)**

- Any capture layer, machine-to-machine ingestion API, OTLP endpoint, or per-machine agent. *(Amended 2026-08-28 — narrowed, not removed: the **Import metric files** mode on `/repos` is an authenticated file-picker on a page a human is signed into, and it is the **only** inbound path. Nothing can push data into TfLens automatically, no endpoint accepts an unauthenticated post, and neither framework is asked to grow an export command — the user uploads the files TechieFlow and the Playbook already write to disk. BRD-139 bounds the surface.)*
- Writing anything to any repository, ever.
- VPS / infra configuration (supplied separately).
- Reading a private GitHub repo **over the API** (this release is public-repo-only for fetching; a per-user PAT is a later release). *(Amended 2026-08-28: a private or corporate repo is no longer out of reach — its telemetry is added through **Import metric files** instead, which needs no credential, no network access to the repo, and no change to the repo itself. What stays out of scope is TfLens authenticating to a private repo and pulling from it.)*
- AppManager licensing, subscriptions, feature flags, payments, issues — none are called.
- GitHub SSO — **deferred to Phase 2** (BRD-94): AppManager has no external-login endpoint, so it needs a bridge or an AppManager change first.
- Roles beyond `Manager`; sharing a user's reports with another user.
- Any estimate presented as a measurement: no rate-card dollars anywhere except the explicitly labelled repricing and rework-estimate figures.
- **Any blended rework-cost figure** (amended 2026-08-28): measured (`cost_attribution: sole`) and apportioned (`shared:n`) miss cost are never summed into one number, in the page, the export or parity — see BRD-122, BRD-130.
- **Writing to any telemetry stream**, including `misses.jsonl`: TfLens consumes the miss stream and never emits into it. Recording a miss is TechieFlow's `*log-miss`, not TfLens's job.
- **Any per-REQ or per-feature effort figure** (added 2026-09-01): the unit of work is **the run**, not the ticket. A `*build-phase` run touching eight REQs has one duration and one token window; dividing it eight ways is arithmetic dressed as measurement — the same distinction `cost_attribution` already draws for misses. Both producers state this as a standing non-goal and neither emits a per-REQ timing field. See BRD-169.
- **Any actor-grouped figure** (added 2026-09-01): the Playbook's records carry an `actor`, and no TfLens surface — page, API, export or parity — groups quality, rework, miss, effort, token, time or cost by it. Both AIFP contracts state this as a hard rule; see BRD-168.
- **Running a framework's exporter.** TfLens does not execute `playbook-telemetry.mjs`, `tf-metrics.sh`, or any framework tooling against a user's repository. It reads what the frameworks have already written, and where that output is transient rather than committed it is **uploaded** through the existing import mode (BRD-153) — no node dependency, no execution surface, no change asked of either framework.

## 4. Development status

**Amended 2026-09-01 — F-EFFORT appended, nothing built yet.** The 2026-09-01 amendment adds `BRD-145`..`BRD-169` (phase effort and efficiency, both frameworks) and their checklist rows at `Not started`, which reopens **F-SHELL** (an eighth nav item), **F-PARSE** (three new `Run` columns, two new Playbook axis columns on the miss tables), **F-FRAMEWORK** (the switch now spans seven report pages, and the Playbook axis of `/misses` becomes fillable), **F-PARITY** (the oracle's `phases` block joins the §13 gate) and **F-REPOS** (the import mode recognises two more stream file names). No code has been written for any of it; the rollup below is otherwise unchanged from 2026-08-29 and the counts are the pre-amendment counts plus the new `Not started` rows. Prior snapshot follows.

**Snapshot as of 2026-08-29 (late)**, rolled up from a `*build-phase` that appended BRD-143/BRD-144, built both, and chained `*verify all` — **137 of 150 requirements `Verified`, 10 `Needs re-verify`, 2 `Implemented`, 1 `N/A`**. The count went **down** from 145 on purpose: the new `mockup-parity` gate (BRD-144) demoted 8 screens that had passed acceptance, render-truth and visual-truth while still drifting from their approved design. `REQ-FN-058` returned to `Verified` and §13's inverted `project_type` is fixed. **§13 parity currently FAILS** (2 findings, one defect: the export inverts `project_type`/`stale_types` on the reclassified TfLens repo), so the export is **not quotable** until `REQ-FN-058` is fixed and parity is re-run. The five that are not: `REQ-FN-067` / `REQ-FN-070` (Playbook-native figures, owner-gated on a repository that emits `events.ndjson`), `REQ-NFR-019` / `REQ-NFR-020` (both `Planned`, never built — **now owned by BRD-143 / BRD-144**, appended by the 2026-08-29 amendment before they were built, which reopens F-OPS to `Partial`), and `REQ-FN-012` (`N/A`, SSO deferred by BRD-94 / ADR-012). Live, per-requirement status: see `PROJECT-STATUS.md` and the **Requirements Status** table in `docs/TfLens-Checklist.md` (created by `*split-brd`).

| Feature (F-code) | Phase | Status | % | Notes |
|------------------|-------|--------|---|-------|
| F-SHELL: App shell and navigation | 1 | Partial | 95 | **`REQ-UI-010` demoted 2026-08-29** by the new mockup-parity gate: the Framework switch renders as plain text with no track where the mockup draws a badge, on all six report pages — 12 findings, and the whole verdict for three screens. Function is unaffected; this is fidelity. **Reopened 2026-09-01 by BRD-151:** an **eighth** nav item, *Phase effort*, sits between *Misses & rework* and *Snapshot export*, and the Framework switch now spans **seven** report pages — neither is built. Prior: Collapsible icon sidebar (Repos first), header with Sync now + user menu, dark-first theme. **Seven nav items since 2026-08-28** — *Misses & rework* between *Routing & economics* and *Snapshot export*; the Framework switch spanned six report pages |
| F-AUTH: AppManager identity — login, registration, sessions | 1 (SSO: 2, deferred) | Done | 100 | `/login`, `/register`, `/reset-password`, sessions and sign-out all verified. `REQ-FN-003` forgot/reset: all three acceptance clauses now proven (enumeration-safety, both dead-link codes collapsing to one outcome, token never logged — two real token leaks found and fixed 2026-08-27), but the API-key header supplying the app scope is now exercised: the owner re-provisioned AppManager on 2026-08-28 and both **AM-001** (role code silently ignored) and **AM-002** (`403 NO_APP_ACCESS` on profile) are resolved server-side. `REQ-NFR-012` / BRD-142 add a documented, repeatable account-restore procedure (`provision-test-accounts`) so a rotated password can no longer block the authenticated suite. GitHub SSO deferred (`REQ-FN-012`, N/A) |
| F-REPOS: Source management — fetch public repos, or import metric files | 1 (import mode: 3) | Done | 100 | Fetch-via-API half is Done: list, Connect+Validate, purge and per-user isolation verified. **Import-metric-files mode built and verified 2026-08-28** (BRD-131..BRD-141): mode fork, drop zone, preview-before-commit (writes nothing until Import), bundle sha256 as dataset identity, idempotent re-import, poller skip, rollup refusal, and a bounded upload surface (`REQ-NFR-014`) that is the app's only inbound path. Private and corporate repos are now reachable with no credential and no network route to the repo. `REQ-UI-013` closed 2026-08-27 — Escape now aborts the remove dialog (TrBlazeUI ships no Escape support at all: TR-014, worked around in `Repos.razor.js`); the same fix cured the connect dialog's post-validation Escape. **Reopened 2026-09-01 (BRD-153):** the import mode must recognise two more entries — the Playbook's normalized `phase-metric` NDJSON and its normalized miss export — which is one entry in a file-name table, not a code path (BRD-132 still holds: there is no second ingest path) |
| F-SYNC: Repo puller — background sync and Sync now | 1 | Done | 100 | `BackgroundService`, SHA-skip and per-repo error isolation all exercised live (2 of 7 repos failed in isolation) |
| F-RAW: Raw archive and rebuild | 1 | Done | 100 | Verified by a real run: 14 raw files replayed → 279 rows, 1 duplicate collapsed, 0 invalid lines |
| F-PARSE: Parser to PostgreSQL with dedupe and overflow | 1 | Partial | 95 | One table per stream + `sync_state`; natural-key dedupe; Npgsql + Dapper. **Reopened 2026-09-01 (BRD-145, BRD-154, BRD-164, BRD-165):** three nullable `Run` columns for the §2.6 fields, three Playbook phase tables, and two nullable Playbook-axis columns (`ItemId`, `FoundPhaseGate`) plus a source-line-hash key on the existing miss tables. None built |
| F-ENGINE: Metrics engine with provenance rules | 2 | Done | 100 | Port of `tf-metrics.sh analyse()`; `Figure` type with `InsufficientData`; fixture parity test green |
| F-COVER: Coverage / health page | 2 | Done | 100 | Landing page; staleness per stream; unknown-fields report. **Extended 2026-08-28**: fifth `misses` stream row, per-repo source badge, bundle-sha identity, days-since-import staleness for imported sources, and the miss data-quality card (escapes-missing-why, orphan counts, reclassification split) |
| F-3Q: Gate outcomes page *(renamed 2026-09-01; ID retained)* | 2 | Done | 100 | Per `project_type`, live vs backfilled columns, taint list |
| F-HARN: Harness comparison page | 2 | Partial | 85 | **`REQ-UI-023` and `REQ-UI-025` demoted 2026-09-01** — they read `Verified` while owner UAT evidence (`docs/uatissuessc/Harness-*.png`, captured 2026-08-30) stood against them unaddressed; the evidence is now cited on both rows. Open: the totals bar chart's **raw axis** (`3000000000`, no unit scaling, no value labels — claude-code at 2.86B makes the other two bars invisible), an extra `Harness \| Total tokens` table, an **11th `Measured dollars` row** against the mockup's ten (an owner call — the row satisfies `REQ-UI-025`'s own acceptance, so acceptance and approved design conflict), and a two-line card title. All four are **composition** drift, which `mockup-parity` compares elements rather than composition and cannot see (TF-011). Prior: claude-code / opencode / codex; OpenCode-only dollars, no cross-harness total. **Corrected 2026-08-27** — the measured figure summed `cost_usd` over *run* records, which never carry it (SCHEMA.md §4 puts it on sessions), so it was structurally null; the earlier `$0.84` came from test pollution in the shared DB. Now $0.04 over 2 OpenCode session records |
| F-ROUTE: Routing and economics page | 2 | Done | 100 | Drift, tokens by model, repricing from `prices.json`, poolables |
| F-MISS: Misses and rework economics | 3 | Done | 100 | Built and verified 2026-08-28. Fifth stream (`misses.jsonl`, three record kinds), three tables, `MissMetrics` + the two provenance guards, the `/misses` page. All 29 miss figures are covered by the BRD §13 parity compare and agree with the oracle key for key. One deliberate divergence is recorded as **D-012 / TF-005** — an unrecorded token count stays unmeasured here, where the reference averages it as zero; latent, since every current dataset carries the field |
| F-EFFORT: Phase effort and efficiency | 3 | Planned | 0 | **Added 2026-09-01** (BRD-145..BRD-169) from three producer contracts that all shipped ahead of the consumer: `docs/Phase-Effort-Telemetry-TfLens.md` (TechieFlow, `runs.jsonl` §2.6 + `--phases`, 2026-08-31), `docs/Phase-Efficiency-TfLens-Contract.md` (Playbook schema-2 `phase-metric`) and `docs/Miss-Telemetry-TfLens-From-AIFP.md` (Playbook miss export). Nothing built. The whole feature rests on **three denominators that must be visible next to their figures** — `tokens_measured_n` for token totals, `fanout.observed_n` for subagent counts, and `complete` / `active_coverage` for Playbook durations — because on today's data the honest fan-out headline is **1 of 13 runs observed**, and a page that only looks right once the data is dense is a page nobody trusts in the meantime |
| F-EXPORT: Weekly snapshot export | 2 | Done | 100 | Button + `export` verb → markdown + JSON, both written for real |
| F-PARITY: Parity check against tf-metrics.sh | 2 | Done | 100 | `tflens.json` layout and `tools/parity-compare.py` verified both ways. **BRD §13 PASSES — the app's figures are quotable.** Re-run end to end 2026-08-28 at parser **1.2.0** after the framework shipped an oracle that reads the fifth stream (which correctly invalidated the 2026-08-27 stamp and took `/export` to NOT QUOTABLE on its own — the invalidation clause demonstrated in the field): `parity-compare.py` exits **0**, 0 findings, over 29 `misses` keys plus the existing set. Recorded as `DECISIONS.md` **P-003**; `data/parity-last.json` written and `/export` reads **QUOTABLE**. The 2026-08-27 SCHEMA.md §4-vs-§5 session-dedupe contradiction was fixed upstream (TF-001). **Superseded again 2026-08-29 (later) — the defect is FIXED.** `REQ-FN-058` is `Verified`: `DeclaredProjectType` now reads the newest declaration, and `project_type` / `stale_types` for the reclassified repo match the reference on both sides. One finding remains and is **not** a defect — `pooled.session_duplicates_collapsed` is a dataset-shape artefact (the app's whole archive vs one pinned snapshot per repo). **BRD-143 is now built (`REQ-NFR-019`, `Implemented`)** with three enforcement layers and a proven orphan-detection path, but it is not yet `Verified`: it cannot see a poisoned raw file replayed by `rebuild`. Prior note:  The post-purge re-run found the export inverts `project_type` / `stale_types` on the one reclassified repository (`REQ-FN-058`, root cause `MetricsEngine.DeclaredProjectType` reading first rather than newest), and the same session found 155 rows carrying fabricated provenance that the diff could only see because the counts disagreed — now owned by **BRD-143** / `REQ-NFR-019`, `Planned`. **Reopened 2026-09-01 (BRD-152):** the oracle grew a `phases` block that rides inside `--report --json` / `--rollup --json`, so it is already reachable by the existing invocation — but no TfLens figure is diffed against it yet, and every phase-effort figure ships **unverified** until it is |
| F-FRAMEWORK: Playbook as a first-class framework — full report set | 3 | Partial | 90 | `events.ndjson` fetch/parse, axis separation and schema-v1 reuse verified, and **all six** report pages now mount the Playbook state — `/misses` joined them on 2026-08-28 and `pb-misses` grades 12 controls clean. **`REQ-FN-067` / `REQ-FN-070` remain `Needs re-verify` on a single external cause**: no connected repository emits `verification/telemetry/events.ndjson`. `techierathore/AI-First-Playbook` publishes neither telemetry path, and the build-harness fixture that once supplied figures was removed on 2026-08-27 as fabricated — grading these `Verified` would mean restoring it. The Playbook axis correctly renders its empty state everywhere. **There is now a way out that does not need the repo to change**: the F-IMPORT mode (BRD-131..BRD-141) accepts an uploaded `verification/telemetry/` bundle, so the owner can supply Playbook telemetry without a credential or a network route to the repository. **Reopened 2026-09-01 (BRD-153..BRD-167):** the Playbook is no longer a schema-discovery problem — it publishes two normalized contracts, so `REQ-FN-067` / `REQ-FN-070` have a defined shape to be graded against for the first time, the switch spans seven pages, and the `/misses` Playbook axis stops being blanket-empty. The external cause stands: still no connected repository supplying Playbook telemetry, and the way out remains an **import**, not a repo change |
| F-OPS: Container, configuration, health, docs and decisions | 1 | Partial | 95 | Dockerfile + compose with PostgreSQL 16, settings/secrets, schema script, `/healthz`, README, DECISIONS.md all Done. **Reopened 2026-08-29 by BRD-144, then BUILT the same day** (`REQ-NFR-020`, `Implemented` 90%): the gate now exists and grades 21 screens against `docs/mockups/` at 1280 and 390, with a catch-proof that breaks a property in the browser and asserts the finding appears and clears. Its first run demoted **8 previously-`Verified` UI rows** on 44 findings, confirming mechanically what the owner had found by hand. **90% not 100%:** clause 3 is unwired — nothing yet writes a per-screen `mockup-parity` row into `gates_run`, so a screen can still reach `Verified` on render-truth and visual-truth alone. That wiring needs a `.tfcore/` change `REQ-NFR-018` forbids this repo from making (upstream TF-008) |

**Legend:** **Done** = shipped & working · **In progress** = actively being built · **Partial** = some sub-features done, others pending · **Planned** = not started. (Maps to the checklist's `Done (pre-existing)` / `In Progress` / `PARTIAL` / `Not Started`.)

## 5. Stakeholders / users

TfLens is multi-user (amended 2026-08-26). Every signed-in person is an AppManager `Manager` for Application 1 and sees only their own connected repos. The owner additionally wears the parity, author and ops hats.

| Role | Who | Needs | Key screens |
|------|-----|-------|-------------|
| **User** (any TechieFlow / Playbook user) | Anyone who registers (email/password via AppManager) — e.g. the demo account `TfLensDemo` | Sign in, connect their public repos, see health and the reports for their own data, export their snapshot | `/login`, `/register`, `/repos`, `/`, `/gate-outcomes`, `/harness`, `/routing`, `/export` |
| **Owner** (dashboard user) | The framework author, signed in like any other user | See whether telemetry is healthy, read the three questions per project type, compare harnesses, see routing drift and the repricing estimate, press Sync now | `/`, `/gate-outcomes`, `/harness`, `/routing` |
| **Parity operator** | The same person, at a terminal, before any number is quoted | Run `tf-metrics.sh --rollup --json` on a pinned dataset, export `tflens.json` for the same SHAs, run the compare script, record the pass in DECISIONS.md | `/export`, the `export` verb, `tools/parity-compare.py` |
| **Author** (consumer of the export) | The same person writing the weekly Numbers row, B1, B3 | A snapshot that never mixes provenances, states *estimate* where it estimates, and carries its parity stamp so it is quotable | `/export` output under `data/reports/<date>/` |
| **Ops** | The same person deploying the container | One image, one volume, env-var secrets, a health endpoint, rolling file logs | Dockerfile, `/healthz`, `logs/` |
| **Downstream frameworks** (not users) | TechieFlow, AI-First-Playbook | Nothing — TfLens never writes to them and never asks them to change | — |

Onboarding path (any user): open `/register` (or sign in at `/login`), go to **Repos**, connect a public GitHub repo by URL, press **Sync now**, and read the Coverage page until it is green. Ops path: set the AppManager settings (`TfLensAppManagerApiKey` / `TfLensAppManagerApiSecret`, App Id 1) and optionally `TfLensGitHubToken`, start the container.

## 6. Context diagram

```mermaid
flowchart LR
  User(["User (any TechieFlow / Playbook user)"]) -- "HTTPS + cookie" --> App["TfLens<br/>Blazor Server dashboard"]
  App -- "login / register / refresh / logout<br/>X-Api-Key, App Id 1" --> AM["AppManager API<br/>appmgrapi.techierathore.com"]
  App -- "GET only, public repos" --> GH["GitHub REST API<br/>user-connected TechieFlow + Playbook repos"]
  App --> DB[("PostgreSQL 16<br/>Dapper via Npgsql")]
  App --> Raw[("Raw archive<br/>data/raw/")]
  App --> Rep[("Snapshots<br/>data/reports/&lt;date&gt;/")]
  Ref["tf-metrics.sh --rollup --json<br/>(reference, owner-run)"] -. "parity diff" .-> Rep
  Rep --> Plan["Numbers table · B1 · B3"]
```

## 7. User journey — primary use case

The weekly loop: sync, check health, read the questions, export, prove parity, quote.

```mermaid
sequenceDiagram
  actor O as Owner
  participant L as "/login"
  participant C as Coverage page
  participant S as RepoSyncService
  participant Q as Gate outcomes page
  participant E as Export page
  participant T as "Terminal (parity)"
  O->>L: log in (cookie)
  L-->>O: redirect to /
  O->>C: press Sync now
  C->>S: SyncAllAsync()
  S-->>C: per-repo report (updated / skipped / error)
  C-->>O: staleness per stream, green or warnings
  O->>Q: open /gate-outcomes
  Q-->>O: per project_type: first-pass, gate dist, escape rate (live | backfilled), taint list
  O->>E: press Export snapshot
  E-->>O: data/reports/2026-08-30/snapshot.md + tflens.json (+ parity banner)
  O->>T: tf-metrics.sh --rollup --json > reference.json
  O->>T: python3 tools/parity-compare.py reference.json tflens.json
  T-->>O: empty diff (exit 0) → record in DECISIONS.md → numbers are quotable
```

## 8. Component sketch

```mermaid
flowchart TB
  subgraph Head["src/TfLens (Blazor Server, .NET 10)"]
    Pages["Pages: /repos · / · /gate-outcomes · /harness · /routing · /export (each with a Framework switch)<br/>/login · /register · /forgot-password · /reset-password · /profile"]
    Sync["RepoSyncService (all users' repos)"]
    Verbs["Verbs: rebuild · sync · export"]
    Auth["Cookie auth + AppManager tokens"]
  end
  subgraph Core["src/TfLens.Core"]
    AMC["AppManagerClient"]
    RepoSvc["RepoRegistry (validate public + telemetry path)"]
    Fetch["GitHubStreamFetcher"]
    Parse["StreamParser + Dedupe"]
    Store["PostgresStore (Dapper + Npgsql)"]
    Engine["MetricsEngine + ExtraMetrics"]
    Exp["SnapshotExporter"]
    Pb["PlaybookAdapter"]
  end
  Pages --> Engine
  Pages --> Exp
  Sync --> Fetch
  Verbs --> Sync
  Verbs --> Exp
  Fetch --> Parse
  Fetch --> Pb
  Parse --> Store
  Pb --> Store
  Engine --> Store
  Exp --> Engine
  Auth --> Pages
  Auth --> AMC
  Pages --> RepoSvc
  RepoSvc --> Store
  Sync --> RepoSvc
```

## 9. Feature catalog

### Screen inventory — every screen, what it is for, and its mockup

Read this table with `docs/mockups/` open. It lists every screen the app has, **what question that screen exists to answer**, the feature that owns it, the requirements it satisfies — **every ID is a link into the §10/§11 ledger, so you can read the requirement itself without searching** — and the mockup the built screen is graded against. The mockups are a click-through: start at [login.html](./mockups/login.html). The per-screen component map (`region → TrBlazeUI control`) is in `docs/TfLens-UIDesign.md`.

| Screen | Route | Purpose — the question this screen answers | Feature | Requirements *(click an ID to jump to it)* | Mockup |
|--------|-------|--------------------------------------------|---------|---------------------------------------------|--------|
| Login | `/login` | **Prove who you are before any figure is shown.** TfLens stores no passwords — identity is delegated to AppManager — and every figure on every other screen is scoped to the signed-in user's own connected repos. | F-AUTH | [BRD‑1](#brd-1) · [BRD‑2](#brd-2) · [BRD‑90](#brd-90) · [BRD‑94](#brd-94) *(GitHub SSO — deferred to Phase 2)* | [login.html](./mockups/login.html) |
| Register | `/register` | **Let a new user in.** TfLens is free and open source, so anyone who uses either framework can create an account and see the reports for their own data; every registrant becomes an AppManager `Manager` and no licence, feature or payment endpoint is ever called. | F-AUTH | [BRD‑91](#brd-91) · [BRD‑95](#brd-95) | [register.html](./mockups/register.html) |
| Forgot password | `/forgot-password` | **Start a password reset without TfLens ever seeing the password.** The request goes to AppManager; TfLens only carries it. | F-AUTH | [BRD‑92](#brd-92) | [forgot-password.html](./mockups/forgot-password.html) |
| Reset password | `/reset-password` | **Finish that reset** from the emailed link, with the new password RSA-encrypted before it leaves the server. | F-AUTH | [BRD‑92](#brd-92) | [reset-password.html](./mockups/reset-password.html) |
| Profile | `/profile` | **The one screen where a user acts on themselves rather than on data** — display name, password, theme preference. | F-AUTH, F-SHELL | [BRD‑106](#brd-106) · [BRD‑107](#brd-107) | [profile.html](./mockups/profile.html) *(user menu shown open)* |
| Repos — sources<br/>*(+ Add source dialog: Fetch via API \| Import metric files; Remove dialog)* | `/repos` | **Nothing else on the site works until this does.** Connect the telemetry TfLens will read — either **fetch** a public GitHub repo's streams through the API, or **import** metric files by upload when the repo is private or corporate, or when the producer's output is transient and cannot be fetched on a schedule (the Playbook's phase metrics). Also where a source is removed and every parsed row and raw archive belonging to it is purged. | F-REPOS | [BRD‑98](#brd-98) · [BRD‑99](#brd-99) · [BRD‑100](#brd-100) · [BRD‑101](#brd-101) · [BRD‑102](#brd-102) · [BRD‑103](#brd-103) · [BRD‑104](#brd-104) · [BRD‑131](#brd-131) · [BRD‑132](#brd-132) · [BRD‑133](#brd-133) · [BRD‑134](#brd-134) · [BRD‑135](#brd-135) · [BRD‑136](#brd-136) · [BRD‑137](#brd-137) · [BRD‑138](#brd-138) · [BRD‑139](#brd-139) · [BRD‑140](#brd-140) · [BRD‑141](#brd-141) | [repos.html](./mockups/repos.html) |
| Shell: sidebar, header, Framework switch, user menu | *(layout)* | **Makes “whose data, and which framework?” answerable on every screen.** Carries the eight-item nav in the order a user should work in, the **Framework switch** (TechieFlow \| Playbook) that re-queries the whole page, **Sync now** with the last-sync badge, the theme toggle and the user menu. | F-SHELL, F-FRAMEWORK | [BRD‑4](#brd-4) · [BRD‑5](#brd-5) · [BRD‑6](#brd-6) · [BRD‑105](#brd-105) · [BRD‑106](#brd-106) · [BRD‑108](#brd-108) · [BRD‑151](#brd-151) | visible on every report mockup, e.g. [coverage.html](./mockups/coverage.html) |
| Coverage / health | `/` | **Is the telemetry itself trustworthy right now?** Per repo: whether a clone has stopped pushing, whether hooks are missing, which streams carry records and how fresh they are. It is the **landing page on purpose** — *every other number on this site is suspect until this page is green* — and it is where amendment, orphan and provenance diagnostics surface instead of being silently applied. | F-COVER, F-RAW | [BRD‑21](#brd-21) · [BRD‑39](#brd-39) · [BRD‑40](#brd-40) · [BRD‑41](#brd-41) · [BRD‑42](#brd-42) · [BRD‑43](#brd-43) · [BRD‑44](#brd-44) | [coverage.html](./mockups/coverage.html) · Playbook state: [playbook.html](./mockups/playbook.html) |
| Gate outcomes | `/gate-outcomes` | **The headline page — and the name is a direct reference, not a slogan.** It renders *the three questions the telemetry schema exists to answer* (`.tfcore/telemetry/SCHEMA.md` §0), per `project_type`: **(1) first-pass rate** — what fraction of REQs reach `Verified` on attempt 1; **(2) gate catch distribution** — of all failures, which gate caught them; **(3) escape rate** — what fraction of defects reached UAT or production instead of being caught by a gate. Live and backfilled figures sit side by side and are **never summed**; there is no “all types” tab and no total row, by design. See the callout below. | F-3Q, F-ENGINE | [BRD‑45](#brd-45) · [BRD‑46](#brd-46) · [BRD‑47](#brd-47) · [BRD‑48](#brd-48) · [BRD‑49](#brd-49) · [BRD‑50](#brd-50) | [gate-outcomes.html](./mockups/gate-outcomes.html) · Playbook state: [gate-outcomes-playbook.html](./mockups/gate-outcomes-playbook.html) |
| Harness comparison | `/harness` | **Does the framework behave the same whichever tool runs it?** One column per harness (`claude-code` · `opencode` · `codex`): run volumes by command, verdict mix, session counts, token totals. Tokens may be compared across harnesses; **dollars may not** — only OpenCode reports measured cost, and the page never shows a dollar total across harnesses. | F-HARN | [BRD‑51](#brd-51) · [BRD‑52](#brd-52) · [BRD‑53](#brd-53) · [BRD‑54](#brd-54) · [BRD‑55](#brd-55) | [harness.html](./mockups/harness.html) · Playbook state: [harness-playbook.html](./mockups/harness-playbook.html) |
| Routing & economics<br/>*(+ Edit prices dialog)* | `/routing` | **Did runs land on the model they were routed to, and what would the mix have cost?** Routing drift (declared `tier`/`tier_model` vs the model actually observed), tokens by model, and the counterfactual repricing — everything repriced as if every run had used the most expensive model observed — always labelled **estimate — tokens × rate card, not measured spend**. | F-ROUTE | [BRD‑56](#brd-56) · [BRD‑57](#brd-57) · [BRD‑58](#brd-58) · [BRD‑59](#brd-59) · [BRD‑60](#brd-60) · [BRD‑61](#brd-61) · [BRD‑62](#brd-62) | [routing.html](./mockups/routing.html) · Playbook state: [routing-playbook.html](./mockups/routing-playbook.html) |
| Misses & rework | `/misses` | **What was missed, which practice let it through, and what did the repair cost?** This is the schema's **fourth** question (added 2026-08-28) and it is deliberately its **own page rather than a fourth band on Gate outcomes** — the three questions are canon and a well-understood surface, and adding to them would dilute it. Its *escape share* is a different measurement from Gate outcomes' *escape rate* and the two are never merged. | F-MISS | [BRD‑118](#brd-118) · [BRD‑119](#brd-119) · [BRD‑120](#brd-120) · [BRD‑121](#brd-121) · [BRD‑122](#brd-122) · [BRD‑123](#brd-123) · [BRD‑124](#brd-124) · [BRD‑125](#brd-125) · [BRD‑126](#brd-126) · [BRD‑167](#brd-167) | [misses.html](./mockups/misses.html) · Playbook state: [misses-playbook.html](./mockups/misses-playbook.html) |
| Phase effort | `/effort` | **What did each phase cost — in time, tokens, models and subagents?** A **budgeting and capacity** view, never a quality scoreboard: `*build-phase` costing more than `*log-miss` is a fact about what those phases *are*. Quality lives on `/misses` and Coverage. Every figure carries its own denominator on screen, because a run whose token window was never computed is not a run that spent nothing. | F-EFFORT | [BRD‑146](#brd-146) · [BRD‑147](#brd-147) · [BRD‑148](#brd-148) · [BRD‑149](#brd-149) · [BRD‑150](#brd-150) · [BRD‑151](#brd-151) | [effort.html](./mockups/effort.html) |
| Phase efficiency<br/>*(Playbook state of `/effort`)* | `/effort` | **The same question, answered from the Playbook's own producer.** Normalized schema-2 phase metrics with stricter bounds of their own: incomplete (`eof`) windows carry no duration, partial active-time coverage renders as an explicit lower bound, and a harness with no normalized producer reads *unsupported*, never zero. | F-EFFORT, F-FRAMEWORK | [BRD‑156](#brd-156) · [BRD‑157](#brd-157) · [BRD‑158](#brd-158) · [BRD‑159](#brd-159) · [BRD‑160](#brd-160) · [BRD‑161](#brd-161) · [BRD‑162](#brd-162) · [BRD‑163](#brd-163) | [effort-playbook.html](./mockups/effort-playbook.html) |
| Snapshot export | `/export` | **Turn the figures into something quotable.** Writes a dated markdown + JSON snapshot and marks it **QUOTABLE** only while parity against `tf-metrics.sh` still holds and no row carries provenance nobody obtained — because the dangerous failure of this product is not a crash, it is a plausible wrong number that gets published and cannot be defended. | F-EXPORT, F-PARITY | [BRD‑63](#brd-63) · [BRD‑64](#brd-64) · [BRD‑65](#brd-65) · [BRD‑66](#brd-66) · [BRD‑67](#brd-67) · [BRD‑70](#brd-70) | [export.html](./mockups/export.html) · Playbook state: [export-playbook.html](./mockups/export-playbook.html) |
| Health endpoint | `/healthz` | **Liveness for the container and the orchestrator.** Not a user surface. | F-OPS | [BRD‑78](#brd-78) | *(no UI)* |

<a id="the-gate-outcomes"></a>
#### What “Gate outcomes” shows — and why it is no longer called “Three questions”

This screen renders the questions `.tfcore/telemetry/SCHEMA.md` §0 declares the telemetry exists to answer — the questions the whole system was built for, in order:

| # | Question | Stated as | Answered from |
|---|----------|-----------|---------------|
| 1 | **First-pass rate** | What fraction of REQs reach `Verified` on **attempt 1**? | `gates.jsonl` |
| 2 | **Gate catch distribution** | Of all failures, **which gate caught them**? | `gates.jsonl` |
| 3 | **Escape rate** | What fraction of defects reached **UAT or production** instead of being caught by a gate? | `gates.jsonl` |
| 4 | *Miss attribution and rework cost* — added 2026-08-28 | **What** was missed, **which phase / agent / model** let it through, and **what did fixing it cost**? | `misses.jsonl` → its own screen, `/misses` |

Rows 1–3 share one stream and one unit — a gate record is *a verdict at an instant* — which is why they belong on one screen. **Row 4 was deliberately not added to it.** A miss is a different unit: an object with a lifecycle that can span several runs, and which can exist with **no verify run at all** (that is how design-phase misses become visible for the first time). It also carries its own *escape share*, a different measurement from row 3's *escape rate* — two definitions of one word on one screen is how a report loses its reader. So row 4 got `/misses`, and row 3 kept its definition and its source untouched.

**Renamed 2026-09-01, owner ruling.** Until then this screen was called **“Three questions”** and lived at `/three-questions`. That label named a *count*, not a subject: it was the only item in the sidebar not named for what it shows, and it only resolved for a reader who had already read SCHEMA.md — a document that lives in `.tfcore/`, outside this repository's `docs/`. **Gate outcomes** states both the subject and the source: all three figures come from `gates.jsonl`, which SCHEMA.md calls *the primary stream*. It also reads cleanly against `Misses & rework` beside it, which is the adjacent-but-different question — **gate verdicts** here, **defect lifecycle** there.

Two things the rename deliberately did **not** change:

- **The schema's own wording.** SCHEMA.md §0 still calls these *the three questions*, and that phrase remains correct wherever this BRD, the code comments or the Architecture refer to the **concept**. TfLens renamed its *screen*, not the framework's vocabulary — that vocabulary is not this product's to change.
- **The feature ID.** This feature is still **F-3Q**. Requirement and feature IDs are stable identifiers that other documents, checklist rows and telemetry records point at; renaming one to match a label would break traceability for no gain. Read `F-3Q` as an opaque key, not as an abbreviation of the current title.

### F-SHELL: App shell and navigation

**Personas:** User, Owner · **Phase:** 1 *(amended 2026-08-26: login moved to F-AUTH; collapsible icon sidebar, user menu, dark-first)*

Every page lives inside one TrBlazeUI sidebar shell (`SidebarProvider` + `Sidebar Collapsible` + `SidebarInset`). The sidebar is **collapsible** via `SidebarTrigger` (icon-only rail with tooltips when collapsed) and every item carries a **Lucide icon**; the order is the order a user should work in: **Repos** first (nothing to see until a repo is connected), then Coverage ("every other number is suspect until this page is green"), Gate outcomes, Harness comparison, Routing & economics, **Misses & rework** *(added 2026-08-28, F-MISS)*, **Phase effort** *(added 2026-09-01, F-EFFORT)*, Snapshot export (the separate Playbook page was retired 2026-08-26 — see F-FRAMEWORK). *Phase effort* sits after *Misses & rework* deliberately: both are cost lenses, and the reader should meet the quality question before the budget one. The header carries the **Framework switch** (TechieFlow | Playbook, F-FRAMEWORK), the page title, a **Sync now** button with the last-sync badge, the theme toggle, and — on the right — the **signed-in user's name** with a `DropdownMenu` (Profile, Manage repos, Sign out); there is no bare sign-out button. The application **starts in dark mode**; the user's toggle choice is persisted per user.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Shell | (layout) | Collapsible icon sidebar (Repos, Coverage, Gate outcomes, Harness, Routing & economics, Misses & rework, **Phase effort**, Snapshot export); header: **Framework switch (TechieFlow / Playbook)** · title · Sync now · last-sync badge · theme toggle · user menu | [coverage.html](./mockups/coverage.html) (shell visible on every report mockup) |

**Workflow:**
1. Unauthenticated request to any page → redirect to `/login` with return URL (F-AUTH).
2. **Sync now** in the header runs `SyncAllAsync(userId)` for the signed-in user's repos and shows a toast with the per-repo outcome.
3. User menu → **Sign out** → AppManager `/AuthSvc/logout` → cookie cleared → `/login`.
4. `SidebarTrigger` collapses/expands the sidebar; the state is remembered (`CookieKey`).

**Requirements:** BRD-2, BRD-4, BRD-5, BRD-6, BRD-105, BRD-106, BRD-107, BRD-124, BRD-151

### F-AUTH: AppManager identity — login, registration, sessions

**Personas:** User, Owner · **Phase:** 1 (GitHub SSO: Phase 2, deferred) *(added 2026-08-26)*

TfLens keeps **no user store and no passwords**. Identity is delegated to the owner's AppManager service (`docs/AppManager-api-usage-guide.md`, v1.4): base URL `https://appmgrapi.techierathore.com`, **Application Id 1**, identified on every call by the `X-Api-Key` / `X-Api-Secret` headers (values from configuration only — F-OPS). Passwords are never sent in clear: TfLens fetches and caches `GET /AuthSvc/public-key` and RSA-OAEP-256-encrypts the password client-side before `POST /AuthSvc/login` or `/AuthSvc/register`. Because TfLens is free and open source, no licence, feature, subscription or payment endpoint is ever called, and every registered user is assigned `applicationRoleCode: "Manager"`. On success TfLens issues its own auth cookie (sliding 12 h, HttpOnly, Secure) carrying the AppManager `userId`, email, display name and role; the AppManager access and refresh tokens are held **server-side** per session and refreshed through `POST /AuthSvc/refresh` before expiry; a resumed cookie is checked with `POST /AuthSvc/validate`. A demo account **`TfLensDemo`** (`tflensdemo@techierathore.com`) is registered in AppManager during development and its public demo repos are connected through the Repos screen (no configuration seed — amended 2026-08-26), so testers and first-time visitors can see a populated dashboard.

**GitHub SSO — deferred to Phase 2 (BRD-94).** AppManager exposes no external-login or token-exchange endpoint, so "Continue with GitHub" cannot obtain an AppManager token without a bridge (a TfLens-held random credential per SSO user). The owner chose to defer this until AppManager grows an SSO endpoint; the login screen reserves the button position but does not show it in this release.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Login | `/login` | Email + password; links to Register and Forgot password; generic error on failure; anonymous | [login.html](./mockups/login.html) |
| Register | `/register` | First name, last name, email, password (+ confirm) per AppManager rules (8+, upper, digit, special); creates the user with role Manager; anonymous | [register.html](./mockups/register.html) |
| Forgot password | `/forgot-password` | Email → `/AuthSvc/forgot-password` (always "if that address exists, an email was sent"); anonymous | [forgot-password.html](./mockups/forgot-password.html) |
| Reset password | `/reset-password?token=…` | New password (+ confirm) → `/AuthSvc/reset-password`; anonymous | [reset-password.html](./mockups/reset-password.html) |
| Profile | `/profile` | Read-only AppManager profile (`GET /UserSvc/profile`) + change password (`POST /UserSvc/change-password`) | [profile.html](./mockups/profile.html) |

```mermaid
sequenceDiagram
  actor U as User
  participant L as "/login page"
  participant A as "AuthService (TfLens)"
  participant AM as "AppManager API (App Id 1)"
  participant C as "Cookie middleware"
  U->>L: email + password
  L->>A: SignInAsync(email, password)
  A->>AM: GET /AuthSvc/public-key (cached)
  AM-->>A: RSA public key
  A->>A: RSA-OAEP-256 encrypt password
  A->>AM: POST /AuthSvc/login {email, encryptedPassword} + X-Api-Key/Secret
  alt success
    AM-->>A: userId, names, applicationRole=Manager, accessToken, refreshToken, expiresAt
    A->>A: store tokens server-side (session store)
    A->>C: SignIn(cookie: userId, email, name, role)
    C-->>U: redirect to return URL or /repos
  else INVALID_CREDENTIALS / ACCOUNT_LOCKED / ACCOUNT_DISABLED
    AM-->>A: error code
    A-->>L: generic "Sign-in failed" (code logged, never shown)
  end
  Note over A,AM: before accessToken expiry POST /AuthSvc/refresh, on sign-out POST /AuthSvc/logout
```

**Workflow:**
1. `/login`: encrypt → login → cookie → redirect (first sign-in with no repos lands on `/repos`).
2. `/register`: validate password rules locally → encrypt → `register` with `applicationRoleCode: "Manager"` → same cookie issue as login.
3. `/forgot-password` → `forgot-password`; `/reset-password` → `reset-password` (API key header supplies the app scope).
4. Session: refresh tokens server-side before `tokenExpiresAt`; on refresh failure → sign out.
5. Sign out: `logout` with the refresh token (per-app scope) → clear cookie.

**Requirements:** BRD-1, BRD-90, BRD-91, BRD-92, BRD-93, BRD-94 (deferred), BRD-95, BRD-96, BRD-97

### F-REPOS: Source management — fetch public repos, or import metric files

**Personas:** User, Owner · **Phase:** 1 (import mode: Phase 3) *(added 2026-08-26; amended 2026-08-28 — F-IMPORT folded in as a second mode of the same dialog rather than a separate screen, owner decision)*

TfLens is for anyone using the frameworks, so the sources to read are **managed in the app, per user**, not in a config file. The `/repos` screen lists the signed-in user's connected sources (owner/name, branch, kind, **source**, visibility, status, last sync or last import, per-stream record counts) with per-row **Sync** / **Re-import** and **Remove** actions and an **Add source** dialog.

**Two ways in, chosen in the dialog (amended 2026-08-28 — F-IMPORT).** The first step of the dialog is a mode choice, and it is the demarcation the whole feature rests on:

| Mode | For | How the data arrives | Row action | Poller |
|---|---|---|---|---|
| **Fetch via API** | **Public** repos | TfLens calls the GitHub API, validates, and pulls on a schedule (F-SYNC) | **Sync** | polls it |
| **Import metric files** | **Private / corporate** repos — or any repo the user would rather not connect | The user uploads a zip of `docs/metrics/` (or `verification/telemetry/`), or the loose `.jsonl` / `.ndjson` files, exactly as their framework already wrote them to disk | **Re-import** | **skips it** |

An imported source has no remote to poll, so it gets **Re-import**, not a Sync button that would do nothing, and the background poller passes over it rather than waking every fifteen minutes to contact something that isn't there. Everything downstream is identical: one extra column (`SourceKind`) on the source row, the same raw archive, the same parser, the same dedupe, the same engine, the same isolation. **No second code path exists**, because a second path is where a second set of bugs lives.

Why this reaches private repos at all: TfLens never needs the repository — it needs the JSONL files inside it, which the frameworks already write in plain text. Uploading them requires no credential, no network route to a corporate host, no PAT, and no change to the repo. What remains out of scope is TfLens *authenticating to* a private repo and pulling from it (§3).

**An imported source is user-supplied, and TfLens does not pretend otherwise.** A fetched file came from a named commit in a public repo; an uploaded file came off someone's desktop and could in principle have been edited on the way. TfLens does not try to detect that — it makes the origin **visible everywhere** instead: a `Synced` / `Imported` badge on the row, a source column on Coverage, a `source_kind` key in the export. The reader always knows which they are looking at. Connecting takes a GitHub URL or `owner/name` (+ branch, default branch auto-detected) and validates it through the GitHub API before saving: the repo must exist, must be **public** (this release supports public repos only — a private repo is refused with an explicit message), and must contain the telemetry path for its kind on that branch (`docs/metrics/` → `techieflow`, `verification/telemetry/` → `playbook`; the kind is auto-detected and can be overridden). Removing a repo stops its sync and **purges** that user's parsed rows and raw archive for it. All data is scoped by user: `sync_state`, the raw archive (`data/raw/<userId>/<owner>__<name>/`), the stream tables and the analysis cache all carry the `UserId`; a page never shows another user's repos. The same public repo may be connected by several users independently (each gets their own copy — the simplest rule that keeps isolation exact). The `appsettings` repo list is used only to seed the `TfLensDemo` account at first start.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Repos | `/repos` | User's sources grid with a **Source** column (`Synced` / `Imported`); Add source button; per-row Sync **or** Re-import, plus Remove; empty state for a new user | [repos.html](./mockups/repos.html) |
| Add source — **Fetch via API** (dialog) | `/repos` | Mode = Fetch via API: URL or owner/name, branch, kind (auto), Validate → shows public ✓ / telemetry path ✓ / default branch → Connect | [repos.html](./mockups/repos.html) (dialog panel) |
| Add source — **Import metric files** (dialog) | `/repos` | Mode = Import metric files: source name, framework, optional `project_type`; drop zone for a `.zip` / `.jsonl` / `.ndjson`; **preview before commit** (records per stream, date range, invalid lines, unknown fields, bundle sha256) → Import | [repos.html](./mockups/repos.html) (dialog panel) |
| Re-import (dialog) | `/repos` | Same import panel, pre-named to the existing source; states records added vs duplicates collapsed after commit | [repos.html](./mockups/repos.html) (dialog panel) |
| Remove source (dialog) | `/repos` | Confirm; explains that parsed rows + raw archive for this source are purged — identical for fetched and imported | [repos.html](./mockups/repos.html) (dialog panel) |

```mermaid
flowchart LR
  A["Paste GitHub URL or owner/name"] --> B["GET /repos/{owner}/{name}"]
  B --> C{"exists?"}
  C -->|"no"| X["Refuse: repo not found"]
  C -->|"yes"| D{"private?"}
  D -->|"yes"| Y["Refuse: public repos only in this release"]
  D -->|"no"| E["Resolve branch (default or chosen)"]
  E --> F["GET /contents/docs/metrics or /verification/telemetry at branch"]
  F --> G{"telemetry path found?"}
  G -->|"no"| Z["Refuse: no TechieFlow or Playbook telemetry at this path"]
  G -->|"yes"| H["Kind detected; save UserRepo; first sync queued"]
```

**Workflow:**
1. New user lands on `/repos` (empty state: "Add your first source" — offering both modes).
2. **Fetch via API:** validate (exists, public, telemetry path) → save → first sync runs → toast.
3. **Import metric files:** name the source → drop the zip or files → TfLens unpacks, validates and **previews** (records per stream, date range, invalid lines, unknown fields, bundle sha256) → the user reviews → Import commits: bytes archived verbatim, then parsed by the same parser → toast states records added and duplicates collapsed.
4. Row **Sync** (fetched) → `SyncRepoAsync(userId, repo)`; row **Re-import** (imported) → the import panel again; row Remove → confirm → purge rows + raw → row disappears.
5. Header Sync now and the background poller iterate every user's **fetched** sources and skip imported ones; errors stay per user and source.

**Requirements:** BRD-98, BRD-99, BRD-100, BRD-101, BRD-102, BRD-103, BRD-104, BRD-131, BRD-132, BRD-133, BRD-134, BRD-135, BRD-136, BRD-138, BRD-139, BRD-140, BRD-141, BRD-153

### F-CFG: Configuration and secrets (retired 2026-08-26)

~~F-CFG~~ — retired in the second amendment. Repos are managed only on the Repos screen (F-REPOS), so there is no repo list and no demo seed in configuration; the remaining infrastructure settings (AppManager connection, database connection, poll interval, optional PAT, `DataRoot`) moved to **F-OPS**. BRD-7 is retired; BRD-8, BRD-9, BRD-10 and BRD-11 now belong to F-OPS. The candidate demo repos (techierathore/TechieFlow, TechieRag, TrBlazeUI, blog, AI-First-Playbook — public ones only) are connected to `TfLensDemo` through the UI during development (BRD-96).

### F-SYNC: Repo puller — background sync and Sync now

**Personas:** Owner (Sync now), Ops (background) · **Phase:** 1

A `BackgroundService` polls every connected repo of every user on the interval; the header button runs the identical code on demand for the signed-in user's repos (amended 2026-08-26: repos come from F-REPOS, not configuration). For each repo it asks GitHub for the latest commit SHA touching the telemetry path (`docs/metrics` for `techieflow`, `verification/telemetry` for `playbook`) on the configured branch. If that SHA equals the one in `sync_state`, the repo is skipped without fetching a byte. Otherwise every stream file is fetched whole at that exact SHA (they are small), written verbatim to the raw archive (F-RAW), and parsed (F-PARSE). A 404 on a stream file means "this stream is absent" and is recorded as zero, not as an error. Errors are per repo: one failing repo never stops the others, and the failure text (status code + short reason, never the token) lands in `sync_state.LastError` for the Coverage page. The puller is structurally read-only — no method exists that issues anything but GET.

```mermaid
flowchart TB
  A["Tick or Sync now"] --> B["For each configured repo"]
  B --> C["GET latest commit SHA touching telemetry path"]
  C --> D{"SHA == sync_state.LastSha?"}
  D -->|"yes"| E["Skip: update LastSyncTs only"]
  D -->|"no"| F["For each stream file"]
  F --> G["GET raw file at SHA (404 = absent)"]
  G --> H["Write data/raw/&lt;repo&gt;/&lt;stream&gt;-&lt;sha&gt;.jsonl verbatim"]
  H --> I["Parse + dedupe + upsert"]
  I --> J["Update sync_state: sha, ts, per-stream counts, LastError=null"]
  C -->|"401 / 403 / 404 / network"| K["Record LastError for this repo; continue with next"]
  E --> L["SyncReport"]
  J --> L
  K --> L
```

**Workflow:**
1. Timer tick (every `PollIntervalMinutes`) or button press.
2. Per repo: SHA lookup → skip or fetch → archive → parse → `sync_state`.
3. Return a `SyncReport` (per repo: `Updated(sha, counts)` / `Skipped` / `Error(reason)`); the UI shows it as a toast and the Coverage page reflects it.
4. Invalidate the cached analysis so pages recompute.

**Requirements:** BRD-12, BRD-13, BRD-14, BRD-15, BRD-16, BRD-17, BRD-18, BRD-112

### F-RAW: Raw archive and rebuild

**Personas:** Ops, Parity operator · **Phase:** 1

The raw archive is the rebuild source and the audit trail. Every fetched file is stored byte-for-byte under `data/raw/<userId>/<owner>__<name>/<stream>-<sha>.jsonl` **before** it is parsed, so a parser bug can never lose data — fix the parser, run `rebuild`, done. `rebuild` (a command verb `dotnet TfLens.dll rebuild`, also a confirm-guarded button on the Coverage page) truncates every stream table in PostgreSQL (amended 2026-08-26), re-applies the schema script, and replays every archived file in repo order and SHA fetch order, then reports files replayed, records stored, and duplicates collapsed per stream. Because parsing is idempotent (F-PARSE), the record counts after a rebuild equal the counts after live syncs.

**Workflow:**
1. `rebuild` requested (verb or button with an "are you sure" dialog).
2. Drop all tables → create DDL → enumerate `data/raw/**/*.jsonl`.
3. Replay in order → recompute `sync_state` counts from the newest SHA per repo.
4. Report; invalidate caches.

**Requirements:** BRD-19, BRD-20, BRD-21, BRD-22, BRD-115

### F-PARSE: Parser to PostgreSQL with dedupe and overflow

**Personas:** Ops, Parity operator · **Phase:** 1

One table per stream (`Run`, `Gate`, `Session`, `Commit`) plus `SyncState` — and, from the 2026-08-28 amendment, three more for the one stream whose records do not all share a shape: `Miss`, `MissFix` and `MissAmend` (F-MISS). Column names follow SCHEMA.md field names exactly (PascalCase form; the mapping table is in the parser and in Architecture §6). A line that is not valid JSON is counted and skipped, exactly as the reference does — and so is a line in `misses.jsonl` whose `kind` the parser does not know. Any property the parser does not know for that stream — and any record with `v > 1` — keeps its unknown properties in a JSON `Overflow` column rather than being dropped; the set of unknown field names seen per repo is reported on the Coverage page ("fields observed that SCHEMA.md doesn't document"). Fields that SCHEMA.md says are "present only when true" or "absent means not captured" are stored as `NULL` when absent, never as `0`/`false`, so downstream can tell "not captured" from "zero".

Dedupe is idempotent on the natural identity of each stream, so re-parsing the same raw file or replaying it during rebuild never double-counts:

| Stream | Identity | Rule |
|--------|----------|------|
| `commits` | `sha` **per repo** | keep first; count collapsed duplicates (expected after union merges — two repos may legitimately share a short sha, hence per repo) |
| `sessions` | `session_id` | OpenCode records are cumulative snapshots: keep the record with the highest `output_tokens`, tie → latest `ts` |
| `runs` | `ts` + `app` + `cmd` | keep first |
| `gates` | `ts` + `app` + `req_id` + `run_id` | keep first |
| `misses` → `miss` | `miss_id` **per repo** | keep **earliest** `ts` — a miss is opened once; a duplicate is a re-parse of the same archived file, not new information *(2026-08-28)* |
| `misses` → `miss-fix` | `miss_id` + `fix_run_id` **per repo** | keep latest `ts` *(2026-08-28)* |
| `misses` → `miss-amend` | `miss_id` + `field` + `ts` **per repo** | keep **earliest** `ts` — amendments are additive and each is a distinct fact *(2026-08-28)* |

Provenance fields are preserved verbatim and typed: `backfilled`, `inferred`, `project_type`, `project_type_inferred`, `harness`. They are what Phase 2 segments on.

```mermaid
flowchart LR
  A["raw JSONL text"] --> B["split lines"]
  B --> C{"valid JSON?"}
  C -->|"no"| D["count + skip"]
  C -->|"yes"| E["map known fields to columns"]
  E --> F["unknown fields to Overflow JSON"]
  F --> G{"natural key already stored?"}
  G -->|"yes"| H["skip (dedupe count)"]
  G -->|"no"| I["insert"]
```

**Workflow:**
1. Receive `(repo, stream, sha, text)`.
2. Parse line by line; map; overflow; dedupe against the unique index; insert in one transaction.
3. Return `(inserted, duplicates, invalidLines, unknownFields[])`.

**Requirements:** BRD-23, BRD-24, BRD-25, BRD-26, BRD-27, BRD-28, BRD-29, BRD-113, BRD-114, BRD-145, BRD-154, BRD-164, BRD-165

### F-ENGINE: Metrics engine with provenance rules

**Personas:** Owner (indirectly — every page), Parity operator · **Phase:** 2

The engine is a field-for-field port of `analyse()` in `.tfcore/telemetry/tf-metrics.sh`, the trusted reference. All figures are computed at request time from the stream tables; nothing derived is ever written back into a stream table. The SCHEMA.md §6 provenance rules are enforced by the shape of the result, not by a flag:

- **Live and backfilled never pool.** The result has `Live[projectType]` and `Backfilled[projectType]`; there is no `Total`.
- **First-pass rate, gate catch distribution and escape rate never pool across `project_type`.** Records with `project_type_inferred: true` are segmented as **unclassified**, never silently as `app`.
- **Taint exclusion.** Any `req_id` with even one backfilled record is excluded from the live first-pass rate (its live `attempt` restarts at 1); the excluded IDs are returned as a list for display.
- **Minimum n.** Any metric with fewer than 3 supporting records is `InsufficientData(n)`, a distinct case of the `Figure` type that a page can only render as text.
- **Dollars never pool across harness.** `Pooled.CostUsd` is always `null` (the reference's contract); real dollars appear only in the harness page for `opencode`.
- **Late-added gates** (`perf`, 2026-08-10) report `ran` (records whose `gates_run` contains the gate) and `caught` side by side; their share of the raw distribution is never presented as a catch rate.
- Poolable metrics (rework ratio, batch size median, REQ throughput median in REQs/hour, tokens total, tokens per Verified REQ, commit cadence, duplicates collapsed) follow SCHEMA.md §8 and the reference's rounding (`%.0f%%`, 2 dp throughput, 1 dp tokens per Verified).

A unit test feeds the checked-in fixture streams to the engine and asserts equality with a `reference.json` produced by the script on the same fixtures — the parity test in miniature, run on every build.

**Workflow:**
1. `Analyse(repos, options)` reads streams per repo, dedupes commits per repo.
2. Segments gates: live vs backfilled → by project type (unclassified for inferred).
3. Computes taint set → per-segment figures → late-gate coverage → pooled block.
4. Returns `AnalysisResult` (same key layout as `--rollup --json`) — memoised until the next sync/rebuild.

**Requirements:** BRD-30, BRD-31, BRD-32, BRD-33, BRD-34, BRD-35, BRD-36, BRD-37, BRD-38, BRD-116, BRD-117, BRD-146, BRD-147, BRD-148, BRD-155, BRD-156, BRD-158, BRD-161, BRD-166, BRD-168, BRD-169

### F-COVER: Coverage / health page

**Personas:** Owner · **Phase:** 2

The first report page (after Repos). For each of the signed-in user's repos it shows: kind, last sync time and outcome, last commit SHA (short, linked to GitHub), record counts per stream, live-vs-backfilled gate counts, and **days since the newest record per stream**. A **fetched** repo whose newest `sessions` or `commits` record is stale (older than a configurable threshold, default 7 days) is flagged in words — "this clone isn't pushing or lacks hooks; run `update-framework.sh` on it" — because the hook lives in `.git/`, which never clones, and this is the one telemetry gap the owner cannot see by reading the files. An **imported** source is read differently (amended 2026-08-28, BRD-137): staleness counts **days since import**, the message is *"this source can't refresh itself — re-import to update"*, and the hook diagnosis is not shown, because it would be advice about a clone TfLens cannot see. A snapshot is not unhealthy for being a snapshot. The page also lists, per repo, any fields observed that SCHEMA.md does not document (from the overflow report) and any records with `v > 1`, and hosts the guarded **Rebuild from raw** button. A single summary badge at the top says **GREEN** (all repos synced, nothing stale, no errors) or **CHECK** with the count of warnings. Every other number on the site is suspect until this page is green.

*Amended 2026-08-28 (F-MISS).* The per-repo stream table goes from four rows to **five** (`misses` joins them), and Coverage gains three data-quality facts the miss stream introduces — none of which is a quality figure and none of which belongs on the `/misses` KPI row: **`escapes_missing_why`** (escapes, `found_by ∈ {owner, production}`, arriving with no `why_missed` — the most valuable records in the stream arriving incomplete), the **`project_type` reclassification split** (a repo whose stored records carry a `project_type` its *current* classification disagrees with, which §6 forbids pooling and which would otherwise render silently as two unrelated projects), and the **orphan counts** (a `miss-fix` or `miss-amend` naming no known `miss`). A repo emitting `miss` records with no `miss-fix` records at all is a **warning, not an error** — most likely the fix path is not wired up yet, which is worth saying and not worth failing on.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Coverage / health | `/` | Summary badge; per-repo cards or grid; five-row stream staleness table; unknown fields; escapes-missing-why + reclassification-split + orphan facts; Rebuild button; Framework switch | [coverage.html](./mockups/coverage.html) · Playbook state: [playbook.html](./mockups/playbook.html) |

**Workflow:**
1. Read `sync_state` + per-stream `MAX(ts)` per repo + counts + overflow field names.
2. Compute staleness per stream vs today; apply thresholds; compose warnings.
3. Render; Sync now / Rebuild refresh in place.

**Requirements:** BRD-39, BRD-40, BRD-41, BRD-42, BRD-43, BRD-44, BRD-127

### F-3Q: Gate outcomes page

**Personas:** Owner, Author · **Phase:** 2

*(Renamed from **“Three questions”** on 2026-09-01 by owner ruling; route `/three-questions` → `/gate-outcomes`. The feature keeps the ID **F-3Q** for traceability — see [§9](#what-gate-outcomes-shows-and-why-it-is-no-longer-called-three-questions).)* This screen renders the three questions `.tfcore/telemetry/SCHEMA.md` §0 says the telemetry exists to answer. The three are: **(1) first-pass rate** — what fraction of REQs reach `Verified` on attempt 1; **(2) gate catch distribution** — of all failures, which gate caught them; **(3) escape rate** — what fraction of defects reached UAT or production instead of being caught by a gate. All three come from `gates.jsonl`, the primary stream, which is why they share one screen. The schema's fourth question (miss attribution and rework cost) is answered on `/misses` and deliberately not added here.

The headline page and the B3 evidence base. For each `project_type` present in the data (`app`, `library`, `docs`, `framework`, and `unclassified` for inferred records) it shows the three questions SCHEMA.md §0 exists to answer — **first-pass rate**, **gate catch distribution** (with `escaped` as its own row, never folded into a gate, and `unattributed` where a failure carries no gate), and **escape rate** — computed from **live records only**, with the backfilled figures for the same type in an adjacent, clearly labelled column that is never summed with live. Under each type: records, REQs scored, REQs excluded by backfill taint, and the late-gate coverage lines (`perf gate: ran on n records, caught k → rate | insufficient data (n=…) | not yet run on this data (gate added 2026-08-10)`). The tainted REQ IDs are listed in full in a collapsible panel. Any figure below the minimum n renders as `insufficient data (n=…)`. There is no "all types" tab and no total row — by design.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Gate outcomes | `/gate-outcomes` | One section (or tab) per project_type; live column + labelled backfilled column; taint list; late-gate coverage; Framework switch | [gate-outcomes.html](./mockups/gate-outcomes.html) · Playbook state: [gate-outcomes-playbook.html](./mockups/gate-outcomes-playbook.html) |

**Workflow:**
1. `Analyse()` → iterate `Live` and `Backfilled` keyed by type.
2. Render first-pass, escape rate, distribution table (rows in the reference's `GATE_ORDER`), late-gate coverage.
3. Render the taint list and the standing note: "figures are deliberately not combined across project_type or provenance (SCHEMA.md §6)".

**Requirements:** BRD-45, BRD-46, BRD-47, BRD-48, BRD-49, BRD-50

### F-HARN: Harness comparison page

**Personas:** Owner, Author · **Phase:** 2

The portability page — the B1 story rendered as data. Three columns, one per harness the framework detects (SCHEMA.md §1): **`claude-code` · `opencode` · `codex`** (Codex CLI — amended 2026-08-26; TechieFlow now detects it). Per column: run counts by command, gate records and verdict mix, session counts, token totals (input, output, cache read, cache write, from both `runs` §2.5 fields and `sessions`), tokens per verified REQ. **Real `cost_usd` is shown for OpenCode only**, in its own card labelled "the only measured dollars in the system"; Claude Code and Codex show "not measured (null by design)". Tokens may be compared across harness; dollars may not, and the page never shows a dollar total across harnesses. Records with `harness: null` get **no column** but are never hidden: a footnote row states "*n* records with harness not detected — excluded from the columns above" (owner decision 2026-08-26). The page honours the Framework switch (F-FRAMEWORK). This page has no reference in `tf-metrics.sh`, so it is spot-checked by hand once (F-PARITY).

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Harness comparison | `/harness` | Columns claude-code · opencode · codex; "not detected" footnote; tokens chart; OpenCode-only cost card; Framework switch | [harness.html](./mockups/harness.html) · Playbook state: [harness-playbook.html](./mockups/harness-playbook.html) |

**Workflow:**
1. Group `runs`, `gates`, `sessions` by `harness`; count the `null` group separately.
2. Compute volumes, verdict mix, token totals, tokens per Verified; dollars for `opencode` only.
3. Render the three columns + one bar chart (tokens by harness) + the not-detected footnote.

**Requirements:** BRD-51, BRD-52, BRD-53, BRD-54, BRD-55

### F-ROUTE: Routing and economics page

**Personas:** Owner, Author · **Phase:** 2

Three panels. **Routing drift** uses the §2.5 per-run fields: count and list of `routed: false` runs, declared `tier`/`tier_model` versus observed `model` (and `models` when more than one), by command. **Tokens by model** sums run tokens per observed model. **Counterfactual repricing** is the B3 claim basis: total tokens (input, output, cache read, cache write) repriced as if every run had used the most expensive model observed in the data, versus the actual mix, using an editable `data/prices.json` rate card (per model: input/output/cache-read/cache-write USD per million tokens). The figure is labelled **estimate — tokens × rate card, not measured spend** in the UI and in the export; runs with `tokens_scope: none` (no tokens captured) are counted and excluded, and stated. The page also carries the poolable metrics per SCHEMA.md §8 — rework ratio, REQ throughput, batch size, commit cadence — straight from the engine. A small editor (dialog) lets the owner edit `prices.json` in place with validation; the file is the source, the dialog is a convenience.

```mermaid
flowchart LR
  A["runs with tokens (scope != none)"] --> B["Σ tokens by model"]
  B --> C["actual cost estimate = Σ tokens_m × price_m"]
  P[("data/prices.json")] --> C
  P --> D["most expensive observed model"]
  B --> E["counterfactual = Σ all tokens × price_max"]
  D --> E
  C --> F["show both, labelled ESTIMATE, with excluded-run count"]
  E --> F
```

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Routing & economics | `/routing` | Drift table; tokens-by-model chart; repricing cards (estimate); poolable metrics; prices editor dialog; Framework switch | [routing.html](./mockups/routing.html) · Playbook state: [routing-playbook.html](./mockups/routing-playbook.html) |

**Workflow:**
1. Drift: filter runs with `tier_model` and `model`; group by `cmd`; list `routed:false`.
2. Tokens by model: sum §2.5 token fields by `model`.
3. Repricing: load `prices.json`; compute actual-mix and all-at-max; label estimate; show excluded runs.
4. Poolables: from `AnalysisResult.Pooled`.

**Requirements:** BRD-56, BRD-57, BRD-58, BRD-59, BRD-60, BRD-61, BRD-62

### F-MISS: Misses and rework economics

**Personas:** Owner, Author · **Phase:** 3 *(added 2026-08-28 — source: `docs/Miss-Telemetry-TfLens.md`)*

TechieFlow gained a **fifth stream** on 2026-08-28, `docs/metrics/misses.jsonl`, and it is the first stream whose records do not all have the same shape. It carries three record kinds linked by `miss_id`: a **`miss`** (what was missed, which phase/agent/model let it through, who found it), a **`miss-fix`** (the repair run, its outcome, its token and cost window) and a **`miss-amend`** (an append-only way to *complete* a field the `miss` left `null` — it may fill a `null`, and may never overwrite a value, including one an earlier amend set). TfLens pulls it, archives it, parses it, stores it, computes over it and shows it on a sixth report page, under the same provenance discipline it already applies to the four existing streams. The producing side is real, not hypothetical: `tf-metrics.sh --rollup --json` already reports a `misses` block, so every figure here is parity-diffable from the first commit and none ships marked unverified.

**Why this feature gets its own guards.** The product's stated dangerous failure mode is a *plausible wrong number* (§1), and miss data is the most seductive material in the system for producing one, because it invites three specific mistakes that all look like ordinary arithmetic:

1. **Presenting an apportioned cost as a measured one.** A fix run that repaired three misses has **one** token window. Dividing by three is arithmetic, not measurement.
2. **Presenting an inferred attribution as an observed one.** *"This model produces the most misses"* is a career-shaping claim if half the attributions were guessed.
3. **Rendering an optional field's distribution over the whole population.** `why_missed` is optional and `null` means *not assessed*, never a zero in some category; using the miss count as the denominator understates every category at once.

All three are handled the way TfLens already handles live-vs-backfilled: **in the shape of the result type, with no switch to relax it** (ADR-007's technique, applied twice more — ADR-019).

**Two open predicates that deliberately disagree.** The lifecycle splits three ways and TfLens must not reconcile the first two, because they answer different questions:

| Question | Predicate | Where it belongs |
|---|---|---|
| How much work is outstanding? (the backlog the owner reads) | latest `MissFix.VerdictAfter ∉ {Verified, wont-fix}` | the KPI tile **open misses** |
| Is this defect still live? (the producer's collapse check) | latest `VerdictAfter != "Verified"` — **`wont-fix` is still live** | not TfLens's job; it explains the gap |
| Deliberately declined | latest `VerdictAfter == "wont-fix"` | its own tile, never folded into open |

`deferred` is outstanding work and stays **open** in both. `wont-fix` is a decision, not a backlog item — but the next failure on that REQ is still the same defect, which is why the producer's check keeps it live. A reviewer will eventually try to "fix" one of these to match the other; the standing comment in `tf-metrics.sh` and BRD-120 exist to stop that.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Misses & rework | `/misses` | Four bands: KPI row · where misses come from (origin phase × class, beside the failed-practice distribution) · who was running (model + agent, `linked` only, labelled observational) · cost of rework (measured \| apportioned \| unattributable); per-miss detail table with the raw record behind a disclosure; period filter defaulting to **all history**; Framework switch | [misses.html](./mockups/misses.html) · Playbook state: [misses-playbook.html](./mockups/misses-playbook.html) |

```mermaid
flowchart TB
  A["misses.jsonl (raw archive)"] --> B{"record kind?"}
  B -->|"miss"| C["Miss table"]
  B -->|"miss-fix"| D["MissFix table"]
  B -->|"miss-amend"| E["MissAmend table (stored, never collapsed)"]
  B -->|"anything else"| F["InvalidLines++ and skip"]
  C --> G["Fold amendments at READ time, oldest first<br/>fill null only, allowlist + closed vocabulary"]
  E --> G
  G --> H{"OriginConfidence == linked?"}
  H -->|"no"| I["MissAttributionTaint: excluded and COUNTED"]
  H -->|"yes"| J["per-phase / per-model / per-agent figures"]
  D --> K{"CostAttribution"}
  K -->|"sole"| L["MissCost.Sole (measured)"]
  K -->|"shared:n"| M["MissCost.Apportioned (labelled)"]
  K -->|"none"| N["MissCost.NoneCount (no numbers at all)"]
```

**Workflow:**
1. Sync fetches `misses.jsonl` at the pinned SHA, archives it verbatim, and parses it — dispatching on each record's own `kind` **within** the misses stream. An unknown `kind` is counted as an invalid line and skipped, never thrown (the same contract as a malformed line, BRD-25).
2. Storage upserts into `Miss` / `MissFix` / `MissAmend` on their natural keys. Amend rows are **stored, not collapsed** — folding is a read-time operation, so `rebuild` re-derives identical values.
3. `MissMetrics` folds amendments oldest-first (re-applying the null-check while folding — a merged stream from several machines can carry an amend and a later-written value in either order), applies `MissAttributionTaint`, and returns each figure as a `Figure` and each cost as a `MissCost`.
4. `/misses` renders the four bands; Coverage renders the data-quality facts; the export writes the `misses` section; `parity-compare.py` diffs the whole block against the oracle.

**Requirements:** BRD-112, BRD-113, BRD-114, BRD-115, BRD-116, BRD-117, BRD-118, BRD-119, BRD-120, BRD-121, BRD-122, BRD-123, BRD-124, BRD-125, BRD-126, BRD-127, BRD-128, BRD-129, BRD-130, BRD-164, BRD-165, BRD-166, BRD-167

### F-EFFORT: Phase effort and efficiency — what each phase cost

**Personas:** Owner, Author, Ops (capacity) · **Phase:** 3 *(added 2026-09-01 — sources: `docs/Phase-Effort-Telemetry-TfLens.md` (TechieFlow), `docs/Phase-Efficiency-TfLens-Contract.md` and `docs/Miss-Telemetry-TfLens-From-AIFP.md` (AI-First-Playbook))*

The owner's question is one sentence: **"how much effort, time and tokens went into each phase — with which model, for how long, and how many subagents did it spin up?"** Both frameworks now answer it, and both shipped their producer before this consumer existed, so nothing here is speculative.

**What was already there, and what genuinely was not.** On the TechieFlow side, most of the answer had been in `runs.jsonl` since the stream started: `cmd` is the phase, `started`/`ended`/`duration_s` the time, the §2.5 fields the tokens and the model, `harness` the harness, `mode`/`attempt` the build-vs-rework split. What was missing was **an aggregation** — nothing grouped by `cmd` — plus three fields that were self-reported or not represented at all, shipped 2026-08-31 as SCHEMA §2.6:

| Field | Type | Why it had to exist |
|---|---|---|
| `subagent_runs` | `int?` | *"How many subagents did it spin?"* had **no honest answer**. `subagents` is a list of agent *kinds* the agent types into its own emit — it carries no count when the same kind is spawned four times, and nothing checked it against reality. `subagent_runs` is **counted from the harness's own store** |
| `tokens_out_subagents` | `int?` | The share of the window the children actually consumed; `tokens_out − tokens_out_subagents` is the main thread's own |
| `model_tokens_out` | `Dictionary<string,long>?` | The per-model **split**, not just the winner's name |

`subagents` stays alongside `subagent_runs` because they answer different questions — *which kinds were invoked* (only the agent knows) versus *how many actually ran* (only the harness knows) — and **where they disagree the measured one is right**. The gap between them is itself a finding about how accurately tasks self-report, so the page shows both (BRD-149).

The model split matters more than the model name. A run that spent 90% of its output on one model and 10% on another, and a run that split evenly, are different facts about cost and about routing; `model` (dominant) and `models` (the set) cannot tell them apart, so **any per-model effort figure built on `model` alone silently attributes the whole window to the winner** (BRD-150). The Playbook states the identical rule from the other side: never group a mixed-model execution solely under its dominant model (BRD-158).

**The three denominators — the whole feature rests on these.** Every figure on this page is bounded, and the bound is **on screen next to the figure**, never in a tooltip. This is the same discipline `/misses` applies to `n of N assessed`, for the same reason: *an exclusion the reader cannot see is indistinguishable from a bug.*

| Bound | Excludes | Renders as |
|---|---|---|
| **Token window** (BRD-146) | runs with `tokens_scope: "none"` or no scope — no token numbers exist for them | `measured on n of N runs` on every token tile |
| **Fan-out observation** (BRD-147) | runs whose window was not `tree` scope, split **two ways**: `unobserved_not_tree` (*we did not look*) and `unobserved_predates_field` (*we could not have looked* — written before 2026-08-31) | `observed_n of runs` **first**, numbers second; `observed_n == 0` reads **"not observed"** |
| **Playbook completeness** (BRD-155, BRD-161) | `complete:false` / EOF windows from duration figures; `active_coverage != "complete"` from active-time comparisons; `data_quality.valid:false` from every numeric aggregate | `n of N eligible` beneath each card; incomplete rows stay visible in the table |

The fan-out bound is the one most likely to be got wrong, **and it fails silently**. A `main`-scope window never read the subagent transcripts at all, so `subagent_runs` is absent — and a consumer that coerces that to `0` reports *"this phase spawns no subagents"* when the truth is *"we did not look."* Pooling the two produces a confident fan-out average largely composed of runs that could not have seen a subagent. On today's framework data the honest headline is **1 of 13 runs observed**, which is why the KPI row carries fan-out **coverage** rather than a fan-out average.

**Three timing concepts that must never substitute for one another** (BRD-156), from the Playbook contract:

| Concept | What it means | Summable? |
|---|---|---|
| **Wall-clock elapsed** | how long the command window stayed open | across non-overlapping executions |
| **Observed active time** | the **union** of assistant-message and tool intervals across main and child sessions; overlapping and nested work counted **once** | across non-overlapping executions |
| **Human effort** | time a person spent reading, deciding, waiting, reviewing | **never captured, never inferred** |

`assistant_elapsed_ms` and `tool_elapsed_ms` are diagnostic sums that legitimately overlap — an assistant envelope can contain tool execution — so the producer unions the intervals and TfLens **never adds the components**. Observed active time is busy wall time and is never labelled human effort, CPU time, utilization or additive compute.

**Command phase, not conceptual phase** (BRD-157). The Playbook's `phase` field is the slash command, and one command can contain several lifecycle stages — `/implement` covers build *and* self-review, `/verify` covers verify *and* the results gate. TfLens labels the dimension **Command phase** and never splits one window between conceptual phases by token proportion. The same producer has **no trustworthy cross-command task identity**, so a whole-task total requires an explicit cohort (repository + checklist + the exact execution IDs or time boundary) supplied by ingestion; a reused `session_id` is **not** sufficient, because one session may execute several tasks, and inferring a cohort from it would group unrelated work under a confident total.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Phase effort (TechieFlow) | `/effort` | KPI row (runs · wall clock · output tokens with `measured on n of N` · heaviest phase · **fan-out coverage**) · phase table one row per `cmd` sorted by output share, with `Measured` as a **column** · expandable per-phase detail (Time · Tokens with mean beside median · By model · Fan-out with the declared-vs-measured line) · routing band · Framework switch | [effort.html](./mockups/effort.html) |
| Phase efficiency (Playbook) | `/effort` | Summary cards (completed phases · median & p90 wall clock · complete-coverage active time with `n of N eligible` · five-part token breakdown · measured cost, with rate-card estimates on a **separate** card · `contributors / spawned` · data-quality cards) · charts · execution table with expandable per-model usage, subagent tree and data-quality explanations · filters · empty & unsupported states | [effort-playbook.html](./mockups/effort-playbook.html) |

```mermaid
flowchart TB
  A["runs.jsonl (TechieFlow)"] --> B{"tokens_scope?"}
  B -->|"none / absent"| C["tokens_unmeasured_n<br/>excluded, NEVER counted as zero"]
  B -->|"main / conversation"| D["token figures OK<br/>fan-out: unobserved_not_tree"]
  B -->|"tree"| E{"subagent_runs present?"}
  E -->|"no (pre 2026-08-31)"| F["fan-out: unobserved_predates_field"]
  E -->|"yes"| G["fanout.observed_n<br/>the DENOMINATOR, shown first"]
  H["phase-metric NDJSON (Playbook, schema 2)"] --> I{"data_quality.valid?"}
  I -->|"false"| J["QUARANTINE<br/>zero-valued totals never aggregate"]
  I -->|"true"| K{"complete?"}
  K -->|"false (eof)"| L["no elapsed, no duration figure<br/>visible in the table"]
  K -->|"true"| M{"active_coverage?"}
  M -->|"partial / unavailable"| N["lower bound only<br/>excluded from comparisons"]
  M -->|"complete"| O["eligible for active-time comparison"]
  D --> P["/effort — every figure beside its denominator"]
  G --> P
  O --> P
  L --> P
  N --> P
```

**What this page is not.** Effort per phase is a **budgeting and capacity** view, not a quality scoreboard — quality lives on `/misses` and `/coverage`. `*build-phase` costing more than `*log-miss` is a fact about what those phases *are*, not evidence that one is inefficient, and the page must not frame it as such (BRD-169). Nor is there a per-REQ effort view, per-subagent detail, or an estimated dollar anywhere on it.

**Workflow:**
1. **TechieFlow:** the ordinary sync fetches `runs.jsonl` at the pinned SHA; the parser stores the three §2.6 fields as **nullable** (BRD-145) and an unrecognised producer field goes to `Overflow`, never to `InvalidLines` — §2.5 added fields in August, §2.6 added more, and it will happen again.
2. **Playbook:** the user runs the framework's own exporter and uploads its stdout through **Import metric files** (BRD-153); `TelemetryImportService` recognises the two new entries, archives the bytes verbatim, and hands them to the same parser — no second ingest path (BRD-132).
3. `PhaseMetrics` groups TechieFlow runs by `cmd` and applies the three denominators; the Playbook adapter validates the §3.1 invariants, quarantines invalid rows, and aggregates `phase_model_usage` for anything per-model.
4. `/effort` renders both axes behind the Framework switch; the export writes an `effort` section; `parity-compare.py` diffs the whole `phases` block against the oracle (BRD-152).

**Requirements:** BRD-145, BRD-146, BRD-147, BRD-148, BRD-149, BRD-150, BRD-151, BRD-152, BRD-153, BRD-154, BRD-155, BRD-156, BRD-157, BRD-158, BRD-159, BRD-160, BRD-161, BRD-162, BRD-163, BRD-168, BRD-169

### F-EXPORT: Weekly snapshot export

**Personas:** Author, Parity operator · **Phase:** 2

A button on `/export` and a command verb (`dotnet TfLens.dll export [--date yyyy-MM-dd]`) write two files to `data/reports/<date>/`: `snapshot.md` (human-readable, sectioned exactly like the pages, provenance never mixed in one figure, every estimate labelled) and `tflens.json` (machine-readable; the same key layout as `tf-metrics.sh --rollup --json` — `per_repo`, `tainted_reqs`, `live`, `backfilled`, `pooled` — plus an `extras` object for harness, routing and repricing, and a `parity` object carrying the last recorded parity run). The page lists previous snapshots with download links and shows a **quotable / not quotable** banner: quotable only if the last parity run on record postdates the last parser change (the build stamps a parser version; the parity record stores the version it validated).

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Snapshot export | `/export` | Export button; list of past snapshots; quotable banner; parity status; Framework switch (one snapshot per framework) | [export.html](./mockups/export.html) · Playbook state: [export-playbook.html](./mockups/export-playbook.html) |

**Workflow:**
1. Press Export (or run the verb) → `Analyse()` + extras → write markdown + JSON → refresh list.
2. Banner reads `data/parity-last.json` (written by the parity procedure) and compares parser version.

**Requirements:** BRD-63, BRD-64, BRD-65, BRD-66, BRD-67, BRD-128, BRD-160

### F-PARITY: Parity check against tf-metrics.sh

**Personas:** Parity operator · **Phase:** 2

Two independent implementations now compute the same metrics from the same files: `tf-metrics.sh` (trusted; the §6 rules live in its code) and TfLens (new, unproven). Correct implementations must agree exactly; any disagreement is by definition a bug in TfLens, and the script is never changed to match the app. TfLens ships the tooling that makes the check cheap: the `tflens.json` export in the reference's key layout, a `tools/parity-compare.py` script that compares key-by-key (not a text diff — key order and formatting may differ) and exits non-zero on any mismatch, the `sync_state` SHAs so the same dataset can be checked out for the script, and a `data/parity-last.json` + DECISIONS.md entry that records each passing run (date, dataset SHAs, script hash, compare output). The full procedure and the zero-tolerance rule are in §13; the metrics the script does not compute (harness, routing, repricing) have no oracle and are spot-checked by hand against raw JSONL once, recorded the same way.

```mermaid
flowchart LR
  A["sync_state SHAs"] --> B["clone repos at those SHAs"]
  B --> C["tf-metrics.sh --rollup ... --json > reference.json"]
  D["TfLens export verb"] --> E["tflens.json"]
  C --> F["tools/parity-compare.py reference.json tflens.json"]
  E --> F
  F -->|"empty diff, exit 0"| G["record in DECISIONS.md + data/parity-last.json"]
  F -->|"any diff"| H["bug in TfLens: fix parser/engine, re-run"]
```

**Workflow:** see §13 (mandatory acceptance test).

**Requirements:** BRD-68, BRD-69, BRD-70, BRD-71, BRD-72, BRD-129, BRD-143, BRD-152

### F-FRAMEWORK: Playbook as a first-class framework — the full report set (was F-PB)

**Personas:** User, Owner · **Phase:** 3 *(amended 2026-08-26: replaces "F-PB: Playbook adapter and page")*

TfLens is a lens over **both** frameworks. The owner will build applications on the AI-First-Playbook specifically to collect its telemetry, so Playbook data deserves the same reports as TechieFlow data — not a single page. **Framework** is therefore a third provenance axis beside live/backfilled and `project_type`: every report page (Coverage, Gate outcomes, Harness comparison, Routing & economics, **Misses & rework**, **Phase effort** and Snapshot export) carries a **Framework switch** (TechieFlow | Playbook) in the header, and no figure ever pools across frameworks — the same rule, applied once more. The single `/playbook` page is retired; its content becomes the Playbook state of the report pages.

*Amended 2026-09-01.* The Playbook is **no longer a schema-discovery problem**. It now publishes two normalized producer contracts — a schema-2 `phase-metric` record (`docs/Phase-Efficiency-TfLens-Contract.md`) and a normalized miss export (`docs/Miss-Telemetry-TfLens-From-AIFP.md`) — both emitted as NDJSON on the exporter's **stdout**, with diagnostics on stderr. That changes what path 2 below actually is, and it changes what the Playbook axis of `/misses` shows: real figures where the Playbook emits them, not a blanket empty state (BRD-167). What does **not** change is the axis separation: Playbook `item_id` and TechieFlow `req_id` are two names for the requirement axis, and Playbook process `found_phase_gate` and TechieFlow assertion `found_gate` are two genuinely different measurements — neither pair ever shares a column or a chart (BRD-165), for the same reason `phase_gate` and `gate` never have.

Two ingestion paths, one set of pages:

1. **Schema v1 streams from a Playbook repo.** SCHEMA.md §11 says the Playbook will emit the same four streams (plus `actor`) when it grows agents. A repo whose telemetry path is `docs/metrics/` flows through the *same* parser, engine and pages automatically, tagged `framework: playbook` at connect time — zero new code beyond the tag and the switch.
2. **The Playbook's own normalized exports** (rewritten 2026-09-01). `verification/telemetry/events.ndjson` is **transient and rotates** — it is the exporter's input, not TfLens's. What TfLens consumes is the exporter's normalized stdout: schema-2 `phase-metric` rows (→ three tables, `PbPhaseExecution` / `PbPhaseModelUsage` / `PbPhaseSubagent`, BRD-154) and normalized `miss` / `miss-fix` / `miss-amend` rows (→ the **existing** three miss tables, with the Playbook axes as their own nullable columns, BRD-164). Because the file rotates, TfLens cannot fetch it on a schedule and must not ask the Playbook to commit it, so it arrives through **Import metric files** (BRD-153) — the mode that already exists for exactly this shape of problem. Playbook-native equivalents still cover the three questions per **`phase_gate`** (plan review · verify · gap report · post-verification bugs), phase token/cost totals, main-vs-subagent split and routing/tokens by model. Playbook process-gates (`phase_gate`) and TechieFlow assertion-gates (`gate`) never share a column or a chart (SCHEMA.md §11). Schema discovery is **satisfied for these two record types** — the contracts document every field — and remains open only for anything the exporter does not yet normalize. When the Playbook converges on schema v1, path 2 shrinks to nothing.

Phase order (owner decision 2026-08-26): Phase 3 — after the TechieFlow reports ship and pass parity. Until then the Playbook state of each page shows the "No Playbook data yet" empty state.

| Screen | Route | Description | Mockup |
|--------|-------|-------------|--------|
| Framework switch | (header, every report page) | Segmented control TechieFlow / Playbook; persisted per user; badge with each framework's repo count | [coverage.html](./mockups/coverage.html) (header) |
| Report pages — Playbook state | `/`, `/gate-outcomes`, `/harness`, `/routing`, `/misses`, `/effort`, `/export` | Same layouts as the TechieFlow state; Gate outcomes keyed by `phase_gate`; Coverage shows the imported Playbook streams; `/misses` and `/effort` render real figures where the Playbook emits them (BRD-162, BRD-167), empty state elsewhere | [playbook.html](./mockups/playbook.html) (Coverage) · [gate-outcomes-playbook.html](./mockups/gate-outcomes-playbook.html) · [harness-playbook.html](./mockups/harness-playbook.html) · [routing-playbook.html](./mockups/routing-playbook.html) · [misses-playbook.html](./mockups/misses-playbook.html) · [effort-playbook.html](./mockups/effort-playbook.html) · [export-playbook.html](./mockups/export-playbook.html) |

```mermaid
flowchart LR
  A["Connected repo"] --> B{"Telemetry path?"}
  B -->|"docs/metrics (schema v1)"| C["StreamParser + MetricsEngine<br/>framework tag = techieflow or playbook"]
  B -->|"verification/telemetry/events.ndjson"| D["PlaybookAdapter (Phase 3)<br/>PbEvent tables, phase_gate axis"]
  C --> E["Report pages<br/>Framework switch"]
  D --> E
  E --> F["Never pooled across frameworks"]
```

**Workflow:**
1. Connect detects the path → tags the repo `framework` (F-REPOS).
2. Sync archives raw and parses via the matching path.
3. Every page filters by the selected framework; export writes one snapshot per framework.

**Requirements:** BRD-73, BRD-74, BRD-75, BRD-76, BRD-108, BRD-109, BRD-110, BRD-126, BRD-153, BRD-162, BRD-163, BRD-165, BRD-167

### F-OPS: Container, configuration, health, docs and decisions

**Personas:** Ops · **Phase:** 1 *(amended 2026-08-26: absorbs the settings formerly in F-CFG; PostgreSQL)*

A multi-stage Dockerfile produces one image; a `docker-compose.yml` runs it beside a **PostgreSQL 16** service (owner decision 2026-08-26 — SQLite is unreliable on container storage; Dapper stays the data-access layer via Npgsql). Volumes: `data/` (raw archive, reports, `prices.json`), `logs/`, and the Postgres data directory. All settings come from configuration with secrets **only** via the PascalCase env-var provider: `TfLensAppManagerApiKey`, `TfLensAppManagerApiSecret`, `TfLensDbConnection` (required); `TfLensGitHubToken` (optional — raises the GitHub API rate limit for public reads). Non-secret: `TfLensAppManagerBaseUrl` (default `https://appmgrapi.techierathore.com`), `TfLensAppManagerAppId` (default `1`), `PollIntervalMinutes` (default 15), `DataRoot` (default `data/`). Startup validates the configuration, applies the idempotent schema script `database/001-schema.sql`, and refuses to run with a missing secret or an unreachable database, logging a redacted reason. `/healthz` (anonymous) reports database reachability and the age of the last successful sync, nothing else. The README states the out-of-scope list verbatim (§3) and the run/rebuild/export commands. `DECISIONS.md` is created at day-1 build time and records: the storage choice (Dapper + PostgreSQL, superseding SQLite), the dedupe keys, the parser version scheme, anything cut for the timebox, and every parity run.

**Workflow:**
1. `docker compose up` → `postgres` + `tflens`; secrets from the environment.
2. Startup: validate config → apply schema script → start poller + web host.
3. `docker exec <c> dotnet TfLens.dll rebuild|sync|export` for operations.

**Requirements:** BRD-8, BRD-9, BRD-10, BRD-11, BRD-77, BRD-78, BRD-79, BRD-80, BRD-81, BRD-111, BRD-144

## 10. Functional requirements (BRD ledger)
- <a id="brd-1"></a>**BRD-1** — User can sign in at `/login` with their AppManager email and password and is redirected to the requested page (first sign-in with no repos lands on `/repos`). *(F-AUTH — amended 2026-08-26)*
- <a id="brd-2"></a>**BRD-2** — System shall place every page except `/login`, `/register`, `/forgot-password`, `/reset-password` and `/healthz` behind cookie authentication (sliding 12 h, HttpOnly, Secure). *(F-AUTH — amended 2026-08-26)*
- ~~**BRD-3**~~ *(removed 2026-08-26: local PBKDF2 credential store superseded by AppManager — see BRD-90)*
- <a id="brd-4"></a>**BRD-4** — User can sign out from the **user menu** in the header (name → DropdownMenu → Sign out), which calls AppManager `/AuthSvc/logout` and clears the cookie. *(F-SHELL — amended 2026-08-26)*
- <a id="brd-5"></a>**BRD-5** — User can navigate between Repos, Coverage, Gate outcomes, Harness, Routing & economics, **Misses & rework**, **Phase effort** and Snapshot export via a TrBlazeUI sidebar with a Lucide icon per item, in that order (Playbook page retired — framework is a header switch, BRD-108). *(F-SHELL — amended 2026-08-26 ×2, 2026-08-28: seven items, Misses & rework between Routing and Snapshot export; 2026-09-01: **eight** items, Phase effort between Misses & rework and Snapshot export)*
- <a id="brd-6"></a>**BRD-6** — User can press **Sync now** in the header and see the last-sync timestamp and a per-repo outcome toast for their own repos. *(F-SHELL — amended 2026-08-26)*
- ~~**BRD-7**~~ *(removed 2026-08-26: no repo list or demo seed in configuration — repos are managed only on the Repos screen, F-REPOS)*
- <a id="brd-8"></a>**BRD-8** — System shall read the AppManager API key/secret, the database connection string and the optional GitHub PAT only from environment / user-secrets via the PascalCase env-var provider (`TfLensAppManagerApiKey`, `TfLensAppManagerApiSecret`, `TfLensDbConnection`, `TfLensGitHubToken`), never from files in the repo. *(F-OPS — amended 2026-08-26 ×2)*
- <a id="brd-9"></a>**BRD-9** — System shall refuse to start when a required secret is missing or the database is unreachable, logging a redacted reason. *(F-OPS — amended 2026-08-26 ×2)*
- <a id="brd-10"></a>**BRD-10** — System shall never log, display, or export the AppManager secret, the connection string, the PAT, or any AppManager token. *(F-OPS — amended 2026-08-26 ×2)*
- <a id="brd-11"></a>**BRD-11** — Ops can override `DataRoot` (default `data/`) for the raw archive, reports and `prices.json`. *(F-OPS — amended 2026-08-26)*
- <a id="brd-12"></a>**BRD-12** — System shall poll every connected repo of every user on the configured interval via a `BackgroundService`. *(F-SYNC — amended 2026-08-26)*
- <a id="brd-13"></a>**BRD-13** — System shall, per repo, read the latest commit SHA touching the telemetry path on the configured branch and skip the repo when it equals the stored SHA. *(F-SYNC)*
- <a id="brd-14"></a>**BRD-14** — System shall fetch each stream file whole at that exact SHA and treat a 404 as "stream absent" (zero records), not an error. *(F-SYNC)*
- <a id="brd-15"></a>**BRD-15** — System shall isolate errors per repo (401/403/404/network), record a redacted reason in `sync_state.LastError`, and continue with the remaining repos. *(F-SYNC)*
- <a id="brd-16"></a>**BRD-16** — System shall be structurally read-only against GitHub: only GET requests, contents-read scope, no code path that writes to any repository. *(F-SYNC)*
- <a id="brd-17"></a>**BRD-17** — System shall update `sync_state` (per user and repo: last SHA, last sync ts, per-stream record counts, last error) after each repo sync. *(F-SYNC — amended 2026-08-26)*
- <a id="brd-18"></a>**BRD-18** — System shall invalidate cached analysis results after every completed sync or rebuild. *(F-SYNC)*
- <a id="brd-19"></a>**BRD-19** — System shall store every stream file verbatim under `data/raw/<userId>/<source>/<stream>-<sha>.jsonl` before parsing it, where `<sha>` is the commit SHA for a fetched file and the **bundle sha256** for an imported one. *(F-RAW — amended 2026-08-26, 2026-08-28)*
- <a id="brd-20"></a>**BRD-20** — Ops can run `rebuild` (command verb) to truncate the stream tables in PostgreSQL, re-apply the schema script and reparse every archived raw file. *(F-RAW — amended 2026-08-26)*
- <a id="brd-21"></a>**BRD-21** — Owner can trigger the same rebuild from the Coverage page behind a confirmation dialog. *(F-RAW)*
- <a id="brd-22"></a>**BRD-22** — System shall report, after a rebuild, files replayed, records stored and duplicates collapsed per stream, and produce the same counts as live syncing did. *(F-RAW)*
- <a id="brd-23"></a>**BRD-23** — System shall store each stream in its own PostgreSQL table (`Run`, `Gate`, `Session`, `Commit`) plus `SyncState`, via Dapper + Npgsql, with columns named exactly after SCHEMA.md fields (PascalCase, quoted identifiers); the `misses` stream, whose records do not all share a shape, occupies **three** tables (`Miss`, `MissFix`, `MissAmend` — BRD-115), and the Playbook's schema-2 phase metrics occupy **three** more (`PbPhaseExecution`, `PbPhaseModelUsage`, `PbPhaseSubagent` — BRD-154). *(F-PARSE — amended 2026-08-26, 2026-08-28, 2026-09-01)*
- <a id="brd-24"></a>**BRD-24** — System shall keep unknown properties (and all properties of records with `v > 1`) in a JSON `Overflow` column instead of dropping them. *(F-PARSE)*
- <a id="brd-25"></a>**BRD-25** — System shall count and skip lines that are not valid JSON, as the reference does. *(F-PARSE)*
- <a id="brd-26"></a>**BRD-26** — System shall dedupe `commits` on `sha` per repo, keeping the first and counting the collapsed duplicates. *(F-PARSE)*
- <a id="brd-27"></a>**BRD-27** — System shall keep, per `session_id`, only the session record with the highest `output_tokens` (tie: latest `ts`). *(F-PARSE)*
- <a id="brd-28"></a>**BRD-28** — System shall dedupe `runs` on `ts+app+cmd` and `gates` on `ts+app+req_id+run_id` so re-parsing never double-counts. *(F-PARSE)*
- <a id="brd-29"></a>**BRD-29** — System shall preserve `backfilled`, `inferred`, `project_type`, `project_type_inferred`, `harness`, `tokens_scope`, every §2.5 optional field and every **§2.6** optional field (`subagent_runs`, `tokens_out_subagents`, `model_tokens_out` — BRD-145) verbatim, storing absent optionals as `NULL` never `0`. The distinction is load-bearing on all three: `null` means *not captured*, and collapsing it to a measured zero is the single defect that most determines whether these pages are trusted. *(F-PARSE — amended 2026-09-01)*
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
- <a id="brd-51"></a>**BRD-51** — User can see per harness — columns **`claude-code`, `opencode`, `codex`** — run counts by command, gate verdict mix, session counts, and token totals at `/harness`. *(F-HARN — amended 2026-08-26)*
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
- <a id="brd-73"></a>**BRD-73** — System shall fetch `verification/telemetry/events.ndjson` (and the joiner output if committed) for Playbook repos that carry it, archive raw, and parse into separate `PbEvent` tables with overflow. **Amended 2026-09-01:** `events.ndjson` is the Playbook exporter's **transient** input — it rotates by design and is frequently absent — so it is a best-effort fetch, never a required one, and its absence is never an error nor a zero-valued run. The Playbook's durable, normalized inputs are the exporter's stdout (schema-2 `phase-metric` rows, BRD-153) and its committed `misses.ndjson`, and TfLens never runs the exporter itself. *(F-FRAMEWORK — amended 2026-08-26, 2026-09-01)*
- <a id="brd-74"></a>**BRD-74** — System shall keep Playbook `phase_gate` data in separate tables and charts from TechieFlow `gate` data — never a shared column or chart. *(F-FRAMEWORK)*
- <a id="brd-75"></a>**BRD-75** — User can see, in the Playbook state of the report pages, the Playbook-native three questions per `phase_gate`, phase token/cost totals, the main-vs-subagent split via `parentID`, and routing/tokens where present. *(F-FRAMEWORK — amended 2026-08-26; replaces the single `/playbook` page)*
- <a id="brd-76"></a>**BRD-76** — System shall record the observed `events.ndjson` field names in DECISIONS.md before the adapter's columns are fixed (schema-discovery first). *(F-FRAMEWORK)*
- <a id="brd-77"></a>**BRD-77** — Ops can build one Docker image (multi-stage, .NET 10) and run it with `data/` and `logs/` volumes and env-var secrets. *(F-OPS)*
- <a id="brd-78"></a>**BRD-78** — Ops can call `/healthz` anonymously and get DB reachability plus last-successful-sync age, nothing else. *(F-OPS)*
- <a id="brd-79"></a>**BRD-79** — System shall ship a README that states the out-of-scope list verbatim and the run / rebuild / sync / export commands. *(F-OPS)*
- <a id="brd-80"></a>**BRD-80** — System shall ship `DECISIONS.md` recording the storage choice, dedupe keys, parser version scheme, timebox cuts, and every parity run. *(F-OPS)*
- <a id="brd-81"></a>**BRD-81** — Ops can run `sync` as a command verb for a one-off headless sync. *(F-OPS)*

## 11. Non-functional requirements
- <a id="brd-82"></a>**BRD-82** — Performance: report pages render from the memoised analysis within a second for the expected data volume (tens of thousands of records across ≤10 repos); a full sync of 5 repos completes in under 30 s on a normal connection. Targets:

  | Metric | Target | Notes |
  |--------|--------|-------|
  | Page render (cached analysis) | p95 load ≤ 1500 ms | single user |
  | Cold analysis (after sync) | ≤ 3 s for 50k records | computed once per sync |
  | Sync, 5 repos, unchanged | ≤ 5 s | SHA lookup only |
  | Rebuild, 5 repos × 20 SHAs | ≤ 60 s | replay from raw |

  perf-budget: p95 load <= 1500ms @ concurrency 1
- <a id="brd-83"></a>**BRD-83** — Security: cookie auth on every page (HttpOnly, Secure, SameSite=Lax); antiforgery on forms; secrets only via environment; PAT is fine-grained contents-read; no inbound API; HTTPS terminated by the VPS proxy (out of scope) — the app sets `ForwardedHeaders` accordingly.
- <a id="brd-84"></a>**BRD-84** — Privacy: TfLens displays and stores only what the streams carry (IDs, counts, durations, verdicts, short SHAs); no requirement text, no commit subjects, nothing from `src/`; the `Overflow` column is never rendered, only its field names.
- <a id="brd-85"></a>**BRD-85** — Accessibility & theme: TrBlazeUI components with semantic markup; every figure has a text equivalent (charts are supplementary); `insufficient data` and `estimate` labels are text, not colour alone; keyboard-reachable Sync / Export / Rebuild / user menu; **dark mode is the default** on first visit, light available via the header toggle, choice persisted per user *(amended 2026-08-26)*.
- <a id="brd-86"></a>**BRD-86** — Observability: Serilog file-based logging in the single executable head — rolling file sink under `logs/` (`logs/tflens-.log`, daily, 14 files retained) plus console, wired at startup before the host builds, unhandled exceptions logged at the boundary, `Log.CloseAndFlush()` on exit (see Coding Standards §Logging). Sync outcomes logged per repo with counts and SHAs only.
- <a id="brd-87"></a>**BRD-87** — Reliability: a failing repo never fails a sync; a failing sync never affects served pages (last good analysis stays); the database can be rebuilt from `data/raw/` at any time with identical counts.
- <a id="brd-88"></a>**BRD-88** — Testability: the engine and parser are in `TfLens.Core` with no web dependency; fixture JSONL under `tests/`; Blazor screens use stable `data-testid` ids for Playwright.
- <a id="brd-89"></a>**BRD-89** — Integrity: the provenance rules (BRD-31..36) have no configuration switch, no query parameter and no UI toggle that relaxes them.

### Amendment 2026-08-26 — identity, repo management, shell
- <a id="brd-90"></a>**BRD-90** — User can sign in with email + password via AppManager `POST /AuthSvc/login` (App Id 1, `X-Api-Key`/`X-Api-Secret` headers, password RSA-OAEP-256-encrypted with the cached `/AuthSvc/public-key`); TfLens stores no passwords. *(F-AUTH)*
- <a id="brd-91"></a>**BRD-91** — User can self-register at `/register` via `POST /AuthSvc/register` with `applicationRoleCode: "Manager"`, with the AppManager password rules validated locally first. *(F-AUTH)*
- <a id="brd-92"></a>**BRD-92** — User can request a password reset at `/forgot-password` and complete it at `/reset-password?token=…` via AppManager, with an enumeration-safe message. *(F-AUTH)*
- <a id="brd-93"></a>**BRD-93** — System shall issue its own auth cookie (userId, email, name, role) and hold the AppManager access/refresh tokens server-side, refreshing via `/AuthSvc/refresh` before expiry, validating a resumed cookie via `/AuthSvc/validate`, and calling `/AuthSvc/logout` on sign-out. *(F-AUTH)*
- <a id="brd-94"></a>**BRD-94** — *(Phase 2 — deferred 2026-08-26)* User can sign in with GitHub as SSO, with the user record living in AppManager; blocked until AppManager offers an external-login / token-exchange endpoint (or a TfLens bridge is accepted). Not in this release; login screen shows no GitHub button. *(F-AUTH)*
- <a id="brd-95"></a>**BRD-95** — System shall treat every user as AppManager `Manager` for Application 1 and shall never call LicenseSvc, FeatureSvc, PaymentSvc or IssueSvc. *(F-AUTH)*
- <a id="brd-96"></a>**BRD-96** — System shall have a demo user `TfLensDemo` (`tflensdemo@techierathore.com`) registered in AppManager during development, listed as UsageGuide test user #1, with its public demo repos connected through the Repos screen (no configuration seed). *(F-AUTH — amended 2026-08-26)*
- <a id="brd-97"></a>**BRD-97** — System shall read the AppManager connection from configuration only: `TfLensAppManagerBaseUrl`, `TfLensAppManagerAppId` (1), `TfLensAppManagerApiKey`, `TfLensAppManagerApiSecret`; the key and secret never appear in the repo, logs or UI. *(F-AUTH)*
- <a id="brd-98"></a>**BRD-98** — User can see their connected sources at `/repos` (owner/name, branch, kind, **source** — `Synced` or `Imported` — visibility badge, status, last sync **or** last import, per-stream counts) with per-row Sync **or** Re-import, and Remove. *(F-REPOS — amended 2026-08-28)*
- <a id="brd-99"></a>**BRD-99** — In **Fetch via API** mode, user can connect a repo by GitHub URL or `owner/name` (+ branch); system shall validate through the GitHub API that it exists, is public, and has the telemetry path for its kind, auto-detecting the kind. *(F-REPOS — amended 2026-08-28: this is now one of the dialog's two modes, BRD-131)*
- <a id="brd-100"></a>**BRD-100** — System shall refuse private repos **in Fetch via API mode** with an explicit message that names the alternative — *"private repos can't be fetched; use **Import metric files** to add this repo's telemetry without a credential"* — and shall offer the mode switch inline rather than dead-ending the user; the server PAT is optional and only raises the rate limit. *(F-REPOS — amended 2026-08-28: refusal now has an exit, BRD-131)*
- <a id="brd-101"></a>**BRD-101** — User can remove a connected source after confirmation; system shall stop its sync and purge that user's parsed rows and raw archive for it — identically for a fetched and an imported source. *(F-REPOS — amended 2026-08-28, see BRD-141)*
- <a id="brd-102"></a>**BRD-102** — System shall scope every page, sync, export, cache and stored row to the signed-in user (`UserId` on `sync_state`, stream tables, raw archive path and reports path); no user can see another user's repos or figures. *(F-REPOS)*
- <a id="brd-103"></a>**BRD-103** — System shall sync every user's **fetched** repos in the background poller and only the signed-in user's fetched repos on header Sync now, keeping errors per user and source; **imported sources are skipped by the poller and by Sync now** — they have no remote to contact. *(F-REPOS — amended 2026-08-28)*
- <a id="brd-104"></a>**BRD-104** — System shall reject a duplicate source name for the same user — whichever mode either was added in, so an imported source can never shadow a fetched one — and allow different users to connect the same public repo independently. *(F-REPOS — amended 2026-08-28)*
- <a id="brd-105"></a>**BRD-105** — User can collapse and expand the sidebar (`SidebarTrigger`); collapsed items show icon + tooltip; the state is remembered. *(F-SHELL)*
- <a id="brd-106"></a>**BRD-106** — System shall show the signed-in user's display name in the header with a DropdownMenu (Profile, Manage repos, Sign out). *(F-SHELL)*
- <a id="brd-107"></a>**BRD-107** — User can view their AppManager profile and change their password at `/profile` (`GET /UserSvc/profile`, `POST /UserSvc/change-password`, both passwords RSA-encrypted). *(F-AUTH)*

### Amendment 2026-08-26 (round 2) — both frameworks, Codex, PostgreSQL
- <a id="brd-108"></a>**BRD-108** — User can switch every report page (Coverage, Gate outcomes, Harness, Routing & economics, **Misses & rework**, **Phase effort**, Snapshot export) between **TechieFlow** and **Playbook** via a header Framework switch; the system shall never pool any figure across frameworks (a third provenance axis, same rule as `project_type`); the choice is persisted per user. *(F-FRAMEWORK, F-SHELL — amended 2026-08-28: six report pages; 2026-09-01: the switch now spans **seven**)*
- <a id="brd-109"></a>**BRD-109** — System shall run a Playbook repo that emits schema v1 streams (`docs/metrics/*.jsonl`) through the same parser, engine and pages as TechieFlow repos, tagged `framework: playbook` at connect time. *(F-FRAMEWORK)*
- <a id="brd-110"></a>**BRD-110** — System shall produce Playbook-native equivalents of the full report set (three questions per `phase_gate`, phase totals, main-vs-subagent split, routing/tokens where present, snapshot export) from separate tables — Phase 3. **Amended 2026-09-01:** this is **no longer schema-discovery-first** for the two record types the Playbook now normalizes — schema-2 `phase-metric` (BRD-153..BRD-163) and the miss export (BRD-164..BRD-167) have published field lists, invariants and reporting guards, so their columns are fixed from the contract rather than from a first parse. Schema discovery, and its DECISIONS.md record (BRD-76), remain required only for anything the exporter does not yet normalize. *(F-FRAMEWORK — amended 2026-09-01)*
- <a id="brd-111"></a>**BRD-111** — Ops can run TfLens with `docker compose` beside a PostgreSQL 16 service; the system shall apply `database/001-schema.sql` idempotently at startup and read the connection string from `TfLensDbConnection`. *(F-OPS)*

### Amendment 2026-08-28 — miss telemetry and rework economics

*Source: `docs/Miss-Telemetry-TfLens.md` (and its sibling `docs/Miss-Telemetry-TechieFlow.md`, the producing side, which shipped 2026-08-28). All Phase 3. Nothing above is renumbered; BRD-101's purge clause and BRD-17's per-stream counts are generic and already reach the new tables and the fifth stream.*
- <a id="brd-112"></a>**BRD-112** — System shall fetch, archive raw and parse a fifth TechieFlow stream, `docs/metrics/misses.jsonl`, on the same SHA-skip / whole-file / 404-means-absent contract as the other four; a repo that does not emit it produces an empty stream row, never an error, and no coordination window is needed in either deploy order. *(F-MISS, F-SYNC)*
- <a id="brd-113"></a>**BRD-113** — System shall dispatch on each record's own `kind` **within** the `misses` stream — `miss` → `MissRecord`, `miss-fix` → `MissFixRecord`, `miss-amend` → `MissAmendRecord` — and shall count-and-skip any other `kind` as an invalid line, never throwing; a malformed miss record shall never fail a sync. *(F-MISS, F-PARSE)*
- <a id="brd-114"></a>**BRD-114** — System shall dedupe the three kinds on their natural keys per user and repo — `miss` on `(MissId)` keeping the **earliest** `Ts`, `miss-fix` on `(MissId, FixRunId)` keeping the latest, `miss-amend` on `(MissId, Field, Ts)` keeping the earliest — so that re-parsing an archived file or replaying it during rebuild never double-inserts. *(F-MISS, F-PARSE)*
- <a id="brd-115"></a>**BRD-115** — System shall store the stream in three tables (`Miss`, `MissFix`, `MissAmend`) in the existing house style (`UserId` a real column and part of every unique index, `CREATE TABLE IF NOT EXISTS`), and `DeleteRepoDataAsync` shall purge **all three** — a repo removal that leaves rows behind would reappear in every figure. *(F-MISS, F-RAW, F-PARSE)*
- <a id="brd-116"></a>**BRD-116** — System shall fold `miss-amend` records into their parent **at read time, never at ingest** — oldest first, re-applying the null-check while folding (a merged stream can carry an amend and a later-written value in either order), applying only fields on the allowlist with values inside their closed vocabulary. An amend naming no known `miss`, or carrying a field off the allowlist, is an **orphan**: counted and surfaced on Coverage, never applied. `rebuild` shall re-derive identical values. *(F-MISS, F-ENGINE)*
- <a id="brd-117"></a>**BRD-117** — System shall enforce an eligibility floor per optional field (`FIELD_SINCE`, `why_missed` since 2026-08-28) in the same code path as the existing `LATE_GATES` rule: a miss written before the field existed leaves that field's denominator entirely and is reported separately as `why_missed_eligible` / `why_missed_predates_field`. *(F-MISS, F-ENGINE)*
- <a id="brd-118"></a>**BRD-118** — Owner can see, per `project_type` and live-only: miss class distribution, design-miss share (`unspecified-gap` ÷ all), escape share of misses (`FoundBy ∈ {owner, production}` ÷ all), miss rate per origin phase, and median time-to-close. The miss-stream escape share is rendered **beside** the existing `gates`-derived escape rate and never merged into it — escape rate keeps its definition and its source. *(F-MISS)*
- <a id="brd-119"></a>**BRD-119** — Owner can see the **failed-practice distribution** (`WhyMissed`: `missing-checklist-item` · `insufficient-verify-method` · `code-audit-limitation` · `ambiguous-acceptance` · `dependency-not-declared` · `instruction-ignored` · `other`) whose denominator is **records that carry the field**, printed as `n of N misses assessed` on its face; `null` means *not assessed* and shall never be coerced into a bucket. *(F-MISS)*
- <a id="brd-120"></a>**BRD-120** — Owner can see **open misses** (latest `MissFix.VerdictAfter ∉ {Verified, wont-fix}`; `deferred` stays open) and **declined misses** (`wont-fix`) as two separate figures; the system shall never fold `wont-fix` into open, and shall never reconcile this predicate with the producer's collapse check — they ask different questions and agreeing would break one of them. *(F-MISS)*
- <a id="brd-121"></a>**BRD-121** — System shall compute every per-origin-phase, per-origin-model and per-origin-agent figure from `OriginConfidence == "linked"` records only (`MissAttributionTaint`, a sibling of `TaintSet`), and shall display how many misses were excluded and why — an exclusion the reader cannot see is indistinguishable from a bug. *(F-MISS, F-ENGINE)*
- <a id="brd-122"></a>**BRD-122** — System shall return every miss token/cost figure as a result type that carries the attribution split — `MissCost(Figure Sole, Figure Apportioned, int NoneCount)` — so that a page binding it **cannot render a blended number, because no such property exists**. Headline cost-per-miss is computed over `CostAttribution == "sole"` only; `none` (including the deliberate `log-miss --fixed` path that omits `fix_run_id`) is a count, never a divisor. *(F-MISS, F-ENGINE)*
- <a id="brd-123"></a>**BRD-123** — Owner can see the money answer per harness with no new machinery: **measured USD** from `CostUsd` for OpenCode (the only measured dollars in the product), and **tokens as the primary figure** for Claude Code and Codex with USD only via `RateCard`, carrying `RateCard.EstimateLabel` on its face and a `_usd_estimate` key in every export. The estimate tile shall be visually distinct from the measured tile — never the same row, never the same styling. *(F-MISS, F-ROUTE)*
- <a id="brd-124"></a>**BRD-124** — User can open **`/misses` — "Misses & rework"**, a sixth report page and a nav item between Routing and Snapshot export, laid out in four bands: KPI row (open · declined · misses this period · design-miss share · escape share · tokens on rework · measured USD on rework) · **where misses come from** (origin phase × miss class with the excluded-attribution count beneath, beside the failed-practice distribution) · **who was running** (origin model and origin agent, `linked` only, taint count visible, band labelled **observational** in standing copy because miss counts per model are confounded by which model gets the hard work) · **cost of rework** (`MissCost` rendered as shaped). A per-miss detail table (id · REQ · class · severity · origin · found by · status · tokens) puts the raw record behind a disclosure. *(F-MISS)*
- <a id="brd-125"></a>**BRD-125** — The `/misses` page shall default to **all history**, with a period filter that narrows the view but does not gate the first one — miss counts are low-volume and a default period would routinely render `insufficient data (n=…)` on a page whose whole job is to show a trend. *(F-MISS)*
- <a id="brd-126"></a>**BRD-126** — `/misses` shall carry the header Framework switch like every other report page and shall show the `PlaybookEmpty` state on the Playbook axis until the Playbook emits miss data; the switch is rendered, never hidden, for a surface one framework has and the other does not. **Amended 2026-09-01:** the Playbook *does* now emit miss data, so the empty state is no longer the permanent condition of that axis — it is what shows when no Playbook miss records have been imported for the signed-in user. Where they have, the axis renders real figures (BRD-167). *(F-MISS, F-FRAMEWORK — amended 2026-09-01)*
- <a id="brd-127"></a>**BRD-127** — Owner can see, on Coverage: the per-repo stream table with **five** rows; `escapes_missing_why` (escapes arriving with no `WhyMissed` — a data-quality figure, stated here and never on the `/misses` KPI row); the **`project_type` reclassification split** stated in words whenever a repo's current classification disagrees with its own stored records, each segment described as a *period* of the project rather than as the whole of it; and the orphan `miss-fix` / `miss-amend` counts. A repo emitting misses with no `miss-fix` records at all is a **warning, not an error**. *(F-MISS, F-COVER)*
- <a id="brd-128"></a>**BRD-128** — System shall write a `misses` section into both export files; the attribution split shall survive into the JSON as **three distinct keys**, never collapsed for tidiness, and every rate-card figure's key shall end `_usd_estimate` while measured ones do not. *(F-MISS, F-EXPORT)*
- <a id="brd-129"></a>**BRD-129** — Parity shall cover the miss figures key-for-key against `tf-metrics.sh --rollup --json`'s `misses` block (`misses_total` · `miss_fixes_total` · `orphan_fixes` · `open_misses` · `wont_fix` · `resolved_misses` · `why_missed_n` · `why_missed{}` · `escapes_missing_why` · `why_missed_eligible` · `why_missed_predates_field` · `amendments_applied` · `orphan_amends` · `class_distribution{}` · `found_by{}` · `design_miss_share` · `escape_share` · `attributed_n` · `attribution_excluded` · `by_origin_phase{}` · `by_origin_model{}` · `by_origin_agent{}` · `cost_sole_n` · `cost_shared_n` · `cost_unattributable_n` · `tokens_per_miss_measured` · `tokens_per_miss_apportioned` · `cost_usd_per_miss_measured` · `cost_usd_records`) — the oracle already ships them, so no miss figure ships marked unverified. *(F-MISS, F-PARITY)*
- <a id="brd-130"></a>**BRD-130** — Integrity: the miss invariants shall have no configuration switch, no query parameter and no UI toggle that relaxes them — no blended measured-and-apportioned cost anywhere (page, export or parity); no per-model or per-agent figure computed from `inferred` attributions, and no hidden exclusion count; no `WhyMissed` distribution rendered over all misses; no `wont-fix` folded into open; no rate-card dollars presented as spend; no miss record folded into the existing `gates`-derived escape rate. *(F-MISS — the NFR sibling of BRD-89)*

### Amendment 2026-08-28 (round 2) — imported telemetry: private and corporate repos

*Owner decision the same day: **fold this into the Repos screen** rather than give it a screen of its own, so the two ways of adding a source sit side by side in one dialog and the demarcation is visible in one grid. No new nav item, no new route. All Phase 3.*
- <a id="brd-131"></a>**BRD-131** — User can choose, in the first step of the Add-source dialog on `/repos`, between **Fetch via API** (public repos — the existing path, BRD-99) and **Import metric files** (any repo, including **private and corporate** ones). The mode is a deliberate, visible fork, not a fallback the user discovers after a failure — and BRD-100's refusal of a private repo offers the switch inline. *(F-REPOS)*
- <a id="brd-132"></a>**BRD-132** — System shall record the mode on the source row as `SourceKind` (`api` | `import`) and surface it as a **Source** column on `/repos` (`Synced` / `Imported` badge) — one column, after which every downstream path (per-user isolation, raw archive, parser, dedupe, engine, cache, export) is unchanged and shared. There shall be **no second ingest code path**. *(F-REPOS)*
- <a id="brd-133"></a>**BRD-133** — In import mode, user can upload a `.zip` of a telemetry directory (`docs/metrics/` for TechieFlow, `verification/telemetry/` for the Playbook) **or** loose `.jsonl` / `.ndjson` files, exactly as the frameworks already write them to disk; system shall archive the bytes **verbatim before parsing** them and shall then run the identical parser, dedupe and engine. **Neither framework is asked to add an export command** — TfLens accepts what already exists. *(F-REPOS, F-RAW)*
- <a id="brd-134"></a>**BRD-134** — System shall compute a **sha256 of the uploaded bundle** and use it as that source's dataset identity wherever a fetched source uses its commit SHA — the raw-archive filename, the Coverage row, the export's `per_repo` block, and the dataset the parity operator pins (BRD-70, §13). For an imported source this is *stronger* than a commit SHA: the operator runs `tf-metrics.sh` against the identical bytes rather than re-cloning and trusting the result matched. *(F-REPOS, F-PARITY)*
- <a id="brd-135"></a>**BRD-135** — Re-import shall be idempotent: the streams' natural keys already collapse duplicates (BRD-26..BRD-28, BRD-114), so a user may re-upload the same bundle, or a later superset of it, without double-counting; the system shall report records added and duplicates collapsed per stream, exactly as a rebuild does. An imported source's row action is **Re-import**, never a Sync button that would do nothing. *(F-REPOS)*
- <a id="brd-136"></a>**BRD-136** — System shall **display source origin everywhere and pool on it nowhere**: a `Synced` / `Imported` badge per source on `/repos` and Coverage, and a `source_kind` key in the export's per-repo block. Origin is a property of *delivery*, not of the data — a record's `backfilled`, `project_type`, `harness` and `origin_confidence` fields mean the same thing whichever way the line arrived — so it is **not** a fifth segmentation axis and never divides a figure. This is the same discipline as the taint counts: shown, never hidden, never a divider. *(F-REPOS, F-ENGINE)*
- <a id="brd-137"></a>**BRD-137** — Coverage shall read staleness differently for an imported source: **days since import**, with the words *"this source can't refresh itself — re-import to update"*, so a snapshot never reddens the health badge merely for being a snapshot; the hook/pushing diagnosis (BRD-41) applies only to fetched sources, where it is meaningful. *(F-COVER)*
- <a id="brd-138"></a>**BRD-138** — System shall **preview before it commits**: after unpacking, the dialog shall show records per stream, the date range, invalid lines, unknown field names and the bundle sha256, and shall write nothing until the user presses Import. A malformed or empty bundle is reported in the preview and never partially ingested. *(F-REPOS)*
- <a id="brd-139"></a>**BRD-139** — The upload surface shall be bounded and is the **only** inbound path: authenticated and per-user; `.zip`, `.jsonl` and `.ndjson` only; a size cap (25 MB) enforced before reading; archive extraction safe against path traversal and archive bombs (entry-count and uncompressed-size limits, no absolute or `..` paths, no symlinks); nothing in an upload is ever executed, rendered as HTML, or written outside `data/raw/<userId>/`; there shall be **no unauthenticated endpoint and no machine-to-machine ingest API** (§3). *(F-REPOS, F-OPS)*
- <a id="brd-140"></a>**BRD-140** — System shall **refuse a precomputed rollup** — `tf-metrics.sh --rollup --json` output, a `tflens.json`, or an exported snapshot — with an explicit message naming what to upload instead. TfLens computes every figure at request time from raw records (BRD-30); importing conclusions rather than evidence would let a plausible wrong number in through the front door, which is the one failure this product exists to prevent. *(F-REPOS, F-ENGINE)*
- <a id="brd-141"></a>**BRD-141** — Removing an imported source shall purge its parsed rows in every stream table and its raw archive exactly as BRD-101 does for a fetched one; no import-only cleanup path shall exist. *(F-REPOS)*

### Amendment 2026-08-28 (round 3) — the test accounts are part of the product, not of the tester's memory
- <a id="brd-142"></a>**BRD-142** — The AppManager accounts the automated suite signs in with shall be **provisioned, documented and restorable from inside this repository**, without help from anyone outside it. `docs/TfLens-UsageGuide.md` is the single source for those credentials; the system shall ship a repeatable procedure that restores a known-good credential for every account that table names, and a guardrail test shall fail when the suite signs in as an account the guide does not list. Any test that mutates an account's password shall restore it, or provision its own throwaway account rather than touch a shared one. *(F-AUTH, F-OPS)*

  **Why this is a requirement and not a chore.** TfLens holds no user table — identity is a live external service (BRD-90), so the authenticated half of the suite rests on mutable state that nothing in the repository could previously rebuild. On 2026-08-28 that failed exactly as the shape predicts: the accounts were removed on the AppManager side and **seven tests plus every authenticated screen became un-verifiable at once**, with no path back that did not involve a human remembering what the passwords had been. A dependency that can silently invalidate the whole verification surface, and that only a person's memory can restore, is a product defect — recorded as `MISS-TfLens-20260828-02` (`unspecified-gap` / `tests` / `blocker`) and built as `REQ-NFR-012`.

### Amendment 2026-08-29 — two gates the product was relying on and had never written down

Both clauses below were already being *enforced by hand* — the first by the parity operator noticing that a count disagreed with upstream, the second by the owner opening the mockups beside the running app. Neither was a requirement, so neither had a gate, and on 2026-08-29 both failed in the same session against a checklist that read 145 `Verified`. They are appended here so the checklist rows built for them (`REQ-NFR-019`, `REQ-NFR-020`) are **owned by the BRD rather than inferred from a finding**.
- <a id="brd-143"></a>**BRD-143** — Stored provenance shall be real: every row in every stream table shall carry a `source_sha` that a sync or an import actually recorded, and no path shall write a row with provenance nobody obtained. A seeding or fixture harness shall not write into the application's own store, or shall write only under a user id the application's own queries exclude, so demo data can never reach a published figure. The system shall ship a check that reports any `source_sha` present in the store which no `SyncState` row or import bundle accounts for — detectable without a network call and without hand-comparing counts against GitHub — and `/export` shall refuse to mark a snapshot **QUOTABLE** while such a row is present, for the same reason it refuses when the reference script has changed (BRD-67, §13). *(F-PARITY, F-PARSE, F-RAW, F-EXPORT)*

  **Why this is a requirement and not a tidiness rule.** `source_sha` is what §13 pins a quotable figure to and what `/export` publishes as dataset identity (BRD-70, BRD-134): a row with invented provenance makes an exported number **unreproducible by the person checking it**, which is the precise failure §1 names. On 2026-08-29 the parity re-run found **155 rows** across `Gate`/`Run`/`Session`/`Commit` carrying two `source_sha` values that do not exist in their repositories — both hand-typed sequential hex, seeded straight into the store, bypassing the sync path — and they had inflated one repository's gate count to 34 against 0 upstream. The gate caught it **only because the counts disagreed**. Had the fabricated rows been fewer, the numbers would have looked plausible and been wrong. Recorded as `REQ-NFR-019`.
- <a id="brd-144"></a>**BRD-144** — A built screen shall be graded against its approved mockup, mechanically. For every screen carrying a mockup in `docs/mockups/`, a gate shall compare the built page against that mockup at 1280 and 390 and shall FAIL on a structural difference — a control the mockup renders as a **badge or pill** rendered as plain text, a **missing icon or icon button**, a **semantic colour** that does not match (status green/amber/red, chart series), a **header or row that wraps** where the mockup is single-line, a **table column clipped** out of its container, or a **value cell narrower than its longest unbreakable token** (a formatted number shall never break mid-digit). The gate shall additionally assert that no route's document escapes the app-shell scroll container, shall run inside the verification phase alongside the render-truth and visual-truth gates, shall write its findings into the `gates` stream as `mockup-parity` so a screen cannot reach `Verified` on those two gates alone, and shall report a screen with no mockup as **`⚠ NO-MOCKUP`**, never as a silent pass. *(F-SHELL, F-OPS — applies to every screen in the §9 inventory)*

  **Why this is a requirement and not a preference.** On 2026-08-29 the owner compared all 18 mockups against the running app and found structural drift on **13 of the 14 comparable screens**, against a checklist that read 145 `Verified`. Every one of those screens had passed acceptance, the data-render gate and the visual-truth gate — because **no gate compared a built screen to its approved design**. Render-truth asks *does the control show data?*; visual-truth asks *do controls overlap or leave the viewport?* A badge rendered as plain text has text and does not overlap; a header that wraps to two rows does not overlap; a 71px value column that splits `2,287,975,139` across three lines does not overlap; a missing icon is nothing to measure. All of them pass, and all of them are wrong. The gate set measured **liveness, not fidelity** — the same shape as the asset-integrity gap found one day earlier (BRD-140's sibling, `REQ-NFR-015`). Recorded as `REQ-NFR-020`; raised upstream against the framework as `TF-008`.

### Amendment 2026-09-01 — phase effort and efficiency, from both frameworks

*Sources: `docs/Phase-Effort-Telemetry-TfLens.md` (TechieFlow — producer shipped 2026-08-31: `runs.jsonl` §2.6 plus the `--phases` oracle), `docs/Phase-Efficiency-TfLens-Contract.md` (AI-First-Playbook — schema-2 `phase-metric`, producer implemented) and `docs/Miss-Telemetry-TfLens-From-AIFP.md` (AI-First-Playbook — normalized miss export, producer implemented). A fourth source, `docs/Miss-Telemetry-TfLens.md`, was re-read during this amendment and required **no change**: every clause of it is already owned by BRD-112..BRD-130.*

*All Phase 3. New feature **F-EFFORT**. Nothing is renumbered and nothing is removed; BRD-5 / BRD-23 / BRD-29 / BRD-73 / BRD-108 / BRD-110 / BRD-126 are amended in place. Two owner decisions were taken at the confirmation gate and are recorded as ADR-023 and ADR-024.*

**The three producers agree on one rule, and it is the rule this whole amendment is built around.** TechieFlow says *an unmeasured thing is unmeasured, not zero, and not passed*; the Playbook says *never treat file absence, EOF, malformed input, or an unsupported harness as a zero-valued run*. It is the same sentence, and it is the seventh appearance of a rule TfLens already enforces under six other names — `PERF-UNMEASURED`, `MOCKUP-UNGRADEABLE`, `⚠ STATIC-ONLY`, `origin_confidence != "linked"`, `cost_attribution != "sole"`, `project_type_inferred`. Every requirement below is an instance of it.

**A — TechieFlow phase effort**
- <a id="brd-145"></a>**BRD-145** — System shall parse and store the three SCHEMA §2.6 fields added to `runs.jsonl` on 2026-08-31 — `subagent_runs` (`int?`), `tokens_out_subagents` (`int?`) and `model_tokens_out` (`Dictionary<string,long>?`) — as **nullable by design**, storing the raw rows and never collapsing them at ingest so that `RebuildAsync` re-derives every figure from the stream alone; and a `runs` record carrying a field TfLens does not know shall go to `Overflow` and **never** increment `InvalidLines` — the producer added fields in August (§2.5) and again now (§2.6) and will again. *(F-EFFORT, F-PARSE)*
- <a id="brd-146"></a>**BRD-146** — System shall exclude from every token figure any run whose token window could not be computed (`tokens_scope: "none"`, or no scope), count them as `tokens_unmeasured_n`, and **never average them in as zero**; every token tile shall carry **`measured on n of N runs`** as visible text, not a tooltip, and `tokens_out_per_run` shall be `null` — rendered *insufficient data (n=…)*, never `0` — below the `MIN_N = 3` floor. *(F-EFFORT, F-ENGINE)*

  **Why this clause names a specific defect.** This is precisely `TF-005`, the divergence TfLens itself reported against the framework on the miss stream: `or 0` cannot tell an absent field from a measured zero, and **the error always runs in the direction that flatters the framework**. The same mistake on the phase stream would report `*log-miss` costing half what it does on a repo where four of nine runs happen to be unmeasured — and the current data has exactly that shape.
- <a id="brd-147"></a>**BRD-147** — System shall restrict every fan-out figure to records with `tokens_scope == "tree"` **and** `subagent_runs != null`, and shall publish the exclusion **two ways because they are two different facts**: `unobserved_not_tree` (the window was `main` / `conversation` / `none` — *we did not look*) and `unobserved_predates_field` (tree-scope with **no `subagent_runs` value at all** — *we could not have looked*). Every fan-out band shall state **`observed_n of runs` first and the numbers second**, and shall render **"not observed"** where `observed_n == 0` — never `0 subagents`. *(F-EFFORT, F-ENGINE)*

  **Amended 2026-09-01 (build): `unobserved_predates_field` is a NULL-CHECK, not a date check.** As first
  written this clause said "written before the field existed", which reads as a comparison against
  `2026-08-31`. The shipped reference script does no such comparison — `analyse_phases` counts a run as
  predating the field when its scope is `tree` **and** `subagent_runs` is `None`, and nothing consults the
  run's timestamp. The two agree in practice for the reason BRD-148 gives: the field has been emitted on
  every tree-scope run since 2026-08-31, so a tree-scope run missing it must predate it. But `FIELD_SINCE`
  is **why the inference is sound, not how the count is made**, and TfLens matches the script because BRD §13
  parity is zero-tolerance. Recorded as `MISS-TfLens-20260901-07`.

  **Why this one is called out separately: it fails silently.** A `main`-scope window never read the subagent transcripts at all. Coercing its absent `subagent_runs` to `0` makes a phase report *"spawns no subagents"* when the truth is *"we did not look"*, and pooling the two produces a confident fan-out average largely composed of runs that could not have seen a subagent. Nothing about the resulting number looks wrong.
- <a id="brd-148"></a>**BRD-148** — System shall extend the `FIELD_SINCE` eligibility floor — already carrying `why_missed: 2026-08-28` beside `LATE_GATES`, **in the same table and the same code path** — with `subagent_runs`, `tokens_out_subagents` and `model_tokens_out`, each at `2026-08-31`, so a run written before a field existed leaves that field's denominator entirely and is reported separately rather than counted against it. *(F-EFFORT, F-ENGINE)*
- <a id="brd-149"></a>**BRD-149** — System shall display **both** the declared subagent list (`subagents`, typed by the agent — it knows which *kinds* were invoked) and the measured spawn count (`subagent_runs`, counted from the harness's own store — it knows how many *actually ran*), and shall state in words that the measured figure is authoritative wherever they disagree. The gap between them is a finding about how accurately tasks self-report, not an error to reconcile away. *(F-EFFORT)*
- <a id="brd-150"></a>**BRD-150** — System shall compute every per-model effort figure from `model_tokens_out` (the per-model **split**) **wherever a run carries one**, and shall never attribute a *split-carrying* run's whole window to its dominant `model`; a run carrying **no** split falls back to its dominant `model` label and shall be **counted apart**, so the weaker provenance is visible rather than blended into the stronger. The per-model band shall carry standing copy stating that the ranking is **observational, not causal** — which model gets the hard phases is not random. *(F-EFFORT, F-ENGINE)*

  **Amended 2026-09-01 (build): the fallback is required, not a loophole.** As first written this clause forbade the dominant label outright. The shipped reference script falls back to it when a run has no split (`analyse_phases`: `elif r.get("model")`), and **every record written before 2026-08-31 has no split** — so refusing the fallback would have failed BRD §13 parity across almost the whole corpus, and would have reported those runs as having no model at all rather than a less precisely attributed one. The defect the clause exists to prevent is untouched: a run that *does* carry a split is never filed whole under its winner. TfLens additionally publishes `PhaseModelEffort.RunsFromLabel` so a reader can see how much of a per-model figure rests on the label rather than the split — which the script does not expose and which is the honest addition. Recorded as `MISS-TfLens-20260901-08`.
- <a id="brd-151"></a>**BRD-151** — User can open **`/effort` — "Phase effort"**, a seventh report page and an eighth nav item between *Misses & rework* and *Snapshot export*: a **KPI row** (runs recorded · total wall clock · total output tokens with its measured-on sub-line · heaviest phase by output share · **fan-out coverage** as `Σ observed_n / runs_live`, a coverage figure deliberately on the KPI row rather than buried); a **phase table**, one row per `cmd` sorted by output-token share, in which **`Measured` is a column and not a footnote**; an expandable per-phase detail of four bands in order (Time · Tokens, showing mean **beside** median because a mean far above the median means one long run dominates the phase · By model · Fan-out, `observed_n` stated first, carrying the declared-vs-measured line whenever they disagree); and a **routing band** in which `drifted` is drift made visible and is never styled as a failure, because routing is observed and never enforced. *(F-EFFORT, F-SHELL)*
- <a id="brd-152"></a>**BRD-152** — Parity shall cover the oracle's `phases` block key-for-key (`phases.runs_live` · `phases.tokens_out_total` · `phases.duration_s_total` · `phases.scope_coverage.*` · and per `cmd`: `runs` · `duration_s.{total,median,max,n}` · `share_of_duration` · `tokens.{in,out,cache_read,cache_write}` · `tokens_measured_n` · `tokens_unmeasured_n` · `tokens_out_median` · `tokens_out_per_run` · `share_of_tokens_out` · `models.<model>.{runs,tokens_out}` · `fanout.` → `observed_n`, `unobserved_n`, `unobserved_not_tree`, `unobserved_predates_field`, `spawns_total`, `spawns_median`, `spawns_max`, `runs_with_fanout`, `tokens_out_subagents`, `subagent_share_of_tokens_out` · `routing.{routed,drifted,unknown}` · `cost_usd_by_harness.<harness>.{usd,records}`). The block rides inside `--report --json` / `--rollup --json`, so **no new oracle invocation is needed**; `share_of_*` values are the oracle's own `"87%"` / `"—"` strings and shall be diffed as strings, not reformatted first. *(F-EFFORT, F-PARITY)*

**B — Playbook phase efficiency (schema 2)**
- <a id="brd-153"></a>**BRD-153** — System shall ingest the Playbook's normalized schema-2 `phase-metric` NDJSON through the existing **Import metric files** mode (BRD-133) — the exporter reads a **transient** event file that rotates, so TfLens can neither fetch it on a schedule nor ask the Playbook to commit it, and its stdout is uploaded like any other bundle with the bundle sha256 as dataset identity (BRD-134). There shall be **no second ingest code path** (BRD-132). Rows shall upsert on `(UserId, Repo, PhaseExecutionId)` — re-import is expected, because the exporter emits every currently readable window — and every normalized row shall retain `source_schema`, `source_harness`, importer version, repository identity and import timestamp. **File absence, EOF, malformed input and an unsupported harness shall never be treated as a zero-valued run.** *(F-EFFORT, F-FRAMEWORK, F-REPOS)*
- <a id="brd-154"></a>**BRD-154** — System shall store schema-2 phase data in three tables — `PbPhaseExecution` (identity, phase, window, completion, `end_reason`, dominant model, tier, the five token components and compatibility totals, cost, turns, the three active-time columns and coverage, the data-quality columns, `tokens_scope`, spawned/contributor counts, attempt and verdict snapshots, `project_type`), `PbPhaseModelUsage` (per model: turns, five token components, totals, cost, `cost_status`, `active_ms`) and `PbPhaseSubagent` (child session, parent session, nullable agent type, lifecycle, turns, tokens, cost) — in the existing house style, with 64-bit integers, provider cost as **fixed-precision decimal and never a binary float**, and source nulls preserved. `DeleteRepoDataAsync` shall purge all three. *(F-EFFORT, F-PARSE)*
- <a id="brd-155"></a>**BRD-155** — System shall validate the producer's invariants on ingest (`tokens_in = input + cache_read + cache_write`; `tokens_out = output + reasoning`; `subagents.spawned >= contributors`; `0 <= observed_active_ms <= elapsed_ms` on a complete window; `complete:false` implies `end_reason:"eof"` with null end and elapsed) and shall **quarantine** from every numeric aggregate any row with `data_quality.valid:false`, a failed invariant, or no finalized assistant turn. A schema-2 window with no assistant turn is **invalid or incomplete, not a valid zero-usage run**, and the producer may retain zero-valued compatibility totals on an invalid row — those values shall never enter a figure. Quarantined rows stay visible with their reason; nothing is silently repaired. *(F-EFFORT, F-ENGINE)*
- <a id="brd-156"></a>**BRD-156** — System shall keep **wall-clock elapsed**, **observed active time** and **human effort** as three separate concepts and shall never substitute one for another. Observed active time is the producer's **union** of assistant and tool intervals with overlapping and nested work counted once; `assistant_elapsed_ms` and `tool_elapsed_ms` are diagnostic sums that legitimately overlap and shall **never be added**. Observed active time shall never be labelled human effort, CPU time, utilization or additive compute; `coverage:"partial"` renders as an explicit **lower bound**, `unavailable` as no figure at all. Human effort is not captured and shall never be inferred from wall-clock or agent time. *(F-EFFORT, F-ENGINE)*
- <a id="brd-157"></a>**BRD-157** — System shall label the phase dimension **Command phase**, because the producer's `phase` is the slash command and one command can contain several lifecycle stages (`/implement` covers build and self-review; `/verify` covers verify and the results gate), and shall **never split one command window between conceptual phases by token proportion**. A whole-task total requires a cohort supplied explicitly by ingestion (repository, checklist identity, and the exact phase execution IDs or time boundary); **a reused `session_id` is not sufficient** — one session may execute several tasks — so without an explicit cohort the page shall show phase rows and label the whole-task total **unavailable** rather than silently group unrelated work. *(F-EFFORT)*
- <a id="brd-158"></a>**BRD-158** — System shall compute mixed-model attribution by aggregating `PbPhaseModelUsage` and shall never assign a whole execution's tokens or cost to its dominant `model` for model-efficiency analysis; a model filter shall match **any** `models[]` member, not only the dominant one. *(F-EFFORT, F-ENGINE)*
- <a id="brd-159"></a>**BRD-159** — System shall show subagent fan-out as **`contributors / spawned`** (a spawned child that produced no tokens is still a spawned child), shall render a recursive grandchild beneath its parent while counting it in the phase total **exactly once**, shall **never sum child usage onto phase totals again** because it is already included, shall describe `spawned − contributors` as a *zero-token or non-contributing child* and **never as an inferred failure**, and shall display an absent child agent type as **unavailable** rather than inferring `"unknown"` from a title or a model name. Child token share shall be computed only where the denominator is positive. *(F-EFFORT)*
- <a id="brd-160"></a>**BRD-160** — System shall honour the producer's cost statuses: a `zero-unverified` provider cost (zero dollars reported against non-zero tokens) shall be excluded from every measured-cost aggregate and shall be shown with its status and the engine caveat — **never as "free" and never as a measured `$0`**; a phase whose models do not all report `cost_status:"complete"` is **partial**, not complete; and measured `cost_usd` shall never share a series, a total or an aggregate with a rate-card `_usd_estimate` figure, in the page, the API or the export. *(F-EFFORT, F-EXPORT, F-ROUTE)*
- <a id="brd-161"></a>**BRD-161** — System shall enforce the producer's aggregation cohorts and state each one's `n` and exclusions **beside the figure** rather than in a global footer: duration aggregates from `complete:true` rows only; active-time averages, percentiles, ratios and model comparisons from `complete:true` **and** `active_coverage:"complete"` only; token totals additionally requiring `data_quality.valid:true` and `token_status:"complete"`; measured-cost totals additionally requiring `cost_status:"complete"`. `legacy-unverified` schema-1 rows stay available for drill-down and are excluded from schema-2 comparisons. No status shall be presented as proof of best-effort event-delivery completeness. Any comparative cohort below three records renders `insufficient data (n=…)`. Storage and filtering shall be UTC; only display is localized. *(F-EFFORT, F-ENGINE)*
- <a id="brd-162"></a>**BRD-162** — User can open the **Phase efficiency** view — the Playbook axis of `/effort`, shipped **behind a repository capability check** — carrying: summary cards (completed phases · median and p90 wall clock · complete-window complete-coverage active time with `n of N eligible` beneath it · input/output/reasoning/cache tokens · measured provider cost, with rate-card estimates on a **separate card** labelled `estimate — tokens × rate card` · `contributors / spawned` · incomplete-window and partial-coverage data-quality cards); charts (stacked token trend by command phase · duration distribution excluding incomplete windows and showing the excluded count · active-vs-wall-clock for eligible executions only · model mix by token and turn share · subagent fan-out · cost and token trend with measured and estimated dollars separated); an execution table whose expandable rows show per-model usage and active time, the subagent tree by `session_id` / `parent_id` including zero-token children, and a data-quality explanation for each of EOF, missing active timestamps, unpaired tools, missing tier and the provider-cost caveat; filters (repository, date range, command phase, model, tier, harness, project type, complete/incomplete, active coverage, tokens scope, verdict snapshot, has-subagents); and the five empty and unsupported states. *(F-EFFORT, F-FRAMEWORK)*
- <a id="brd-163"></a>**BRD-163** — System shall render Claude Code phase effort as **"Phase effort telemetry unsupported for this harness"** — **never as zero and never as an empty measured figure** — until a Claude adapter emits the normalized schema; a harness with no normalized producer is a **data gap**, which is a different fact from a harness that ran and spent nothing. *(F-EFFORT, F-HARN, F-FRAMEWORK)*

**C — Playbook misses**
- <a id="brd-164"></a>**BRD-164** — System shall ingest the Playbook's normalized miss export (`miss` · `miss-fix` · `miss-amend`, produced from its committed `verification/telemetry/misses.ndjson`, with amendments folded and exact fix windows joined by the producer) into the **existing** `Miss` / `MissFix` / `MissAmend` tables; shall upsert raw source records by **immutable source-line identity/hash** preserving stream order, rather than on the TechieFlow natural keys; shall **re-fold** valid amendments at read time before computing anything, exactly as BRD-116 requires for the TechieFlow stream; and shall surface orphan and overwrite diagnostics on Coverage rather than applying or discarding them silently. *(F-MISS, F-FRAMEWORK, F-PARSE)*
- <a id="brd-165"></a>**BRD-165** — System shall preserve the two cross-edition axes as **distinct columns and distinct charts, never merged**: the Playbook's `item_id` beside TechieFlow's `req_id`, and the Playbook's **process** `found_phase_gate` beside TechieFlow's **assertion** `found_gate`. The first pair is one axis under two names and normalizes to two columns on one table; the second pair is two genuinely different measurements and shall never share a column or a chart — the same rule, and the same reason, as `phase_gate` versus `gate` (BRD-74). *(F-MISS, F-FRAMEWORK)*
- <a id="brd-166"></a>**BRD-166** — System shall apply the Playbook's reporting guards, which are **stricter than the TechieFlow stream's and shall not be relaxed to match it**: a model or tier attribution requires `origin_confidence:"linked"` **and** a complete valid source window **and** a non-null observed model; a headline fix token or cost figure requires `cost_attribution:"sole"` **and** a complete valid window **and** `data_quality.cost_status:"complete"`, with `shared:<n>` shown separately as apportioned and `none` excluded. An inferred or unknown origin shall **never** be placed in an *"unknown model"* performance bucket — a bucket named for a model is a claim about a model. *(F-MISS, F-ENGINE)*
- <a id="brd-167"></a>**BRD-167** — User can see, on the Playbook axis of `/misses`, real figures wherever the Playbook emits them: lifecycle **opened / closed / reopened / backlog** counts; miss rate by linked origin phase and model; miss class, `why_missed`, design-miss share and escape share; **rework incidence and intensity** and **median and p90 time-to-close**; sole measured repair tokens and cost beside separately-labelled apportioned repair cost; and the attribution exclusions, assessment denominators and amendment/orphan diagnostics that bound all of them. The `PlaybookEmpty` state (BRD-126) is what shows when no Playbook miss records have been imported — no longer the permanent condition of that axis. *(F-MISS, F-FRAMEWORK)*

**D — Cross-cutting integrity**
- <a id="brd-168"></a>**BRD-168** — **No actor-grouped reporting, anywhere.** No TfLens surface — page, API, export or parity — shall group any quality, miss, rework, effort, token, time or cost figure by `actor`. The Playbook's records carry the field (SCHEMA.md §11) and both AIFP contracts state the prohibition as a hard rule; TfLens honours it structurally, with no query parameter, filter or toggle that could produce such a grouping. *(F-EFFORT, F-MISS, F-ENGINE, F-EXPORT — the third NFR sibling of BRD-89 and BRD-130)*

  **Why a producer would forbid a grouping it is perfectly able to emit.** Per-actor development metrics are a measurement whose *existence* changes the thing measured, and the frameworks emit `actor` for provenance — knowing whose machine a record came from — not for comparison. A dashboard that can rank people will be read as ranking people whatever its caption says, and TfLens's whole premise is that a number which cannot be defended should not be renderable.
- <a id="brd-169"></a>**BRD-169** — Integrity: the phase-effort and phase-efficiency invariants shall have **no configuration switch, no query parameter and no UI toggle** that relaxes them — no `0` rendered where the answer is *not measured*; measured and unobserved never pooled, and every exclusion count displayed beside its figure; no estimated dollars on any effort tile, and no dollars ever priced from a rate card and presented as spend; **no per-REQ or per-feature effort view** (a run's window divided across the REQs it touched is arithmetic dressed as measurement — both producers state this as a standing non-goal and neither emits a per-REQ timing field); no per-subagent cost attribution, which the transcripts do not carry; and no *"phase X costs more than phase Y, therefore X is inefficient"* framing — `*build-phase` costing more than `*log-miss` is a fact about what those phases **are**. Effort per phase is a **budgeting and capacity** view; quality lives on `/misses` and `/coverage`. *(F-EFFORT — the NFR sibling of BRD-89, BRD-130 and BRD-168)*

## 12. Constraints & assumptions

- Blazor Server on the current LTS .NET (10); **PostgreSQL 16** (owner decision 2026-08-26 — SQLite is unreliable on container storage); Dapper via Npgsql; TrBlazeUI where it fits (dogfood). Docker Compose on a VPS — infra config supplied separately.
- Timebox 1–2 days; phase order is hard (1 → 2 → 3). Anything cut for time is recorded in DECISIONS.md.
- Schema v=1 as documented in `.tfcore/telemetry/SCHEMA.md` at 2026-08-26, **plus §5.5 (`misses.jsonl`, three record kinds) added 2026-08-28**; `tf-metrics.sh` at the matching date is the reference. A reference change invalidates the last parity stamp (the script hash is recorded).
- Repos are connected only through the Repos screen; the demo repos are connected to `TfLensDemo` by hand during development.
- Playbook report set (F-FRAMEWORK) is Phase 3, after the TechieFlow set ships and passes parity (owner decision 2026-08-26). Miss telemetry (F-MISS) is Phase 3 as well, after the existing Playbook items (owner decision 2026-08-28).
- `origin_model`, `origin_harness`, `origin_confidence` and `cost_attribution` are **emitter-derived, never agent-written** (SCHEMA.md §5.5), and `tf-emit.sh` forces the model/harness to `null` whenever the lookup fails. A non-`linked` record therefore cannot carry a model name at all — BRD-121 filters on a value the producer controls, not on an agent's self-assessment.
- `project_type` can now be `framework` (detected structurally by the producer, written nowhere), and a repo can legitimately span two segments: every greenfield repo is born `docs` and is upgraded to `app` on refresh, while already-written records keep the old value because streams are append-only and corrections happen at read time. TfLens caused this case and must state the split (BRD-127) rather than silently render one project as two.
- No Playbook `events.ndjson` sample exists at day-1; Phase 3 starts with schema discovery. **Superseded in part 2026-09-01:** the Playbook now publishes two normalized contracts (schema-2 `phase-metric` and the miss export), so those two record types are specified rather than discovered; discovery remains only for anything the exporter does not normalize.
- **Schema v=1 plus SCHEMA §2.6** (2026-08-31): `runs.jsonl` gained `subagent_runs`, `tokens_out_subagents` and `model_tokens_out`, and `tf-metrics.sh` gained a `--phases` mode whose block also rides inside `--report --json` / `--rollup --json`. The producer change is **additive and backward-compatible** — old records simply lack the three fields and the oracle reports them as `unobserved_predates_field` rather than as zeros; no existing key changed, no backfill was performed, and the streams stayed append-only. **The existing §13 parity gate therefore keeps passing unchanged**, and the new keys join it when `/effort` ships. A reconstructed `subagent_runs` would be a guess, which is why none was written.
- **Fan-out coverage will be thin for weeks.** Only runs recorded after 2026-08-31, under a harness whose window resolves to `tree` scope, carry the fan-out fields. On the framework's own current data that is **1 of 13 runs**. `/effort` must look correct at `observed_n = 1 of 13`, because that is what it will show first — and a page that only looks right once the data is dense is a page nobody trusts in the meantime.
- **The Playbook's phase input is transient by design.** `verification/telemetry/events.ndjson` is rotated by the framework; the exporter is expected to be re-run and its output checkpointed before rotation. TfLens consequently sees an inherently gappy series and shows the last successful checkpoint rather than implying continuity. Event writes are **best-effort**, and no status the producer emits — `token_status`, `cost_status`, `coverage` — is evidence of end-to-end delivery completeness; TfLens reports ingestion and invariant diagnostics and never silently repairs a gap.
- **The Playbook's miss guards are stricter than TechieFlow's, deliberately** (BRD-166). It requires a complete valid source window and a non-null observed model on top of `origin_confidence:"linked"`, and `cost_status:"complete"` on top of `cost_attribution:"sole"`. A future reviewer will notice the asymmetry and try to unify the two; the stricter guard is not an accident and unifying downward would weaken a claim the producer refuses to make.
- **`actor` exists in the Playbook stream and is never a grouping key** (BRD-168). Both AIFP contracts state the prohibition explicitly. It is retained for provenance, not comparison.
- **No canonical cross-phase task identity exists on either side.** A whole-task figure requires a cohort the ingestion job supplies explicitly; a reused `session_id` may span several tasks and is never a valid substitute (BRD-157).
- A0′ ("logging live, three runs") is satisfied by the frameworks' existing emission, not by TfLens; the only machine-side task is running `update-framework.sh` on each clone so the per-clone hooks exist. TfLens can trail A0′ without blocking it.
- Multi-user (amended 2026-08-26) but single process; the memoised analysis lives in process memory keyed by user; no horizontal scaling.
- Identity is AppManager (App Id 1, API v1.4). AppManager has no SSO endpoint today — GitHub SSO (BRD-94) is deferred to Phase 2.
- Public GitHub repos only **for fetching** in this release; unauthenticated GitHub API limits (60 req/h per IP) apply unless the optional server PAT is set. Private and corporate repos are reached by **importing metric files** instead (BRD-131..BRD-141) — no credential, no network route to the repo, no change to the repo.
- An imported bundle is user-supplied and could in principle be edited before upload. TfLens does not attempt to detect that; it makes origin visible on every surface instead (BRD-136). Detecting tampering would require a signature the frameworks do not produce, and asking them to produce one is out of scope (§1).

## 13. Parity check — the mandatory acceptance test

**Principle:** two independent implementations compute the same metrics from the same files — `tf-metrics.sh` (existing, trusted; SCHEMA.md §6 enforced in its code) and TfLens (new, unproven). Correct implementations must agree exactly. Any disagreement is, by definition, a bug in TfLens. The script is never "fixed" to match the app.

**Why this test exists:** the dangerous failure mode is not a crash — it is a *plausible wrong number*. A pooling bug produces a figure that looks normal, gets exported, and ends up quoted publicly in B3. Once published it cannot be defended. The parity diff is the only cheap way to catch that class of bug.

**Procedure** (run before TfLens's export is used for any weekly Numbers row or any post, and re-run after every parser or engine change):

1. Pick a fixed dataset. For a **fetched** source: clone the repo at the exact commit SHA TfLens's `sync_state` shows for its last sync (also printed in the export's `per_repo`). For an **imported** source (amended 2026-08-28): use the archived bundle itself, identified by its **sha256** — run the reference over `data/raw/<userId>/<source>/` directly. Same data in, or the comparison is meaningless; the imported case is the stronger of the two, because the operator compares the identical bytes instead of re-cloning and trusting the result matched.
2. Run the reference: `bash .tfcore/telemetry/tf-metrics.sh --rollup <repo1> <repo2> ... --json > reference.json`.
3. Run TfLens's export for the same repos: `dotnet TfLens.dll export` → `data/reports/<date>/tflens.json`.
4. Compare, key by key: `python3 tools/parity-compare.py reference.json tflens.json` — it checks per-repo record counts per stream and backfilled counts; commit duplicates collapsed; the tainted-REQ set (identical set of IDs); first-pass rate, gate catch distribution, escape rate per project_type, live and backfilled separately; late-gate coverage (`ran` / `caught` per gate); every poolable metric; every `insufficient data (n=…)` marker — the n must match, and a figure the reference refuses to print TfLens must also refuse to print. It also covers the `misses` block (BRD-129) and, from 2026-09-01, the **`phases`** block (BRD-152) — the latter rides inside `--rollup --json` already, so step 2 needs no new invocation. `share_of_*` and `subagent_share_of_tokens_out` come back as the oracle's own `"87%"` / `"—"` strings and are **diffed as strings**; `tokens_out_per_run`, `duration_s.median`, `spawns_median` and `spawns_max` come back as a real `null` below the `MIN_N` floor, and TfLens must return `null` there too — a `0` on either side is a mismatch, not a rounding difference.
5. **Zero tolerance:** any mismatch fails. Debug TfLens until the diff is empty. The only acceptable permanent differences are metrics TfLens adds that the script does not compute (`extras`) — those have no reference and are spot-checked by hand against raw JSONL once.
6. Record the passing run in DECISIONS.md and `data/parity-last.json`: date, commit SHAs of the dataset, `tf-metrics.sh` hash, TfLens parser version, and the compare script's output. That entry is the licence to trust the export.

**Standing rule after ship:** the weekly snapshot export is only quotable if the last parity run on record postdates the last parser change. The `/export` page shows this as the quotable / not-quotable banner (BRD-67).

**Second standing rule (added 2026-08-29, BRD-143):** a passing diff is not on its own a licence to quote, because the diff only compares TfLens against the reference **over whatever rows are in the store**. Provenance the store never obtained is invisible to it until the counts happen to disagree with upstream — which is how 155 fabricated rows survived until 2026-08-29. The export is therefore quotable only when the parity run passes **and** no row carries a `source_sha` that no `SyncState` row or import bundle accounts for; `/export` refuses `QUOTABLE` on either condition.

**Third standing rule (added 2026-09-01, BRD-152, BRD-163).** The Playbook's phase and miss figures have **no oracle at all** — `tf-metrics.sh` reads TechieFlow streams and knows nothing about schema-2 `phase-metric` rows. They therefore stand where `extras` stands (step 5): spot-checked by hand against the raw NDJSON once, recorded in DECISIONS.md, and **never quoted on the strength of a passing TechieFlow diff**, which says nothing about them. The TechieFlow `phases` block is the opposite case — it has a first-class oracle, so every figure on the TechieFlow axis of `/effort` ships **unverified until BRD-152's compare is green**. A page whose two halves have different evidentiary standing must say so on its face rather than let a reader assume the stronger one.

## 14. Definition of done

- [ ] All configured repos syncing; Coverage page green with real staleness numbers
- [ ] A private/corporate repo's telemetry reaches the reports through **Import metric files**, with the source shown as `Imported` on `/repos` and Coverage, and its bundle sha256 usable to pin a parity run (F-IMPORT, Phase 3)
- [ ] Three-questions page renders per project_type with live/backfilled separation and the taint-exclusion list visible
- [ ] Harness comparison page shows claude-code vs opencode side by side, with OpenCode-only dollars
- [ ] Counterfactual repricing figure renders from `prices.json`, labelled estimate
- [ ] Weekly snapshot export produces markdown + JSON
- [ ] `/misses` renders all four bands with the taint count, the `n of N assessed` denominator and the three-column cost split visible (F-MISS, Phase 3)
- [ ] The `misses` parity block diffs clean against `tf-metrics.sh --rollup --json` (BRD-129)
- [ ] Parity check (§13) passed with an empty diff, recorded in DECISIONS.md
- [ ] No row in any stream table carries a `source_sha` that no sync or import accounts for, and `/export` refuses `QUOTABLE` while one is present (BRD-143)
- [ ] Every screen with a mockup in `docs/mockups/` passes the `mockup-parity` gate at 1280 and 390; screens without one report `⚠ NO-MOCKUP` (BRD-144)
- [ ] `/effort` renders the TechieFlow axis with **every denominator visible beside its figure** — `measured on n of N runs` on each token tile, `Measured` as a table column, and fan-out stated as `observed_n of runs` (correct at `1 of 13`, not only once the data is dense) (F-EFFORT, Phase 3)
- [ ] The `phases` parity block diffs clean against `tf-metrics.sh --rollup --json`, with `null` matching `null` below `MIN_N` and `share_of_*` compared as strings (BRD-152)
- [ ] `/effort` renders the Playbook axis from an imported schema-2 bundle: `contributors / spawned` shown as a pair, an EOF window showing no elapsed value, a partial-coverage execution excluded from comparisons but present in the table, and a mixed-model execution contributing each model's own tokens (BRD-162)
- [ ] A Claude Code repo renders **unsupported** on the Playbook phase surface, never zero (BRD-163); a `zero-unverified` provider cost never appears as `$0` or "free" (BRD-160)
- [ ] `/misses` renders real Playbook figures from an imported miss bundle, with `item_id` / `req_id` and `found_phase_gate` / `found_gate` in **distinct** columns (BRD-165, BRD-167)
- [ ] No surface — page, API, export or parity — can group any figure by `actor` (BRD-168)
- [ ] DECISIONS.md records: storage choice, dedupe keys, anything cut for the timebox
- [ ] Finish report delivered: any field observed in real files that SCHEMA.md doesn't document; any place TfLens disagrees with `tf-metrics.sh --rollup` on the same data (must be none); what breaks first when schema v=2 appears

## 15. Success metrics

- Parity diff empty on the first real dataset within the timebox; re-run green after every parser change.
- Coverage page identifies at least one real staleness/hook gap on the live repos (the page proves its worth by finding the gap the files hide).
- Weekly snapshot used for the plan's Numbers table from the first week after ship, with no provenance mix reported in review.
- B1 harness page and B3 repricing figure sourced directly from the export, with the *estimate* label carried into the posts.
- TfLens's own `docs/metrics/` streams show the full TechieFlow phase sequence with gates enforced (A-V evidence).

## 16. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Plausible wrong number reaches a public post | Medium | High | Provenance rules in the result type (no flag); mandatory parity diff; quotable banner tied to parser version |
| Schema v=2 renames a known field and it silently drops out of a metric | Medium | Medium | Overflow report + `v > 1` warning on Coverage; "what breaks first" section in the finish report |
| Reference script changes after a parity run | Medium | Medium | Script hash recorded in the parity entry; banner shows not-quotable until re-run |
| PAT expiry / rate limit | Low | Low | 401/403 surfaced per repo on Coverage; poll interval 15 min keeps calls far below limits |
| Playbook file shape differs from the brief's description | High | Low | Schema-discovery first; adapter isolated in its own tables/page |
| Timebox pressure erodes Phase 2 pages | Medium | Medium | Phase order hard; cuts recorded in DECISIONS.md; Coverage + Gate outcomes + Export are the minimum |
| TrBlazeUI lacks a control a screen needs (no KPI card, no table primitives) | Medium | Low | Compose from `Card` + Tailwind per the library's documented KPI pattern; log gaps to `docs/TfLens-TrBlazeUI-Feedback.md` |
| AppManager outage blocks all sign-ins | Low | High | Sessions survive on the server-side refresh token until expiry; clear "identity service unavailable" message; no local fallback by design |
| Cross-user data leak through a missed `UserId` filter | Low | High | `UserId` is a mandatory parameter of every store read; integration test signs in two users and asserts isolation |
| A malicious upload escapes the archive directory or exhausts the disk | Low | High | Extension allow-list, 25 MB cap before read, entry-count and uncompressed-size limits, no absolute/`..`/symlink entries, extraction confined to `data/raw/<userId>/`, nothing executed or rendered (BRD-139) |
| An imported bundle was hand-edited before upload | Low | Medium | Not detectable without a signature the frameworks do not produce; origin is displayed on every surface instead (BRD-136) so a reader always knows which figures rest on imported data |
| A user uploads a rollup or snapshot instead of raw streams | Medium | Medium | Refused explicitly with a message naming what to upload (BRD-140); the preview (BRD-138) shows what was actually recognised before anything commits |
| Unauthenticated GitHub rate limit (60/h) with many users | Medium | Medium | Optional server PAT (5,000/h); SHA-skip keeps steady-state to 1 call per repo per poll |
| An apportioned rework cost is quoted as a measured one | Medium | High | `MissCost` has no blended property to bind (BRD-122); the export keeps three distinct keys (BRD-128); parity diffs `cost_sole_n` / `cost_shared_n` / `cost_unattributable_n` separately |
| A per-model miss figure built on guessed attributions drives a bad routing decision | Medium | High | `MissAttributionTaint` filters to `linked` only and the excluded count is on the page (BRD-121); the band carries standing observational copy (BRD-124) |
| The `why_missed` distribution is rendered over all misses and understates every category | Medium | Medium | Denominator is records carrying the field, printed as `n of N assessed` on its face (BRD-119); the eligibility floor excludes pre-2026-08-28 records explicitly (BRD-117); parity checks `why_missed_n` |
| One project appears as two segments after a `docs` → `app` reclassification, with no visible reason | High | Medium | Coverage states the split in words and describes each segment as a period of the project (BRD-127) — TfLens caused this case and hits it first |
| A `main`-scope run's absent `subagent_runs` is coerced to `0` and a phase reports "no subagents" when the truth is "we did not look" | **High** | High | Fan-out restricted to `tree` scope with `subagent_runs != null`; the exclusion split published two ways; `observed_n of runs` stated **first** and "not observed" rendered where it is zero (BRD-147); parity diffs `unobserved_not_tree` and `unobserved_predates_field` separately |
| An unmeasured token window is averaged in as zero, halving a phase's apparent cost | High | High | Unmeasured runs excluded and counted as `tokens_unmeasured_n`; `measured on n of N runs` on every tile; `Measured` is a table column (BRD-146). This is `TF-005` arriving on a third stream — the defect is known and the error always flatters the framework |
| `/effort` is read as a quality scoreboard and a phase is "optimised" for costing more than another | Medium | Medium | No efficiency framing anywhere on the page; standing copy states it is a budgeting and capacity view; quality stays on `/misses` and `/coverage` (BRD-169) |
| A per-model effort ranking built on the dominant `model` misattributes a mixed-model window and drives a bad routing decision | Medium | High | Per-model figures computed from `model_tokens_out` / `PbPhaseModelUsage` only, never from the dominant label (BRD-150, BRD-158); the band carries standing **observational** copy — which model gets the hard phases is not random |
| A Claude Code repo shows zero phase effort and is read as having spent nothing | Medium | High | Rendered as **unsupported**, never zero — a harness with no normalized producer is a data gap, not a measurement (BRD-163) |
| A `zero-unverified` provider cost is read as "this phase was free" | Medium | Medium | Excluded from measured-cost aggregates and shown with its status and the engine caveat; never `$0`, never "free" (BRD-160) |
| A whole-task total is assembled from a reused `session_id` and silently groups unrelated work | Medium | High | A task cohort requires an explicit ingestion boundary; without one the total renders **unavailable** rather than being inferred (BRD-157) |
| The Playbook's transient event file rotates before a checkpoint and a gap is read as a quiet period | Medium | Medium | Last successful checkpoint shown; absence, EOF and malformed input never become zero-valued runs; ingestion and invariant diagnostics reported rather than repaired (BRD-153, BRD-155) |
| A per-actor figure is added later "just for the team view" | Low | High | Prohibited structurally with no filter or parameter that could produce one (BRD-168); both producer contracts state the rule |

## 17. Glossary

- **The three questions** *(schema concept)* — the canonical questions `.tfcore/telemetry/SCHEMA.md` §0 declares the telemetry exists to answer: **first-pass rate**, **gate catch distribution** and **escape rate**, all three from `gates.jsonl`. They are rendered on the **Gate outcomes** screen (`/gate-outcomes`), which was called *Three questions* until 2026-09-01 — the **schema keeps the phrase**, the screen does not. A **fourth** question (miss attribution and rework cost, added 2026-08-28) is answered by `misses.jsonl` on its own screen, `/misses`, and never renamed or redefined the first three. See [§9](#what-gate-outcomes-shows-and-why-it-is-no-longer-called-three-questions).
- **Escape rate vs escape share** — *escape rate* is question 3, from `gates.jsonl` (`gate: "escaped"`), shown on `/gate-outcomes`. *Escape share* is a miss-stream figure (`found_by ∈ {owner, production}` ÷ all misses), shown on `/misses`. Adjacent, never merged.
- **Stream** — one of the five append-only JSONL files under `docs/metrics/` (`runs`, `gates`, `sessions`, `commits`, `misses`).
- **Source** — a thing TfLens reads telemetry from: a **fetched** public GitHub repo (`SourceKind: api`) or an **imported** bundle of metric files (`SourceKind: import`). Both produce identical rows; only the delivery differs.
- **Bundle sha256** — the content fingerprint of an uploaded bundle; the imported source's dataset identity, standing where a fetched source uses its commit SHA.
- **Live / backfilled** — provenance; backfilled records were reconstructed after the fact and carry `backfilled: true`.
- **Taint** — a REQ that has any backfilled record; excluded from the live first-pass rate.
- **project_type** — `app` | `library` | `docs` | `framework`; `unclassified` when `project_type_inferred: true`.
- **Harness** — `claude-code` | `opencode` | `codex` | `null`; detected by `tf-emit.sh`, never declared.
- **Late gate** — a gate added after the stream started (`perf`, 2026-08-10); reported against `gates_run` coverage.
- **Poolable** — a metric that may be summed across provenances and project types (runs, commits, tokens, cadence).
- **Repricing (estimate)** — tokens × rate card from `prices.json`; never a measurement.
- **Parity** — exact agreement between `tf-metrics.sh --rollup --json` and `tflens.json` on the same dataset.
- **Raw archive** — `data/raw/<repo>/<stream>-<sha>.jsonl`; the rebuild source.
- **REQ-UI-\* / REQ-FN-\* / REQ-RAG-\* / REQ-NFR-\*** — checklist requirement IDs produced by `*split-brd`.
- **TrBlazeUI** — the Blazor component library dogfooded by the UI; **TechieRag** — not used here.
- **Framework** — the provenance axis TechieFlow | Playbook; figures never pool across it.
- **phase_gate** — the Playbook's process-gate axis (plan review · verify · gap report · post-verification bugs), distinct from TechieFlow's assertion `gate`.
- **codex** — the Codex CLI harness value detected by `tf-emit.sh`.
- **AppManager** — the owner's identity/licensing service (`appmgrapi.techierathore.com`); TfLens is Application 1 and uses only AuthSvc + UserSvc.
- **Manager** — the AppManager application role every TfLens user receives.
- **TfLensDemo** — the demo account used for testing and first-visit demos.
- **miss** — a record of what was missed: which REQ, which class of defect, which phase/agent/model let it through, who found it. One defect is one miss however many times it fails (the producer's collapse rule).
- **miss-fix** — the repair record: the fix run, its verdict, and its token/cost window.
- **miss-amend** — an append-only record that *completes* a `miss` field left `null`; it may fill a `null` and may **never** overwrite a value, including one an earlier amend set. Folded at read time, never at ingest.
- **why_missed** — which *practice* failed (`missing-checklist-item` · `insufficient-verify-method` · `code-audit-limitation` · `ambiguous-acceptance` · `dependency-not-declared` · `instruction-ignored` · `other`), as distinct from `miss_class`, which says *what* was missed. Optional; `null` means **not assessed**, never zero.
- **origin_confidence** — `linked` (the origin run resolved to a real `runs.jsonl` record) · `inferred` · `unknown`. Emitter-derived; only `linked` records reach a per-model, per-agent or per-phase figure.
- **cost_attribution** — `sole` (the fix run touched exactly this REQ — a measurement) · `shared:n` (one token window over n REQs — apportioned, never headline) · `none` (no distinct fix run; counted, never divided).
- **Attribution taint** — the miss-stream sibling of backfill taint: records with `origin_confidence != "linked"` are excluded from every per-origin figure, and the excluded count is displayed.
- **Escape share (misses)** — `found_by ∈ {owner, production}` ÷ all misses. A second, adjacent figure to the `gates`-derived **escape rate**, never merged with it.
- **Command phase** — the measured slash command (`build-phase`, `/implement`, `/verify`, …), which may contain several conceptual lifecycle stages. The dimension `/effort` groups by. Distinct from a **conceptual phase**, which the producers cannot separate and TfLens never manufactures by splitting a window.
- **`subagent_runs`** — the count of subagents that actually ran, **counted from the harness's own store**. Distinct from **`subagents`**, the list of agent *kinds* an agent types into its own emit. Where they disagree the measured one is right; both are shown, and the gap is a finding.
- **`tokens_scope`** — how much of the session a run's token window covered: `tree` (main thread **and** subagent transcripts — the only scope from which a fan-out figure may be computed) · `main` · `conversation` · `none`. Never a quality signal; always a bound.
- **Observed (fan-out)** — a run with `tokens_scope == "tree"` **and** a non-null `subagent_runs`. `observed_n` is the denominator of every fan-out figure and is stated before the figure. Its complement splits two ways: **`unobserved_not_tree`** (*we did not look*) and **`unobserved_predates_field`** (*we could not have looked* — written before 2026-08-31).
- **`measured on n of N`** — the standing sub-line on every token tile: `n` is `tokens_measured_n`, `N` the phase's run count. An unmeasured window is excluded, never averaged in as zero.
- **Wall-clock elapsed** — how long a command window stayed open. **Observed active time** — the union of assistant and tool intervals across main and child sessions, overlaps counted once; a lower bound when coverage is `partial`. **Human effort** — time a person spent; not captured by either framework and never inferred from the other two.
- **`phase_execution_id`** — the Playbook's stable per-command identity; the upsert key for a phase row. There is deliberately **no** trustworthy cross-command `task_execution_id`, so a whole-task total needs an explicitly supplied cohort.
- **`contributors / spawned`** — children that produced tokens over children that were launched. The difference is a *zero-token or non-contributing child*, never an inferred failure.
- **`zero-unverified`** — a provider cost of zero reported against non-zero tokens. Excluded from measured-cost aggregates; never rendered as `$0` or "free".
- **Quarantine** — a schema-2 row with `data_quality.valid:false`, a failed invariant, or no finalized assistant turn: kept and shown with its reason, excluded from every numeric aggregate, never silently repaired.
- **`item_id` / `req_id`** — the Playbook's and TechieFlow's names for the requirement axis; normalized to two columns, never merged into one. **`found_phase_gate` / `found_gate`** — the Playbook's **process** gate and TechieFlow's **assertion** gate; two different measurements that never share a column or a chart.
- **`actor`** — a provenance field the Playbook stream carries. Never a grouping key on any TfLens surface (BRD-168).

---
Last updated: 2026-09-01
Last amended: 2026-09-01 — **phase effort and efficiency, from both frameworks (BRD-145..BRD-169)**. New feature **F-EFFORT** (Phase 3) and a seventh report page, `/effort`, fed by three producer contracts that all shipped ahead of this consumer: TechieFlow's `runs.jsonl` §2.6 fields (`subagent_runs`, `tokens_out_subagents`, `model_tokens_out`, 2026-08-31) plus the `--phases` oracle block; the Playbook's schema-2 `phase-metric` record; and the Playbook's normalized miss export. The whole amendment turns on **three denominators that must be on screen beside their figures** — `measured on n of N runs` for tokens, `observed_n of runs` for fan-out (with the exclusion split two ways: *we did not look* vs *we could not have looked*), and `complete` / `active_coverage` / `data_quality.valid` for Playbook durations — because coercing any of them to `0` is `TF-005` arriving on a third stream, and the error always runs in the direction that flatters the framework. Also: wall-clock, observed active time and human effort kept three separate concepts with diagnostic sums never added; **Command phase** labelling with no conceptual-phase allocation and no task cohort inferred from a reused `session_id`; per-model effort from the split, never the dominant label; `contributors / spawned` with recursive children counted exactly once; `zero-unverified` cost never shown as free; Claude Code rendered **unsupported**, never zero; the Playbook's stricter miss guards preserved rather than unified downward; `item_id`/`req_id` and `found_phase_gate`/`found_gate` kept as distinct axes; and two new prohibitions with no switch — **no per-REQ effort view** (BRD-169) and **no actor-grouped reporting anywhere** (BRD-168). Two owner decisions taken at the confirmation gate: the Playbook's transient phase output arrives through the existing **Import metric files** mode with no second ingest path (ADR-023), and Playbook misses **reuse** the three existing miss tables with their axes as distinct nullable columns (ADR-024). BRD-5 / BRD-23 / BRD-29 / BRD-73 / BRD-108 / BRD-110 / BRD-126 amended in place (eight nav items · three more tables · §2.6 fields preserved verbatim · `events.ndjson` is transient and best-effort · the switch spans seven pages · the Playbook is no longer schema-discovery-first · `/misses` Playbook axis is fillable). §13 gains a third standing rule — the Playbook's figures have **no oracle**, so a passing TechieFlow diff says nothing about them. A fourth source, `docs/Miss-Telemetry-TfLens.md`, was re-read and required **no change**: BRD-112..BRD-130 already own every clause of it. Nothing renumbered, nothing removed. Prior: 2026-08-29 — **two gates the product was relying on and had never written down (BRD-143, BRD-144)**. `BRD-143`: stored provenance must be real — every stream row's `source_sha` must be one a sync or import actually recorded, a fixture harness must not be able to write into the application's own store, an orphan-SHA check must make pollution detectable without a network call, and `/export` must refuse `QUOTABLE` while such a row is present. `BRD-144`: a built screen must be graded against its approved mockup mechanically — badge-vs-plain-text, missing icons, wrong semantic colour, unintended wrapping, clipped columns and value cells narrower than their longest unbreakable token all FAIL at 1280 and 390, findings land in the `gates` stream as `mockup-parity`, and a screen with no mockup reports `⚠ NO-MOCKUP` rather than passing silently. Both were appended after the fact: `REQ-NFR-019` and `REQ-NFR-020` had been logged as findings on 2026-08-29 with no owning BRD requirement, and are now owned rather than inferred. §13 gains a second standing rule (a passing diff alone is not a licence to quote), §14 gains two done-criteria, F-PARITY records that §13 currently FAILS, and F-OPS reopens to Partial. Nothing renumbered, nothing removed. Prior: 2026-08-28 (round 3) — **the test accounts are part of the product (BRD-142)**: the AppManager accounts the suite signs in with must be provisioned, documented and restorable from inside the repository, with a guardrail test binding the suite to the UsageGuide table and password-mutating tests required to restore or isolate. Raised by an owner report after the accounts were deleted server-side and took seven tests and every authenticated screen down at once (`MISS-TfLens-20260828-02` → `REQ-NFR-012`). Round 2 (same day) — **imported telemetry (BRD-131..BRD-141)**: the Add-source dialog on `/repos` gains a mode fork — **Fetch via API** (public repos) or **Import metric files** — so **private and corporate repositories are reachable without TfLens holding any credential**; a `SourceKind` column gives the grid a visible `Synced` / `Imported` demarcation; the uploaded bundle's sha256 stands where a commit SHA does, including for parity pinning; origin is displayed everywhere and pools nowhere; imported sources get **Re-import** rather than Sync and are skipped by the poller; the upload surface is bounded (BRD-139) and a precomputed rollup is refused (BRD-140). Two out-of-scope lines were amended rather than quietly broken. Owner decision the same day: folded into the Repos screen, not a separate `/import` route. Round 1 (same day): miss telemetry and rework economics: new feature **F-MISS** (Phase 3) and BRD-112..BRD-130 — a fifth stream `misses.jsonl` with three record kinds on one file, three tables with a full purge, read-time amendment folding, the `why_missed` denominator and its eligibility floor, the two deliberately-disagreeing open predicates, `MissAttributionTaint` (`linked` only, count displayed), `MissCost` as a three-way shape so a blended number is unrepresentable, the sixth report page `/misses`, the Coverage data-quality facts (`escapes_missing_why`, the reclassification split, orphans) and full parity coverage of the producer's `misses` block. BRD-5 / BRD-23 / BRD-108 amended in place (seven nav items · three more tables · the switch spans six report pages); nothing renumbered, nothing removed. Source: `docs/Miss-Telemetry-TfLens.md`. Prior: 2026-08-26 (round 2) — both frameworks get the full report set via a Framework switch (F-FRAMEWORK replaces F-PB, Phase 3); harness columns claude-code/opencode/codex with a null footnote; PostgreSQL replaces SQLite (Dapper stays); F-CFG retired into F-OPS (BRD-7 retired); mockup links added to every screens table + a Screen inventory. Round 1 (same day): AppManager identity (F-AUTH), per-user repo management (F-REPOS), shell rework; BRD-3 retired; GitHub SSO (BRD-94) deferred to Phase 2
Highest BRD ID: BRD-169
Sources harvested: docs/TfLens-Project-Brief.md (v2, superseded → docs/OldDocs/), .tfcore/telemetry/SCHEMA.md (incl. §5.5, 2026-08-28; §2.6, 2026-08-31), .tfcore/telemetry/tf-metrics.sh (incl. `--phases`), docs/ravi-90day-positioning-plan-v2.4.2.md (context only), docs/Miss-Telemetry-TfLens.md + docs/Miss-Telemetry-TechieFlow.md (2026-08-28 amendment; the former re-read 2026-09-01 with no change required), docs/Phase-Effort-Telemetry-TfLens.md + docs/Phase-Efficiency-TfLens-Contract.md + docs/Miss-Telemetry-TfLens-From-AIFP.md (2026-09-01 amendment)
Custom instructions applied: Dapper + PostgreSQL (owner, superseding SQLite); repos managed only in the UI; Phase 3 as schema-discovery (no events.ndjson sample); split-brd deferred until after review
First-pass draft from concept — review and edit. New BRDs may be added (append-only); do not renumber existing IDs.

**Last amended: 2026-09-01** — BRD-147 and BRD-150 corrected against the shipped `tf-metrics.sh` reference script after the F-EFFORT build's parity run (401 figures, 0 diffs) showed the documents describing behaviour the producer does not have. No IDs added, none removed.
