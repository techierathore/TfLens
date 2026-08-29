using System.Globalization;
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
        var vMisses = await objStore.ReadMissesAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vMissFixes = await objStore.ReadMissFixesAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vMissAmends = await objStore.ReadMissAmendsAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
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

        // ---- stage 6: the miss block — live-only, segmented per project type, amendments folded at read
        // time. It is computed beside the gate figures and never inside them: the miss escape share is a
        // second, adjacent figure and the gates-derived escape rate above is untouched (REQ-FN-077).
        var vMissFigures = MissFigures.Compute(vMisses, vMissFixes, vMissAmends, vRuns);

        objLogger.LogInformation(
            "Analysed user {UserId} framework {Framework}: {Gates} gates, {Runs} runs, {Sessions} sessions, {Commits} commits, {Misses} misses, {MissFixes} miss fixes, {Tainted} tainted REQs, {AttributionExcluded} misses outside the per-origin figures",
            aUserId,
            aFramework,
            vGates.Count,
            vRuns.Count,
            vSessions.Count,
            vCommits.Count,
            vMissFigures.MissesTotal,
            vMissFigures.MissFixesTotal,
            vTainted.Count,
            vMissFigures.Live.Values.Sum(aSegment => aSegment.Attribution.AttributionExcluded));

        return new AnalysisResult
        {
            UserId = aUserId,
            Framework = aFramework,
            PerRepo = vPerRepo,
            TaintedReqs = TaintSet.ForDisplay(vTainted),
            Live = vLiveFigures,
            Backfilled = vBackfilledFigures,
            Pooled = vPooled,
            Misses = vMissFigures,
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
    /// The project type a repository <em>currently</em> declares — the newest declaration its records carry.
    /// </summary>
    /// <param name="aGates">The repository's gate records.</param>
    /// <param name="aRuns">The repository's run records.</param>
    /// <param name="aSessions">The repository's session records.</param>
    /// <param name="aCommits">The repository's commit records.</param>
    /// <returns>
    /// The declared type on the newest record carrying one, across all four streams, defaulting to
    /// <c>app</c> when no record declares one — the same default the reference falls back to.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Why newest, not first.</b> The reference reads the repository's <c>core-config.yaml</c>, which
    /// holds one value: what the project declares itself to be <i>today</i>. TfLens has no config file to
    /// read, only the streams, and each record froze the declaration in force when it was written. A
    /// reclassified repository therefore carries both the old and the new value, and the record that
    /// answers the reference's question is the most recent one — not the first the store happens to hand
    /// back, and not the most numerous. TfLens itself is the live case: it was reclassified from
    /// <c>docs</c> to <c>app</c>, and <c>docs</c> still outnumbers <c>app</c> in the gate stream while
    /// every stream's newest record reads <c>app</c>.
    /// </para>
    /// <para>
    /// <b>Ordering and ties.</b> Records are ranked by their <c>Ts</c> instant, newest first. A record
    /// whose timestamp is missing or unparseable never outranks one that has a usable instant; it is
    /// considered only when nothing parseable declares a type. Records sharing an instant — and records
    /// with no usable instant among themselves — are broken by ordinal comparison of the declared value
    /// itself, lowest first. That tie-break deliberately depends on nothing but the values in hand, so
    /// the answer is identical on every run regardless of the order the store returns rows in.
    /// </para>
    /// <para>
    /// This is the <em>declared</em> type, which is what the reference's <c>per_repo</c> line reports —
    /// it is a coverage fact, not a segment key. Segmentation is <see cref="Segment.KeyFor"/>, and an
    /// inferred record still segments as <c>unclassified</c> there (REQ-FN-048).
    /// </para>
    /// </remarks>
    private static string DeclaredProjectType(
        IEnumerable<GateRecord> aGates,
        IEnumerable<RunRecord> aRuns,
        IEnumerable<SessionRecord> aSessions,
        IEnumerable<CommitRecord> aCommits)
    {
        var vDeclarations = aGates.Select(aGate => DeclarationOf(aGate.Ts, aGate.ProjectType))
            .Concat(aRuns.Select(aRun => DeclarationOf(aRun.Ts, aRun.ProjectType)))
            .Concat(aSessions.Select(aSession => DeclarationOf(aSession.Ts, aSession.ProjectType)))
            .Concat(aCommits.Select(aCommit => DeclarationOf(aCommit.Ts, aCommit.ProjectType)))
            .Where(aDeclaration => aDeclaration is not null)
            .Select(aDeclaration => aDeclaration!.Value);

        var vNewest = vDeclarations
            .OrderByDescending(aDeclaration => aDeclaration.Instant is not null)
            .ThenByDescending(aDeclaration => aDeclaration.Instant ?? DateTimeOffset.MinValue)
            .ThenBy(aDeclaration => aDeclaration.Type, StringComparer.Ordinal)
            .Select(aDeclaration => aDeclaration.Type)
            .FirstOrDefault();

        return vNewest ?? "app";
    }

    /// <summary>
    /// Pairs one record's declared project type with the instant it was declared at.
    /// </summary>
    /// <param name="aTs">The record's ISO-8601 timestamp; may be blank or unparseable.</param>
    /// <param name="aProjectType">The record's declared project type; may be absent.</param>
    /// <returns>
    /// The declaration, or <c>null</c> when the record declares no type. <c>Instant</c> is <c>null</c>
    /// when the timestamp cannot be read, which ranks the declaration below every dated one.
    /// </returns>
    private static (DateTimeOffset? Instant, string Type)? DeclarationOf(string? aTs, string? aProjectType)
    {
        if (string.IsNullOrEmpty(aProjectType))
        {
            return null;
        }

        var vInstant = DateTimeOffset.TryParse(
            aTs,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var vMoment)
            ? vMoment
            : (DateTimeOffset?)null;

        return (vInstant, aProjectType);
    }

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
