using System.Globalization;
using Microsoft.Extensions.Options;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;

namespace TfLens.Core.Metrics;

/// <summary>
/// The metrics <c>tf-metrics.sh</c> does not compute — and which therefore have no parity oracle.
/// </summary>
/// <remarks>
/// Three rules shape this class and none of them is optional.
/// <para>
/// <b>ADR-017 — the harness columns are fixed.</b> <see cref="HarnessOrder"/> is the detected vocabulary
/// <c>claude-code</c> / <c>opencode</c> / <c>codex</c>, always in that order, and a harness with no
/// records still yields a column (it renders as em dashes rather than vanishing). Records whose
/// <c>harness</c> is <c>null</c> are counted into <see cref="HarnessComparison.NotDetectedRecords"/> —
/// a footnote, never a fourth column, and never dropped: SCHEMA.md §1 says a missing label is merely
/// missing, and a wrong one corrupts every comparison built on it.
/// </para>
/// <para>
/// <b>BRD-54 — there is no cross-harness dollar total.</b> Measured <c>cost_usd</c> exists only on
/// OpenCode <b>session</b> records (SCHEMA.md §4 — the OpenCode plugin is the one emitter that can
/// measure a real price, and it writes it onto the session stream, never onto a run), so
/// <see cref="HarnessComparison.OpenCodeCostUsd"/> is the only money this class reports as measured.
/// There is no member here, and none on the contracts, that could hold a total across harnesses.
/// Tokens may be compared across harness; dollars may not (SCHEMA.md §2.5).
/// </para>
/// <para>
/// <b>ADR-009 / SCHEMA.md §4 — repricing is an estimate.</b> Every money figure on
/// <see cref="RoutingAnalysis"/> other than the OpenCode measurement is tokens × <see cref="RateCard"/>,
/// carries <see cref="RateCard.EstimateLabel"/> wherever it is rendered, and is exported under a key
/// ending <c>_usd_estimate</c>. Runs the framework could not scope tokens for are excluded and counted
/// rather than assumed zero (BRD-60), and an observed model the card does not price is named in
/// <see cref="RoutingAnalysis.MissingPriceModels"/> rather than quietly priced at nothing.
/// </para>
/// </remarks>
public sealed class ExtraMetrics : IExtraMetrics
{
    /// <summary>
    /// The harness columns, in the order every rendering uses them (ADR-017).
    /// </summary>
    /// <remarks>
    /// This is the detected vocabulary of SCHEMA.md §1, not a display preference: adding a value here
    /// without <c>tf-emit.sh</c> detecting it would produce an always-empty column, and removing one
    /// would silently hide a harness's records.
    /// </remarks>
    public static readonly IReadOnlyList<string> HarnessOrder = ["claude-code", "opencode", "codex"];

    /// <summary>The <c>tokens_scope</c> value that means the token window could not be computed.</summary>
    private const string TokensScopeNone = "none";

    /// <summary>The one harness that can measure a real price (SCHEMA.md §4).</summary>
    private const string OpenCodeHarness = "opencode";

    /// <summary>The bucket a run with no <c>cmd</c> falls into, matching the reference's <c>?</c>.</summary>
    private const string UnknownBucket = "?";

    /// <summary>How many commands the harness column lists.</summary>
    private const int TopCommands = 3;

    private readonly ITelemetryStore objStore;
    private readonly TfLensOptions objOptions;

    /// <summary>
    /// Creates the extra-metrics service.
    /// </summary>
    /// <param name="aStore">The telemetry store; every read is scoped by user id (ADR-013).</param>
    /// <param name="aOptions">Configuration, for <see cref="TfLensOptions.PricesPath"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency was not supplied.</exception>
    public ExtraMetrics(ITelemetryStore aStore, IOptions<TfLensOptions> aOptions)
    {
        ArgumentNullException.ThrowIfNull(aStore);
        ArgumentNullException.ThrowIfNull(aOptions);

        objStore = aStore;
        objOptions = aOptions.Value;
    }

    /// <summary>
    /// Computes the per-harness comparison for one user and framework.
    /// </summary>
    /// <remarks>
    /// Every harness in <see cref="HarnessOrder"/> gets a column whether or not it has records, so a
    /// harness that has stopped emitting is visibly empty rather than absent. Undetected records are
    /// counted across all three streams the columns cover — runs, gates and sessions — and reported as
    /// the footnote.
    /// </remarks>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis; figures never pool across frameworks (ADR-016).</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>Three columns, the not-detected footnote count, and the OpenCode measured dollars.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task<HarnessComparison> CompareHarnessesAsync(
        int aUserId,
        string aFramework,
        CancellationToken aCancellationToken = default)
    {
        var vRuns = await objStore.ReadRunsAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vGates = await objStore.ReadGatesAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vSessions = await objStore.ReadSessionsAsync(aUserId, aFramework, null, aCancellationToken)
            .ConfigureAwait(false);

        var vColumns = HarnessOrder.Select(aH => BuildColumn(aH, vRuns, vGates, vSessions)).ToList();

        var vNotDetected =
            vRuns.Count(aR => aR.Harness is null)
            + vGates.Count(aG => aG.Harness is null)
            + vSessions.Count(aS => aS.Harness is null);

        var (vCost, vCostSessions) = MeasuredOpenCodeCost(vSessions);

        return new HarnessComparison(vColumns, vNotDetected, vCost, vCostSessions);
    }

    /// <summary>
    /// Computes routing drift, tokens by observed model and the counterfactual repricing.
    /// </summary>
    /// <remarks>
    /// Routing is observed, never enforced (SCHEMA.md §2.5) — <c>routed: false</c> is drift made
    /// visible, not an error, so the drift rows list unrouted runs first and then everything else that
    /// carries routing fields. The repricing is an estimate in the strong sense: it prices the same
    /// token base twice, once at each run's observed model and once at the single most expensive
    /// observed model, so the two figures are comparable. Runs with <c>tokens_scope: none</c> or no
    /// token fields at all are excluded from both and counted, and models the rate card does not price
    /// are excluded from both and named.
    /// </remarks>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The routing view; every money figure on it is an estimate and says so.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task<RoutingAnalysis> AnalyseRoutingAsync(
        int aUserId,
        string aFramework,
        CancellationToken aCancellationToken = default)
    {
        var vRuns = await objStore.ReadRunsAsync(aUserId, aFramework, null, aCancellationToken).ConfigureAwait(false);
        var vCard = await RateCard.LoadAsync(objOptions.PricesPath, aCancellationToken).ConfigureAwait(false);

        var vRouting = vRuns.Where(HasRoutingFields).ToList();
        var vDrift = vRouting
            .OrderBy(aR => aR.Routed == false ? 0 : 1)
            .ThenByDescending(aR => aR.Ts, StringComparer.Ordinal)
            .Select(aR => new DriftRow(aR.Ts, aR.Cmd, aR.Tier, aR.TierModel, aR.Model, aR.Models, aR.Routed))
            .ToList();

        var vTokensByModel = TokensByModel(vRuns);
        var vRepricing = Reprice(vRuns, vCard);

        return new RoutingAnalysis(
            vRouting.Count,
            vRouting.Count(aR => aR.Routed == false),
            vTokensByModel.Count,
            vDrift,
            vTokensByModel,
            vRepricing.ActualMixUsd,
            vRepricing.AllAtMaxUsd,
            vRepricing.MostExpensiveModel,
            vRepricing.ExcludedRuns,
            vTokensByModel.Select(aM => aM.Model).Where(aM => vCard.Find(aM) is null)
                .OrderBy(aM => aM, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Builds one harness's column, including for a harness with no records at all.
    /// </summary>
    /// <param name="aHarness">The harness name from <see cref="HarnessOrder"/>.</param>
    /// <param name="aRuns">Every run record for the user and framework.</param>
    /// <param name="aGates">Every gate record for the user and framework.</param>
    /// <param name="aSessions">Every session record for the user and framework.</param>
    /// <returns>The column; a harness with no records yields zeros and a not-applicable figure.</returns>
    private static HarnessColumn BuildColumn(
        string aHarness,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyList<GateRecord> aGates,
        IReadOnlyList<SessionRecord> aSessions)
    {
        var vRuns = aRuns.Where(aR => string.Equals(aR.Harness, aHarness, StringComparison.Ordinal)).ToList();
        var vGates = aGates.Where(aG => string.Equals(aG.Harness, aHarness, StringComparison.Ordinal)).ToList();
        var vSessions = aSessions.Count(aS => string.Equals(aS.Harness, aHarness, StringComparison.Ordinal));

        var vTokensIn = vRuns.Sum(aR => (long)(aR.TokensIn ?? 0));
        var vTokensOut = vRuns.Sum(aR => (long)(aR.TokensOut ?? 0));

        return new HarnessColumn(
            aHarness,
            vRuns.Count,
            TopByCount(vRuns.Select(aR => aR.Cmd), TopCommands),
            vGates.Count,
            TopByCount(vGates.Select(aG => aG.Verdict), int.MaxValue),
            vSessions,
            vTokensIn,
            vTokensOut,
            vRuns.Sum(aR => (long)(aR.TokensCacheRead ?? 0)),
            vRuns.Sum(aR => (long)(aR.TokensCacheWrite ?? 0)),
            TokensPerVerified(vTokensIn + vTokensOut, vGates));
    }

    /// <summary>
    /// Tokens per <c>Verified</c> verdict for one harness, mirroring the reference's pooled formula.
    /// </summary>
    /// <remarks>
    /// The numerator is input + output tokens — the same two counts <c>tf-metrics.sh</c> totals for
    /// <c>tokens_per_verified_req</c> — taken from the harness's own runs; the denominator is its
    /// <c>Verified</c> gate records. Below <see cref="MetricsConstants.MinN"/> verdicts the figure
    /// refuses to be a number; with no tokens captured it is not applicable, because zero would read as
    /// a measurement of nothing rather than an absence of measurement.
    /// </remarks>
    /// <param name="aTokens">Input plus output tokens for the harness.</param>
    /// <param name="aGates">The harness's gate records.</param>
    /// <returns>The figure, to one decimal place as the reference prints it.</returns>
    private static Figure TokensPerVerified(long aTokens, IReadOnlyList<GateRecord> aGates)
    {
        var vVerified = aGates.Count(aG => string.Equals(aG.Verdict, "Verified", StringComparison.Ordinal));

        if (vVerified < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(vVerified);
        }

        if (aTokens <= 0)
        {
            return Figure.NotApplicable();
        }

        var vValue = Math.Round((double)aTokens / vVerified, 1, MidpointRounding.ToEven);
        return Figure.Value(vValue, vVerified, vValue.ToString("F1", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The only measured dollars in TfLens — the OpenCode <c>cost_usd</c> sum over the session stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The measurement lives on sessions, not runs (SCHEMA.md §4).</b> No <c>runs.jsonl</c> record
    /// carries <c>cost_usd</c> at all; the OpenCode plugin writes the real measured price onto the
    /// session stream. Summing runs therefore yielded a structural <c>null</c> — the page reported
    /// "not measured" over a dataset that had actually been measured, which is precisely the plausible
    /// wrong number this product exists to prevent.
    /// </para>
    /// <para>
    /// <b>BRD-27 — a duplicated session must not double-count its dollars.</b> The plugin appends a
    /// <i>cumulative</i> snapshot at every root-session idle, so several records legitimately share a
    /// <c>session_id</c> and only the largest is complete. The stream's own dedupe rule is applied
    /// before anything is summed, so a session that idled five times contributes its price once. The
    /// dedupe runs before the harness filter, because collapsing is a property of the stream and
    /// attribution is a property of the surviving record.
    /// </para>
    /// <para>
    /// <b>BRD-53 — <c>null</c> means nothing measured, never zero.</b> Claude Code and Codex report
    /// <c>null</c> by design and a rate-card figure may never stand in for them, so the sum stays
    /// <c>null</c> unless at least one OpenCode session actually carries a measurement: a zero over
    /// unmeasured records would read as "it cost nothing". Nothing here pools across harnesses.
    /// </para>
    /// </remarks>
    /// <param name="aSessions">Every session record for the user and framework.</param>
    /// <returns>
    /// The measured total and the number of deduped OpenCode session records that carried a
    /// <c>cost_usd</c> and were summed into it. The count travels with the figure so the page can state
    /// the basis it was actually computed over; reporting it separately is what let a caption claim
    /// "over 12 opencode runs" for a sum of two session records.
    /// </returns>
    private static (decimal? Cost, int Sessions) MeasuredOpenCodeCost(IReadOnlyList<SessionRecord> aSessions)
    {
        var vMeasured = Dedupe.Sessions(aSessions).Records
            .Where(aS => string.Equals(aS.Harness, OpenCodeHarness, StringComparison.Ordinal) && aS.CostUsd.HasValue)
            .Select(aS => aS.CostUsd!.Value)
            .ToList();

        return vMeasured.Count == 0
            ? (null, 0)
            : (Math.Round(vMeasured.Sum(), 2, MidpointRounding.ToEven), vMeasured.Count);
    }

    /// <summary>
    /// Counts labels and returns the busiest first.
    /// </summary>
    /// <param name="aValues">The label of each record; <c>null</c> becomes <see cref="UnknownBucket"/>.</param>
    /// <param name="aTake">How many entries to keep.</param>
    /// <returns>Label and count pairs, highest count first then ordinal by label.</returns>
    private static IReadOnlyList<KeyValuePair<string, int>> TopByCount(IEnumerable<string?> aValues, int aTake) =>
        aValues
            .GroupBy(aV => string.IsNullOrWhiteSpace(aV) ? UnknownBucket : aV)
            .Select(aG => new KeyValuePair<string, int>(aG.Key, aG.Count()))
            .OrderByDescending(aP => aP.Value)
            .ThenBy(aP => aP.Key, StringComparer.Ordinal)
            .Take(aTake)
            .ToList();

    /// <summary>Tells whether a run carries any of the §2.5 routing fields.</summary>
    /// <param name="aRun">The run record.</param>
    /// <returns><c>true</c> when the run can appear in the drift table.</returns>
    private static bool HasRoutingFields(RunRecord aRun) =>
        aRun.Tier is not null || aRun.TierModel is not null || aRun.Model is not null
        || aRun.Models is not null || aRun.Routed is not null;

    /// <summary>
    /// Tells whether a run must be excluded from repricing.
    /// </summary>
    /// <remarks>
    /// BRD-60 and SCHEMA.md §2.5: <c>tokens_scope: none</c> means the window could not be computed, and
    /// absent token fields mean "not captured", never zero. Either way the run has no token base to
    /// reprice and is counted as excluded instead of contributing nothing silently.
    /// </remarks>
    /// <param name="aRun">The run record.</param>
    /// <returns><c>true</c> when the run carries no usable token counts.</returns>
    private static bool HasNoTokenBase(RunRecord aRun) =>
        string.Equals(aRun.TokensScope, TokensScopeNone, StringComparison.OrdinalIgnoreCase)
        || (aRun.TokensIn is null && aRun.TokensOut is null
            && aRun.TokensCacheRead is null && aRun.TokensCacheWrite is null);

    /// <summary>
    /// Sums the four §2.5 token counts per observed model.
    /// </summary>
    /// <param name="aRuns">Every run record for the user and framework.</param>
    /// <returns>One row per observed model, largest total first.</returns>
    private static IReadOnlyList<ModelTokens> TokensByModel(IReadOnlyList<RunRecord> aRuns) =>
        aRuns
            .Where(aR => !string.IsNullOrWhiteSpace(aR.Model))
            .GroupBy(aR => aR.Model!, StringComparer.Ordinal)
            .Select(aG => new ModelTokens(
                aG.Key,
                aG.Sum(aR => (long)(aR.TokensIn ?? 0)),
                aG.Sum(aR => (long)(aR.TokensOut ?? 0)),
                aG.Sum(aR => (long)(aR.TokensCacheRead ?? 0)),
                aG.Sum(aR => (long)(aR.TokensCacheWrite ?? 0))))
            .OrderByDescending(aM => aM.Total)
            .ThenBy(aM => aM.Model, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Prices the observed token mix twice — as observed, and as if one model had done everything.
    /// </summary>
    /// <remarks>
    /// Both figures are <b>estimates</b> (<see cref="RateCard.EstimateLabel"/>). They are computed over
    /// exactly the same token base so their difference is meaningful: a run excluded for want of tokens,
    /// or a model the card does not price, is excluded from both sides rather than from one. "Most
    /// expensive" is decided by pricing the whole eligible mix at each priced observed model and taking
    /// the largest result, so it reflects the actual shape of the workload rather than a headline rate.
    /// </remarks>
    /// <param name="aRuns">Every run record for the user and framework.</param>
    /// <param name="aCard">The operator's rate card.</param>
    /// <returns>The two estimates, the model the counterfactual reprices to, and the excluded-run count.</returns>
    private static RepricingResult Reprice(IReadOnlyList<RunRecord> aRuns, RateCard aCard)
    {
        var vObserved = aRuns.Where(aR => !string.IsNullOrWhiteSpace(aR.Model)).ToList();
        var vExcluded = vObserved.Count(HasNoTokenBase);
        var vEligible = vObserved.Where(aR => !HasNoTokenBase(aR) && aCard.Find(aR.Model) is not null).ToList();

        if (vEligible.Count == 0)
        {
            return new RepricingResult(null, null, null, vExcluded);
        }

        var vActual = vEligible.Sum(aR => aCard.Find(aR.Model)!.EstimateUsd(
            aR.TokensIn ?? 0, aR.TokensOut ?? 0, aR.TokensCacheRead ?? 0, aR.TokensCacheWrite ?? 0));

        var vIn = vEligible.Sum(aR => (long)(aR.TokensIn ?? 0));
        var vOut = vEligible.Sum(aR => (long)(aR.TokensOut ?? 0));
        var vCacheRead = vEligible.Sum(aR => (long)(aR.TokensCacheRead ?? 0));
        var vCacheWrite = vEligible.Sum(aR => (long)(aR.TokensCacheWrite ?? 0));

        var vMax = vEligible
            .Select(aR => aR.Model!)
            .Distinct(StringComparer.Ordinal)
            .Select(aM => new KeyValuePair<string, decimal>(
                aM, aCard.Find(aM)!.EstimateUsd(vIn, vOut, vCacheRead, vCacheWrite)))
            .OrderByDescending(aP => aP.Value)
            .ThenBy(aP => aP.Key, StringComparer.Ordinal)
            .First();

        // Round ONCE, here, and round both figures the same way. Both are computed to full precision
        // above — the actual mix by summing exact per-run estimates, the counterfactual by pricing the
        // pooled token total — so the two remain comparable and their delta is meaningful.
        return new RepricingResult(
            Math.Round(vActual, 2, MidpointRounding.ToEven),
            Math.Round(vMax.Value, 2, MidpointRounding.ToEven),
            vMax.Key,
            vExcluded);
    }

    /// <summary>The two repricing estimates and what they were computed over.</summary>
    /// <param name="ActualMixUsd">Estimated cost of the observed model mix; <c>null</c> when nothing was priceable.</param>
    /// <param name="AllAtMaxUsd">Estimated cost of the same tokens at one model; <c>null</c> when nothing was priceable.</param>
    /// <param name="MostExpensiveModel">The model the counterfactual reprices to.</param>
    /// <param name="ExcludedRuns">Runs with an observed model but no usable token base.</param>
    private sealed record RepricingResult(
        decimal? ActualMixUsd,
        decimal? AllAtMaxUsd,
        string? MostExpensiveModel,
        int ExcludedRuns);
}
