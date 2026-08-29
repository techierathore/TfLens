using TfLens.Core.Contracts;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// A miss stream spanning two project types, built to make the export's two shapes visibly different.
/// </summary>
/// <remarks>
/// <para>
/// The <c>misses</c> block in <c>tflens.json</c> is the shape <c>tf-metrics.sh</c> computes, which does
/// not segment the miss stream; the tables under it are TfLens's own per-<c>project_type</c> shape
/// (REQ-FN-077). A single-project-type fixture would make the two indistinguishable and would prove
/// nothing, so this one spreads four misses across <c>app</c> and <c>library</c> and chooses the values
/// so that the pooled figures cannot be read off either segment: the design-miss share is 25% pooled,
/// 33% in <c>app</c>, and refused outright in <c>library</c>, which holds one record.
/// </para>
/// <para>
/// Every record is dated on or after <c>why_missed</c>'s eligibility floor, so the eligibility split is
/// exercised with a known answer rather than left to whatever today's date happens to be.
/// </para>
/// </remarks>
public static class MissExportFixture
{
    /// <summary>The repository holding the <c>app</c> misses.</summary>
    public const string AppRepo = "acme/alpha";

    /// <summary>The repository holding the single <c>library</c> miss.</summary>
    public const string LibraryRepo = "acme/gamma";

    /// <summary>Timestamp every record carries — on the <c>why_missed</c> floor, so all four are eligible.</summary>
    private const string Ts = "2026-08-28T09:00:00Z";

    /// <summary>The store with the miss stream loaded on top of the engine fixtures.</summary>
    /// <returns>The store.</returns>
    public static FixtureTelemetryStore Store() =>
        ExportFixture.Store()
            .SeedMisses(
                ExportFixture.UserId,
                AppRepo,
                ExportFixture.Framework,
                AppMisses(),
                AppFixes())
            .SeedMisses(
                ExportFixture.UserId,
                LibraryRepo,
                ExportFixture.Framework,
                LibraryMisses(),
                LibraryFixes());

    /// <summary>Three <c>app</c> misses: one design escape, one declined, one nobody classified.</summary>
    /// <returns>The miss records.</returns>
    private static IReadOnlyList<MissRecord> AppMisses() =>
    [
        Miss("MISS-01", AppRepo, "app", "unspecified-gap", "owner", "instruction-ignored",
            "linked", "build-phase", "claude-opus-5", "flow-master"),
        Miss("MISS-02", AppRepo, "app", "wrong-behaviour", "agent-review", null,
            "linked", "build-phase", null, "flow-master"),
        Miss("MISS-03", AppRepo, "app", null, null, null, "inferred", null, null, null)
    ];

    /// <summary>One <c>library</c> miss — an escape with no <c>why_missed</c>, found in production.</summary>
    /// <returns>The miss records.</returns>
    private static IReadOnlyList<MissRecord> LibraryMisses() =>
    [
        Miss("MISS-04", LibraryRepo, "library", "other", "production", null,
            "linked", "verify-phase", "claude-sonnet-4", "verifier")
    ];

    /// <summary>Three <c>sole</c> fixes — enough to support a measured token figure, and no more.</summary>
    /// <returns>The fix records.</returns>
    private static IReadOnlyList<MissFixRecord> AppFixes() =>
    [
        // Each fix names the run that made it, because the cost divisor is DERIVED from how many
        // misses a run closed (2026-08-29) rather than read from the stored string. RUN-1 and RUN-2
        // each closed one miss; RUN-3 closed two — MISS-03 here and MISS-04 in the library repo —
        // which is what makes the shared column non-empty without stamping it by hand.
        Fix("MISS-01", AppRepo, "app", "Verified", "sole", 300, "claude-code", null, "RUN-1"),
        Fix("MISS-02", AppRepo, "app", "wont-fix", "sole", 600, "claude-code", null, "RUN-2"),
        Fix("MISS-03", AppRepo, "app", "deferred", "sole", 900, "claude-code", null, "RUN-3")
    ];

    /// <summary>One apportioned fix, on the only harness that measures dollars.</summary>
    /// <returns>The fix records.</returns>
    private static IReadOnlyList<MissFixRecord> LibraryFixes() =>
    [
        Fix("MISS-04", LibraryRepo, "library", "Verified", "shared:2", 400, "opencode", 0.5m, "RUN-3")
    ];

    /// <summary>Builds one miss record.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aRepo">The repository.</param>
    /// <param name="aProjectType">The declared project type.</param>
    /// <param name="aMissClass">What was missed, or <c>null</c> when nobody classified it.</param>
    /// <param name="aFoundBy">Who found it, or <c>null</c>.</param>
    /// <param name="aWhyMissed">Which practice failed, or <c>null</c>.</param>
    /// <param name="aConfidence">The attribution confidence; only <c>linked</c> reaches a per-origin figure.</param>
    /// <param name="aOriginPhase">The phase that should have produced the artifact, or <c>null</c>.</param>
    /// <param name="aOriginModel">The originating model, or <c>null</c>.</param>
    /// <param name="aOriginAgent">The originating agent persona, or <c>null</c>.</param>
    /// <returns>The record.</returns>
    private static MissRecord Miss(
        string aMissId,
        string aRepo,
        string aProjectType,
        string? aMissClass,
        string? aFoundBy,
        string? aWhyMissed,
        string aConfidence,
        string? aOriginPhase,
        string? aOriginModel,
        string? aOriginAgent) => new()
    {
        UserId = ExportFixture.UserId,
        Repo = aRepo,
        SourceSha = "fixture",
        Ts = Ts,
        App = "Fixture",
        ProjectType = aProjectType,
        Harness = "claude-code",
        MissId = aMissId,
        MissClass = aMissClass,
        FoundBy = aFoundBy,
        WhyMissed = aWhyMissed,
        OriginConfidence = aConfidence,
        OriginPhase = aOriginPhase,
        OriginModel = aOriginModel,
        OriginAgent = aOriginAgent
    };

    /// <summary>Builds one fix record.</summary>
    /// <param name="aMissId">The miss the fix names.</param>
    /// <param name="aRepo">The repository.</param>
    /// <param name="aProjectType">The declared project type.</param>
    /// <param name="aVerdict">The verdict after the fix.</param>
    /// <param name="aCostAttribution"><c>sole</c>, <c>shared:n</c> or <c>none</c>.</param>
    /// <param name="aTokensOut">Output tokens the repairing run spent.</param>
    /// <param name="aHarness">The harness that emitted the record.</param>
    /// <param name="aCostUsd">Measured dollars, on OpenCode only.</param>
    /// <returns>The record.</returns>
    private static MissFixRecord Fix(
        string aMissId,
        string aRepo,
        string aProjectType,
        string aVerdict,
        string aCostAttribution,
        int aTokensOut,
        string aHarness,
        decimal? aCostUsd,
        string aFixRunId) => new()
    {
        UserId = ExportFixture.UserId,
        Repo = aRepo,
        SourceSha = "fixture",
        Ts = Ts,
        App = "Fixture",
        ProjectType = aProjectType,
        Harness = aHarness,
        MissId = aMissId,
        FixCmd = "fix-issues",
        FixRunId = aFixRunId,
        FixAttempt = 1,
        VerdictAfter = aVerdict,
        CostAttribution = aCostAttribution,
        TokensOut = aTokensOut,
        CostUsd = aCostUsd,
        // A real emitted record always carries a scope; without one there is no window to divide
        // and the record is unattributable however it is labelled.
        TokensScope = "tree"
    };
}
