namespace TfLens.Core.Contracts;

/// <summary>
/// One schema-2 <c>phase-metric</c> execution, stored in the <c>"PbPhaseExecution"</c> table
/// (REQ-FN-095, BRD-154, ADR-025).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three tables rather than one wide row.</b> The Playbook contract requires filtering and
/// aggregating on <i>any</i> <c>models[]</c> member (BRD-158) and rendering a recursive sub-agent tree
/// by <c>session_id</c> / <c>parent_id</c> (BRD-159); neither is expressible over a JSON column, and a
/// mixed-model execution flattened onto its dominant model is the exact misattribution BRD-150 forbids.
/// So the per-model split lives in <see cref="PbPhaseModelUsageRecord"/> and the sub-agent sessions in
/// <see cref="PbPhaseSubagentRecord"/>, both linked on <see cref="PhaseExecutionId"/>.
/// </para>
/// <para>
/// <b>Timing is three types, not three names for one number (ADR-027).</b> <see cref="ElapsedMs"/> is
/// wall clock. <see cref="ObservedActiveMs"/> is the producer's <i>union</i> of assistant and tool
/// intervals, overlaps counted once. <see cref="AssistantElapsedMs"/> and <see cref="ToolElapsedMs"/>
/// are diagnostics and <b>must never be added together</b> — an assistant envelope can contain tool
/// execution, which is exactly why the producer unions rather than sums. Human effort has no member at
/// all: neither framework captures it, and a property that exists is a property something eventually
/// fills by inference from wall-clock time.
/// </para>
/// <para>
/// <b>A row that fails an invariant is quarantined, not corrected.</b> <see cref="DataQualityValid"/>
/// <c>false</c> — or a failed compatibility sum, or <c>spawned &lt; contributors</c> — means the row is
/// stored, displayed with its reason and excluded from every numeric aggregate. The producer may retain
/// zero-valued totals on an invalid row, so a consumer that trusts the numbers there gets a confident
/// zero rather than an error.
/// </para>
/// <para>
/// Every optional member is nullable and an absent value stays <c>null</c>, never <c>0</c> — the same
/// rule the stream records keep (SCHEMA.md §2.5).
/// </para>
/// </remarks>
public sealed record PbPhaseExecutionRecord
{
    /// <summary>AppManager user who owns the repository this execution came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>The producer's stable identifier for one phase execution — the dedupe key and the link.</summary>
    public required string PhaseExecutionId { get; init; }

    /// <summary>The <c>phase-metric</c> schema version the row was read from.</summary>
    public int? SourceSchema { get; init; }

    /// <summary>The harness the producer detected; <c>null</c> means not detected.</summary>
    public string? SourceHarness { get; init; }

    /// <summary>The phase command that ran.</summary>
    public string? Phase { get; init; }

    /// <summary>The main session the execution ran in.</summary>
    public string? SessionId { get; init; }

    /// <summary>How finely the producer resolved the window.</summary>
    public string? Granularity { get; init; }

    /// <summary>ISO-8601 start of the window.</summary>
    public string? StartedAt { get; init; }

    /// <summary>ISO-8601 end of the window; <c>null</c> on an incomplete window and never back-filled.</summary>
    public string? EndedAt { get; init; }

    /// <summary>Wall-clock duration in milliseconds — not active time, and never a person's effort.</summary>
    public long? ElapsedMs { get; init; }

    /// <summary>Whether the window closed; <c>false</c> implies <see cref="EndReason"/> is <c>eof</c>.</summary>
    public bool? Complete { get; init; }

    /// <summary>Why the window ended.</summary>
    public string? EndReason { get; init; }

    /// <summary>
    /// The model that answered most of the turns — a <b>label</b>, never a basis for per-model effort.
    /// </summary>
    /// <remarks>Per-model figures read <see cref="PbPhaseModelUsageRecord"/> (BRD-150, ADR-025).</remarks>
    public string? DominantModel { get; init; }

    /// <summary>Routing tier the execution requested.</summary>
    public string? Tier { get; init; }

    /// <summary>Input tokens over the window.</summary>
    public long? TokensInput { get; init; }

    /// <summary>Output tokens over the window.</summary>
    public long? TokensOutput { get; init; }

    /// <summary>Reasoning tokens over the window.</summary>
    public long? TokensReasoning { get; init; }

    /// <summary>Cache-read tokens over the window.</summary>
    public long? TokensCacheRead { get; init; }

    /// <summary>Cache-write tokens over the window.</summary>
    public long? TokensCacheWrite { get; init; }

    /// <summary>The producer's input-side compatibility total; checked against the legs, never trusted blindly.</summary>
    public long? TokensIn { get; init; }

    /// <summary>The producer's output-side compatibility total; checked against the legs, never trusted blindly.</summary>
    public long? TokensOut { get; init; }

    /// <summary>
    /// Provider cost in USD, as <b>fixed-precision decimal</b> — money is never a binary float.
    /// </summary>
    /// <remarks>
    /// Stored <c>numeric(20,10)</c>. A headline cost figure additionally requires <c>sole</c>
    /// attribution, a complete valid window and <see cref="CostStatus"/> <c>complete</c>; those guards
    /// are the Playbook's own and are deliberately stricter than the TechieFlow ones (ADR-024).
    /// </remarks>
    public decimal? CostUsd { get; init; }

    /// <summary>Assistant turns in the window.</summary>
    public int? Turns { get; init; }

    /// <summary><b>Diagnostic only.</b> Never added to <see cref="ToolElapsedMs"/> (ADR-027).</summary>
    public long? AssistantElapsedMs { get; init; }

    /// <summary><b>Diagnostic only.</b> Never added to <see cref="AssistantElapsedMs"/> (ADR-027).</summary>
    public long? ToolElapsedMs { get; init; }

    /// <summary>The producer's union of assistant and tool intervals, overlaps counted once (ADR-027).</summary>
    public long? ObservedActiveMs { get; init; }

    /// <summary>How much of the window the producer could observe activity across.</summary>
    public string? ActiveCoverage { get; init; }

    /// <summary>The producer's own verdict on the row; <c>false</c> quarantines it from every aggregate.</summary>
    public bool? DataQualityValid { get; init; }

    /// <summary>JSON array of the issues the producer or TfLens found, stored verbatim.</summary>
    public string? DataQualityIssues { get; init; }

    /// <summary>Completeness of the token window.</summary>
    public string? TokenStatus { get; init; }

    /// <summary>Completeness of the cost figure; only <c>complete</c> may reach a headline cost.</summary>
    public string? CostStatus { get; init; }

    /// <summary>
    /// Scope the token counts cover, and the gate on every fan-out claim.
    /// </summary>
    /// <remarks>
    /// Only a <c>tree</c>-scope window read the sub-agent transcripts, so only a <c>tree</c>-scope row
    /// can support a statement about <see cref="SubagentsSpawned"/> (ADR-026).
    /// </remarks>
    public string? TokensScope { get; init; }

    /// <summary>Sub-agent sessions spawned; the producer asserts this is at least <see cref="SubagentsContributors"/>.</summary>
    public int? SubagentsSpawned { get; init; }

    /// <summary>Sub-agent sessions that actually produced output.</summary>
    public int? SubagentsContributors { get; init; }

    /// <summary>The attempt number as it stood when the execution ended.</summary>
    public int? AttemptSnapshot { get; init; }

    /// <summary>The gate verdict as it stood when the execution ended.</summary>
    public string? GateVerdictSnapshot { get; init; }

    /// <summary>Declared or inferred project type; figures never pool across it.</summary>
    public string? ProjectType { get; init; }

    /// <summary>ISO-8601 timestamp the row was imported — phase output arrives through import (ADR-023).</summary>
    public string? ImportedAt { get; init; }

    /// <summary>JSON object of producer properties this column set does not cover, preserved verbatim.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One model's usage inside one phase execution, stored in the <c>"PbPhaseModelUsage"</c> table
/// (REQ-FN-095, BRD-158, ADR-025).
/// </summary>
/// <remarks>
/// A <b>child table</b>, deliberately, where <see cref="RunRecord.ModelTokensOut"/> is a JSON column:
/// this one must serve <c>WHERE "Model" = …</c> and an aggregate over any member of the model list,
/// and a blob cannot. That contrast is the rule — a per-model split read only ever as a whole stays
/// JSON; one that must be queried becomes a table.
/// </remarks>
public sealed record PbPhaseModelUsageRecord
{
    /// <summary>AppManager user who owns the repository this row came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>The <see cref="PbPhaseExecutionRecord.PhaseExecutionId"/> this usage belongs to.</summary>
    public required string PhaseExecutionId { get; init; }

    /// <summary>The model, exactly as the producer named it — part of the dedupe key.</summary>
    public required string Model { get; init; }

    /// <summary>Turns this model answered.</summary>
    public int? Turns { get; init; }

    /// <summary>Input tokens attributed to this model.</summary>
    public long? TokensInput { get; init; }

    /// <summary>Output tokens attributed to this model.</summary>
    public long? TokensOutput { get; init; }

    /// <summary>Reasoning tokens attributed to this model.</summary>
    public long? TokensReasoning { get; init; }

    /// <summary>Cache-read tokens attributed to this model.</summary>
    public long? TokensCacheRead { get; init; }

    /// <summary>Cache-write tokens attributed to this model.</summary>
    public long? TokensCacheWrite { get; init; }

    /// <summary>The producer's input-side compatibility total for this model.</summary>
    public long? TokensIn { get; init; }

    /// <summary>The producer's output-side compatibility total for this model.</summary>
    public long? TokensOut { get; init; }

    /// <summary>Provider cost in USD as fixed-precision decimal; never a binary float.</summary>
    public decimal? CostUsd { get; init; }

    /// <summary>Completeness of that cost; only <c>complete</c> may reach a headline figure.</summary>
    public string? CostStatus { get; init; }

    /// <summary>Active milliseconds attributed to this model; <c>null</c> when not measured.</summary>
    public long? ActiveMs { get; init; }
}

/// <summary>
/// One sub-agent session inside one phase execution, stored in the <c>"PbPhaseSubagent"</c> table
/// (REQ-FN-095, BRD-159, ADR-025).
/// </summary>
/// <remarks>
/// <see cref="ParentSessionId"/> is what the recursive sub-agent tree is walked over, which is why it
/// carries its own read index. A parent id naming a session no row reports is left unresolved and
/// counted as such rather than promoted to a root — the same rule <c>PlaybookAgentSplit</c> already
/// applies to the <c>parentID</c> chain.
/// </remarks>
public sealed record PbPhaseSubagentRecord
{
    /// <summary>AppManager user who owns the repository this row came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>The <see cref="PbPhaseExecutionRecord.PhaseExecutionId"/> this session belongs to.</summary>
    public required string PhaseExecutionId { get; init; }

    /// <summary>The sub-agent's own session id — part of the dedupe key.</summary>
    public required string SessionId { get; init; }

    /// <summary>The session that spawned it; the edge the tree is built from.</summary>
    public string? ParentSessionId { get; init; }

    /// <summary>The agent name the producer recorded.</summary>
    public string? Agent { get; init; }

    /// <summary>ISO-8601 start of the sub-agent's window.</summary>
    public string? StartedAt { get; init; }

    /// <summary>ISO-8601 end of the sub-agent's window; <c>null</c> when it did not close.</summary>
    public string? EndedAt { get; init; }

    /// <summary>Wall-clock duration in milliseconds.</summary>
    public long? ElapsedMs { get; init; }

    /// <summary>Whether the sub-agent's window closed.</summary>
    public bool? Complete { get; init; }

    /// <summary>Turns the sub-agent took.</summary>
    public int? Turns { get; init; }

    /// <summary>Input-side tokens for the sub-agent.</summary>
    public long? TokensIn { get; init; }

    /// <summary>Output-side tokens for the sub-agent.</summary>
    public long? TokensOut { get; init; }

    /// <summary>Provider cost in USD as fixed-precision decimal; never a binary float.</summary>
    public decimal? CostUsd { get; init; }

    /// <summary>Completeness of that cost; only <c>complete</c> may reach a headline figure.</summary>
    public string? CostStatus { get; init; }
}
