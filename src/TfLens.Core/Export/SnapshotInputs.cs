using TfLens.Core.Contracts;

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
/// <param name="DatasetShas">Repository to commit SHA, so the exact dataset can be checked out (REQ-FN-062).</param>
/// <param name="Parity">The last recorded parity run, or <c>null</c> when none has ever passed.</param>
/// <param name="ParityStatus">One of the <see cref="ParityStatuses"/> constants for this parser version.</param>
/// <param name="ParityReason">
/// One of the <see cref="ParityReasons"/> constants — which of the three invalidating facts produced
/// <paramref name="ParityStatus"/>, so a reader can tell a parser change from a reference-script change
/// from a script that could not be hashed at all (REQ-FN-063).
/// </param>
/// <param name="RateCardPath">Where the repricing rates were read from, for provenance.</param>
/// <param name="GeneratedTs">ISO-8601 timestamp the snapshot was produced.</param>
internal sealed record SnapshotInputs(
    int UserId,
    string Framework,
    DateOnly Date,
    AnalysisResult Analysis,
    PlaybookAnalysis? Playbook,
    HarnessComparison Harness,
    RoutingAnalysis Routing,
    IReadOnlyList<KeyValuePair<string, string>> DatasetShas,
    ParityRecord? Parity,
    string ParityStatus,
    string ParityReason,
    string RateCardPath,
    string GeneratedTs);
