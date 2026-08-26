# TfLens — Architecture

**Last updated:** 2026-08-26
**Status:** Target (greenfield)

<!-- AGENT-ONLY AUTHORING NOTES — never render as visible text.
  DEPTH MANDATE: human document; every non-trivial module gets prose; every significant runtime
  flow beyond the primary path gets its own diagram.
  MERMAID MANDATE: html-render-shell.md §5.5 — quote every label, never use `end` as a node id.
-->

## Table of Contents

1. [Tech stack](#tech-stack)
2. [Component map](#component-map)
3. [Data flow — primary path](#data-flow-primary-path)
4. [Module responsibilities](#module-responsibilities)
5. [Runtime flows beyond the primary path](#runtime-flows-beyond-the-primary-path)
6. [Data model](#data-model)
7. [Metrics engine — parity with tf-metrics.sh](#metrics-engine-parity-with-tf-metrics-sh)
8. [Cross-cutting concerns](#cross-cutting-concerns)
9. [Deployment architecture](#deployment-architecture)
10. [Architectural decisions (ADR-style log)](#architectural-decisions-adr-style-log)
11. [Target architecture (brownfield only — if enhancement changes structure)](#target-architecture-brownfield-only-if-enhancement-changes-structure)
12. [Open questions / risks](#open-questions-risks)
13. [Sources harvested](#sources-harvested)

## 1. Tech stack

| Layer | Choice | Version | Notes |
|-------|--------|---------|-------|
| Runtime | .NET | 10 (LTS) | SDK 10.0.302 present on the dev machine. The brief asks for "current LTS"; .NET 10 is the LTS line as of 2026-08. |
| UI | Blazor Server (Interactive Server render mode) + TrBlazeUI | TrBlazeUI.Components 1.0.7 / Primitives 1.0.3 | Single web head `src/TfLens`. Dogfoods TrBlazeUI where it fits (shell, nav, cards, grid, badges, alerts, dialogs). |
| Data access | Dapper + Microsoft.Data.Sqlite | latest stable | Owner decision (day-1 kickoff, 2026-08-26). Hand-written DDL; no migrations — the store is disposable and rebuilt from `data/raw/`. |
| DB | SQLite | file `data/tflens.db` | One table per stream + `sync_state` + Playbook tables. Never the source of truth — raw JSONL is. |
| GitHub access | `HttpClient` against the GitHub REST API (`/repos/{owner}/{repo}/commits`, `/contents`) | — | Fine-grained read-only PAT (`contents:read`). No Octokit dependency (ADR-004). |
| Auth | ASP.NET Core cookie authentication, single configured user | — | Username + PBKDF2 password hash from configuration/env. No identity DB. |
| Logging | Serilog, rolling file sink under `logs/` + console | — | Standing TechieFlow NFR; wired first in `Program.cs`. |
| Background work | `BackgroundService` (`RepoSyncService`) with `PeriodicTimer` | — | Poll interval from config; manual "Sync now" triggers the same code path. |
| Tests | xUnit + fixture JSONL under `tests/TfLens.Core.Tests/Fixtures/` | — | Parity fixtures mirror real stream shapes; metrics engine tested without the web host. |
| Parity tooling | `tools/parity-compare.py` (Python 3, stdlib only) | — | Same runtime as `tf-metrics.sh`; key-by-key JSON compare. |
| Container | Docker (Linux), volume-mounted `data/` + `logs/` | — | Infra/VPS config is supplied separately and out of scope. |
| Vector store / RAG | none | — | No AI features in TfLens. |

## 2. Component map

```mermaid
flowchart TB
  subgraph GH["GitHub (read-only, PAT contents:read)"]
    R1["TechieFlow-managed repos<br/>docs/metrics/*.jsonl"]
    R2["Playbook-managed repos<br/>verification/telemetry/events.ndjson"]
  end

  subgraph Head["src/TfLens — Blazor Server head (.NET 10)"]
    Sync["RepoSyncService<br/>(BackgroundService + Sync now)"]
    Auth["Cookie auth<br/>(single user)"]
    subgraph Pages["Report pages (TrBlazeUI)"]
      P1["Coverage / health  /"]
      P2["Three questions  /three-questions"]
      P3["Harness comparison  /harness"]
      P4["Routing & economics  /routing"]
      P5["Snapshot export  /export"]
      P6["Playbook  /playbook"]
    end
    CLI["Command verbs<br/>rebuild · sync · export"]
  end

  subgraph Core["src/TfLens.Core — engine (no UI, no web deps)"]
    Fetch["GitHubStreamFetcher"]
    Parse["StreamParser + Dedupe"]
    PbParse["PlaybookAdapter (Phase 3)"]
    Store["SqliteStore (Dapper)"]
    Metrics["MetricsEngine<br/>(port of tf-metrics.sh analyse)"]
    Extra["ExtraMetrics<br/>harness · routing · repricing"]
    Export["SnapshotExporter<br/>markdown + JSON"]
  end

  subgraph Disk["Volume: data/"]
    Raw[("data/raw/&lt;repo&gt;/&lt;stream&gt;-&lt;sha&gt;.jsonl")]
    DB[("data/tflens.db")]
    Rep[("data/reports/&lt;date&gt;/")]
    Prices[("data/prices.json")]
  end

  R1 --> Fetch
  R2 --> Fetch
  Sync --> Fetch
  CLI --> Sync
  CLI --> Export
  Fetch --> Raw
  Fetch --> Parse
  Fetch --> PbParse
  Parse --> Store
  PbParse --> Store
  Store --> DB
  Raw -. "rebuild" .-> Parse
  Pages --> Metrics
  Pages --> Extra
  Metrics --> Store
  Extra --> Store
  Extra --> Prices
  P5 --> Export
  Export --> Metrics
  Export --> Rep
  Auth --> Pages
```

Two projects, one head. `TfLens.Core` holds everything that must be testable and parity-checked without a browser: fetching, parsing, dedupe, storage, the metrics engine, and the exporter. `TfLens` is the only executable: it hosts Blazor Server, the background sync, cookie auth, and the three command verbs (`rebuild`, `sync`, `export`) so that a Docker `exec` or a parity run never needs a second image (ADR-005).

## 3. Data flow — primary path

The primary path is "owner opens a report page". Every figure on every page is computed at request time from the stream tables; nothing derived is stored.

```mermaid
sequenceDiagram
  actor O as Owner
  participant B as Blazor page (TrBlazeUI)
  participant A as Cookie auth
  participant M as MetricsEngine (Core)
  participant S as SqliteStore (Dapper)
  participant DB as data/tflens.db
  O->>B: GET /three-questions
  B->>A: authenticated?
  A-->>B: yes (cookie) / redirect /login
  B->>M: Analyse(repos, filters)
  M->>S: ReadStream("gates"), ReadStream("runs"), ...
  S->>DB: SELECT * FROM Gate ... (per repo)
  DB-->>S: rows (+ Overflow JSON)
  S-->>M: typed records
  M->>M: segment live vs backfilled, per project_type; taint set; MIN_N guard
  M-->>B: AnalysisResult (figures or InsufficientData(n))
  B-->>O: render cards / grid / labelled columns
```

The engine never receives a "pool everything" option: `Analyse` returns a structure keyed `live[project_type]`, `backfilled[project_type]`, `pooled` — the same shape as `tf-metrics.sh --rollup --json` — so a page cannot even ask for a merged first-pass rate (§7).

## 4. Module responsibilities

| Module | Responsibility | Depends on |
|--------|----------------|------------|
| `src/TfLens` | Blazor Server head: pages, layout, cookie auth, `RepoSyncService`, command verbs, Serilog wiring, `prices.json` editor | TfLens.Core, TrBlazeUI |
| `src/TfLens.Core/GitHub` | `GitHubStreamFetcher` — latest-SHA-touching-path lookup, whole-file fetch, raw archive to `data/raw/` | HttpClient |
| `src/TfLens.Core/Parsing` | `StreamParser` — JSONL → typed records, schema-v check, overflow capture; `Dedupe` — natural-key rules per stream | (none) |
| `src/TfLens.Core/Storage` | `SqliteStore` — DDL, Dapper CRUD, `sync_state`, idempotent upsert, `Rebuild()` | Dapper, Microsoft.Data.Sqlite |
| `src/TfLens.Core/Metrics` | `MetricsEngine` — the `analyse()` port; `ExtraMetrics` — harness comparison, routing drift, counterfactual repricing; `InsufficientData` type | Storage |
| `src/TfLens.Core/Export` | `SnapshotExporter` — markdown + machine-readable JSON (`tflens.json`) to `data/reports/<date>/` | Metrics |
| `src/TfLens.Core/Playbook` | `PlaybookAdapter` — `events.ndjson` → `PbEvent` table; phase totals; `parentID` split (Phase 3) | Storage |
| `tests/TfLens.Core.Tests` | xUnit: parser, dedupe, engine (fixture-driven, incl. provenance-separation tests) | TfLens.Core |
| `tools/parity-compare.py` | Key-by-key compare of `reference.json` (tf-metrics.sh) vs `tflens.json`; non-zero exit on any diff | Python 3 |

**`GitHubStreamFetcher`.** For each configured repo it asks the REST API for the newest commit touching the telemetry path on the configured branch (`GET /repos/{o}/{r}/commits?sha={branch}&path=docs/metrics&per_page=1`). If that SHA equals `sync_state.LastSha`, the repo is skipped — no file traffic at all. Otherwise it fetches each stream file at that exact SHA (`GET /repos/{o}/{r}/contents/docs/metrics/{stream}.jsonl?ref={sha}` with `Accept: application/vnd.github.raw`), writes the bytes verbatim to `data/raw/{owner}__{repo}/{stream}-{sha}.jsonl` **before** parsing, and only then hands the text to the parser. A 404 on a stream file is a legitimate "stream absent" (recorded as zero records), not an error. The fetcher is structurally read-only: it holds no write scopes, and no code path issues anything but GET.

**`StreamParser` + `Dedupe`.** Line-by-line `System.Text.Json` parse; a malformed line is counted and skipped (mirrors `read_stream` in the reference, which logs and skips). Known fields map to typed columns named exactly as SCHEMA.md; any property not in the known set for that stream (or any record with `v > 1`) is preserved in the `Overflow` JSON column. The dedupe keys are the natural identities the brief fixes: `commits` on `sha` (per repo — two repos may share a short sha), `sessions` keep-highest-`output_tokens`-then-latest-`ts` per `session_id`, `runs` on `ts+app+cmd`, `gates` on `ts+app+req_id+run_id`. Re-parsing the same raw file is therefore a no-op.

**`SqliteStore`.** Owns the DDL (§6), opens one connection per unit of work, and exposes stream reads per repo plus `sync_state`. `Rebuild()` drops every table, recreates the schema, and replays every file under `data/raw/` in `(repo, sha-commit-order)` — the raw archive is the rebuild source, never the API.

**`MetricsEngine`.** A field-for-field port of `analyse()` in `tf-metrics.sh` (§7). Its public result type has no "total" slot: figures live under `Live[projectType]`, `Backfilled[projectType]`, and `Pooled`. Any figure with fewer than `MinN = 3` supporting records is an `InsufficientData(n)` value, which the UI renders as text, never as a number.

**`ExtraMetrics`.** The metrics the reference does not compute — per-harness volumes/tokens/verdict mix (dollars only for `harness == "opencode"`), routing drift (`routed:false`, `tier_model` vs `model`), tokens by model, and counterfactual repricing from `data/prices.json` (labelled *estimate* everywhere). These have no parity oracle and are spot-checked by hand against raw JSONL once (BRD §9 F-PARITY).

**`SnapshotExporter`.** Writes `data/reports/<yyyy-MM-dd>/snapshot.md` (human) and `tflens.json` (machine, same key layout as `tf-metrics.sh --rollup --json` plus an `extras` object) — the diffable numbers for the plan's Numbers table and B3, with the parity stamp (last passing run date + dataset SHAs) embedded.

**`PlaybookAdapter` (Phase 3).** Separate tables (`PbEvent`, `PbSyncState`), separate page. Phase-gates (`phase_gate`) and TechieFlow assertion-gates (`gate`) never share a column or chart (SCHEMA.md §11). Built as schema-discovery: the first task is to parse the real `events.ndjson` and record the observed field names in DECISIONS.md before any chart exists.

## 5. Runtime flows beyond the primary path

### 5.1 Repo sync (background + Sync now)

```mermaid
sequenceDiagram
  participant T as PeriodicTimer / Sync now button
  participant SS as RepoSyncService
  participant F as GitHubStreamFetcher
  participant GH as GitHub REST API
  participant Raw as data/raw/
  participant P as StreamParser + Dedupe
  participant S as SqliteStore
  T->>SS: tick / click
  loop per configured repo
    SS->>F: LatestShaAsync(owner, repo, branch, path)
    F->>GH: GET /commits?sha=branch&path=docs/metrics&per_page=1
    GH-->>F: sha
    alt sha == sync_state.LastSha
      SS->>S: touch LastSyncTs (skipped)
    else changed
      loop per stream runs/gates/sessions/commits
        F->>GH: GET /contents/docs/metrics/{stream}.jsonl?ref=sha (raw)
        GH-->>F: bytes or 404
        F->>Raw: write {stream}-{sha}.jsonl verbatim
        F->>P: parse(text)
        P->>S: upsert by natural key (idempotent)
      end
      SS->>S: sync_state = {sha, ts, per-stream counts}
    end
  end
  SS-->>T: SyncReport (per repo: skipped / updated / error)
```

Errors are per-repo and non-fatal: a 401/403/404/network failure is written to that repo's `sync_state.LastError` and surfaced on the Coverage page; the other repos still sync. Nothing is ever written back to any repository.

### 5.2 Rebuild from raw

```mermaid
flowchart LR
  A["rebuild verb / button"] --> B["Drop all tables"]
  B --> C["Create schema (DDL)"]
  C --> D["Enumerate data/raw/*/*.jsonl"]
  D --> E["Order by repo, then SHA fetch order"]
  E --> F["Parse + dedupe + upsert"]
  F --> G["Recompute sync_state counts"]
  G --> H["Report: files replayed, records, duplicates collapsed"]
```

### 5.3 Login (single-user cookie)

```mermaid
sequenceDiagram
  actor O as Owner
  participant L as /login page
  participant A as AuthService
  participant C as Cookie middleware
  O->>L: username + password
  L->>A: Verify(username, password)
  A->>A: constant-time compare vs PBKDF2 hash from config
  alt valid
    A-->>L: ok
    L->>C: SignIn (cookie, sliding 12h, HttpOnly, Secure)
    C-->>O: redirect to /
  else invalid
    A-->>L: fail (logged, no detail)
    L-->>O: generic error
  end
```

### 5.4 Weekly snapshot export

```mermaid
flowchart LR
  A["Export button or export verb"] --> B["MetricsEngine.Analyse(all repos)"]
  B --> C["ExtraMetrics (harness, routing, repricing)"]
  C --> D["Read parity stamp from DECISIONS.md section or data/parity-last.json"]
  D --> E["Write data/reports/&lt;date&gt;/snapshot.md"]
  D --> F["Write data/reports/&lt;date&gt;/tflens.json"]
  E --> G["Show download links + quotable / not-quotable banner"]
  F --> G
```

## 6. Data model

One table per stream, columns named exactly as SCHEMA.md fields (PascalCase per Coding Standards maps 1:1 — `req_id` → `ReqId`, etc.; the JSON→column mapping table lives in `StreamParser`). Every stream table carries `Repo` (owner/name), `SourceSha`, and `Overflow` (JSON text of unknown fields).

```mermaid
erDiagram
  SyncState {
    string Repo PK
    string Kind
    string Branch
    string LastSha
    string LastSyncTs
    string LastError
    int RunsCount
    int GatesCount
    int SessionsCount
    int CommitsCount
  }
  Run {
    string Repo
    string SourceSha
    int V
    string Ts
    string App
    string ProjectType
    bool ProjectTypeInferred
    bool Backfilled
    string Harness
    string Cmd
    string Mode
    string Started
    string Ended
    int DurationS
    string ReqsTouched
    int ReqsCount
    string Subagents
    int FilesWritten
    string BuildResult
    string Tier
    string TierModel
    string Model
    string Models
    bool Routed
    int TokensIn
    int TokensOut
    int TokensCacheRead
    int TokensCacheWrite
    real CostUsd
    string TokensScope
    int Attempt
    string Overflow
  }
  Gate {
    string Repo
    string SourceSha
    int V
    string Ts
    string App
    string ProjectType
    bool ProjectTypeInferred
    bool Backfilled
    string Inferred
    string Harness
    string RunId
    string ReqId
    string ReqClass
    int Attempt
    string Verdict
    string Gate
    string GatesRun
    string FailureClass
    string PriorVerdict
    string Proof
    string Overflow
  }
  Session {
    string Repo
    string SourceSha
    int V
    string Ts
    string App
    string ProjectType
    string Harness
    string SessionId
    string Model
    int DurationS
    int InputTokens
    int OutputTokens
    int CacheReadTokens
    int CacheCreationTokens
    real CostUsd
    string Overflow
  }
  Commit {
    string Repo
    string SourceSha
    int V
    string Ts
    string App
    string ProjectType
    string Sha
    int Files
    int Insertions
    int Deletions
    string SubjectPrefix
    string Branch
    string Overflow
  }
  PbEvent {
    string Repo
    string SourceSha
    string Ts
    string EventType
    string PhaseGate
    string SessionId
    string ParentId
    int Tokens
    real CostUsd
    string Overflow
  }
  SyncState ||--o{ Run : "Repo"
  SyncState ||--o{ Gate : "Repo"
  SyncState ||--o{ Session : "Repo"
  SyncState ||--o{ Commit : "Repo"
  SyncState ||--o{ PbEvent : "Repo"
```

Unique indexes implement the dedupe keys: `UcCommitRepoSha (Repo, Sha)`, `UcRunIdentity (Repo, Ts, App, Cmd)`, `UcGateIdentity (Repo, Ts, App, ReqId, RunId)`; sessions are collapsed in the parser (keep max `OutputTokens`, tie → latest `Ts`) and stored with `UcSessionRepoId (Repo, SessionId)`. `PbEvent` columns are provisional until the real file is parsed (Phase 3 schema-discovery). Nullable-vs-absent is preserved: an absent optional field is stored as `NULL`, never `0` (SCHEMA.md §2.5).

## 7. Metrics engine — parity with tf-metrics.sh

The reference script is the specification. The port keeps its names so a diff reads naturally:

| tf-metrics.sh | TfLens.Core | Rule carried |
|---|---|---|
| `read_stream` | `SqliteStore.ReadStream` | invalid lines skipped and counted |
| `dedupe_commits` | `Dedupe.Commits` | per repo, first wins, count collapsed |
| `seg()` | `Segment.ByProjectType` | `project_type_inferred` → `unclassified`, never `app` |
| `tainted = {req_id of any backfilled}` | `TaintSet` | excluded from live first-pass; listed on screen |
| `first_pass_rate` | `FirstPassRate` | `attempt==1 && verdict==Verified` ÷ distinct eligible `req_id`; `MinN` guard |
| `gate_distribution` | `GateDistribution` | over `verdict ∉ {Verified, Done (pre-existing)}`; `escaped` its own row; `unattributed` bucket |
| `late_gate_coverage` | `LateGateCoverage` | `LATE_GATES = { perf: 2026-08-10 }`; `ran` = `gates_run` contains; `caught` beside it |
| `escape_rate` | `EscapeRate` | REQs with `gate=="escaped"` ÷ REQs with any failure; `MinN` |
| pooled block | `Pooled` | rework ratio, batch median, throughput median (REQs/hour, 2 dp), tokens total (sessions in+out), tokens per Verified (1 dp), cadence, `cost_usd: null` |
| `pct()` | `Pct` | `"—"` when denominator 0; `%.0f%%` otherwise |
| `MIN_N = 3` | `MetricsEngine.MinN` | constant, no configuration key |

Two guarantees are enforced by type, not by discipline: (1) `AnalysisResult` has no member that could hold a cross-`project_type` or cross-provenance rate, and (2) `Figure` is a discriminated union of `Value` / `InsufficientData(n)` / `NotApplicable` — a page binding a `Figure` cannot print a number for an `InsufficientData` case. A unit test asserts the engine output on the fixture set equals a checked-in `reference.json` produced by the script.

## 8. Cross-cutting concerns

- **Logging** — Serilog, `WriteTo.File("logs/tflens-.log", rollingInterval: Day, retainedFileCountLimit: 14)` + console, wired before the host builds; `ILogger<T>` in app code; sync outcomes logged per repo (IDs and counts only — never file contents).
- **Error handling** — per-repo sync errors captured to `sync_state.LastError` and rendered on Coverage; Blazor error boundary on each page; unhandled exceptions → `Log.Fatal` at the head boundary.
- **Auth** — cookie auth for every page and the export endpoint; `/login` and `/healthz` are the only anonymous routes; antiforgery on the login form.
- **Secrets** — PAT and auth credentials from environment / user-secrets via the PascalCase env-var provider (`TfLensGitHubToken`, `TfLensAuthUser`, `TfLensAuthPasswordHash`). Never in `appsettings.json`, never in the repo, never logged.
- **Privacy** — TfLens stores and shows only what the streams carry (SCHEMA.md §9). The overflow column is displayed nowhere; it exists for rebuild fidelity and the "unknown fields" report.
- **Caching** — `AnalysisResult` memoised per `(sync version, filter)` in `IMemoryCache`; invalidated on every completed sync or rebuild.
- **Health** — `/healthz` reports DB reachable + last successful sync age; no metrics exposed there.
- **Telemetry** — none outbound. TfLens itself is a TechieFlow-managed repo, so its own `docs/metrics/` streams are emitted by the framework as usual (and it may read them like any other repo).

## 9. Deployment architecture

```mermaid
flowchart LR
  Dev["Dev machine<br/>dotnet run"] --> Img["Docker image<br/>(multi-stage, .NET 10)"]
  Img --> VPS["VPS container<br/>(infra config out of scope)"]
  VPS --> Vol[("Volume: data/ + logs/")]
  VPS --> GH["GitHub REST API<br/>(outbound HTTPS only)"]
  Owner(["Owner browser"]) -- "HTTPS + cookie" --> VPS
```

Single container, single process. Persistent state is exactly two directories on one volume: `data/` (`tflens.db`, `raw/`, `reports/`, `prices.json`) and `logs/`. The image never contains the PAT; it arrives as an environment variable. No inbound endpoint exists other than the authenticated UI and `/healthz`.

## 10. Architectural decisions (ADR-style log)

- **ADR-001 — Blazor Server with TrBlazeUI as the only UI head.** Reason: the brief fixes Blazor Server; dogfooding TrBlazeUI is an explicit goal; a single-user dashboard has no WASM/offline need.
- **ADR-002 — SQLite via Dapper + Microsoft.Data.Sqlite, hand-written DDL, no migrations.** Reason: owner choice at kickoff; the store is disposable (rebuilt from `data/raw/`), so migrations add ceremony without value and the overflow JSON column stays an explicit `TEXT` column.
- **ADR-003 — No vector store / RAG.** Reason: TfLens has no AI features.
- **ADR-004 — Raw `HttpClient` against the GitHub REST API, not Octokit.** Reason: three GET calls per repo; a typed SDK adds a dependency and hides the `Accept: application/vnd.github.raw` header the whole-file fetch relies on.
- **ADR-005 — One executable (`src/TfLens`) hosting web + background sync + command verbs (`rebuild`, `sync`, `export`).** Reason: 1–2 day timebox and one Docker image; the verbs run via `dotnet TfLens.dll rebuild` / `docker exec`, and share the `TfLens.Core` engine so parity runs use exactly the code the pages use.
- **ADR-006 — `TfLens.Core` is UI-free and web-free.** Reason: the engine must be unit-testable against fixture JSONL and driven by the CLI verbs without a browser; it also keeps the parity surface (engine output) identical between the export and the pages.
- **ADR-007 — Provenance rules are encoded in the result type (`AnalysisResult` has no total slot; `Figure` carries `InsufficientData`).** Reason: the brief demands "no flag to disable"; a shape that cannot express the forbidden number is stronger than a check.
- **ADR-008 — Parity compare script in Python (`tools/parity-compare.py`).** Reason: it runs where `tf-metrics.sh` runs (Python 3 already required), needs no build, and compares `reference.json` vs `tflens.json` key-by-key.
- **ADR-009 — `data/prices.json` is the only editable input; repricing is labelled *estimate* in every rendering and export.** Reason: SCHEMA.md §4 forbids presenting a rate-card figure as a measurement; the label is part of the contract.
- **ADR-010 — Playbook adapter is separate tables + separate page, built schema-discovery-first.** Reason: SCHEMA.md §11 (`gate` vs `phase_gate` are different axes); no sample file exists yet (kickoff answer), so the adapter's first task is to parse the real file and record the field names.

## 11. Target architecture (brownfield only — if enhancement changes structure)

Not applicable — greenfield; §2 is the target.

## 12. Open questions / risks

- **Schema v=2 arrival.** Unknown fields land in `Overflow` and are listed on the Coverage page ("fields observed that SCHEMA.md doesn't document"), but a *renamed* or *re-typed* known field would silently fall into the overflow and drop out of the metrics. First thing that breaks: any figure depending on the renamed field, with no error. Mitigation: a per-sync "unknown fields" report + a hard warning when `v > 1` is seen.
- **Playbook `events.ndjson` shape is unknown** — `PbEvent` columns are provisional (ADR-010).
- **GitHub rate limits / PAT expiry.** A fine-grained PAT has a maximum lifetime; expiry shows up as 401s on the Coverage page. Poll interval defaults to 15 minutes; 5 repos × 5 calls is far below the 5,000/hour authenticated limit.
- **Reference drift.** `tf-metrics.sh` can change (it is part of the framework and is refreshed by `update-framework.sh`). The parity procedure re-runs after every parser change *in TfLens*, but a reference change also invalidates the last parity stamp — record the script's own hash in the parity entry.
- **Short-SHA collision across repos** is handled (dedupe per repo), but the same repo cloned under two names in config would double-count; config validation rejects duplicate `owner/name`.

## 13. Sources harvested

- `docs/TfLens-Project-Brief.md` (v2) — concept, phases, constraints, parity procedure, definition of done. Superseded by this document + the BRD; archived to `docs/OldDocs/`.
- `.tfcore/telemetry/SCHEMA.md` (schema v=1) — field names, enums, provenance rules.
- `.tfcore/telemetry/tf-metrics.sh` — reference implementation of every reporting rule (the parity oracle).
- `docs/ravi-90day-positioning-plan-v2.4.2.md` — A-V / A0′ / B1 / B3 context only; stays in `docs/` (independently authoritative).
