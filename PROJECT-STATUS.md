---
project: TfLens
stack: .NET 10 / Blazor Server / TrBlazeUI 2.0.0 / PostgreSQL 16 (Dapper + Npgsql) / Serilog / docker compose
last_updated: 2026-08-29
current_phase: Build — mockup parity repaired; local-dev database un-pinned; 15 UI REQs + REQ-NFR-011 awaiting a verifier run
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

**Two owner reports today, both about things every gate passed.**

**1. Mockup parity.** 18 mockups vs the running app: 13 of 14 comparable screens had drifted, against a checklist reading 145 `Verified`. All 15 REQs repaired. `/harness` rendered figures in a **71px** column so `Cache read 2,287,975,139` broke across three lines **mid-number** (now 149px, one line, with the mockup's pass-share bar); `/routing` had escaped the shell's scroll container — `document.scrollHeight` **2607px against a 900px viewport** — because `.tflens-page` was `position: static` (now 900/900 everywhere).

**2. The local development database was one nobody chose.** The `build-phase` run of `2026-08-28T04:46:59Z` ran `docker compose up -d postgres`, creating `tflens-postgres` on port 5433 **at that same second** and stopping **`WinPostgre`** — this machine's actual local dev server on 5550, which also hosts **`AppMngrDb`**, the AppManager database TfLens authenticates against. It went unnoticed for a day because **the same connection string was hard-coded in five places** (`TfLensOptions.LocalDevelopmentConnection`, `PostgresFixture.LocalDefault`, and three separate `const`s in the Core DB tests), all naming `Port=5433` with the password inline. Every layer agreed with every other layer, so nothing could disagree loudly enough to fail — and the DevGuide, `.env.example`, the startup error message and two guardrail tests all *instructed and enforced* that arrangement. All five copies are gone; there is now **no database default in any environment**; `TfLens:DbConnection` and `TfLens:AppManagerAppId` live in the user-secrets store; and the tests resolve the connection exactly the way the app does, so they can never drift onto a different server again. Data migrated to `WinPostgre` with **verified row parity on every table**.

**What links them is the same thing the telemetry already said:** `insufficient-verify-method` is the largest `why_missed` category and the `app` escape rate is **91%**. The gates measure whether something is alive, not whether it is right — and in the database case they measured five copies of one assumption agreeing with each other.

## Next command to run

```
/TechieFlow:agents:verifier *verify all
```
15 UI REQs plus `REQ-NFR-011` sit at `Implemented` / `Needs re-verify` — the ceiling for a self-smoke; only an executed verify-phase writes `Verified` (`guard-verify.sh` enforces it).

**One thing is yours, and it is destructive so I did not do it.** `tflens-postgres` is stopped with its restart policy cleared, but the container and its `pgdata` volume still exist. Its data is in `WinPostgre` with verified parity, and a dump is at `/tmp/tflens-from-stray-container-2026-08-29.sql`. To remove it:
```bash
docker rm tflens-postgres && docker volume rm tflens_pgdata
```

## Open requirements
- `Implemented` — the **15 UI REQs repaired today**; each carries its measured before/after in the checklist Remarks. Awaiting the verifier, not more work
- `Planned` — **`REQ-NFR-020`** a built screen is graded against its approved mockup, mechanically. **The root cause. Nothing on this list recurs less until it exists**
- `Needs re-verify` — **`REQ-NFR-011`** local-development configuration: no database default anywhere, connection + app id in user secrets, tests resolving the connection the way the app does
- `Planned` — `REQ-NFR-019` stored provenance is real: no row may claim a `source_sha` no sync obtained
- `Needs re-verify` — `REQ-FN-067` / `REQ-FN-070` Playbook-native figures — owner-gated on a repository that emits `events.ndjson`
- `N/A` — `REQ-FN-012` GitHub SSO, deferred by BRD-94 / ADR-012

## Known blockers
- **OWNER — no repository emits `events.ndjson`**, so the Playbook axis has 0 repos. The four Playbook mockups are **not comparable** and were not counted as drift.
- **OWNER — remove the stray container when you are ready** (command above). Stopped and un-pinned, but `docker rm` + `docker volume rm` are destructive, so they are yours.
- **OWNER — `docs/mockups/profile.html` is wrong and should be corrected.** It says passwords are RSA-encrypted "before they leave the **browser**". This is Blazor Server: `AppManagerClient.Encrypt` (`AppManagerClient.cs:281`) runs RSA-OAEP-SHA256 **server-side**. The app's "server" wording is accurate; the triage's claim that it misstated a security property was **withdrawn**, and the mockup is the artefact to fix.
- **UPSTREAM — TF-008 raised today (High):** no mockup-parity gate. Same shape as TF-007 one day earlier.
- **UPSTREAM — TF-005 open**: `analyse_misses` averages an unrecorded `tokens_out` as zero. Latent only.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-29 | **BRD §13 parity re-run + verifier** | Parity **PASS, 0 findings**, `/export` QUOTABLE after purging 155 fabricated-provenance rows · 638/638 .NET · 79/82 Playwright | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*triage-issues`** (analyse-only, mockup parity) | 18 mockups vs the running app at 1280x900; **14 comparable, 13 drifted, 20 findings**; **15 REQs demoted**, 1 new `Planned` (`REQ-NFR-020`); 16 `escaped` gate records + 16 misses. **Zero source/test files modified** | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*log-miss`** x2 | `MISS-…-21` the DevGuide and the code disagreed on whether `Connected repos` may plot a trend — **resolved in the code's favour**: `ConnectedTs` IS stored history, the docs were stale and were corrected. `MISS-…-22` a filter fix declared complete 2026-08-28 was not true in the shipped page (measured x=313, y=482) | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
| 2026-08-29 | **`*fix-issues` — the local-dev database** | `tflens-postgres` (created by the `2026-08-28T04:46:59Z` build-phase run, **not** by this session) stopped and un-pinned; **5 hard-coded copies** of one connection string removed; DB connection + app id moved into user secrets; DevGuide setup, `.env.example`, `secrets.example.json` and both startup messages rewritten to stop telling developers to stand up a second server; 2 guardrail tests inverted to forbid what they used to require. Data migrated to `WinPostgre` — **row parity exact on all 7 tables**. **638/638 .NET**, integration suite now running against `WinPostgre`; app smoked end-to-end, 0 console errors. `MISS-…-23` (the pinning) + `MISS-…-24` (mine: used the container and read past the inline password without questioning either) | [Requirements Status](docs/TfLens-Checklist.md#requirements-status) |
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
