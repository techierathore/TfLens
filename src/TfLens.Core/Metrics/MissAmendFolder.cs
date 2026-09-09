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
/// Three things make an amend an <b>orphan</b>: a field on neither <see cref="AmendableFields"/> nor
/// <see cref="AmendableFreeTextFields"/>, a value outside that field's closed vocabulary (or, for a
/// free-text field, no text at all), or a <c>miss_id</c> naming no known miss. An orphan is
/// counted and surfaced on Coverage and <b>never applied</b> — exactly as an orphan <c>miss-fix</c> is.
/// An amend that is well-formed but arrives at a field already carrying a value is neither applied nor
/// an orphan: it is <i>ignored</i>, and counted as such, because the producer's own emitter refuses the
/// same write out loud (SCHEMA.md §5.5.7).
/// </para>
/// </remarks>
public static class MissAmendFolder
{
    /// <summary>Wire field name of <see cref="MissRecord.WhyMissed"/> — which practice failed.</summary>
    public const string WhyMissedField = "why_missed";

    /// <summary>Wire field name of <see cref="MissRecord.Sort"/> — whose gap it was (added 2026-09-07).</summary>
    /// <remarks>
    /// The same constant <see cref="MissSorts.Field"/> declares, aliased here so a reader of this class
    /// sees the field beside its siblings. One spelling, one place.
    /// </remarks>
    public const string SortField = MissSorts.Field;

    /// <summary>Wire field name of <see cref="MissRecord.What"/> — the one free-text sentence.</summary>
    public const string WhatField = "what";

    /// <summary>
    /// The allowlist: which wire fields an amend may complete, and the closed vocabulary of each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field earns a place here only when it is (a) a closed-vocabulary <i>judgement</i> a reader can
    /// still make correctly later and (b) not derived by the emitter. <c>why_missed</c> qualifies;
    /// <c>found_gate</c> is a fact about a run that is over, and <c>origin_model</c>,
    /// <c>origin_confidence</c> and every token or cost field are emitter-derived and excluded outright
    /// (SCHEMA.md §5.5.7). Kept byte-for-byte in step with <c>AMENDABLE</c> in <c>tf-emit.sh</c>.
    /// </para>
    /// <para>
    /// <b>Extended 2026-09-08 (BRD-116) with <see cref="SortField"/>.</b> Most amendments in the estate
    /// today complete <c>sort</c> on records written before the field existed, which is exactly the case
    /// this class was written for. An amend carrying a <c>sort</c> value outside the four is an orphan
    /// like any other: counted, surfaced on Coverage, and <b>never coerced to the nearest legal one</b> —
    /// a silently corrected judgement is worse than a missing one, because nothing downstream can see it
    /// happened.
    /// </para>
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
            ],
            // The four values are declared once, in <see cref="MissSorts"/>, so the vocabulary an amend
            // is checked against and the vocabulary a stored value is checked against can never drift
            // apart. Two copies would be two places for the closed set to stop being closed (BRD-170).
            [SortField] = MissSorts.All
        };

    /// <summary>
    /// Amendable fields carrying <b>free prose</b> — there is no vocabulary to close, so the only check
    /// is that the amendment actually carries text (BRD-116, added 2026-09-08).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>what</c> is the miss stream's one free-text field: one sentence, in the owner's words. It is
    /// still a completion rather than a revision — it may fill a <c>null</c> and may never overwrite a
    /// value — so every other rule in this class applies to it unchanged. What cannot apply is the closed
    /// vocabulary, and an amend carrying an empty or whitespace-only <c>value</c> is an orphan for that
    /// reason: a blank sentence completes nothing and would make a record look answered.
    /// </para>
    /// <para>
    /// It is a <b>separate</b> collection rather than an empty vocabulary in
    /// <see cref="AmendableFields"/>, because an empty list already means "no legal value" and reading it
    /// as "every value is legal" would be the one mistake that turns the allowlist into a free-text back
    /// door for every field at once (SCHEMA.md §9, constraint 7).
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> AmendableFreeTextFields =
        new HashSet<string>(StringComparer.Ordinal) { WhatField };

    /// <summary>
    /// Says whether an amend may complete a wire field at all, by either rule.
    /// </summary>
    /// <param name="aField">The wire field name.</param>
    /// <returns><c>true</c> when the field is on the allowlist.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aField"/> is <c>null</c>.</exception>
    public static bool IsAmendable(string aField)
    {
        ArgumentNullException.ThrowIfNull(aField);

        return AmendableFields.ContainsKey(aField) || AmendableFreeTextFields.Contains(aField);
    }

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

        var vCompletions = new List<MissAmendCompletion>();
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
            vCompletions.Add(new MissAmendCompletion(vAmend.Repo, vAmend.MissId, vAmend.Field));
        }

        return new MissFoldResult(vFolded, vCompletions, vIgnored, vOrphans);
    }

    /// <summary>
    /// Says why an amendment can never be applied, or <c>null</c> when it is well-formed and linked.
    /// </summary>
    /// <param name="aAmend">The amendment being considered.</param>
    /// <param name="aIndexByKey">Where each known miss sits in the folded list.</param>
    /// <returns>An <see cref="MissAmendOrphanReasons"/> value, or <c>null</c>.</returns>
    private static string? Reject(MissAmendRecord aAmend, IReadOnlyDictionary<string, int> aIndexByKey)
    {
        if (AmendableFields.TryGetValue(aAmend.Field, out var vVocabulary))
        {
            // Closed vocabulary. A value outside it is an orphan and is NEVER mapped to the nearest
            // legal one: coercion would hide the disagreement inside a figure nobody could audit.
            if (aAmend.Value is null || !vVocabulary.Contains(aAmend.Value, StringComparer.Ordinal))
            {
                return MissAmendOrphanReasons.ValueOutsideVocabulary;
            }
        }
        else if (AmendableFreeTextFields.Contains(aAmend.Field))
        {
            // Free prose: the only thing there is to check is that it says something.
            if (string.IsNullOrWhiteSpace(aAmend.Value))
            {
                return MissAmendOrphanReasons.ValueOutsideVocabulary;
            }
        }
        else
        {
            return MissAmendOrphanReasons.FieldNotAllowlisted;
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
        SortField => aMiss.Sort,
        WhatField => aMiss.What,
        _ => null
    };

    /// <summary>Produces the parent with one amendable field filled in.</summary>
    /// <param name="aMiss">The parent miss, whose field is known to be <c>null</c>.</param>
    /// <param name="aAmend">The amendment, already validated against the allowlist and vocabulary.</param>
    /// <returns>A new record carrying the amended value.</returns>
    private static MissRecord Apply(MissRecord aMiss, MissAmendRecord aAmend) => aAmend.Field switch
    {
        WhyMissedField => aMiss with { WhyMissed = aAmend.Value },
        SortField => aMiss with { Sort = aAmend.Value },
        WhatField => aMiss with { What = aAmend.Value },
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
/// <param name="Completions">Every value an amendment completed — one entry per applied amendment.</param>
/// <param name="AmendmentsIgnored">Well-formed amendments that arrived at a field already carrying a value.</param>
/// <param name="Orphans">Amendments that could never be applied, with the reason each was refused.</param>
public sealed record MissFoldResult(
    IReadOnlyList<MissRecord> Misses,
    IReadOnlyList<MissAmendCompletion> Completions,
    int AmendmentsIgnored,
    IReadOnlyList<MissAmendOrphan> Orphans)
{
    /// <summary>Amendments that filled a <c>null</c> — parity key <c>amendments_applied</c>.</summary>
    public int AmendmentsApplied => Completions.Count;

    /// <summary>Orphan amendments — parity key <c>orphan_amends</c>.</summary>
    public int OrphanAmends => Orphans.Count;

    /// <summary>
    /// How many values an amendment completed for one wire field.
    /// </summary>
    /// <remarks>
    /// The per-field half of <see cref="AmendmentsApplied"/>. BRD-176 requires the count to sit beside
    /// the distribution it belongs to, and a single total cannot say whether the amendments completed
    /// the field being charted or a different one.
    /// </remarks>
    /// <param name="aField">The wire field name, e.g. <c>sort</c>.</param>
    /// <returns>The number of records whose value for that field came from an amendment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aField"/> is <c>null</c>.</exception>
    public int CompletedFor(string aField)
    {
        ArgumentNullException.ThrowIfNull(aField);

        return Completions.Count(aCompletion =>
            string.Equals(aCompletion.Field, aField, StringComparison.Ordinal));
    }

    /// <summary>What a fold over nothing returns.</summary>
    public static MissFoldResult Empty { get; } = new([], [], 0, []);
}

/// <summary>
/// One field value an amendment completed on a miss that had left it <c>null</c> (BRD-176).
/// </summary>
/// <remarks>
/// It names the record and the field and carries no value, because its whole purpose is to be
/// <i>counted</i> beside a distribution: a reader who folds amendments silently cannot tell a field that
/// was answered from one that was answered later, and the fold is invisible without this count.
/// </remarks>
/// <param name="Repo"><c>owner/name</c> of the repository the amendment and its parent came from.</param>
/// <param name="MissId">The miss whose field the amendment completed.</param>
/// <param name="Field">The wire field name that was completed.</param>
public sealed record MissAmendCompletion(string Repo, string MissId, string Field);
