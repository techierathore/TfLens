# TfLens — UAT readiness and numbers proof, 2026-10-12

| | |
|---|---|
| App | TfLens |
| Date | 2026-10-12 |
| For | the owner's live UAT on 2026-10-17 |
| Running at | http://localhost:5099 (left running) |

## The short version

- **The numbers you asked about hold.** For each of the four imported public repositories, TfLens shows the same runs in the last 7 days, misses in the last 7 days and gate outcomes by gate as a straight count of the jsonl files: 16 of 16 figures match. User 1's pooled pages match on all 18 figures. Before today's fixes TrBlazeUI's misses read 23 against 22 in the files: a withdrawn miss was still being counted.
- **The whole export now agrees with `tf-metrics.sh` key for key.** The first compare gave 86 differences; all were traced and fixed in TfLens, none was allow-listed. The pass is recorded (DECISIONS.md P-004), and `/export` reads **QUOTABLE** at parser 1.3.0.
- **Lekhak is not a source anywhere I can see, locally or on the deployed site.** Two public repositories *mention* its name in their own records (details below).
- **Sevak was not imported.** GitHub reports it as not found, which is how a private repository answers. You chose to skip it.
- **Five of the eight rows you named are Verified.** REQ-UI-006, REQ-NFR-020, REQ-NFR-025, REQ-FN-063 and REQ-NFR-019 are Verified. REQ-UI-023, REQ-UI-029 and REQ-UI-033 pass every functional test but fail the check that compares each screen with its approved mockup. Two of the causes are mistakes in that check, which I reported to TechieFlow:

| Problem | What it affects | Does it block or break anything |
|---|---|---|
| TF-053 — the mockup check calls a box that scrolls its own long text "cut off" | the compare-output box on `/export`, so REQ-UI-032 and REQ-UI-033 | No. The page is correct; those two rows stay below Verified on that one check |
| TF-054 — the mockup check counts a whole chart as an "icon" | the token chart on `/harness`, so REQ-UI-023 | No. The page is correct; the row stays below Verified on that one check |

To get both fixed, open a session in the TechieFlow repository and paste:

```
Fix TF-053 and TF-054 filed by TfLens in /mnt/c/1MyCode/TfLens/docs/TfLens-TechieFlow-Feedback.md, then reply in that file's Resolution status.
```

## 1. Starting it as the Usage Guide says

`docker compose up -d postgres`, then `dotnet run --project src/TfLens -c Release --urls http://localhost:5099`. `/healthz` answers `200 {"status":"ok","database":"up"}`.

What failed or misled on today's machine:

1. **`dotnet` is not on the WSL PATH.** The SDK is installed in the `.dotnet` folder of your Linux home (version 10.0.302), so `dotnet run` fails in a fresh shell. It works once that folder is added to the PATH, as the Usage Guide now shows. Windows also has `dotnet.exe` (10.0.401).
2. **The guide starts a database the app does not use.** `docker compose up -d postgres` starts the `tflens-postgres` container on port 5433. The app's user secrets point at the `WinPostgre` container on port 5550, which holds all the real data. `Program.cs` itself records that a second Postgres container was once an agent's mistake. The compose container started cleanly and was left running, unused.
3. **The guide says `TfLens:DbConnection` "falls back to the local Development connection".** That fallback was removed on 2026-08-29. Without the secret the app refuses to start.
4. **The first sign-in after a start could fail.** Typing into the sign-in form before the page became interactive was wiped, and Sign in then posted empty fields. This happened four times today. **Fixed** (REQ-UI-001): the fields stay disabled for the fraction of a second before the page is interactive.
5. **One live test failed against AppManager today.** After a sign-out, AppManager now says the session token was *cancelled*; the test expected it to say the token had *run out*. AppManager's own API guide lists the new answer, so the test was corrected. TfLens itself behaves the same either way.
6. Your path `C:\1MyCode\TrBlazeUI` does not exist; the repository is at `C:\3AIGenCode\TrBlazeUI` (remote `techierathore/TrBlazeUI`). That copy was used.

## Lekhak and the public-only rule

- **Local database:** no row in any of the 16 tables mentions Lekhak, and neither do the raw archives under `src/TfLens/data`. Nothing to remove.
- **Deployed site (https://tflens.techierathore.com):** both accounts I can sign in with (`tflensdemo@…` and `tflenstest2@…`) have **no sources at all**, and no page mentions Lekhak. I cannot see any other account on that site; if you use a third one there, check its Repos page yourself.
- **Mentions inside public repositories.** TechieFlow's own records name Lekhak 45 times: 33 miss descriptions that quote Lekhak feedback items by number and 12 run `mode` labels. TrBlazeUI's records name it 4 times, in miss descriptions. All 49 are those repositories' own records (`app` is TechieFlow or TrBlazeUI), so they are not Lekhak's numbers and none was removed. But the word "Lekhak" **will be visible as text** if you open the raw miss or run detail for those records on stage. The same text is already public in those repositories' `docs/metrics`.
- **Sevak:** `api.github.com/repos/techierathore/Sevak` answers 404 both anonymously and with the TfLens token, as Lekhak does. Not imported.
- **Chatur shows a `private` badge on Repos, though it is public.** TfLens marks every source *created* by an import as not public, because an imported source is not fetched from GitHub. TechieFlow, TechieRag and TrBlazeUI kept `public` because they existed as fetched sources first. Nothing was changed; this is your call (see "Decisions for you").

## 2. The rows you asked to re-verify

| Row | Before | Now | Why |
|---|---|---|---|
| REQ-UI-006 sidebar | Implemented | **Verified** | all three shell tests pass |
| REQ-NFR-020 graded against the mockup | Implemented | **Verified** | new test: a badge planted in a copy of the harness mockup fails the parity gate at 1280 and 390. A whole missing section is caught by the render check instead (proved by hand: "anchored control … is not on the page") |
| REQ-NFR-025 deploy pipeline | Implemented | **Verified** | new guardrail tests read `deploy.yml`: push to main → build → deploy → `/healthz` probe that exits 1. The live site answers 200 and serves `blazor.web.js` |
| REQ-FN-063 parity record | Needs re-verify | **Verified** | parity run recorded with date, script hash, parser 1.3.0 and all six dataset identities, and P-004 is in DECISIONS.md. The 2026-09-11 record named no dataset at all |
| REQ-NFR-019 every stored row traces to a real sync or import | Implemented | **Verified** | the check of where each stored number came from: 31 sources, 6,706 rows, none unaccounted for |
| REQ-UI-023 harness columns | Needs re-verify | Needs re-verify | tests pass; parity reports the chart's `<svg>` as "an icon" — a tool false positive, **TF-054** |
| REQ-UI-029 repricing cards | Implemented | Needs re-verify | tests pass; parity flags the drift table's **pager** (566 rows → paged; the mockup draws 3 rows and no pager). Needs your decision |
| REQ-UI-033 quotable banner | Needs re-verify | Needs re-verify | tests pass and the banner reads QUOTABLE. Parity still reports (a) the new green "0 differences" badge as neutral in dark mode (computed colour hue 155, low chroma) and (b) the compare-output box as "clipped" because it scrolls — **TF-053** |

Rows touched along the way:

- **REQ-UI-001 (login)** was reopened and fixed. Verified.
- **REQ-FN-021 (skip-when-unchanged)** was reopened and fixed. TechieBlog showed `error` on Repos from an old failed attempt that a successful check never cleared. Verified.
- **REQ-UI-027 and REQ-UI-032** were reopened. The `/export` dataset table showed "—" for every imported source; it now shows `sha256 <8 chars>`. The sort arrows the mockups don't draw were removed. Both now pass their tests but fail parity on the pager and the badge colour above.

## 3. Numbers check

### What "counted from the files" means

A Python script reads each repository's `docs/metrics/*.jsonl` directly and shares no code with TfLens (`tests/.artifacts/uat-2026-10-12/count.py`). It applies the SCHEMA rules TfLens claims to apply:

- A record's time is its `ts`. Last 7 days means `ts ≥ now − 7 days`, the same rolling window the pages use.
- Live records only; `backfilled: true` never pools.
- A `run-void` removes the run it names, and a `miss-void` removes the miss it names.
- Misses are split by the kind of project (app, library, framework), as the page tabs are.
- A gate failure is any verdict other than `Verified`, and it is credited to its `gate` (`unattributed` when none is named).

TechieFlow's new fields (`v`, `ts` beside `ended`) and new run commands (`amend-docs`, `framework-reset`, `log-miss`, `fix-issues`, `triage-and-fix`) are read correctly. `ts` and `ended` differ on about half the runs in every repository, and TfLens dates a run by `ts`, as SCHEMA defines.

### Why each repository was checked in its own account

TfLens's pages pool all of a user's repositories within each project-kind tab (app, library, framework), and no page shows a 7-day figure for one repository. So each repository's `docs/metrics` folder was imported **alone** into test user 2 and its pages read in the browser, then removed. User 2 is empty again. Then all four were imported into user 1 and the pooled pages checked against the sum of the files.

### Per repository, on the final build (read 2026-10-12 11:47–11:50 UTC)

| Repo | Figure | TfLens | Counted from files | Match |
|---|---|---|---|---|
| Chatur | Runs, last 7 days | 28 | 28 | yes |
| Chatur | Misses, last 7 days — app tab | 71 | 71 | yes |
| Chatur | Gate failures caught by `acceptance` — app | 2 | 2 | yes |
| Chatur | Gate failures caught by `render` — app | 210 | 210 | yes |
| Chatur | Gate failures caught by `mockup-parity` — app | 622 | 622 | yes |
| TechieRag | Runs, last 7 days | 41 | 41 | yes |
| TechieRag | Misses, last 7 days — library tab | 18 | 18 | yes |
| TechieRag | Gate failures caught by `escaped` — app | 2 | 2 | yes |
| TechieRag | Gate failures caught by `escaped` — library | 20 | 20 | yes |
| TrBlazeUI | Runs, last 7 days | 30 | 30 | yes |
| TrBlazeUI | Misses, last 7 days — library tab | 22 | 22 | yes (was **23**, fixed: REQ-FN-143) |
| TrBlazeUI | Gate failures caught by `acceptance` — library | 1 | 1 | yes |
| TrBlazeUI | Gate failures caught by `escaped` — library | 102 | 102 | yes |
| TechieFlow | Runs, last 7 days | 40 | 40 | yes |
| TechieFlow | Misses, last 7 days — framework tab | 24 | 24 | yes |
| TechieFlow | Gate failures caught by `acceptance` — framework-requirement | 2 | 2 | yes |
| Sevak | — | not imported | — | not public on GitHub |

Every other gate reads 0 in TfLens and 0 in the files; those rows are left out. No figure sat within an hour of the 7-day edge, and the file counts were identical at the start and end of the read.

### User 1, pooled (six sources: the four above plus TechieBlog and TfLens, fetched)

18 of 18 figures match: 139 runs in the last 7 days; misses 71 (app), 40 (library) and 24 (framework); and all 14 gate rows (`tests/.artifacts/uat-2026-10-12/pooled-final.md`).

### The full parity compare, and what it found

The figures above are three of several hundred. The parity compare covers every figure the framework's own script (`tf-metrics.sh`) produces, and the first run gave **86 differences**. Each one was a TfLens bug or a stale input:

| Cause | Fix | Row |
|---|---|---|
| `miss-void` records (TechieFlow, from 22 September) were read as invalid lines, so withdrawn misses stayed in every figure | parsed, stored, and removed with their fixes and amends on every page and in the export | REQ-FN-143 |
| A second fix attempt in one fix run, and a second correction of one reviewed run, were merged into one record | each attempt and each correction is now kept as its own record | REQ-FN-144 |
| Gate records naming no requirement or no verdict were scored; the reference lists them as malformed | scored nowhere, and listed as malformed in the export | REQ-FN-145 |
| An import never recorded how many duplicate sessions it collapsed (Chatur: 5) | recorded | REQ-FN-146 |
| `claude-opus-5-5` had no price in TfLens; every repository's rate card prices it at 4 / 20 / 0.2 / 5 USD per million | rate added to the Anthropic provider | — |
| Three reason lists were compared by position, although their order is only the order each tool walked the repositories | compared as exact multisets in `tools/parity-compare.py` | — |

After the fixes: **0 differences, 47 allowed** (environment and documented additions). Parser version 1.2.0 → **1.3.0**.

## 4. Left running

TfLens runs at **http://localhost:5099**, started with `bash .tfcore/utils/tf-verify-boot.sh start --project src/TfLens/TfLens.csproj --port 5099 --config Release --environment Development`. The YOLO guard refuses a backgrounded `dotnet run`, and this script is the framework's own way to keep it up. It runs the published copy in `tests/.artifacts/verify/run-5099` against the same `src/TfLens` data and the same database. Stop it with `bash .tfcore/utils/tf-verify-boot.sh stop --port 5099`.

User 1 (`tflensdemo@techierathore.com`) holds six sources:

- **Imported:** Chatur, TechieRag, TrBlazeUI, TechieFlow.
- **Fetched:** TechieBlog, TfLens.

**The smoke checklist in `docs/TfLens-UsageGuide.md` is ready to run.**

## Decisions for you

1. **Chatur's `private` badge.** I recommend showing an imported source as public when GitHub says it is public. The risk in leaving it: on stage, a public repository reads `private`, which contradicts the 12 Oct public-only rule you will be stating. Quick alternative for the demo: remove Chatur and connect it with **Fetch via API**. GitHub may hold different bytes from your local folder, though.
2. **The `/routing` drift table pages its 566 rows; the mockup draws no pager.** I recommend drawing the pager in the mockup. Removing it would put 566 rows on one page.
3. **Sort arrows were removed** from the `/routing` and `/export` tables, because the approved mockups don't draw them (REQ-UI-027, REQ-UI-032). If you want sorting back, the mockups need it drawn.
4. **Four attribute-only annotations were added to mockups**, with the original files backed up in `tests/.artifacts/uat-2026-10-12/`:
   - `routing.html`: `data-tf-state` on the three non-default tab panels.
   - `harness.html`: `data-tf-sample` on the OpenCode verdict bar, which is sample data. User 1 has no OpenCode verdicts.
   - `export.html`: `data-tf-state="not-quotable"` on the banner, which the mockup draws in its NOT QUOTABLE state.

   Nothing drawn changed. Revert them if you would rather the gate grade those boxes.
5. **"Lekhak" text inside TechieFlow and TrBlazeUI records** stays visible in raw record views. Removing it would mean editing those repositories, which you ruled out.

## Evidence

All under `tests/.artifacts/uat-2026-10-12/`:

- Per-repo page reads before and after the fixes: `perrepo-before.json`, `perrepo-final.json`.
- File counts and tables: `files-final.json`, `table-final.md`, `pooled-final.md`.
- The parity run: `parity-reference.json`, `parity-tflens.json`, `parity-compare-recorded.txt`, plus the first failing compare `parity-compare-dry.txt`.
- The mockup and price-file backups.

The verify ledger is `docs/.last-verify.json`, and screenshots are under `tests/.artifacts/verify/screens/`.
