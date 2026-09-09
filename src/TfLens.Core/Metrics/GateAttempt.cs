using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Derives <c>attempt</c> at read time for gate records that omit it (REQ-FN-113, BRD-180).
/// </summary>
/// <remarks>
/// <para>
/// <b><c>attempt</c> is <i>defined</i>, not judged.</b> SCHEMA.md §3.1 says it is one plus the number of
/// prior live gate records for the same <c>(project, req_id)</c> — a count over this very stream in
/// order. A record that omits it was being read as "no attempt", which drops it out of the first-pass
/// rate entirely rather than failing it: <b>the framework's own twelve requirement verdicts, all
/// passing, reported a first-pass rate of 0%</b>. Across the estate 80 gate records carry no attempt.
/// </para>
/// <para>
/// <b>Read-time only, and never a backfill.</b> Nothing here writes to a stream, a table or a raw
/// archive (§3): the derivation is re-done on every read, so a later sync of the same records produces
/// the same answer and no stored value ever drifts from it. A record that <i>carries</i> an attempt is
/// returned untouched — the producer's number always wins.
/// </para>
/// <para>
/// <b>Live records only.</b> The definition counts prior <i>live</i> records, and a backfilled record's
/// attempt is assumed rather than observed (SCHEMA.md §3.1), so backfilled records neither receive a
/// derived attempt nor advance the counter for a live one. That is the same boundary
/// <see cref="TaintSet"/> draws, one step earlier.
/// </para>
/// <para>
/// The order is the oracle's: <c>ts</c> then <c>run_id</c>, both ordinal, which makes the numbering a
/// property of the data rather than of the order the store happened to return rows in.
/// </para>
/// </remarks>
public static class GateAttempt
{
    /// <summary>
    /// Fills in the attempt every record that omits one is defined to have.
    /// </summary>
    /// <param name="aGates">Every gate record read for the user and framework, live and backfilled.</param>
    /// <returns>
    /// The same records — backfilled ones untouched, live ones carrying either their own
    /// <c>attempt</c> or a derived one flagged <see cref="GateRecord.AttemptDerived"/> — and the count
    /// of derivations, which is stated beside every rate built on them.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="aGates"/> is <c>null</c>.</exception>
    public static DerivedAttempts Derive(IReadOnlyList<GateRecord> aGates)
    {
        ArgumentNullException.ThrowIfNull(aGates);

        var vSeen = new Dictionary<ReqKey, int>();
        var vDerived = new Dictionary<GateRecord, GateRecord>(ReferenceEqualityComparer.Instance);
        var vCount = 0;

        foreach (var vGate in aGates
                     .Where(aGate => aGate.Backfilled != true)
                     .OrderBy(aGate => aGate.Ts, StringComparer.Ordinal)
                     .ThenBy(aGate => aGate.RunId ?? string.Empty, StringComparer.Ordinal))
        {
            var vKey = ReqKey.Of(vGate);
            var vOrdinal = vSeen.GetValueOrDefault(vKey) + 1;
            vSeen[vKey] = vOrdinal;

            if (vGate.Attempt is not null)
            {
                continue;
            }

            vDerived[vGate] = vGate with { Attempt = vOrdinal, AttemptDerived = true };
            vCount++;
        }

        if (vCount == 0)
        {
            return new DerivedAttempts(aGates, 0);
        }

        var vRecords = aGates
            .Select(aGate => vDerived.TryGetValue(aGate, out var vReplacement) ? vReplacement : aGate)
            .ToList();

        return new DerivedAttempts(vRecords, vCount);
    }
}

/// <summary>
/// Gate records read with their attempts completed, and how many that took (REQ-FN-113, BRD-180).
/// </summary>
/// <remarks>
/// The count travels with the records for the same reason <see cref="TokenWindow.MeasuredN"/> travels
/// with its tokens: a first-pass rate that moved because attempts were derived is a different claim from
/// one read straight off the stream, and the reader is told rather than left to assume. A caller
/// therefore cannot hold the corrected records without also holding the number to print beside the rate.
/// </remarks>
/// <param name="Records">
/// Every record handed in, in the order handed in — backfilled ones untouched, live ones carrying an
/// attempt.
/// </param>
/// <param name="DerivedN">Live records whose attempt this read worked out; never written back.</param>
public sealed record DerivedAttempts(IReadOnlyList<GateRecord> Records, int DerivedN);
