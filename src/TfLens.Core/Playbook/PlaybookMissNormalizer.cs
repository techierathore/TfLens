using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Playbook;

/// <summary>
/// Ingests the AI-First-Playbook's normalized miss export into the <b>existing</b> <c>Miss</c> /
/// <c>MissFix</c> / <c>MissAmend</c> tables, and reads it back under the Playbook's own guards
/// (REQ-FN-103, REQ-FN-105, BRD-164, BRD-166, ADR-024).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the existing tables.</b> The two editions record the same lifecycle — a defect is opened, one
/// or more repairs close or move it, an amendment completes a field that was left <c>null</c> — and the
/// producer contract asks for <i>normalization</i>, not for a parallel schema. A second set of tables
/// would mean a second folder, a second guard set, a second export block and a second Coverage panel:
/// four places for the two editions to drift apart, each of which would drift silently because nothing
/// compares them. One set of tables makes the drift a compile error instead (ADR-024).
/// </para>
/// <para>
/// <b>What differs is carried as difference, never merged.</b> <c>MissRecord.ItemId</c> sits beside
/// <c>ReqId</c> — one axis under two names — and <c>MissRecord.FoundPhaseGate</c> sits beside
/// <c>FoundGate</c>, which are two genuinely different measurements and never share a column or a chart
/// (REQ-FN-104, BRD-165, BRD-74). <see cref="PlaybookMissReport"/> honours the second rule by
/// construction: it exposes <see cref="PlaybookMissReport.ByFoundPhaseGate"/> and has no member that can
/// hold assertion-gate data at all, so a page bound to it cannot pool the two.
/// </para>
/// <para>
/// <b>The natural key is the source line, not the miss id.</b> The producer emits its export from a
/// committed <c>misses.ndjson</c> with amendments already folded and exact fix windows already joined,
/// and it re-emits the whole file on every run. Keying on the TechieFlow natural keys would therefore
/// collapse the producer's own history — two fix records for one run, or a corrected line and its
/// predecessor, are distinct source lines and distinct facts. Keying on an <b>immutable hash of the
/// exported line</b> makes re-import idempotent without discarding anything, and preserves stream order
/// because the list this class returns is in file order and nothing sorts it. Cluster A's partial index
/// <c>UcMissUserRepoSourceLine … WHERE "SourceLineHash" IS NOT NULL</c> is the storage half of the same
/// rule: TechieFlow rows carry no hash and must not collide with each other on <c>NULL</c>.
/// </para>
/// <para>
/// <b>Amendments are re-folded at read time, by the one folder.</b> The producer folds its own
/// amendments before exporting; TfLens folds again through <see cref="MissAmendFolder"/> anyway, exactly
/// as BRD-116 requires for the TechieFlow stream. This is not redundancy — it is the difference between
/// trusting a producer and re-checking it, it keeps <c>RebuildAsync</c> re-deriving identical values from
/// <c>data/raw/</c>, and it means orphan and overwrite amendments surface as <b>visible diagnostics</b>
/// rather than being applied or discarded silently. Writing a second folding implementation here is the
/// specific failure ADR-024 argues against.
/// </para>
/// <para>
/// <b>The wall between editions.</b> <c>UserRepo.Framework</c> is the wall (ADR-016), and the architecture
/// names the residual risk plainly: a query that forgets the framework filter would pool Playbook and
/// TechieFlow misses with nothing to show for it. Every entry point here therefore takes the framework as
/// a <b>required positional parameter</b> and rejects anything but <see cref="FrameworkNames.Playbook"/>.
/// There is no overload without it, and no default value that a caller could leave alone.
/// </para>
/// </remarks>
public static class PlaybookMissNormalizer
{
    /// <summary>Fields every kind carries; mapped onto the columns all three tables share.</summary>
    private static readonly string[] CommonKeys =
        ["v", "ts", "kind", "app", "project_type", "project_type_inferred", "backfilled", "harness"];

    /// <summary>Wire keys of <c>kind:"miss"</c> that reach a column; everything else is preserved as overflow.</summary>
    private static readonly HashSet<string> MissKeys = new(
        CommonKeys.Concat(
        [
            "miss_id", "req_id", "item_id", "req_class", "miss_class", "artifact", "severity", "why_missed",
            "origin_phase", "origin_agent", "origin_run_id", "origin_confidence", "origin_model",
            "origin_harness", "found_by", "found_phase", "found_gate", "found_phase_gate", "found_run_id",
            "failure_class"
        ]),
        StringComparer.Ordinal);

    /// <summary>Wire keys of <c>kind:"miss-fix"</c> that reach a column.</summary>
    /// <remarks>
    /// <c>item_id</c> is deliberately absent: <c>"MissFix"</c> has no such column, because the fix's
    /// requirement axis is the one its parent miss already carries and duplicating it would give one
    /// axis two places to disagree. It is preserved as overflow like any other unmapped key.
    /// </remarks>
    private static readonly HashSet<string> MissFixKeys = new(
        CommonKeys.Concat(
        [
            "miss_id", "req_id", "fix_run_id", "fix_cmd", "fix_attempt", "verdict_after", "reopened",
            "cost_attribution", "tokens_in", "tokens_out", "tokens_cache_read", "tokens_cache_write",
            "cost_usd", "tokens_scope", "model"
        ]),
        StringComparer.Ordinal);

    /// <summary>Wire keys of <c>kind:"miss-amend"</c> that reach a column.</summary>
    private static readonly HashSet<string> MissAmendKeys = new(
        CommonKeys.Concat(["miss_id", "field", "value"]),
        StringComparer.Ordinal);

    /// <summary>
    /// The immutable source-line identity a Playbook row is keyed on (REQ-FN-103, BRD-164).
    /// </summary>
    /// <remarks>
    /// SHA-256 of the trimmed line's UTF-8 bytes, lower-case hex. Trimmed so a trailing <c>\r</c> from a
    /// Windows checkout cannot mint a second identity for a line the producer wrote once; nothing else is
    /// normalized, because re-ordering keys or re-formatting numbers would make the hash depend on this
    /// code's opinions rather than on the producer's bytes — and the whole value of the key is that the
    /// producer and TfLens compute it from the same thing.
    /// </remarks>
    /// <param name="aLine">One exported NDJSON line, exactly as read.</param>
    /// <returns>The 64-character lower-case hex digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aLine"/> is <c>null</c>.</exception>
    public static string SourceLineHashOf(string aLine)
    {
        ArgumentNullException.ThrowIfNull(aLine);

        var vDigest = SHA256.HashData(Encoding.UTF8.GetBytes(aLine.Trim()));
        return Convert.ToHexStringLower(vDigest);
    }

    /// <summary>
    /// Normalizes the exporter's stdout into rows for the three existing miss tables (REQ-FN-103).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never throws on content. A line that is not JSON, is not an object, or declares a <c>kind</c>
    /// outside <see cref="MissKinds"/> is counted and skipped, exactly as <c>StreamParser</c> treats a
    /// malformed <c>misses.jsonl</c> line: an export is a whole file, and one bad line must not cost the
    /// rest of it.
    /// </para>
    /// <para>
    /// Duplicate source lines within one export collapse to the first occurrence and are counted, so a
    /// re-import of an unchanged file produces the same rows in the same order. Across imports the
    /// store's <c>ON CONFLICT DO NOTHING</c> and the partial unique index do the same job.
    /// </para>
    /// </remarks>
    /// <param name="aRepo">The connected source; must be on <see cref="FrameworkNames.Playbook"/>.</param>
    /// <param name="aSourceSha">The bundle sha256 or commit SHA the export arrived under.</param>
    /// <param name="aNdjson">The exporter's stdout, verbatim.</param>
    /// <returns>Rows ready for <see cref="ITelemetryStore.UpsertAsync"/>, plus the ingest diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRepo"/> or <paramref name="aNdjson"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="aSourceSha"/> is blank, or the repository is not a Playbook source.</exception>
    public static PlaybookMissNormalization Normalize(UserRepo aRepo, string aSourceSha, string aNdjson)
    {
        ArgumentNullException.ThrowIfNull(aRepo);
        ArgumentNullException.ThrowIfNull(aNdjson);
        ArgumentException.ThrowIfNullOrWhiteSpace(aSourceSha);

        if (!string.Equals(aRepo.Framework, FrameworkNames.Playbook, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Repository {aRepo.Repo} is on framework '{aRepo.Framework}'. The Playbook miss export may "
                + "only be normalized into a Playbook source, or the two editions would pool (ADR-016).",
                nameof(aRepo));
        }

        return Normalize(aRepo.UserId, aRepo.Repo, aSourceSha, aNdjson);
    }

    /// <summary>
    /// Normalizes the exporter's stdout when the caller has already established the axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="UserRepo"/> overload above exists to CHECK the axis; this one exists for the
    /// caller that cannot. <see cref="Parsing.StreamParser"/> is handed a repository <b>name</b> and
    /// never the row, so a framework check made there would be a check made on nothing — worse than
    /// absent, because it would read as protection.
    /// </para>
    /// <para>
    /// The axis is established twice before these bytes arrive here, in the two places it can be:
    /// <c>ImportStreamCatalog.TryResolveFramework</c> refuses a bundle mixing editions and resolves
    /// <c>playbookmisses</c> onto the Playbook axis, and <see cref="ReadAsync"/> takes the framework as a
    /// mandatory read parameter — which is where a pooling read would actually happen (ADR-016), and the
    /// residual risk the architecture names.
    /// </para>
    /// </remarks>
    /// <param name="aUserId">The AppManager user the rows belong to.</param>
    /// <param name="aRepo">The <c>owner/name</c> of the Playbook source.</param>
    /// <param name="aSourceSha">The bundle sha256 standing as dataset identity (ADR-022).</param>
    /// <param name="aNdjson">The exporter's stdout.</param>
    /// <returns>The three record lists plus the counts a caller reports.</returns>
    public static PlaybookMissNormalization Normalize(
        int aUserId,
        string aRepo,
        string aSourceSha,
        string aNdjson)
    {
        ArgumentNullException.ThrowIfNull(aRepo);
        ArgumentNullException.ThrowIfNull(aNdjson);
        ArgumentException.ThrowIfNullOrWhiteSpace(aSourceSha);

        var vState = new NormalizeState(aUserId, aRepo, aSourceSha);

        foreach (var vLine in aNdjson.Split('\n'))
        {
            AddLine(vState, vLine);
        }

        return vState.ToNormalization();
    }

    /// <summary>
    /// Reads the Playbook miss block for one user, folding amendments and applying the Playbook's own
    /// guards (REQ-FN-103, REQ-FN-105, REQ-FN-104).
    /// </summary>
    /// <remarks>
    /// <paramref name="aFramework"/> is required and is checked rather than merely forwarded, because the
    /// architecture's stated residual risk is a read that forgets it. Passing
    /// <see cref="FrameworkNames.TechieFlow"/> here is a programming error, not a mode: the TechieFlow
    /// stream is reported by <see cref="MissFigures"/> under its own, weaker guards.
    /// </remarks>
    /// <param name="aStore">The telemetry store.</param>
    /// <param name="aUserId">The AppManager user whose rows are read.</param>
    /// <param name="aFramework">The provenance axis; must be <see cref="FrameworkNames.Playbook"/>.</param>
    /// <param name="aRepo">One repository, or <c>null</c> for every Playbook source this user connected.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The folded misses, the diagnostics, and the guarded figures.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aStore"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aFramework"/> is not the Playbook.</exception>
    public static async Task<PlaybookMissReport> ReadAsync(
        ITelemetryStore aStore,
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aStore);
        RequirePlaybook(aFramework);

        var vMisses = await aStore
            .ReadMissesAsync(aUserId, aFramework, aRepo, aCancellationToken).ConfigureAwait(false);
        var vFixes = await aStore
            .ReadMissFixesAsync(aUserId, aFramework, aRepo, aCancellationToken).ConfigureAwait(false);
        var vAmends = await aStore
            .ReadMissAmendsAsync(aUserId, aFramework, aRepo, aCancellationToken).ConfigureAwait(false);

        return Read(aFramework, vMisses, vFixes, vAmends);
    }

    /// <summary>
    /// Computes the Playbook miss block from rows already read for one framework (REQ-FN-105).
    /// </summary>
    /// <remarks>
    /// The order is fixed and is the whole method: <b>fold first</b>, then apply the eligibility floor,
    /// then guard, then count. Folding before the <c>why_missed</c> distribution is what lets an
    /// amendment reach that distribution at all (BRD-116); guarding before counting is what keeps a
    /// refused record out of every figure it could have flattered.
    /// </remarks>
    /// <param name="aFramework">The provenance axis the rows were read on; must be the Playbook.</param>
    /// <param name="aMisses">The stored <c>miss</c> rows, unfolded, in stream order.</param>
    /// <param name="aFixes">The stored <c>miss-fix</c> rows.</param>
    /// <param name="aAmends">The stored <c>miss-amend</c> rows.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException">Any record list is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aFramework"/> is not the Playbook.</exception>
    public static PlaybookMissReport Read(
        string aFramework,
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<MissAmendRecord> aAmends)
    {
        RequirePlaybook(aFramework);
        ArgumentNullException.ThrowIfNull(aMisses);
        ArgumentNullException.ThrowIfNull(aFixes);
        ArgumentNullException.ThrowIfNull(aAmends);

        // ---- (1) re-fold, through the one folder, before anything is counted (BRD-116, BRD-164).
        var vFold = MissAmendFolder.Fold(aMisses, aAmends);

        var vKnown = vFold.Misses
            .Select(aMiss => LinkKey(aMiss.Repo, aMiss.MissId))
            .ToHashSet(StringComparer.Ordinal);

        var vOrphanFixes = aFixes
            .Where(aFix => !vKnown.Contains(LinkKey(aFix.Repo, aFix.MissId)))
            .Select(aFix => new PlaybookOrphanFix(aFix.Repo, aFix.MissId, aFix.FixRunId))
            .ToList();

        // ---- (2) the eligibility floor, before the optional field's denominator (BRD-166).
        var vEligibility = LateGateCoverageCalculator.EligibilityFor(
            MissAmendFolder.WhyMissedField,
            vFold.Misses,
            aMiss => aMiss.Ts,
            aMiss => aMiss.WhyMissed);

        return new PlaybookMissReport
        {
            Framework = aFramework,
            Misses = vFold.Misses,
            Diagnostics = new PlaybookMissDiagnostics
            {
                AmendmentsApplied = vFold.AmendmentsApplied,
                OverwriteAmendmentsIgnored = vFold.AmendmentsIgnored,
                OrphanAmends = vFold.Orphans,
                OrphanFixes = vOrphanFixes
            },
            WhyMissedEligibility = vEligibility,
            WhyMissedDistribution = Distribution(vFold.Misses, aMiss => aMiss.WhyMissed),
            ByItemId = Distribution(vFold.Misses, aMiss => aMiss.ItemId),
            ByFoundPhaseGate = Distribution(vFold.Misses, aMiss => aMiss.FoundPhaseGate),
            Attribution = Attribute(vFold.Misses),
            Cost = Money(aFixes)
        };
    }

    /// <summary>
    /// Splits misses into the ones that may name a model or a tier and the ones that may not (BRD-166).
    /// </summary>
    /// <remarks>
    /// Both distributions are computed over the attributed records <b>only</b>. A refused record does not
    /// reappear under <c>unknown</c>, under <c>not-recorded</c> or under any other stand-in, because a
    /// bucket named for a model is a claim about a model and the producer declined to make it.
    /// </remarks>
    /// <param name="aMisses">The folded misses.</param>
    /// <returns>The attributed records, their distributions, and the refusals by reason.</returns>
    private static PlaybookAttributionSplit Attribute(IReadOnlyList<MissRecord> aMisses)
    {
        var vAttributed = new List<MissRecord>();
        var vRefused = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var vMiss in aMisses)
        {
            var vReason = PlaybookMissGuards.RefuseAttribution(vMiss);
            if (vReason is null)
            {
                vAttributed.Add(vMiss);
                continue;
            }

            vRefused[vReason] = vRefused.GetValueOrDefault(vReason) + 1;
        }

        return new PlaybookAttributionSplit
        {
            Attributed = vAttributed,
            Refused = Refusals(vRefused),
            ByOriginModel = Distribution(vAttributed, aMiss => aMiss.OriginModel),
            ByOriginPhase = Distribution(vAttributed, aMiss => aMiss.OriginPhase)
        };
    }

    /// <summary>
    /// The three cost cohorts and the measured-dollar figure (BRD-166).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Headline tokens come from <see cref="PlaybookCostCohort.Headline"/> records alone; apportioned
    /// tokens divide each shared window by its own <c>n</c>. They are returned inside the existing
    /// <see cref="MissCost"/>, which has no property that could hold their sum — the same technique, and
    /// the same type, the TechieFlow edition uses (ADR-019).
    /// </para>
    /// <para>
    /// Measured dollars are summed from <c>CostUsd</c> on headline records and from nothing else. A
    /// rate-card <c>*_usd_estimate</c> value never reaches that column — <see cref="Normalize"/> does not
    /// map it — and there is no property on the result that could hold the two together.
    /// </para>
    /// </remarks>
    /// <param name="aFixes">The stored fix rows.</param>
    /// <returns>The cost split.</returns>
    private static PlaybookCostSplit Money(IReadOnlyList<MissFixRecord> aFixes)
    {
        var vHeadlineTokens = new List<double>();
        var vApportionedTokens = new List<double>();
        var vMeasuredUsd = new List<decimal>();
        var vRefused = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var vHeadline = 0;
        var vApportioned = 0;
        var vExcluded = 0;

        foreach (var vFix in aFixes)
        {
            var vVerdict = PlaybookMissGuards.ClassifyCost(vFix);

            if (vVerdict.Cohort == PlaybookCostCohort.Headline)
            {
                vHeadline++;
                AddIfPresent(vHeadlineTokens, vFix.TokensOut, 1);
                if (vFix.CostUsd is { } vUsd)
                {
                    vMeasuredUsd.Add(vUsd);
                }

                continue;
            }

            if (vVerdict.Cohort == PlaybookCostCohort.Apportioned)
            {
                vApportioned++;
                AddIfPresent(vApportionedTokens, vFix.TokensOut, vVerdict.Across ?? 1);
                continue;
            }

            if (vVerdict.Cohort == PlaybookCostCohort.Excluded)
            {
                vExcluded++;
            }

            if (vVerdict.Reason is { } vReason)
            {
                vRefused[vReason] = vRefused.GetValueOrDefault(vReason) + 1;
            }
        }

        return new PlaybookCostSplit
        {
            HeadlineTokens = new MissCost(Mean(vHeadlineTokens, 1), Mean(vApportionedTokens, 1), vExcluded),
            HeadlineRecords = vHeadline,
            ApportionedRecords = vApportioned,
            ExcludedRecords = vExcluded,
            Refused = Refusals(vRefused),
            MeasuredUsdPerFix = MeanUsd(vMeasuredUsd),
            MeasuredUsdTotal = vMeasuredUsd.Count == 0 ? null : vMeasuredUsd.Sum(),
            MeasuredRecords = vMeasuredUsd.Count
        };
    }

    /// <summary>Adds one record's share of a token window, skipping the records that carry none.</summary>
    /// <remarks>
    /// An absent count is not a zero. Summing it as one would present an unmeasured repair as a costless
    /// one, which is the failure this product exists to prevent (BRD-31..BRD-36).
    /// </remarks>
    /// <param name="aValues">The per-record values collected so far.</param>
    /// <param name="aTokens">The record's output tokens, or <c>null</c>.</param>
    /// <param name="aAcross">How many ways the window splits; <c>1</c> for a headline record.</param>
    private static void AddIfPresent(List<double> aValues, int? aTokens, int aAcross)
    {
        if (aTokens is { } vTokens)
        {
            aValues.Add((double)vTokens / aAcross);
        }
    }

    /// <summary>
    /// Counts an optional field's values, leaving the records that do not carry it out entirely.
    /// </summary>
    /// <remarks>
    /// A <c>null</c> is <i>not assessed</i>: not a bucket, not an <c>other</c>, not a zero. It is neither
    /// counted nor allowed to inflate the denominator every share is read against.
    /// </remarks>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="aRecords">The records to count.</param>
    /// <param name="aValueOf">Reads the optional field.</param>
    /// <returns>One row per value observed, ordinally ordered so the report order is stable.</returns>
    private static IReadOnlyList<MissCategoryCount> Distribution<T>(
        IReadOnlyList<T> aRecords,
        Func<T, string?> aValueOf)
    {
        var vCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var vRecord in aRecords)
        {
            var vValue = aValueOf(vRecord);
            if (!string.IsNullOrEmpty(vValue))
            {
                vCounts[vValue] = vCounts.GetValueOrDefault(vValue) + 1;
            }
        }

        var vDenominator = vCounts.Sum(aEntry => aEntry.Value);
        return vCounts
            .Select(aEntry => new MissCategoryCount(
                aEntry.Key,
                aEntry.Value,
                MetricsConstants.Pct(aEntry.Value, vDenominator)))
            .ToList();
    }

    /// <summary>Renders a reason-to-count map as the refusal rows the reader is shown.</summary>
    /// <param name="aCounts">How many records each reason refused.</param>
    /// <returns>The rows, ordinally ordered.</returns>
    private static IReadOnlyList<PlaybookGuardRefusal> Refusals(IReadOnlyDictionary<string, int> aCounts) =>
        aCounts.Select(aEntry => new PlaybookGuardRefusal(aEntry.Key, aEntry.Value)).ToList();

    /// <summary>The mean of a per-record quantity, refusing below the minimum-n floor.</summary>
    /// <param name="aValues">One value per record that carried the quantity.</param>
    /// <param name="aDecimals">Decimal places to round to.</param>
    /// <returns><c>NotApplicable</c> when nothing carried it, <c>InsufficientData</c> below three records, else the mean.</returns>
    private static Figure Mean(IReadOnlyList<double> aValues, int aDecimals)
    {
        if (aValues.Count == 0)
        {
            return Figure.NotApplicable();
        }

        if (aValues.Count < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aValues.Count);
        }

        var vMean = Math.Round(aValues.Sum() / aValues.Count, aDecimals, MidpointRounding.ToEven);
        return Figure.Value(vMean, aValues.Count, vMean.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Measured dollars per headline fix, to four decimal places.</summary>
    /// <param name="aValues">The measurements; empty when nothing measured any.</param>
    /// <returns>The mean, or an honest refusal.</returns>
    private static Figure MeanUsd(IReadOnlyList<decimal> aValues)
    {
        if (aValues.Count == 0)
        {
            return Figure.NotApplicable();
        }

        if (aValues.Count < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aValues.Count);
        }

        var vMean = Math.Round(aValues.Sum() / aValues.Count, 4, MidpointRounding.ToEven);
        return Figure.Value(
            (double)vMean,
            aValues.Count,
            vMean.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The link key, scoped to the repository exactly as <c>MissAmendFolder</c> scopes it.</summary>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aMissId">The miss id.</param>
    /// <returns>The composite key.</returns>
    private static string LinkKey(string aRepo, string aMissId) => aRepo + " " + aMissId;

    /// <summary>Refuses any framework but the Playbook's, so no read can pool the two editions.</summary>
    /// <param name="aFramework">The framework the caller passed.</param>
    /// <exception cref="ArgumentOutOfRangeException">The framework is not <see cref="FrameworkNames.Playbook"/>.</exception>
    private static void RequirePlaybook(string aFramework)
    {
        if (!string.Equals(aFramework, FrameworkNames.Playbook, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(aFramework),
                aFramework,
                "The Playbook miss guards apply to the Playbook edition only. Reading another framework "
                + "here would apply one edition's guards to the other's records (ADR-016, ADR-024).");
        }
    }

    /// <summary>Reads one exported line into the right record list, or counts it as skipped.</summary>
    /// <param name="aState">The accumulating normalization.</param>
    /// <param name="aLine">The raw line, exactly as read.</param>
    private static void AddLine(NormalizeState aState, string aLine)
    {
        var vTrimmed = aLine.Trim();
        if (vTrimmed.Length == 0)
        {
            return;
        }

        aState.Lines++;

        JsonDocument vDocument;
        try
        {
            vDocument = JsonDocument.Parse(vTrimmed);
        }
        catch (JsonException)
        {
            aState.InvalidLines++;
            return;
        }

        using (vDocument)
        {
            if (vDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                aState.InvalidLines++;
                return;
            }

            AddRecord(aState, vDocument.RootElement, SourceLineHashOf(vTrimmed));
        }
    }

    /// <summary>Dispatches one parsed export record on its own <c>kind</c>.</summary>
    /// <param name="aState">The accumulating normalization.</param>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aHash">The line's immutable source-line hash.</param>
    private static void AddRecord(NormalizeState aState, JsonElement aObject, string aHash)
    {
        if (!aState.Hashes.Add(aHash))
        {
            aState.DuplicateSourceLines++;
            return;
        }

        switch (Text(aObject, "kind"))
        {
            case MissKinds.Miss:
                aState.Misses.Add(BuildMiss(aState, aObject, aHash));
                break;
            case MissKinds.MissFix:
                aState.MissFixes.Add(BuildFix(aState, aObject, aHash));
                break;
            case MissKinds.MissAmend:
                aState.MissAmends.Add(BuildAmend(aState, aObject, aHash));
                break;
            default:
                // An unknown kind in a stream TfLens does know is the same class of problem as a malformed
                // line: counted and skipped, never thrown (REQ-FN-072).
                aState.UnknownKinds++;
                aState.InvalidLines++;
                aState.Hashes.Remove(aHash);
                break;
        }
    }

    /// <summary>Builds one <c>miss</c> row, with both Playbook axes in their own columns.</summary>
    /// <param name="aState">The accumulating normalization, for the user, repo and source SHA.</param>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aHash">The line's immutable source-line hash.</param>
    /// <returns>The row.</returns>
    private static MissRecord BuildMiss(NormalizeState aState, JsonElement aObject, string aHash) => new()
    {
        UserId = aState.UserId,
        Repo = aState.Repo,
        SourceSha = aState.SourceSha,
        V = Number(aObject, "v") is { } vVersion ? (int)vVersion : 1,
        Ts = Text(aObject, "ts") ?? string.Empty,
        App = Text(aObject, "app"),
        ProjectType = Text(aObject, "project_type"),
        ProjectTypeInferred = Flag(aObject, "project_type_inferred"),
        Backfilled = Flag(aObject, "backfilled"),
        Harness = Text(aObject, "harness"),
        MissId = Text(aObject, "miss_id") ?? aHash,
        ReqId = Text(aObject, "req_id"),
        ItemId = Text(aObject, "item_id"),
        ReqClass = Text(aObject, "req_class"),
        MissClass = Text(aObject, "miss_class"),
        Artifact = Text(aObject, "artifact"),
        Severity = Text(aObject, "severity"),
        WhyMissed = Text(aObject, "why_missed"),
        OriginPhase = Text(aObject, "origin_phase"),
        OriginAgent = Text(aObject, "origin_agent"),
        OriginRunId = Text(aObject, "origin_run_id"),
        OriginConfidence = Text(aObject, "origin_confidence"),
        OriginModel = Text(aObject, "origin_model"),
        OriginHarness = Text(aObject, "origin_harness"),
        FoundBy = Text(aObject, "found_by"),
        FoundPhase = Text(aObject, "found_phase"),
        FoundGate = Text(aObject, "found_gate"),
        FoundPhaseGate = Text(aObject, "found_phase_gate"),
        FoundRunId = Text(aObject, "found_run_id"),
        FailureClass = Text(aObject, "failure_class"),
        SourceLineHash = aHash,
        Overflow = Overflow(aObject, MissKeys)
    };

    /// <summary>Builds one <c>miss-fix</c> row.</summary>
    /// <remarks>
    /// <c>cost_usd</c> is the only money key read, and it is read as a decimal. No key ending in
    /// <see cref="PlaybookMissGuards.UsdEstimateSuffix"/> is mapped anywhere, so a rate-card estimate
    /// cannot become a measurement by passing through this method (BRD-166).
    /// </remarks>
    /// <param name="aState">The accumulating normalization.</param>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aHash">The line's immutable source-line hash.</param>
    /// <returns>The row.</returns>
    private static MissFixRecord BuildFix(NormalizeState aState, JsonElement aObject, string aHash) => new()
    {
        UserId = aState.UserId,
        Repo = aState.Repo,
        SourceSha = aState.SourceSha,
        V = Number(aObject, "v") is { } vVersion ? (int)vVersion : 1,
        Ts = Text(aObject, "ts") ?? string.Empty,
        App = Text(aObject, "app"),
        ProjectType = Text(aObject, "project_type"),
        ProjectTypeInferred = Flag(aObject, "project_type_inferred"),
        Backfilled = Flag(aObject, "backfilled"),
        Harness = Text(aObject, "harness"),
        MissId = Text(aObject, "miss_id") ?? aHash,
        ReqId = Text(aObject, "req_id"),
        FixRunId = Text(aObject, "fix_run_id"),
        FixCmd = Text(aObject, "fix_cmd"),
        FixAttempt = Number(aObject, "fix_attempt") is { } vAttempt ? (int)vAttempt : null,
        VerdictAfter = Text(aObject, "verdict_after"),
        Reopened = Flag(aObject, "reopened"),
        CostAttribution = Text(aObject, "cost_attribution"),
        TokensIn = Number(aObject, "tokens_in") is { } vIn ? (int)vIn : null,
        TokensOut = Number(aObject, "tokens_out") is { } vOut ? (int)vOut : null,
        TokensCacheRead = Number(aObject, "tokens_cache_read") is { } vRead ? (int)vRead : null,
        TokensCacheWrite = Number(aObject, "tokens_cache_write") is { } vWrite ? (int)vWrite : null,
        CostUsd = Number(aObject, "cost_usd"),
        TokensScope = Text(aObject, "tokens_scope"),
        Model = Text(aObject, "model"),
        SourceLineHash = aHash,
        Overflow = Overflow(aObject, MissFixKeys)
    };

    /// <summary>Builds one <c>miss-amend</c> row, stored verbatim and folded only at read time.</summary>
    /// <param name="aState">The accumulating normalization.</param>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aHash">The line's immutable source-line hash.</param>
    /// <returns>The row.</returns>
    private static MissAmendRecord BuildAmend(NormalizeState aState, JsonElement aObject, string aHash) => new()
    {
        UserId = aState.UserId,
        Repo = aState.Repo,
        SourceSha = aState.SourceSha,
        V = Number(aObject, "v") is { } vVersion ? (int)vVersion : 1,
        Ts = Text(aObject, "ts") ?? string.Empty,
        App = Text(aObject, "app"),
        ProjectType = Text(aObject, "project_type"),
        ProjectTypeInferred = Flag(aObject, "project_type_inferred"),
        Backfilled = Flag(aObject, "backfilled"),
        Harness = Text(aObject, "harness"),
        MissId = Text(aObject, "miss_id") ?? aHash,
        Field = Text(aObject, "field") ?? string.Empty,
        Value = Text(aObject, "value"),
        SourceLineHash = aHash,
        Overflow = Overflow(aObject, MissAmendKeys)
    };

    /// <summary>
    /// Preserves every property that reaches no column, verbatim, as a JSON object.
    /// </summary>
    /// <remarks>
    /// This is where <c>data_quality</c> and the source window live, which is why
    /// <see cref="PlaybookMissGuards.QualityOf"/> reads from here. It is also where a rate-card
    /// <c>*_usd_estimate</c> lands — preserved, so a rebuild loses nothing, and unreachable from any
    /// measured figure.
    /// </remarks>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aMapped">The wire keys this kind maps onto columns.</param>
    /// <returns>The overflow JSON, or <c>null</c> when every property was mapped.</returns>
    private static string? Overflow(JsonElement aObject, IReadOnlySet<string> aMapped)
    {
        var vExtras = aObject.EnumerateObject()
            .Where(aProperty => !aMapped.Contains(aProperty.Name))
            .ToList();

        if (vExtras.Count == 0)
        {
            return null;
        }

        var vBuffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var vWriter = new Utf8JsonWriter(vBuffer))
        {
            vWriter.WriteStartObject();
            foreach (var vProperty in vExtras)
            {
                vProperty.WriteTo(vWriter);
            }

            vWriter.WriteEndObject();
        }

        return Encoding.UTF8.GetString(vBuffer.WrittenSpan);
    }

    /// <summary>Reads a string property, or <c>null</c> when it is absent, null or not a string.</summary>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aName">The wire key.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static string? Text(JsonElement aObject, string aName) =>
        aObject.TryGetProperty(aName, out var vValue) && vValue.ValueKind == JsonValueKind.String
            ? vValue.GetString()
            : null;

    /// <summary>Reads a numeric property as a decimal, or <c>null</c> when it is absent or not a number.</summary>
    /// <remarks>
    /// Decimal rather than double because provider cost is fixed precision, not binary float — the
    /// producer contract states it, and a token count is exact in decimal too.
    /// </remarks>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aName">The wire key.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static decimal? Number(JsonElement aObject, string aName) =>
        aObject.TryGetProperty(aName, out var vValue)
        && vValue.ValueKind == JsonValueKind.Number
        && vValue.TryGetDecimal(out var vNumber)
            ? vNumber
            : null;

    /// <summary>Reads a boolean property, or <c>null</c> when it is absent or not a boolean.</summary>
    /// <param name="aObject">The parsed line.</param>
    /// <param name="aName">The wire key.</param>
    /// <returns>The flag, or <c>null</c> — never coerced to <c>false</c>.</returns>
    private static bool? Flag(JsonElement aObject, string aName)
    {
        if (!aObject.TryGetProperty(aName, out var vValue))
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

    /// <summary>The mutable accumulator one <see cref="Normalize"/> call builds its result in.</summary>
    /// <param name="aUserId">The AppManager user the rows belong to.</param>
    /// <param name="aRepo">The <c>owner/name</c> of the Playbook source the export came from.</param>
    /// <param name="aSourceSha">The bundle sha256 or commit SHA.</param>
    private sealed class NormalizeState(int aUserId, string aRepo, string aSourceSha)
    {
        /// <summary>The AppManager user the rows belong to.</summary>
        public int UserId { get; } = aUserId;

        /// <summary>The <c>owner/name</c> of the Playbook source the export came from.</summary>
        public string Repo { get; } = aRepo;

        /// <summary>The bundle sha256 or commit SHA the export arrived under.</summary>
        public string SourceSha { get; } = aSourceSha;

        /// <summary>Source-line hashes already seen in this export.</summary>
        public HashSet<string> Hashes { get; } = new(StringComparer.Ordinal);

        /// <summary><c>miss</c> rows, in file order.</summary>
        public List<MissRecord> Misses { get; } = [];

        /// <summary><c>miss-fix</c> rows, in file order.</summary>
        public List<MissFixRecord> MissFixes { get; } = [];

        /// <summary><c>miss-amend</c> rows, in file order.</summary>
        public List<MissAmendRecord> MissAmends { get; } = [];

        /// <summary>Non-blank lines read.</summary>
        public int Lines { get; set; }

        /// <summary>Lines skipped as malformed or of an unknown kind.</summary>
        public int InvalidLines { get; set; }

        /// <summary>Lines whose <c>kind</c> is outside <see cref="MissKinds"/>.</summary>
        public int UnknownKinds { get; set; }

        /// <summary>Lines whose source-line hash had already been seen in this export.</summary>
        public int DuplicateSourceLines { get; set; }

        /// <summary>Freezes the accumulator into the result the caller gets.</summary>
        /// <returns>The normalization.</returns>
        public PlaybookMissNormalization ToNormalization() => new()
        {
            Parsed = new ParseResult
            {
                UserId = UserId,
                Repo = Repo,
                SourceSha = SourceSha,
                Stream = StreamKind.Misses,
                Misses = Misses,
                MissFixes = MissFixes,
                MissAmends = MissAmends,
                InvalidLines = InvalidLines,
                DuplicatesCollapsed = DuplicateSourceLines
            },
            Lines = Lines,
            UnknownKinds = UnknownKinds,
            DuplicateSourceLines = DuplicateSourceLines
        };
    }
}
