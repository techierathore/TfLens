using System.Text.Json;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Builds in-memory stream records for the rule tests, so each test states only the fields it is about.
/// </summary>
public static class GateFixtures
{
    private const int UserId = 7;
    private const string Repo = "acme/alpha";
    private const string Sha = "fixture";

    /// <summary>
    /// Builds one gate record.
    /// </summary>
    /// <param name="aReqId">The requirement the verdict is about.</param>
    /// <param name="aAttempt">Attempt number; first-pass rate counts only <c>1</c>.</param>
    /// <param name="aVerdict">The verdict.</param>
    /// <param name="aGate">The gate that produced it, or <c>null</c> when nothing failed.</param>
    /// <param name="aGatesRun">The gates that ran, for late-gate coverage.</param>
    /// <param name="aProjectType">The declared project type.</param>
    /// <param name="aProjectTypeInferred">Whether the project type was inferred.</param>
    /// <param name="aBackfilled">Whether the record was reconstructed rather than emitted live.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <param name="aRepo">The repository the record was read from.</param>
    /// <returns>The record.</returns>
    public static GateRecord Gate(
        string? aReqId = "REQ-FN-001",
        int? aAttempt = 1,
        string? aVerdict = "Verified",
        string? aGate = null,
        IReadOnlyList<string>? aGatesRun = null,
        string? aProjectType = "app",
        bool? aProjectTypeInferred = null,
        bool? aBackfilled = null,
        string aTs = "2026-08-01T00:00:00Z",
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
        ReqId = aReqId,
        ReqClass = "FN",
        Attempt = aAttempt,
        Verdict = aVerdict,
        Gate = aGate,
        GatesRun = aGatesRun is null ? null : JsonSerializer.Serialize(aGatesRun)
    };

    /// <summary>
    /// Builds one run record.
    /// </summary>
    /// <param name="aCmd">The phase command that ran.</param>
    /// <param name="aMode">The run mode.</param>
    /// <param name="aDurationS">Wall-clock duration in seconds.</param>
    /// <param name="aReqsCount">REQs touched.</param>
    /// <returns>The record.</returns>
    public static RunRecord Run(string? aCmd = "build-phase", string? aMode = "build", int? aDurationS = 3600, int? aReqsCount = 4) => new()
    {
        UserId = UserId,
        Repo = Repo,
        SourceSha = Sha,
        Ts = "2026-08-01T00:00:00Z",
        App = "AlphaApp",
        ProjectType = "app",
        Cmd = aCmd,
        Mode = aMode,
        DurationS = aDurationS,
        ReqsCount = aReqsCount
    };

    /// <summary>
    /// Builds one session record.
    /// </summary>
    /// <param name="aSessionId">The harness session identifier.</param>
    /// <param name="aInputTokens">Input tokens.</param>
    /// <param name="aOutputTokens">Output tokens.</param>
    /// <returns>The record.</returns>
    public static SessionRecord Session(string aSessionId, int? aInputTokens, int? aOutputTokens) => new()
    {
        UserId = UserId,
        Repo = Repo,
        SourceSha = Sha,
        Ts = "2026-08-01T00:00:00Z",
        App = "AlphaApp",
        ProjectType = "app",
        SessionId = aSessionId,
        InputTokens = aInputTokens,
        OutputTokens = aOutputTokens
    };

    /// <summary>
    /// Builds one commit record.
    /// </summary>
    /// <param name="aSha">The commit SHA — the dedupe key within a repository.</param>
    /// <param name="aTs">The commit timestamp.</param>
    /// <param name="aRepo">The repository the commit belongs to.</param>
    /// <returns>The record.</returns>
    public static CommitRecord Commit(string aSha, string aTs, string aRepo = Repo) => new()
    {
        UserId = UserId,
        Repo = aRepo,
        SourceSha = Sha,
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = "app",
        Sha = aSha
    };
}
