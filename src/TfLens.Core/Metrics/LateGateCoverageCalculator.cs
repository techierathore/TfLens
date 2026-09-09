using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of the reference's <c>late_gate_coverage</c> block, and of its sibling <c>FIELD_SINCE</c>
/// eligibility floor (REQ-FN-052, REQ-FN-076).
/// </summary>
/// <remarks>
/// <para>
/// A gate that entered the enum mid-stream is structurally understated in a raw distribution, so its
/// share there is never its catch rate (SCHEMA.md §3.5, REQ-FN-052). This reports <c>ran</c> — records
/// whose <c>gates_run</c> contains the gate — beside <c>caught</c>, and derives the rate from
/// <c>ran</c> alone. There is no code path here that divides <c>caught</c> by the distribution total.
/// </para>
/// <para>
/// <b>An optional field added mid-stream is the same rule seen from the other side</b>, which is why
/// it lives in this class and reads its dates from <see cref="MetricsConstants.FieldSince"/>, the table
/// that sits beside <see cref="MetricsConstants.LateGates"/>. A record written before the field existed
/// had no field to fill, so it leaves that field's denominator entirely and is <b>reported
/// separately</b> rather than counted as unassessed and rather than silently dropped
/// (SCHEMA.md §5.5.6, BRD-117).
/// </para>
/// </remarks>
public static class LateGateCoverageCalculator
{
    /// <summary>
    /// Computes coverage for every gate in <see cref="MetricsConstants.LateGates"/>.
    /// </summary>
    /// <param name="aSegmentRecords">Every gate record in the segment — not only the failures.</param>
    /// <param name="aFailureCounts">The gate distribution counts from <see cref="GateDistribution.Count"/>.</param>
    /// <returns>One entry per late gate, in the table's declared order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aSegmentRecords"/> or <paramref name="aFailureCounts"/> is <c>null</c>.</exception>
    public static IReadOnlyList<LateGateCoverage> Compute(
        IReadOnlyList<GateRecord> aSegmentRecords,
        IReadOnlyDictionary<string, int> aFailureCounts)
    {
        ArgumentNullException.ThrowIfNull(aSegmentRecords);
        ArgumentNullException.ThrowIfNull(aFailureCounts);

        var vCoverage = new List<LateGateCoverage>();
        foreach (var vLateGate in MetricsConstants.LateGates)
        {
            var vRan = aSegmentRecords.Count(aRecord => GatesRun.Contains(aRecord, vLateGate.Key));
            var vCaught = aFailureCounts.GetValueOrDefault(vLateGate.Key);
            vCoverage.Add(new LateGateCoverage(
                vLateGate.Key,
                vRan,
                vCaught,
                vLateGate.Value,
                CatchRate(vRan, vCaught)));
        }

        return vCoverage;
    }

    /// <summary>
    /// Splits a set of records into the ones eligible to carry an optional field and the ones that
    /// predate it, and counts how many eligible records actually carry it (REQ-FN-076).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The floor comes from <see cref="MetricsConstants.FieldSince"/> — the table beside
    /// <see cref="MetricsConstants.LateGates"/>, and the same rule: read a mid-stream addition against
    /// what could have been observed, never against the total, and state the excluded count. A field
    /// with no row in that table has no floor, so every record is eligible.
    /// </para>
    /// <para>
    /// <b>Assessed is a subset of eligible, never of the whole set.</b> A record that predates the field
    /// <i>and does not carry it</i> is not an unassessed record: it never had the chance, so it leaves
    /// the denominator entirely rather than pushing every category's share down. The two counts are
    /// returned together because a reader cannot check <c>n of N assessed</c> against the oracle without
    /// both.
    /// </para>
    /// <para>
    /// <b>A record that carries the field is eligible whatever its date</b> (amended 2026-09-08,
    /// BRD-116, BRD-117). An amendment may complete a field on a record written before that field
    /// existed — which is the ordinary case for <c>sort</c> — and excluding such a record would fold the
    /// answer in and then discard it, leaving the amendment with no visible effect at all.
    /// </para>
    /// <para>
    /// Timestamps are ISO-8601 UTC text, whose lexical order is chronological, so the comparison is the
    /// date prefix against the floor date — no parsing, no time zone, no chance of a locale changing a
    /// denominator. A record with no usable timestamp is treated as eligible: excluding it would quietly
    /// shrink a denominator on the strength of a missing field.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The record type being assessed.</typeparam>
    /// <param name="aField">The wire field name, e.g. <c>why_missed</c>.</param>
    /// <param name="aRecords">The records in the segment.</param>
    /// <param name="aTimestampOf">Reads a record's ISO-8601 timestamp.</param>
    /// <param name="aValueOf">Reads the optional field; <c>null</c> means the record does not carry it.</param>
    /// <returns>The floor date, the eligible count, the predates count and the assessed count.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static FieldEligibility EligibilityFor<T>(
        string aField,
        IReadOnlyList<T> aRecords,
        Func<T, string?> aTimestampOf,
        Func<T, string?> aValueOf)
    {
        ArgumentNullException.ThrowIfNull(aField);
        ArgumentNullException.ThrowIfNull(aRecords);
        ArgumentNullException.ThrowIfNull(aTimestampOf);
        ArgumentNullException.ThrowIfNull(aValueOf);

        MetricsConstants.FieldSince.TryGetValue(aField, out var vSince);

        var vEligible = 0;
        var vPredates = 0;
        var vAssessed = 0;

        foreach (var vRecord in aRecords)
        {
            var vValue = aValueOf(vRecord);

            // A record that CARRIES the field is eligible whatever its date. This is the case BRD-116
            // exists for: most amendments in the estate complete `sort` on records written before the
            // field existed, and a floor that then removed them from the denominator would make the
            // amendment invisible — it would fold an answer in and immediately throw it away. The
            // reference states the same rule in the same words ("an older record completed by an amend
            // is sorted, whatever its date"), so the two agree by construction rather than by luck.
            if (vValue is null && PredatesField(aTimestampOf(vRecord), vSince))
            {
                vPredates++;
                continue;
            }

            vEligible++;
            if (vValue is not null)
            {
                vAssessed++;
            }
        }

        return new FieldEligibility(aField, vSince, vEligible, vPredates, vAssessed);
    }

    /// <summary>
    /// Says whether one record is inside a field's eligibility denominator (REQ-FN-076).
    /// </summary>
    /// <remarks>
    /// The public single-record form of the floor <see cref="EligibilityFor{T}"/> applies in bulk, so
    /// any other figure that has to respect the same floor reads it from this one table and this one
    /// code path rather than re-deriving the comparison. <c>escapes_missing_why</c> is the first such
    /// caller: an escape written before <c>why_missed</c> existed had no field to leave empty, which is
    /// not the same as leaving one empty, and counting it would overstate the warning against exactly
    /// the oldest records nobody can now complete.
    /// <para>
    /// This form takes no value, and deliberately does not carry
    /// <see cref="EligibilityFor{T}"/>'s carries-the-field escape: its callers ask about records that
    /// are <i>missing</i> the field, so a record that carries it is outside their numerator already and
    /// the escape could never change an answer.
    /// </para>
    /// </remarks>
    /// <param name="aField">The field name, e.g. <c>why_missed</c>.</param>
    /// <param name="aTimestamp">The record's ISO-8601 timestamp.</param>
    /// <returns><c>true</c> when the record is inside that field's denominator.</returns>
    public static bool IsEligibleForField(string aField, string? aTimestamp)
    {
        ArgumentNullException.ThrowIfNull(aField);

        MetricsConstants.FieldSince.TryGetValue(aField, out var vSince);

        return !PredatesField(aTimestamp, vSince);
    }

    /// <summary>
    /// Says whether a record was written before the field it is being read for existed.
    /// </summary>
    /// <param name="aTimestamp">The record's ISO-8601 timestamp; <c>null</c> or short counts as eligible.</param>
    /// <param name="aSince">The floor date <c>yyyy-MM-dd</c>, or <c>null</c> when the field has no floor.</param>
    /// <returns><c>true</c> when the record had no chance to carry the field.</returns>
    private static bool PredatesField(string? aTimestamp, string? aSince)
    {
        if (aSince is null || aTimestamp is null || aTimestamp.Length < aSince.Length)
        {
            return false;
        }

        return string.CompareOrdinal(aTimestamp[..aSince.Length], aSince) < 0;
    }

    /// <summary>
    /// The catch rate a late gate has earned the right to state.
    /// </summary>
    /// <param name="aRan">Records whose <c>gates_run</c> contains the gate.</param>
    /// <param name="aCaught">Failures the gate caught.</param>
    /// <returns><see cref="FigureKind.NotApplicable"/> when the gate has not run at all, <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> runs, else the rate.</returns>
    private static Figure CatchRate(int aRan, int aCaught)
    {
        if (aRan == 0 && aCaught == 0)
        {
            return Figure.NotApplicable();
        }

        return aRan < MetricsConstants.MinN
            ? Figure.InsufficientData(aRan)
            : Figure.Value(100.0 * aCaught / aRan, aRan, MetricsConstants.Pct(aCaught, aRan));
    }
}
