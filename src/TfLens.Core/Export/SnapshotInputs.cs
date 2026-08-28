using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Export;

/// <summary>
/// Everything one snapshot is rendered from, gathered once so both files describe the same instant.
/// </summary>
/// <remarks>
/// REQ-FN-056 requires <c>snapshot.md</c> and <c>tflens.json</c> to be written atomically for the same
/// analysis. Collecting the inputs into one immutable value first is how that is guaranteed: the
/// markdown and the JSON are two renderings of this record, so they cannot disagree even if a sync
/// completes between the two writes.
/// </remarks>
/// <param name="UserId">The AppManager user id the figures belong to.</param>
/// <param name="Framework">The provenance axis; one snapshot per framework (ADR-016).</param>
/// <param name="Date">The report date, which is also the folder name.</param>
/// <param name="Analysis">The engine's output — the parity surface, reproduced without reinterpretation.</param>
/// <param name="Playbook">
/// The Playbook-native report set, or <c>null</c> for a TechieFlow snapshot (REQ-FN-070).
/// </param>
/// <param name="Harness">The per-harness comparison, which has no parity oracle.</param>
/// <param name="Routing">The routing and repricing view, whose money figures are estimates.</param>
/// <param name="RepoOrigins">
/// The per-repository facts the engine's <see cref="AnalysisResult.PerRepo"/> does not carry — how the
/// source's data arrives, how many <c>miss</c> records it holds, and which project types its older
/// records still declare (REQ-FN-080, REQ-FN-087).
/// </param>
/// <param name="MissParity">
/// The miss figures in the shape the reference computes them — one bucket, not one per
/// <c>project_type</c> (REQ-FN-080).
/// </param>
/// <param name="MeasuredRework">
/// The measuring harness's money row computed over <c>sole</c> fix records only — the two
/// <c>cost_usd_*</c> keys, in the record set the reference computes them over (REQ-FN-080).
/// </param>
/// <param name="DatasetShas">Repository to commit SHA, so the exact dataset can be checked out (REQ-FN-062).</param>
/// <param name="Parity">The last recorded parity run, or <c>null</c> when none has ever passed.</param>
/// <param name="ParityStatus">One of the <see cref="ParityStatuses"/> constants for this parser version.</param>
/// <param name="ParityReason">
/// One of the <see cref="ParityReasons"/> constants — which of the three invalidating facts produced
/// <paramref name="ParityStatus"/>, so a reader can tell a parser change from a reference-script change
/// from a script that could not be hashed at all (REQ-FN-063).
/// </param>
/// <param name="RateCardPath">Where the repricing rates were read from, for provenance.</param>
/// <param name="Prices">
/// The operator's rate card, so a token count can be priced as an <b>estimate</b> — never as spend
/// (BRD-123, SCHEMA.md §4).
/// </param>
/// <param name="GeneratedTs">ISO-8601 timestamp the snapshot was produced.</param>
internal sealed record SnapshotInputs(
    int UserId,
    string Framework,
    DateOnly Date,
    AnalysisResult Analysis,
    PlaybookAnalysis? Playbook,
    HarnessComparison Harness,
    RoutingAnalysis Routing,
    IReadOnlyList<SnapshotRepoOrigin> RepoOrigins,
    MissSegmentFigures MissParity,
    MissHarnessCost? MeasuredRework,
    IReadOnlyList<KeyValuePair<string, string>> DatasetShas,
    ParityRecord? Parity,
    string ParityStatus,
    string ParityReason,
    string RateCardPath,
    RateCard Prices,
    string GeneratedTs);

/// <summary>
/// The per-repository facts the export carries beside the engine's own <see cref="PerRepoFacts"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourceKind"/> is <b>displayed and never divided on</b> (BRD-136, ADR-021). It is carried
/// here rather than on <see cref="PerRepoFacts"/> precisely so that no figure can reach it: the engine
/// never sees it, so no engine method can take it as a parameter and no result-type key can be split by
/// it. The value is the <b>stored</b> one — <see cref="SourceKinds.Api"/> or
/// <see cref="SourceKinds.Import"/> — not the <i>Synced</i> / <i>Imported</i> badge wording, so a copy
/// change never becomes a schema change (BRD-132).
/// </para>
/// <para>
/// <see cref="Misses"/> and <see cref="StaleProjectTypes"/> are the two per-repository keys the reference
/// emits that the engine's block does not carry, and they exist here for one reason: an absent key the
/// reference emits is a parity <c>MISSING</c> finding, and a MISSING key is always closed by implementing
/// it rather than by allow-listing it (BRD §13).
/// </para>
/// </remarks>
/// <param name="Repo"><c>owner/name</c> of the repository.</param>
/// <param name="SourceKind">The stored source kind — <c>api</c> or <c>import</c>.</param>
/// <param name="Misses">
/// Stored <c>miss</c> records for the repository, live <b>and</b> backfilled, exactly as the reference's
/// <c>per_repo[].misses</c> counts them. It is a coverage fact — how much of the stream arrived — not a
/// quality figure, which is why the backfilled half is not held out of it here.
/// </param>
/// <param name="StaleProjectTypes">
/// Project types the repository's <c>gates</c> and <c>runs</c> records still declare that differ from the
/// type the repository reads as today. Append-only streams keep the value they were written with, so a
/// reclassified project legitimately occupies two segments that §6 forbids pooling; the list says so
/// rather than hiding it.
/// </param>
internal sealed record SnapshotRepoOrigin(
    string Repo,
    string SourceKind,
    int Misses,
    IReadOnlyList<string> StaleProjectTypes);
