using System.Globalization;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Reads a run's <c>duration_s</c> at read time from the two timestamps it already carries
/// (REQ-FN-112, BRD-179; BRD-189 to BRD-192 for the counts).
/// </summary>
/// <remarks>
/// <para>
/// <b>The timestamps win.</b> A record stores a start, an end and a duration, and on a stream written
/// before the emitter checked them the third can contradict the first two. TechieBlog holds fourteen
/// such records — one storing <c>-166</c>, and thirteen storing a plausible round number (3600, 2700,
/// 1800, 900, 600) that bears no relation to their own clocks: one <c>refresh-status</c> record started
/// 20:05:00, ended 17:59:39 and stored 600. Only the one storing the negative was ever detectable by
/// reading the number alone. So the duration is taken from the record's own <c>started</c> and
/// <c>ended</c> whenever both parse, and a stored figure survives only where they cannot be read.
/// </para>
/// <para>
/// <b>An impossible record carries no duration at all.</b> Where <c>ended</c> precedes <c>started</c>
/// the record is excluded from every duration figure — nothing is substituted, clamped or guessed, and
/// a plausible stored number is refused exactly as a negative one is. The record itself is never edited
/// or deleted: the streams are append-only (SCHEMA.md §3), so it stays where it is and the exclusion is
/// published instead (<see cref="DerivedDurations.ImpossibleN"/>).
/// </para>
/// <para>
/// <b>No elapsed time is not corruption.</b> A run whose start and end fall in the same second recorded
/// no elapsed time, which is a different fact from a start after an end and has a different remedy.
/// Reporting the two as one made this repository look as though it held thirty corrupt records when it
/// held thirty runs that simply recorded nothing (<see cref="DerivedDurations.AbsentN"/>).
/// </para>
/// <para>
/// <b>Read-time only.</b> TfLens writes to no stream (§3) and nothing is backfilled: the derivation is
/// re-done on every read, and the counts of it are published beside every time figure built on it,
/// because a total that moved because stored figures were overridden is a different claim from one read
/// straight off the stream.
/// </para>
/// <para>
/// Every consumer goes through <see cref="UsableSeconds"/>, so two readers of the same stream cannot
/// disagree about the same record.
/// </para>
/// </remarks>
public static class RunDuration
{
    /// <summary>The <c>duration_derived</c> marker for a window closed by the record's own <c>ended</c>.</summary>
    public const string FromEnded = "ended";

    /// <summary>The <c>duration_derived</c> marker for a window closed by <c>ts</c>, SCHEMA.md's stand-in.</summary>
    public const string FromTs = "ts";

    /// <summary>
    /// <see cref="RunRecord.DurationQuality"/> for a record whose timestamps overrode a stored figure
    /// that disagreed with them by more than <see cref="AgreementToleranceSeconds"/> (BRD-192).
    /// </summary>
    public const string Recomputed = "recomputed";

    /// <summary>
    /// <see cref="RunRecord.DurationQuality"/> for a record whose <c>ended</c> precedes its
    /// <c>started</c>: it carries no duration and is excluded from every duration figure (BRD-190).
    /// </summary>
    public const string Impossible = "impossible";

    /// <summary>
    /// <see cref="RunRecord.DurationQuality"/> for a record that recorded no elapsed time — never
    /// corrupt, never a failure (BRD-191).
    /// </summary>
    public const string NoElapsedTime = "absent";

    /// <summary>
    /// How far a stored duration may differ from the record's own timestamps before it is overridden.
    /// </summary>
    /// <remarks>
    /// One second, so that ordinary rounding is never reported as a disagreement. A difference larger
    /// than this is not rounding: the thirteen TechieBlog records that provoked this rule are out by
    /// hours.
    /// </remarks>
    public const int AgreementToleranceSeconds = 1;

    /// <summary>The stream's timestamp format; the oracle parses exactly this shape.</summary>
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>
    /// The seconds between a run's own <c>started</c> and <c>ended</c>, or <c>null</c> when either
    /// cannot be read.
    /// </summary>
    /// <remarks>
    /// Deliberately signed: a negative span is the impossible record, and the caller must be able to
    /// tell it from an unreadable pair. <c>ts</c> is <b>not</b> consulted here — it stands in for an
    /// absent <c>ended</c> only in <see cref="Derive"/>, never in the comparison that overrides a stored
    /// figure, because <c>ts</c> is when the record was written rather than a measured end.
    /// </remarks>
    /// <param name="aRun">The run.</param>
    /// <returns>The signed span in seconds, or <c>null</c> when the pair cannot be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRun"/> is <c>null</c>.</exception>
    public static int? SpanOf(RunRecord aRun)
    {
        ArgumentNullException.ThrowIfNull(aRun);

        if (string.IsNullOrWhiteSpace(aRun.Started) || string.IsNullOrWhiteSpace(aRun.Ended))
        {
            return null;
        }

        return SecondsBetween(aRun.Started, aRun.Ended);
    }

    /// <summary>
    /// The one duration figure every consumer reads: the seconds this record may contribute, or
    /// <c>null</c> when it may contribute none.
    /// </summary>
    /// <remarks>
    /// The timestamps decide whenever they can be read — a positive span is the duration, and a span
    /// that is zero or negative means the record carries none. Only where they cannot be read at all
    /// does a positive stored <c>duration_s</c> answer.
    /// </remarks>
    /// <param name="aRun">The run.</param>
    /// <returns>The usable duration in seconds, or <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRun"/> is <c>null</c>.</exception>
    public static int? UsableSeconds(RunRecord aRun)
    {
        var vSpan = SpanOf(aRun);

        if (vSpan is not null)
        {
            return vSpan > 0 ? vSpan : null;
        }

        return aRun.DurationS is > 0 ? aRun.DurationS : null;
    }

    /// <summary>
    /// Reads every record's duration and reports what that took.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each returned record carries, in <c>DurationS</c>, exactly the seconds it may contribute — the
    /// span where the timestamps could be read, the stored figure where they could not, and
    /// <c>null</c> where the record carries no duration at all. Downstream code therefore cannot reach
    /// a stored figure the timestamps overrode, which is the whole point: the override happens once,
    /// here, rather than once per figure.
    /// </para>
    /// <para>
    /// Backfilled runs are handed back like any other and excluded upstream, where <c>runs_live</c> is
    /// decided. A reconstructed duration is a guess, and an effort report built on guesses cannot be
    /// defended when someone asks how it was measured (SCHEMA.md §6).
    /// </para>
    /// </remarks>
    /// <param name="aRuns">Every stored run record for the framework, live and backfilled.</param>
    /// <returns>The same records in the order handed in, their durations read, and the four counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRuns"/> is <c>null</c>.</exception>
    public static DerivedDurations Derive(IReadOnlyList<RunRecord> aRuns)
    {
        ArgumentNullException.ThrowIfNull(aRuns);

        var vRecords = new List<RunRecord>(aRuns.Count);
        var vDerived = 0;
        var vMeasured = 0;
        var vImpossible = 0;
        var vAbsent = 0;
        var vRecomputed = 0;

        foreach (var vRun in aRuns)
        {
            // A backfilled run is handed back exactly as it arrived and counted in nothing. Its window
            // was reconstructed rather than measured, so neither deriving a duration for it nor
            // publishing it as an exclusion would mean anything — an effort report built on guesses
            // cannot be defended when someone asks how it was measured (SCHEMA.md §6). Callers exclude
            // these upstream, where `runs_live` is decided; this is the guarantee for anyone who does not.
            if (vRun.Backfilled == true)
            {
                vRecords.Add(vRun);
                continue;
            }

            var vSpan = SpanOf(vRun);
            var vStored = vRun.DurationS;
            string? vDerivedFrom = null;

            // Where the record can answer for itself and has not, close the window with its own clocks.
            // `ended` may be absent on an older record; SCHEMA.md's own rule is that `ended` IS the
            // moment the record was written, which is exactly what `ts` holds. A negative result is
            // refused rather than clamped — an impossible pair is not a zero-length run.
            if (UsableSeconds(vRun) is null && !string.IsNullOrWhiteSpace(vRun.Started))
            {
                var vHasEnded = !string.IsNullOrWhiteSpace(vRun.Ended);
                var vEnd = vHasEnded ? vRun.Ended : vRun.Ts;
                var vSeconds = string.IsNullOrWhiteSpace(vEnd) ? null : SecondsBetween(vRun.Started, vEnd);

                if (vSeconds is >= 0)
                {
                    vStored = vSeconds;
                    vDerivedFrom = vHasEnded ? FromEnded : FromTs;
                    vDerived++;
                }
            }

            var vHasStored = vStored is > 0;
            string? vQuality = null;

            // The four counts. The first three partition the records exactly; the fourth is a subset of
            // the measured ones, saying how many of them had a stored figure overridden. The same verdict
            // rides on the record (DurationQuality) so a command phase can state its own share of each.
            if (vSpan is < 0)
            {
                vImpossible++;
                vQuality = Impossible;
            }
            else if (vSpan is 0)
            {
                vAbsent++;
                vQuality = NoElapsedTime;
            }
            else if (vSpan is null && !vHasStored)
            {
                vAbsent++;
                vQuality = NoElapsedTime;
            }
            else if (vSpan is not null && vHasStored
                     && Math.Abs(vSpan.Value - vStored!.Value) > AgreementToleranceSeconds)
            {
                vRecomputed++;
                vQuality = Recomputed;
            }

            var vUsable = vSpan is not null
                ? vSpan > 0 ? vSpan : null
                : vHasStored ? vStored : null;

            if (vUsable is not null)
            {
                vMeasured++;
            }

            vRecords.Add(vRun with
            {
                DurationS = vUsable,
                DurationDerivedFrom = vDerivedFrom,
                DurationQuality = vQuality
            });
        }

        return new DerivedDurations(vRecords, vDerived, vMeasured, vImpossible, vAbsent, vRecomputed);
    }

    /// <summary>
    /// The whole seconds between two stream timestamps.
    /// </summary>
    /// <param name="aStarted">The <c>started</c> timestamp.</param>
    /// <param name="aEnded">The <c>ended</c> timestamp, or the <c>ts</c> standing in for it.</param>
    /// <returns>The signed duration in seconds, or <c>null</c> when the pair cannot be read.</returns>
    private static int? SecondsBetween(string? aStarted, string? aEnded)
    {
        if (!TryParse(aStarted, out var vStart) || !TryParse(aEnded, out var vEnd))
        {
            return null;
        }

        var vSeconds = (vEnd - vStart).TotalSeconds;

        return vSeconds is < int.MinValue or > int.MaxValue ? null : (int)vSeconds;
    }

    /// <summary>Parses one stream timestamp as UTC.</summary>
    /// <param name="aValue">The timestamp.</param>
    /// <param name="aMoment">The parsed instant.</param>
    /// <returns><c>true</c> when the timestamp could be read.</returns>
    private static bool TryParse(string? aValue, out DateTimeOffset aMoment)
    {
        aMoment = default;

        return !string.IsNullOrWhiteSpace(aValue)
               && DateTimeOffset.TryParseExact(
                   aValue,
                   TimestampFormat,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out aMoment);
    }
}

/// <summary>
/// Run records with their durations read, and what that took (REQ-FN-112, BRD-179, BRD-189 to BRD-192).
/// </summary>
/// <remarks>
/// The counts travel with the records so a caller cannot hold the corrected durations without also
/// holding the figures to print beside every total built on them — the same technique as
/// <see cref="TokenWindow"/> and <see cref="FanoutObservation"/> (ADR-026), applied to time. A total
/// without its exclusions is just a different wrong number: TechieBlog's wall clock falls from 79.0 h
/// to 72.9 h under this rule, and nothing on the page would say why.
/// </remarks>
/// <param name="Records">Every record handed in, in the order handed in, durations read.</param>
/// <param name="DerivedN">
/// Records whose window this read closed from their own timestamps because they carried no usable
/// duration. Not a subset of <paramref name="MeasuredN"/>: a run that started and ended in the same
/// second is derived and still contributes nothing.
/// </param>
/// <param name="MeasuredN">Records that produced a usable duration — the total's own denominator.</param>
/// <param name="ImpossibleN">Records whose <c>ended</c> precedes their <c>started</c>, discarded.</param>
/// <param name="AbsentN">Records that recorded no elapsed time; never corrupt, never a failure.</param>
/// <param name="RecomputedN">
/// Records where the timestamps overrode a stored figure that disagreed with them by more than
/// <see cref="RunDuration.AgreementToleranceSeconds"/>. A subset of <paramref name="MeasuredN"/>.
/// </param>
public sealed record DerivedDurations(
    IReadOnlyList<RunRecord> Records,
    int DerivedN,
    int MeasuredN,
    int ImpossibleN,
    int AbsentN,
    int RecomputedN);
