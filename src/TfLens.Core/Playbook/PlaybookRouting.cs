using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// Decides which telemetry path a connected repository is read through (REQ-FN-069, BRD-109).
/// </summary>
/// <remarks>
/// <para>
/// The framework tag and the telemetry layout are two different facts and this class is where they stop
/// being confused with each other. <see cref="UserRepo.Framework"/> is the <b>provenance</b> axis: it
/// says whose process produced the work, it is stored at connect time and every engine read takes it, so
/// Playbook figures never pool with TechieFlow figures (ADR-016, REQ-FN-055).
/// <see cref="UserRepo.Kind"/> is the <b>layout</b>: it says which telemetry directory the repository
/// actually committed.
/// </para>
/// <para>
/// A Playbook repository that has converged on schema v1 emits <c>docs/metrics/*.jsonl</c>. It is
/// therefore <c>Framework == playbook</c> with <c>Kind == techieflow</c>, and it needs no adapter, no new
/// parser and no new page — it flows through exactly the same code as a TechieFlow repository, tagged
/// <c>playbook</c>. That is the whole of BRD-109: a routing decision, not new code.
/// </para>
/// </remarks>
public static class PlaybookRouting
{
    /// <summary>
    /// Chooses the route for a repository from the telemetry layout it committed.
    /// </summary>
    /// <param name="aRepo">The connected repository row.</param>
    /// <returns>
    /// <see cref="TelemetryRoute.PlaybookAdapter"/> only when the repository's telemetry layout is the
    /// Playbook one; <see cref="TelemetryRoute.SchemaV1Streams"/> for every schema-v1 layout, whichever
    /// framework the repository is tagged with.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRepo"/> is <c>null</c>.</exception>
    public static TelemetryRoute RouteFor(UserRepo aRepo)
    {
        ArgumentNullException.ThrowIfNull(aRepo);
        return RouteForKind(aRepo.Kind);
    }

    /// <summary>
    /// Chooses the route from a detected telemetry layout alone.
    /// </summary>
    /// <param name="aKind">The detected layout — <c>techieflow</c> or <c>playbook</c>.</param>
    /// <returns>The route the layout implies.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The layout is not recognised.</exception>
    public static TelemetryRoute RouteForKind(string aKind) => aKind switch
    {
        FrameworkNames.TechieFlow => TelemetryRoute.SchemaV1Streams,
        FrameworkNames.Playbook => TelemetryRoute.PlaybookAdapter,
        _ => throw new ArgumentOutOfRangeException(nameof(aKind), aKind, "Unknown telemetry kind.")
    };

    /// <summary>
    /// Whether a repository needs the Playbook adapter at all.
    /// </summary>
    /// <param name="aRepo">The connected repository row.</param>
    /// <returns><c>true</c> only for repositories on the <c>events.ndjson</c> layout.</returns>
    public static bool UsesAdapter(UserRepo aRepo) => RouteFor(aRepo) == TelemetryRoute.PlaybookAdapter;

    /// <summary>
    /// The repository-relative telemetry directory to read.
    /// </summary>
    /// <param name="aRepo">The connected repository row.</param>
    /// <returns><c>docs/metrics</c> or <c>verification/telemetry</c>, from the layout — never from the framework tag.</returns>
    public static string TelemetryPathFor(UserRepo aRepo)
    {
        ArgumentNullException.ThrowIfNull(aRepo);
        return FrameworkNames.TelemetryPath(aRepo.Kind);
    }

    /// <summary>
    /// The stream wire names to fetch for a repository.
    /// </summary>
    /// <param name="aRepo">The connected repository row.</param>
    /// <returns>
    /// The four schema-v1 streams for a converged repository — the same list a TechieFlow repository
    /// gets — or the Playbook stream list for an <c>events.ndjson</c> repository.
    /// </returns>
    public static IReadOnlyList<string> StreamsFor(UserRepo aRepo)
    {
        ArgumentNullException.ThrowIfNull(aRepo);
        return FrameworkNames.Streams(aRepo.Kind);
    }
}
