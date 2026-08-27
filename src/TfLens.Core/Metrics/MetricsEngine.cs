using Microsoft.Extensions.Logging;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The field-for-field port of <c>analyse()</c> in <c>.tfcore/telemetry/tf-metrics.sh</c>.
/// </summary>
/// <remarks>
/// The reference script is the specification (Architecture §7). The stages here are the reference's,
/// in its order: read the streams, build the per-repo facts, split live from backfilled, compute the
/// taint set, produce one figure block per (provenance, project type), then the pooled block. Nothing
/// is written back to any stream table — every figure is arithmetic done at request time (REQ-FN-046,
/// SCHEMA.md §8). There is no parameter, option or overload that merges two segments (REQ-NFR-009).
/// </remarks>
public sealed class MetricsEngine : IMetricsEngine
{
    private readonly ITelemetryStore objStore;
    private readonly ILogger<MetricsEngine> objLogger;

    /// <summary>
    /// Creates the engine over a telemetry store.
    /// </summary>
    /// <param name="aStore">The store every record is read through; reads are scoped by user and framework.</param>
    /// <param name="aLogger">Logger for counts only — never a record body (REQ-NFR-004).</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public MetricsEngine(ITelemetryStore aStore, ILogger<MetricsEngine> aLogger)
    {
        ArgumentNullException.ThrowIfNull(aStore);
        ArgumentNullException.ThrowIfNull(aLogger);

        objStore = aStore;
        objLogger = aLogger;
    }

    /// <inheritdoc />
    public async Task<AnalysisResult> AnalyseAsync(
        int aUserId,
        string aFramework,
        CancellationToken aCancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aFramework);

        // ---- stage 1: read the streams (the reference's read_stream loop over repos)
        var vGates = await objStore.ReadGatesAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vRuns = await objStore.ReadRunsAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vSessions = await objStore.ReadSessionsAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vRawCommits = await objStore.ReadCommitsAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vRepos = await objStore.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        var vEvents = await objStore.ReadPbEventsAsync(aUserId, null, aCancellationToken).ConfigureAwait(false);
        var vSyncStates = await objStore.ReadSyncStateAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        var (vCommits, vDuplicates) = DedupeCommits.PerRepo(vRawCommits);
        var vSessionDuplicates = SessionDuplicatesFor(vRepos, aFramework, vSyncStates);

        // ---- stage 2: per-repo facts
        var vPerRepo = PerRepoFactsFor(vRepos, aFramework, vGates, vRuns, vSessions, vCommits, vEvents);

        // ---- stage 3: split by provenance, then compute the taint set over the backfilled half
        var vLive = vGates.Where(aGate => aGate.Backfilled != true).ToList();
        var vBackfilled = vGates.Where(aGate => aGate.Backfilled == true).ToList();
        var vTainted = TaintSet.FromBackfilled(vGates);

        // ---- stage 4: figures per (provenance, project type) — never across either axis
        var vLiveFigures = SegmentsFor(vLive, vTainted, Provenance.Live);
        var vBackfilledFigures = SegmentsFor(vBackfilled, vTainted, Provenance.Backfilled);

        // ---- stage 5: the pooled block, which both separations exempt
        var vPooled = Pooled.Compute(vRuns, vSessions, vCommits, vDuplicates, vGates, vSessionDuplicates);

        objLogger.LogInformation(
            "Analysed user {UserId} framework {Framework}: {Gates} gates, {Runs} runs, {Sessions} sessions, {Commits} commits, {Tainted} tainted REQs",
            aUserId,
            aFramework,
            vGates.Count,
            vRuns.Count,
            vSessions.Count,
            vCommits.Count,
            vTainted.Count);

        return new AnalysisResult
        {
            UserId = aUserId,
            Framework = aFramework,
            PerRepo = vPerRepo,
            TaintedReqs = TaintSet.ForDisplay(vTainted),
            Live = vLiveFigures,
            Backfilled = vBackfilledFigures,
            Pooled = vPooled,
            ParserVersion = ParserVersion.Current
        };
    }

    /// <summary>
    /// Totals the session collapses ingest recorded, over this framework's repositories only.
    /// </summary>
    /// <remarks>
    /// Every other pooled figure is scoped to one framework because the store's reads are; this one is
    /// read from <c>"SyncState"</c>, which has no framework column, so the scoping is done here against
    /// <c>"UserRepo"</c> instead. A repository that has never been synced has no state row and
    /// contributes nothing, which is the right answer rather than a missing one (REQ-FN-063, ADR-016).
    /// </remarks>
    /// <param name="aRepos">The user's connected repositories.</param>
    /// <param name="aFramework">The provenance axis being analysed.</param>
    /// <param name="aStates">The user's sync bookkeeping, one row per synced repository.</param>
    /// <returns>Session records ingest collapsed across this framework's repositories.</returns>
    private static int SessionDuplicatesFor(
        IReadOnlyList<UserRepo> aRepos,
        string aFramework,
        IReadOnlyList<SyncState> aStates)
    {
        var vRepoNames = aRepos
            .Where(aRepo => string.Equals(aRepo.Framework, aFramework, StringComparison.Ordinal))
            .Select(aRepo => aRepo.Repo)
            .ToHashSet(StringComparer.Ordinal);

        return aStates
            .Where(aState => vRepoNames.Contains(aState.Repo))
            .Sum(aState => aState.SessionDuplicatesCollapsed);
    }

    /// <summary>
    /// Builds the per-repository fact lines the Coverage page and the export show.
    /// </summary>
    /// <remarks>
    /// <c>app</c> falls back to the repository's own name when no record carries one, because that is
    /// what the reference's <c>app_name()</c> does: it names the app from the single
    /// <c>docs/*-Checklist.md</c> when there is exactly one, and otherwise from the repository
    /// directory. A connected repository that has not yet produced a single telemetry record is the
    /// second case, so emitting <c>null</c> there disagreed with the reference on every such line
    /// (BRD §13 parity run, 2026-08-27).
    /// </remarks>
    /// <param name="aRepos">Every repository the user has connected.</param>
    /// <param name="aFramework">The framework being analysed; repositories on another axis are excluded.</param>
    /// <param name="aGates">Gate records read for this framework.</param>
    /// <param name="aRuns">Run records read for this framework.</param>
    /// <param name="aSessions">Session records read for this framework.</param>
    /// <param name="aCommits">Commit records after dedupe.</param>
    /// <param name="aEvents">Playbook event records for the user.</param>
    /// <returns>One line per repository on this framework, ordinally ordered by <c>owner/name</c>.</returns>
    private static IReadOnlyList<PerRepoFacts> PerRepoFactsFor(
        IReadOnlyList<UserRepo> aRepos,
        string aFramework,
        IReadOnlyList<GateRecord> aGates,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyList<SessionRecord> aSessions,
        IReadOnlyList<CommitRecord> aCommits,
        IReadOnlyList<PbEventRecord> aEvents)
    {
        var vFacts = new List<PerRepoFacts>();
        foreach (var vRepo in aRepos
                     .Where(aRepo => string.Equals(aRepo.Framework, aFramework, StringComparison.Ordinal))
                     .OrderBy(aRepo => aRepo.Repo, StringComparer.Ordinal))
        {
            var vGatesHere = aGates.Where(aGate => aGate.Repo == vRepo.Repo).ToList();
            var vRunsHere = aRuns.Where(aRun => aRun.Repo == vRepo.Repo).ToList();
            var vSessionsHere = aSessions.Where(aSession => aSession.Repo == vRepo.Repo).ToList();
            var vCommitsHere = aCommits.Where(aCommit => aCommit.Repo == vRepo.Repo).ToList();

            vFacts.Add(new PerRepoFacts(
                vRepo.Repo,
                vGatesHere.Select(aGate => aGate.App)
                    .Concat(vRunsHere.Select(aRun => aRun.App))
                    .Concat(vSessionsHere.Select(aSession => aSession.App))
                    .Concat(vCommitsHere.Select(aCommit => aCommit.App))
                    .FirstOrDefault(aApp => !string.IsNullOrEmpty(aApp))
                    ?? vRepo.Name,
                DeclaredProjectType(vGatesHere, vRunsHere, vSessionsHere, vCommitsHere),
                vRepo.Framework,
                vGatesHere.Count,
                vGatesHere.Count(aGate => aGate.Backfilled == true),
                vRunsHere.Count,
                vSessionsHere.Count,
                vCommitsHere.Count,
                aEvents.Count(aEvent => aEvent.Repo == vRepo.Repo)));
        }

        return vFacts;
    }

    /// <summary>
    /// The project type a repository declares, as the reference reads it from <c>core-config.yaml</c>.
    /// </summary>
    /// <param name="aGates">The repository's gate records.</param>
    /// <param name="aRuns">The repository's run records.</param>
    /// <param name="aSessions">The repository's session records.</param>
    /// <param name="aCommits">The repository's commit records.</param>
    /// <returns>The first declared type carried by any record, defaulting to <c>app</c> exactly as the reference does.</returns>
    /// <remarks>
    /// This is the <em>declared</em> type, which is what the reference's <c>per_repo</c> line reports —
    /// it is a coverage fact, not a segment key. Segmentation is <see cref="Segment.KeyFor"/>, and an
    /// inferred record still segments as <c>unclassified</c> there (REQ-FN-048).
    /// </remarks>
    private static string DeclaredProjectType(
        IEnumerable<GateRecord> aGates,
        IEnumerable<RunRecord> aRuns,
        IEnumerable<SessionRecord> aSessions,
        IEnumerable<CommitRecord> aCommits) =>
        aGates.Select(aGate => aGate.ProjectType)
            .Concat(aRuns.Select(aRun => aRun.ProjectType))
            .Concat(aSessions.Select(aSession => aSession.ProjectType))
            .Concat(aCommits.Select(aCommit => aCommit.ProjectType))
            .FirstOrDefault(aType => !string.IsNullOrEmpty(aType)) ?? "app";

    /// <summary>
    /// Computes one figure block per project type inside a single provenance bucket.
    /// </summary>
    /// <param name="aBucket">The bucket's gate records — either every live record or every backfilled one.</param>
    /// <param name="aTainted">REQs carrying a backfilled record.</param>
    /// <param name="aProvenance">Which bucket this is; the taint exclusion is a live-only rule.</param>
    /// <returns>The segments, ordinally keyed by project type.</returns>
    private static SortedDictionary<string, SegmentFigures> SegmentsFor(
        IReadOnlyList<GateRecord> aBucket,
        HashSet<string?> aTainted,
        Provenance aProvenance)
    {
        var vSegments = new SortedDictionary<string, SegmentFigures>(StringComparer.Ordinal);
        foreach (var vSegment in Segment.ByProjectType(aBucket))
        {
            vSegments[vSegment.Key] = FiguresFor(vSegment.Value, aTainted, aProvenance);
        }

        return vSegments;
    }

    /// <summary>
    /// The reference's per-segment figure block, field for field.
    /// </summary>
    /// <param name="aRecords">The segment's gate records.</param>
    /// <param name="aTainted">REQs carrying a backfilled record.</param>
    /// <param name="aProvenance">Which provenance bucket the segment belongs to.</param>
    /// <returns>The segment's figures.</returns>
    private static SegmentFigures FiguresFor(
        IReadOnlyList<GateRecord> aRecords,
        HashSet<string?> aTainted,
        Provenance aProvenance)
    {
        // REQ-FN-049: a REQ with any backfilled record leaves the live numerator AND denominator.
        var vEligible = aProvenance == Provenance.Backfilled
            ? aRecords
            : aRecords.Where(aRecord => !aTainted.Contains(aRecord.ReqId)).ToList();

        var vReqs = new HashSet<string?>(vEligible.Select(aRecord => aRecord.ReqId), StringComparer.Ordinal);
        var vFirstPass = new HashSet<string?>(
            vEligible
                .Where(aRecord => aRecord.Attempt == 1 && aRecord.Verdict == "Verified")
                .Select(aRecord => aRecord.ReqId),
            StringComparer.Ordinal);

        var vFailures = aRecords
            .Where(aRecord => !MetricsConstants.NonFailureVerdicts.Contains(aRecord.Verdict!))
            .ToList();
        var vCounts = GateDistribution.Count(vFailures);

        var vEscapedReqs = new HashSet<string?>(
            aRecords.Where(aRecord => aRecord.Gate == MetricsConstants.Escaped).Select(aRecord => aRecord.ReqId),
            StringComparer.Ordinal);
        var vFailedReqs = new HashSet<string?>(
            vFailures.Select(aRecord => aRecord.ReqId),
            StringComparer.Ordinal);

        var vExcluded = aProvenance == Provenance.Live
            ? new HashSet<string?>(
                aRecords.Where(aRecord => aTainted.Contains(aRecord.ReqId)).Select(aRecord => aRecord.ReqId),
                StringComparer.Ordinal).Count
            : 0;

        return new SegmentFigures
        {
            Records = aRecords.Count,
            ReqsScored = vReqs.Count,
            ReqsExcludedBackfillTaint = vExcluded,
            FirstPassN = vFirstPass.Count,
            FirstPassRate = Metrics.FirstPassRate.Compute(vFirstPass.Count, vReqs.Count),
            GateDistribution = Metrics.GateDistribution.Rows(vCounts, vFailures.Count),
            GateDistributionN = vFailures.Count,
            GateDistributionNote = Metrics.GateDistribution.Note(vFailures.Count),
            LateGateCoverage = LateGateCoverageCalculator.Compute(aRecords, vCounts),
            EscapeRate = Metrics.EscapeRate.Compute(vEscapedReqs.Count, vFailedReqs.Count)
        };
    }
}
