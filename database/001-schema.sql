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

-- Imported telemetry (F-IMPORT, added 2026-08-28). These three columns are SCHEMA ONLY here: the
-- behaviour behind them belongs to REQ-FN-084 (bundle sha256 as dataset identity) and REQ-FN-085
-- (idempotent re-import; poller and Sync skip imported sources). Added as guarded ALTERs because
-- "UserRepo" already shipped and this file is applied at every startup.
--
-- Invariant REQ-FN-084 encodes: a "UserRepo" carries "LastSha" OR "BundleSha", never both — a fetched
-- source's dataset identity is the commit SHA, an imported one's is the uploaded bundle's sha256. It is
-- deliberately NOT a CHECK constraint here: the import cluster enforces it in code, and a constraint on
-- a table applied at every startup would fail the whole schema script on one legacy row.

-- REQ-FN-087: the only structural trace of how a source's data arrived. 'api' | 'import' (BRD-132).
-- Stored value, not badge wording: /repos and Coverage render these as 'Synced' / 'Imported'
-- via SourceKinds.DisplayName, so rewording the badge is never a schema change.
-- No stream table carries it and no figure segments on it (ADR-021).
ALTER TABLE "UserRepo"
    ADD COLUMN IF NOT EXISTS "SourceKind" text NOT NULL DEFAULT 'api';

-- The column was briefly created with the BADGE WORDING as its default ('Synced'/'Imported') during
-- the 2026-08-28 build, before BRD-132's stored vocabulary was restored. Changing the default does
-- not rewrite rows that already exist, so any database created in that window still holds the label.
-- Behaviour was unaffected (SourceKinds.IsImport falls through to 'api' for anything unrecognised),
-- but the export's `source_kind` key would have carried the wrong word to a downstream consumer.
-- Idempotent and runs at every startup, so any environment self-heals on its next boot.
UPDATE "UserRepo" SET "SourceKind" = 'api'    WHERE "SourceKind" = 'Synced';
UPDATE "UserRepo" SET "SourceKind" = 'import' WHERE "SourceKind" = 'Imported';

-- REQ-FN-084: sha256 of the uploaded bundle, standing exactly where a fetched source's commit SHA
-- stands — raw-archive filename, Coverage row, export per_repo block and the dataset a parity run pins.
-- NULL for every fetched source.
ALTER TABLE "UserRepo"
    ADD COLUMN IF NOT EXISTS "BundleSha" text NULL;

-- REQ-FN-085: when the source was last imported. A poller tick and a header Sync leave it untouched,
-- because they make no outbound request for an imported source. NULL for every fetched source.
ALTER TABLE "UserRepo"
    ADD COLUMN IF NOT EXISTS "LastImportTs" timestamptz NULL;

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

-- REQ-FN-071 / BRD-112: the fifth TechieFlow stream. One count for the whole `misses` stream, summing
-- the three tables its three record kinds land in — the stream is one file, and Coverage's per-repo
-- stream table therefore goes from four rows to five rather than to seven. A repository that does not
-- emit misses.jsonl keeps this at zero, which is the same fact a 404 states.
ALTER TABLE "SyncState"
    ADD COLUMN IF NOT EXISTS "MissesCount" integer NOT NULL DEFAULT 0;

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

-- ------------------------------------------------- misses (F-MISS, added 2026-08-28)

-- docs/metrics/misses.jsonl is the ONE stream whose records do not all share a shape: `miss` opens,
-- `miss-fix` closes and `miss-amend` completes a field the miss left null (SCHEMA.md §5.5). It gets
-- three tables rather than one wide nullable table, because the three shapes share only the §1 common
-- set and a single table would make every column of two kinds nullable and every query a discriminator
-- check (ADR-018). House style is unchanged: every identifier double-quoted, "UserId" a real column and
-- part of every unique index (ADR-013), CREATE TABLE IF NOT EXISTS so this file stays idempotent.

CREATE TABLE IF NOT EXISTS "Miss" (
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
    "MissId"              text    NOT NULL,  -- wire: miss_id — the link key for MissFix and MissAmend
    "ReqId"               text    NULL,      -- NULL is meaningful: no REQ existed to miss
    "ReqClass"            text    NULL,
    "MissClass"           text    NULL,
    "Artifact"            text    NULL,
    "Severity"            text    NULL,
    -- wire: why_missed. NULL means NOT ASSESSED, never "nothing to say" and never a bucket: the
    -- failed-practice distribution's denominator is the rows that carry it (SCHEMA.md §5.5.6).
    "WhyMissed"           text    NULL,
    "OriginPhase"         text    NULL,
    "OriginAgent"         text    NULL,
    "OriginRunId"         text    NULL,
    -- Derived by tf-emit.sh, never written by an agent. A provenance boundary: only 'linked' rows reach
    -- a per-phase, per-model or per-agent figure (SCHEMA.md §6, REQ-FN-078).
    "OriginConfidence"    text    NULL,
    "OriginModel"         text    NULL,
    "OriginHarness"       text    NULL,
    "FoundBy"             text    NULL,
    "FoundPhase"          text    NULL,
    "FoundGate"           text    NULL,
    "FoundRunId"          text    NULL,
    "FailureClass"        text    NULL,
    "Overflow"            jsonb   NULL
);

-- BRD-114: a miss is opened once, so its id is its identity within a repository. The parser keeps the
-- earliest ts; this index makes the store agree, and a re-parse of the same archived file is a no-op.
CREATE UNIQUE INDEX IF NOT EXISTS "UcMissUserRepoMissId"
    ON "Miss" ("UserId", "Repo", "MissId");

CREATE INDEX IF NOT EXISTS "IxMissUserRepo" ON "Miss" ("UserId", "Repo");
CREATE INDEX IF NOT EXISTS "IxMissOriginModel" ON "Miss" ("UserId", "OriginModel");
CREATE INDEX IF NOT EXISTS "IxMissMissId" ON "Miss" ("UserId", "MissId");

CREATE TABLE IF NOT EXISTS "MissFix" (
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
    "MissId"              text    NOT NULL,  -- a fix matching no miss is an ORPHAN: counted, never dropped
    "ReqId"               text    NULL,
    -- wire: fix_run_id. NULL is a deliberate emission, not a gap — `log-miss --fixed` omits it when the
    -- repairing run cannot be identified, which is exactly what makes the record cost 'none' (§5.5.3).
    "FixRunId"            text    NULL,
    "FixCmd"              text    NULL,
    "FixAttempt"          integer NULL,
    "VerdictAfter"        text    NULL,
    "Reopened"            boolean NULL,
    -- 'sole' | 'shared:<n>' | 'none'. A headline cost figure is computed over 'sole' alone; an
    -- apportioned window is arithmetic, not measurement, and never sums into it (ADR-019).
    "CostAttribution"     text    NULL,
    "TokensIn"            integer NULL,
    "TokensOut"           integer NULL,
    "TokensCacheRead"     integer NULL,
    "TokensCacheWrite"    integer NULL,
    "CostUsd"             numeric NULL,      -- only ever non-null for opencode; never summed across harness
    "TokensScope"         text    NULL,
    "Model"               text    NULL,
    "Overflow"            jsonb   NULL
);

-- BRD-114: one repair run produces one fix record per miss it repaired. COALESCE mirrors the parser's
-- key exactly, so the deliberate no-fix_run_id record (§5.5.3) keys on the empty string in both places.
CREATE UNIQUE INDEX IF NOT EXISTS "UcMissFixUserRepoMissIdFixRunId"
    ON "MissFix" ("UserId", "Repo", "MissId", COALESCE("FixRunId", ''));

CREATE INDEX IF NOT EXISTS "IxMissFixUserRepo" ON "MissFix" ("UserId", "Repo");
CREATE INDEX IF NOT EXISTS "IxMissFixMissId" ON "MissFix" ("UserId", "MissId");

-- Amendments are STORED, not collapsed. Folding into the parent is a read-time operation over these
-- rows, so `rebuild` replays and re-derives them exactly like every other figure, and the invariant
-- ("an amend may fill a null, never overwrite one") is re-checked by TfLens rather than trusted to the
-- producer — a stream merged from several machines can carry an amend and a later-written value in
-- either order (ADR-020, REQ-FN-075).
CREATE TABLE IF NOT EXISTS "MissAmend" (
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
    "MissId"              text    NOT NULL,  -- naming no known miss makes it an orphan, never applied
    "Field"               text    NOT NULL,  -- must be on the allowlist or it is an orphan
    "Value"               text    NULL,      -- must be inside that field's closed vocabulary
    "Overflow"            jsonb   NULL
);

-- BRD-114: ts is part of the key rather than a tie-break — two amendments of one field at different
-- instants are two distinct facts, and only a byte-identical re-parse collapses.
CREATE UNIQUE INDEX IF NOT EXISTS "UcMissAmendUserRepoMissIdFieldTs"
    ON "MissAmend" ("UserId", "Repo", "MissId", "Field", "Ts");

CREATE INDEX IF NOT EXISTS "IxMissAmendUserRepo" ON "MissAmend" ("UserId", "Repo");
CREATE INDEX IF NOT EXISTS "IxMissAmendMissId" ON "MissAmend" ("UserId", "MissId");

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

-- ------------------------------------------- phase effort (F-EFFORT, added 2026-09-01)

-- "Run" gains the three SCHEMA §2.6 fields the producer started emitting on 2026-08-31 (REQ-FN-088,
-- BRD-145). ALL NULLABLE BY DESIGN: null means "NOT CAPTURED", and a measured zero is a different
-- fact. A main-scope window never read the subagent transcripts, so its absent "SubagentRuns" is not
-- a report of zero subagents — coalescing it would turn "we did not look" into a measurement, which
-- is precisely the defect ADR-026 exists to prevent.
ALTER TABLE "Run" ADD COLUMN IF NOT EXISTS "SubagentRuns"       integer NULL;
ALTER TABLE "Run" ADD COLUMN IF NOT EXISTS "TokensOutSubagents" bigint  NULL;

-- {model_id: output_tokens} over the window — the per-model SPLIT, not just the winner. jsonb rather
-- than a child table because it is only ever read WHOLE: never joined, never filtered on one model,
-- and the run's token window is already atomic. "PbPhaseModelUsage" below is a child table for the
-- opposite reason — the Playbook contract must filter and aggregate on any models[] member (BRD-158),
-- and a JSON blob cannot serve a WHERE model = … (ADR-025).
ALTER TABLE "Run" ADD COLUMN IF NOT EXISTS "ModelTokensOut"     jsonb   NULL;

-- Exists only for /effort: every other "Run" read is by (UserId, Repo), and grouping by "Cmd" across
-- repositories is a new access pattern.
CREATE INDEX IF NOT EXISTS "IxRunUserCmd" ON "Run" ("UserId", "Cmd");

-- The Playbook's misses land in the EXISTING miss tables (ADR-024) rather than a parallel PbMiss* set,
-- because a Playbook miss and a TechieFlow miss are the SAME measurement. What genuinely differs is
-- carried as difference, in its OWN column (REQ-FN-104, BRD-165):
--   "ItemId"         — the Playbook's requirement axis. One axis under two names, beside "ReqId".
--   "FoundPhaseGate" — the Playbook PROCESS gate. "FoundGate" is a TechieFlow ASSERTION gate; these are
--                      two genuinely different measurements and must NEVER share a column or a chart.
ALTER TABLE "Miss" ADD COLUMN IF NOT EXISTS "ItemId"         text NULL;
ALTER TABLE "Miss" ADD COLUMN IF NOT EXISTS "FoundPhaseGate" text NULL;

-- The Playbook's natural key: an immutable hash of the exported source line, preserving stream order
-- (REQ-FN-103, BRD-164, ADR-024). NULL on every TechieFlow row, which is what the partial index below
-- depends on. The normalizer that COMPUTES the hash is REQ-FN-103's other half and lives with the
-- ingest cluster; these columns and that index are the schema half.
ALTER TABLE "Miss"      ADD COLUMN IF NOT EXISTS "SourceLineHash" text NULL;
ALTER TABLE "MissFix"   ADD COLUMN IF NOT EXISTS "SourceLineHash" text NULL;
ALTER TABLE "MissAmend" ADD COLUMN IF NOT EXISTS "SourceLineHash" text NULL;

-- PARTIAL, and the WHERE clause is the whole point. TechieFlow rows carry no "SourceLineHash", and in
-- PostgreSQL a NULL never collides with another NULL in a unique index — but stating the rule without
-- the predicate would still index every TechieFlow row for nothing and would break the moment anyone
-- COALESCEd it. Restricting the key to the rows that actually have one leaves
-- "UcMissUserRepoMissId" to govern the TechieFlow edition: two editions, two natural keys, one table.
CREATE UNIQUE INDEX IF NOT EXISTS "UcMissUserRepoSourceLine"
    ON "Miss" ("UserId", "Repo", "SourceLineHash")
    WHERE "SourceLineHash" IS NOT NULL;

-- Schema-2 phase data occupies THREE tables, not one wide row (ADR-025, REQ-FN-095, BRD-154). The
-- contract requires filtering and aggregating on any models[] member (BRD-158) and rendering a
-- recursive subagent tree by session_id / parent_id (BRD-159); neither is expressible over a JSON
-- column, and a mixed-model execution flattened onto its dominant model is the exact misattribution
-- BRD-150 forbids.
--
-- Three things here are deliberate:
--   1. Token and turn counters are 64-bit. A phase tree's cumulative output is not an int32 quantity.
--   2. "CostUsd" is numeric(20,10) — FIXED PRECISION, never `real` or `double precision`. The contract
--      states it outright: provider cost is money, and money is not a binary float.
--   3. Every column the producer may leave null IS nullable. "Not captured" and "zero" stay different
--      facts at the column level, exactly as they do on the stream tables (SCHEMA.md §2.5).
--
-- Timing is three types, not three names for one number (ADR-027). "AssistantElapsedMs" and
-- "ToolElapsedMs" are DIAGNOSTICS and must never be added together: an assistant envelope can CONTAIN
-- tool execution, which is why the producer publishes a single unioned "ObservedActiveMs". There is
-- deliberately no human-effort column — neither framework captures it, and a column that exists is a
-- column something eventually populates by inference from wall-clock time.
CREATE TABLE IF NOT EXISTS "PbPhaseExecution" (
    "UserId"                integer       NOT NULL,
    "Repo"                  text          NOT NULL,
    "PhaseExecutionId"      text          NOT NULL,  -- the producer's stable id for one phase execution
    "SourceSchema"          integer       NULL,      -- the phase-metric schema version the row came from
    "SourceHarness"         text          NULL,
    "Phase"                 text          NULL,
    "SessionId"             text          NULL,
    "Granularity"           text          NULL,
    "StartedAt"             text          NULL,
    "EndedAt"               text          NULL,      -- null on an incomplete window; never back-filled
    "ElapsedMs"             bigint        NULL,      -- WALL CLOCK. Not active time, not human effort.
    "Complete"              boolean       NULL,
    "EndReason"             text          NULL,      -- complete:false implies end_reason 'eof'
    "DominantModel"         text          NULL,      -- a LABEL; per-model effort reads PbPhaseModelUsage
    "Tier"                  text          NULL,
    "TokensInput"           bigint        NULL,
    "TokensOutput"          bigint        NULL,
    "TokensReasoning"       bigint        NULL,
    "TokensCacheRead"       bigint        NULL,
    "TokensCacheWrite"      bigint        NULL,
    "TokensIn"              bigint        NULL,      -- the producer's compatibility total
    "TokensOut"             bigint        NULL,      -- the producer's compatibility total
    "CostUsd"               numeric(20,10) NULL,     -- fixed precision; NEVER real/double precision
    "Turns"                 integer       NULL,
    "AssistantElapsedMs"    bigint        NULL,      -- diagnostic only — never summed with the next
    "ToolElapsedMs"         bigint        NULL,      -- diagnostic only — never summed with the previous
    "ObservedActiveMs"      bigint        NULL,      -- the producer's UNION, overlaps counted once
    "ActiveCoverage"        text          NULL,
    -- A row that fails an invariant, or carries data_quality.valid false, is QUARANTINED: stored,
    -- displayed with its reason, excluded from every numeric aggregate. This matters more than it
    -- sounds, because the producer may retain zero-valued compatibility totals on an invalid row, so a
    -- consumer that trusts the numbers gets a confident zero rather than an error.
    "DataQualityValid"      boolean       NULL,
    "DataQualityIssues"     text          NULL,
    "TokenStatus"           text          NULL,
    "CostStatus"            text          NULL,      -- headline cost needs 'complete'; nothing weaker
    "TokensScope"           text          NULL,      -- governs whether a fan-out claim may be made
    "SubagentsSpawned"      integer       NULL,
    "SubagentsContributors" integer       NULL,      -- spawned >= contributors is a producer invariant
    "AttemptSnapshot"       integer       NULL,
    "GateVerdictSnapshot"   text          NULL,
    "ProjectType"           text          NULL,
    "ImportedAt"            text          NULL,
    "Overflow"              jsonb         NULL
);

-- Per-model usage inside one phase execution. A CHILD TABLE, not JSON, because BRD-158 requires
-- WHERE "Model" = … and a blob cannot serve one (ADR-025). Money stays fixed precision here too.
CREATE TABLE IF NOT EXISTS "PbPhaseModelUsage" (
    "UserId"           integer       NOT NULL,
    "Repo"             text          NOT NULL,
    "PhaseExecutionId" text          NOT NULL,
    "Model"            text          NOT NULL,
    "Turns"            integer       NULL,
    "TokensInput"      bigint        NULL,
    "TokensOutput"     bigint        NULL,
    "TokensReasoning"  bigint        NULL,
    "TokensCacheRead"  bigint        NULL,
    "TokensCacheWrite" bigint        NULL,
    "TokensIn"         bigint        NULL,
    "TokensOut"        bigint        NULL,
    "CostUsd"          numeric(20,10) NULL,
    "CostStatus"       text          NULL,
    "ActiveMs"         bigint        NULL
);

-- One row per subagent session inside a phase execution; "ParentSessionId" is what the recursive
-- subagent tree (BRD-159) is walked over, which is why it carries its own read index below.
CREATE TABLE IF NOT EXISTS "PbPhaseSubagent" (
    "UserId"           integer       NOT NULL,
    "Repo"             text          NOT NULL,
    "PhaseExecutionId" text          NOT NULL,
    "SessionId"        text          NOT NULL,
    "ParentSessionId"  text          NULL,
    "Agent"            text          NULL,
    "StartedAt"        text          NULL,
    "EndedAt"          text          NULL,
    "ElapsedMs"        bigint        NULL,
    "Complete"         boolean       NULL,
    "Turns"            integer       NULL,
    "TokensIn"         bigint        NULL,
    "TokensOut"        bigint        NULL,
    "CostUsd"          numeric(20,10) NULL,
    "CostStatus"       text          NULL
);

-- unique keys — "UserId" is part of every one of them (ADR-013)
CREATE UNIQUE INDEX IF NOT EXISTS "UcPbPhaseExecUserRepoId"
    ON "PbPhaseExecution"  ("UserId", "Repo", "PhaseExecutionId");
CREATE UNIQUE INDEX IF NOT EXISTS "UcPbPhaseModelUserRepoIdModel"
    ON "PbPhaseModelUsage" ("UserId", "Repo", "PhaseExecutionId", "Model");
CREATE UNIQUE INDEX IF NOT EXISTS "UcPbPhaseSubUserRepoIdSession"
    ON "PbPhaseSubagent"   ("UserId", "Repo", "PhaseExecutionId", "SessionId");

-- read paths
CREATE INDEX IF NOT EXISTS "IxPbPhaseExecUserRepo" ON "PbPhaseExecution" ("UserId", "Repo");
CREATE INDEX IF NOT EXISTS "IxPbPhaseExecPhase"    ON "PbPhaseExecution" ("UserId", "Phase");
CREATE INDEX IF NOT EXISTS "IxPbPhaseSubParent"    ON "PbPhaseSubagent"  ("UserId", "ParentSessionId");

-- ---------------------------------------------------------------- provenance (REQ-NFR-019 / BRD-143)

-- The ledger of dataset identities an ingest path states it ACTUALLY OBTAINED.
--
-- On 2026-08-29 the BRD §13 parity re-run found 155 rows across "Gate"/"Run"/"Session"/"Commit"
-- carrying two "SourceSha" values that do not exist in their repositories. They had been seeded
-- straight into the tables, and the only reason anyone noticed is that the counts disagreed with
-- upstream — had there been fewer rows the numbers would have looked plausible and been wrong. A
-- "SourceSha" is what BRD §13 pins a quotable figure to and what /export publishes as dataset
-- identity, so an invented one makes an exported number unreproducible by the person checking it.
--
-- This table is the oracle that makes such a row detectable WITHOUT a network call: the sync writes a
-- row here at the moment it fetches, the import writes one at the moment it commits a bundle, and
-- ProvenanceAudit reports any stored "SourceSha" that matches no entry here, no "SyncState"."LastSha",
-- no "UserRepo"."BundleSha" and no raw-archive file name.
CREATE TABLE IF NOT EXISTS "SourceProvenance" (
    "UserId"     integer NOT NULL,
    "Repo"       text    NOT NULL,
    "SourceSha"  text    NOT NULL,
    "Kind"       text    NOT NULL,   -- api | import | archive
    "ObtainedTs" text    NOT NULL,
    CONSTRAINT "PkSourceProvenance" PRIMARY KEY ("UserId", "Repo", "SourceSha")
);

CREATE INDEX IF NOT EXISTS "IxSourceProvenanceUser" ON "SourceProvenance" ("UserId");

-- Adoption of what is already known, run on every startup because it derives ONLY from facts an ingest
-- path recorded: the SHA a sync stamped onto "SyncState", and the bundle sha256 an import stamped onto
-- "UserRepo". It can never manufacture provenance for a SHA nobody obtained, so it is idempotent and
-- safe to repeat. Rows already present are left exactly as they are.
INSERT INTO "SourceProvenance" ("UserId", "Repo", "SourceSha", "Kind", "ObtainedTs")
SELECT "UserId", "Repo", "LastSha", 'api', COALESCE("LastSyncTs", '')
FROM "SyncState"
WHERE "LastSha" IS NOT NULL AND btrim("LastSha") <> ''
ON CONFLICT ON CONSTRAINT "PkSourceProvenance" DO NOTHING;

INSERT INTO "SourceProvenance" ("UserId", "Repo", "SourceSha", "Kind", "ObtainedTs")
SELECT "UserId", "Repo", "BundleSha", 'import', "ConnectedTs"
FROM "UserRepo"
WHERE "BundleSha" IS NOT NULL AND btrim("BundleSha") <> ''
ON CONFLICT ON CONSTRAINT "PkSourceProvenance" DO NOTHING;

-- REQ-NFR-019 clause 1, second layer. StreamParser.Parse refuses a blank source SHA, but the 155 rows
-- arrived through raw SQL, which is exactly the layer the application does not control. PostgreSQL
-- enforces the same rule for every writer: a stream row with no provenance at all cannot exist.
-- Wrapped so the file stays idempotent — ALTER TABLE ... ADD CONSTRAINT has no IF NOT EXISTS.
--
-- The existence test is NOT decoration. This script is re-applied by every startup AND by every
-- `rebuild`, and ALTER TABLE takes an ACCESS EXCLUSIVE lock the moment it is attempted — even when it
-- is about to fail as a duplicate and be rolled back. Attempting it unconditionally deadlocked the
-- Postgres-backed test classes against each other within minutes of being added. Checking
-- pg_constraint first means an established database takes no lock at all.
DO $$
DECLARE
    vTable text;
    vName  text;
BEGIN
    FOREACH vTable IN ARRAY ARRAY['Run', 'Gate', 'Session', 'Commit', 'Miss', 'MissFix', 'MissAmend', 'PbEvent']
    LOOP
        vName := 'Ck' || vTable || 'SourceShaPresent';

        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = vName AND conrelid = format('%I', vTable)::regclass)
        THEN
            EXECUTE format(
                'ALTER TABLE %I ADD CONSTRAINT %I CHECK (btrim("SourceSha") <> '''')',
                vTable,
                vName);
        END IF;
    END LOOP;
END
$$;
