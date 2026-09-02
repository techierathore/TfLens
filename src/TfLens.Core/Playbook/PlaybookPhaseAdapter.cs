using System.Globalization;
using System.Text.Json;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;

namespace TfLens.Core.Playbook;

/// <summary>
/// Normalizes one schema-2 <c>phase-metric</c> NDJSON record into the three <c>PbPhase*</c> rows
/// (REQ-FN-094, BRD-153, ADR-023, ADR-025).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no second ingest path (BRD-132).</b> The Playbook exporter reads a transient
/// <c>events.ndjson</c> that rotates, so TfLens can neither poll it nor ask the framework to commit it;
/// the exporter's stdout arrives through <c>TelemetryImportService</c> like any other bundle. That makes
/// this class a <i>mapper</i>, not a pipeline: <see cref="StreamParser"/> owns the line loop, the
/// invalid-line count and the dedupe, exactly as it does for the five TechieFlow streams, and the only
/// wiring this record type needed was one entry in <c>ImportStreamCatalog</c>.
/// </para>
/// <para>
/// <b>Nothing here is a zero.</b> An absent file, an empty file, a malformed line, a record of another
/// kind and an unsupported harness all produce <b>no row</b> — never a row of zeroes. The distinction is
/// the whole point of the requirement: a run that spent nothing and a window nobody could read are two
/// different facts, and only the first one is a number (BRD-153, BRD-163).
/// </para>
/// <para>
/// <b>Every row keeps its provenance.</b> <see cref="PbPhaseExecutionRecord.SourceSchema"/>,
/// <see cref="PbPhaseExecutionRecord.SourceHarness"/>, the repository identity (the <c>UserId</c> and
/// <c>Repo</c> on every one of the three records) and <see cref="PbPhaseExecutionRecord.ImportedAt"/>
/// are columns; the importer version and the bundle sha256 have none, so they are preserved under a
/// reserved <c>tflens</c> key inside <see cref="PbPhaseExecutionRecord.Overflow"/> beside the producer
/// properties this column set does not cover.
/// </para>
/// </remarks>
public static class PlaybookPhaseAdapter
{
    /// <summary>The producer's <c>kind</c> for the records this adapter reads.</summary>
    public const string RecordKind = "phase-metric";

    /// <summary>The <c>schema</c> version this adapter normalizes.</summary>
    /// <remarks>
    /// A record declaring a lower schema is a sparse schema-1 event: it is read, but it is labelled
    /// <see cref="LegacyUnverified"/> and stays out of every schema-2 comparison (BRD-161).
    /// </remarks>
    public const int Schema2 = 2;

    /// <summary>The token status a schema-1 row carries, and the reason it is drill-down only.</summary>
    public const string LegacyUnverified = "legacy-unverified";

    /// <summary>The reserved <see cref="PbPhaseExecutionRecord.Overflow"/> key TfLens writes under.</summary>
    /// <remarks>
    /// The producer never emits a <c>tflens</c> property, so the key cannot collide with one of its own,
    /// and a reader can tell our two provenance values from the producer's verbatim extras by the key
    /// alone. It exists because the importer version and the bundle sha256 have no column on
    /// <c>"PbPhaseExecution"</c> and losing them would break the retention clause of BRD-153.
    /// </remarks>
    public const string TfLensOverflowKey = "tflens";

    /// <summary>The harnesses that have a normalized schema-2 phase producer today (BRD-163).</summary>
    /// <remarks>
    /// Claude Code is deliberately absent. Its adapter does not exist, so its phase effort is
    /// <b>unsupported</b> — a data gap, which is a different fact from a harness that ran and spent
    /// nothing, and must never render as zero.
    /// </remarks>
    public static readonly IReadOnlyList<string> NormalizedHarnesses = ["opencode"];

    /// <summary>The wire fields the schema-2 contract documents (§3), for the Coverage report.</summary>
    public static readonly IReadOnlyList<string> DocumentedFields =
    [
        "schema", "kind", "phase_execution_id", "phase", "started_at", "ended_at", "elapsed_ms",
        "complete", "end_reason", "model", "models", "tokens", "tokens_in", "tokens_out", "cost_usd",
        "attempt", "gate_verdict", "project_type", "timestamp", "session_id", "harness", "granularity",
        "turns", "observed_active_effort", "data_quality", "tokens_scope", "subagents", "tier"
    ];

    /// <summary>
    /// Wire fields <c>"PbPhaseExecution"</c> (or one of its two child tables) has somewhere to put.
    /// </summary>
    /// <remarks>
    /// <c>kind</c> is resolved by the table the row lands in, and <c>timestamp</c> has no column at all —
    /// the window's boundaries are <c>started_at</c> and <c>ended_at</c>, and a third instant beside them
    /// would invite a duration nobody measured. It is preserved verbatim in <c>Overflow</c> instead.
    /// </remarks>
    private static readonly HashSet<string> MappedFields =
        DocumentedFields.Where(aF => !string.Equals(aF, "timestamp", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The version of the assembly that normalized the row, retained on every one (BRD-153).</summary>
    public static string ImporterVersion { get; } =
        typeof(PlaybookPhaseAdapter).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Reads one NDJSON record into its execution row and the two child row sets.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> — which the caller counts as an invalid line and skips — when the object is
    /// not a <c>phase-metric</c> record or carries no <c>phase_execution_id</c> to key it on. Neither
    /// case is an exception and neither is a zero-valued run: there is simply nothing to store.
    /// </remarks>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aUserId">The AppManager user the rows belong to.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aSourceSha">The bundle sha256 the rows arrived on, retained in <c>Overflow</c>.</param>
    /// <param name="aImportedAt">ISO-8601 UTC instant the import ran.</param>
    /// <returns>The three row sets, or <c>null</c> when the record is not one of ours.</returns>
    public static PhaseRowSet? Read(
        JsonElement aObj, int aUserId, string aRepo, string aSourceSha, string aImportedAt)
    {
        ArgumentNullException.ThrowIfNull(aRepo);

        if (aObj.ValueKind != JsonValueKind.Object || !IsPhaseMetric(aObj))
        {
            return null;
        }

        var vId = ReadString(aObj, "phase_execution_id");

        if (string.IsNullOrWhiteSpace(vId))
        {
            return null;
        }

        var vExecution = BuildExecution(aObj, aUserId, aRepo, aSourceSha, aImportedAt, vId);

        return new PhaseRowSet(
            vExecution,
            BuildModels(aObj, aUserId, aRepo, vId),
            BuildSubagents(aObj, aUserId, aRepo, vId));
    }

    /// <summary>
    /// Says whether a record is a <c>phase-metric</c> one.
    /// </summary>
    /// <remarks>
    /// The <c>kind</c> is what identifies it, not the file it arrived in: a bundle may legitimately carry
    /// diagnostics or another record type on the same stream, and those are skipped rather than coerced.
    /// </remarks>
    /// <param name="aObj">The record's JSON object.</param>
    /// <returns><c>true</c> when the record declares the phase-metric kind.</returns>
    public static bool IsPhaseMetric(JsonElement aObj) =>
        string.Equals(ReadString(aObj, "kind"), RecordKind, StringComparison.Ordinal);

    /// <summary>
    /// Says whether a harness has a normalized phase producer (BRD-163).
    /// </summary>
    /// <param name="aHarness">The harness name, or <c>null</c> when none was detected.</param>
    /// <returns><c>true</c> only for a harness that emits the schema-2 record.</returns>
    public static bool IsHarnessSupported(string? aHarness) =>
        !string.IsNullOrWhiteSpace(aHarness)
        && NormalizedHarnesses.Contains(aHarness, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Collapses executions repeated inside one file, keeping the <b>last</b> emission.
    /// </summary>
    /// <remarks>
    /// Keep-last rather than keep-first because the exporter re-emits every currently readable window:
    /// a later line for the same <c>phase_execution_id</c> is the same window read further on, so it is
    /// at least as complete as the earlier one. The database's <c>UcPbPhaseExecUserRepoId</c> applies the
    /// identical rule across files.
    /// </remarks>
    /// <param name="aRecords">The executions as parsed, in file order.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    public static DedupeResult<PbPhaseExecutionRecord> DedupeExecutions(
        IReadOnlyList<PbPhaseExecutionRecord> aRecords) =>
        KeepLast(aRecords, aR => $"{aR.UserId}{aR.Repo}{aR.PhaseExecutionId}");

    /// <summary>
    /// Collapses per-model rows repeated inside one file, keeping the last.
    /// </summary>
    /// <param name="aRecords">The per-model rows as parsed.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    public static DedupeResult<PbPhaseModelUsageRecord> DedupeModelUsages(
        IReadOnlyList<PbPhaseModelUsageRecord> aRecords) =>
        KeepLast(aRecords, aR => $"{aR.UserId}{aR.Repo}{aR.PhaseExecutionId}{aR.Model}");

    /// <summary>
    /// Collapses sub-agent rows repeated inside one file, keeping the last.
    /// </summary>
    /// <remarks>
    /// This is also what makes a recursive grandchild appear <b>exactly once</b>: a producer that both
    /// nests a child under its parent and repeats it in the flat list yields one row, not two, so no
    /// phase total can count it twice (BRD-159).
    /// </remarks>
    /// <param name="aRecords">The sub-agent rows as parsed.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    public static DedupeResult<PbPhaseSubagentRecord> DedupeSubagents(
        IReadOnlyList<PbPhaseSubagentRecord> aRecords) =>
        KeepLast(aRecords, aR => $"{aR.UserId}{aR.Repo}{aR.PhaseExecutionId}{aR.SessionId}");

    /// <summary>Builds the execution row, including the two nested blocks that fan out to columns.</summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aUserId">The AppManager user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aSourceSha">The bundle sha256.</param>
    /// <param name="aImportedAt">ISO-8601 UTC import instant.</param>
    /// <param name="aId">The producer's phase execution id.</param>
    /// <returns>The execution row.</returns>
    private static PbPhaseExecutionRecord BuildExecution(
        JsonElement aObj, int aUserId, string aRepo, string aSourceSha, string aImportedAt, string aId)
    {
        var vTokens = ReadTokens(aObj, "tokens");
        var vActive = Child(aObj, "observed_active_effort");
        var vQuality = Child(aObj, "data_quality");
        var vSubagents = Child(aObj, "subagents");
        var vSchema = ReadInt(aObj, "schema");

        return new PbPhaseExecutionRecord
        {
            UserId = aUserId,
            Repo = aRepo,
            PhaseExecutionId = aId,
            SourceSchema = vSchema,
            SourceHarness = ReadString(aObj, "harness"),
            Phase = ReadString(aObj, "phase"),
            SessionId = ReadString(aObj, "session_id"),
            Granularity = ReadString(aObj, "granularity"),
            StartedAt = ReadUtc(aObj, "started_at"),
            EndedAt = ReadUtc(aObj, "ended_at"),
            ElapsedMs = ReadLong(aObj, "elapsed_ms"),
            Complete = ReadBool(aObj, "complete"),
            EndReason = ReadString(aObj, "end_reason"),
            DominantModel = ReadString(aObj, "model"),
            Tier = ReadString(aObj, "tier"),
            TokensInput = vTokens.Input,
            TokensOutput = vTokens.Output,
            TokensReasoning = vTokens.Reasoning,
            TokensCacheRead = vTokens.CacheRead,
            TokensCacheWrite = vTokens.CacheWrite,
            TokensIn = ReadLong(aObj, "tokens_in"),
            TokensOut = ReadLong(aObj, "tokens_out"),
            CostUsd = ReadDecimal(aObj, "cost_usd"),
            Turns = ReadInt(aObj, "turns"),
            AssistantElapsedMs = ReadLong(vActive, "assistant_elapsed_ms"),
            ToolElapsedMs = ReadLong(vActive, "tool_elapsed_ms"),
            ObservedActiveMs = ReadLong(vActive, "observed_active_ms"),
            ActiveCoverage = ReadString(vActive, "coverage"),
            DataQualityValid = ReadBool(vQuality, "valid"),
            DataQualityIssues = ReadJsonText(vQuality, "issues"),
            TokenStatus = TokenStatusOf(vQuality, vSchema),
            CostStatus = ReadString(vQuality, "cost_status"),
            TokensScope = ReadString(aObj, "tokens_scope"),
            SubagentsSpawned = ReadInt(vSubagents, "spawned"),
            SubagentsContributors = ReadInt(vSubagents, "contributors"),
            AttemptSnapshot = ReadInt(aObj, "attempt"),
            GateVerdictSnapshot = ReadString(aObj, "gate_verdict"),
            ProjectType = ReadString(aObj, "project_type"),
            ImportedAt = aImportedAt,
            Overflow = BuildOverflow(aObj, aSourceSha)
        };
    }

    /// <summary>
    /// Reads the token status, labelling anything below schema 2 <see cref="LegacyUnverified"/>.
    /// </summary>
    /// <remarks>
    /// A sparse schema-1 event is normalized for backward compatibility and remains reachable by
    /// drill-down, but it never enters a schema-2 comparison — which is a property of the label, so the
    /// label is applied at ingest where the schema number is still in front of us (BRD-161, §7).
    /// </remarks>
    /// <param name="aQuality">The <c>data_quality</c> object, or a default element when absent.</param>
    /// <param name="aSchema">The record's declared schema version.</param>
    /// <returns>The token status.</returns>
    private static string? TokenStatusOf(JsonElement aQuality, int? aSchema) =>
        aSchema is not null && aSchema < Schema2
            ? LegacyUnverified
            : ReadString(aQuality, "token_status");

    /// <summary>Builds the per-model rows — the only basis any model figure is ever computed from.</summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aUserId">The AppManager user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aId">The producer's phase execution id.</param>
    /// <returns>One row per named model; empty when the producer named none.</returns>
    private static IReadOnlyList<PbPhaseModelUsageRecord> BuildModels(
        JsonElement aObj, int aUserId, string aRepo, string aId)
    {
        if (!aObj.TryGetProperty("models", out var vModels) || vModels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var vRows = new List<PbPhaseModelUsageRecord>();

        foreach (var vEntry in vModels.EnumerateArray())
        {
            var vName = ReadString(vEntry, "model");

            if (vEntry.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(vName))
            {
                continue;
            }

            vRows.Add(BuildModel(vEntry, aUserId, aRepo, aId, vName));
        }

        return vRows;
    }

    /// <summary>Builds one per-model row.</summary>
    /// <param name="aEntry">One member of <c>models[]</c>.</param>
    /// <param name="aUserId">The AppManager user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aId">The producer's phase execution id.</param>
    /// <param name="aModel">The model name, exactly as the producer wrote it.</param>
    /// <returns>The per-model row.</returns>
    private static PbPhaseModelUsageRecord BuildModel(
        JsonElement aEntry, int aUserId, string aRepo, string aId, string aModel)
    {
        var vTokens = ReadTokens(aEntry, "tokens");

        return new PbPhaseModelUsageRecord
        {
            UserId = aUserId,
            Repo = aRepo,
            PhaseExecutionId = aId,
            Model = aModel,
            Turns = ReadInt(aEntry, "turns"),
            TokensInput = vTokens.Input,
            TokensOutput = vTokens.Output,
            TokensReasoning = vTokens.Reasoning,
            TokensCacheRead = vTokens.CacheRead,
            TokensCacheWrite = vTokens.CacheWrite,
            TokensIn = ReadLong(aEntry, "tokens_in"),
            TokensOut = ReadLong(aEntry, "tokens_out"),
            CostUsd = ReadDecimal(aEntry, "cost_usd"),
            CostStatus = ReadString(aEntry, "cost_status"),
            ActiveMs = ReadLong(aEntry, "active_ms")
        };
    }

    /// <summary>
    /// Flattens <c>subagents.sessions[]</c>, including any nested children, into one row per session.
    /// </summary>
    /// <remarks>
    /// The tree is rebuilt at read time from <see cref="PbPhaseSubagentRecord.ParentSessionId"/>, so a
    /// producer that nests and a producer that emits a flat list with parent links both normalize to the
    /// same rows — and a grandchild is stored once whichever way it arrived (BRD-159).
    /// </remarks>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aUserId">The AppManager user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aId">The producer's phase execution id.</param>
    /// <returns>One row per sub-agent session; empty when the producer listed none.</returns>
    private static IReadOnlyList<PbPhaseSubagentRecord> BuildSubagents(
        JsonElement aObj, int aUserId, string aRepo, string aId)
    {
        var vRows = new List<PbPhaseSubagentRecord>();
        CollectSubagents(Child(Child(aObj, "subagents"), "sessions"), aUserId, aRepo, aId, null, vRows);
        return vRows;
    }

    /// <summary>Walks one <c>sessions[]</c> array and its nested arrays, appending a row for each.</summary>
    /// <param name="aSessions">The array to walk; a non-array is ignored.</param>
    /// <param name="aUserId">The AppManager user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aId">The producer's phase execution id.</param>
    /// <param name="aParent">The session that nests this array, when it was nested.</param>
    /// <param name="aRows">The accumulating rows.</param>
    private static void CollectSubagents(
        JsonElement aSessions,
        int aUserId,
        string aRepo,
        string aId,
        string? aParent,
        List<PbPhaseSubagentRecord> aRows)
    {
        if (aSessions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var vSession in aSessions.EnumerateArray())
        {
            var vSessionId = ReadString(vSession, "session_id");

            if (vSession.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(vSessionId))
            {
                continue;
            }

            aRows.Add(BuildSubagent(vSession, aUserId, aRepo, aId, aParent, vSessionId));
            CollectSubagents(Child(vSession, "sessions"), aUserId, aRepo, aId, vSessionId, aRows);
        }
    }

    /// <summary>Builds one sub-agent row.</summary>
    /// <remarks>
    /// An absent <c>agent</c> stays <c>null</c>. It is displayed as <i>unavailable</i> and is never
    /// inferred from a title or a model name, because a guess here would read as a measurement (BRD-159).
    /// </remarks>
    /// <param name="aSession">One member of <c>sessions[]</c>.</param>
    /// <param name="aUserId">The AppManager user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aId">The producer's phase execution id.</param>
    /// <param name="aParent">The nesting session, when this row arrived nested.</param>
    /// <param name="aSessionId">The sub-agent's own session id.</param>
    /// <returns>The sub-agent row.</returns>
    private static PbPhaseSubagentRecord BuildSubagent(
        JsonElement aSession, int aUserId, string aRepo, string aId, string? aParent, string aSessionId) =>
        new()
        {
            UserId = aUserId,
            Repo = aRepo,
            PhaseExecutionId = aId,
            SessionId = aSessionId,
            ParentSessionId = ReadString(aSession, "parent_session_id") ?? aParent,
            Agent = ReadString(aSession, "agent"),
            StartedAt = ReadUtc(aSession, "started_at"),
            EndedAt = ReadUtc(aSession, "ended_at"),
            ElapsedMs = ReadLong(aSession, "elapsed_ms"),
            Complete = ReadBool(aSession, "complete"),
            Turns = ReadInt(aSession, "turns"),
            TokensIn = ReadLong(aSession, "tokens_in"),
            TokensOut = ReadLong(aSession, "tokens_out"),
            CostUsd = ReadDecimal(aSession, "cost_usd"),
            CostStatus = ReadString(aSession, "cost_status")
        };

    /// <summary>
    /// Preserves every producer property this column set does not cover, plus TfLens's own provenance.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aSourceSha">The bundle sha256 the row arrived on.</param>
    /// <returns>A JSON object as text; never <c>null</c>, because the provenance is always written.</returns>
    private static string BuildOverflow(JsonElement aObj, string aSourceSha)
    {
        using var vStream = new MemoryStream();

        using (var vWriter = new Utf8JsonWriter(vStream))
        {
            vWriter.WriteStartObject();

            foreach (var vProperty in aObj.EnumerateObject().Where(aP => !MappedFields.Contains(aP.Name)))
            {
                vProperty.WriteTo(vWriter);
            }

            vWriter.WriteStartObject(TfLensOverflowKey);
            vWriter.WriteString("importer_version", ImporterVersion);
            vWriter.WriteString("source_sha", aSourceSha);
            vWriter.WriteEndObject();
            vWriter.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(vStream.ToArray());
    }

    /// <summary>Keeps the last record per natural key, in first-seen order.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="aRecords">The records in file order.</param>
    /// <param name="aKey">The natural key of one record.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    private static DedupeResult<T> KeepLast<T>(IReadOnlyList<T> aRecords, Func<T, string> aKey)
    {
        var vOrder = new List<string>();
        var vKept = new Dictionary<string, T>(StringComparer.Ordinal);

        foreach (var vRecord in aRecords)
        {
            var vKey = aKey(vRecord);

            if (!vKept.ContainsKey(vKey))
            {
                vOrder.Add(vKey);
            }

            vKept[vKey] = vRecord;
        }

        return new DedupeResult<T>(vOrder.Select(aK => vKept[aK]).ToList(), aRecords.Count - vOrder.Count);
    }

    /// <summary>Reads a nested object, yielding a default element when it is absent.</summary>
    /// <param name="aObj">The parent object.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The nested element, or a default one every reader below answers <c>null</c> for.</returns>
    private static JsonElement Child(JsonElement aObj, string aName) =>
        aObj.ValueKind == JsonValueKind.Object && aObj.TryGetProperty(aName, out var vChild)
            ? vChild
            : default;

    /// <summary>Reads the five token legs of a <c>tokens</c> object; each stays <c>null</c> when absent.</summary>
    /// <param name="aObj">The object carrying the block.</param>
    /// <param name="aName">The block's property name.</param>
    /// <returns>The five legs.</returns>
    private static PhaseTokens ReadTokens(JsonElement aObj, string aName)
    {
        var vTokens = Child(aObj, aName);

        return new PhaseTokens(
            ReadLong(vTokens, "input"),
            ReadLong(vTokens, "output"),
            ReadLong(vTokens, "reasoning"),
            ReadLong(vTokens, "cache_read"),
            ReadLong(vTokens, "cache_write"));
    }

    /// <summary>Reads a string property; absent or JSON-null yields <c>null</c>, never <c>""</c>.</summary>
    /// <param name="aObj">The object to read from.</param>
    /// <param name="aName">The wire name.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static string? ReadString(JsonElement aObj, string aName) =>
        aObj.ValueKind == JsonValueKind.Object
        && aObj.TryGetProperty(aName, out var vValue)
        && vValue.ValueKind == JsonValueKind.String
            ? vValue.GetString()
            : null;

    /// <summary>
    /// Reads a timestamp property and normalizes it to UTC.
    /// </summary>
    /// <remarks>
    /// Storage and filtering are UTC and only display is localized (BRD-161), so the conversion happens
    /// once, here, rather than in every comparison that would otherwise have to remember.
    /// </remarks>
    /// <param name="aObj">The object to read from.</param>
    /// <param name="aName">The wire name.</param>
    /// <returns>The instant as ISO-8601 UTC, the original text when it does not parse, or <c>null</c>.</returns>
    private static string? ReadUtc(JsonElement aObj, string aName)
    {
        var vText = ReadString(aObj, aName);

        if (vText is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            vText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var vMoment)
            ? vMoment.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            : vText;
    }

    /// <summary>Reads an array or object property as its verbatim JSON text.</summary>
    /// <param name="aObj">The object to read from.</param>
    /// <param name="aName">The wire name.</param>
    /// <returns>The raw JSON text, or <c>null</c> when absent or JSON-null.</returns>
    private static string? ReadJsonText(JsonElement aObj, string aName) =>
        aObj.ValueKind == JsonValueKind.Object
        && aObj.TryGetProperty(aName, out var vValue)
        && vValue.ValueKind != JsonValueKind.Null
            ? vValue.GetRawText()
            : null;

    /// <summary>Reads a 32-bit integer; absent stays <c>null</c> and a present <c>0</c> stays <c>0</c>.</summary>
    /// <param name="aObj">The object to read from.</param>
    /// <param name="aName">The wire name.</param>
    /// <returns>The value, or <c>null</c> when the field was not captured.</returns>
    private static int? ReadInt(JsonElement aObj, string aName) =>
        aObj.ValueKind == JsonValueKind.Object
        && aObj.TryGetProperty(aName, out var vValue)
        && vValue.ValueKind == JsonValueKind.Number
        && vValue.TryGetInt32(out var vInt)
            ? vInt
            : null;

    /// <summary>Reads a 64-bit integer — a phase tree's cumulative counters are not int32 quantities.</summary>
    /// <param name="aObj">The object to read from.</param>
    /// <param name="aName">The wire name.</param>
    /// <returns>The value, or <c>null</c> when the field was not captured.</returns>
    private static long? ReadLong(JsonElement aObj, string aName) =>
        aObj.ValueKind == JsonValueKind.Object
        && aObj.TryGetProperty(aName, out var vValue)
        && vValue.ValueKind == JsonValueKind.Number
        && vValue.TryGetInt64(out var vLong)
            ? vLong
            : null;

    /// <summary>Reads money as fixed-precision decimal; absent stays <c>null</c>, never zero spend.</summary>
    /// <param name="aObj">The object to read from.</param>
    /// <param name="aName">The wire name.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static decimal? ReadDecimal(JsonElement aObj, string aName) =>
        aObj.ValueKind == JsonValueKind.Object
        && aObj.TryGetProperty(aName, out var vValue)
        && vValue.ValueKind == JsonValueKind.Number
        && vValue.TryGetDecimal(out var vDecimal)
            ? vDecimal
            : null;

    /// <summary>Reads a boolean; absent stays <c>null</c> and is never coerced to <c>false</c>.</summary>
    /// <param name="aObj">The object to read from.</param>
    /// <param name="aName">The wire name.</param>
    /// <returns><c>true</c>, <c>false</c>, or <c>null</c> when the field was absent.</returns>
    private static bool? ReadBool(JsonElement aObj, string aName)
    {
        if (aObj.ValueKind != JsonValueKind.Object || !aObj.TryGetProperty(aName, out var vValue))
        {
            return null;
        }

        return vValue.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    /// <summary>The five token legs of one <c>tokens</c> block.</summary>
    /// <param name="Input">Input tokens.</param>
    /// <param name="Output">Output tokens.</param>
    /// <param name="Reasoning">Reasoning tokens.</param>
    /// <param name="CacheRead">Cache-read tokens.</param>
    /// <param name="CacheWrite">Cache-write tokens.</param>
    private readonly record struct PhaseTokens(
        long? Input, long? Output, long? Reasoning, long? CacheRead, long? CacheWrite);
}

/// <summary>
/// What one <c>phase-metric</c> line normalizes to: one execution and its two child row sets.
/// </summary>
/// <param name="Execution">The <c>"PbPhaseExecution"</c> row.</param>
/// <param name="Models">The <c>"PbPhaseModelUsage"</c> rows — the only basis of any per-model figure.</param>
/// <param name="Subagents">The <c>"PbPhaseSubagent"</c> rows, flattened from the producer's sessions.</param>
public sealed record PhaseRowSet(
    PbPhaseExecutionRecord Execution,
    IReadOnlyList<PbPhaseModelUsageRecord> Models,
    IReadOnlyList<PbPhaseSubagentRecord> Subagents);
