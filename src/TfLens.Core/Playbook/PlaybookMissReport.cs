using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Playbook;

/// <summary>
/// What one Playbook miss export normalized into, plus the ingest diagnostics (REQ-FN-103, BRD-164).
/// </summary>
/// <remarks>
/// <see cref="Parsed"/> goes straight to <c>ITelemetryStore.UpsertAsync</c>; nothing else here reaches
/// the database. The counts exist so Coverage can say what an import skipped: a line TfLens could not
/// read is a fact about the data, and a fact about the data that only a log file knows is one nobody
/// acts on.
/// </remarks>
public sealed record PlaybookMissNormalization
{
    /// <summary>The three record lists, in file order, ready to upsert.</summary>
    public required ParseResult Parsed { get; init; }

    /// <summary>Non-blank lines read from the exporter's stdout.</summary>
    public required int Lines { get; init; }

    /// <summary>Lines whose <c>kind</c> was outside <c>miss</c> · <c>miss-fix</c> · <c>miss-amend</c>.</summary>
    public required int UnknownKinds { get; init; }

    /// <summary>
    /// Lines whose source-line hash had already been seen in this same export.
    /// </summary>
    /// <remarks>
    /// Expected rather than alarming: the exporter re-emits its whole file every run, so a re-import is
    /// the normal case. This counts only repeats <b>within one file</b>; a line repeated across two
    /// imports is collapsed by the store's partial unique index instead.
    /// </remarks>
    public required int DuplicateSourceLines { get; init; }

    /// <summary>Rows normalized across all three kinds.</summary>
    public int Records => Parsed.Misses.Count + Parsed.MissFixes.Count + Parsed.MissAmends.Count;
}

/// <summary>
/// The Playbook edition's miss block for one user and one framework (REQ-FN-103, REQ-FN-104, REQ-FN-105).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is where BRD-165 is enforced by shape.</b> It exposes
/// <see cref="ByFoundPhaseGate"/> — the Playbook's <i>process</i> gate — and has no member of any kind
/// that can hold a TechieFlow <i>assertion</i> gate, so a chart bound to this report cannot pool the two
/// however it is captioned. <see cref="ByItemId"/> is the other half of the same rule read the other way:
/// the Playbook's requirement axis is a genuine peer of TechieFlow's <c>req_id</c>, so it gets its own
/// column and its own distribution rather than being written into one.
/// </para>
/// <para>
/// Every figure below is computed <b>after</b> amendments were folded through
/// <see cref="MissAmendFolder"/> and <b>after</b> the Playbook guards ran, and the records each guard
/// refused leave the engine as counts with reasons rather than as a silence.
/// </para>
/// </remarks>
public sealed record PlaybookMissReport
{
    /// <summary>The provenance axis these rows were read on; always the Playbook.</summary>
    public required string Framework { get; init; }

    /// <summary>The misses with every legal amendment applied, in the order they were read.</summary>
    public required IReadOnlyList<MissRecord> Misses { get; init; }

    /// <summary>The orphan and overwrite diagnostics Coverage renders.</summary>
    public required PlaybookMissDiagnostics Diagnostics { get; init; }

    /// <summary>
    /// The <c>why_missed</c> floor: which records could have carried it, and how many did.
    /// </summary>
    /// <remarks>
    /// <c>FIELD_SINCE</c> is applied <b>before</b> the denominator, so a miss written before the field
    /// existed leaves it rather than being counted as unassessed. <see cref="WhyMissedAssessed"/> is the
    /// <c>n of N assessed</c> line the distribution must print on its face (BRD-166).
    /// </remarks>
    public required FieldEligibility WhyMissedEligibility { get; init; }

    /// <summary>Which practice failed, over the records that carry the field — never the miss count.</summary>
    public required IReadOnlyList<MissCategoryCount> WhyMissedDistribution { get; init; }

    /// <summary>
    /// Misses by the Playbook's requirement axis <c>item_id</c> (BRD-165).
    /// </summary>
    /// <remarks>
    /// Read from <see cref="MissRecord.ItemId"/> alone. A TechieFlow <c>req_id</c> is the same axis under
    /// another name and is reported by the TechieFlow engine under that name; the two are never summed
    /// into one distribution, because a row must always be able to say which edition wrote it.
    /// </remarks>
    public required IReadOnlyList<MissCategoryCount> ByItemId { get; init; }

    /// <summary>
    /// Misses by the Playbook's <b>process</b> gate <c>found_phase_gate</c> (BRD-165, BRD-74).
    /// </summary>
    /// <remarks>
    /// Read from <see cref="MissRecord.FoundPhaseGate"/> alone, and never from
    /// <see cref="MissRecord.FoundGate"/>. A process gate and an assertion gate are two genuinely
    /// different measurements, so merging them would produce a distribution whose rows are not comparable
    /// to each other — the same rule, and the same reason, as <c>phase_gate</c> versus <c>gate</c>.
    /// </remarks>
    public required IReadOnlyList<MissCategoryCount> ByFoundPhaseGate { get; init; }

    /// <summary>The records that may name a model or a tier, and the ones the guards refused.</summary>
    public required PlaybookAttributionSplit Attribution { get; init; }

    /// <summary>The three cost cohorts and the measured-dollar figure.</summary>
    public required PlaybookCostSplit Cost { get; init; }

    /// <summary>
    /// The <c>n of N assessed</c> line every optional-field distribution is read against (BRD-166).
    /// </summary>
    /// <remarks>
    /// Assessed over <i>eligible</i>, never over the record total: the records that predate the field are
    /// reported by <see cref="FieldEligibility.PredatesField"/> and belong to no denominator.
    /// </remarks>
    public string WhyMissedAssessed =>
        $"{WhyMissedEligibility.Assessed} of {WhyMissedEligibility.Eligible} assessed";
}

/// <summary>
/// The amendment and orphan diagnostics the Playbook edition surfaces on Coverage (REQ-FN-103, BRD-164).
/// </summary>
/// <remarks>
/// The producer folds its own amendments before it exports, so in the healthy case every count here is
/// zero. That is exactly why they are published: a non-zero count means TfLens and the producer disagree
/// about the same file, and an exclusion the reader cannot see is indistinguishable from a bug.
/// </remarks>
public sealed record PlaybookMissDiagnostics
{
    /// <summary>Amendments that filled a <c>null</c> — parity key <c>amendments_applied</c>.</summary>
    public required int AmendmentsApplied { get; init; }

    /// <summary>
    /// Well-formed amendments that arrived at a field already carrying a value.
    /// </summary>
    /// <remarks>
    /// These are the <b>overwrite</b> diagnostics BRD-164 asks for. An amend completes a record; it never
    /// alters a fact, so one that would overwrite is neither applied nor an orphan — it is ignored, and
    /// counted, because the producer's own emitter refuses the same write out loud.
    /// </remarks>
    public required int OverwriteAmendmentsIgnored { get; init; }

    /// <summary>Amendments that could never be applied, with the reason each was refused.</summary>
    public required IReadOnlyList<MissAmendOrphan> OrphanAmends { get; init; }

    /// <summary>Fix records naming a miss TfLens holds no row for.</summary>
    public required IReadOnlyList<PlaybookOrphanFix> OrphanFixes { get; init; }

    /// <summary>Orphan amendments — parity key <c>orphan_amends</c>.</summary>
    public int OrphanAmendCount => OrphanAmends.Count;

    /// <summary>Orphan fix records — parity key <c>orphan_fixes</c>.</summary>
    public int OrphanFixCount => OrphanFixes.Count;
}

/// <summary>One <c>miss-fix</c> whose <c>miss_id</c> names no miss TfLens holds (REQ-FN-103).</summary>
/// <remarks>
/// Carries an identity and nothing else — never a whole record — for the same reason
/// <see cref="MissAmendOrphan"/> does: a diagnostic is a pointer to a problem, not a second copy of the
/// stream.
/// </remarks>
/// <param name="Repo"><c>owner/name</c> of the repository it came from.</param>
/// <param name="MissId">The miss it named.</param>
/// <param name="FixRunId">The repairing run, or <c>null</c> when the record carried none.</param>
public sealed record PlaybookOrphanFix(string Repo, string MissId, string? FixRunId);

/// <summary>
/// The misses that may support a model or tier figure, and the ones the guards refused (REQ-FN-105).
/// </summary>
/// <remarks>
/// <b>There is no bucket here for a refused record.</b> <see cref="ByOriginModel"/> and
/// <see cref="ByOriginPhase"/> are computed over <see cref="Attributed"/> alone, and every attributed
/// record carries a non-<c>null</c> observed model, so no <c>unknown</c>, <c>not-recorded</c> or em-dash
/// row can appear beside real model names. An inferred or unknown origin is reported by
/// <see cref="Refused"/>, keyed on what was wrong with the <i>data</i>.
/// </remarks>
public sealed record PlaybookAttributionSplit
{
    /// <summary>Records passing all three conditions: <c>linked</c>, complete valid window, observed model.</summary>
    public required IReadOnlyList<MissRecord> Attributed { get; init; }

    /// <summary>How many records each guard reason held out of every per-origin figure.</summary>
    public required IReadOnlyList<PlaybookGuardRefusal> Refused { get; init; }

    /// <summary>Misses by observed origin model, over attributed records only.</summary>
    public required IReadOnlyList<MissCategoryCount> ByOriginModel { get; init; }

    /// <summary>Misses by origin phase, over attributed records only.</summary>
    public required IReadOnlyList<MissCategoryCount> ByOriginPhase { get; init; }

    /// <summary>Records every per-origin figure was computed from — parity key <c>attributed_n</c>.</summary>
    public int AttributedN => Attributed.Count;

    /// <summary>Records held out of every per-origin figure — parity key <c>attribution_excluded</c>.</summary>
    public int RefusedN => Refused.Sum(aEntry => aEntry.Records);
}

/// <summary>
/// The Playbook edition's cost answer: three cohorts that never merge (REQ-FN-105, BRD-166).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HeadlineTokens"/> is the existing <see cref="MissCost"/>, reused rather than re-invented:
/// it has no property that could hold a headline-plus-apportioned blend, so the wrong number is
/// unrepresentable rather than merely forbidden (ADR-019).
/// </para>
/// <para>
/// <b>Measured and estimated dollars never share a series or a total.</b> The only money members here
/// are measured — they read <c>MissFixRecord.CostUsd</c> and nothing else — and there is deliberately no
/// estimated-dollar member beside them to be added to. Rate-card <c>*_usd_estimate</c> values are
/// preserved in each record's overflow and are unreachable from any figure on this type.
/// </para>
/// </remarks>
public sealed record PlaybookCostSplit
{
    /// <summary>Output tokens per fix, split headline versus apportioned — never one blended number.</summary>
    public required MissCost HeadlineTokens { get; init; }

    /// <summary>Fix records admitted to the headline cohort: <c>sole</c> + valid window + <c>cost_status:"complete"</c>.</summary>
    public required int HeadlineRecords { get; init; }

    /// <summary>Fix records reported separately as apportioned: <c>shared:&lt;n&gt;</c> over a valid window.</summary>
    public required int ApportionedRecords { get; init; }

    /// <summary>Fix records excluded as <c>none</c> — a count that is never a divisor.</summary>
    public required int ExcludedRecords { get; init; }

    /// <summary>How many records each guard reason kept out of a cost cohort.</summary>
    public required IReadOnlyList<PlaybookGuardRefusal> Refused { get; init; }

    /// <summary>Measured dollars per headline fix, or an honest refusal below three records.</summary>
    public required Figure MeasuredUsdPerFix { get; init; }

    /// <summary>Measured dollars summed over headline records, or <c>null</c> when none were measured.</summary>
    public required decimal? MeasuredUsdTotal { get; init; }

    /// <summary>How many headline records carried a measured <c>cost_usd</c>.</summary>
    public required int MeasuredRecords { get; init; }
}
