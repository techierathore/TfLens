---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-29
current_phase: Build — the mockup-parity gate now exists and immediately demoted 8 screens
last_verified_build: PASS
last_verified_date: 2026-08-29
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

**`REQ-FN-058` is fixed and `Verified`, so the export's figures are honest again.** `MetricsEngine.DeclaredProjectType` took `.FirstOrDefault()` with no ordering and returned whichever record the store handed back first; it now ranks all four streams by instant and returns the repository's **current** declaration, which is what the reference reads. `project_type` and `stale_types` for the reclassified TfLens repo now read `app` / `docs` on **both** sides. The before/after was measured, not quoted — the old body was restored, rebuilt and re-compared (3 findings), then the fix restored (1 finding). **The surviving finding is not a defect:** `pooled.session_duplicates_collapsed` 45 vs 7 is the counter reading `presented − stored` across the whole 34-file archive while the reference sees one pinned snapshot per repo, matching the 2026-08-29 adjudication.

**The two most consequential rows were built — and both are `Implemented`, not `Verified`, on purpose.** `BRD-143`/`BRD-144` were appended *before* either was built, so neither is inferred from a UAT finding any more. `REQ-NFR-019` enforces provenance at three layers, including CHECK constraints that refuse the raw-SQL path the 155 fabricated rows actually used, and it **proved detection end to end** (clean → orphan injected under test user 3 → NOT QUOTABLE → cleaned → QUOTABLE). `REQ-NFR-020`'s gate proved it can **catch**, and that catch-proof found a bug in the gate itself: a column crushed to 18px returned `null` instead of the worst overflow, so it had been silently passing the most broken column it could meet.

**The new gate immediately demoted 8 previously-`Verified` screens — which is the whole point of it.** 44 findings over 21 screens. `framework-switch` renders as plain text with no track where the mockup draws a badge, on all six report pages: **12 findings, and the entire verdict for three screens.** `drift-table` breaks `claude-opus-5` and `fix-issues` mid-token — the exact failure BRD-144 was written around, invisible to render-truth and visual-truth because the text is present and nothing overlaps.

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TfLens
```
FIX mode over the 8 drifted UI rows. Start with **`REQ-UI-010`** — one control, clears three screens outright and leads on three more.

## Open requirements
- `Needs re-verify` — **`REQ-UI-010`** framework switch has no badge chrome on 6 pages (12 findings). **The single highest-value fix on this list**
- `Needs re-verify` — **`REQ-UI-027`** (18) · **`REQ-UI-037`** (4) · **`REQ-UI-014`** (2) formatted values break mid-digit in too-narrow cells
- `Needs re-verify` — **`REQ-UI-011`** (6) row wrap · **`REQ-UI-036`** (2) `kpi-rework-usd` green in the mockup, neutral in the app
- `Needs re-verify` — **`REQ-UI-001`** / **`REQ-UI-003`** document escapes the scroll container @390 (122px / 14px) where the mockup does **not** scroll
- `Implemented` — **`REQ-NFR-019`** built; 4 named gaps below · **`REQ-NFR-020`** built; clause 3 (`gates_run`) unwired
- `Needs re-verify` — `REQ-FN-067` / `REQ-FN-070` owner-gated on a repo emitting `events.ndjson`; unchanged
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **OWNER — is BRD-144 clause 2 meant to bind the anonymous auth routes?** `/register` and `/reset-password` overflow, but **their approved mockups scroll too**. The clause is worded absolutely yet says *app-shell* scroll container, and those four routes sit outside the shell. Left `Verified` and logged as `MISS-…-29` rather than resolved by the agent that benefits from either answer.
- **OWNER — `REQ-NFR-019` still cannot see a poisoned raw `.jsonl` replayed by `rebuild`**, which is *half of what happened on 2026-08-29*. The raw archive must be a provenance oracle because `SyncState` keeps only the newest SHA while user 2 holds rows on 8 SHAs. Making the ledger the sole oracle is a one-time adoption decision on live data.
- **OWNER — legacy harness rows under `userId 9001`** sit below the new 90000 reserved floor and are technically exportable. Not purged; deletion is yours.
- **OWNER — remove the stray container:** `docker rm tflens-postgres && docker volume rm tflens_pgdata`. Data is in `WinPostgre` with verified parity.
- **OWNER — `docs/mockups/profile.html` is wrong** (claims RSA-in-browser; this is Blazor Server). The app's wording is correct — the mockup is the artefact to fix.
- **`REQ-NFR-008` — order-dependent instability in the verify suite, recorded not dismissed.** `ui-misses.spec.ts` failed in **both** full runs at a **different test each time**, and passes **9/9 twice in isolation**. Pre-existing: the 2026-08-29 occurrence predates the new gate. It was written off as flake once already.
- **UPSTREAM — TF-008 open:** wiring `mockup-parity` into `gates_run` needs a `.tfcore/` change that `REQ-NFR-018` forbids this repo from making.
- **UPSTREAM — TF-005 open**: `analyse_misses` averages an unrecorded `tokens_out` as zero. Latent only.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-29 | **`*amend-docs` + `*build-phase` + `*verify all`** | **BRD-143 / BRD-144 appended before build**; §13 gained a second standing rule, §14 two criteria, F-OPS reopened. 3 clusters fanned out. Build **PASS 0 warnings**; **.NET 683/683 serial**; Playwright 2 full runs **78/85 then 79/85**, 3 skipped. `rebuild` replayed **34 files / 1101 records / 0 invalid** through the new provenance write-path. Parity: FN-058's two keys match the reference exactly; 1 finding, adjudicated dataset-shape. **New `mockup-parity` gate: 1 PASS / 11 FAIL / 6 SKIPPED / 3 NO-MOCKUP, 44 findings** → 8 UI rows demoted. `REQ-FN-058` → **Verified**; `REQ-NFR-019` / `REQ-NFR-020` → **Implemented** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **Purge of the 155 fabricated rows + BRD §13 parity re-run** | Purged exactly **155** plus the **8 poisoned raw files**; `rebuild --user 2` → 34 files, 1101 records, **0 invalid**. Parity FAILED with the inverted `project_type` — **fixed above**. TechieFlow's gate data was 100% fabricated (34 → 0) | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*verify all`** (Release, all 4 gates) | **145 of 150 `Verified`** (+17). Perf gate credited for the first time — `REQ-NFR-001` p95 **172.2 ms** vs a 1500 ms budget. Booted with **no `TfLens*` env var**, which is `REQ-NFR-011`'s acceptance observed rather than argued | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*triage-issues`** (analyse-only, mockup parity) | 18 mockups vs the running app; **14 comparable, 13 drifted, 20 findings**; 15 REQs demoted, `REQ-NFR-020` raised. **Zero source files modified** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*fix-issues` — the local-dev database** | `tflens-postgres` stopped and un-pinned; **5 hard-coded copies** of one connection string removed; data migrated to `WinPostgre` with **exact row parity on all 7 tables** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- **TrBlazeUI: 20 entries, all open** (highest TR-023). None added today — the `framework-switch` chrome is app-level composition, and the theme-toggle icon finding was anchoring plus a recorded decision, not a library gap.
- **TechieFlow framework: 8 entries, 3 open** — TF-008 (no mockup-parity gate — **this session built the app-side half**) · TF-007 · TF-005.
- **AppManager: 2 entries, both resolved.** TechieRag: 0 — not used (ADR-003).

## Standards compliance (last check)
- `TfLens.Guardrails.Tests` **104/104**. **9 rows in the Requirements Status table carry stray unescaped pipes** (`REQ-UI-005/011/012/023/032/040/041`, `REQ-NFR-011`, `REQ-FN-087`) and therefore mis-split their cells. This session's writer located `Status` by its enum value and `Details` by its `[view]` link instead of by position, so it added no new corruption — but `REQ-UI-040`'s lost 2026-08-28 owner remark is still lost and still needs one `git show`, which is yours to run.

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- **Sparklines on 3 of 4 Coverage tiles and 2 of 3 Three-questions tiles are deliberately NOT built** — no stored series behind them, and a line through invented points is what BRD §1 forbids. The mockup-parity allow-list carries these as `UNUSED`: those mockups have no `kpi-*` anchors, so the gate cannot reach them either way
- The Coverage repo-card header runs to two rows because it carries a `Synced`/`Imported` badge the mockup predates (REQ-UI-042 / BRD-136)
- **Chart series colours are ungraded** — canvas/SVG carry no per-series anchor on either side, though BRD-144 names them
