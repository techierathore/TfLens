using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// Reads the three stored phase tables and builds the report a page binds (REQ-FN-096 … REQ-FN-102).
/// </summary>
/// <remarks>
/// <para>
/// One entry point, so no surface has to assemble the cohorts for itself. Every guard the requirements
/// turn on — quarantine before aggregation, the four nested cohorts, the exclusions beside each figure,
/// the unsupported-harness state — lives behind this call rather than in a page, because a second
/// implementation of any of them would be a second chance to render a zero nobody measured.
/// </para>
/// <para>
/// The harness is read from the rows themselves and falls back to the caller's hint only when there are
/// none. That ordering matters for BRD-163: a repository with no phase rows and an unsupported harness
/// must render <i>unsupported</i>, which is a different fact from a supported harness that has simply
/// not been re-imported yet — and neither is a zero.
/// </para>
/// </remarks>
public static class PlaybookPhaseEffort
{
    /// <summary>
    /// Builds the Playbook phase-effort report for one user, optionally narrowed to one repository.
    /// </summary>
    /// <param name="aStore">The telemetry store.</param>
    /// <param name="aUserId">The AppManager user id — isolation is a parameter, not a filter (ADR-013).</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aHarness">The harness to judge support by when no row names one.</param>
    /// <param name="aCancellationToken">Cancels the reads.</param>
    /// <returns>The report; every figure is <c>unavailable</c> rather than zero when nothing qualified.</returns>
    public static async Task<PlaybookPhaseReport> ReadAsync(
        ITelemetryStore aStore,
        int aUserId,
        string? aRepo = null,
        string? aHarness = null,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aStore);

        var vExecutions = await aStore.ReadPhaseExecutionsAsync(aUserId, aRepo, aCancellationToken)
            .ConfigureAwait(false);
        var vModels = await aStore.ReadPhaseModelUsagesAsync(aUserId, aRepo, aCancellationToken)
            .ConfigureAwait(false);
        var vSubagents = await aStore.ReadPhaseSubagentsAsync(aUserId, aRepo, aCancellationToken)
            .ConfigureAwait(false);

        return PlaybookPhaseReport.Build(HarnessOf(vExecutions, aHarness), vExecutions, vModels, vSubagents);
    }

    /// <summary>
    /// Names the harness the rows came from, falling back to the caller's hint.
    /// </summary>
    /// <param name="aExecutions">The stored execution rows.</param>
    /// <param name="aHint">The harness the caller knows about, when it knows one.</param>
    /// <returns>The harness, or <c>null</c> when nothing named one — which renders unsupported.</returns>
    private static string? HarnessOf(IReadOnlyList<PbPhaseExecutionRecord> aExecutions, string? aHint) =>
        aExecutions.Select(aE => aE.SourceHarness).FirstOrDefault(aH => !string.IsNullOrWhiteSpace(aH))
        ?? aHint;
}
