-- TfLens schema — PostgreSQL 16 (ADR-015).
--
-- Idempotent by construction: every statement is CREATE ... IF NOT EXISTS, so the application
-- applies this file at every startup and there is no migration framework. That is safe because the
-- store is disposable — the raw JSONL archive under data/raw/ is the source of truth, and `rebuild`
-- drops these tables and replays it.
--
-- Every identifier is double-quoted. PostgreSQL folds unquoted identifiers to lower case, which
-- would silently destroy the PascalCase names the Coding Standards fix; quoting keeps "ReqId" as
-- "ReqId" in DDL and in every Dapper query.
--
-- Column names are the SCHEMA.md field names in PascalCase (req_id -> "ReqId", cost_usd -> "CostUsd").
-- Absent optional fields are stored NULL, never 0 (SCHEMA.md §2.5). "Overflow" holds, verbatim, any
-- property the parser did not recognise, so a rebuild loses nothing when the schema grows.
--
-- Every stream table carries "UserId": isolation is a column on the data, a parameter on every read,
-- and part of every unique index — not a filter someone remembers to add (ADR-013).

-- ---------------------------------------------------------------- identity

CREATE TABLE IF NOT EXISTS "UserRepo" (
    "UserId"      integer     NOT NULL,
    "Repo"        text        NOT NULL,
    "Owner"       text        NOT NULL,
    "Name"        text        NOT NULL,
    "Branch"      text        NOT NULL,
    "Kind"        text        NOT NULL,
    "Framework"   text        NOT NULL,
    "IsPublic"    boolean     NOT NULL DEFAULT true,
    "ConnectedTs" text        NOT NULL,
    CONSTRAINT "PkUserRepo" PRIMARY KEY ("UserId", "Repo")
);

-- Server-side AppManager tokens. The browser only ever holds "SessionId" (inside the auth cookie);
-- the token columns are encrypted at rest with ASP.NET Data Protection before they are written here.
CREATE TABLE IF NOT EXISTS "AuthSession" (
    "SessionId"       text NOT NULL,
    "UserId"          integer NOT NULL,
    "Email"           text NOT NULL,
    "DisplayName"     text NOT NULL,
    "AccessToken"     text NOT NULL,
    "RefreshToken"    text NOT NULL,
    "TokenExpiresAt"  text NOT NULL,
    "CreatedTs"       text NOT NULL,
    "LastValidatedTs" text NULL,
    CONSTRAINT "PkAuthSession" PRIMARY KEY ("SessionId")
);

CREATE INDEX IF NOT EXISTS "IxAuthSessionUserId" ON "AuthSession" ("UserId");

CREATE TABLE IF NOT EXISTS "SyncState" (
    "UserId"        integer NOT NULL,
    "Repo"          text    NOT NULL,
    "Kind"          text    NULL,
    "Branch"        text    NULL,
    "LastSha"       text    NULL,
    "LastSyncTs"    text    NULL,
    "LastError"     text    NULL,
    "RunsCount"     integer NOT NULL DEFAULT 0,
    "GatesCount"    integer NOT NULL DEFAULT 0,
    "SessionsCount" integer NOT NULL DEFAULT 0,
    "CommitsCount"  integer NOT NULL DEFAULT 0,
    "EventsCount"   integer NOT NULL DEFAULT 0,
    CONSTRAINT "PkSyncState" PRIMARY KEY ("UserId", "Repo")
);

-- REQ-FN-063: how many session records ingest threw away because another record already held that
-- session id. Unlike the five *Count columns this cannot be recovered with COUNT(*) — the collapsed
-- rows were never written — so it is a property of ingest and has to be persisted as one. `rebuild`
-- replays the whole raw archive and therefore SETS it; an incremental sync ADDS its own pass to it.
-- Added after the table shipped, so it is a guarded ALTER rather than a column on the CREATE above:
-- the schema file is applied at every startup and must stay idempotent on an existing database.
ALTER TABLE "SyncState"
    ADD COLUMN IF NOT EXISTS "SessionDuplicatesCollapsed" integer NOT NULL DEFAULT 0;

-- ---------------------------------------------------------------- streams

CREATE TABLE IF NOT EXISTS "Run" (
    "UserId"              integer NOT NULL,
    "Repo"                text    NOT NULL,
    "SourceSha"           text    NOT NULL,
    "V"                   integer NOT NULL DEFAULT 1,
    "Ts"                  text    NOT NULL,
    "App"                 text    NULL,
    "ProjectType"         text    NULL,
    "ProjectTypeInferred" boolean NULL,
    "Backfilled"          boolean NULL,
    "Harness"             text    NULL,
    "Cmd"                 text    NULL,
    "Mode"                text    NULL,
    "Started"             text    NULL,
    "Ended"               text    NULL,
    "DurationS"           integer NULL,
    "ReqsTouched"         text    NULL,
    "ReqsCount"           integer NULL,
    "Subagents"           text    NULL,
    "FilesWritten"        integer NULL,
    "BuildResult"         text    NULL,
    "Tier"                text    NULL,
    "TierModel"           text    NULL,
    "Model"               text    NULL,
    "Models"              text    NULL,
    "Routed"              boolean NULL,
    "TokensIn"            integer NULL,
    "TokensOut"           integer NULL,
    "TokensCacheRead"     integer NULL,
    "TokensCacheWrite"    integer NULL,
    "CostUsd"             numeric NULL,
    "TokensScope"         text    NULL,
    "Attempt"             integer NULL,
    "Overflow"            jsonb   NULL
);

-- The dedupe key from BRD-28: a run is identified by when it happened, for what app, and which
-- command. Re-parsing the same raw file is therefore a no-op.
CREATE UNIQUE INDEX IF NOT EXISTS "UcRunIdentity"
    ON "Run" ("UserId", "Repo", "Ts", COALESCE("App", ''), COALESCE("Cmd", ''));

CREATE INDEX IF NOT EXISTS "IxRunUserFramework" ON "Run" ("UserId", "Repo");

CREATE TABLE IF NOT EXISTS "Gate" (
    "UserId"              integer NOT NULL,
    "Repo"                text    NOT NULL,
    "SourceSha"           text    NOT NULL,
    "V"                   integer NOT NULL DEFAULT 1,
    "Ts"                  text    NOT NULL,
    "App"                 text    NULL,
    "ProjectType"         text    NULL,
    "ProjectTypeInferred" boolean NULL,
    "Backfilled"          boolean NULL,
    "Inferred"            text    NULL,
    "Harness"             text    NULL,
    "RunId"               text    NULL,
    "ReqId"               text    NULL,
    "ReqClass"            text    NULL,
    "Attempt"             integer NULL,
    "Verdict"             text    NULL,
    "Gate"                text    NULL,
    "GatesRun"            text    NULL,
    "FailureClass"        text    NULL,
    "PriorVerdict"        text    NULL,
    "Proof"               text    NULL,
    "Overflow"            jsonb   NULL
);

-- BRD-28: gates are identified by timestamp, app, requirement and run.
CREATE UNIQUE INDEX IF NOT EXISTS "UcGateIdentity"
    ON "Gate" ("UserId", "Repo", "Ts", COALESCE("App", ''), COALESCE("ReqId", ''), COALESCE("RunId", ''));

CREATE INDEX IF NOT EXISTS "IxGateUserRepo" ON "Gate" ("UserId", "Repo");
CREATE INDEX IF NOT EXISTS "IxGateReqId" ON "Gate" ("UserId", "ReqId");

CREATE TABLE IF NOT EXISTS "Session" (
    "UserId"              integer NOT NULL,
    "Repo"                text    NOT NULL,
    "SourceSha"           text    NOT NULL,
    "V"                   integer NOT NULL DEFAULT 1,
    "Ts"                  text    NOT NULL,
    "App"                 text    NULL,
    "ProjectType"         text    NULL,
    "Harness"             text    NULL,
    "SessionId"           text    NOT NULL,
    "Model"               text    NULL,
    "DurationS"           integer NULL,
    "InputTokens"         integer NULL,
    "OutputTokens"        integer NULL,
    "CacheReadTokens"     integer NULL,
    "CacheCreationTokens" integer NULL,
    "CostUsd"             numeric NULL,
    "Overflow"            jsonb   NULL
);

-- A session record is rewritten as the session grows, so the natural key is the session id and the
-- parser keeps the largest copy (BRD-27). The unique index makes the store agree.
CREATE UNIQUE INDEX IF NOT EXISTS "UcSessionUserRepoId"
    ON "Session" ("UserId", "Repo", "SessionId");

CREATE INDEX IF NOT EXISTS "IxSessionUserRepo" ON "Session" ("UserId", "Repo");

CREATE TABLE IF NOT EXISTS "Commit" (
    "UserId"        integer NOT NULL,
    "Repo"          text    NOT NULL,
    "SourceSha"     text    NOT NULL,
    "V"             integer NOT NULL DEFAULT 1,
    "Ts"            text    NOT NULL,
    "App"           text    NULL,
    "ProjectType"   text    NULL,
    "Sha"           text    NOT NULL,
    "Files"         integer NULL,
    "Insertions"    integer NULL,
    "Deletions"     integer NULL,
    "SubjectPrefix" text    NULL,
    "Branch"        text    NULL,
    "Overflow"      jsonb   NULL
);

-- Per repo, not across them: two repositories may legitimately share a short SHA (BRD-26).
CREATE UNIQUE INDEX IF NOT EXISTS "UcCommitUserRepoSha"
    ON "Commit" ("UserId", "Repo", "Sha");

CREATE INDEX IF NOT EXISTS "IxCommitUserRepo" ON "Commit" ("UserId", "Repo");

-- ---------------------------------------------------------------- playbook (Phase 3)

-- Columns amended 2026-08-26 (REQ-FN-068, ADR-010) from the Playbook's own emitter,
-- harness/opencode/plugin/telemetry.ts in techierathore/AI-First-Playbook, and its joiner,
-- scripts/playbook-telemetry.mjs. The field names, wire spellings and types are recorded in
-- DECISIONS.md §Playbook; no captured events.ndjson has been parsed, so value ranges are unverified.
-- The wire spelling is camelCase with capitalised acronyms (sessionID / parentID / messageID), and
-- "tokens" arrives as a nested object, which is why it lands in five columns rather than one.
-- Playbook process-gates ("PhaseGate") and TechieFlow assertion-gates live in different tables and
-- never share a column or a chart (SCHEMA.md §11) — there is deliberately no "Gate" column here and no
-- "PhaseGate" column on "Gate".
CREATE TABLE IF NOT EXISTS "PbEvent" (
    "UserId"           integer NOT NULL,
    "Repo"             text    NOT NULL,
    "SourceSha"        text    NOT NULL,
    "Ts"               text    NOT NULL,  -- wire: ts
    "Kind"             text    NULL,      -- wire: kind — phase-start | turn | phase-end
    "PhaseGate"        text    NULL,      -- derived: "command" of the enclosing phase-start record
    "Arguments"        text    NULL,      -- wire: arguments (phase-start only)
    "SessionId"        text    NULL,      -- wire: sessionID
    "ParentId"         text    NULL,      -- wire: parentID — null on a main session
    "MessageId"        text    NULL,      -- wire: messageID (turn only) — the dedupe key
    "Model"            text    NULL,      -- wire: model — "providerID/modelID"
    "TokensInput"      integer NULL,      -- wire: tokens.input
    "TokensOutput"     integer NULL,      -- wire: tokens.output
    "TokensReasoning"  integer NULL,      -- wire: tokens.reasoning
    "TokensCacheRead"  integer NULL,      -- wire: tokens.cache.read
    "TokensCacheWrite" integer NULL,      -- wire: tokens.cache.write
    "CostUsd"          numeric NULL,      -- wire: cost
    "Overflow"         jsonb   NULL
);

-- A turn's identity is its messageID, NOT its timestamp: the emitter appends a fresh turn record on
-- every message.updated event, so one assistant message produces many rows as it streams and only the
-- last carries its final token and cost counts. The parser collapses them keeping the highest
-- TokensOutput (tie -> latest Ts); this index makes a re-fetch of a message that was still streaming
-- collapse onto the row already stored rather than double-counting its tokens. The upsert for turn rows
-- is therefore ON CONFLICT DO UPDATE keeping the greater TokensOutput, not DO NOTHING.
CREATE UNIQUE INDEX IF NOT EXISTS "UcPbEventTurn"
    ON "PbEvent" ("UserId", "Repo", "MessageId")
    WHERE "MessageId" IS NOT NULL;

-- phase-start and phase-end records carry no messageID; they are identified by kind, timestamp and session.
CREATE UNIQUE INDEX IF NOT EXISTS "UcPbEventMarker"
    ON "PbEvent" ("UserId", "Repo", COALESCE("Kind", ''), "Ts", COALESCE("SessionId", ''))
    WHERE "MessageId" IS NULL;

CREATE INDEX IF NOT EXISTS "IxPbEventUserRepo" ON "PbEvent" ("UserId", "Repo");

CREATE INDEX IF NOT EXISTS "IxPbEventPhaseGate" ON "PbEvent" ("UserId", "Repo", "PhaseGate");
