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

**Current:** `1.0.0` — the first shipping parser.

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

**Reason — verified against the live AppManager API, not inferred from the guide.** The
`X-Api-Key` / `X-Api-Secret` headers are optional on the AppManager side, because the client also
sends `applicationId: 1` in every request body and AppManager resolves the application from that. So
*no* pair works. What does **not** work is half a pair: AppManager rejects a key without its matching
secret with `401 INVALID_API_KEY` on every call, which would turn every sign-in, registration and
password reset into an authentication failure that looks like a user error. That is precisely the
silent misconfiguration BRD-9 exists to prevent, so it is refused at startup instead.

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

*No parity run has been recorded yet. The export is **not quotable** until one is.*

---

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
