<!-- ============================================================================
     TfLens — DECISIONS.md

     HOW TO USE THIS FILE (read before appending)

     This is an append-only log. Nothing here is ever rewritten or deleted; a
     decision that stops being true is marked **Superseded by D-nnn** and the
     replacement is appended at the bottom of its section. That is what makes a
     figure traceable months later: the record of *why* a column, key or rule is
     the way it is survives the change that replaced it.

     Sections, and who appends to which:

       §1 Storage          — the store choice and its consequences.
       §2 Dedupe keys      — the natural key of every stream. Change one and the
                             counts change, so every change needs an entry here.
       §3 Parser version   — the version scheme and every bump, with its reason.
       §4 Operations       — configuration, secrets, container and deployment.
       §5 Cut for the timebox — what was deliberately not built, and the trigger
                             that would make it worth building.
       §6 Parity runs      — ONE ENTRY PER PASSING RUN (BRD §13 step 6). Append
                             at the END of the section, newest last. Use the
                             template at the top of the section verbatim; the
                             /export page's quotable banner reads the same facts
                             from data/parity-last.json.
       §7 Playbook schema discovery — the field names observed in a real
                             events.ndjson, recorded BEFORE any PbEvent column is
                             fixed (ADR-010). Append one entry per file examined.

     Numbering: D-001 upward, never reused, allocated in append order across the
     whole file. When you append, take the next free number — check the index.

     Every entry carries: id, date (ISO), decision in one line, reason, and what
     it supersedes or is superseded by.
     ============================================================================ -->

# TfLens — Decisions

The record of every decision that shapes the data TfLens stores, the numbers it computes and the way
it is operated. Architectural decisions with an `ADR-nnn` id live in
`docs/TfLens-Architecture.md` §10 and are cross-referenced here rather than repeated.

## Index

| Id | Section | Date | Decision |
|----|---------|------|----------|
| D-001 | Storage | 2026-08-26 | PostgreSQL 16 via Npgsql + Dapper, superseding SQLite |
| D-002 | Storage | 2026-08-26 | The raw JSONL archive is the source of truth; the database is disposable |
| D-003 | Storage | 2026-08-26 | Idempotent schema script at startup instead of a migration framework |
| D-004 | Dedupe keys | 2026-08-26 | The four stream dedupe keys |
| D-005 | Parser version | 2026-08-26 | Parser-version scheme and the rule for bumping it |
| D-006 | Operations | 2026-08-26 | The AppManager API-key pair is required whole or not at all |
| D-007 | Operations | 2026-08-26 | Local Postgres port publish lives in `docker-compose.override.yml`, not the deploy file |
| D-008 | Operations | 2026-08-26 | Secrets reach the process only through the PascalCase environment provider |
| D-009 | Operations | 2026-08-26 | `/healthz` reads `SyncState` directly rather than widening `ITelemetryStore` |
| D-010 | Cut for the timebox | 2026-08-26 | What was deliberately not built in this release |
| D-011 | Dedupe keys | 2026-08-26 | The Playbook `events.ndjson` dedupe key, superseding the provisional row in D-004 |
| S-001 | Playbook schema discovery | 2026-08-26 | No `events.ndjson` found; shape taken from the emitter source instead |
| P-002 | Parity runs | 2026-08-27 | Parity re-run and re-recorded at parser 1.1.0 after the metric addition |
| P-003 | Parity runs | 2026-08-28 | Parity re-run and re-recorded at parser 1.2.0 after the oracle learned the `misses` stream |
| D-012 | Provenance | 2026-08-28 | An unrecorded token count stays unmeasured, even though the reference averages it as zero (TF-005) |

---

## §1 Storage

### D-001 — PostgreSQL 16 via Npgsql + Dapper, superseding SQLite

**Date:** 2026-08-26 · **Supersedes:** the original SQLite choice (ADR-002) · **See:** ADR-015

The store is **PostgreSQL 16**, reached with **Dapper over Npgsql**, with hand-written DDL in
`database/001-schema.sql`. There is no ORM and no migration framework.

**Reason.** TfLens runs in a container whose data directory is a mounted volume, and SQLite on volume
storage is unreliable — file locking and `fsync` semantics differ enough across host filesystems to
risk corruption under the background poller's concurrent writes. Dapper stays because the
parity-auditable, hand-written SQL *is* the point: every figure must be traceable to a query someone
can read.

**Consequences that bind the rest of the app.**

- Every identifier in DDL and in every query is **double-quoted PascalCase** (`"Gate"."ReqId"`).
  PostgreSQL folds unquoted identifiers to lower case, which would silently destroy the naming the
  Coding Standards fix. Column names are the SCHEMA.md field names in PascalCase
  (`req_id` → `"ReqId"`, `cost_usd` → `"CostUsd"`).
- Upserts are `INSERT … ON CONFLICT DO NOTHING` against the unique indexes that encode the dedupe
  keys in §2, so the dedupe rule lives in one place — the index — rather than in parser code that
  could disagree with it.
- Absent optional fields are stored `NULL`, never `0` and never `false`. "Not captured" must stay
  distinguishable from "zero" at the column level.
- Anything the parser does not recognise for a stream — and every property of a record with `v > 1` —
  goes verbatim into the `jsonb` `"Overflow"` column rather than being dropped. The payload itself is
  never rendered; only the distinct field names are reported.

### D-002 — The raw JSONL archive is the source of truth; the database is disposable

**Date:** 2026-08-26 · **See:** ADR-015, BRD-19, BRD-20

Every fetched stream file is written byte-for-byte to
`data/raw/<userId>/<owner>__<name>/<stream>-<sha>.jsonl` **before** it is parsed. No
re-serialization, no normalization. `rebuild` truncates every stream table and replays that archive
in `(user, repo, sha-fetch-order)`, reading only from disk and never from the GitHub API.

**Reason.** A parser bug is recoverable if the bytes survive it, and unrecoverable if they do not. It
also makes the correctness story cheap: a rebuild run immediately after a live sync must produce
identical per-stream counts, which is a single assertion covering the entire dedupe and parse path.

**Consequence.** `data/` is what gets backed up. Postgres never does.

### D-003 — Idempotent schema script at startup instead of a migration framework

**Date:** 2026-08-26 · **See:** ADR-015

`database/001-schema.sql` is built entirely from `CREATE … IF NOT EXISTS` and is applied on **every**
startup. Re-running it against an existing database is a no-op. There is no EF migrations history, no
`__EFMigrationsHistory` table and no versioned migration chain.

**Reason.** The store is disposable (D-002), so the usual reason for migrations — preserving data
across a schema change — does not apply. When the schema grows, the change is: edit the script, drop
the tables, `rebuild`. Adding a migration framework would add ceremony and a second source of truth
about the schema.

---

## §2 Dedupe keys

### D-004 — The four stream dedupe keys

**Date:** 2026-08-26 · **See:** BRD-26..28, REQ-FN-033..035

These keys decide the counts. Changing one changes every figure derived from that stream, so a change
here requires a new entry in this section, a parser-version bump (§3) and a fresh parity run (§6).

| Stream | Natural key | Collision rule | Encoded as |
|--------|-------------|----------------|-----------|
| `commits` | `(UserId, Repo, Sha)` | Keep the first; count the collapsed duplicates. **Per repo, not across repos** — two repositories may legitimately share a short SHA. | `UcCommitUserRepoSha` |
| `sessions` | `(UserId, Repo, SessionId)` | Keep the record with the **highest `output_tokens`**; on a tie, the **latest `ts`**. OpenCode rewrites a session record as it grows, so records are cumulative snapshots and replaying an earlier one must never lower the stored figure. | `UcSessionUserRepoId` |
| `runs` | `(UserId, Repo, Ts, App, Cmd)` | Keep the first. A run is identified by when it happened, for what app, and which command. | `UcRunIdentity` |
| `gates` | `(UserId, Repo, Ts, App, ReqId, RunId)` | Keep the first. | `UcGateIdentity` |
| `pb_events` *(Phase 3)* | Superseded by **D-011**. | — | — |

`UserId` and `Repo` are part of every key, not a filter applied afterwards. Two users connecting the
same public repository get two fully independent sets of rows, and one user's re-parse can never
collapse another user's record.

Nullable key components are coalesced to `''` inside the index so PostgreSQL's "NULLs are distinct"
rule cannot let a duplicate through the back door.

### D-011 — The Playbook `events.ndjson` dedupe key, superseding the provisional row in D-004

**Date:** 2026-08-26 · **See:** REQ-FN-065, REQ-FN-068, ADR-010 · **Evidence:** S-001 (§7)

The provisional key `(UserId, Repo, Ts, EventType, SessionId)` in D-004 was written before anything was
known about the file. S-001 shows it is not merely arbitrary but **wrong in a way that inflates every
token and cost figure**, so it is replaced here.

The Playbook's telemetry plugin appends a fresh `turn` record on **every** `message.updated` event. One
assistant message therefore produces many rows as it streams, each a larger snapshot of the same
message, and only the last carries its final token and cost counts. Keying on `ts` makes every one of
those snapshots a distinct row, and summing them multiplies the totals by however many times the
message happened to be flushed. The Playbook's own joiner avoids this by keeping "only the LAST turn row
per `messageID`", and TfLens now does the same.

| Record kind | Natural key | Collision rule | Encoded as |
|-------------|-------------|----------------|-----------|
| `turn` | `(UserId, Repo, MessageId)` | Keep the record with the **highest output tokens** (`tokens.output + tokens.reasoning`); on a tie, the **latest `ts`**. Identical in shape to the `sessions` rule, and for the identical reason: the records are cumulative snapshots and replaying an earlier one must never lower the stored figure. | `UcPbEventTurn` (partial: `WHERE "MessageId" IS NOT NULL`) |
| `phase-start`, `phase-end` | `(UserId, Repo, Kind, Ts, SessionId)` | Keep the first. These records carry no `messageID` and are not snapshots. | `UcPbEventMarker` (partial: `WHERE "MessageId" IS NULL`) |

Because the turn rule keeps the largest rather than the first, the store's upsert for `"PbEvent"` rows
carrying a `MessageId` must be `ON CONFLICT … DO UPDATE` keeping the greater `TokensOutput` — **not**
the `DO NOTHING` the other four streams use. A `DO NOTHING` there would freeze the first partial
snapshot of any message that was still streaming when the file was fetched.

---

## §3 Parser version

### D-005 — Parser-version scheme and the rule for bumping it

**Date:** 2026-08-26 · **See:** BRD-68, REQ-FN-060

The parser version is a single semantic-version constant, `TfLens.Core.ParserVersion.Current`,
stamped into the build and into **every** export.

| Component | Bump when |
|-----------|-----------|
| **Major** | A stored column changes meaning or disappears, or a dedupe key changes (§2). Old exports are no longer comparable to new ones. |
| **Minor** | A field is newly recognised (moves out of `"Overflow"` into a column), or a metric is added. Old exports stay comparable for the metrics they carry. |
| **Patch** | A defect fix that changes no correct output — or changes an output that was wrong. |

**Current:** `1.2.0`.

| Version | Date | Why |
|---------|------|-----|
| `1.0.0` | 2026-08-26 | The first shipping parser. |
| `1.1.0` | 2026-08-27 | **Minor — a metric was added:** `pooled.session_duplicates_collapsed`. The reference gained the same figure when `tf-metrics.sh` learned to de-duplicate the sessions stream (SCHEMA.md §4), and TfLens must emit every key the reference emits or the compare fails on a MISSING key. Nothing stored changed meaning and no dedupe key moved, so this is not a major bump; old exports stay comparable for the metrics they carry. Per the rule below the bump un-quoted every export until parity was re-run — see §6 P-002. |
| `1.2.0` | 2026-08-28 | **Minor — metrics were added:** the whole `misses` block (29 figures), plus `per_repo.misses` and `per_repo.stale_types`. The reference gained them when `tf-metrics.sh` learned to read the fifth stream (`STREAMS` gained `misses`, and with it `analyse_misses()`), and TfLens must emit every key the reference emits or the compare fails on a MISSING key. Nothing stored changed meaning and no dedupe key moved, so this is not a major bump. The changed oracle had already un-quoted every export on its own — see §6 P-003. |

**The rule that makes the version worth stamping:** the weekly snapshot export is quotable only if
the last parity run on record (§6) postdates the last parser-version bump. The `/export` page renders
this as the quotable / not-quotable banner by comparing the stamp in the export against the stamp in
`data/parity-last.json`. Bumping the version therefore *un-quotes* every export until parity is
re-run, which is exactly the intent.

---

## §4 Operations

### D-006 — The AppManager API-key pair is required whole or not at all

**Date:** 2026-08-26 · **See:** BRD-9, BRD-97, REQ-FN-010, REQ-FN-038

`TfLensDbConnection` is **unconditionally required**: startup fails without it.

`TfLensAppManagerApiKey` and `TfLensAppManagerApiSecret` are validated as a **pair**. Both set is
valid. Both unset is valid. Exactly one set fails startup with a redacted message.

**Reason — verified against the live AppManager API, not inferred from the guide.** Half a pair does
not work: AppManager rejects a key without its matching secret with `401 INVALID_API_KEY` on every
call, which would turn every sign-in, registration and password reset into an authentication failure
that looks like a user error. That is precisely the silent misconfiguration BRD-9 exists to prevent,
so it is refused at startup instead.

**Amended 2026-08-27 — "the pair is optional" was too broad, and the pair is now configured.** The
original entry said the headers were optional because the client sends `applicationId: 1` in every
request body. That is true for *most* endpoints and false for exactly two. Measured live, whole pair
versus none:

| Endpoint | No pair | With pair |
|---|---|---|
| `/AuthSvc/login`, `/AuthSvc/validate`, `/UserSvc/change-password` | `200` / unchanged | identical |
| `/AuthSvc/forgot-password` | `400 APPLICATION_ID_REQUIRED` | **`200`** |
| `/AuthSvc/reset-password` | `400 APPLICATION_ID_REQUIRED` | **reaches real token validation** |
| `/UserSvc/profile` | `200` | **`403 NO_APP_ACCESS`** |

Two consequences. First, the pair is **required** for this app, not optional: the forgot/reset
endpoints refuse a body `applicationId` and accept the app scope *only* from the header, so without
the pair those two features cannot work at all (REQ-FN-003 was blocked on exactly this until the owner
supplied credentials on 2026-08-27). Second, the pair must **not** be sent everywhere: attaching an
application identity to a token-scoped user read turns it into an application-access check, and
AppManager answers `NO_APP_ACCESS` for the demo account. `AppManagerClient.SendsApiKeyHeaders` therefore
sends the pair on `/AuthSvc/*` only. That is right on its own terms — an application credential does
not belong on a request the bearer token already scopes — and it also sidesteps an AppManager-side
grant gap (the demo user holds no access row for Application 1, the same gap that makes its
`applicationRole` come back empty). Granting that access in the AppManager admin UI is the root fix and
is recorded under PROJECT-STATUS "Known blockers".

**Amended 2026-08-28 — the `/UserSvc/*` exclusion is REVERSED; the pair now goes on every path.** The
amendment above was right on its evidence and is now wrong on the facts, so it is corrected rather than
rewritten. Its `/UserSvc/profile` row — *none → `200`, pair → `403 NO_APP_ACCESS`* — described an
**AppManager-side defect**, reported as AM-002 in `docs/TfLens-AppManager-Feedback.md` and fixed by the
owner on 2026-08-28 together with AM-001. Re-measured live against
`https://appmgrapi.techierathore.com` that day:

| Endpoint | No pair | With pair |
|---|---|---|
| `/UserSvc/profile` | `200`, but `applicationRole` is an **empty string** | **`200` with `applicationRole: "Manager"`** |

Everything else in the table above still holds. The exclusion therefore reversed sign: while the defect
stood it kept the Profile page alive, and once the defect was fixed it became the only reason TfLens
could not read an application role AppManager was finally willing to give it. `SendsApiKeyHeaders` now
returns `true` for `/UserSvc/*` as well as `/AuthSvc/*` — every path this client calls.

The original reasoning — *"an application credential does not belong on a request the bearer token
already scopes"* — is retired as a general principle for this API. AppManager treats the pair as the
**application selector**, not as a second authorisation: without it there is no application to scope the
role to, which is precisely why the field comes back empty. Two consequences worth keeping: a
`NO_APP_ACCESS` on this path in future means AM-002 has regressed, and the pair being required
whole-or-not-at-all (the decision this entry is actually about) is unchanged.

**Known tension to re-open if the acceptance is read strictly.** REQ-FN-010's acceptance line reads
"a missing key or secret fails startup (REQ-FN-038)". Read literally that would make both variables
unconditionally required. It is recorded here as a deliberate deviation, on the evidence above: an
unconditional requirement would force operators to invent credentials that AppManager does not need,
and would make the working "no pair" configuration unlaunchable. If the owner wants the literal
reading, the change is one comparison in `TfLensOptions.Validate()` — and this entry should be
superseded rather than edited.

### D-007 — The local Postgres port publish lives in `docker-compose.override.yml`

**Date:** 2026-08-26 · **See:** REQ-FN-045, Architecture §9

`docker-compose.yml` — the file the deploy uses — publishes **no** Postgres port. The database is
reachable only from inside the compose network, so it has no inbound exposure at all.
`docker-compose.override.yml`, which Compose merges automatically for local work, publishes
`5433:5432`.

**Reason.** A host-run `dotnet run` and the integration tests need to reach the same database the
container uses, and the alternatives were both worse: publishing the port in the deploy file would
expose the database in production to buy a development convenience, and a separate
`docker-compose.dev.yml` would need an explicit `-f` that someone eventually forgets. The override
file is the mechanism Compose provides for exactly this split. The production deploy opts out
explicitly with `docker compose -f docker-compose.yml up`.

**Consequence.** `5433`, not `5432`, is the documented local port everywhere — README, `.env.example`
and the integration tests.

### D-008 — Secrets reach the process only through the PascalCase environment provider

**Date:** 2026-08-26 · **See:** BRD-8, BRD-10, REQ-FN-037, REQ-NFR-003

Environment variables are spelled `TfLensDbConnection` — PascalCase, no separators, not
`TFLENS_DB_CONNECTION` and not `TfLens__DbConnection`. A custom configuration provider
(`PascalCaseEnvironmentConfigurationSource`) maps `TfLens<Name>` onto the configuration path
`TfLens:<Name>`, which binds to `TfLensOptions`.

**Reason.** One spelling, documented once, that matches the option property names exactly — so
`TfLensAppManagerApiKey` and `TfLensOptions.AppManagerApiKey` cannot drift apart. The provider is
also the single place that touches the environment, which turns "no code reads a secret from
anywhere else" into a one-line grep rather than a code review.

**The invariants this buys, each enforced by a test in `tests/TfLens.Guardrails.Tests`.**

- No secret value exists in `appsettings*.json` or any other committed file.
- `Environment.GetEnvironmentVariable` is called nowhere outside the provider itself.
- No log statement, rendered value or exported field can carry a token, PAT, API secret, password or
  connection string.

**Amendment 2026-08-28 — which route, for which audience (REQ-NFR-011).** D-008 fixes the *spelling*
and the *single entry point*, and it says nothing about where a human puts the value. That gap had a
cost: every document opened with `copy .env.example .env`, which reads as "this is the app's settings
file" — and it is not. **Nothing in `Program.cs` parses `.env`.** It is `docker compose`'s
interpolation file, needed before any container starts so Compose can expand `${TfLensDbPassword}`
into the `postgres` service. A developer editing it for an F5 run was editing a file the process never
opens.

The two routes are therefore named explicitly, by audience:

- **Local development (F5 / `dotnet run`) → user secrets.** `UserSecretsId=tflens-dev-secrets`, so
  `secrets.json` lives outside the repository and cannot be committed. Committed placeholder template:
  `src/TfLens/secrets.example.json`.
- **Deployment (`docker compose`, CI/CD) → PascalCase environment variables**, supplied to Compose
  from `.env`.

Both land on the same `TfLens:*` keys through the same provider, so D-008's invariants are untouched —
user secrets were already an approved source under BRD-8 ("environment / **user-secrets**").

**`appsettings.Development.json` was considered and rejected** as the local path, despite being the
most convenient file to edit. It is *committed*: a real `sk_live_…` placed there enters git history,
where deleting it later does not remove it. `ConfigurationHygieneTests` already fails the build when a
`TfLens` secret key appears in any `appsettings*.json`, and that test stays. Convenience that trades a
credential into version control is not convenience.

Three guardrail tests in `DeveloperOnboardingTests` pin this: the `UserSecretsId` must be declared
(losing it breaks the F5 path *silently* — the app still starts on the Development connection fallback,
the AppManager pair simply never arrives), the template must hold no real credential, and the Developer
Guide must name the user-secrets mechanism.

### D-009 — `/healthz` reads `SyncState` directly rather than widening `ITelemetryStore`

**Date:** 2026-08-26 · **See:** BRD-78, REQ-FN-041

`/healthz` must report two facts and nothing else: database reachability, and the age of the last
successful sync. Reachability comes from `ITelemetryStore.PingAsync`. The sync age comes from a
single scalar query — `SELECT MAX("LastSyncTs") FROM "SyncState" WHERE "LastError" IS NULL` — issued
from the health endpoint itself.

**Reason.** The age is a **cross-user** fact, and every method on `ITelemetryStore` deliberately takes
`userId` as a mandatory parameter (ADR-013). Adding an unscoped read to that interface would put the
one method that ignores `userId` next to the twenty that require it — exactly the shape ADR-013
exists to prevent, and an inviting mistake for the next person. Keeping the query at the endpoint
leaves the store's contract uniformly user-scoped.

**Constraints on the endpoint.** It is anonymous; it discloses no version, no configuration, no repo
name and no user data; it returns `503` when the database is unreachable; and a failure to read the
age degrades to `null`, never to an error and never to a leaked exception message.

---

## §5 Cut for the timebox

### D-010 — What was deliberately not built in this release

**Date:** 2026-08-26 · **See:** BRD §3, ADR-012, ADR-013

Each of these was considered and cut. The trigger column says what would make it worth revisiting —
none of them is a "someday" item without one.

| Cut | Why | Trigger to revisit |
|-----|-----|--------------------|
| **GitHub SSO** | AppManager v1.4 has no external-login or token-exchange endpoint. The only bridge would be a TfLens-held per-user random credential, which the owner declined. | AppManager ships an SSO or token-exchange endpoint. |
| **Private GitHub repos** | Would require accepting, encrypting and rotating a per-user PAT — a whole credential-lifecycle feature, for a release whose point is the metrics engine. | Demand for private repos, plus a decision on per-user token storage. |
| **Per-user GitHub PAT** | Same reason. The single server PAT raises the rate limit for public reads and grants nothing else. | As above. |
| **Migration framework** | The store is disposable (D-002, D-003). | The database ever becomes the source of truth — which would itself need a decision entry. |
| **Octokit** | Three `GET` calls per repo. A typed SDK adds a dependency and hides the `Accept: application/vnd.github.raw` header the whole-file fetch depends on. | The GitHub surface grows past a handful of endpoints. |
| **Vector store / RAG** | TfLens has no AI feature of any kind (ADR-003). | A requirement that actually needs one. There is none today. |
| **Roles beyond `Manager`** | Every TfLens user is a `Manager` for Application 1. No licence, feature-flag, payment or issue endpoint is called. | A feature that genuinely differs by role. |
| **Sharing a user's reports with another user** | Cross-user visibility is the one thing the isolation model must never allow by accident. Adding sharing means adding an explicit grant model, not relaxing the isolation. | An explicit sharing requirement, designed as a grant rather than as a filter that can be forgotten. |
| **Charting library** | Every figure must have a text equivalent anyway; the tables are the deliverable and the charts are supplementary. | The tables exist and are proven correct. |
| **Playbook report set** | Phase 3. The `events.ndjson` shape is unknown, so its columns are provisional (§7). | A real `events.ndjson` to parse. |
| **Non-root container user** | The image runs as root. `data/` and `logs/` are host bind mounts, and a non-root uid would fail to write them unless the operator chowns both directories first — an operational trap that produces a container which starts and then silently cannot archive. The app has no inbound API and no upload path, so the blast radius of the container user is small. | The volumes move to named volumes, or the deploy adds a documented `chown` step. Then switch to `USER $APP_UID`. |
| **Content-Security-Policy header** | Blazor Server's framework script and TrBlazeUI's interop would need a nonce pipeline to survive a real CSP, and a policy relaxed until it works is worse than none — it reads as protection while providing none. The other four headers (`X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`) ship today. | A nonce-emitting render pipeline, or a Blazor release that ships one. |

---

## §6 Parity runs

<!-- APPEND ONE ENTRY PER PASSING RUN, NEWEST LAST. Copy the template verbatim.
     A run is only recorded here when the diff was EMPTY. A failing run is a bug
     in TfLens, not an entry in this log (BRD §13 step 5).
     The same facts go into data/parity-last.json, which the /export page reads
     for its quotable / not-quotable banner. -->

**Template — copy this block, do not improvise the fields:**

```
### P-nnn — Parity run YYYY-MM-DD

- **Date (UTC):**
- **Framework:**                 techieflow | playbook
- **User id:**
- **Dataset (repo → SHA):**      owner/name → <full sha>   (one line per repo)
- **tf-metrics.sh sha256:**
- **TfLens parser version:**
- **Reference file:**            reference.json
- **TfLens export:**             data/reports/<userId>/<date>/tflens.json
- **Compare output:**            (paste tools/parity-compare.py output verbatim — an empty diff still prints its summary)
- **Extras spot-checked:**       yes/no — which metrics, against which raw file
- **Verdict:**                   PASS (empty diff)
```

### P-001 — Parity run 2026-08-27

- **Date (UTC):**                2026-08-27
- **Framework:**                 techieflow
- **User id:**                   2
- **Dataset (repo → SHA):**      `techierathore/TechieBlog` → `30e66161343661d94b8bd4b01e97c63a30b1c579`
- **Dataset (repo → SHA):**      `techierathore/TechieFlow` → `708fcffbdf1c61cf327fc3e0038291bce40091d6`
- **Dataset (repo → SHA):**      `techierathore/TechieRag` → `4f6f0a3796481e01c408b6d93eb72d90ecb0176b`
- **Dataset (repo → SHA):**      `techierathore/TrBlazeUI` → `49cf7a73f3f78219abccd1ecab49db797315a16c`
- **tf-metrics.sh sha256:**      `sha256:960d12b497f5093e98f696800805e8ceb70efb63c2560489d99fa96fe5c03f3c`
- **TfLens parser version:**     1.0.0
- **Reference file:**            `tests/.artifacts/parity/reference.json`
- **TfLens export:**             `src/TfLens/data/reports/2/2026-08-27/techieflow/tflens.json`
- **Compare output:**

```
parity-compare: reference=tests/.artifacts/parity/reference.json
parity-compare: tflens   =tests/.artifacts/parity/tflens.json

  (19 INFO lines: 4 × ENV-OK per_repo[*].repo — the reference echoes the filesystem path it was
   handed, TfLens uses owner/name; 12 × ADDED-OK per_repo[*].{framework,events,source_sha};
   1 × KEYED per_repo; 2 × ADDED-OK extras / parity)

parity-compare: 0 finding(s), 19 allowed difference(s).
parity-compare: PASS -- the two implementations agree key for key.
```

- **Extras spot-checked:**       no — unchanged since the REQ-FN-064 hand-check; `extras` carries no
                                 oracle and is ADDED-OK on this run as on every other.
- **Verdict:**                   PASS (empty diff). `src/TfLens/data/parity-last.json` written, and
                                 `/export` now reads **QUOTABLE**.

**What closed it.** Two things, in this order.

1. **The framework fixed the oracle.** `.tfcore/telemetry/tf-metrics.sh` moved from
   `326b586e…4412` to `960d12b4…3f3c`, gaining a `dedupe_sessions()` that implements the SCHEMA.md §4
   consumer rule (highest `output_tokens` per `session_id`, ties on latest `ts`) and a new
   `pooled.session_duplicates_collapsed`. Its own docstring now records that the §5 wording *"reasoned
   about one source of duplication and concluded there were none at all, which silently overstated
   every session and token figure."* **That is exactly the owner decision the failed run below asked
   for, resolved in the reference's favour — option (a).** BRD-27 stands; the two implementations now
   agree that this dataset holds 56 sessions.
2. **TfLens implemented the one genuine gap.** The reference dedupes sessions at *read* time and can
   therefore report what it collapsed. TfLens dedupes at *write* time — `UcSessionUserRepoId` plus
   `ON CONFLICT DO NOTHING` — so by the time the engine reads the store the duplicates are gone and a
   read-time count would be `0`. The figure is a property of **ingest**, so it is now persisted as one:
   a new `"SyncState"."SessionDuplicatesCollapsed"` column, measured as *session records presented
   minus rows stored*. That formulation is what catches the TechieFlow duplicate, which is spread
   across two archived `sessions.jsonl` snapshots and is invisible to any single parse. `rebuild`
   replays the whole archive and therefore **sets** the count (so running it twice does not double it);
   an incremental sync **adds** its own pass. `MetricsEngine` sums it over the framework's repositories
   only, and `SnapshotJson` emits it immediately after `commit_duplicates_collapsed`. Nothing was added
   to `tools/parity-compare.py`'s allow-lists — a MISSING key always fails, by design, and this one was
   closed by implementing it.

The dataset was rebuilt from the REQ-FN-027 raw archive and each repository given a
`.tfcore/core-config.yaml` carrying its true `project_type` (TechieBlog `app`, TechieFlow `framework`,
TechieRag `app`, TrBlazeUI `library`), which also cleared the two `per_repo[*].project_type` findings
the earlier stand-in dataset produced. The four `per_repo[*].repo` differences are the documented
environment keys and were accepted with `--allow-environment-keys`, as `ENVIRONMENT_KEYS` provides for.

**One environment note, not a finding.** `TfLensOptions.ReferenceScriptPath` defaults to the relative
`.tfcore/telemetry/tf-metrics.sh` and is resolved against the process working directory, which for
TfLens is `src/TfLens` — the directory that makes the equally relative `DataRoot` land on
`src/TfLens/data`. The oracle lives at the repository root, so from there the default path names no
file and a genuinely passing stamp degrades to NOT QUOTABLE with reason `script-unavailable`. The
smoke therefore boots with `TfLensReferenceScriptPath` set to the absolute path, which is what the
option exists for. Two relative paths anchored at one working directory cannot both be right when the
files they name live at different roots; whether the default should change is an owner call and was
left alone here, because `AbsentReferenceScriptIsNotQuotableAndDoesNotThrow` deliberately fixes the
current behaviour.

---

<!-- The block below is NOT a P-entry. §6 records passing runs only; this is the run log of a
     FAILED attempt. SUPERSEDED by P-001 above, which resolves it: the owner decision it asks for
     was taken upstream, in the reference's favour. Kept rather than deleted because it is the
     record of how the disagreement was found and what it cost. -->

**Run log — 2026-08-27, BRD §13 executed, did NOT pass (no P-entry, no `parity-last.json`).
⚠ SUPERSEDED — see P-001 above; the blocker described here is resolved.**

The procedure was run end to end for the first time. The oracle is present at
`.tfcore/telemetry/tf-metrics.sh` (sha256
`326b586e19cffedaeefd614125039085c574a1c5c2ae325ae26d69a839ca4412`); its `--rollup` mode is
read-only, so it was runnable here. The earlier claim in `PROJECT-STATUS.md` that the script "is not
present anywhere in this tree" was wrong.

- **Framework / user:** techieflow, user 2.
- **Dataset (repo → SHA, from `SyncState.LastSha`):**
  - `techierathore/TechieFlow` → `708fcffbdf1c61cf327fc3e0038291bce40091d6`
  - `techierathore/TrBlazeUI` → `49cf7a73f3f78219abccd1ecab49db797315a16c`
  - the five zero-record `tflenstest/Store*` repositories user 2 also has connected (no SHA — never synced)
- **Dataset construction:** `git clone` is not available to an agent, and it is not needed — REQ-FN-027
  archives every fetched stream verbatim *before* parsing, so the archive under
  `src/TfLens/data/raw/2/**` **is** the dataset. Each `<stream>-<sha>.jsonl` was materialised as
  `tests/.artifacts/parity/<Name>/docs/metrics/<stream>.jsonl`, concatenating every archived SHA for a
  stream because the store holds their union (verified: the concatenated record counts, after the
  documented BRD-26/27/28 dedupe keys, reproduce the database counts exactly — runs 42, gates 77,
  sessions 35). `.tfcore/core-config.yaml` and `docs/<App>-Checklist.md` stubs were written so the
  reference's `project_type()` and `app_name()` read the values the records already declare.
- **Result: 4 findings, 31 allowed differences. NOT an empty diff, so nothing was recorded and the
  banner correctly still reads NOT QUOTABLE.**

Five further findings (`per_repo[Store*].app` — reference `"StoreFramework"`, TfLens `null`) were a
genuine TfLens bug and were **fixed**, not allow-listed: the reference's `app_name()` falls back to
the repository's own directory name, and TfLens emitted `null` for any repository with no records.
`MetricsEngine.PerRepoFactsFor` now falls back to `UserRepo.Name`. Nothing was added to
`tools/parity-compare.py`'s allow-lists.

**The 4 remaining findings are one root cause, and it is a conflict between the reference and its own
SCHEMA.md — it needs an owner decision, not a code change.**

```
FAIL DIFF per_repo[TechieFlow].sessions        reference=21       tflens=20
FAIL DIFF pooled.sessions                      reference=36       tflens=35
FAIL DIFF pooled.tokens_total                  reference=7810195  tflens=7762638
FAIL DIFF pooled.tokens_per_verified_req       reference=156203.9 tflens=155252.8
```

`sessions-708fcffbdf1c61cf327fc3e0038291bce40091d6.jsonl` contains the **same line twice** (lines 9
and 10, byte-identical, `session_id` `cb2d3e32-ebbb-4cd6-8c64-1e8d81566179`, `input_tokens` 4361,
`output_tokens` 43196). The entire divergence is that one line: 21 − 20 = 1 session, and
7810195 − 7762638 = **47557 = 4361 + 43196** exactly.

- **TfLens collapses it** — BRD-27, and the `UcSessionUserRepoId` unique index, keep one record per
  `session_id`. That is a verbatim port of **SCHEMA.md §4**: *"the plugin appends a CUMULATIVE
  snapshot at every root-session idle … so several records may share a `session_id` — consumers take
  the record with the highest `output_tokens` (or the latest `ts`) per `session_id`"* (added
  2026-08-20).
- **`tf-metrics.sh` counts both** — `analyse()` dedupes `commits` only. Its `dedupe_commits()`
  docstring still asserts the older **SCHEMA.md §5** claim that *"runs/gates/sessions … cannot be
  independently reconstructed elsewhere, so a union merge has no way to manufacture a second copy of
  them"*. This dataset falsifies that claim.

So the reference does not implement its own §4 rule, and the two documents (§4 and §5) contradict
each other. BRD §13 step 5 says the script is never changed and any disagreement is a TfLens bug; but
making TfLens agree would repeal BRD-27, break the unique index and break idempotent replay
(re-running `rebuild` would double the count). `parity-compare.py`'s own contract forbids the third
option — `pooled.sessions` and `pooled.tokens_total` are **figures**, and *"no figure may ever be
added to this list"*.

**Owner decision required:** either (a) SCHEMA.md §5 is corrected and `tf-metrics.sh` is taught the §4
consumer rule — which only the owner may do, and which makes the diff empty; or (b) BRD-27 is
repealed. Until one of those happens, no TfLens figure is quotable, which is the correct and honest
state.

> **✅ RESOLVED 2026-08-27 — option (a) was taken, upstream.** `tf-metrics.sh` gained `dedupe_sessions()`
> and now implements the §4 consumer rule itself; its hash moved `326b586e…4412` → `960d12b4…3f3c`.
> BRD-27, the `UcSessionUserRepoId` index and idempotent replay all stand unchanged. The remaining gap
> was on the TfLens side — the reference also began emitting `pooled.session_duplicates_collapsed`,
> which TfLens did not — and that was implemented, not allow-listed. See **P-001** above for the
> passing run.

---

### P-002 — Parity run 2026-08-27 (re-run at parser 1.1.0)

- **Date (UTC):**                2026-08-27
- **Framework:**                 techieflow
- **User id:**                   2
- **Dataset (repo → SHA):**      unchanged from P-001 — `TechieBlog` → `30e66161…`, `TechieFlow` → `708fcffb…`, `TechieRag` → `4f6f0a37…`, `TrBlazeUI` → `49cf7a73…`
- **tf-metrics.sh sha256:**      `sha256:960d12b497f5093e98f696800805e8ceb70efb63c2560489d99fa96fe5c03f3c` (unchanged)
- **TfLens parser version:**     **1.1.0** (bumped from 1.0.0 — §3)
- **Compare output:**

```
parity-compare: 0 finding(s), 19 allowed difference(s).
parity-compare: PASS -- the two implementations agree key for key.
```

- **Figures agreed, key for key:** sessions 56 · tokens_total 14,846,715 · tokens_per_verified_req
                                 65,985.4 · commits 181 · session_duplicates_collapsed 2 ·
                                 commit_duplicates_collapsed 0.
- **Verdict:**                   PASS (empty diff). `src/TfLens/data/parity-last.json` re-written at
                                 parser 1.1.0; `/export` reads **QUOTABLE** again.

**Why this run exists.** P-001 passed at parser `1.0.0`, but the change that made it pass — emitting
`pooled.session_duplicates_collapsed` — is itself a metric addition, which D-005 makes a **minor**
bump. Leaving the constant at `1.0.0` would have left a stamp claiming a parity run that predated the
parser which produced the export. The bump was applied, and it did exactly what D-005 says it should:
the very next export read `parity NOT QUOTABLE` despite an otherwise-valid record on disk, because the
run on file no longer postdated the parser. Re-running the procedure and re-recording restored
**QUOTABLE**. The mechanism is therefore not merely implemented but demonstrated end to end, which is
the third clause of REQ-FN-063's acceptance.

---

### P-003 — Parity run 2026-08-28 (re-run at parser 1.2.0, first run covering the `misses` block)

- **Date (UTC):**                2026-08-28
- **Framework:**                 techieflow
- **User id:**                   2
- **Dataset (repo → SHA):**      `techierathore/TechieBlog` → `30e66161343661d94b8bd4b01e97c63a30b1c579`
- **Dataset (repo → SHA):**      `techierathore/TechieFlow` → `4f6f5bbafa01f0362fdf95f3ad3837a6f3aa2556`
- **Dataset (repo → SHA):**      `techierathore/TechieRag` → `4f6f0a3796481e01c408b6d93eb72d90ecb0176b`
- **Dataset (repo → SHA):**      `techierathore/TrBlazeUI` → `49cf7a73f3f78219abccd1ecab49db797315a16c`
- **tf-metrics.sh sha256:**      `sha256:f4b2667a265f2ff3afa4d4ee0330b8bf15f92acf494d3852eec5c0813a7d09a7`
                                 (was `sha256:960d12b4…3f3c`)
- **TfLens parser version:**     **1.2.0** (bumped from 1.1.0 — §3)
- **Reference file:**            `tests/.artifacts/parity/reference.json`
- **TfLens export:**             `src/TfLens/data/reports/2/2026-08-28/techieflow/tflens.json`
- **Compare output:**

```
parity-compare: reference=tests/.artifacts/parity/reference.json
parity-compare: tflens   =tests/.artifacts/parity/tflens.json

  (24 INFO lines: 4 × ENV-OK per_repo[*].repo; 16 × ADDED-OK per_repo[*].{framework,events,
   source_sha,source_kind}; 1 × KEYED per_repo; 2 × ADDED-OK extras / parity;
   1 × COVERED misses — all 29 figures BRD-129 names present on both documents and compared)

parity-compare: 0 finding(s), 24 allowed difference(s).
parity-compare: PASS -- the two implementations agree key for key.
```

- **Figures agreed, key for key:** misses 4 · miss fixes 4 · orphan fixes 0 · open 0 · wont-fix 1 ·
                                 resolved 3 · escapes_missing_why 1 · why_missed_n 1 ·
                                 why_missed_eligible 4 · predates_field 0 · design_miss_share 0% ·
                                 escape_share 50% · attributed_n 2 · attribution_excluded 2 ·
                                 by_origin_model `{claude-opus-5: 1, ?: 1}` · cost_sole_n 0 ·
                                 cost_shared_n 0 · cost_unattributable_n 4 ·
                                 tokens_per_miss_measured `null` · cost_usd_records 0. Plus the whole
                                 pre-existing surface: sessions 58 · tokens_total 15,319,316 ·
                                 commits 211 · session_duplicates_collapsed 12.
- **Extras spot-checked:**       no — `extras` still carries no oracle and is ADDED-OK on this run as on
                                 every other. The new `extras.misses_repricing` sits under it for exactly
                                 that reason: the reference computes no rate-card dollars.
- **Verdict:**                   PASS (empty diff). `src/TfLens/data/parity-last.json` re-written at
                                 parser 1.2.0; `/export` reads **QUOTABLE** again.

**Why this run exists — the invalidation clause doing its job.** The framework replaced
`.tfcore/telemetry/tf-metrics.sh` on 2026-08-28 with a version that reads the fifth stream:
`STREAMS` gained `"misses"`, and with it an `analyse_misses()` function, a top-level `misses` block of
29 figures, and two new `per_repo` keys (`misses`, `stale_types`). A changed reference invalidates the
last stamp by design (REQ-FN-063 clause 3), so the export went un-quotable and
`ParityStampTests.HashingAgreesWithTheDigestTheRecorderWrites` went red — correctly. **The constant was
not edited to make the test green.** The block was implemented, the procedure was re-run end to end, and
only then was the digest re-pinned, exactly as P-001 and P-002 did.

**What was implemented, not allow-listed.** Three keys the new reference emits that TfLens did not:
the whole `misses` object, `per_repo[].misses` and `per_repo[].stale_types`. A MISSING key always fails
by design, and all three were closed by emitting them. One key **was** added to
`tools/parity-compare.py`'s allow-list — `per_repo[].source_kind` (BRD-136) — which is a genuine
structural difference: the reference reads a working tree and has no concept of how data arrived.

**Two shapes, and why the export carries the pooled one.** `analyse_misses()` deliberately does not
segment the miss stream — its own comment says *"raw counts and the miss-class distribution ARE
poolable: a miss counts as a miss whoever missed it; only its attribution is confidence-bounded"* —
while REQ-FN-077 segments TfLens's own miss figures per `project_type` and offers no "all types" tab.
BRD-129 requires the export's block to diff against the reference's key for key, and a segmented block
cannot. The `misses` key therefore carries the reference's shape and the Markdown carries both, labelled
as two. It is produced by running **the engine's own** `MissFigures.Compute` a second time with the
segment key collapsed, not by aggregating segment results in the exporter: aggregation would be a second
implementation of every figure, and a mean cannot be pooled from rounded per-segment means anyway,
because a segment below `MinN` carries no value to pool. `Segment` still has no "all types" bucket and
`MissAnalysis.Live` still cannot express one.

**One figure the reference bounds twice.** `cost_usd_per_miss_measured` / `cost_usd_records` are
computed over `[f for f in sole if f.cost_usd is not None]` — the cost attribution bounds the dollars
exactly as it bounds the token columns. `MissHarnessCost` is a per-*harness* row and carries no such
bound, so reading `cost_usd_records` straight off it would count an apportioned repair as a measured
one. The bound is applied the same way the segment collapse is — by handing `MissFigures.Compute` the
record set the reference hands it — and is fixed by
`MissExportTests.MeasuredDollarsAreBoundedByTheCostAttribution`. Both figures are `0` / `null` on this
dataset, so the run would have passed either way; the fixture is what found it.

**The dataset was rebuilt.** The four repositories under `tests/.artifacts/parity/` were re-materialised
from the REQ-FN-027 raw archive under `src/TfLens/data/raw/2/**`, which had grown since P-002 (a new
TechieFlow SHA carrying the first `misses-*.jsonl`, plus new runs/gates/sessions/commits snapshots on
TechieFlow and TrBlazeUI). Every archived SHA for a stream is concatenated, because the store holds
their union; the reconstructed per-repo counts reproduce the database exactly — gates 214/34/0/43, runs
41/21/0/24, sessions 21/22/0/15, commits 101/44/0/66, misses 0/4/0/0 — and the reference's
`session_duplicates_collapsed` of 12 matches the sum ingest recorded in `"SyncState"`.

**One data defect found and reported, not silently absorbed.** `"UserRepo"."SourceKind"` in this
deployment carries the string `Synced` — the *badge* wording — because the column was created with that
DEFAULT before `database/001-schema.sql` was corrected to `api`. BRD-132 fixes the stored vocabulary at
`api` | `import`, so the export canonicalises on the way out (anything that is not `import` is a fetched
source, the same rule `SourceKinds.DisplayName` degrades by) rather than echoing a third spelling onto
the wire. The column itself belongs to the import cluster and is untouched here; the rows want a
one-line `UPDATE` and the column a corrected default.

## §7 Playbook schema discovery

<!-- APPEND ONE ENTRY PER events.ndjson EXAMINED, NEWEST LAST.
     ADR-010: the PbEvent columns are provisional and MUST NOT be fixed until a
     real file has been parsed and its field names recorded here. Record what the
     file actually contains — including fields SCHEMA.md does not document — before
     any column is added, renamed or typed. -->

**Template — copy this block, do not improvise the fields:**

```
### S-nnn — events.ndjson observed YYYY-MM-DD

- **Source repo → SHA:**
- **Line count / records parsed:**
- **Field names observed (verbatim, in file order):**
- **Types inferred, and from how many samples:**
- **Fields SCHEMA.md documents but the file does not carry:**
- **Fields the file carries that SCHEMA.md does not document:**
- **`phase_gate` values observed:**
- **Proposed column mapping (field → PascalCase column → type → nullable):**
- **Proposed dedupe key, and why it is the natural one:**
- **Left in `"Overflow"` for now, and why:**
```

**Standing constraint, whatever the file turns out to contain.** Playbook process-gates
(`phase_gate`) and TechieFlow assertion-gates (`gate`) are different axes and must never share a
table, a column or a chart. They are not two spellings of one concept.

### D-012 — An unrecorded token count stays unmeasured, even where the reference averages it as zero

**Date:** 2026-08-28 · **See:** BRD-122, BRD-123, BRD-130, REQ-FN-079, REQ-NFR-013, TF-005

`tokens_per_miss_measured` and `tokens_per_miss_apportioned` divide by the `miss-fix` records that
**carry** `tokens_out`, not by every record with that cost attribution.

**The reference does the opposite.** `analyse_misses` in `.tfcore/telemetry/tf-metrics.sh` computes
`sum(tokens_out or 0) / len(sole)`, so a repair whose tokens were never recorded is averaged in as a
repair that cost nothing. On the four-record example in TF-005 the reference reports `150.0` where
TfLens reports `200.0` over three records and names the fourth unmeasured.

**Why TfLens does not follow it.** Coercing an absent measurement to zero and then dividing by it is
the precise failure this product exists to detect — it is the same shape as the `$0.84` fabrication
removed on 2026-08-27, and the same shape as the pooled `cost_usd` the reference itself refuses to
compute for exactly this reason. BRD-31..36 make "absent renders as an absence, never as `0`"
structural rather than conventional, and `Figure` exists so that a refusal cannot be read as a
number. Adopting the reference's arithmetic here would mean shipping a figure the product's own
integrity rules forbid, in order to agree with a tool about a number both would then have wrong.

**Why this does not fail BRD §13 today.** The divergence is **latent**. Every dataset observed so far
carries `tokens_out` on every `sole` record, so both implementations produce the identical value and
the parity gate passes (P-003, exit 0, 0 findings). It becomes live only on a stream where a
`sole`-attributed fix omits the field. That is a real possibility — `tf-emit.sh` does not require it —
which is why it is raised upstream as **TF-005** with two acceptable resolutions offered: exclude
unrecorded records from the divisor and publish the denominator, **or** make `tokens_out` mandatory on
a `sole` record so the absent case cannot arise. Either removes the disagreement at the source.

**If the gate ever fails on these two keys**, that is this decision surfacing, not a regression. The
fix is upstream, not a change to `MissFigures`. The call site carries a comment saying so, and
`MissCostTests.AFixCarryingNoTokenCountIsNotCountedAsZero` pins the behaviour against a well-meant
"correction".

**Rejected alternative — adopt the reference's arithmetic to guarantee parity.** It would have made
the gate unconditionally green and cost one line. It was rejected because a green parity gate is
evidence that two implementations agree, and it is worth having only while both are trying to be
right; buying agreement by adopting a figure believed to be wrong turns the product's headline claim
into a formality.

---

### S-001 — no `events.ndjson` found; shape taken from the emitter source instead, 2026-08-26

**Read the first two bullets before quoting anything below.** This entry does **not** describe a
captured file. It describes the code that writes one. That is a weaker claim than the template asks
for, and the difference is recorded rather than papered over.

- **Source repo → SHA:** *No `events.ndjson` was found.* The columns below come from
  `techierathore/AI-First-Playbook@main`, two files:
  - `harness/opencode/plugin/telemetry.ts` — the emitter (an opt-in OpenCode plugin)
  - `scripts/playbook-telemetry.mjs` — the joiner that reads the file back
- **Line count / records parsed:** **zero.** No file has been parsed. Field names, wire spellings,
  nesting and types below are read off the emitter and are authoritative; **value ranges,
  cardinalities and real-world edge cases are unobserved.** The code carries this as
  `PlaybookSchemaStatus.EmitterSourceDerived`, between `Provisional` and `Discovered`, and every
  Playbook figure and every Playbook snapshot renders the caveat.

**Why no file exists, and where it was looked for.** `events.ndjson` is *runtime output*: the plugin
writes it into the directory of the project being built, and it is never committed to the Playbook
repository itself. Searched, all negative:

- Local machine — `find` for `events.ndjson` and `*.ndjson` across `/`, `$HOME`, `/mnt/c`,
  `/mnt/c/1MyCode` (unbounded depth) and `/mnt/c/Users`; `find` for any directory named
  `verification`. Zero hits.
- This repository's `docs/` and `.tfcore/` — greps for `playbook`, `ai-first`, `events.ndjson` and any
  GitHub URL. No upstream Playbook repository is named anywhere in the TfLens docs.
- Web — searches for `"ai-first-playbook" github`, `"verification/telemetry/events.ndjson"`; GitHub
  repository search via the unauthenticated API. The one same-named public repository
  (`lessch4os/ai-first-playbook`) is an unrelated methodology repo with no telemetry paths at all.
- `techierathore/AI-First-Playbook` — full recursive tree at `main`: no `events.ndjson`, no
  `verification/telemetry/` directory. `contents/verification/telemetry` → 404. Raw fetches of
  `verification/telemetry/events.ndjson`, `telemetry/events.ndjson` and `events.ndjson` on `main` →
  404. GitHub code search would settle it but requires authentication.

**To upgrade this entry to `Discovered`, someone must run a Playbook-managed project under OpenCode
with `PLAYBOOK_TELEMETRY=1` and commit — or hand over — the `verification/telemetry/events.ndjson`
it writes.** That is the one outstanding ask; nothing else about Phase 3 is blocked on anything else.

**2026-08-27 — the file in the raw archive is a build-harness fixture, NOT a captured run.**
`src/TfLens/data/raw/2/techierathore__AI-First-Playbook/events-0d7e6a3b….jsonl` exists and replays
cleanly into 45 `"PbEvent"` rows, so it is easy to mistake for the captured file this entry has been
waiting for. It is not one, and `PlaybookSchemaState.Status` therefore stays
`EmitterSourceDerived`. What gives it away, on inspection of the 45 records:

- Nine sessions `pb-ses-000` … `pb-ses-008`, each with the identical three turns and the identical
  token counts (`input` 40000 / 40900 / 41800, `output` 5200 / 5290 / 5380, `reasoning` 800,
  `cache.read` 22000, `cache.write` 3100) and the identical costs (0.42 / 0.47 / 0.52). Real turns
  do not repeat to the token across nine sessions.
- **No record carries `parentID` at all** — the field is absent from every line, so the fixture
  exercises no sub-agent chain. The `parentID` resolution required by REQ-FN-067 is therefore
  covered by unit tests (`PlaybookReportBuilderTests`: nested chain, orphan chain, minimum-n) and
  not by this archive.
- One process gate only (`/build-phase`), so the four gates BRD-75 names (plan review, verify, gap
  report, post-verification bugs) are unobserved here too.
- The archive is named `events-{sha}.jsonl`, whereas `PlaybookAdapter.ArchiveAsync` writes
  `events-{sha}.ndjson`: the file was placed by the demo seed, not fetched by the adapter.

The consequence for the pages and the export is only that the Playbook figures are thin, not that
they are wrong: 45 events, 9 sessions, 1 process gate, 1 946 430 tokens, $12.69 measured, and a
main-vs-sub-agent split of 9 / 0 — which is the correct answer for a stream that reports no parent.

- **Field names observed (verbatim, in emission order):** `kind`, `command`, `sessionID`,
  `arguments`, `parentID`, `messageID`, `model`, `tokens`, `cost`, `ts`.

  **The spelling is the headline.** The Playbook emits **camelCase with capitalised acronyms** —
  `sessionID`, `parentID`, `messageID` — not the snake_case of the four TechieFlow streams. Every
  day-1 guess (`event_type`, `phase_gate`, `session_id`, `parent_id`, `cost_usd`) matched **nothing**
  the Playbook emits. Had the adapter shipped against them it would have parsed every real file into
  rows that were entirely `NULL` except the timestamp, and reported that as a successful sync.

- **Types inferred, and from how many samples:** from the emitter source, not samples. `kind`,
  `command`, `arguments`, `sessionID`, `messageID`, `model`, `ts` are strings; `parentID` is a string
  **or JSON `null`**; `cost` is a number; `tokens` is an **object**, not a scalar.

- **Record kinds, and which fields each carries** — one file interleaves three shapes:

  | `kind` | Written on | Fields |
  |--------|-----------|--------|
  | `phase-start` | `command.execute.before` | `kind`, `command`, `sessionID`, `arguments`, `ts` |
  | `turn` | assistant `message.updated` | `kind`, `sessionID`, `parentID`, `messageID`, `model`, `tokens`, `cost`, `ts` |
  | `phase-end` | `session.idle` | `kind`, `sessionID`, `ts` |

  `ts` is stamped by the emitter onto every record. Only `turn` records carry tokens, model or cost.

- **Fields SCHEMA.md documents but the file does not carry:** effectively all of them. There is no
  `v`, no `app`, no `project_type`, no `backfilled`, no `harness`, no `attempt`, **and no verdict of
  any kind**. The stream is pure harness telemetry; every framework-sourced fact is joined in later
  from the checklist by `scripts/playbook-telemetry.mjs`.

- **Fields the file carries that SCHEMA.md does not document:** `messageID`, `arguments`, and the
  nested `tokens` object shape.

- **`phase_gate` values observed:** **none — there is no `phase_gate` field.** This is the second
  substantive finding. The phase appears exactly once per phase, as `command` on the `phase-start`
  record; the `turn` and `phase-end` records that follow belong to it **by sequence**. `PhaseGate` is
  therefore a **derived** column: the parser latches `command` on `phase-start` and stamps it onto
  every record until the next one, which is precisely how the Playbook's own joiner reads the same
  file. It is recorded as derived, in the parser, in the DDL comment and on the record type, so no
  one later mistakes it for an emitted field.

- **Proposed column mapping (field → column → type → nullable):**

  | Wire | Column | Type | Null | Note |
  |------|--------|------|------|------|
  | `ts` | `"Ts"` | text | no | on every record |
  | `kind` | `"Kind"` | text | yes | `phase-start` \| `turn` \| `phase-end` |
  | `command` | `"PhaseGate"` | text | yes | **derived** — latched from the enclosing `phase-start` |
  | `arguments` | `"Arguments"` | text | yes | `phase-start` only |
  | `sessionID` | `"SessionId"` | text | yes | |
  | `parentID` | `"ParentId"` | text | yes | `null` on a main session |
  | `messageID` | `"MessageId"` | text | yes | `turn` only — the dedupe key |
  | `model` | `"Model"` | text | yes | `providerID/modelID` |
  | `tokens.input` | `"TokensInput"` | integer | yes | |
  | `tokens.output` | `"TokensOutput"` | integer | yes | |
  | `tokens.reasoning` | `"TokensReasoning"` | integer | yes | joiner counts as **output** |
  | `tokens.cache.read` | `"TokensCacheRead"` | integer | yes | joiner counts as **input** |
  | `tokens.cache.write` | `"TokensCacheWrite"` | integer | yes | joiner counts as **input** |
  | `cost` | `"CostUsd"` | numeric | yes | OpenCode-measured; never summed across harnesses |

  The old single `"Tokens" integer` column could not hold this at all — the wire value is an object
  with five leaves. That is why the amendment adds columns rather than renaming them.

- **Proposed dedupe key, and why it is the natural one:** see **D-011** (§2). `turn` records key on
  `messageID` keeping the largest; markers key on `kind + ts + sessionID`.

- **Left in `"Overflow"` for now, and why:** nothing known is left there. `Overflow` remains wired so
  that any field a real file turns out to carry — the plugin is versioned independently of TfLens —
  is preserved verbatim for a rebuild rather than dropped.

**Consequences recorded here so they are not rediscovered later.**

1. **The three questions cannot be computed from `events.ndjson`.** The stream carries no verdict.
   The Playbook's verdict vocabulary — `PASS`, `PASS (code-audit)`, `FAIL`, `FAIL (code-audit)`,
   `DATA-GAP`, `BLOCKED` — is parsed out of the *project checklist* by the joiner, alongside
   `attempt` and `project_type`. So the per-`phase_gate` three questions need the **joiner output**,
   which BRD-73 asks for only "if committed" and which no repository has been observed to commit.
   Until then those figures render `—` with the reason attached, and are never invented.
2. **Phase totals, the main-vs-subagent split and tokens-by-model *are* computable** from the stream
   alone: `PhaseGate` (derived) × the five token columns, `parentID`, and `model` respectively.
3. **The main-vs-subagent rule needs the emitter's own fallback.** `parentID` is `null` both for a
   genuine main session *and* for a session whose parent the plugin could not learn. The joiner
   therefore treats a turn as a child when `parentID` is set **or** its `sessionID` differs from the
   phase's session. TfLens mirrors this: only a session named by a `phase-start` record counts as
   main, so a parentless stranger is classified as a sub-agent rather than as a second main session.

**Standing constraint, restated because this entry fixes columns.** Playbook process-gates
(`phase_gate`) and TechieFlow assertion-gates (`gate`) are different axes and never share a table, a
column or a chart. `"PbEvent"` has no `"Gate"` column, `"Gate"` has no `"PhaseGate"` column, no query
joins the two tables, and the result types key them by different C# types — `PhaseGateKey` on the
Playbook side, `string` on the TechieFlow side — so one cannot be assigned into the other's slot.
`PlaybookAxisSeparationTests` fails the build if any of that stops being true.

---

## §8 No-oracle extras — hand spot-checks (REQ-FN-064)

<!-- APPEND ONE ENTRY PER SPOT-CHECK, NEWEST LAST.
     The harness comparison, routing drift and counterfactual repricing have NO parity oracle:
     tf-metrics.sh does not compute them, so §6 cannot cover them. BRD §9 F-PARITY therefore
     requires each to be checked by hand against the raw JSONL at least once, and the check to be
     recorded here with the actual numbers — not with a claim that it was done. -->

### X-001 — Counterfactual repricing, checked by hand and CORRECTED, 2026-08-27

- **Metric:** `RoutingAnalysis.ActualMixUsd` / `AllAtMaxUsd` / `DeltaUsd` (BRD-58..BRD-60, ADR-009)
- **Dataset:** `tests/TfLens.Core.Tests/Fixtures/tflens-fixtures/parity-repo/docs/metrics/runs.jsonl` (10 records)
- **Method:** the four §2.5 token fields read straight out of the raw JSONL with `python3` and priced
  against `RateCard.Default()` in `decimal`, independently of the C# path.

Row census — 10 runs: 1 carries no `model` (`metrics-report`), 1 has `tokens_scope: none`
(`gpt-5-codex`, correctly counted in `RunsExcludedNoTokenScope`), 1 is `gpt-5-codex` with a real token
base but **no rate-card line** (correctly named in `MissingPriceModels`, never priced at zero). That
leaves **7 priceable runs**:

| # | Model | Exact USD |
|---|-------|-----------|
| 0 | `claude-sonnet-4-6` | 0.975 |
| 1 | `claude-sonnet-4-6` | 0.6375 |
| 2 | `claude-haiku-4-5` | 0.05125 |
| 3 | `anthropic/claude-sonnet-4-6` | 0.435 |
| 4 | `anthropic/claude-sonnet-4-6` | 0.192 |
| 8 | `claude-opus-4-6` | 2.775 |
| 9 | `claude-haiku-4-5` | 0.01795 |
| | **Exact total** | **5.0837 → $5.08** |

Counterfactual, pricing the pooled token base (841 in / 107,384 out / 2,626,036 cache-read /
150,930 cache-write) at each observed model: `claude-haiku-4-5` $1.37, `claude-sonnet-4-6` $4.11,
`anthropic/claude-sonnet-4-6` $4.11 (same line — the provider prefix resolves to the bare id),
**`claude-opus-4-6` $6.85 (max)**. Delta **$1.77**.

**This spot-check found two independent defects, which is why it exists.**

1. **Code defect — rounding accumulation.** `ModelRate.EstimateUsd` rounded each run to cents, and
   `Reprice` summed those rounded figures. That reported **$5.10** instead of $5.08. Worse, it made
   the two headline figures incomparable: the counterfactual prices *one pooled total* (one rounding)
   while the actual mix summed *seven separately-rounded* runs — so the delta, which is the entire
   point of the Routing page, was computed across two different bases. Fixed: `EstimateUsd` now
   returns full precision and `Reprice` rounds once, identically, for both figures.
2. **Test defect — an off-by-one in the original hand count.** The expectation asserted **$5.07** and a
   delta of **$1.78**. $5.07 is the total of the first *six* priceable runs: the original count silently
   dropped row 9, the last line of the file. A wrong expectation that happens to sit near the truth is
   the most dangerous kind, because it makes a wrong implementation look nearly right — the code said
   5.10, the test said 5.07, and neither was 5.08. Corrected in `RoutingRepricingTests`.

- **Verdict:** PASS after correction — `ActualMixUsd` $5.08, `AllAtMaxUsd` $6.85, `DeltaUsd` $1.77,
  `RunsExcludedNoTokenScope` 1, `MissingPriceModels` `["gpt-5-codex"]`, all matching the hand figures.
- **Standing note:** every figure above is **tokens × rate card, not measured spend**, and is rendered
  and exported with `RateCard.EstimateLabel` beside it (SCHEMA.md §4).

### X-002 — Harness comparison, routing drift and repricing on the REAL dataset, 2026-08-27

X-001 checked repricing against a 10-record test fixture. This is the check REQ-FN-064 actually asks
for: the three no-oracle extras, on the live dataset the BRD §13 run above was executed against.

- **Metrics:** `extras.harness`, `extras.routing`, `extras.repricing` (REQ-FN-058, BRD-51..BRD-62).
  `tf-metrics.sh` computes none of them, so they have no parity oracle and cannot appear in §6.
- **Repo / SHA (the raw files read):** `techierathore/TechieFlow` →
  `708fcffbdf1c61cf327fc3e0038291bce40091d6` and `techierathore/TrBlazeUI` →
  `49cf7a73f3f78219abccd1ecab49db797315a16c`, i.e. every
  `src/TfLens/data/raw/2/techierathore__{TechieFlow,TrBlazeUI}/{runs,gates,sessions}-*.jsonl`.
- **Compared against:** `data/reports/2/2026-08-27/techieflow/tflens.json`.
- **Method:** the raw JSONL read with `python3`, the BRD-26/27/28 dedupe keys applied by hand, and each
  figure recomputed from the record fields — never from the C# path. Base record counts after dedupe:
  **runs 42, gates 77, sessions 35**, which match the `Run`/`Gate`/`Session` tables exactly.

**Harness comparison — every figure matched.** Tokens are summed over **run** records (not sessions),
which is what `ExtraMetrics.BuildColumn` defines them as:

| Harness | runs | gate records | sessions | tokens in / out / cache-read / cache-write | tokens per Verified REQ |
|---|---|---|---|---|---|
| `claude-code` | 26 | 68 | 32 | 3,074,400 / 361,920 / 1,440,000 / 192,000 | 3,436,320 ÷ 46 = **74,702.6** |
| `opencode` | 12 | 0 | 2 | 1,521,000 / 178,800 / 720,000 / 96,000 | *insufficient data (n=0)* — 0 Verified gates |
| `codex` | 4 | 8 | 1 | 0 / 0 / 0 / 0 | *—* — no token base |

Verdict mixes matched too (`claude-code` Verified 46 / Failed 22; `codex` Verified 4 / FAIL 2 / Needs
re-verify 2). `not_detected_records` = **1** is right: exactly one gate record carries no `harness`,
and it is excluded from all three columns rather than folded into one.

**Routing drift — every figure matched.** `runs_with_routing_fields` **36**, `unrouted_runs` **8**
(runs whose `routed` is literally `false`), `distinct_models` **1**, drift table **36 rows**.
`tokens_by_model` for `claude-opus-4`: in 4,595,400 / out 540,720 / cache-read 2,160,000 /
cache-write 288,000, **total 7,584,120** — reproduced to the token.

**Repricing — matched, including the two "suspicious" values, which are both correct.**
`missing_price_models` = `["claude-opus-4"]` is right: `data/prices.json` carries `claude-opus-4-8`,
`claude-opus-5` and `claude-fable-5`, but no plain `claude-opus-4`, so nothing was priceable and the
three USD estimates are `null` rather than 0. `runs_excluded_no_token_scope` = **0** is also right
even though 6 of the 42 runs have no token base: `Reprice` counts exclusions among runs that carry an
observed `model`, and all 6 of those runs carry `model: null`, so they are out of scope by definition
rather than silently dropped.

**One finding — `extras.harness.opencode_cost_usd` is structurally always `null`.**
`ExtraMetrics.MeasuredOpenCodeCost` sums `cost_usd` over **run** records. In this dataset **no run
record carries `cost_usd` at all**; the measured OpenCode dollars are on the **session** records —
`0.017749` and `0.019918`, i.e. a real measured **$0.04** that the page reports as "not measured".
SCHEMA.md §4 is explicit that this is the documented source: the OpenCode plugin *"emits into this
stream"* — sessions.jsonl — *"with **real `cost_usd`**"*. BRD-53 asks for real `cost_usd` for
`opencode`, and the one measured-dollars figure in the product currently cannot ever show it.
Not changed here: `extras` is REQ-FN-058's surface, and this record is the sanctioned way the gap is
raised. Recorded for the owner.

- **Verdict:** PASS on harness comparison, routing drift and repricing — every figure reproduced by
  hand. One defect found and recorded (`opencode_cost_usd` reads the wrong stream).
- **Playbook axis:** not covered here. `events.ndjson` is a TfLens-only stream (`per_repo[].events`,
  REQ-FN-065); its figures are an `extras` axis with no reference and are checked in §7.
