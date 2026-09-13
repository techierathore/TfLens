using System.Globalization;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of the reference's pooled block — the metrics both provenance separations exempt.
/// </summary>
/// <remarks>
/// These count events (runs, commits, tokens) rather than scoring requirements, so pooling them does
/// not manufacture a misleading rate (SCHEMA.md §6). The formulas and rounding are the reference's:
/// throughput to 2 dp in REQs per hour, tokens per verified REQ to 1 dp, commit cadence to 2 dp
/// (REQ-FN-053). <see cref="PooledMetrics.CostUsd"/> is a computed <c>null</c> on the contract and is
/// never assigned here (REQ-FN-051).
/// </remarks>
public static class Pooled
{
    private const string BuildPhaseCmd = "build-phase";
    private const string FixMode = "fix";
    private const string VerifiedVerdict = "Verified";
    private const string UnknownCmd = "?";
    private const int SecondsPerHour = 3600;

    /// <summary>Decimal places the reference rounds the pooled list price to.</summary>
    private const int ListUsdDigits = 4;

    /// <summary>
    /// Computes every poolable figure.
    /// </summary>
    /// <param name="aRuns">Every run record for the user and framework.</param>
    /// <param name="aSessions">Every session record for the user and framework.</param>
    /// <param name="aCommits">Commit records after <see cref="DedupeCommits.PerRepo"/>.</param>
    /// <param name="aDuplicatesCollapsed">How many duplicate commit records were collapsed.</param>
    /// <param name="aGates">Every gate record — the <c>Verified</c> transitions are the tokens-per-REQ denominator.</param>
    /// <param name="aSessionDuplicatesCollapsed">
    /// How many duplicate session records ingest collapsed, read from <c>"SyncState"</c>. It is a
    /// parameter rather than something computed here because sessions are deduped on the way into the
    /// store, not on the way out: <paramref name="aSessions"/> has already lost them (REQ-FN-063).
    /// </param>
    /// <returns>The pooled block.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static PooledMetrics Compute(
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyList<SessionRecord> aSessions,
        IReadOnlyList<CommitRecord> aCommits,
        int aDuplicatesCollapsed,
        IReadOnlyList<GateRecord> aGates,
        int aSessionDuplicatesCollapsed = 0,
        RateCard? aPrices = null)
    {
        ArgumentNullException.ThrowIfNull(aRuns);
        ArgumentNullException.ThrowIfNull(aSessions);
        ArgumentNullException.ThrowIfNull(aCommits);
        ArgumentNullException.ThrowIfNull(aGates);

        var vBuildRuns = aRuns.Where(aRun => aRun.Cmd == BuildPhaseCmd).ToList();
        var vFixRuns = aRuns.Where(aRun => aRun.Mode == FixMode).ToList();
        var vThroughput = aRuns
            // THE TIMESTAMPS WIN here too (BRD-179). Throughput divides by a duration, so reading the
            // stored field directly would divide by a figure the record's own clocks contradict — the
            // thirteen TechieBlog records storing a plausible round number are exactly that, and a
            // REQs-per-second built on one is wrong in a way no reader could see. `UsableSeconds` is the
            // one function every consumer goes through, and it returns null for an impossible record and
            // for one that recorded no elapsed time, so both drop out of the figure rather than skewing it.
            .Where(aRun => RunDuration.UsableSeconds(aRun) is not null && aRun.ReqsCount is not (null or 0))
            .Select(aRun => (double)aRun.ReqsCount!.Value / RunDuration.UsableSeconds(aRun)!.Value)
            .ToList();
        var vBatch = vBuildRuns
            .Where(aRun => aRun.ReqsCount is not null)
            .Select(aRun => (double)aRun.ReqsCount!.Value)
            .ToList();

        var vVerifiedTransitions = aGates.Count(aGate => aGate.Verdict == VerifiedVerdict);

        // Priced at read time from the tokens already on each record; nothing priced is ever stored, so
        // changing a rate re-prices every run ever recorded and no record was ever wrong.
        var vListPrices = aPrices is null
            ? []
            : aRuns.Select(aRun => ListPrice.For(aRun, aPrices)).Where(aP => aP is not null)
                .Select(aP => aP!.Value).ToList();
        var vTokens = aSessions.Sum(aSession => (long)(aSession.InputTokens ?? 0) + (aSession.OutputTokens ?? 0));
        var vActiveDays = ActiveDays(aCommits);

        return new PooledMetrics
        {
            RunsTotal = aRuns.Count,
            RunsByCmd = RunsByCmd(aRuns),
            ReworkRatio = Ratio(vFixRuns.Count, vBuildRuns.Count),
            ThroughputMedianReqsPerHour = ThroughputMedian(vThroughput),
            BatchSizeMedian = BatchMedian(vBatch),
            Sessions = aSessions.Count,
            TokensTotal = vTokens,
            TokensPerVerifiedReq = TokensPerVerified(vTokens, vVerifiedTransitions),

            // The list price over EVERY run, not only the build ones: it is a price on measured tokens,
            // so any run that carries tokens carries one. Rounded to four places, which is what the
            // reference publishes — a money figure quoted to six would imply a precision the rate card
            // does not have.
            ListUsdTotal = (decimal)ListPrice.Round(vListPrices.Sum(), ListUsdDigits),
            ListUsdRecords = vListPrices.Count,
            ListUsdPerVerifiedReq =
                vVerifiedTransitions >= MetricsConstants.MinN && vListPrices.Count >= MetricsConstants.MinN
                    ? (decimal)ListPrice.Round(vListPrices.Sum() / vVerifiedTransitions, ListUsdDigits)
                    : null,
            Commits = aCommits.Count,
            CommitDuplicatesCollapsed = aDuplicatesCollapsed,
            SessionDuplicatesCollapsed = aSessionDuplicatesCollapsed,
            ActiveDays = vActiveDays,
            CommitsPerActiveDay = Cadence(aCommits.Count, vActiveDays)
        };
    }

    /// <summary>
    /// Counts runs per command, in the reference's sorted key order.
    /// </summary>
    /// <param name="aRuns">The run records.</param>
    /// <returns>One entry per command, ordinally sorted; a run with no command counts under <c>?</c>.</returns>
    private static IReadOnlyList<KeyValuePair<string, int>> RunsByCmd(IEnumerable<RunRecord> aRuns)
    {
        var vCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var vRun in aRuns)
        {
            var vCmd = string.IsNullOrEmpty(vRun.Cmd) ? UnknownCmd : vRun.Cmd;
            vCounts[vCmd] = vCounts.GetValueOrDefault(vCmd) + 1;
        }

        return vCounts.ToList();
    }

    /// <summary>
    /// The rework ratio — fix-mode runs over build-phase runs.
    /// </summary>
    /// <param name="aFixRuns">Runs whose mode is <c>fix</c>.</param>
    /// <param name="aBuildRuns">Runs whose command is <c>build-phase</c> — the denominator.</param>
    /// <returns>The percentage, or <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> build-phase runs.</returns>
    private static Figure Ratio(int aFixRuns, int aBuildRuns) =>
        aBuildRuns < MetricsConstants.MinN
            // The reference names the records in its own refusal — "n=0 build-phase runs" — and BRD §13
            // diffs a refusal as a string, so the noun is part of the figure rather than decoration.
            ? Figure.InsufficientData(aBuildRuns, "build-phase runs")
            : Figure.Value(100.0 * aFixRuns / aBuildRuns, aBuildRuns, MetricsConstants.Pct(aFixRuns, aBuildRuns));

    /// <summary>
    /// The median REQ throughput, converted to REQs per hour and rounded to two decimal places.
    /// </summary>
    /// <param name="aThroughput">REQs per second for every run that carried both a duration and a count.</param>
    /// <returns>The rounded median, or <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> runs.</returns>
    private static Figure ThroughputMedian(IReadOnlyList<double> aThroughput)
    {
        if (aThroughput.Count < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aThroughput.Count);
        }

        var vPerHour = Math.Round(
            MetricsConstants.Median(aThroughput)!.Value * SecondsPerHour,
            2,
            MidpointRounding.ToEven);

        return Figure.Value(vPerHour, aThroughput.Count, vPerHour.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The median REQ count of a build-phase run — unrounded, as the reference leaves it.
    /// </summary>
    /// <param name="aBatch">REQ counts of the build-phase runs that carried one.</param>
    /// <returns>The median, or <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> runs.</returns>
    private static Figure BatchMedian(IReadOnlyList<double> aBatch)
    {
        if (aBatch.Count < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aBatch.Count);
        }

        var vMedian = MetricsConstants.Median(aBatch)!.Value;
        return Figure.Value(vMedian, aBatch.Count, vMedian.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Tokens per <c>Verified</c> verdict, to one decimal place.
    /// </summary>
    /// <param name="aTokens">Input plus output tokens across every session.</param>
    /// <param name="aVerifiedTransitions">Gate records carrying the <c>Verified</c> verdict — the denominator.</param>
    /// <returns>The rounded quotient, <see cref="FigureKind.NotApplicable"/> when no tokens were recorded, or <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> transitions.</returns>
    private static Figure TokensPerVerified(long aTokens, int aVerifiedTransitions)
    {
        if (aVerifiedTransitions < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aVerifiedTransitions);
        }

        if (aTokens == 0)
        {
            return Figure.NotApplicable();
        }

        var vPerReq = Math.Round((double)aTokens / aVerifiedTransitions, 1, MidpointRounding.ToEven);
        return Figure.Value(vPerReq, aVerifiedTransitions, vPerReq.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Distinct days carrying at least one commit, taken from the date part of the timestamp.
    /// </summary>
    /// <param name="aCommits">The deduped commit records.</param>
    /// <returns>How many distinct days appear.</returns>
    private static int ActiveDays(IEnumerable<CommitRecord> aCommits)
    {
        var vDays = new HashSet<string>(StringComparer.Ordinal);
        foreach (var vCommit in aCommits)
        {
            if (!string.IsNullOrEmpty(vCommit.Ts))
            {
                vDays.Add(vCommit.Ts.Length <= 10 ? vCommit.Ts : vCommit.Ts[..10]);
            }
        }

        return vDays.Count;
    }

    /// <summary>
    /// Commits per active day, to two decimal places.
    /// </summary>
    /// <param name="aCommits">Commits after dedupe.</param>
    /// <param name="aActiveDays">Distinct days carrying a commit — the denominator.</param>
    /// <returns>The rounded quotient, <see cref="FigureKind.NotApplicable"/> when no day carries a commit, or <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> commits.</returns>
    /// <remarks>
    /// The reference prints this figure from any non-zero number of days. TfLens applies the
    /// <see cref="MetricsConstants.MinN"/> floor here as everywhere else (REQ-FN-050): a cadence read
    /// off one or two commits is exactly the plausible wrong number the floor exists to refuse. The
    /// deviation is deliberate and only ever stricter than the reference.
    /// </remarks>
    private static Figure Cadence(int aCommits, int aActiveDays)
    {
        if (aActiveDays == 0)
        {
            return Figure.NotApplicable();
        }

        if (aCommits < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aCommits);
        }

        var vPerDay = Math.Round((double)aCommits / aActiveDays, 2, MidpointRounding.ToEven);
        return Figure.Value(vPerDay, aCommits, vPerDay.ToString(CultureInfo.InvariantCulture));
    }
}
