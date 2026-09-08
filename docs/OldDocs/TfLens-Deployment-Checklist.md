# TfLens — Deployment Checklist

Everything needed to stand TfLens up on a new machine, in order, with the reason each step exists. Work
top to bottom; every section ends with something you can check rather than assume.

**Last updated:** 2026-09-02
**Audience:** whoever deploys and operates TfLens (today: the owner)

TfLens is one process plus a PostgreSQL 16 database. It is **read-only against every external system** —
it never writes to GitHub, and it never calls AppManager's licensing, feature-flag, payment or issue
services. Its persistent state is two directories, `data/` and `logs/`; the database is disposable and
can be rebuilt from `data/` at any time (`DECISIONS.md` D-002).

> **Two deployment paths, and this document covers both.**
> **Sections 1–12 are the self-contained path**: one machine, `docker-compose.yml` at the repo root,
> TfLens plus its own PostgreSQL container. That is what you run on a laptop, a spare box, or any host
> where TfLens is the only thing living there.
> **Section 13 is the production path**: `tflens.techierathore.com` on the shared VPS, deployed by
> GitHub Actions, using the server's native PostgreSQL, Caddy and Seq. It reuses §§2–4 for *what* each
> secret means and *how* to obtain it — it only changes where you put the value.
> Sections 7–11 (verification, parity, operating commands, backup, constraints) apply to both.

---

## Table of contents

1. [Prerequisites](#prerequisites)
2. [Configuration and secrets](#configuration-and-secrets)
3. [`TfLensGitHubToken` — create it first](#tflensgithubtoken-create-it-first)
4. [AppManager credentials](#appmanager-credentials)
5. [Deploy with Docker Compose](#deploy-with-docker-compose)
6. [Deploy for local development](#deploy-for-local-development)
7. [First-run verification](#first-run-verification)
8. [Before quoting any number — the parity run](#before-quoting-any-number-the-parity-run)
9. [Operating commands](#operating-commands)
10. [Backup and restore](#backup-and-restore)
11. [Known constraints](#known-constraints)
12. [Upgrading](#upgrading)
13. [Production: deploying to the VPS as `tflens.techierathore.com`](#13-production-deploying-to-the-vps-as-tflenstechierathorecom)

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

*This is the self-contained path — TfLens plus its own PostgreSQL container, on a host where nothing
else is running. For `tflens.techierathore.com` on the shared VPS, skip to §13; the compose file there
is a different one and starts no database of its own.*

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
5. **Check the reports** — `/gate-outcomes`, `/harness`, `/routing` all render.
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

*On the VPS there is no upgrade procedure: `git push` to `main` is the upgrade (§13.9). What follows
is the self-contained path.*

1. Pull the new code.
2. `docker compose build tflens && docker compose up -d` (the schema script re-applies itself; no
   migration step).
3. **Re-run the parity procedure (§8)** — a parser or engine change invalidates the previous stamp, and
   so does a change to the framework's `tf-metrics.sh`. `/export` will read `NOT QUOTABLE` with the
   reason until you do.

☑ **Check:** `/healthz` green, Coverage renders, `/export` banner reflects a fresh parity run.

---

## 13. Production: deploying to the VPS as `tflens.techierathore.com`

The production deployment is a GitHub Actions pipeline. **Push to `main` is the deploy** — there is no
SSH step you perform, no compose file you edit on the server, and no image you build locally.

This section is TfLens's filled-in instance of `docs/claude-code-deployment-brief-v3.2.md`, running on
the server built by `docs/bluehost-vps-runbook-v5.2.md`. Read those two if you want the reasoning
behind the environment; everything you need to *do* is here.

**Work §§13.1–13.8 top to bottom before the first push.** They take about 20 minutes and most of it is
copying values into GitHub. Then §13.9 is the deploy and §13.10 is what to check afterwards.

### 13.1 What the pipeline does, and what it deliberately does not

Every push to `main` runs three jobs (`.github/workflows/deploy.yml`):

```
build    Docker image → ghcr.io/techierathore/tflens  (tags :latest and the short commit SHA)
deploy   over SSH as ciuser —
           1. mkdir -p /srv/apps/tflens, /srv/data/tflens/data, /srv/data/tflens/logs
           2. sudo ensure-db tflens                     (idempotent; no pgvector)
           3. render docker-compose.prod.template.yml with the secrets → /srv/apps/tflens/docker-compose.yml
           4. docker compose pull && up -d && image prune -f
verify   curl https://tflens.techierathore.com/healthz, 5 attempts — fails the run on no 200
```

Everything is idempotent: the first deploy and the hundredth run the same steps.

Four things the pipeline will never do, by design — if you find yourself wanting one of them, the want
is the bug:

| It never… | Because |
|---|---|
| touches DNS | It holds no DNS credentials. The A record is yours, once, in §13.7. |
| touches Caddy | Routing is yours. You write `/srv/caddy/sites/tflens.caddy` by hand once (§13.7), exactly the way `seq.caddy` was written, and edit it there whenever it needs to change. The deployment brief permits a pipeline to create that file; TfLens's does not, so there is one owner of routing and no possibility of a deploy quietly reverting an edit you made on the server. |
| runs `docker login` on the server | The server already holds a stored GHCR PAT (runbook Step 12). No PAT belongs in this repo's secrets. |
| runs any `sudo` but `ensure-db` | That is the CI account's entire sudoers allowlist. `sudo docker` in particular **fails** — `ciuser` is in the `docker` group and needs no sudo. |

### 13.2 TfLens's per-app variables

The deployment brief's §3 table, filled in. These are already baked into the workflow and the compose
template — this is here so you can recognise them, not so you can retype them.

| Variable | Value | Note |
|---|---|---|
| `APP_NAME` | `tflens` | The single infra identity: container name, image name, `/srv` folders, Caddy snippet, and the Seq `App` property. Not the C# project name. |
| `DOMAIN` | `tflens.techierathore.com` | Public. |
| `DB_NAME` | `tflens` | One database on the VPS's native PostgreSQL, owner `appuser`. |
| `NEEDS_PGVECTOR` | no | |
| `HAS_UPLOADS` | no — **but two bind mounts anyway** | TfLens has no user uploads. It does have the raw archive and the log directory, which are mounted under `/srv/data/tflens/` for exactly the reason uploads are: a container is thrown away on every deploy. |
| `HAS_EF_MIGRATIONS` | no | `database/001-schema.sql` is applied idempotently at every startup. No migration framework, no history (`DECISIONS.md` D-003). |
| `DOTNET_VERSION` | 10 | |
| `PROJECT_PATH` | `src/TfLens/TfLens.csproj` | |
| `MEM_LIMIT` | `512m` | Blazor Server holds a circuit per open tab and the poller parses whole JSONL streams in memory. |
| `INTERNAL_APIS` | none | TfLens calls AppManager over its **public** URL, `https://appmgrapi.techierathore.com` — not `http://appmgrapi:8080`. Changing that is a deliberate decision, not a tidy-up: see §13.14. |

### 13.3 Server prerequisites — verify, do not rebuild

The pipeline assumes the runbook build is finished. Confirm it once, from your own machine, before you
spend time debugging a deploy that was never going to work:

```bash
ssh ravi@YOUR_VPS_IP 'sudo /srv/checkup.sh'
```

☑ **Check:** `0 failed`. Then confirm the five things TfLens specifically needs:

```bash
ssh -i ~/.ssh/vps_ciuser ciuser@YOUR_VPS_IP '
  whoami                                          # ciuser
  docker ps --format "{{.Names}}"                 # must list: caddy, seq
  sudo -l                                         # must list ONLY /usr/local/bin/ensure-db
  touch /srv/apps/.t && rm /srv/apps/.t && echo "APPS WRITE OK"
  touch /srv/caddy/sites/.t && rm /srv/caddy/sites/.t && echo "CADDY WRITE OK"'
```

☑ **Check:** all five lines as described. A missing `seq` container means §13.5 has nothing to create a
key in; a missing `caddy` means the site will never be reachable.

**About the PostgreSQL version.** §1 asks for PostgreSQL 16; the VPS runs **18**, natively on Ubuntu
rather than in a container. That is fine and needs nothing changed — 16 is the floor, not the target,
and the schema uses only `jsonb` and expression indexes, which long predate it. What *does* change is
ownership of the data: on the VPS the database is one of many in a shared cluster, backed up by the
server's weekly `pg_dumpall`, and it is **not** where TfLens's truth lives (§13.13).

### 13.4 GitHub secrets

Eight secrets in total. Four are shared by every app in the portfolio and probably already exist; four
are TfLens's own and definitely do not.

**Organisation-level (runbook §2.2) — reused, not created here:**

| Secret | Value |
|---|---|
| `VPS_HOST` | your VPS IP |
| `VPS_USER` | `ciuser` — **not** `ravi` |
| `VPS_SSH_KEY` | the entire private key from `cat ~/.ssh/vps_ciuser`, including the `-----BEGIN`/`-----END` lines |
| `DB_PASSWORD` | `appuser`'s password, from runbook Step 6 |

> If this repository is not under a GitHub organisation, add these four as **repository** secrets
> instead. Same names, same values; the workflow cannot tell the difference.

**Repository-level — add all four on this repo** (GitHub → this repo → Settings → Secrets and
variables → Actions → *New repository secret*):

| Secret | Required? | Value from | If left empty |
|---|---|---|---|
| `SEQ_API_KEY` | Yes | §13.5 | Deploy succeeds with a warning; TfLens logs to console and file only, and nothing appears in Seq. |
| `TFLENS_GITHUB_TOKEN` | Strongly recommended | §3 of this document — create the fine-grained, public-read-only token exactly as described there | Deploy succeeds with a warning, then **sync fails on anything past one or two repositories** and every repo shows `error`. This is the single most common way a healthy TfLens deployment looks broken. |
| `TFLENS_APPMANAGER_API_KEY` | Pair | §4 — AppManager admin UI, Application 1 | Password reset answers `400 APPLICATION_ID_REQUIRED`. Login, registration and sessions still work. |
| `TFLENS_APPMANAGER_API_SECRET` | Pair | §4 | As above. |

Three things worth getting right the first time:

- **The AppManager pair is whole or nothing.** A key without its secret makes AppManager answer
  `401 INVALID_API_KEY` on *every* call, and TfLens refuses to start (`DECISIONS.md` D-006). The deploy
  job checks for a half pair and fails on the runner, before it touches the server.
- **`DB_PASSWORD` must not contain a `$` or a `;`.** The value is substituted into a connection string
  inside a YAML file that `docker compose` then interpolates: a `$` would be read as a compose variable
  and a `;` would end the connection string early. The runbook's `openssl rand -base64 24` produces
  neither, so this only bites a hand-chosen password.
- **Never paste a secret into `docker-compose.prod.template.yml`.** It is a template with placeholders
  precisely so the repository never holds a value. The real file exists only on the server, rendered at
  deploy time.

☑ **Check:** the repo's Actions secrets page lists exactly `SEQ_API_KEY`, `TFLENS_APPMANAGER_API_KEY`,
`TFLENS_APPMANAGER_API_SECRET`, `TFLENS_GITHUB_TOKEN`, and the four shared ones are visible either
there or as inherited organisation secrets.

**The settings that are *not* secrets — you set none of these.** They are already written into
`docker-compose.prod.template.yml`, in the repository, because none of them is a credential and a
value in version control is a value you can review in a diff:

| Setting | Value on the VPS | Why it is not a secret / not a decision |
|---|---|---|
| `TfLensAppManagerAppId` | `1` | **TfLens *is* Application 1 in AppManager.** An application id is a public identifier, not a credential — it already travels in the body of every request TfLens makes. It is written into the template explicitly rather than left to the code default, so the file states TfLens's identity instead of implying it. Nothing to set, and nothing to put in a secret. |
| `TfLensAppManagerBaseUrl` | `https://appmgrapi.techierathore.com` | The public AppManager URL — see §13.14 for why the container name is not used instead. |
| `TfLensDataRoot` | `/app/data` | The bind-mounted archive (§13.13). |
| `TfLensPollIntervalMinutes` | `15` | The default. |
| `TfLensStalenessDays` | not set → `7` | The default. Add it to the template if you want a different Coverage warning threshold. |
| `Seq__Url` | `http://seq:5341` | Infrastructure, identical for every app on the server. The *key* is the secret; the address is not. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Turns on HSTS, the exception handler and `CookieSecurePolicy.Always`. |

To change any of them: edit `docker-compose.prod.template.yml` and push. That is a normal code change
and goes out with the next deploy.

### 13.5 Seq — create TfLens's own API key

Seq is already running on the server; the pipeline never creates, starts or configures it. All you do
is issue TfLens a key of its own. **Never reuse another app's** — a per-app key stamps `App`
server-side, can be revoked without redeploying anything else, and can be throttled alone if TfLens
ever gets noisy (runbook §6.3).

1. Open `https://seq.techierathore.com` and sign in as `admin`.
2. **Settings → API Keys → Add API Key.**
3. **Title:** `tflens`
4. **Applied properties:** add `App` = `tflens`
5. Leave the minimum level at Information.
6. Save, and **copy the key immediately** — Seq shows it exactly once.
7. Paste it into the repo secret `SEQ_API_KEY` (§13.4).

What this wires up, so you know what to expect: the compose file sets `Seq__Url=http://seq:5341` and
`Seq__ApiKey`, and `TfLensLogging` registers the Seq sink **only** when a URL is present. Plain HTTP
over the shared `web` Docker network is correct — the traffic never leaves the server, so there is no
TLS and no certificate here, and `https://seq.techierathore.com` is for your browser only and must
never appear in app config. If Seq is down or slow, TfLens starts normally and the sink retries;
logging is never a startup dependency.

Nothing changes for local development: no Seq URL is configured on your machine, so no sink is
registered and Serilog writes to console and `logs/` exactly as before.

☑ **Check (after the first deploy):** Seq, filtered on `App = 'tflens'`, shows TfLens's startup lines.

### 13.6 The GitHub token, on production terms

Create it exactly as §3 describes — fine-grained, **Public Repositories (read-only)**, no other
permission — and put the value in `TFLENS_GITHUB_TOKEN`, not in a `.env` file. There is no `.env` on
the VPS: the pipeline injects every value at deploy time.

Two production-specific notes:

- **The rate limit is per token, and now the whole server shares one.** 5,000 requests/hour is far more
  than the 15-minute poller will use, but it is one budget for every user connected in TfLens, not per
  user.
- **Expiry is a silent failure.** When the token lapses, TfLens falls back to anonymous reads and
  repositories start showing `GitHub rate limit reached` again. Note the expiry date somewhere you will
  see it. Rotating is: new token → update the secret → re-run the workflow (§13.11). No code change.

☑ **Check:** run §3's `rate_limit` curl with the token you are about to store; it must print
`limit=5000`.

### 13.7 The two manual server steps: DNS, then the Caddy entry

Both are one-time, both take about two minutes, and both must be done **before the first push**.

Add this record at the registrar that holds `techierathore.com`:

| Field | Value |
|---|---|
| Type | `A` |
| Name | `tflens` |
| Value | your VPS IP |
| TTL | default / auto |

- **On Cloudflare, set it to DNS only (grey cloud).** An orange cloud proxies the request and Caddy can
  never complete its certificate challenge.
- **On GoDaddy**, this is *Manage DNS → DNS Records* — not the nameserver page, which only accepts
  hostnames and will reject an IP.

Do this **before** the Caddy entry below, so Caddy's first attempt at the certificate succeeds rather
than being retried, and well before the first push — the `verify` job resolves the real hostname, so
without the record the pipeline fails at the last step even though the app is running perfectly.

☑ **Check:** `dig +short tflens.techierathore.com` returns your VPS IP.

**Then the Caddy site entry**, on the server, once. This is the same two-command shape as `seq.caddy`
in runbook §6.2 — a snippet file in `/srv/caddy/sites/`, never an edit to the main `Caddyfile`:

```bash
cat > /srv/caddy/sites/tflens.caddy << 'EOF'
tflens.techierathore.com {
    reverse_proxy tflens:8080
}
EOF
docker exec caddy caddy reload --config /etc/caddy/Caddyfile
```

`tflens:8080` is the container name on the shared `web` network, not a port on the host — TfLens
publishes nothing on the VPS. Blazor Server's interactivity is a long-lived WebSocket, which Caddy's
`reverse_proxy` upgrades with no extra directive, so this really is the whole file.

There is nothing to do for TLS: Caddy obtains and renews the certificate itself on the first request
to the hostname.

You can write this entry before the container exists — Caddy will simply answer 502 until the first
deploy — so doing it now, in the same sitting as the DNS record, is the least error-prone order.

☑ **Check:** `docker exec caddy caddy validate --config /etc/caddy/Caddyfile` passes, and
`curl -I https://tflens.techierathore.com` returns a certificate (a `502` body at this stage is
correct and expected — nothing is running behind it yet).

> **This file is yours from here on.** The pipeline never reads, writes or reloads any Caddy config, so
> a change you make here survives every future deploy. To change routing later: edit that same file and
> re-run the `caddy reload` line.

### 13.8 Pre-flight — the whole list, in one place

Tick all eight before pushing:

- [ ] `sudo /srv/checkup.sh` reports `0 failed`, and `caddy` + `seq` are both running (§13.3).
- [ ] Org (or repo) secrets exist: `VPS_HOST`, `VPS_USER` = `ciuser`, `VPS_SSH_KEY` (private key, full
      block), `DB_PASSWORD`.
- [ ] Repo secret `SEQ_API_KEY` holds a key titled `tflens` with applied property `App=tflens` (§13.5).
- [ ] Repo secret `TFLENS_GITHUB_TOKEN` holds a token that prints `limit=5000` (§13.6).
- [ ] Repo secrets `TFLENS_APPMANAGER_API_KEY` and `TFLENS_APPMANAGER_API_SECRET` are **both** set or
      **both** absent (§13.4).
- [ ] DNS A record `tflens` → VPS IP resolves (§13.7).
- [ ] `/srv/caddy/sites/tflens.caddy` exists on the server and Caddy has been reloaded (§13.7).
- [ ] `docker-compose.prod.template.yml` contains no real secret — only placeholders.

### 13.9 Deploy

```bash
git push origin main
```

Then watch **Actions → deploy**. Roughly: `build` 3–6 minutes on a cold cache and under two on a warm
one, `deploy` under a minute, `verify` anywhere from 25 seconds to about three minutes — it waits 20
seconds, then retries up to five times, because on a first deploy it is waiting on a schema apply, a
database connection and a certificate all at once.

On the **first** deploy specifically, expect two things that happen only once and then never again:
`ensure-db` actually creates the `tflens` database (on every later run it finds it and returns), and
Caddy fetches the TLS certificate on the very first request to the new hostname. Both are part of why
`verify` retries rather than probing once.

☑ **Check:** all three jobs green, and `https://tflens.techierathore.com/healthz` answers
`{"status":"ok","database":"up",...}` in your browser.

### 13.10 After the first green deploy

1. **Walk §7's first-run verification** against the real URL — sign in, connect a public repository,
   Sync now, check Coverage, open `/gate-outcomes`, `/harness` and `/routing`, export from `/export`.
   That is the acceptance test; a green pipeline only proves the app answers `/healthz`.
2. **Confirm logs reach Seq**: `https://seq.techierathore.com`, filter `App = 'tflens'`.
3. **Add the UptimeRobot monitor** — one HTTP(s) monitor on
   `https://tflens.techierathore.com/healthz`. Because `/healthz` reports database reachability and
   answers `503` when it is down, this one monitor catches app crashed, Caddy down, TLS expired and
   database down, with no keyword matching needed.
4. **Re-run the server checkup**: `ssh ravi@YOUR_VPS_IP 'sudo /srv/checkup.sh'` — `no container
   restart-looping` and `caddy config valid` must still pass with `tflens` running.

Backups need nothing: `pg_dumpall` picks up the new database and the weekly `/srv/data` archive picks
up `/srv/data/tflens/` automatically.

### 13.11 Operating the production instance

TfLens publishes no port on the VPS — it is reachable only through Caddy, on the `web` Docker network.
So everything below goes through `docker exec`, as `ravi` (or `ciuser`; both are in the `docker` group):

| Task | Command on the server |
|---|---|
| Follow the logs | `docker logs -f tflens` |
| Health from inside | `docker exec tflens wget -qO- http://localhost:8080/healthz` |
| One sync pass now | `docker exec tflens dotnet TfLens.dll sync` |
| Rebuild a user's streams from the archive | `docker exec tflens dotnet TfLens.dll rebuild --user <id>` |
| Export a snapshot | `docker exec tflens dotnet TfLens.dll export --user <id> [--framework techieflow\|playbook]` |
| Restart without redeploying | `cd /srv/apps/tflens && docker compose restart` |
| Re-deploy the current image | `cd /srv/apps/tflens && docker compose up -d` |

The verbs share the engine the pages use (`ADR-005`), and the container's working directory is `/app`
with `TfLensDataRoot=/app/data`, so §6's "run every CLI verb from `src/TfLens`" trap does not exist
here — the data root is absolute.

**`rebuild` is still the fix for a repository stuck showing stale or missing data**, for the reason in
§9: sync skips a repository whose recorded SHA has not moved, so lost rows are never restored by
polling alone.

**Re-running a deploy without a code change:** Actions → deploy → *Run workflow* (the workflow accepts
`workflow_dispatch`). Use this after rotating a secret — secrets are baked into the compose file at
deploy time, so a rotated value reaches the container only on the next deploy.

### 13.12 Changing routing, and rolling back

**Routing.** `/srv/caddy/sites/tflens.caddy` is a hand-written file (§13.7) and nothing in this
repository or the pipeline knows it exists. To change it — a redirect, a header, a second hostname —
edit it on the server and reload:

```bash
nano /srv/caddy/sites/tflens.caddy
docker exec caddy caddy validate --config /etc/caddy/Caddyfile   # before, not after
docker exec caddy caddy reload  --config /etc/caddy/Caddyfile
```

`reload` on an invalid config leaves the previous one running rather than taking the site down, but
validating first tells you *why* in one line instead of sending you to `docker logs caddy`.

**Rollback, the clean way:** `git revert <bad commit> && git push`. The pipeline redeploys the previous
code exactly like any other release and the history stays honest.

**Rollback, the fast way** (only when Actions itself is down): every deploy pushed the image under its
short commit SHA as well as `:latest`, so every version you have ever deployed is already in GHCR.

```bash
cd /srv/apps/tflens
nano docker-compose.yml     # image: ghcr.io/techierathore/tflens:a1b2c3d
docker compose pull && docker compose up -d
```

Change the tag back to `:latest` once the fix is pushed, or the next pipeline run will pin you to the
old image. Rolling back swaps the **code**, not the data — which for TfLens is unusually safe, because
the schema is applied idempotently and the archive is the source of truth.

### 13.13 What persists, and what to back up

| Path on the VPS | What it is | Backed up by |
|---|---|---|
| `/srv/data/tflens/data` | **The source of truth.** `raw/` (every fetched file, byte-for-byte, before parsing), `reports/`, `prices.json`, `parity-last.json`, and `keys/` — the Data Protection key ring. | The runbook's weekly `/srv/data` archive → OneDrive + Google Drive |
| `/srv/data/tflens/logs` | Rolling Serilog file sink, 14 days | Same archive |
| PostgreSQL database `tflens` | A derived cache. `rebuild` reconstructs it from `raw/`. | Weekly `pg_dumpall` — convenient, not load-bearing |
| `/srv/apps/tflens/docker-compose.yml` | Rendered config, **contains live secrets** | Same archive. Anyone with the archive has the secrets; treat it accordingly. |

§10's rule holds unchanged on the VPS: **back up `data/`; do not bother about PostgreSQL.** The one
addition production makes is `keys/`. Losing the key ring does not lose data, but it orphans every
encrypted AppManager token in `AuthSession` and invalidates every live auth cookie — everyone is simply
signed out and signs in again (BRD-87). That is why the data directory is a bind mount and not a
container path.

Restore is §10's: put `/srv/data/tflens/data` back, `docker compose up -d`, then
`docker exec tflens dotnet TfLens.dll rebuild --user <id>` for each user.

### 13.14 Production differences you should know about before you meet them

- **`/export`'s banner starts at its never-run state on a fresh server**, because `parity-last.json`
  lives in `data/` and a new deployment has none. The parity procedure (§8) is a developer activity on
  a developer's machine; the server is where the numbers are read, not where they are certified.
- **TfLens calls AppManager over the public internet**, `https://appmgrapi.techierathore.com`, even
  though both containers sit on the same `web` network. Switching to `http://appmgrapi:8080` would be
  faster and would survive a DNS outage, but it also changes TfLens's TLS posture towards a service it
  sends an API key pair to, and it silently breaks local development, which has no such container. Left
  as is on purpose.
- **The container runs as root**, and the Dockerfile must keep having no `USER` directive. A non-root
  container user cannot write to the `ravi`-owned bind mounts, and the failure surfaces at runtime as a
  permission error on the first archive write — not at deploy time.
- **The database is shared infrastructure.** `tflens` is one database in a cluster serving every app.
  Never `DROP` it to "start clean"; `rebuild` is the reset, and it touches only TfLens's tables.
- **`/healthz` is the only anonymous route that reports anything.** Sign-in and static assets are
  necessarily anonymous too; everything that renders data requires the auth cookie (BRD-2). That is why
  the verify job probes `/healthz` and nothing else — it is the only path a credential-less caller can
  learn a useful yes/no from, and BRD-78 caps what it may say to database reachability and last-sync
  age.

### 13.15 When a deploy fails, look here first

Read the actual error before changing anything — for a crash-looping container that means
`docker logs tflens`, which names the fault. Guessing from symptoms is what makes these expensive.

| Symptom | Cause | Fix |
|---|---|---|
| SSH step: permission denied | `VPS_SSH_KEY` holds the `.pub` file, or `VPS_USER` is still `ravi` | Re-paste the full private key including BEGIN/END; set `VPS_USER` to `ciuser` |
| `sudo: a password is required` | A step used sudo for something outside the allowlist | Only `/usr/local/bin/ensure-db` is permitted — remove the sudo |
| `docker compose pull` → `unauthorized` | The **server's** stored GHCR PAT expired | Re-run runbook Step 12 on the server. Not a pipeline change, and no PAT goes in a secret |
| verify fails, DNS error | A record missing or not propagated | Add it (§13.7), then re-run the job |
| verify fails, 502 in the browser | Container crash-looping | `docker logs tflens` |
| Container logs: refuses to start, missing DB connection | `DB_PASSWORD` empty, or a `$`/`;` in the password mangled the connection string | §13.4 |
| Container logs: `INVALID_API_KEY` at startup | Half an AppManager pair reached the server | Set both or neither (`DECISIONS.md` D-006) |
| Starts, but `/healthz` answers 503 | Database unreachable | The connection string must use `172.17.0.1` — never `localhost`, never a container name. Confirm `ensure-db` ran in the deploy log |
| Runs fine, no logs in Seq | `SEQ_API_KEY` missing or revoked | Recreate the key (§13.5), update the secret, re-run the workflow |
| Every repository shows `error` with a rate-limit message | `TFLENS_GITHUB_TOKEN` empty or expired | §13.6. The app is behaving correctly — it ran out of anonymous quota |
| Coverage empty after a restore | Rows not replayed | `docker exec tflens dotnet TfLens.dll rebuild --user <id>` |
