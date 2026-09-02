---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-09-02
current_phase: Verify — 170 of 179 Verified; one owner command outstanding
last_verified_build: PASS
last_verified_date: 2026-09-01
---

# TfLens — Status

<!--
  ============================================================================
  THIS FILE IS A CRISP, FIXED-SHAPE SNAPSHOT — OVERWRITE IT, NEVER APPEND TO IT.
  It has exactly the sections below and NO others. It should stay well under
  ~60 lines — a human reads it in ten seconds.
  See .tfcore/tasks/_status-update-gate.md §"CRISP, FIXED-SHAPE snapshot".
  ============================================================================
-->

## Where I am

**Checklist: 170 `Verified` · 4 `Implemented` · 3 `Needs re-verify` · 1 `Planned` · 1 `N/A` of 179.** Build PASS; Playwright 95/101 (3 pre-existing failures); .NET 815/815; Guardrails 119/119.

**F-EFFORT is built and verified** — all 27 REQs in one pass, 25 `Verified`. `/effort` was graded by `mockup-parity` on its first run and returned one finding, the sidebar false positive that fires on every screen. The engine was diffed against the reference script `tf-metrics.sh`: **401 figures, 0 diffs**. Two Playbook rows are held `Implemented` on purpose — their acceptance is *real figures* and none exist yet.

**One thing needs you: `.vs/` is tracked.** Owner-found 2026-09-02. The ignore rule read `/.vs/TfLens.slnx`, covering only the solution subfolder, so `.vs/ProjectEvaluation/`'s three `.bin` files are tracked — and one carries 246 absolute paths from this machine's Visual Studio install. The rule is now `/.vs/`, but **an ignore rule does not untrack anything**; the command is below and is yours to run. `REQ-NFR-024` states the general rule REQ-NFR-016 only gave two instances of. The audit that should have caught it skips every dot-directory — raised upstream as `TF-014`, not fixable here (REQ-NFR-018).

## Next command to run

```
git rm -r --cached .vs && git commit -m "Untrack .vs — per-developer IDE state (REQ-NFR-024)"
```
**Yours, not mine** — version-control writes are owner-only in every mode. It untracks the three files while leaving them on disk; the widened `/.vs/` rule keeps them out from then on. After that, `*verify all` re-confirms the tree, then `*handoff-phase`.

## Open requirements
- `Planned` — **`REQ-NFR-024`** no per-developer or machine-specific state tracked in version control. Ignore rule widened this pass; **three `.vs/ProjectEvaluation/*.bin` files remain tracked until the command above is run**
- `Implemented` — **`REQ-UI-050`** / **`REQ-UI-051`** the two Playbook axes: built and asserted in the state that renders today (unsupported / empty), populated branches written and guarded. Owner-gated on a Playbook repository that exports — same gate as `REQ-FN-067`/`REQ-FN-070`
- `Needs re-verify` — **`REQ-UI-018`** `stat-sparkline` paints amber via `@AccentClass` where the design specifies `--chart-1` (`MISS-TfLens-20260901-03`); the only remaining `color` finding anywhere
- `Implemented (70%)` — **`REQ-NFR-020`** the gate catches real drift and caught a bug in itself; still passes screens it cannot see (TF-011); clause 3 blocked on TF-008
- `Implemented` — **`REQ-NFR-019`** provenance fail-open closed; the three residual gaps are owner decisions
- `Needs re-verify` — **`REQ-FN-067`** / **`REQ-FN-070`** owner-gated on a repository that emits `events.ndjson`
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **OWNER — untrack `.vs`** (command above). Until then the working tree is dirty after every solution load and one machine's absolute paths stay committed.
- **OWNER — a Playbook repository that exports closes four rows at once.** `REQ-UI-050`, `REQ-UI-051`, `REQ-FN-067`, `REQ-FN-070`. The ingest exists for both streams (`phase-metrics.ndjson`, `playbook-misses.ndjson`) — an upload, not a build.
- **OWNER — ratify or revert the eleventh `Measured dollars` row (`REQ-UI-025`).** BRD-53 requires it; the approved mockup showed ten. The requirement was kept and the mockup amended, as a YOLO default on a call reserved for you. One-line revert; reversing means amending BRD-53.
- **OWNER — does BRD-144 clause 2 bind the anonymous auth routes?** `/register` and `/reset-password` overflow, but their approved mockups scroll too. Until you rule, `/login` and `/forgot-password` carry a compact brand bar below 768px and those two do not.
- **OWNER — `DataRoot` resolves against the working directory**, so running from the repo root makes the integrity banner call 486 rows of real data fabricated (`MISS-…-30-02`). Probe order touches the docker deployment that mounts `data/`.
- **OWNER — `REQ-NFR-019` cannot see a poisoned raw `.jsonl` replayed by `rebuild`.** Making the ledger the sole oracle is a one-time adoption decision on live data.
- **OWNER — harness writes to the app's own store cannot be prevented from inside this repo.** Provision a `tflens_harness` role with writes revoked on the eight stream tables.
- **OWNER — legacy harness rows under `userId 9001`** sit below the 90000 reserved floor; deletion is yours.
- **OWNER — remove the stray container:** `docker rm tflens-postgres && docker volume rm tflens_pgdata`.
- **OWNER — `docs/mockups/profile.html` is wrong** (claims RSA-in-browser; this is Blazor Server). The app is correct; the mockup is the artefact to fix.
- **UPSTREAM — TF-014 raised today:** the gitignore audit prunes every dot-directory, so it cannot see the IDE-state folders it exists to catch. **TF-011** the parity gate passes screens it cannot see · **TF-008** no `gates_run` row · **TF-009** grades no `border-style` · **TF-010** the renderer omits its own mandated CTA box.
- **`REQ-NFR-008` — order-dependent instability, partly fixed.** The Postgres-backed classes now share one xUnit collection (647/647 over three runs). `ui-misses.spec.ts`'s Playwright-side ordering is untouched and still only one clean observation.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-09-02 | **`*log-miss` — `.vs/` tracked, owner-found** | `MISS-TfLens-20260902-01` (`unspecified-gap` / `config` / `minor`, `missing-checklist-item`, **`req_id: null`** — no REQ owned it, which is the finding). Ignore rule widened `/.vs/TfLens.slnx` → `/.vs/`; **new `REQ-NFR-024`** states the rule REQ-NFR-016 gave only instances of, with acceptance written against the harm (no absolute machine path in tracked content) rather than a folder list. `TF-014` raised upstream — the audit prunes every dot-directory from its walk. **No code touched, no build run**; the three tracked `.bin` files await the owner's untrack | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*amend-docs` — three clauses corrected against the reference script** | BRD-147 (`unobserved_predates_field` is a null-check, not a date check) and BRD-150 (the dominant-model fallback is required where a run carries no split) amended in place; Architecture §7 and ADR-026 corrected to `FanoutObservation(double? Spawns, …)` — only `tokens_out_per_run` is genuinely `MIN_N`-floored. **ADR-028** records the resolution rule. Each divergence confirmed by reading `analyse_phases` directly. No code touched; `REQ-FN-090`/`092`/`093` keep `Verified` — the code always matched the script, the document was the drift | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*build-phase` — F-EFFORT, 27 REQs in one pass** | **25 `Verified`, 2 held.** 8 clusters in 3 waves + a 9th for acceptance. Playwright **95/101** (+14 acceptance tests); .NET **815/815** — Core 647 over three runs after fixing a real `40P01` deadlock, Guardrails **105 → 119**. `/effort` graded on its first run with 1 finding; **0 new findings** across the pass. Engine vs the reference script: **401 figures, 0 diffs**, which surfaced three doc divergences. Guards caught four things eyes did not. `TR-029`/`TR-030` raised; 7 spec gaps logged | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*fix-issues` — the 2026-08-30 UAT drift, closed** | Eight REQs → `Verified`. `mockup-parity` **39 findings → 18**; `/harness` 3 → 1. Root cause: `Harness.razor.css` had **not parsed for two days** (a comment-close marker written as prose), so the recorded fix had never applied — now a build failure via `ScopedCssTests`. `REQ-NFR-015` closed structurally with `MapStaticAssets` fingerprinting. `TR-028` raised; a gate bug fixed in `_mockup-parity.ts` | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*verify` — the `/gate-outcomes` rename** | Playwright 78/85; render+visual 486 controls, 0 problems; .NET 689/689 Release. Rename introduced **0 findings**. `REQ-UI-018` held on a real colour drift. Agent overstep logged as `MISS-TfLens-20260901-02` + `TF-013` | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*amend-docs` — phase effort and efficiency (F-EFFORT)** | `BRD-145`..`BRD-169` appended + 7 amended; Architecture gains three adapters, three tables and **ADR-023..ADR-027**. 27 checklist rows appended. Owner evidence wired into the table, which exposed two rows reading `Verified` over open findings — both demoted (`MISS-TfLens-20260901-01`) | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-30 | **`*build-phase` (FIX) — harness repair + mockup anchor sweep** | 60 anchors added to 3 mockups: `harness` 22 → **135** comparisons. Gate went 10 PASS/0 findings → 2 PASS/39 findings — same app, measured properly. 8 findings adjudicated as the TF-012 false positive | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-30 | **Owner review of `/harness` vs its mockup** | **The gate's PASS was not trustworthy** — it compared 22 elements, zero page content, and passed. `TF-011` (High) raised | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*build-phase` + `*verify all`** | New `mockup-parity` gate: 44 findings → 8 UI rows demoted, then repaired to 0. Perf `REQ-NFR-001` p95 559 ms vs a 1500 ms budget | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **Purge of the 155 fabricated rows + parity re-run** | Purged 155 rows plus 8 poisoned raw files; `rebuild --user 2` → 34 files, 1101 records, **0 invalid** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- **TrBlazeUI: 28 entries, all open** (highest `TR-030`). Newest: `TR-028` `BarChart` exposes no axis/grid/label control and no route to `ApexChartOptions`, so a chart cannot be built to an approved design · `TR-029` `Badge` has no white-space handling, so a multi-word status spills its pill · `TR-030` `CollapsibleTrigger` takes no `Class`, so a disclosure header cannot span its row.
- **TechieFlow framework: 14 entries, 8 open** — **TF-014 (new)** the gitignore audit prunes every dot-directory · **TF-011 (High)** the parity gate passes screens it cannot see · **TF-013** verify-phase is silent about infrastructure · TF-010 · TF-009 · TF-008 · TF-007 · TF-005.
- **AppManager: 2 entries, both resolved.** TechieRag: 0 — not used (ADR-003).

## Standards compliance (last check)
- `TfLens.Guardrails.Tests` **119/119** (was 105). Coding-standards greps clean.
- Requirements Status table: **179 rows, zero with unescaped pipes** — measured, not assumed.
- Two new build-failing guardrail families this cycle: `ScopedCssTests` (a dead scoped-CSS rule is a build failure) and `ActorGroupingTests` + `PhaseEffortIntegrityTests` (REQ-NFR-022/023), the latter proven to fire by injecting real violations and restoring the files byte-identical.

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- **Sparklines on most Coverage and Gate-outcomes tiles are deliberately NOT built** — no stored series behind them, and a line through invented points is what BRD §1 forbids
- **`REQ-UI-027`'s `models` column renders a raw JSON array** — no gate can see it (non-inline children) and the acceptance never said how to render it. `MISS-…-30-01`; needs an acceptance bullet before a fix
- **Chart series colours are ungraded** — canvas/SVG carry no per-series anchor on either side, though BRD-144 names them
