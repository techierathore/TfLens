# TfLens — Developer Guide

**Last updated:** 2026-08-27
**Audience:** a developer who has just cloned this repository and needs to run it, change it, and fix bugs in it.

> **Start here if the app won't start.** Jump straight to [Running TfLens locally](#running-tflens-locally),
> or to [Troubleshooting](#troubleshooting) if you already have an error on screen.

## Table of Contents

1. [Running TfLens locally](#running-tflens-locally)
2. [Configuration reference](#configuration-reference)
3. [Troubleshooting](#troubleshooting)
4. [How the solution fits together](#how-the-solution-fits-together)
5. [Command verbs](#command-verbs)
6. [Tests](#tests)
7. [Screen-by-screen reference](#screen-by-screen-reference)

---

## Running TfLens locally

TfLens needs two things: the .NET 10 SDK, and a PostgreSQL 16 database. Nothing else.

> ### `.env` is not TfLens's configuration file — read this before you edit it
>
> **`.env` is `docker compose`'s file, and nothing else reads it.** There is no `.env` loader in
> `Program.cs`; `dotnet run` and F5 never open it. It exists so Compose can interpolate
> `${TfLensDbPassword}` into the `postgres` service at *deployment* time.
>
> **For local development the secrets live in your user-secrets `secrets.json`** — outside the
> repository, so nothing you put there can be committed. That is the path documented below, and it
> is the one to edit when you need to change a key.
>
> Do **not** put secrets in `appsettings.Development.json`. That file *is* committed, so a real
> AppManager secret in it goes to git history — and `ConfigurationHygieneTests` fails the build to
> stop exactly that.

### The short version (Visual Studio / Rider, Windows)

1. **Start the database once**, from the repository root:

   ```powershell
   copy .env.example .env
   docker compose up -d postgres
   ```

   This runs PostgreSQL 16 as the container `tflens-postgres` and publishes it on **localhost:5433**.
   (`.env` is needed *here* — Compose reads it. The app still won't.)

2. **Put your AppManager credentials in user secrets, once.** Right-click the **TfLens** project →
   **Manage User Secrets**. Visual Studio opens your own private `secrets.json`, stored at
   `%APPDATA%\Microsoft\UserSecrets\tflens-dev-secrets\secrets.json`. Paste in the block from
   [`src/TfLens/secrets.example.json`](../src/TfLens/secrets.example.json) and fill it in:

   ```json
   {
     "TfLens:AppManagerApiKey": "ak_live_…",
     "TfLens:AppManagerApiSecret": "sk_live_…",
     "TfLens:GitHubToken": ""
   }
   ```

   This is the file to come back to whenever a credential changes. It is not in the repository and
   cannot be committed.

3. **Press F5.**

That is the whole setup. There is **one** launch profile, `TfLens`. It pins no connection string —
TfLens falls back to the local compose database **in Development only**, as the lowest-priority
configuration source, so anything you set overrides it. It opens on <http://localhost:5014>.

> **You can skip step 2 and still sign in.** Most AppManager endpoints resolve the application from
> the `applicationId` in the request body. Only `/AuthSvc/forgot-password` and `/AuthSvc/reset-password`
> take the app scope from the key headers, so **password reset is the part that dies without the pair**.
> Supply it whole or not at all — a half-pair is refused at startup (see the configuration table).

### The short version (shell, Linux/macOS/WSL)

```bash
cp .env.example .env
docker compose up -d postgres

dotnet user-secrets set "TfLens:AppManagerApiKey"    "ak_live_…" --project src/TfLens
dotnet user-secrets set "TfLens:AppManagerApiSecret" "sk_live_…" --project src/TfLens

dotnet run --project src/TfLens          # http://localhost:5014
```

`dotnet run` uses the same single profile and the same Development fallback. On Linux/macOS the same
secrets file is at `~/.microsoft/usersecrets/tflens-dev-secrets/secrets.json`, and you can edit it
directly instead of using the CLI.

### Where the development default comes from

TfLens **refuses to start without a database** rather than starting and failing at the first user's
sign-in (BRD-9). That is deliberate, but it makes the out-of-the-box experience depend entirely on
where a new developer's connection string comes from — so in Development, and only in Development,
`TfLensOptions.LocalDevelopmentConnection` is seeded as the **lowest-priority** configuration source.

Priority, lowest to highest:

| # | Source | Overrides the one above | Committed? |
|---|---|---|---|
| 1 | The Development fallback (code) | — | yes — but it is a throwaway local container credential, not a secret |
| 2 | `appsettings.json` / `appsettings.Development.json` | yes | **yes — never put a secret here** |
| 3 | `dotnet user-secrets` (`secrets.json`) | yes | **no — this is the local-development secrets path** |
| 4 | Environment variables, including `TfLensDbConnection` | yes | no — this is the *deployment* path (`docker compose`, CI/CD) |

So `dotnet user-secrets set TfLens:DbConnection …` works, and an environment variable beats even that.
Nothing is seeded outside Development, so a deployment still fails fast on a missing setting.

**Rows 3 and 4 are the same settings by two routes, for two audiences.** Row 3 is how *you* run the app
on your machine; row 4 is how a *deployment* supplies the same values. `.env` belongs to row 4 and only
row 4 — it feeds `docker compose`, never `dotnet run`. Row 2 is deliberately the one place with no
secret in it at all, which is what `ConfigurationHygieneTests` enforces.

> **Why not put it in the launch profile?** That was the first attempt and it was wrong. A launch
> profile sets an *environment variable* — the highest-priority source — so it silently overrode
> `user-secrets`, meaning the documented way to point at your own database did nothing at all. It also
> needed a second "own database" profile that existed only in order to fail. One profile, and a default
> that anything can override, is the smaller and more honest design.

### Using your own PostgreSQL instead

Just set the value — no profile switching. Any of these beats the Development fallback:

```powershell
# PowerShell, this session only
$env:TfLensDbConnection = "Host=localhost;Port=5432;Database=tflens;Username=me;Password=…"
dotnet run --project src\TfLens
```

```bash
# bash
export TfLensDbConnection="Host=localhost;Port=5432;Database=tflens;Username=me;Password=…"
dotnet run --project src/TfLens
```

Or store it on your machine only, never committed:

```bash
dotnet user-secrets set TfLens:DbConnection "Host=…" --project src/TfLens
```

You do **not** need to create the schema. `database/001-schema.sql` is idempotent and is applied at
every startup (ADR-015); an empty database is enough.

### Signing in

Identity is **AppManager** (Application Id 1) — TfLens stores no users and no passwords (ADR-011). The
documented test accounts live in [`docs/TfLens-UsageGuide.md`](./TfLens-UsageGuide.md):

| Account | Password | AppManager `userId` |
|---|---|---|
| `tflensdemo@techierathore.com` | `TfLensDemo!23` | 2 |
| `tflenstest2@techierathore.com` | `TfLensTest2!23` | 3 |

Sign-in reaches the live AppManager API over the internet, so it needs network access. It does **not**
need the `TfLensAppManagerApiKey`/`Secret` pair — see the note in the configuration table.

---

## Configuration reference

Every setting is **PascalCase with no separators**: `TfLensDbConnection`, never `TFLENS_DB_CONNECTION`
and never `TfLens__DbConnection`. A custom configuration provider
(`src/TfLens/Configuration/PascalCaseEnvironmentConfigurationSource.cs`) maps `TfLens*` environment
variables onto `TfLens:*` configuration keys, which is why application code reads
`IConfiguration["TfLens:DbConnection"]` and never calls `Environment.GetEnvironmentVariable`.

| Setting | Required | What it is |
|---|---|---|
| `TfLensDbConnection` | **Yes** | Npgsql connection string. Startup fails without it, and fails again if the database is unreachable. |
| `TfLensAppManagerApiKey` | No* | AppManager API key. |
| `TfLensAppManagerApiSecret` | No* | AppManager API secret. |
| `TfLensGitHubToken` | No | GitHub PAT. **Raises the rate limit only** (60/hr → 5,000/hr); it grants no access a public request would not have. |
| `TfLensAppManagerBaseUrl` | No | Defaults to `https://appmgrapi.techierathore.com`. |
| `TfLensAppManagerAppId` | No | Defaults to `1`. |
| `TfLensDataRoot` | No | Defaults to `data`. Holds `raw/`, `reports/`, `prices.json`. |
| `TfLensPollIntervalMinutes` | No | Defaults to `15`. |
| `TfLensStalenessDays` | No | Defaults to `7`. Drives the Coverage staleness warning. |

> **\* The AppManager key/secret pair is optional, but must be supplied _whole or not at all_.**
> AppManager resolves the application from the `applicationId` the client sends in every request body,
> so with **no** pair configured every call succeeds. A **half-configured or wrong** pair is rejected
> with `401 INVALID_API_KEY` on every call, so `TfLensOptions.Validate()` refuses that configuration at
> startup rather than letting every sign-in fail mysteriously. Recorded as `D-006` in
> [`DECISIONS.md`](../DECISIONS.md).

### Where to put each one

| Context | Mechanism |
|---|---|
| **Local development — the default answer** | **User secrets.** Visual Studio: right-click the TfLens project → *Manage User Secrets*. Shell: `dotnet user-secrets set "TfLens:AppManagerApiKey" "…" --project src/TfLens`. Template: [`src/TfLens/secrets.example.json`](../src/TfLens/secrets.example.json). |
| Shell, one session only | `export` / `$env:` before `dotnet run` — beats user secrets, so useful for a one-off override, easy to forget you set it |
| Docker / deployment | PascalCase environment variables on the container — see `docker-compose.yml`; `.env` supplies them to Compose |
| ~~Launch profile~~ | **Don't.** A launch profile sets an *environment variable*, the highest-priority source, so it silently overrides your user secrets. This was tried and reverted — see the note above and `DeveloperOnboardingTests`. |
| ~~`appsettings.Development.json`~~ | **Never for secrets.** It is committed. `ConfigurationHygieneTests` fails the build if a `TfLens` secret key appears in any `appsettings*.json`. |

---

## Troubleshooting

### `TfLens cannot start — the database connection string is not configured`

The app has no `TfLensDbConnection`. Either you are on the `TfLens (own database)` launch profile, or
you are running the binary directly without an environment. The error message itself lists the exact
commands for your platform. See [Running TfLens locally](#running-tflens-locally).

### `TfLens cannot start — the database named by TfLensDbConnection is unreachable`

The connection string is set but nothing answered. In order of likelihood:

1. **The container isn't running.** `docker compose up -d postgres`, then check with
   `docker ps --filter name=tflens-postgres`.
2. **`.env` is missing**, so Compose could not interpolate `TfLensDbPassword` and never started the
   service. Copy `.env.example` to `.env`.
3. **Port 5433 isn't published.** Production compose deliberately keeps Postgres off the host network;
   `docker-compose.override.yml` publishes `5433:5432` for local development and Compose merges it
   automatically. If you ran `docker compose -f docker-compose.yml up`, the override was skipped.
4. **Windows + Docker in WSL.** `localhost:5433` normally works from Windows thanks to WSL2 port
   forwarding. If it does not, confirm the container is up inside WSL and that Docker Desktop's WSL
   integration is enabled.
5. **Something else owns 5433.** `docker ps` and look for another Postgres.

### `TfLens cannot start — TfLensAppManagerApiKey and TfLensAppManagerApiSecret must be set together`

You supplied one half of the pair. Supply both or neither — see the note in
[Configuration reference](#configuration-reference).

### Sign-in fails for a known-good password

Sign-in calls the live AppManager API, so it needs outbound network access. The UI deliberately shows
one generic message for every failure except `ACCOUNT_LOCKED` — a failed sign-in must not reveal
whether an account exists (BRD-90). **The real AppManager error code is in the log**, never on screen:
look for `Sign-in refused with {Code}` in `logs/tflens-*.log`.

### A page shows a table with fewer rows than the database has

Almost certainly `DataTable` truncation. `ShowPagination="false"` only hides the pager — the table still
renders `InitialPageSize` rows (default **5**) and drops the rest silently. Set `InitialPageSize`
explicitly on every fixed-row table.

### An icon is missing and there is no error

`LucideIcon` resolves only canonical Lucide names. The package's own `lucide.json` ships 212 aliases,
but the component ignores them and renders an invisible placeholder. `alert-triangle`, `check-circle`,
`help-circle`, `alert-circle` and `x-circle` are all aliases — use `triangle-alert`, `circle-check`,
`circle-question-mark`, `circle-alert`, `circle-x`.

### A per-user preference does not stick

Two traps, both already worked around, both worth knowing:

1. **A cookie name may not contain `:`.** ASP.NET Core silently drops such cookies from the request —
   the browser stores and sends them quite happily, so the failure is invisible from the client. The
   cookies are `tflens-theme`, `tflens-framework`, `tflens-sidebar`.
2. **`IHttpContextAccessor.HttpContext` is null inside an interactive Blazor Server circuit**, because
   the circuit outlives the request that created it. Preferences are therefore read back from the
   browser (`ShellPreferences.SyncFrameworkFromBrowserAsync`), not from the request.

### A report page shows no data although rows exist in the database

`MemoryAnalysisCache` is keyed on the `SyncState` version. If you insert stream rows straight into
Postgres without touching `SyncState`, the cached analysis stays stale. Restart the app, or sync/rebuild.

### `rebuild` in the container replays 0 files, but works on the dev machine

`TfLensDataRoot` defaults to the **relative** path `data`, which each host resolves differently:

| How you run it | Where the raw archive actually lives |
|---|---|
| `dotnet run --project src/TfLens` | `src/TfLens/data/` — relative to the **project** directory |
| Docker / compose | `/app/data`, bind-mounted to `./data` at the **repository root** |

So a dev machine and a container do not share a raw archive unless you point them at the same place —
set `TfLensDataRoot` explicitly if you want that. This matters because **the raw archive, not the
database, is the source of truth**: `rebuild` replays whatever is under `raw/`, so replaying in a
container that has never synced legitimately reports "0 files replayed".

> **Both locations are gitignored, and one of them must stay that way.** `data/keys/` holds the ASP.NET
> Data Protection key ring that encrypts the stored AppManager tokens and the auth cookie. The original
> ignore rule was root-anchored (`/data/`) and did **not** cover `src/TfLens/data/`, so a dev machine
> was writing key material into a non-ignored directory. The rule is now `data/` + `**/data/`. If you
> cloned before 2026-08-27, check whether any `key-*.xml` was ever committed — and if so, rotate by
> deleting the ring; existing sessions will simply be re-issued on next sign-in.

### The export says NOT QUOTABLE

Correct, and not a bug. No parity run has been recorded yet (`data/parity-last.json` does not exist).
Run the BRD §13 parity procedure; the banner turns green only when a passing run covers the current
`ParserVersion.Current`.

---

## How the solution fits together

```
src/TfLens          Blazor Server head — pages, shell, auth, background sync, command verbs
src/TfLens.Core     the engine: AppManager client, repo registry, GitHub fetch, parse, store,
                    metrics, export, Playbook adapter. No web dependency, unit-testable alone.
tests/              Core.Tests (263) · Guardrails.Tests (45) · Integration.Tests (16)
database/           001-schema.sql — idempotent, applied at every startup
tools/              parity-compare.py — key-by-key diff against tf-metrics.sh
data/               raw/ (rebuild source of truth) · reports/ · prices.json
```

Two rules shape almost every file, and are worth internalising before changing anything:

- **`UserId` is a parameter, never a filter.** Every store read, every engine call, the raw-archive path
  and the reports path all take it (ADR-013). A cross-user leak should be a compile-time absence rather
  than a forgotten `WHERE`.
- **The result type forbids the wrong number.** `AnalysisResult` has no member that can hold a
  cross-`project_type` or cross-provenance rate, and `Figure` has no way to print a number for its
  `InsufficientData` / `NotApplicable` cases (ADR-007). Always render a figure through
  `Components/Shared/FigureText.razor`.

---

## Command verbs

The same binary that serves the UI runs the verbs, so a parity run exercises production code (ADR-005):

```bash
dotnet run --project src/TfLens -- sync    --user 2
dotnet run --project src/TfLens -- rebuild --user 2
dotnet run --project src/TfLens -- export  --user 2 --framework techieflow
```

In a container: `docker exec tflens dotnet TfLens.dll rebuild --user 2`.
Visual Studio ships `sync verb` and `rebuild verb` launch profiles for the same thing.

---

## Tests

```bash
dotnet test                                     # all three projects
dotnet test tests/TfLens.Core.Tests             # engine, parser, store
dotnet test tests/TfLens.Guardrails.Tests       # coding standards + secret-leak checks, as tests
dotnet test tests/TfLens.Integration.Tests      # boots the real host; cross-user isolation over HTTP
```

The integration and store tests need the live PostgreSQL. They read `TfLensDbConnection` and otherwise
fall back to the documented local compose service.

---

## Screen-by-screen reference

See [`TfLens-DevGuide-Screens.md`](./TfLens-DevGuide-Screens.md) for the per-screen trace: every route,
its controls, the service and SQL behind each, its states, and its known gotchas.
