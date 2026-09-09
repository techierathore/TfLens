namespace TfLens.Core.Contracts;

/// <summary>
/// The <b>Data quality</b> facts Coverage states about the records themselves (REQ-UI-039, BRD-127 as
/// amended 2026-09-08).
/// </summary>
/// <remarks>
/// <para>
/// Every member here is a <b>count</b>, never a rate and never a grade. They say how complete the stored
/// records are — which fields arrived after the records did, which values had to be worked out rather
/// than read, which values no vocabulary in <c>SCHEMA.md</c> recognises — and none of them belongs on the
/// <c>/misses</c> KPI row, where a figure about the data would read as a figure about the work.
/// </para>
/// <para>
/// Nothing here is a health failure. Both field-completeness counts are large today and will stay large,
/// because a field arriving after the records did is not a fault in any repository; that is precisely why
/// the figure lives on this page and beside the other provenance facts.
/// </para>
/// </remarks>
/// <param name="FieldCompleteness">Per repository, how many misses carry no <c>sort</c> and no <c>what</c>.</param>
/// <param name="Derived">Values worked out at read time rather than read from the record.</param>
/// <param name="UnrecognisedValues">Stored values that no <c>SCHEMA.md</c> vocabulary lists.</param>
/// <param name="Diagnostics">The miss stream's link-integrity and provenance counts.</param>
public sealed record CoverageDataQuality(
    IReadOnlyList<MissFieldCompleteness> FieldCompleteness,
    IReadOnlyList<DerivedValueCount> Derived,
    IReadOnlyList<UnrecognisedValueFact> UnrecognisedValues,
    MissStreamDiagnostics Diagnostics)
{
    /// <summary>What a framework with no miss stream reports — nothing, never a page of zeros.</summary>
    public static CoverageDataQuality Empty { get; } =
        new([], [], [], MissStreamDiagnostics.Empty);

    /// <summary>Stored values across every stream that no documented vocabulary lists.</summary>
    public int UnrecognisedRecordTotal => UnrecognisedValues.Sum(aFact => aFact.Records);
}

/// <summary>
/// How complete one repository's miss records are on the two fields added 2026-09-07 (BRD-127, BRD-172).
/// </summary>
/// <remarks>
/// <see cref="MissingSort"/> and <see cref="Eligible"/> are two different facts and are deliberately
/// stated side by side rather than pooled: a record written before the field existed <b>predates</b> it
/// and never <i>declined to answer it</i>. The difference between the two columns is how many records
/// could not have carried the field at all, which is why <c>/misses</c> reads <c>n of N sorted</c> rather
/// than a percentage of everything.
/// </remarks>
/// <param name="Repo"><c>owner/name</c> of the repository, or <c>All repos</c> on the total row.</param>
/// <param name="Misses">Miss records stored for it, after amendments are folded.</param>
/// <param name="MissingSort">Misses that carry no <c>sort</c>, whether or not they were eligible to.</param>
/// <param name="MissingWhat">Misses that carry no <c>what</c>, whether or not they were eligible to.</param>
/// <param name="Eligible">Misses written on or after the fields' 2026-09-07 floor.</param>
public sealed record MissFieldCompleteness(
    string Repo,
    int Misses,
    int MissingSort,
    int MissingWhat,
    int Eligible)
{
    /// <summary>Misses that could not have carried either field — reported in those words, never as unsorted.</summary>
    public int PredatesField => Misses - Eligible;
}

/// <summary>
/// One value the reader works out rather than reads, with the count of records it is worked out for
/// (BRD-179, BRD-180).
/// </summary>
/// <remarks>
/// Deriving is not backfilling: TfLens writes to no stream, the value is recomputed every time a figure
/// is built, and <c>Rebuild from raw</c> re-derives the same number. The count is stated <b>beside</b>
/// every figure built on it, because the alternative to deriving is worse than it looks — skipping the
/// runs counts them as zero time while still counting their tokens, and dropping the gates removes them
/// from the first-pass rate rather than failing it.
/// </remarks>
/// <param name="Field">The field, as the record spells it — e.g. <c>duration_s</c> on a run.</param>
/// <param name="How">The derivation rule, in words.</param>
/// <param name="Derivable">Records the rule answers for, none of which needs a guess.</param>
public sealed record DerivedValueCount(string Field, string How, int Derivable);

/// <summary>One stored value that no vocabulary in <c>SCHEMA.md</c> lists (BRD-181).</summary>
/// <remarks>
/// Shown and counted, never filtered away. A reader that dropped these to a known list would lose the
/// records silently and its totals would look entirely normal — which is the failure this product exists
/// to prevent. The value is carried exactly as the record spells it and is never coerced to the nearest
/// legal one.
/// </remarks>
/// <param name="Where">The stream and field it was observed on — e.g. <c>gates.verdict</c>.</param>
/// <param name="Value">The value exactly as the record carries it.</param>
/// <param name="Records">How many stored rows carry it.</param>
public sealed record UnrecognisedValueFact(string Where, string Value, int Records);

/// <summary>
/// The miss stream's link-integrity and provenance counts, surfaced rather than applied silently
/// (BRD-116, BRD-127, BRD-174, BRD-176, BRD-177).
/// </summary>
/// <param name="EscapesMissingWhy">Escapes that arrived with no <c>why_missed</c>.</param>
/// <param name="OrphanFixes"><c>miss-fix</c> records naming no stored miss.</param>
/// <param name="OrphanAmends">Amendments naming no stored miss, or a field off the allowlist.</param>
/// <param name="AmendmentsIgnored">Well-formed amendments that arrived at a field already carrying a value.</param>
/// <param name="AmendmentsFolded">Amendments that filled a <c>null</c> at read time.</param>
/// <param name="BackfilledMissesExcluded">Backfilled misses held out of every miss figure.</param>
/// <param name="BackfilledMissFixesExcluded">Backfilled fix records held out of every miss figure.</param>
/// <param name="ReviewRecords"><c>review</c> records read; a fourth kind, never counted as a miss.</param>
/// <param name="FrameworkOwnVerdicts">Gate records carrying <c>req_class: FR</c>, kept out of every application figure.</param>
public sealed record MissStreamDiagnostics(
    int EscapesMissingWhy,
    int OrphanFixes,
    int OrphanAmends,
    int AmendmentsIgnored,
    int AmendmentsFolded,
    int BackfilledMissesExcluded,
    int BackfilledMissFixesExcluded,
    int ReviewRecords,
    int FrameworkOwnVerdicts)
{
    /// <summary>What a framework with no miss stream reports.</summary>
    public static MissStreamDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Records that link to nothing — the whole link-integrity picture in one count.</summary>
    public int OrphanRecordTotal => OrphanFixes + OrphanAmends + AmendmentsIgnored;
}
