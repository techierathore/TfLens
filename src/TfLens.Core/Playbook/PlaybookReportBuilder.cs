using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// Computes the Playbook-native report set from the separate <c>"PbEvent"</c> table (REQ-FN-067, BRD-75).
/// </summary>
/// <remarks>
/// <para>
/// The builder reads <c>"PbEvent"</c> and nothing else. It never opens <c>"Gate"</c>, so no query it
/// issues can join a Playbook process-gate to a TechieFlow assertion-gate, and its result type keys
/// gates by <see cref="PhaseGateKey"/> rather than <see cref="string"/>, so no chart bound to it can be
/// fed by both axes (SCHEMA.md §11, REQ-FN-066).
/// </para>
/// <para>
/// Every rate it produces is a <see cref="Figure"/>, which structurally cannot carry a number below
/// <see cref="MetricsConstants.MinN"/> supporting records — the same minimum-n rule as the TechieFlow
/// engine, obtained from the same type rather than from a repeated check. Cost is <c>null</c> — rendered
/// <c>—</c> — whenever the events carry none, and is never coerced to zero.
/// </para>
/// </remarks>
public sealed class PlaybookReportBuilder : IPlaybookReportBuilder
{
    private readonly ITelemetryStore objStore;

    /// <summary>
    /// Creates the builder.
    /// </summary>
    /// <param name="aStore">The telemetry store; only its Playbook read is used.</param>
    public PlaybookReportBuilder(ITelemetryStore aStore) => objStore = aStore;

    /// <inheritdoc />
    public async Task<PlaybookAnalysis> BuildAsync(
        int aUserId,
        string? aRepo = null,
        CancellationToken aCancellationToken = default)
    {
        var vEvents = await objStore.ReadPbEventsAsync(aUserId, aRepo, aCancellationToken).ConfigureAwait(false);
        return Build(aUserId, vEvents);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A one-line delegation on purpose. <see cref="PlaybookPhaseEffort"/> is static because it is pure
    /// over the rows it is handed — three reads and a fold — and that is exactly the shape a unit test
    /// wants. What it is not is something a Razor page may call: the page would then hold the store, the
    /// repository scope and the harness hint, and would own a read path it has no business owning. This
    /// method is the seam, and it is thin because the work genuinely lives elsewhere.
    /// </remarks>
    public Task<PlaybookPhaseReport> BuildPhaseReportAsync(
        int aUserId,
        string? aRepo = null,
        CancellationToken aCancellationToken = default) =>
        PlaybookPhaseEffort.ReadAsync(objStore, aUserId, aRepo, null, aCancellationToken);

    /// <summary>
    /// Computes the report set from an in-memory event set.
    /// </summary>
    /// <param name="aUserId">The user the events belong to.</param>
    /// <param name="aEvents">The stored Playbook events.</param>
    /// <returns>The Playbook-native figures.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aEvents"/> is <c>null</c>.</exception>
    public static PlaybookAnalysis Build(int aUserId, IReadOnlyList<PbEventRecord> aEvents)
    {
        ArgumentNullException.ThrowIfNull(aEvents);

        var vTotals = BuildPhaseTotals(aEvents);

        return new PlaybookAnalysis
        {
            UserId = aUserId,
            SchemaStatus = PlaybookSchemaState.Status,
            PerRepo = BuildRepoFacts(aEvents),
            PhaseTotals = vTotals,
            PhaseQuestions = BuildQuestions(vTotals),
            AgentSplit = BuildAgentSplit(aEvents),
            TokensByModel = BuildTokensByModel(aEvents),
            ObservedFields = PlaybookWireFields.Names,
            ProvisionalNotes = PlaybookSchemaState.ProvisionalNotes,
            ParserVersion = ParserVersion.Current
        };
    }

    /// <summary>
    /// Groups the events per repository for the Playbook state of the Coverage page.
    /// </summary>
    /// <param name="aEvents">The stored events.</param>
    /// <returns>One line per repository, alphabetically.</returns>
    private static IReadOnlyList<PlaybookRepoFacts> BuildRepoFacts(IReadOnlyList<PbEventRecord> aEvents) =>
        aEvents
            .GroupBy(aE => aE.Repo, StringComparer.Ordinal)
            .OrderBy(aG => aG.Key, StringComparer.Ordinal)
            .Select(aG => new PlaybookRepoFacts(
                aG.Key,
                aG.Count(),
                aG.Select(aE => aE.SessionId).Where(aS => aS is not null).Distinct(StringComparer.Ordinal).Count(),
                aG.Select(aE => PhaseGateKey.From(aE.PhaseGate)).Distinct().Count(),
                aG.Min(aE => aE.Ts),
                aG.Max(aE => aE.Ts)))
            .ToList();

    /// <summary>
    /// Totals events, sessions, tokens and cost per Playbook process gate.
    /// </summary>
    /// <param name="aEvents">The stored events.</param>
    /// <returns>One row per observed <c>phase_gate</c>, busiest first.</returns>
    private static IReadOnlyList<PhaseGateTotals> BuildPhaseTotals(IReadOnlyList<PbEventRecord> aEvents) =>
        aEvents
            .GroupBy(aE => PhaseGateKey.From(aE.PhaseGate))
            .Select(aG => new PhaseGateTotals(
                aG.Key,
                aG.Count(),
                aG.Select(aE => aE.SessionId).Where(aS => aS is not null).Distinct(StringComparer.Ordinal).Count(),
                aG.Sum(aE => aE.TokensInTotal + aE.TokensOutTotal),
                SumCost(aG)))
            .OrderByDescending(aT => aT.Events)
            .ThenBy(aT => aT.PhaseGate.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Totals tokens per observed model, the Playbook equivalent of the routing view (BRD-75).
    /// </summary>
    /// <remarks>
    /// Only <c>turn</c> records carry a model, so <c>phase-start</c> and <c>phase-end</c> contribute
    /// nothing. Input and output are split the way the Playbook's own joiner splits them: cache reads and
    /// writes count as input, reasoning tokens count as output. The cache legs are also reported
    /// separately so the split stays inspectable.
    /// </remarks>
    /// <param name="aEvents">The stored events.</param>
    /// <returns>One row per model, heaviest first.</returns>
    private static IReadOnlyList<ModelTokens> BuildTokensByModel(IReadOnlyList<PbEventRecord> aEvents) =>
        aEvents
            .Where(aE => !string.IsNullOrWhiteSpace(aE.Model))
            .GroupBy(aE => aE.Model!, StringComparer.Ordinal)
            .Select(aG => new ModelTokens(
                aG.Key,
                aG.Sum(aE => (long)(aE.TokensInput ?? 0)),
                aG.Sum(aE => aE.TokensOutTotal),
                aG.Sum(aE => (long)(aE.TokensCacheRead ?? 0)),
                aG.Sum(aE => (long)(aE.TokensCacheWrite ?? 0))))
            .OrderByDescending(aM => aM.Total)
            .ThenBy(aM => aM.Model, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Builds the gate-outcomes rows, one per process gate.
    /// </summary>
    /// <remarks>
    /// While <see cref="PlaybookSchemaState.IsVerdictMapRecorded"/> is <c>false</c> every figure is
    /// <see cref="FigureKind.NotApplicable"/> and carries the reason: the Playbook verdict vocabulary has
    /// never been observed, and deriving it from the brief's prose is what ADR-010 forbids. The rows are
    /// still produced so the pages bind to a real shape rather than an empty state.
    /// </remarks>
    /// <param name="aTotals">The per-gate totals, which fix the row set and order.</param>
    /// <returns>One row per process gate.</returns>
    private static IReadOnlyList<PhaseGateQuestions> BuildQuestions(IReadOnlyList<PhaseGateTotals> aTotals) =>
        aTotals
            .Select(aT => new PhaseGateQuestions(
                aT.PhaseGate,
                Figure.NotApplicable(),
                Figure.NotApplicable(),
                Figure.NotApplicable(),
                aT.Events,
                PlaybookSchemaState.VerdictMapUnavailableReason))
            .ToList();

    /// <summary>
    /// Sums measured spend, keeping <c>null</c> when no event carried any.
    /// </summary>
    /// <param name="aEvents">The events to total.</param>
    /// <returns>The sum, or <c>null</c> so the page renders <c>—</c> rather than a manufactured zero.</returns>
    private static decimal? SumCost(IEnumerable<PbEventRecord> aEvents)
    {
        var vCosts = aEvents.Select(aE => aE.CostUsd).Where(aC => aC.HasValue).ToList();
        return vCosts.Count == 0 ? null : vCosts.Sum(aC => aC!.Value);
    }

    /// <summary>
    /// Splits sessions into main and sub-agent by resolving the <c>parentID</c> chain to its root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A session's parent is the first non-blank parent id any of its events carries, and the chain is
    /// walked to a session that has no parent, so a sub-agent of a sub-agent still resolves to the main
    /// session that started the work. A chain that leaves the known session set, or that closes on
    /// itself, is counted in <see cref="PlaybookAgentSplit.UnresolvedParentSessions"/> rather than being
    /// promoted to a main session.
    /// </para>
    /// <para>
    /// The Playbook's emitter leaves <c>parentID</c> <c>null</c> when it could not learn the parent — for
    /// a session that predates the plugin, for instance — so its own joiner uses a fallback: a turn is a
    /// child turn when its <c>parentID</c> is set <b>or</b> its session differs from the phase's own
    /// session. That fallback is mirrored here through the <c>phase-start</c> records, which are the only
    /// records that name a main session: a parentless session that no <c>phase-start</c> ever names is
    /// classified as a sub-agent, not as a second main session.
    /// </para>
    /// </remarks>
    /// <param name="aEvents">The stored events.</param>
    /// <returns>The split.</returns>
    private static PlaybookAgentSplit BuildAgentSplit(IReadOnlyList<PbEventRecord> aEvents)
    {
        var vPhaseSessions = aEvents
            .Where(aE => string.Equals(aE.Kind, PlaybookEventKinds.PhaseStart, StringComparison.Ordinal))
            .Select(aE => aE.SessionId)
            .Where(aS => !string.IsNullOrWhiteSpace(aS))
            .Select(aS => aS!)
            .ToHashSet(StringComparer.Ordinal);

        var vSessions = aEvents
            .Where(aE => !string.IsNullOrWhiteSpace(aE.SessionId))
            .GroupBy(aE => aE.SessionId!, StringComparer.Ordinal)
            .ToDictionary(
                aG => aG.Key,
                aG => new SessionTally(
                    aG.Select(aE => aE.ParentId).FirstOrDefault(aP => !string.IsNullOrWhiteSpace(aP)),
                    aG.Sum(aE => aE.TokensInTotal + aE.TokensOutTotal),
                    SumCost(aG)),
                StringComparer.Ordinal);

        var vMainSessions = 0;
        var vMainTokens = 0L;
        var vSubSessions = 0;
        var vSubTokens = 0L;
        var vUnresolved = 0;
        var vMainCosts = new List<decimal>();
        var vSubCosts = new List<decimal>();

        foreach (var vPair in vSessions)
        {
            var vTally = vPair.Value;

            if (IsMainSession(vPair.Key, vTally, vPhaseSessions))
            {
                vMainSessions++;
                vMainTokens += vTally.Tokens;
                AddCost(vMainCosts, vTally.CostUsd);
                continue;
            }

            vSubSessions++;
            vSubTokens += vTally.Tokens;
            AddCost(vSubCosts, vTally.CostUsd);

            if (vTally.ParentId is null || !ResolvesToRoot(vPair.Key, vSessions))
            {
                vUnresolved++;
            }
        }

        return new PlaybookAgentSplit(
            vMainSessions,
            vMainTokens,
            vMainCosts.Count == 0 ? null : vMainCosts.Sum(),
            vSubSessions,
            vSubTokens,
            vSubCosts.Count == 0 ? null : vSubCosts.Sum(),
            vUnresolved);
    }

    /// <summary>
    /// Decides whether one session is a main session.
    /// </summary>
    /// <param name="aSessionId">The session id.</param>
    /// <param name="aTally">Its parent, tokens and spend.</param>
    /// <param name="aPhaseSessions">Sessions named by a <c>phase-start</c> record.</param>
    /// <returns><c>true</c> when the session has no parent and is not contradicted by the phase records.</returns>
    private static bool IsMainSession(
        string aSessionId,
        SessionTally aTally,
        HashSet<string> aPhaseSessions)
    {
        if (aTally.ParentId is not null)
        {
            return false;
        }

        // With no phase-start records to check against, parentID is all there is to go on.
        return aPhaseSessions.Count == 0 || aPhaseSessions.Contains(aSessionId);
    }

    /// <summary>
    /// Adds a measured cost to a running list, ignoring the absent case.
    /// </summary>
    /// <param name="aCosts">The running list.</param>
    /// <param name="aCost">The cost, which may be absent.</param>
    private static void AddCost(List<decimal> aCosts, decimal? aCost)
    {
        if (aCost.HasValue)
        {
            aCosts.Add(aCost.Value);
        }
    }

    /// <summary>
    /// Walks a session's parent chain and reports whether it ends at a known parentless session.
    /// </summary>
    /// <param name="aSessionId">The session to start from.</param>
    /// <param name="aSessions">Every known session and its parent.</param>
    /// <returns><c>true</c> when the chain reaches a main session; <c>false</c> on an unknown parent or a cycle.</returns>
    private static bool ResolvesToRoot(string aSessionId, IReadOnlyDictionary<string, SessionTally> aSessions)
    {
        var vSeen = new HashSet<string>(StringComparer.Ordinal) { aSessionId };
        var vCurrent = aSessions[aSessionId].ParentId;

        while (vCurrent is not null)
        {
            if (!vSeen.Add(vCurrent) || !aSessions.TryGetValue(vCurrent, out var vParent))
            {
                return false;
            }

            vCurrent = vParent.ParentId;
        }

        return true;
    }

    /// <summary>One session's parent, tokens and measured spend.</summary>
    /// <param name="ParentId">The parent session id, or <c>null</c> for a main session.</param>
    /// <param name="Tokens">Tokens summed over the session's events.</param>
    /// <param name="CostUsd">Measured spend, or <c>null</c> when the events carry none.</param>
    private sealed record SessionTally(string? ParentId, long Tokens, decimal? CostUsd);
}
