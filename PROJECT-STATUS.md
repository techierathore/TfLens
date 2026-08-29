---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-29
current_phase: Build — mockup parity repaired; 15 UI REQs `Implemented`, awaiting a verifier run
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

**The owner compared all 18 mockups against the running app and found structural drift on 13 of the 14 comparable screens — against a checklist that read 145 `Verified`.** Triage reproduced 20 findings and demoted 15 REQs; `*fix-issues` has now repaired all 15. The two worst were real damage to the product's purpose and both are gone: `/harness` rendered every figure in a **71px** column so `Cache read 2,287,975,139` broke across three lines **mid-number** (now 149px, one line, and `Verdict mix` is the mockup's pass-share bar instead of a 14-line text dump); and `/routing` had escaped the shell's scroll container — `document.scrollHeight` **2607px against a 900px viewport**, ~1,700px of blank void — because `.tflens-page` was `position: static`, so TrBlazeUI's absolutely-positioned `sr-only` pagination labels anchored to `main.relative`. Every route now measures 900/900.

**The finding that outlives the fixes is why nothing caught any of it.** All 15 REQs had passed acceptance, the §4a data-render gate and the §4b visual-truth gate — because those gates ask *does the control show data* and *do controls overlap*, and a badge rendered as plain text has text, a header that wraps does not overlap, and a missing icon is nothing to measure. The telemetry now says the same thing numerically: **`insufficient-verify-method` is 24 of 46** answered `why_missed` records and the `app` **escape rate is 91%**, with `escaped` (22) larger than every real gate catch combined (5). That gap is `REQ-NFR-020`, raised upstream as **TF-008**.

## Next command to run

```
/TechieFlow:agents:verifier *verify ui
```
15 REQs sit at `Implemented`, which is the ceiling for a self-smoke — only an executed verify-phase can write `Verified` (`guard-verify.sh` enforces it). Targets: `REQ-UI-001` `-002` `-003` `-004` `-005` `-006` `-009` `-010` `-011` `-014` `-018` `-023` `-032` `-033` `-036`.

Then `*handoff-phase TfLens`.

## Open requirements
- `Implemented` — the **15 UI REQs repaired today**; each carries its measured before/after in the checklist Remarks. Awaiting the verifier, not more work
- `Planned` — **`REQ-NFR-020`** a built screen is graded against its approved mockup, mechanically. **The root cause. Nothing on this list recurs less until it exists**
- `Planned` — `REQ-NFR-019` stored provenance is real: no row may claim a `source_sha` no sync obtained
- `Needs re-verify` — `REQ-FN-067` / `REQ-FN-070` Playbook-native figures — owner-gated on a repository that emits `events.ndjson`
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **OWNER — no repository emits `events.ndjson`**, so the Playbook axis has 0 repos. The four Playbook mockups are **not comparable** and were not counted as drift.
- **OWNER — `docs/mockups/profile.html` is wrong and should be corrected.** It says passwords are RSA-encrypted "before they leave the **browser**". This is Blazor Server: `AppManagerClient.Encrypt` (`AppManagerClient.cs:281`) runs RSA-OAEP-SHA256 **server-side**. The app's "server" wording is accurate; the triage's claim that it misstated a security property was **withdrawn**, and the mockup is the artefact to fix.
- **UPSTREAM — TF-008 raised today (High):** no mockup-parity gate. Same shape as TF-007 one day earlier.
- **UPSTREAM — TF-005 open**: `analyse_misses` averages an unrecorded `tokens_out` as zero. Latent only.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-29 | **BRD §13 parity re-run + verifier** | Parity **PASS, 0 findings**, `/export` QUOTABLE after purging 155 fabricated-provenance rows · 638/638 .NET · 79/82 Playwright | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*triage-issues`** (analyse-only, mockup parity) | 18 mockups vs the running app at 1280x900; **14 comparable, 13 drifted, 20 findings**; **15 REQs demoted**, 1 new `Planned` (`REQ-NFR-020`); 16 `escaped` gate records + 16 misses. **Zero source/test files modified** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*log-miss`** x2 | `MISS-…-21` the DevGuide and the code disagreed on whether `Connected repos` may plot a trend — **resolved in the code's favour**: `ConnectedTs` IS stored history, the docs were stale and were corrected. `MISS-…-22` a filter fix declared complete 2026-08-28 was not true in the shipped page (measured x=313, y=482) | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*fix-issues` — mockup parity** | All 15 REQs repaired to `Implemented`. Build **PASS, 0 warnings** · **638/638 .NET** · **Playwright 79 passed / 0 failed / 3 skipped (15.9m, exit 0)** — fully green. The first post-fix run showed 1 failure which was **not a regression**: `ui-coverage-misses.spec.ts` hard-coded `TechieRag`, a repo this workspace no longer connects (TfLens is connected in its place), so its selector returned `[]` while every connected repo rendered all five stream rows correctly. The spec now reads the repo list **from the page**, which is the assertion it always meant to make — a literal list fails on a swap and, worse, silently passes over a repo that was removed · **0 console errors on all 18 screen captures** · mobile 390: no horizontal scroll on any route, no document-scroll escape · header **105px → 64px**, harness value column **71px → 149px**, `/routing` doc **2607px → 900px**, `Days since` visible again. **→ 130 Verified / 15 Implemented / 2 Needs re-verify / 2 Planned / 1 N/A** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |

## Library feedback summary
- **TrBlazeUI: 20 entries, all open.** **TR-016** (no info/success/warning Badge variant) is behind several of today's "badge rendered as plain text" findings. Noted for a future entry: `SelectContent.DisposeAsync` throws `JSDisconnectedException` on circuit teardown — harmless, but the library should swallow it.
- **TechieFlow framework: 8 entries, 3 open** — **TF-008 added today** (no mockup-parity gate) · TF-007 · TF-005.
- **AppManager: 2 entries, both resolved.** TechieRag: 0 — not used (ADR-003).

## Standards compliance (last check)
- `TfLens.Guardrails.Tests` **95/95**. Also repaired today: **6 checklist status rows carried unescaped `|` inside their Remarks** and rendered as broken table rows; all 150 rows are now well-formed.

## Deferred / future
- GitHub SSO (BRD-94 → REQ-FN-012) — waits on an AppManager external-login endpoint
- **Sparklines on 3 of 4 Coverage tiles and 2 of 3 Three-questions tiles are deliberately NOT built.** The mockups draw them; `Newest record age`, `Sync errors` and `Last successful sync` have no stored series behind them, and a line through invented points is what BRD §1 exists to forbid. Recorded as a deviation, not a gap
- The Coverage repo-card header runs to two rows because it carries a `Synced`/`Imported` source badge the mockup predates (REQ-UI-042 / BRD-136) — removing a required badge to match an older drawing would be the wrong fix
