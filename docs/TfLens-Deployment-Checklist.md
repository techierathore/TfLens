# TfLens — Deployment Checklist

Everything needed to stand TfLens up on a new machine, in order, with the reason each step exists. Work
top to bottom; every section ends with something you can check rather than assume.

**Last updated:** 2026-08-27
**Audience:** whoever deploys and operates TfLens (today: the owner)

TfLens is one process plus a PostgreSQL 16 database. It is **read-only against every external system** —
it never writes to GitHub, and it never calls AppManager's licensing, feature-flag, payment or issue
services. Its persistent state is two directories, `data/` and `logs/`; the database is disposable and
can be rebuilt from `data/` at any time (`DECISIONS.md` D-002).

---

## Table of contents

1. [Prerequisites](#1-prerequisites)
2. [Configuration and secrets](#2-configuration-and-secrets)
3. [`TfLensGitHubToken` — create it first](#3-tflensgithubtoken--create-it-first)
4. [AppManager credentials](#4-appmanager-credentials)
5. [Deploy with Docker Compose](#5-deploy-with-docker-compose)
6. [Deploy for local development](#6-deploy-for-local-development)
7. [First-run verification](#7-first-run-verification)
8. [Before quoting any number — the parity run](#8-before-quoting-any-number--the-parity-run)
9. [Operating commands](#9-operating-commands)
10. [Backup and restore](#10-backup-and-restore)
11. [Known constraints](#11-known-constraints)
12. [Upgrading](#12-upgrading)

---

## 1. Prerequisites

| Need | Version | Why |
|---|---|---|
| Docker + Compose | any current | The shipped deployment path (`docker-compose.yml`) |
| PostgreSQL | **16** | The schema uses `jsonb` and expression indexes; the compose file pins `postgres:16` |
| .NET SDK | **10.0.302+** | Local development and running the CLI verbs outside the container |
| Python 3 | any current | `tools/parity-compare.py` and the framework's `tf-metrics.sh` |
| Node 20 + Playwright | any current | Only for the verification harness; not needed to run the app |

A GitHub account able to create a personal access token — see §3.

☑ **Check:** `docker --version && docker compose version && dotnet --version && python3 --version`

---

## 2. Configuration and secrets

Every setting reaches the process through the **PascalCase environment-variable provider** — the name in
the environment is the option name prefixed with `TfLens`. Secrets are never read from a file in the
repository (BRD-8).

| Variable | Required? | What happens without it |
|---|---|---|
| `TfLensDbConnection` | **Yes** | **Startup fails.** Deliberate — a misconfigured deploy should fail immediately, not at the first user's sign-in (BRD-9). |
| `TfLensAppManagerApiKey` | **Pair — yes in practice** | Password reset is dead. `/AuthSvc/forgot-password` and `/AuthSvc/reset-password` accept the application scope **only** from this header and answer `400 APPLICATION_ID_REQUIRED` without it. Login, registration and sessions still work. |
| `TfLensAppManagerApiSecret` | **Pair — yes in practice** | As above. **Set both or neither**: a half pair makes AppManager return `401 INVALID_API_KEY` on *every* call, so TfLens refuses to start (`DECISIONS.md` D-006). |
| `TfLensGitHubToken` | **Strongly recommended** | Sync is limited to **60 GitHub requests/hour** and cannot complete a pass over more than one or two repositories. See §3. |
| `TfLensDbPassword` | Compose only | Interpolated into `TfLensDbConnection` and into the `postgres` service. Compose refuses to start without it. |
| `TfLensAppManagerBaseUrl` | No | Defaults to `https://appmgrapi.techierathore.com`. |
| `TfLensAppManagerAppId` | No | Defaults to `1`. |
| `TfLensDataRoot` | No | Defaults to `data/`, resolved against the working directory. The container sets `/app/data`. |
| `TfLensPollIntervalMinutes` | No | Defaults to `15`. How often the background poller sweeps every user's repositories. |
| `TfLensStalenessDays` | No | Defaults to `7`. Drives the Coverage page's "this clone isn't pushing" warning. |
| `TfLensReferenceScriptPath` | No | Path to `tf-metrics.sh` for the quotable banner's script-hash check. The default probes `.tfcore/telemetry/tf-metrics.sh` from the working directory upward, which finds it in a normal checkout. Set it explicitly if the framework lives elsewhere. |
| `TfLensHostPort` | Compose only | Host port for the container. Defaults to `8080`. |

Put secrets in a `.env` file beside `docker-compose.yml`. **`.env` is gitignored — keep it that way.**

```dotenv
TfLensDbPassword=<choose a strong password>
TfLensAppManagerApiKey=ak_live_xxxxxxxxxxxxxxxxxxxx
TfLensAppManagerApiSecret=sk_live_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
TfLensGitHubToken=github_pat_xxxxxxxxxxxxxxxxxxxx
```

☑ **Check:** `grep -c . .env` returns 4, and `git check-ignore .env` prints `.env`.

---

## 3. `TfLensGitHubToken` — create it first

**This is the setting most likely to make a fresh deployment look broken.** Without it TfLens reads
GitHub anonymously, which GitHub caps at **60 requests per hour, per IP**. A single sync pass costs
several requests per repository (a commit-SHA lookup plus one fetch per stream), so connecting and
syncing four repositories exhausts the hour's quota outright. Repositories then show `error` with
`GitHub rate limit reached — try again in N minutes`, and the Coverage page reports failed syncs. The
app is behaving correctly; it has simply run out of anonymous quota.

With a token the limit is **5,000 requests/hour** — comfortably more than the poller will ever use.

### Creating the token

TfLens only ever reads **public** repositories, so the token needs no repository access at all beyond
public data, and **no write scope of any kind**. Either form works:

**Fine-grained token (preferred — least privilege):**

1. GitHub → **Settings → Developer settings → Personal access tokens → Fine-grained tokens** → *Generate new token*.
2. **Token name:** `TfLens read-only`.
3. **Expiration:** your choice; note the date, because sync starts failing silently-ish when it lapses (you will see the rate-limit message again).
4. **Repository access:** **Public Repositories (read-only)**. Do **not** grant access to private repositories — TfLens refuses to connect a private repo anyway (BRD-100, REQ-FN-015), and a test asserts a configured token cannot be used to reach one.
5. **Permissions:** leave everything at the default *no access*; public-repository read needs nothing extra. If your UI insists on a repository permission, set **Contents: Read-only** and nothing else.
6. Generate, copy the value (it is shown once), and put it in `.env` as `TfLensGitHubToken`.

**Classic token (acceptable):** create one with **no scopes ticked at all**. An unscoped classic token
still authenticates you for public-data reads and lifts the limit to 5,000/h, while granting nothing.

### Verify it

```bash
set -a; . ./.env; set +a
curl -s -H "Authorization: Bearer $TfLensGitHubToken" https://api.github.com/rate_limit \
  | python3 -c "import json,sys; d=json.load(sys.stdin)['resources']['core']; print(f\"limit={d['limit']} remaining={d['remaining']}\")"
```

☑ **Check:** `limit=5000`. If it prints `limit=60`, the token was not sent or is invalid.

> **Rotation.** Replace the value in `.env` and restart the container. Nothing is cached across a
> restart, and the token is never written to the database or the raw archive.

---

## 4. AppManager credentials

Identity is AppManager (Application Id **1**); TfLens has no user table of its own. Get the API key and
secret from the AppManager admin UI, under that application's settings.

- **Whole pair or neither.** One without the other fails startup by design.
- The pair is sent on `/AuthSvc/*` only, never on `/UserSvc/*` — see `DECISIONS.md` D-006.

Verify the pair reaches AppManager (this sends no mail — the address does not exist):

```bash
set -a; . ./.env; set +a
curl -s -X POST "$TfLensAppManagerBaseUrl/AuthSvc/forgot-password" \
  -H 'Content-Type: application/json' \
  -H "X-Api-Key: $TfLensAppManagerApiKey" -H "X-Api-Secret: $TfLensAppManagerApiSecret" \
  -d '{"email":"tflens-probe-nonexistent@example.invalid"}'
```

☑ **Check:** `{"success":true,...}`. `APPLICATION_ID_REQUIRED` means the headers did not arrive;
`INVALID_API_KEY` means the pair is wrong or half-configured.

> **Known AppManager-side gap.** Application 1 currently defines no `Manager` role, so registration's
> `applicationRoleCode: "Manager"` is silently downgraded to `User`, and `GET /UserSvc/profile` answers
> `403 NO_APP_ACCESS` when an app context is resolved. TfLens is unaffected — it issues its own
> `Manager` claim (BRD-95) and scopes the key pair away from `/UserSvc/*`. Details and reproduction:
> `docs/TfLens-AppManager-Feedback.md` (AM-001, AM-002).

---

## 5. Deploy with Docker Compose

```bash
git clone <repo> && cd TfLens
# create .env per §2
docker compose up -d
docker compose ps
docker compose logs -f tflens
```

What this starts:

| Service | Image | Port | Volume |
|---|---|---|---|
| `postgres` | `postgres:16` | not published (compose network only) | named volume `pgdata` |
| `tflens` | built from `Dockerfile` | `${TfLensHostPort:-8080}` → `8080` | `./data`, `./logs` bind-mounted |

The schema script `database/001-schema.sql` is applied **idempotently at every startup** — there is no
migration framework and no migration history to manage (`DECISIONS.md` D-003).

To publish Postgres locally for inspection, use `docker-compose.override.yml`; it is deliberately kept
out of the deploy file (`DECISIONS.md` D-007).

☑ **Check:** `curl -s localhost:8080/healthz` returns JSON with database reachability and last-sync age.

---

## 6. Deploy for local development

```bash
docker compose up -d postgres          # or point at your own PostgreSQL 16
set -a; . ./.env; set +a
export TfLensDbConnection="Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=$TfLensDbPassword"
dotnet build TfLens.slnx -c Release
dotnet run --project src/TfLens/TfLens.csproj -c Release --urls http://localhost:5099
```

Two things that will bite otherwise:

- **`TfLensDataRoot` defaults to `data/` relative to the working directory.** `dotnet run --project
  src/TfLens` runs with the working directory at `src/TfLens`, so the archive lives at
  `src/TfLens/data/`. **Run every CLI verb from `src/TfLens`** or `rebuild` will find an empty archive,
  truncate the tables and replay nothing.
- **Run tests with `-m:1`.** `dotnet test TfLens.slnx -c Release -m:1`. Without it, projects run in
  parallel against one shared database and produce spurious failures.

☑ **Check:** `/healthz` answers 200 and `/login` renders.

---

## 7. First-run verification

Walk this once on any new deployment. Test accounts are listed in `docs/TfLens-UsageGuide.md` — use
those, never invented ones.

1. **Sign in** at `/login` (or register a new account at `/register`).
2. **Connect a repository** on `/repos` → *Connect*. Paste a public GitHub URL or `owner/name` and press
   **Validate**. All three checks must pass — *Repository exists · Public · Telemetry path found* — before
   *Connect* enables. The kind is auto-detected: `docs/metrics` → **techieflow**,
   `verification/telemetry` → **playbook**.
3. **Sync** with the header's **Sync now**.
4. **Coverage (`/`)** should show one card per repository with a short SHA and non-empty stream counts.
5. **Check the reports** — `/three-questions`, `/harness`, `/routing` all render.
6. **Export** from `/export`; confirm `data/reports/<userId>/<date>/<framework>/` now holds
   `snapshot.md` and `tflens.json`.

☑ **Check:** Coverage is green (or its warnings are ones you recognise), and no repository shows
`error`. A rate-limit error here means §3 was skipped.

---

## 8. Before quoting any number — the parity run

**No figure TfLens renders may be quoted until a parity run passes.** Two independent implementations —
the framework's `tf-metrics.sh` and TfLens — compute the same metrics from the same files and must agree
exactly. `/export` shows this as a **QUOTABLE / NOT QUOTABLE** banner, and it is the product's central
safety device (BRD §13).

Re-run it **after every parser or engine change, and after every framework update**, because the record
stores the reference script's hash and a changed script invalidates the stamp.

```bash
# 1. Materialise the dataset from the raw archive (it is byte-identical to what was fetched)
PD=tests/.artifacts/parity; rm -rf $PD; mkdir -p $PD
for d in src/TfLens/data/raw/<userId>/*/; do
  name=$(basename "$d" | sed 's/^[^_]*__//')
  mkdir -p "$PD/$name/docs/metrics" "$PD/$name/.tfcore"
  for s in runs gates sessions commits; do cat "$d"${s}-*.jsonl > "$PD/$name/docs/metrics/${s}.jsonl" 2>/dev/null || : > "$PD/$name/docs/metrics/${s}.jsonl"; done
  # the oracle reads project_type from the repo's own config; copy the real repo's value
  printf 'metrics:\n  project_type: <app|library|docs|framework>\n' > "$PD/$name/.tfcore/core-config.yaml"
done

# 2. The reference
bash .tfcore/telemetry/tf-metrics.sh --rollup $PD/*/ --json > $PD/reference.json

# 3. TfLens, for the same data — run from src/TfLens
(cd src/TfLens && dotnet bin/Release/net10.0/TfLens.dll export --user <userId> --framework techieflow)
cp src/TfLens/data/reports/<userId>/<date>/techieflow/tflens.json $PD/tflens.json

# 4. Compare
python3 tools/parity-compare.py $PD/reference.json $PD/tflens.json --allow-environment-keys
```

☑ **Check:** exit code 0 and `PASS — the two implementations agree key for key`. Only then record the
run in `DECISIONS.md` §6 and confirm `/export` reads **QUOTABLE**.

**Any mismatch is by definition a bug in TfLens, not in the reference** — the script is never edited to
match the app. `--allow-environment-keys` downgrades only the documented path-vs-`owner/name` naming
difference; nothing else may be waved through.

---

## 9. Operating commands

Run from `src/TfLens` locally, or `docker exec tflens dotnet TfLens.dll <verb>` in the container.

| Command | What it does |
|---|---|
| `rebuild --user <id>` | Truncates that user's stream tables and replays the whole raw archive. The recovery path for any parser bug. |
| `sync` | One poll pass over every connected repository, exactly as the background poller does. |
| `export --user <id> [--framework techieflow\|playbook] [--date yyyy-MM-dd]` | Writes the snapshot pair. With no `--framework`, writes one per framework. |

**`rebuild` is also the fix for a repository stuck showing stale or missing data.** Sync skips a
repository whose recorded SHA has not changed, so if its rows are lost the poller will never restore
them — only `rebuild` will.

---

## 10. Backup and restore

**Back up `data/`. Do not bother backing up PostgreSQL.**

Every fetched file is archived byte-for-byte under `data/raw/<userId>/<owner>__<name>/` *before* it is
parsed, so the archive is the source of truth and the database is a derived cache (`DECISIONS.md` D-002).
`data/` also holds `reports/` (exported snapshots), `prices.json` (the repricing rate card) and
`parity-last.json` (the quotable stamp).

Restore: put `data/` back, start the app, run `rebuild --user <id>` for each user.

☑ **Check:** after a restore, per-stream counts on Coverage match what they were before.

---

## 11. Known constraints

- **Public repositories only.** Private repositories are refused at validation; a configured
  `TfLensGitHubToken` does not and must not change that (BRD-100).
- **60 requests/hour without a token.** See §3 — this is the single most common cause of a deployment
  that looks broken.
- **Password reset needs the AppManager key pair** (§4). Everything else in the auth flow works without it.
- **The Playbook axis needs a repository that publishes `verification/telemetry/events.ndjson`.** None
  currently does, so the Playbook state of every report page correctly shows its empty state.
- **Nothing is quotable until §8 passes**, and a framework update invalidates the stamp by design.

---

## 12. Upgrading

1. Pull the new code.
2. `docker compose build tflens && docker compose up -d` (the schema script re-applies itself; no
   migration step).
3. **Re-run the parity procedure (§8)** — a parser or engine change invalidates the previous stamp, and
   so does a change to the framework's `tf-metrics.sh`. `/export` will read `NOT QUOTABLE` with the
   reason until you do.

☑ **Check:** `/healthz` green, Coverage renders, `/export` banner reflects a fresh parity run.
