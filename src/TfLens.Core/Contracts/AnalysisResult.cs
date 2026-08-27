namespace TfLens.Core.Contracts;

/// <summary>
/// The whole output of the metrics engine for one user and one framework.
/// </summary>
/// <remarks>
/// ADR-007 — this type has no member that could hold a cross-<c>project_type</c> or cross-provenance
/// rate. <see cref="Live"/> and <see cref="Backfilled"/> are keyed by project type and never merged;
/// <see cref="Pooled"/> holds only the metrics the reference explicitly exempts from both separations
/// (run counts, cadence, tokens), and its <see cref="PooledMetrics.CostUsd"/> is always <c>null</c>.
/// The key layout mirrors <c>tf-metrics.sh --rollup --json</c> so the parity compare reads key-for-key.
/// </remarks>
public sealed record AnalysisResult
{
    /// <summary>The user the figures belong to; every read is scoped by it (ADR-013).</summary>
    public required int UserId { get; init; }

    /// <summary>The framework axis the figures belong to — never pooled across (ADR-016).</summary>
    public required string Framework { get; init; }

    /// <summary>One line per connected repository the figures were computed from.</summary>
    public required IReadOnlyList<PerRepoFacts> PerRepo { get; init; }

    /// <summary>
    /// REQ IDs with any backfilled gate record, excluded from the live first-pass rate.
    /// </summary>
    /// <remarks>
    /// Their live <c>attempt</c> numbering restarts at 1 (SCHEMA.md §3.1), so a live first-pass rate
    /// that included them would be flattering and wrong. The list is shown on screen rather than
    /// silently applied.
    /// </remarks>
    public required IReadOnlyList<string> TaintedReqs { get; init; }

    /// <summary>Live figures, keyed by project type. Never merged with <see cref="Backfilled"/>.</summary>
    public required IReadOnlyDictionary<string, SegmentFigures> Live { get; init; }

    /// <summary>Backfilled figures, keyed by project type. Never merged with <see cref="Live"/>.</summary>
    public required IReadOnlyDictionary<string, SegmentFigures> Backfilled { get; init; }

    /// <summary>The metrics the reference exempts from both separations.</summary>
    public required PooledMetrics Pooled { get; init; }

    /// <summary>Parser version stamped into every export, so a figure can be traced to the code that made it.</summary>
    public required string ParserVersion { get; init; }

    /// <summary>
    /// Every project type present across <see cref="Live"/> and <see cref="Backfilled"/>, sorted.
    /// </summary>
    /// <remarks>The Three-questions page renders one tab per entry, and deliberately has no "all" tab.</remarks>
    public IReadOnlyList<string> ProjectTypes =>
        Live.Keys.Concat(Backfilled.Keys).Distinct().OrderBy(aK => aK, StringComparer.Ordinal).ToList();
}

/// <summary>Per-repository facts shown on the Coverage page and carried into the export.</summary>
/// <param name="Repo"><c>owner/name</c> of the repository.</param>
/// <param name="App">Application name the records carry.</param>
/// <param name="ProjectType">The repository's declared project type.</param>
/// <param name="Framework">The provenance axis the repository belongs to.</param>
/// <param name="Gates">Gate records stored.</param>
/// <param name="GatesBackfilled">How many of those are backfilled.</param>
/// <param name="Runs">Run records stored.</param>
/// <param name="Sessions">Session records stored, after dedupe.</param>
/// <param name="Commits">Commit records stored, after dedupe.</param>
/// <param name="Events">Playbook event records stored.</param>
public sealed record PerRepoFacts(
    string Repo,
    string? App,
    string? ProjectType,
    string Framework,
    int Gates,
    int GatesBackfilled,
    int Runs,
    int Sessions,
    int Commits,
    int Events);

/// <summary>
/// The figures for one (provenance, project type) segment — the only shape a rate is ever computed in.
/// </summary>
public sealed record SegmentFigures
{
    /// <summary>Gate records in this segment.</summary>
    public required int Records { get; init; }

    /// <summary>Distinct REQ IDs eligible to be scored in this segment.</summary>
    public required int ReqsScored { get; init; }

    /// <summary>REQs dropped from the live segment because they carry a backfilled record; zero on backfilled segments.</summary>
    public required int ReqsExcludedBackfillTaint { get; init; }

    /// <summary>Distinct REQs that passed on their first attempt.</summary>
    public required int FirstPassN { get; init; }

    /// <summary>First-pass rate — first-attempt <c>Verified</c> REQs over eligible REQs.</summary>
    public required Figure FirstPassRate { get; init; }

    /// <summary>Failure counts per gate, in the reference's gate order, omitting gates with no failures.</summary>
    public required IReadOnlyList<GateCount> GateDistribution { get; init; }

    /// <summary>Failure records the distribution is computed over — the honest denominator for its shares.</summary>
    public required int GateDistributionN { get; init; }

    /// <summary><c>insufficient data (n=…)</c> when there are too few failures to read shares off; otherwise <c>null</c>.</summary>
    public required string? GateDistributionNote { get; init; }

    /// <summary>For each late-added gate, how many records ran it beside how many it caught.</summary>
    public required IReadOnlyList<LateGateCoverage> LateGateCoverage { get; init; }

    /// <summary>Escape rate — REQs no gate caught, over REQs with any failure.</summary>
    public required Figure EscapeRate { get; init; }
}

/// <summary>One row of the gate catch distribution.</summary>
/// <param name="Gate">Gate name, or <c>unattributed</c> when the failure named none.</param>
/// <param name="Count">Failures attributed to it.</param>
/// <param name="Share">Its share of <see cref="SegmentFigures.GateDistributionN"/>, as the reference prints it.</param>
public sealed record GateCount(string Gate, int Count, string Share);

/// <summary>Coverage of a gate that entered the enum after collection started.</summary>
/// <param name="Gate">The gate name.</param>
/// <param name="Ran">Records whose <c>gates_run</c> includes it — the honest denominator.</param>
/// <param name="Caught">Failures it caught.</param>
/// <param name="Since">The date the gate was added.</param>
/// <param name="CatchRate">Caught over ran, or <c>insufficient data</c> when too few records ran it.</param>
public sealed record LateGateCoverage(string Gate, int Ran, int Caught, string Since, Figure CatchRate);

/// <summary>
/// The metrics the reference exempts from the provenance separations.
/// </summary>
/// <remarks>
/// These count events (runs, commits, tokens) rather than scoring requirements, so pooling them does
/// not manufacture a misleading rate. <see cref="CostUsd"/> is always <c>null</c>: estimated dollars
/// are never presented as a measurement (SCHEMA.md §4, BRD-35).
/// </remarks>
public sealed record PooledMetrics
{
    /// <summary>All run records.</summary>
    public required int RunsTotal { get; init; }

    /// <summary>Run counts per command, sorted by command name.</summary>
    public required IReadOnlyList<KeyValuePair<string, int>> RunsByCmd { get; init; }

    /// <summary>Fix-mode runs over build-phase runs — how much of the work is rework.</summary>
    public required Figure ReworkRatio { get; init; }

    /// <summary>Median REQs per hour across runs that carry both a duration and a REQ count.</summary>
    public required Figure ThroughputMedianReqsPerHour { get; init; }

    /// <summary>Median REQ count of a build-phase run.</summary>
    public required Figure BatchSizeMedian { get; init; }

    /// <summary>Session records after dedupe.</summary>
    public required int Sessions { get; init; }

    /// <summary>Input plus output tokens across every session.</summary>
    public required long TokensTotal { get; init; }

    /// <summary>Tokens per <c>Verified</c> verdict, to one decimal place.</summary>
    public required Figure TokensPerVerifiedReq { get; init; }

    /// <summary>
    /// Always <c>null</c>. Measured dollars exist only per-harness for OpenCode and are never totalled.
    /// </summary>
    /// <remarks>
    /// The property exists so the export's key layout matches <c>--rollup --json</c> exactly; there is
    /// no code path that assigns it a value.
    /// </remarks>
    public decimal? CostUsd => null;

    /// <summary>Commit records after dedupe.</summary>
    public required int Commits { get; init; }

    /// <summary>Duplicate commit records collapsed on <c>sha</c> — expected, not corruption.</summary>
    public required int CommitDuplicatesCollapsed { get; init; }

    /// <summary>
    /// Duplicate session records collapsed on <c>session_id</c> — expected, not corruption.
    /// </summary>
    /// <remarks>
    /// The sibling above is computed here, at read time, because commits are deduped here. Sessions are
    /// not: the store's <c>UcSessionUserRepoId</c> index collapses them on the way in, so by the time
    /// this block is computed the duplicates no longer exist and a read-time count would always be zero.
    /// The figure is therefore carried in from <c>"SyncState"</c>, where ingest recorded it
    /// (REQ-FN-063), and summed over the repositories on this framework only — the same scoping every
    /// other pooled figure uses.
    /// </remarks>
    public required int SessionDuplicatesCollapsed { get; init; }

    /// <summary>Distinct days that carry at least one commit.</summary>
    public required int ActiveDays { get; init; }

    /// <summary>Commits per active day, to two decimal places.</summary>
    public required Figure CommitsPerActiveDay { get; init; }
}
