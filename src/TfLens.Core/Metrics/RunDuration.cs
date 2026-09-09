using System.Globalization;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Derives a run's <c>duration_s</c> at read time from the two timestamps it already carries
/// (REQ-FN-112, BRD-179).
/// </summary>
/// <remarks>
/// <para>
/// <b>The bug this fixes counts a run's tokens and none of its time.</b> <c>duration_s</c> was added to
/// the stream after some records were written. Those records carry <c>started</c> and <c>ended</c> but
/// no duration, and a reader that sums <c>duration_s</c> scores them as <b>zero time while still
/// counting their tokens</b> — so a phase's time covers one set of runs and its tokens another. The
/// framework's own reset reported <b>16h49m</b> under that bug against a true <b>55h57m</b>.
/// </para>
/// <para>
/// <b>Arithmetic on two recorded facts, never a guess.</b> Where <c>ended</c> is absent too, SCHEMA.md's
/// own rule is that <c>ended</c> <i>is</i> the moment the record was written, which is exactly what
/// <c>ts</c> holds — so the fallback is the schema's definition rather than an approximation. A run that
/// carries no <c>started</c>, or whose timestamps will not parse, or whose window comes out negative, is
/// left exactly as it arrived: an unparseable pair is not a zero-length run.
/// </para>
/// <para>
/// <b>Read-time only.</b> TfLens writes to no stream (§3) and nothing is backfilled: the derivation is
/// re-done on every read and the count of it is published beside every time figure built on it
/// (<see cref="PhaseDuration.DerivedN"/>), because a total that grew because durations were derived is a
/// different claim from one read straight off the stream.
/// </para>
/// <para>
/// A run that already carries a duration is returned untouched — including one carrying zero, which the
/// oracle also leaves alone. Zero is the producer's measurement, and replacing it would be an edit
/// rather than a completion.
/// </para>
/// </remarks>
public static class RunDuration
{
    /// <summary>The <c>duration_derived</c> marker for a window closed by the record's own <c>ended</c>.</summary>
    public const string FromEnded = "ended";

    /// <summary>The <c>duration_derived</c> marker for a window closed by <c>ts</c>, SCHEMA.md's stand-in.</summary>
    public const string FromTs = "ts";

    /// <summary>The stream's timestamp format; the oracle parses exactly this shape.</summary>
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>
    /// Completes the durations the records themselves can answer for.
    /// </summary>
    /// <remarks>
    /// Backfilled runs are handed back untouched. They are already excluded from every effort figure —
    /// a reconstructed duration is a guess, and an effort report built on guesses cannot be defended
    /// when someone asks how it was measured (SCHEMA.md §6) — so deriving one would produce a number
    /// nothing may read.
    /// </remarks>
    /// <param name="aRuns">Every stored run record for the framework, live and backfilled.</param>
    /// <returns>The same records, with derivable durations filled in, and how many were derived.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRuns"/> is <c>null</c>.</exception>
    public static DerivedDurations Derive(IReadOnlyList<RunRecord> aRuns)
    {
        ArgumentNullException.ThrowIfNull(aRuns);

        var vRecords = new List<RunRecord>(aRuns.Count);
        var vCount = 0;

        foreach (var vRun in aRuns)
        {
            var vDerived = DerivedFor(vRun);

            if (vDerived is null)
            {
                vRecords.Add(vRun);
                continue;
            }

            vRecords.Add(vDerived);
            vCount++;
        }

        return vCount == 0 ? new DerivedDurations(aRuns, 0) : new DerivedDurations(vRecords, vCount);
    }

    /// <summary>
    /// The completed record for one run, or <c>null</c> when nothing could be worked out for it.
    /// </summary>
    /// <param name="aRun">The run.</param>
    /// <returns>The run carrying its derived duration, or <c>null</c> to leave it as it arrived.</returns>
    private static RunRecord? DerivedFor(RunRecord aRun)
    {
        if (aRun.Backfilled == true || aRun.DurationS is not null || string.IsNullOrWhiteSpace(aRun.Started))
        {
            return null;
        }

        var vHasEnded = !string.IsNullOrWhiteSpace(aRun.Ended);
        var vEnd = vHasEnded ? aRun.Ended : aRun.Ts;

        if (string.IsNullOrWhiteSpace(vEnd))
        {
            return null;
        }

        var vSeconds = SecondsBetween(aRun.Started!, vEnd!);

        return vSeconds is null
            ? null
            : aRun with { DurationS = vSeconds, DurationDerivedFrom = vHasEnded ? FromEnded : FromTs };
    }

    /// <summary>
    /// The whole seconds between two stream timestamps.
    /// </summary>
    /// <remarks>
    /// A negative window is refused rather than clamped: two timestamps in the wrong order are a
    /// data-quality finding, and a zero standing in for one would be the same coercion this whole
    /// derivation exists to undo.
    /// </remarks>
    /// <param name="aStarted">The <c>started</c> timestamp.</param>
    /// <param name="aEnded">The <c>ended</c> timestamp, or the <c>ts</c> standing in for it.</param>
    /// <returns>The duration in seconds, or <c>null</c> when the pair cannot be read.</returns>
    private static int? SecondsBetween(string aStarted, string aEnded)
    {
        if (!TryParse(aStarted, out var vStart) || !TryParse(aEnded, out var vEnd))
        {
            return null;
        }

        var vSeconds = (vEnd - vStart).TotalSeconds;

        return vSeconds is < 0 or > int.MaxValue ? null : (int)vSeconds;
    }

    /// <summary>Parses one stream timestamp as UTC.</summary>
    /// <param name="aValue">The timestamp.</param>
    /// <param name="aMoment">The parsed instant.</param>
    /// <returns><c>true</c> when the timestamp could be read.</returns>
    private static bool TryParse(string aValue, out DateTimeOffset aMoment) =>
        DateTimeOffset.TryParseExact(
            aValue,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out aMoment)
        || DateTimeOffset.TryParse(
            aValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out aMoment);
}

/// <summary>
/// Run records read with their durations completed, and how many that took (REQ-FN-112, BRD-179).
/// </summary>
/// <remarks>
/// The count travels with the records so a caller cannot hold the corrected durations without also
/// holding the figure to print beside every total built on them — the same technique as
/// <see cref="TokenWindow"/> and <see cref="FanoutObservation"/> (ADR-026), applied to time.
/// </remarks>
/// <param name="Records">Every record handed in, in the order handed in, with derivable durations filled.</param>
/// <param name="DerivedN">Live records whose duration this read worked out; never written back.</param>
public sealed record DerivedDurations(IReadOnlyList<RunRecord> Records, int DerivedN);
