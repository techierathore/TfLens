# TfLens

A free, multi-user, **read-only** Blazor Server lens over the telemetry that
[TechieFlow](https://github.com/techierathore) and the AI-First-Playbook already emit.

You sign in through AppManager, connect your own **public** GitHub repos, and TfLens pulls the JSONL
streams those repos publish, archives them byte-for-byte, parses them into PostgreSQL scoped to your
user id, and renders five report pages — Coverage, Three questions, Harness comparison, Routing &
economics, Snapshot export — each with a Framework switch (TechieFlow | Playbook).

TfLens never writes to a repository, never accepts an inbound event, and never pools a figure across
a provenance boundary. The product's dangerous failure mode is a *plausible wrong number*, so the
provenance rules are enforced by the shape of the result type and every exported figure is gated
behind the [parity procedure](#parity-procedure).

---

## Table of contents

1. [Out of scope](#out-of-scope)
2. [Prerequisites](#prerequisites)
3. [Configuration — environment variables](#configuration--environment-variables)
4. [Run with Docker Compose](#run-with-docker-compose)
5. [Run locally with `dotnet run`](#run-locally-with-dotnet-run)
6. [Command verbs](#command-verbs)
7. [Parity procedure](#parity-procedure)
8. [Health check](#health-check)
9. [Data on disk](#data-on-disk)
10. [Repository layout](#repository-layout)
11. [Tests](#tests)
12. [Documentation map](#documentation-map)

---

## Out of scope

The following are **explicitly out of scope** for this release. Reproduced verbatim from
`docs/TfLens-BRD.md` §3:

- Any capture or ingestion endpoint; OTLP; per-machine agents.
- Writing anything to any repository, ever.
- VPS / infra configuration (supplied separately).
- Private GitHub repos (this release is public-repo-only; a per-user PAT is a later release).
- AppManager licensing, subscriptions, feature flags, payments, issues — none are called.
- GitHub SSO — **deferred to Phase 2** (BRD-94): AppManager has no external-login endpoint, so it needs a bridge or an AppManager change first.
- Roles beyond `Manager`; sharing a user's reports with another user.
- Any estimate presented as a measurement: no rate-card dollars anywhere except the explicitly labelled repricing figure.

---

## Prerequisites

| Path | What you need |
|------|---------------|
| Docker Compose (recommended) | Docker Engine 24+ with the Compose v2 plugin (`docker compose version`). Nothing else — the image is built from this repo. |
| Local `dotnet run` | .NET SDK **10.0** (`dotnet --version` → `10.*`), plus a reachable PostgreSQL **16**. The compose file can supply just the database (see below). |
| Parity runs | Python 3 (for `tools/parity-compare.py`) and a checkout of TechieFlow so `.tfcore/telemetry/tf-metrics.sh` is available. |

An AppManager account is required to sign in. TfLens holds no users and no passwords — registration,
login, password reset and password change all go to AppManager
(`https://appmgrapi.techierathore.com`, Application Id 1). Every TfLens user is a `Manager`; no
licensing, feature-flag, payment or issue endpoint is ever called.

---

## Configuration — environment variables

Every setting reaches the app through **PascalCase environment variables** with no separators —
`TfLensDbConnection`, not `TFLENS_DB_CONNECTION` and not `TfLens__DbConnection`. A custom
configuration provider maps `TfLens<Name>` onto `TfLens:<Name>`, so application code never reads the
environment directly and **no secret is ever read from a file in the repository**.

Copy `.env.example` to `.env` and fill it in. `.env` is gitignored and must never be committed.

### Secrets

| Variable | Required | What it is |
|----------|----------|------------|
| `TfLensDbConnection` | **Yes** | Npgsql connection string, e.g. `Host=postgres;Port=5432;Database=tflens;Username=tflens;Password=…`. Startup fails if it is missing or the database is unreachable. |
| `TfLensAppManagerApiKey` | Pair | AppManager API key, sent as `X-Api-Key`. |
| `TfLensAppManagerApiSecret` | Pair | AppManager API secret, sent as `X-Api-Secret`. |
| `TfLensGitHubToken` | No | Fine-grained, **contents-read-only** GitHub PAT. It raises the read rate limit from 60/h to 5,000/h and grants no additional repository access. Its absence changes no behaviour. |

**"Pair" means whole or not at all.** The AppManager API-key headers are optional on the AppManager
side — the client also sends `applicationId` in every request body, which resolves the application on
its own. What AppManager does *not* tolerate is half a pair: a key without its secret is rejected
with `INVALID_API_KEY` on every call. TfLens therefore refuses to start when exactly one of the two
is set (see `DECISIONS.md` → D-006).

### Non-secret settings (all have working defaults)

| Variable | Default | What it does |
|----------|---------|--------------|
| `TfLensAppManagerBaseUrl` | `https://appmgrapi.techierathore.com` | AppManager API root. |
| `TfLensAppManagerAppId` | `1` | AppManager Application Id. |
| `TfLensPollIntervalMinutes` | `15` | How often the background poller syncs every user's repos. |
| `TfLensDataRoot` | `data` (`/app/data` in the container) | Root of the persistent volume. Governs the raw archive, the reports folder, `prices.json` and `parity-last.json`. |

### Compose-only variables

| Variable | Default | What it does |
|----------|---------|--------------|
| `TfLensDbPassword` | *(required)* | Password for the compose `postgres` service; also interpolated into `TfLensDbConnection`. |
| `TfLensHostPort` | `8080` | Host port the app is published on. |

Nothing sensitive is baked into the image, printed to a log, rendered in the UI or written to an
export. See `DECISIONS.md` → D-008.

---

## Run with Docker Compose

```bash
cp .env.example .env
$EDITOR .env                 # at minimum, set TfLensDbPassword

docker compose up --build -d
docker compose logs -f tflens
```

Then open <http://localhost:8080> and sign in (or register) with an AppManager account.

What `docker compose up` does, from a clean state and with no manual steps:

1. Starts **PostgreSQL 16** (`tflens-postgres`) with its data directory on the named volume `pgdata`
   and waits for `pg_isready`.
2. Builds the multi-stage image and starts `tflens`, which applies `database/001-schema.sql`
   **idempotently** at startup — there is no migration framework, and re-running the script on an
   existing database is a no-op.
3. Pings the database. If it is unreachable, or a required setting is missing, the process logs a
   **redacted** reason and exits non-zero rather than serving a degraded app.
4. Publishes `/healthz` for the compose healthcheck.

Two bind mounts hold everything persistent:

- `./data` → `/app/data` — the raw JSONL archive, reports, `prices.json`, `parity-last.json`.
- `./logs` → `/app/logs` — the rolling Serilog file sink (`tflens-<date>.log`, daily, 14 retained).

Postgres is **not** published outside the compose network in the production deploy. The local
`docker-compose.override.yml` (which Compose merges automatically) publishes `5433:5432` so a
host-run `dotnet run` and the test suite can reach the same database. To deploy without that
override:

```bash
docker compose -f docker-compose.yml up -d --build
```

### Build the image on its own

```bash
docker build -t tflens:latest .
```

---

## Run locally with `dotnet run`

Start just the database from compose, then run the app on the host:

```bash
docker compose up -d postgres          # publishes 5433 via docker-compose.override.yml

export TfLensDbConnection="Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev"
export TfLensAppManagerApiKey=""       # both blank is a valid configuration; half a pair is not
export TfLensAppManagerApiSecret=""

dotnet run --project src/TfLens
```

The app listens on <http://localhost:5014> under the `http` launch profile. HTTPS is terminated by
the reverse proxy in a real deployment, never by the app — `ForwardedHeaders` is configured so the
scheme and client IP arriving from the proxy are honoured.

Build and test the whole solution:

```bash
dotnet build TfLens.slnx
dotnet test  TfLens.slnx
```

---

## Command verbs

The **same executable** serves the UI and runs the command verbs, so a headless run exercises exactly
the code path the pages use — there is no second implementation to drift.

| Verb | What it does |
|------|--------------|
| *(none)* | Serve the Blazor Server app. |
| `sync` | Run one headless sync pass over the connected repos and exit with a status reflecting the per-repo outcomes. |
| `rebuild` | Truncate every stream table, re-apply `database/001-schema.sql`, and replay every archived raw file in `(user, repo, sha-fetch-order)`. Reads **only** from `data/raw/`, never from the GitHub API. |
| `export` | Write the weekly snapshot pair — `snapshot.md` + `tflens.json` — for one user, framework and date. |

Options: `--user <id>` narrows a verb to one user (required for `export`), and
`--framework <techieflow\|playbook>` selects the provenance axis for `export` (default
`techieflow`).

### In a container

```bash
docker exec tflens dotnet TfLens.dll sync
docker exec tflens dotnet TfLens.dll sync    --user 2
docker exec tflens dotnet TfLens.dll rebuild
docker exec tflens dotnet TfLens.dll rebuild --user 2
docker exec tflens dotnet TfLens.dll export  --user 2 --framework techieflow
```

### From a published build

```bash
dotnet publish src/TfLens -c Release -o out
dotnet out/TfLens.dll sync
dotnet out/TfLens.dll rebuild
dotnet out/TfLens.dll export --user 2 --framework techieflow
```

### From the source tree

```bash
dotnet run --project src/TfLens -- sync
dotnet run --project src/TfLens -- rebuild
dotnet run --project src/TfLens -- export --user 2
```

`rebuild` is also available from the Coverage page as a guarded button; the button and the verb share
one implementation, and a rebuild run immediately after a live sync must produce **identical**
per-stream record counts.

---

## Parity procedure

Two independent implementations compute the same metrics from the same files: `tf-metrics.sh`
(existing, trusted) and TfLens (new, unproven). Correct implementations must agree exactly. **Any
disagreement is by definition a bug in TfLens** — the script is never "fixed" to match the app.

Run this before TfLens's export is used for any published number, and re-run it after **every**
parser or engine change.

1. **Fix the dataset.** Clone the same repos TfLens is configured to pull, checked out at the exact
   commit SHAs TfLens's `sync_state` shows for its last sync (also printed in the export's
   `per_repo`). Same data in, or the comparison is meaningless.

2. **Run the reference:**

   ```bash
   bash .tfcore/telemetry/tf-metrics.sh --rollup <repo1> <repo2> … --json > reference.json
   ```

3. **Run TfLens's export** for the same repos:

   ```bash
   docker exec tflens dotnet TfLens.dll export --user <id> --framework techieflow
   # → data/reports/<userId>/<date>/tflens.json
   ```

4. **Compare, key by key:**

   ```bash
   python3 tools/parity-compare.py reference.json data/reports/<userId>/<date>/tflens.json
   ```

   The script checks per-repo record counts per stream and backfilled counts; commit duplicates
   collapsed; the tainted-REQ set (identical set of IDs); first-pass rate, gate catch distribution
   and escape rate per `project_type`, live and backfilled separately; late-gate coverage
   (`ran` / `caught` per gate); every poolable metric; and every `insufficient data (n=…)` marker —
   the `n` must match, and a figure the reference refuses to print TfLens must also refuse to print.

5. **Zero tolerance.** Any mismatch fails. Debug TfLens until the diff is empty. The only acceptable
   permanent differences are metrics TfLens adds that the script does not compute (`extras`); those
   have no reference and are spot-checked by hand against raw JSONL once.

6. **Record the passing run** in `DECISIONS.md` and `data/parity-last.json`: date, commit SHAs of the
   dataset, `tf-metrics.sh` hash, TfLens parser version, and the compare script's output. That entry
   is the licence to trust the export.

**Standing rule after ship:** the weekly snapshot export is quotable only if the last parity run on
record postdates the last parser change. The `/export` page shows this as the quotable /
not-quotable banner.

---

## Health check

`/healthz` is the only anonymous non-auth route besides the sign-in pages. It reports **two facts and
nothing else** — database reachability and the age of the last successful sync:

```bash
curl -i http://localhost:8080/healthz
```

```json
{"status":"ok","database":"up","lastSuccessfulSyncAgeSeconds":412}
```

It returns `503` when the database is unreachable. It deliberately discloses no version, no
configuration, no repo names, no user data and no metrics. `lastSuccessfulSyncAgeSeconds` is `null`
when no repo has ever synced successfully.

---

## Data on disk

Everything under `TfLensDataRoot` (default `data/`, `/app/data` in the container) is user-scoped —
the user id is part of the **path**, not a filter applied when reading it:

```
data/
  raw/<userId>/<owner>__<name>/<stream>-<sha>.jsonl   raw archive, written verbatim before parsing
  reports/<userId>/<date>/snapshot.md                 weekly snapshot, human-readable
  reports/<userId>/<date>/tflens.json                 weekly snapshot, parity-comparable
  prices.json                                         editable rate card for counterfactual repricing
  parity-last.json                                    record of the last parity run
logs/
  tflens-<date>.log                                   rolling Serilog sink, daily, 14 files retained
```

The **raw archive is the source of truth**, not the database. The database is disposable: `rebuild`
drops every stream table and replays `data/raw/` to identical counts. Back up `data/`; you never need
to back up Postgres.

`data/` and `logs/` are gitignored.

---

## Repository layout

```
src/TfLens.Core/      engine, parser, store contracts, options — no web dependency at all
src/TfLens/           the only executable: Blazor Server + background sync + command verbs
database/001-schema.sql   idempotent PostgreSQL 16 schema, applied at every startup
tests/                unit, guardrail and integration tests
docs/                 BRD, Architecture, UI design, Usage guide, Checklist, Coding standards
tools/parity-compare.py   key-by-key diff against tf-metrics.sh --rollup --json
Dockerfile            multi-stage build → one image
docker-compose.yml    app + PostgreSQL 16
DECISIONS.md          decision log, dedupe keys, parser-version scheme, every parity run
```

`TfLens.Core` builds on `Microsoft.NET.Sdk` (not `.Web`) and references no ASP.NET or Blazor package,
so the engine and parser are driven by the CLI verbs and unit tests without a browser.

---

## Tests

```bash
dotnet test TfLens.slnx
```

Three groups:

- **`tests/TfLens.Core.Tests`** — engine and parser unit tests against checked-in fixture JSONL.
- **`tests/TfLens.Guardrails.Tests`** — cross-cutting guardrails that fail the build when an
  invariant is broken: `TfLens.Core` has no web dependency; no code reads the environment directly;
  no secret, token, PAT or connection string can be logged, rendered or exported; every store and
  engine method takes `userId` as a mandatory parameter.
- **`tests/TfLens.Integration.Tests`** — the cross-user isolation proof. It creates two users' worth
  of rows in a live PostgreSQL and asserts neither user can reach the other's repos, stream rows,
  sync state, raw-archive path or reports path. It needs a reachable database:

  ```bash
  export TfLensDbConnection="Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev"
  dotnet test tests/TfLens.Integration.Tests
  ```

Tests tagged `Category=Blocked` assert behaviour that depends on code not yet landed and are expected
to fail until it does; skip them with `--filter "Category!=Blocked"`.

---

## Documentation map

| Document | What is in it |
|----------|---------------|
| `docs/TfLens-BRD.md` | Business requirements, scope, the BRD ledger, the parity mandate. |
| `docs/TfLens-Architecture.md` | Component map, data model, runtime flows, the ADR log. |
| `docs/TfLens-Checklist.md` | The single source of truth for every requirement's status. |
| `docs/TfLens-UIDesign.md` | Screen-by-screen component map; mockups under `docs/mockups/`. |
| `docs/TfLens-UsageGuide.md` | Canonical test users and the walkthrough every smoke and UAT run follows. |
| `docs/TfLens-Coding-Standards.md` | Naming, logging and environment-variable conventions. |
| `DECISIONS.md` | Every decision that shapes the data, plus the record of each passing parity run. |

---

## Licence and posture

TfLens is read-only against every system it touches. It issues nothing but `GET` against the GitHub
API, holds no write scopes, exposes no ingestion or capture endpoint, and stores only what the
telemetry streams carry — ids, counts, durations, verdicts and short SHAs. No requirement text, no
commit subject lines, nothing from `src/`.
