using System.Text.Json;
using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// The schema-2 §3.1 invariants, and the quarantine a failed one puts a row into (REQ-FN-096, BRD-155).
/// </summary>
/// <remarks>
/// <para>
/// <b>Quarantine happens before aggregation, not after.</b> A quarantined row is stored, displayed with
/// its reason, and excluded from <b>every</b> numeric aggregate. That ordering is the requirement: the
/// producer may retain zero-valued compatibility totals on an invalid row, so a consumer that reaches
/// the aggregate first gets a confident zero rather than an error — and a zero is indistinguishable from
/// a real one once it is inside a sum.
/// </para>
/// <para>
/// <b>Nothing is repaired.</b> No leg is recomputed from the others, no <c>ended_at</c> is inferred and
/// no total is corrected. TfLens reports what the producer emitted and says why it cannot be used
/// (§7 — <i>never silently repair</i>).
/// </para>
/// <para>
/// <b>Validation is a pure function of the stored row, re-applied at read time.</b> There is no
/// "quarantined" column to trust: the same predicate runs at ingest, where it writes its findings into
/// <see cref="PbPhaseExecutionRecord.DataQualityIssues"/> so the row can explain itself, and again over
/// the stored rows before any cohort is formed. A row that was written by an older build is therefore
/// judged by today's rules, which is the same reasoning that keeps miss amendments folding at read time
/// (ADR-020).
/// </para>
/// </remarks>
public static class PlaybookPhaseInvariants
{
    /// <summary>The prefix every reason TfLens found carries, so a producer issue stays distinguishable.</summary>
    public const string TfLensPrefix = "tflens:";

    /// <summary>The producer declared the row invalid.</summary>
    public const string ProducerInvalid = TfLensPrefix + "producer-invalid";

    /// <summary><c>tokens_in</c> is not <c>input + cache_read + cache_write</c>.</summary>
    public const string TokensInMismatch = TfLensPrefix + "tokens-in-not-sum-of-legs";

    /// <summary><c>tokens_out</c> is not <c>output + reasoning</c>.</summary>
    public const string TokensOutMismatch = TfLensPrefix + "tokens-out-not-sum-of-legs";

    /// <summary>Fewer sessions were spawned than contributed, which cannot be.</summary>
    public const string SpawnedBelowContributors = TfLensPrefix + "spawned-below-contributors";

    /// <summary>Observed active time falls outside the window it is supposed to sit inside.</summary>
    public const string ActiveOutsideWindow = TfLensPrefix + "observed-active-outside-window";

    /// <summary>An incomplete window carries an end boundary, a duration, or an end reason other than EOF.</summary>
    public const string IncompleteWindowNotEof = TfLensPrefix + "incomplete-window-not-eof";

    /// <summary>The window finalized no assistant turn, so it is incomplete rather than a free run.</summary>
    public const string NoFinalizedAssistantTurn = TfLensPrefix + "no-finalized-assistant-turn";

    /// <summary>The end reason an incomplete window must carry.</summary>
    private const string EofReason = "eof";

    /// <summary>
    /// Judges one execution against the producer's stated invariants.
    /// </summary>
    /// <remarks>
    /// An invariant whose inputs are absent is <b>not</b> a violation: a null leg means the producer did
    /// not capture it, and failing a row for that would quarantine sparse-but-honest data. Only values
    /// that are present and disagree are findings.
    /// </remarks>
    /// <param name="aExecution">The stored or freshly parsed execution row.</param>
    /// <returns>The verdict and every reason behind it, in a fixed order.</returns>
    public static PhaseValidation Validate(PbPhaseExecutionRecord aExecution)
    {
        ArgumentNullException.ThrowIfNull(aExecution);

        var vReasons = new List<PhaseQuarantineReason>();

        AddProducerVerdict(aExecution, vReasons);
        AddTokenSums(aExecution, vReasons);
        AddFanoutOrder(aExecution, vReasons);
        AddTimingBounds(aExecution, vReasons);
        AddWindowShape(aExecution, vReasons);
        AddAssistantTurn(aExecution, vReasons);

        return new PhaseValidation(vReasons.Count > 0, vReasons);
    }

    /// <summary>
    /// Writes the findings into the row so a stored row can state why it is quarantined.
    /// </summary>
    /// <remarks>
    /// The producer's own <c>issues</c> array is preserved first and verbatim; TfLens's codes are
    /// appended after it and are prefixed, so a reader can always tell which side found what. The array
    /// is a display artefact — no aggregate reads it, because every cohort re-runs
    /// <see cref="Validate(PbPhaseExecutionRecord)"/> over the row itself.
    /// </remarks>
    /// <param name="aExecution">The freshly parsed execution row.</param>
    /// <returns>The row with its merged issue list.</returns>
    public static PbPhaseExecutionRecord Annotate(PbPhaseExecutionRecord aExecution)
    {
        ArgumentNullException.ThrowIfNull(aExecution);

        var vFound = Validate(aExecution);

        if (!vFound.IsQuarantined && aExecution.DataQualityIssues is null)
        {
            return aExecution;
        }

        var vIssues = ProducerIssues(aExecution.DataQualityIssues);
        vIssues.AddRange(vFound.Reasons.Select(aR => aR.Code));

        return aExecution with { DataQualityIssues = JsonSerializer.Serialize(vIssues) };
    }

    /// <summary>Reads the producer's <c>issues</c> array back out of the stored JSON text.</summary>
    /// <param name="aIssues">The stored JSON array, or <c>null</c>.</param>
    /// <returns>The producer's issue strings, or an empty list when there were none.</returns>
    public static List<string> ProducerIssues(string? aIssues)
    {
        if (string.IsNullOrWhiteSpace(aIssues))
        {
            return [];
        }

        try
        {
            using var vDocument = JsonDocument.Parse(aIssues);

            return vDocument.RootElement.ValueKind != JsonValueKind.Array
                ? []
                : vDocument.RootElement.EnumerateArray()
                    .Select(aE => aE.ValueKind == JsonValueKind.String ? aE.GetString() : aE.GetRawText())
                    .Where(aE => !string.IsNullOrWhiteSpace(aE))
                    .Select(aE => aE!)
                    .Where(aE => !aE.StartsWith(TfLensPrefix, StringComparison.Ordinal))
                    .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Honours the producer's own verdict — <c>valid:false</c> quarantines the row outright.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aReasons">The accumulating reasons.</param>
    private static void AddProducerVerdict(
        PbPhaseExecutionRecord aExecution, List<PhaseQuarantineReason> aReasons)
    {
        if (aExecution.DataQualityValid == false)
        {
            aReasons.Add(new PhaseQuarantineReason(
                ProducerInvalid,
                "The producer marked this window invalid. Its compatibility totals may still hold "
                + "zeroes, so none of its numbers may enter an aggregate."));
        }
    }

    /// <summary>Checks the two compatibility sums against the five legs they are meant to total.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aReasons">The accumulating reasons.</param>
    private static void AddTokenSums(PbPhaseExecutionRecord aExecution, List<PhaseQuarantineReason> aReasons)
    {
        var vIn = Sum(aExecution.TokensInput, aExecution.TokensCacheRead, aExecution.TokensCacheWrite);
        var vOut = Sum(aExecution.TokensOutput, aExecution.TokensReasoning);

        if (aExecution.TokensIn is not null && vIn is not null && aExecution.TokensIn != vIn)
        {
            aReasons.Add(new PhaseQuarantineReason(
                TokensInMismatch,
                $"tokens_in is {aExecution.TokensIn} but input + cache_read + cache_write is {vIn}."));
        }

        if (aExecution.TokensOut is not null && vOut is not null && aExecution.TokensOut != vOut)
        {
            aReasons.Add(new PhaseQuarantineReason(
                TokensOutMismatch,
                $"tokens_out is {aExecution.TokensOut} but output + reasoning is {vOut}."));
        }
    }

    /// <summary>Checks that no more sessions contributed tokens than were ever spawned.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aReasons">The accumulating reasons.</param>
    private static void AddFanoutOrder(PbPhaseExecutionRecord aExecution, List<PhaseQuarantineReason> aReasons)
    {
        if (aExecution.SubagentsSpawned is not null
            && aExecution.SubagentsContributors is not null
            && aExecution.SubagentsSpawned < aExecution.SubagentsContributors)
        {
            aReasons.Add(new PhaseQuarantineReason(
                SpawnedBelowContributors,
                $"subagents.spawned is {aExecution.SubagentsSpawned} and subagents.contributors is "
                + $"{aExecution.SubagentsContributors}; a session cannot contribute without being spawned."));
        }
    }

    /// <summary>Checks <c>0 &lt;= observed_active_ms &lt;= elapsed_ms</c> on a complete window.</summary>
    /// <remarks>
    /// Only on a complete window: an open window has no end boundary, so it has no elapsed time for the
    /// observed activity to be compared against, and inventing one would be the repair this refuses.
    /// </remarks>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aReasons">The accumulating reasons.</param>
    private static void AddTimingBounds(PbPhaseExecutionRecord aExecution, List<PhaseQuarantineReason> aReasons)
    {
        if (aExecution.Complete != true || aExecution.ObservedActiveMs is null)
        {
            return;
        }

        var vActive = aExecution.ObservedActiveMs.Value;
        var vElapsed = aExecution.ElapsedMs;

        if (vActive < 0 || (vElapsed is not null && vActive > vElapsed))
        {
            aReasons.Add(new PhaseQuarantineReason(
                ActiveOutsideWindow,
                $"observed_active_ms is {vActive} against an elapsed window of "
                + $"{vElapsed?.ToString() ?? "null"}; observed activity cannot exceed the window it "
                + "was observed in."));
        }
    }

    /// <summary>Checks that an incomplete window is EOF-shaped with a null end and a null duration.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aReasons">The accumulating reasons.</param>
    private static void AddWindowShape(PbPhaseExecutionRecord aExecution, List<PhaseQuarantineReason> aReasons)
    {
        if (aExecution.Complete != false)
        {
            return;
        }

        var vIsEof = string.Equals(aExecution.EndReason, EofReason, StringComparison.Ordinal);

        if (!vIsEof || aExecution.EndedAt is not null || aExecution.ElapsedMs is not null)
        {
            aReasons.Add(new PhaseQuarantineReason(
                IncompleteWindowNotEof,
                "complete:false requires end_reason \"eof\" with a null ended_at and a null elapsed_ms. "
                + $"This row reports end_reason \"{aExecution.EndReason ?? "null"}\", ended_at "
                + $"\"{aExecution.EndedAt ?? "null"}\" and elapsed_ms "
                + $"{aExecution.ElapsedMs?.ToString() ?? "null"}."));
        }
    }

    /// <summary>Checks that the window finalized at least one assistant turn.</summary>
    /// <remarks>
    /// A start/end window with no finalized assistant turn is invalid or incomplete — it is <b>not</b> a
    /// valid zero-usage run, and displaying it as a free run is the exact confident-zero this whole class
    /// exists to prevent (§3.1, acceptance test 12).
    /// </remarks>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aReasons">The accumulating reasons.</param>
    private static void AddAssistantTurn(PbPhaseExecutionRecord aExecution, List<PhaseQuarantineReason> aReasons)
    {
        if (aExecution.Turns is null || aExecution.Turns <= 0)
        {
            aReasons.Add(new PhaseQuarantineReason(
                NoFinalizedAssistantTurn,
                "The window finalized no assistant turn, so it is invalid or incomplete rather than a "
                + "run that legitimately spent nothing."));
        }
    }

    /// <summary>Adds optional legs, yielding <c>null</c> unless every one of them was captured.</summary>
    /// <param name="aLegs">The legs to add.</param>
    /// <returns>The sum, or <c>null</c> when any leg was absent.</returns>
    private static long? Sum(params long?[] aLegs) =>
        aLegs.Any(aLeg => aLeg is null) ? null : aLegs.Sum(aLeg => aLeg!.Value);
}

/// <summary>One reason a row is quarantined, with the code that names it and the sentence that explains it.</summary>
/// <param name="Code">The stable code, written into the row's issue list.</param>
/// <param name="Explanation">The sentence the execution table renders beside the row.</param>
public sealed record PhaseQuarantineReason(string Code, string Explanation);

/// <summary>
/// The verdict on one execution row.
/// </summary>
/// <remarks>
/// A quarantined row is never dropped: <see cref="Reasons"/> is what the table shows instead of the
/// numbers, and the row stays visible precisely so nobody concludes the work never happened.
/// </remarks>
/// <param name="IsQuarantined">True when the row may not enter any numeric aggregate.</param>
/// <param name="Reasons">Every reason found, in a fixed order; empty on a clean row.</param>
public sealed record PhaseValidation(bool IsQuarantined, IReadOnlyList<PhaseQuarantineReason> Reasons);
