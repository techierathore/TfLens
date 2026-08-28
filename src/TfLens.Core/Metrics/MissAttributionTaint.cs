using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The miss-stream sibling of <see cref="TaintSet"/> — the records a per-origin figure must not count
/// (REQ-FN-078, BRD-121).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TaintSet"/> keeps REQs whose <c>attempt</c> numbering restarted out of the live first-pass
/// rate. This keeps misses whose origin was <i>guessed</i> out of every per-origin-phase,
/// per-origin-model and per-origin-agent figure: only <see cref="Linked"/> records are counted, and
/// like the taint set the exclusion is both <b>applied and displayed</b> — the count and the reason
/// leave this class as data, not as a log line, because an exclusion the reader cannot see is
/// indistinguishable from a bug.
/// </para>
/// <para>
/// <see cref="MissRecord.OriginConfidence"/> is derived by <c>tf-emit.sh</c> and never written by an
/// agent, and the emitter forces <see cref="MissRecord.OriginModel"/> and
/// <see cref="MissRecord.OriginHarness"/> to <c>null</c> whenever its lookup fails (SCHEMA.md §5.5.1,
/// §6). The filter is therefore on a value the producer controls, not on an agent's self-assessment —
/// which is what makes the guarantee real rather than aspirational.
/// </para>
/// <para>
/// There is no parameter, flag or overload that returns the excluded records to a figure. Relaxing the
/// rule would mean editing this file (REQ-NFR-013).
/// </para>
/// </remarks>
public static class MissAttributionTaint
{
    /// <summary>The one <c>origin_confidence</c> value a per-origin figure may be computed from.</summary>
    public const string Linked = "linked";

    /// <summary>The bucket a record whose <c>origin_confidence</c> is absent is reported under.</summary>
    /// <remarks>
    /// Deliberately not <c>unknown</c>: that is a real value in the producer's vocabulary, and folding an
    /// absent field into it would report a record the emitter never classified as one the emitter
    /// classified as unclassifiable. A <c>null</c> stays <c>null</c>-shaped all the way to the page.
    /// </remarks>
    public const string NotRecorded = "not-recorded";

    /// <summary>Why the excluded records were excluded, in the words the page and the export both show.</summary>
    public const string ExclusionReason =
        "origin_confidence is not \"linked\" — the originating run was guessed rather than found, so the "
        + "record cannot support a per-phase, per-model or per-agent figure";

    /// <summary>
    /// Splits misses into the ones a per-origin figure may count and the ones it may not.
    /// </summary>
    /// <param name="aMisses">The segment's misses, already folded and already live-only.</param>
    /// <returns>The <c>linked</c> records, the excluded counts by confidence value, and the reason.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aMisses"/> is <c>null</c>.</exception>
    public static MissAttributionSet Partition(IEnumerable<MissRecord> aMisses)
    {
        ArgumentNullException.ThrowIfNull(aMisses);

        var vLinked = new List<MissRecord>();
        var vExcluded = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var vMiss in aMisses)
        {
            if (string.Equals(vMiss.OriginConfidence, Linked, StringComparison.Ordinal))
            {
                vLinked.Add(vMiss);
                continue;
            }

            var vKey = vMiss.OriginConfidence ?? NotRecorded;
            vExcluded[vKey] = vExcluded.GetValueOrDefault(vKey) + 1;
        }

        return new MissAttributionSet(
            vLinked,
            vExcluded.Select(aEntry => new MissAttributionExclusion(aEntry.Key, aEntry.Value)).ToList(),
            ExclusionReason);
    }
}

/// <summary>
/// The outcome of an attribution split: what a per-origin figure may count, and what it refused to.
/// </summary>
/// <remarks>
/// The refusal is part of the result rather than a side effect, exactly as
/// <see cref="MissFoldResult"/> carries its orphan counts. <see cref="AttributedN"/> and
/// <see cref="AttributionExcluded"/> are the producer's <c>attributed_n</c> and
/// <c>attribution_excluded</c> parity keys.
/// </remarks>
/// <param name="Linked">The records every per-phase, per-model and per-agent figure is computed from.</param>
/// <param name="ExcludedByConfidence">How many records each non-<c>linked</c> confidence value kept out.</param>
/// <param name="Reason">Why they were excluded — <see cref="MissAttributionTaint.ExclusionReason"/>.</param>
public sealed record MissAttributionSet(
    IReadOnlyList<MissRecord> Linked,
    IReadOnlyList<MissAttributionExclusion> ExcludedByConfidence,
    string Reason)
{
    /// <summary>Records a per-origin figure was computed from — parity key <c>attributed_n</c>.</summary>
    public int AttributedN => Linked.Count;

    /// <summary>Records held out of every per-origin figure — parity key <c>attribution_excluded</c>.</summary>
    public int AttributionExcluded => ExcludedByConfidence.Sum(aEntry => aEntry.Records);

    /// <summary>What a split over nothing returns.</summary>
    public static MissAttributionSet Empty { get; } = new([], [], MissAttributionTaint.ExclusionReason);
}
