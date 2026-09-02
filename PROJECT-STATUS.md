---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-09-02
current_phase: Handoff — 169 of 179 Verified; UAT bundle ready, awaiting owner UAT
last_verified_build: PASS
last_verified_date: 2026-09-02
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

**Handoff complete — the UAT bundle is ready.** Checklist: **169 `Verified` · 4 `Needs re-verify` · 4 `Implemented` · 1 `In Progress` · 1 `N/A` of 179**, no `Blocked` rows. Release build PASS; .NET **821/821**; Playwright **95/101** (3 failed, 3 skipped); render **489 controls / 22 screen-states, 0 non-RENDERS**; visual **1280 + 390, 0 problems, 0 console errors**; `mockup-parity` **17 findings, none of them real**.

**Read `docs/TfLens-UsageGuide.md` Known limitations before filing a UAT bug** — three things look like defects and are not: `/export` reads NOT QUOTABLE by design, `mockup-parity`'s 17 findings are all adjudicated false positives, and the Playbook axis is empty because no connected repository emits `events.ndjson`.

**Two findings were surfaced at handoff and are recorded, not fixed.** `/export`'s dataset-SHA table lists `isolation-probe/belongs-to-user-two` for user 1, who does not own it — a harness-written `SyncState` row under the wrong user id, invisible to `REQ-NFR-019`'s audit because that audit only checks streams→SyncState (`provenance-check` reports `0 unaccounted`). And the guide described `/effort` as *"not built yet"* — it has been built and verified since 2026-09-01, so a tester would have skipped a whole screen; its walkthrough is now written.

## Next command to run

```
Manual UAT per docs/TfLens-UsageGuide.md smoke checklist.
```

Run the app with `dotnet run --project src/TfLens -c Release --urls http://localhost:5099` (verified 2026-09-02: serves on 5099, `/healthz` 200, stylesheet 200). Sign in with the Test-users table at the top of the guide. After UAT passes, set `current_phase: Released` here by hand.

For an end-user manual rather than a test plan, run `*productguide TfLens`.

## Open requirements
- `Needs re-verify` — **`REQ-FN-063`** the §13 diff is 4 findings, all one corrupt record in TechieBlog's telemetry — that repository's to fix, not TfLens's (`MISS-TfLens-20260902-07`); **`REQ-UI-033`** the quotable banner reads NOT QUOTABLE for that reason — the banner is correct and is doing its job
- `Needs re-verify` — **`REQ-FN-067`** / **`REQ-FN-070`** owner-gated on a Playbook repository that emits `events.ndjson`
- `Implemented` — **`REQ-UI-050`** / **`REQ-UI-051`** the two Playbook axes: built and asserted in the state that renders today, owner-gated on the same repository
- `Implemented (80%)` — **`REQ-NFR-020`** the gate caught two real defects this run and produced two new false-positive classes; still passes screens it cannot see (TF-011)
- `Implemented` — **`REQ-NFR-019`** provenance fail-open closed; the three residual gaps are owner decisions
- `In Progress (75%)` — **`REQ-NFR-024`** clause 3 CLOSED (`docs/mockups/check2.mjs` deleted); clause 4 is `TF-014`, upstream
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **TECHIEBLOG'S OWN AGENTS — 14 records in `TechieBlog/docs/metrics/runs.jsonl` carry `started` AFTER `ended`.** One (line 16) stores the negative arithmetic as `duration_s: -166` and is therefore visible; the other 13 (records 20–29, 31, 32) store a plausible round duration (3600, 2700, 1800, 1500, 1200, 900, 600, 420) over impossible timestamps — e.g. `refresh-status` started `20:05:00`, ended `17:59:39`. The round starts climb on a tidy schedule while the ends climb independently and lower, which reads as a fabricated timeline written over real end times. **TechieBlog's phase-effort durations are largely not measurements**, and TfLens reports them faithfully. **This is not TfLens's to repair** — it is that repository's data, owned by its own TechieFlow agents (`*log-miss` / `*fix-issues` there). TfLens is a lens; it changes nothing it looks at (BRD §1). The systemic half is `TF-015` below. Until it is fixed there, the §13 diff holds at 4 findings and `/export` correctly reads NOT QUOTABLE.
- **UPSTREAM `TF-015` — `tf-emit.sh` accepts `ended` earlier than `started`.** The producer computed and wrote `duration_s: -166` without complaint, and `tf-metrics.sh` then sums negatives (`if r.get("duration_s")` is truthy) while TfLens rejects them (`DurationS is > 0`) — so the same corpus yields two different totals and neither tool flags the impossible record. A one-line validation at emit time would have stopped all 14 at the source.
- **OWNER — two guards for one concept (`REQ-NFR-005`).** `Pooled.cs:55` admits a negative duration into the throughput median (`is not (null or 0)`); `PhaseMetrics.cs:504` rejects it (`is > 0`). With the `-166` record present that put a negative REQs-per-second into the median (6.26 against the true 6.52). The reference has the identical hole, so §13 agrees with it rather than catching it. Tightening TfLens alone would create a divergence the next time a negative arrives — your call, same shape as the guard question itself.
- **OWNER — the 2026-08-29 parity run was never recorded in DECISIONS.md §6**, only in `parity-last.json`. BRD §13 step 6 and BRD-71 require both, so `REQ-FN-063`'s second acceptance clause has never actually been satisfied. Worth a §6 entry when the next run passes.
- **OWNER — a Playbook repository that exports closes four rows at once.** `REQ-UI-050`, `REQ-UI-051`, `REQ-FN-067`, `REQ-FN-070`. The ingest exists for both streams — an upload, not a build.
- **OWNER — ratify or revert the eleventh `Measured dollars` row (`REQ-UI-025`).** BRD-53 requires it; the approved mockup showed ten.
- **OWNER — does BRD-144 clause 2 bind the anonymous auth routes?** `/register` (1280 +73, 390 +416) and `/reset-password` (390 +166) still overflow, and their approved mockups scroll too. Unchanged this run, awaiting your ruling.
- **OWNER — `DataRoot` resolves against the working directory**, so running from the repo root makes the integrity banner call 486 rows of real data fabricated (`MISS-…-30-02`).
- **OWNER — `REQ-NFR-019` cannot see a poisoned raw `.jsonl` replayed by `rebuild`.** A one-time adoption decision on live data.
- **OWNER — harness writes to the app's own store cannot be prevented from inside this repo.** Provision a `tflens_harness` role with writes revoked on the eight stream tables.
- **OWNER — legacy harness rows under `userId 9001`** sit below the 90000 reserved floor; deletion is yours.
- **OWNER — remove the stray container:** `docker rm tflens-postgres && docker volume rm tflens_pgdata`.
- **OWNER — `docs/mockups/profile.html` is wrong** (claims RSA-in-browser; this is Blazor Server). The app is correct; the mockup is the artefact to fix.
- **UPSTREAM — TF-011** the parity gate passes screens it cannot see · **TF-014** the gitignore audit prunes every dot-directory · TF-013 · TF-010 · TF-009 · TF-008 · TF-007 · TF-005.
- **`REQ-NFR-008` — order-dependent instability, partly fixed.** Postgres-backed classes share one xUnit collection (647/647 again this run). `ui-misses.spec.ts`'s Playwright-side ordering is untouched.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-09-02 | **`*handoff-phase` — READY FOR UAT (with two recorded findings)** | UsageGuide finalised: test-users table reconciled (both accounts sign in live), **`/effort` walkthrough written — the guide had it as "not built yet" since 2026-09-01**, Framework switch corrected to seven routes, counts refreshed (.NET 630→**821**, Playwright 77→**101** specs, REQs 143→**179**), Known limitations rewritten so a tester does not file the three by-design behaviours. Two findings recorded not fixed: the stray `isolation-probe/belongs-to-user-two` `SyncState` row on user 1's `/export` (`REQ-NFR-019`, a fourth audit gap — streams→SyncState only) and the still-open §13 blocker in TechieBlog's telemetry. Feedback consolidated: TrBlazeUI 28, TechieFlow 15 (`TF-015` new), AppManager 2 resolved. Verdict **⚠ ready for UAT** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-02 | **§13 re-run over an API-fetched dataset — provenance verified, 4 findings, all one foreign record** | Re-run per BRD §13 step 1 against streams **fetched from the GitHub API at the pinned SHAs**, not off local disk: all **23 stream files hash-identical** to TfLens's own archives, so the fetch path and the archive agree and the disagreement is genuinely about the data. 4 findings, all the single TechieBlog record whose `ended` precedes its `started`; **13 more such records** hide behind plausible round durations. That repo's data is its own agents' to repair — an earlier edit of it from here was **reverted**, since TfLens changes nothing it looks at (BRD §1). `TF-015` raised for the producer-side gap. No stamp written | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-02 | **`*fix-issues` — three UI defects closed, and the parity "re-run" turned out to be four real ones** | All three reported defects fixed and verified: `REQ-UI-048` (the mockup states the missing `white-space: nowrap` rule outright and the build never carried it), `REQ-UI-038` (the fix already existed in `GateOutcomes.razor.css` and had not been applied to the second tab strip), `REQ-UI-018` (the line strokes `--chart-1` on the polyline, matching the mockup's own cascade position — moving the colour onto the svg drew the same pixels but still failed the gate). `mockup-parity` **22 → 17 findings, 0 real**. **BRD §13 executed for real: 62 findings → 4.** A stale store (43), `cost_sole_n` biased upward (`REQ-FN-079`), `session_duplicates_collapsed` on the replay path, six MISSING late-gate keys (`REQ-FN-052`). The remaining 4 are one `duration_s: -166` record in TechieBlog — owner-gated. Also: a phantom `RENDER-EMPTY` traced to a gate list demanding a control the design forbids (`REQ-UI-023`), and `REQ-UI-020`'s spec was the stale side of a correct change. Playwright 95/101, .NET **821/821**, render **489 controls / 0 non-RENDERS**, visual 0 problems | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-02 | **`*verify all` — the confirming run that was not** | Release, rung #2 on :5099 against `localhost:5550/tflens`. Playwright **95/101** (3 skipped; `00-boot` was a cold-start JIT timeout and passes warm in 17.3s), .NET **815/815**, render **487 controls / 22 screen-states, 0 problems**, visual 1280+390 **0 problems**, assets **78 over 13 pages, 0 missing, 0 redirects**, perf **p95 load 412.7 ms vs 1500 ms** (150 samples, Release, 0 errors/non-200/redirects). `mockup-parity` **921 comparisons / 13 screens, 22 findings, 9 waived**: **2 NEW REAL** — `/effort` mid-word token break and `/misses` container overflow, both at 390, both invisible to §4a/§4b — plus the stale-parity-record failure on `/export`. **Four rows demoted**: `REQ-UI-048`, `REQ-UI-038`, `REQ-FN-063`, `REQ-UI-033`. Nine `/export` findings and the 9-screen `app-sidebar` clip **disproved by measurement**, not waived. The shipped `tf-mockup-parity.sh` cross-check (1015 comparisons / 1956 content-level) returns 356 findings capped at 40/screen — run without this app's allowList it reproduces the same families; its one real-looking candidate (`clip@390:repo-streams-*`) measured 292/292 and is a false positive | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-02 | **`*log-miss` — `.vs/` tracked, owner-found; untracked same day** | `MISS-TfLens-20260902-01`, **`req_id: null`** — no REQ owned it, which is the finding. **New `REQ-NFR-024`** states the rule against machine-specific tracked state, and caught `docs/mockups/check2.mjs` on its first application. `TF-014` raised upstream. No code touched | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*amend-docs` — three clauses corrected against the reference script** | BRD-147 and BRD-150 amended in place; Architecture §7 and ADR-026 corrected; **ADR-028** records the resolution rule. `REQ-FN-090`/`092`/`093` keep `Verified` — the code always matched the script | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*build-phase` — F-EFFORT, 27 REQs in one pass** | **25 `Verified`, 2 held.** Playwright 95/101; .NET 815/815. Engine vs the reference script: **401 figures, 0 diffs**. `TR-029`/`TR-030` raised; 7 spec gaps logged | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*fix-issues` — the 2026-08-30 UAT drift, closed** | Eight REQs → `Verified`. `mockup-parity` **39 → 18** findings. Root cause: `Harness.razor.css` had not parsed for two days — now a build failure via `ScopedCssTests` | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-09-01 | **`*verify` — the `/gate-outcomes` rename** | Rename introduced **0 findings**. `REQ-UI-018` held on a real colour drift | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-30 | **`*build-phase` (FIX) — harness repair + mockup anchor sweep** | 60 anchors added to 3 mockups: `harness` 22 → **135** comparisons. Same app, measured properly | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*build-phase` + `*verify all`** | New `mockup-parity` gate: 44 findings → 8 UI rows demoted, then repaired to 0 | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- **TrBlazeUI: 28 entries, all open** (highest `TR-030`). Newest: `TR-028` `BarChart` exposes no axis/grid/label control · `TR-029` `Badge` has no white-space handling · `TR-030` `CollapsibleTrigger` takes no `Class`.
- **TechieFlow framework: 15 entries, 9 open** — **TF-015 (High, new)** `tf-emit.sh` accepts `ended` before `started`, and the two consumers then disagree about the resulting negative duration · **TF-011 (High)** the parity gate passes screens it cannot see · TF-014 · TF-013 · TF-010 · TF-009 · TF-008 · TF-007 · TF-005. **Two new false-positive classes observed 2026-09-02** (positional cell pairing across tables of unequal column count; `wrap` inferred from row height) are recorded in `REQ-NFR-020`'s Remarks — they belong to TF-011's family and did not need a new entry.
- **AppManager: 2 entries, both resolved.** TechieRag: 0 — not used (ADR-003).

## Standards compliance (last check)
- `TfLens.Guardrails.Tests` **119/119**. Coding-standards greps clean. `TfLens.Core.Tests` 647/647, `TfLens.Integration.Tests` 49/49, all Release.
- **Correction to a claim this file carried:** the Requirements Status table does **not** have zero unescaped pipes. 179 rows, **11 of them carry an unescaped `|` inside their Remarks cell**, which splits the row into 9 columns for any mechanical reader (`REQ-UI-005`, `011`, `012`, `023`, `032`, `041`, `045`, `047`, `REQ-FN-087`, `REQ-NFR-011`, `REQ-NFR-015`). Measured this run with a splitter that ignores escaped pipes. Worth a one-pass repair.
- Two build-failing guardrail families hold: `ScopedCssTests`, and `ActorGroupingTests` + `PhaseEffortIntegrityTests` (REQ-NFR-022/023).

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- **Sparklines on most Coverage and Gate-outcomes tiles are deliberately NOT built** — no stored series behind them, and a line through invented points is what BRD §1 forbids
- **`REQ-UI-027`'s `models` column renders a raw JSON array** — no gate can see it and the acceptance never said how to render it (`MISS-…-30-01`)
- **Chart series colours are ungraded** — canvas/SVG carry no per-series anchor on either side, though BRD-144 names them
