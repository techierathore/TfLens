using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Folds <c>miss-amend</c> records into their parent <b>at read time</b> (REQ-FN-075, BRD-116, ADR-020).
/// </summary>
/// <remarks>
/// <para>
/// <b>Amendments are stored, not collapsed.</b> Folding at ingest would make the stored value depend on
/// the order files happened to arrive — TfLens ingests archived files from many machines, and a merged
/// stream can legitimately carry an amend and a later-written value in either order. Storing the rows
/// and folding here keeps <c>RebuildAsync</c> re-deriving identical values from <c>data/raw/</c> and
/// lets TfLens <b>re-check</b> the invariant rather than trust the producer to have enforced it.
/// </para>
/// <para>
/// The rule, applied oldest first: an amend may set a field that is currently <c>null</c>; it may
/// <b>never</b> overwrite a non-<c>null</c> value, including one an earlier amend set. Because the
/// parent is read before any amend is applied, the outcome is the same whichever order the two records
/// arrived in — which is the whole point.
/// </para>
/// <para>
/// Three things make an amend an <b>orphan</b>: a field off <see cref="AmendableFields"/>, a value
/// outside that field's closed vocabulary, or a <c>miss_id</c> naming no known miss. An orphan is
/// counted and surfaced on Coverage and <b>never applied</b> — exactly as an orphan <c>miss-fix</c> is.
/// An amend that is well-formed but arrives at a field already carrying a value is neither applied nor
/// an orphan: it is <i>ignored</i>, and counted as such, because the producer's own emitter refuses the
/// same write out loud (SCHEMA.md §5.5.7).
/// </para>
/// </remarks>
public static class MissAmendFolder
{
    /// <summary>Wire field name of <see cref="MissRecord.WhyMissed"/> — the only amendable field today.</summary>
    public const string WhyMissedField = "why_missed";

    /// <summary>
    /// The allowlist: which wire fields an amend may complete, and the closed vocabulary of each.
    /// </summary>
    /// <remarks>
    /// A field earns a place here only when it is (a) a closed-vocabulary <i>judgement</i> a reader can
    /// still make correctly later and (b) not derived by the emitter. <c>why_missed</c> qualifies;
    /// <c>found_gate</c> is a fact about a run that is over, and <c>origin_model</c>,
    /// <c>origin_confidence</c> and every token or cost field are emitter-derived and excluded outright
    /// (SCHEMA.md §5.5.7). Kept byte-for-byte in step with <c>AMENDABLE</c> in <c>tf-emit.sh</c>.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AmendableFields =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [WhyMissedField] =
            [
                "missing-checklist-item",
                "insufficient-verify-method",
                "code-audit-limitation",
                "ambiguous-acceptance",
                "dependency-not-declared",
                "instruction-ignored",
                "other"
            ]
        };

    /// <summary>
    /// Applies every amendment to its parent, oldest first, re-checking the null rule as it goes.
    /// </summary>
    /// <remarks>
    /// The input records are never mutated: each applied amendment produces a new
    /// <see cref="MissRecord"/>, and the returned list preserves the input order so a caller's own
    /// ordering (the store returns rows by <c>Ts</c>) survives the fold.
    /// </remarks>
    /// <param name="aMisses">The stored <c>miss</c> rows, exactly as read.</param>
    /// <param name="aAmends">The stored <c>miss-amend</c> rows, in any order.</param>
    /// <returns>The folded misses and the counts a reader has to be shown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aMisses"/> or <paramref name="aAmends"/> is <c>null</c>.</exception>
    public static MissFoldResult Fold(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissAmendRecord> aAmends)
    {
        ArgumentNullException.ThrowIfNull(aMisses);
        ArgumentNullException.ThrowIfNull(aAmends);

        var vFolded = aMisses.ToList();
        var vIndexByKey = BuildIndex(vFolded);

        var vApplied = 0;
        var vIgnored = 0;
        var vOrphans = new List<MissAmendOrphan>();

        // Oldest first. Ts is ISO-8601 UTC text, whose lexical order is its chronological order; the
        // secondary key keeps the outcome deterministic when two amendments share an instant.
        var vOrdered = aAmends
            .OrderBy(aA => aA.Ts, StringComparer.Ordinal)
            .ThenBy(aA => aA.MissId, StringComparer.Ordinal)
            .ThenBy(aA => aA.Field, StringComparer.Ordinal);

        foreach (var vAmend in vOrdered)
        {
            var vReason = Reject(vAmend, vIndexByKey);
            if (vReason is not null)
            {
                vOrphans.Add(new MissAmendOrphan(vAmend.Repo, vAmend.MissId, vAmend.Field, vAmend.Value, vReason));
                continue;
            }

            var vIndex = vIndexByKey[KeyOf(vAmend.Repo, vAmend.MissId)];
            var vParent = vFolded[vIndex];

            // The null-check, re-applied here rather than assumed. An amend completes a record; it never
            // alters a fact, and that has to hold whichever order the two records reached the archive.
            if (Current(vParent, vAmend.Field) is not null)
            {
                vIgnored++;
                continue;
            }

            vFolded[vIndex] = Apply(vParent, vAmend);
            vApplied++;
        }

        return new MissFoldResult(vFolded, vApplied, vIgnored, vOrphans);
    }

    /// <summary>
    /// Says why an amendment can never be applied, or <c>null</c> when it is well-formed and linked.
    /// </summary>
    /// <param name="aAmend">The amendment being considered.</param>
    /// <param name="aIndexByKey">Where each known miss sits in the folded list.</param>
    /// <returns>An <see cref="MissAmendOrphanReasons"/> value, or <c>null</c>.</returns>
    private static string? Reject(MissAmendRecord aAmend, IReadOnlyDictionary<string, int> aIndexByKey)
    {
        if (!AmendableFields.TryGetValue(aAmend.Field, out var vVocabulary))
        {
            return MissAmendOrphanReasons.FieldNotAllowlisted;
        }

        if (aAmend.Value is null || !vVocabulary.Contains(aAmend.Value, StringComparer.Ordinal))
        {
            return MissAmendOrphanReasons.ValueOutsideVocabulary;
        }

        return aIndexByKey.ContainsKey(KeyOf(aAmend.Repo, aAmend.MissId))
            ? null
            : MissAmendOrphanReasons.UnknownMiss;
    }

    /// <summary>Reads the parent's current value for an amendable wire field.</summary>
    /// <param name="aMiss">The parent miss.</param>
    /// <param name="aField">The wire field name, already known to be on the allowlist.</param>
    /// <returns>The stored value, or <c>null</c> when the field has not been filled.</returns>
    private static string? Current(MissRecord aMiss, string aField) => aField switch
    {
        WhyMissedField => aMiss.WhyMissed,
        _ => null
    };

    /// <summary>Produces the parent with one amendable field filled in.</summary>
    /// <param name="aMiss">The parent miss, whose field is known to be <c>null</c>.</param>
    /// <param name="aAmend">The amendment, already validated against the allowlist and vocabulary.</param>
    /// <returns>A new record carrying the amended value.</returns>
    private static MissRecord Apply(MissRecord aMiss, MissAmendRecord aAmend) => aAmend.Field switch
    {
        WhyMissedField => aMiss with { WhyMissed = aAmend.Value },
        _ => aMiss
    };

    /// <summary>Indexes the misses by repository and id, keeping the first of any duplicate key.</summary>
    /// <param name="aMisses">The misses being folded.</param>
    /// <returns>Where each key sits in the list.</returns>
    private static Dictionary<string, int> BuildIndex(IReadOnlyList<MissRecord> aMisses)
    {
        var vIndex = new Dictionary<string, int>(aMisses.Count, StringComparer.Ordinal);
        for (var vAt = 0; vAt < aMisses.Count; vAt++)
        {
            vIndex.TryAdd(KeyOf(aMisses[vAt].Repo, aMisses[vAt].MissId), vAt);
        }

        return vIndex;
    }

    /// <summary>
    /// The link key. Scoped to the repository, because a merged view spans several of them and a miss id
    /// is only promised to be unique within the app that minted it.
    /// </summary>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aMissId">The miss id.</param>
    /// <returns>The composite key.</returns>
    private static string KeyOf(string aRepo, string aMissId) => aRepo + "" + aMissId;
}

/// <summary>Why an amendment could never be applied (REQ-FN-075).</summary>
/// <remarks>These are the values <see cref="MissAmendOrphan.Reason"/> takes; Coverage renders them.</remarks>
public static class MissAmendOrphanReasons
{
    /// <summary>The <c>field</c> is not on <see cref="MissAmendFolder.AmendableFields"/>.</summary>
    public const string FieldNotAllowlisted = "field-not-allowlisted";

    /// <summary>The <c>value</c> is absent or outside that field's closed vocabulary.</summary>
    public const string ValueOutsideVocabulary = "value-outside-vocabulary";

    /// <summary>The <c>miss_id</c> names no miss TfLens holds — the same shape as an orphan <c>miss-fix</c>.</summary>
    public const string UnknownMiss = "unknown-miss";
}

/// <summary>One amendment that was counted and surfaced rather than applied (REQ-FN-075).</summary>
/// <param name="Repo"><c>owner/name</c> of the repository it came from.</param>
/// <param name="MissId">The miss it named.</param>
/// <param name="Field">The wire field it tried to complete.</param>
/// <param name="Value">The value it carried; a name and a value, never a whole record.</param>
/// <param name="Reason">One of <see cref="MissAmendOrphanReasons"/>.</param>
public sealed record MissAmendOrphan(string Repo, string MissId, string Field, string? Value, string Reason);

/// <summary>
/// The outcome of a read-time fold: the misses to compute over, and the counts a reader must be shown.
/// </summary>
/// <remarks>
/// The counts are part of the result rather than a log line because an exclusion the reader cannot see
/// is indistinguishable from a bug. <see cref="AmendmentsApplied"/> and <see cref="OrphanAmends"/> are
/// the producer's <c>amendments_applied</c> and <c>orphan_amends</c> parity keys.
/// </remarks>
/// <param name="Misses">The misses with every legal amendment applied, in the input order.</param>
/// <param name="AmendmentsApplied">Amendments that filled a <c>null</c> — parity key <c>amendments_applied</c>.</param>
/// <param name="AmendmentsIgnored">Well-formed amendments that arrived at a field already carrying a value.</param>
/// <param name="Orphans">Amendments that could never be applied, with the reason each was refused.</param>
public sealed record MissFoldResult(
    IReadOnlyList<MissRecord> Misses,
    int AmendmentsApplied,
    int AmendmentsIgnored,
    IReadOnlyList<MissAmendOrphan> Orphans)
{
    /// <summary>Orphan amendments — parity key <c>orphan_amends</c>.</summary>
    public int OrphanAmends => Orphans.Count;

    /// <summary>What a fold over nothing returns.</summary>
    public static MissFoldResult Empty { get; } = new([], 0, 0, []);
}
