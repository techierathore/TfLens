using TfLens.Core.Contracts;
using TfLens.Core.Playbook;

namespace TfLens.Core.Abstractions;

/// <summary>
/// Reads an AI-First-Playbook repository's <c>verification/telemetry</c> stream into <c>"PbEvent"</c>.
/// </summary>
/// <remarks>
/// The adapter exists only for repositories that have <b>not</b> converged on schema v1. A Playbook
/// repository that emits <c>docs/metrics/*.jsonl</c> is routed straight through the shared parser,
/// engine and pages by <c>PlaybookRouting</c> and never reaches this interface (REQ-FN-069, BRD-109).
/// </remarks>
public interface IPlaybookAdapter
{
    /// <summary>
    /// Fetches, archives, probes, parses and stores one repository's Playbook stream at one SHA.
    /// </summary>
    /// <param name="aRepo">The connected repository; must be routed to <see cref="TelemetryRoute.PlaybookAdapter"/>.</param>
    /// <param name="aSha">The commit SHA to read the files at.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>What was fetched, archived, observed and written.</returns>
    /// <exception cref="ArgumentException">The repository does not route to the adapter.</exception>
    Task<PlaybookIngestResult> IngestAsync(
        UserRepo aRepo,
        string aSha,
        CancellationToken aCancellationToken = default);
}

/// <summary>
/// Computes the Playbook-native report set from the separate <c>"PbEvent"</c> table.
/// </summary>
/// <remarks>
/// The Playbook counterpart of <see cref="IMetricsEngine"/>, and deliberately a separate interface:
/// nothing that reads TechieFlow assertion-gate data can be handed a Playbook process-gate result, so
/// the two axes cannot meet in a shared query, column or chart (SCHEMA.md §11, REQ-FN-066).
/// </remarks>
public interface IPlaybookReportBuilder
{
    /// <summary>
    /// Builds the report set for one user.
    /// </summary>
    /// <param name="aUserId">The AppManager user id — a required parameter, never a filter (ADR-013).</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's Playbook repositories.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The Playbook-native figures, carrying their own schema-status caveat.</returns>
    Task<PlaybookAnalysis> BuildAsync(
        int aUserId,
        string? aRepo = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Builds the schema-2 phase-efficiency report — the Playbook axis of <c>/effort</c> (BRD-162).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="BuildAsync"/> because it reads a different stream through different
    /// cohorts: <see cref="PlaybookAnalysis"/> comes from <c>events.ndjson</c>, this comes from the
    /// exporter's normalized <c>phase-metric</c> NDJSON arriving through the import mode (ADR-023), and
    /// the two have no shared denominator. Returning them together would invite a caller to read one's
    /// <c>n</c> against the other's figures.
    /// </para>
    /// <para>
    /// A harness with no normalized producer reports <b>unsupported</b>, never zero and never an empty
    /// measured figure (BRD-163) — a data gap and a harness that ran and spent nothing are different
    /// facts, and only one of them is a number.
    /// </para>
    /// </remarks>
    /// <param name="aUserId">The AppManager user id — a required parameter, never a filter (ADR-013).</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's Playbook repositories.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The phase report, with every figure gated by the cohort it belongs to (BRD-161).</returns>
    Task<PlaybookPhaseReport> BuildPhaseReportAsync(
        int aUserId,
        string? aRepo = null,
        CancellationToken aCancellationToken = default);
}
