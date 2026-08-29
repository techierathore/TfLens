using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Builds in-memory <c>misses.jsonl</c> records for the miss figure tests, so each test states only the
/// fields it is about.
/// </summary>
/// <remarks>
/// Every optional defaults to <c>null</c> rather than to a plausible value: the whole point of these
/// figures is that an absent field is <i>not assessed</i> and never a zero, so a fixture that quietly
/// filled one in would hide the bug the test exists to catch.
/// </remarks>
public static class MissFixtures
{
    /// <summary>The user id every fixture record is attributed to.</summary>
    public const int UserId = 7;

    /// <summary>The repository every fixture record is read from.</summary>
    public const string Repo = "acme/alpha";

    private const string Sha = "fixture";

    /// <summary>
    /// Builds one <c>miss</c> record.
    /// </summary>
    /// <param name="aMissId">The link key and the dedupe key.</param>
    /// <param name="aMissClass">What was missed.</param>
    /// <param name="aWhyMissed">Which practice failed; <c>null</c> is <b>not assessed</b>.</param>
    /// <param name="aFoundBy">Who found it.</param>
    /// <param name="aOriginPhase">The command that should have produced the artifact correctly.</param>
    /// <param name="aOriginModel">The model of the originating run.</param>
    /// <param name="aOriginAgent">The agent persona that was running.</param>
    /// <param name="aOriginConfidence"><c>linked</c> | <c>inferred</c> | <c>unknown</c>, or <c>null</c>.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <param name="aProjectType">The declared project type.</param>
    /// <param name="aProjectTypeInferred">Whether that type was inferred.</param>
    /// <param name="aBackfilled">Whether the record was reconstructed rather than emitted live.</param>
    /// <param name="aRepo">The repository the record was read from.</param>
    /// <returns>The record.</returns>
    public static MissRecord Miss(
        string aMissId,
        string? aMissClass = null,
        string? aWhyMissed = null,
        string? aFoundBy = null,
        string? aOriginPhase = null,
        string? aOriginModel = null,
        string? aOriginAgent = null,
        string? aOriginConfidence = null,
        string aTs = "2026-08-28T09:00:00Z",
        string? aProjectType = "app",
        bool? aProjectTypeInferred = null,
        bool? aBackfilled = null,
        string aRepo = Repo) => new()
    {
        UserId = UserId,
        Repo = aRepo,
        SourceSha = Sha,
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = aProjectType,
        ProjectTypeInferred = aProjectTypeInferred,
        Backfilled = aBackfilled,
        MissId = aMissId,
        MissClass = aMissClass,
        WhyMissed = aWhyMissed,
        FoundBy = aFoundBy,
        OriginPhase = aOriginPhase,
        OriginModel = aOriginModel,
        OriginAgent = aOriginAgent,
        OriginConfidence = aOriginConfidence
    };

    /// <summary>
    /// Builds one <c>miss-fix</c> record.
    /// </summary>
    /// <param name="aMissId">The miss it repairs.</param>
    /// <param name="aVerdictAfter">The verdict the fix left behind.</param>
    /// <param name="aCostAttribution"><c>sole</c> | <c>shared:n</c> | <c>none</c>, or <c>null</c> for absent.</param>
    /// <param name="aTokensOut">Output tokens of the fix run's window.</param>
    /// <param name="aCostUsd">Measured spend; only ever meaningful on OpenCode.</param>
    /// <param name="aHarness">The harness that ran the fix.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <param name="aFixRunId">The repairing run; <c>null</c> is the deliberate <c>log-miss --fixed</c> path.</param>
    /// <param name="aFixAttempt">One more than the count of prior fixes.</param>
    /// <param name="aProjectType">The declared project type.</param>
    /// <param name="aBackfilled">Whether the record was reconstructed rather than emitted live.</param>
    /// <param name="aRepo">The repository the record was read from.</param>
    /// <returns>The record.</returns>
    public static MissFixRecord Fix(
        string aMissId,
        string? aVerdictAfter = "Verified",
        string? aCostAttribution = null,
        int? aTokensOut = null,
        decimal? aCostUsd = null,
        string? aHarness = null,
        string aTs = "2026-08-28T12:00:00Z",
        string? aFixRunId = "2026-08-28T11:00:00Z",
        int? aFixAttempt = 1,
        string? aTokensScope = "tree",
        string? aProjectType = "app",
        bool? aBackfilled = null,
        string aRepo = Repo) => new()
    {
        UserId = UserId,
        Repo = aRepo,
        SourceSha = Sha,
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = aProjectType,
        Backfilled = aBackfilled,
        Harness = aHarness,
        MissId = aMissId,
        FixRunId = aFixRunId,
        FixCmd = "fix-issues",
        FixAttempt = aFixAttempt,
        VerdictAfter = aVerdictAfter,
        CostAttribution = aCostAttribution,
        TokensOut = aTokensOut,
        CostUsd = aCostUsd,
        // A real emitted record always carries a scope; `null` and "none" both mean the window
        // could not be computed, which is what makes a record genuinely unattributable. The
        // fixture defaulted to null before 2026-08-29, which modelled a record no emitter writes.
        TokensScope = aTokensScope
    };

    /// <summary>
    /// Builds one <c>miss-amend</c> record.
    /// </summary>
    /// <param name="aMissId">The miss it completes.</param>
    /// <param name="aValue">The value to set.</param>
    /// <param name="aField">The wire field name being completed.</param>
    /// <param name="aTs">The record timestamp; amendments fold oldest first.</param>
    /// <param name="aRepo">The repository the record was read from.</param>
    /// <returns>The record.</returns>
    public static MissAmendRecord Amend(
        string aMissId,
        string? aValue,
        string aField = "why_missed",
        string aTs = "2026-08-29T09:00:00Z",
        string aRepo = Repo) => new()
    {
        UserId = UserId,
        Repo = aRepo,
        SourceSha = Sha,
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = "app",
        MissId = aMissId,
        Field = aField,
        Value = aValue
    };
}
