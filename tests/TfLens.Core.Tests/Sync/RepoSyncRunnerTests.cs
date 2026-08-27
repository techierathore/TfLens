using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Services.Sync;

namespace TfLens.Core.Tests.Sync;

/// <summary>Covers the one sync code path: SHA skip, raw-archive ordering, error isolation and state.</summary>
public sealed class RepoSyncRunnerTests : IDisposable
{
    private const string Sha = "abc123def4567890";
    private const string RunsText = "{\"v\":1,\"cmd\":\"build\"}\n{\"v\":1,\"cmd\":\"verify\"}\n";
    private const string GatesText = "{\"v\":1,\"req\":\"REQ-FN-021\"}\n";

    private readonly string objDataRoot =
        Path.Combine(Path.GetTempPath(), "tflens-sync-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Removes the temporary data root the archive was written under.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, recursive: true);
        }
    }

    /// <summary>An unchanged telemetry SHA skips the repository without fetching a single file (REQ-FN-021).</summary>
    [Fact]
    public async Task UnchangedShaFetchesNoFiles()
    {
        var vHarness = BuildHarness();
        vHarness.Store.States["2|techierathore/TfLens"] = new SyncState
        {
            UserId = 2,
            Repo = "techierathore/TfLens",
            LastSha = Sha,
            LastSyncTs = "2026-01-01T00:00:00Z",
            RunsCount = 7
        };

        var vReport = await vHarness.Runner.SyncAsync(2);

        vReport.SkippedCount.Should().Be(1);
        vHarness.Fetcher.ShaCalls.Should().ContainSingle();
        vHarness.Fetcher.FileCalls.Should().BeEmpty();
        vHarness.Parser.Calls.Should().BeEmpty();
    }

    /// <summary>A skip touches only the last-sync timestamp and leaves the SHA and counts alone (REQ-FN-021).</summary>
    [Fact]
    public async Task SkipTouchesOnlyTheTimestamp()
    {
        var vHarness = BuildHarness();
        vHarness.Store.States["2|techierathore/TfLens"] = new SyncState
        {
            UserId = 2,
            Repo = "techierathore/TfLens",
            LastSha = Sha,
            LastSyncTs = "2020-01-01T00:00:00Z",
            RunsCount = 7,
            LastError = null
        };

        await vHarness.Runner.SyncAsync(2);

        var vState = vHarness.Store.States["2|techierathore/TfLens"];
        vState.LastSha.Should().Be(Sha);
        vState.RunsCount.Should().Be(7);
        vState.LastSyncTs.Should().NotBe("2020-01-01T00:00:00Z");
    }

    /// <summary>The raw archive is on disk before the parser is handed the text (REQ-FN-027).</summary>
    [Fact]
    public async Task ArchiveIsWrittenBeforeTheParse()
    {
        var vHarness = BuildHarness();
        var vSeen = new Dictionary<StreamKind, bool>();
        vHarness.Parser.OnParse = aStream => vSeen[aStream] = File.Exists(ArchivePath(aStream));

        await vHarness.Runner.SyncAsync(2);

        vSeen[StreamKind.Runs].Should().BeTrue();
        vSeen[StreamKind.Gates].Should().BeTrue();
    }

    /// <summary>The archived bytes are byte-identical to what the fetcher returned (REQ-FN-027).</summary>
    [Fact]
    public async Task ArchivedBytesAreVerbatim()
    {
        var vHarness = BuildHarness();

        await vHarness.Runner.SyncAsync(2);

        var vPath = ArchivePath(StreamKind.Runs);
        File.Exists(vPath).Should().BeTrue();

        var vArchived = await File.ReadAllTextAsync(vPath);
        vArchived.Should().Be(RunsText);
    }

    /// <summary>A parser exception leaves the archive intact so a rebuild can still replay it (REQ-FN-027).</summary>
    [Fact]
    public async Task ArchiveSurvivesAParserFailure()
    {
        var vHarness = BuildHarness();
        vHarness.Parser.ThrowOnStream = StreamKind.Runs;

        var vReport = await vHarness.Runner.SyncAsync(2);

        vReport.ErrorCount.Should().Be(1);
        File.Exists(ArchivePath(StreamKind.Runs)).Should().BeTrue();
    }

    /// <summary>A repository missing one stream syncs successfully with that stream at zero (REQ-FN-022).</summary>
    [Fact]
    public async Task AbsentStreamIsZeroNotAnError()
    {
        var vHarness = BuildHarness();

        var vReport = await vHarness.Runner.SyncAsync(2);

        vReport.UpdatedCount.Should().Be(1);
        var vState = vHarness.Store.States["2|techierathore/TfLens"];
        vState.LastError.Should().BeNull();
        vState.SessionsCount.Should().Be(0);
        vState.CommitsCount.Should().Be(0);
    }

    /// <summary>A failing repository is recorded and the remaining repositories still sync (REQ-FN-023).</summary>
    [Fact]
    public async Task OneFailingRepositoryDoesNotStopTheOthers()
    {
        var vHarness = BuildHarness();
        vHarness.Store.Repos.Add(BuildRepo(2, "techierathore", "TrBlazeUI"));
        vHarness.Fetcher.Failures["techierathore/TrBlazeUI"] =
            new HttpRequestException("boom", null, System.Net.HttpStatusCode.Unauthorized);

        var vReport = await vHarness.Runner.SyncAsync(2);

        vReport.ErrorCount.Should().Be(1);
        vReport.UpdatedCount.Should().Be(1);
        vHarness.Store.States["2|techierathore/TrBlazeUI"].LastError.Should().StartWith("HTTP 401");
        vHarness.Store.States["2|techierathore/TfLens"].LastError.Should().BeNull();
    }

    /// <summary>A token inside a failure message never reaches the stored error (REQ-FN-023).</summary>
    [Fact]
    public async Task TokenInAFailureNeverReachesLastError()
    {
        const string vToken = "ghp_SuperSecretTokenValue1234567890";
        var vHarness = BuildHarness();
        vHarness.Fetcher.Failures["techierathore/TfLens"] =
            new InvalidOperationException($"GET https://x:{vToken}@api.github.com/repos failed with Bearer {vToken}");

        var vReport = await vHarness.Runner.SyncAsync(2);

        var vError = vHarness.Store.States["2|techierathore/TfLens"].LastError!;
        vError.Should().NotContain(vToken);
        vError.Should().NotContain("api.github.com");
        vReport.Results.Single().Error.Should().NotContain(vToken);
    }

    /// <summary>The poller covers every user; Sync now covers only the caller (REQ-FN-018).</summary>
    [Fact]
    public async Task PollerCoversEveryUserAndSyncNowOnlyTheCaller()
    {
        var vHarness = BuildHarness();
        vHarness.Store.Repos.Add(BuildRepo(9, "otheruser", "OtherRepo"));
        vHarness.Fetcher.Shas["otheruser/OtherRepo"] = Sha;

        var vCallerReport = await vHarness.Runner.SyncAsync(2);
        var vPollerReport = await vHarness.Runner.SyncAsync(null);

        vCallerReport.Results.Should().ContainSingle().Which.Repo.Should().Be("techierathore/TfLens");
        vPollerReport.Results.Select(aR => aR.Repo)
            .Should().BeEquivalentTo(["techierathore/TfLens", "otheruser/OtherRepo"]);
    }

    /// <summary>A successful sync records the SHA, a timestamp and the parser's per-stream counts (REQ-FN-025).</summary>
    [Fact]
    public async Task StateCarriesShaTimestampAndCounts()
    {
        var vHarness = BuildHarness();

        await vHarness.Runner.SyncAsync(2);

        var vState = vHarness.Store.States["2|techierathore/TfLens"];
        vState.LastSha.Should().Be(Sha);
        vState.LastSyncTs.Should().NotBeNullOrWhiteSpace();
        vState.RunsCount.Should().Be(2);
        vState.GatesCount.Should().Be(1);
        vState.LastError.Should().BeNull();
    }

    /// <summary>The archive lands under the user and repository the sync belonged to (REQ-FN-017, REQ-FN-027).</summary>
    [Fact]
    public async Task ArchivePathIsUserAndRepositoryScoped()
    {
        var vHarness = BuildHarness();

        await vHarness.Runner.SyncAsync(2);

        var vExpected = Path.Combine(objDataRoot, "raw", "2", "techierathore__TfLens", $"runs-{Sha}.jsonl");
        File.Exists(vExpected).Should().BeTrue();
    }

    /// <summary>Replaying the archive after a live sync yields identical per-stream counts (REQ-FN-029).</summary>
    [Fact]
    public async Task RebuildFromTheArchiveMatchesTheLiveSyncCounts()
    {
        var vHarness = BuildHarness();
        vHarness.Store.RawRoot = Path.Combine(objDataRoot, "raw");
        vHarness.Store.Parser = vHarness.Parser;

        await vHarness.Runner.SyncAsync(2);
        var vLiveCounts = new Dictionary<string, int>(vHarness.Store.RowCounts);

        var vReport = await vHarness.Store.RebuildAsync(2);

        vReport.FilesReplayed.Should().Be(2);
        vHarness.Store.RowCounts.Should().BeEquivalentTo(vLiveCounts);
    }

    /// <summary>Naming a repository that this user has not connected is an error, not an exception.</summary>
    [Fact]
    public async Task SyncingAnUnconnectedRepositoryIsAnError()
    {
        var vHarness = BuildHarness();

        var vResult = await vHarness.Runner.SyncRepoAsync(2, "someone/else");

        vResult.Outcome.Should().Be(SyncOutcome.Error);
        vHarness.Fetcher.ShaCalls.Should().BeEmpty();
    }

    /// <summary>The archive path one stream lands at in this test's data root.</summary>
    /// <param name="aStream">The stream.</param>
    /// <returns>The absolute file path.</returns>
    private string ArchivePath(StreamKind aStream) => Path.Combine(
        objDataRoot, "raw", "2", "techierathore__TfLens", $"{StreamNames.ToName(aStream)}-{Sha}.jsonl");

    /// <summary>Builds a connected repository row.</summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aOwner">GitHub owner.</param>
    /// <param name="aName">GitHub repository name.</param>
    /// <returns>The row.</returns>
    private static UserRepo BuildRepo(int aUserId, string aOwner, string aName) => new()
    {
        UserId = aUserId,
        Repo = $"{aOwner}/{aName}",
        Owner = aOwner,
        Name = aName,
        Branch = "main",
        Kind = FrameworkNames.TechieFlow,
        Framework = FrameworkNames.TechieFlow,
        ConnectedTs = "2026-01-01T00:00:00Z"
    };

    /// <summary>Wires a runner over the fakes, with one connected repository carrying two streams.</summary>
    /// <returns>The harness.</returns>
    private SyncHarness BuildHarness()
    {
        var vFetcher = new FakeGitHubStreamFetcher();
        vFetcher.Shas["techierathore/TfLens"] = Sha;
        vFetcher.Files["techierathore/TfLens:docs/metrics/runs.jsonl"] = RunsText;
        vFetcher.Files["techierathore/TfLens:docs/metrics/gates.jsonl"] = GatesText;

        var vStore = new FakeTelemetryStore();
        vStore.Repos.Add(BuildRepo(2, "techierathore", "TfLens"));

        var vParser = new FakeStreamParser();
        var vOptions = Options.Create(new TfLensOptions { DataRoot = objDataRoot });

        var vInvalidator = new AnalysisCacheInvalidator(NullLogger<AnalysisCacheInvalidator>.Instance);

        var vServices = new ServiceCollection();
        vServices.AddScoped<IGitHubStreamFetcher>(_ => vFetcher);
        vServices.AddScoped<ITelemetryStore>(_ => vStore);
        vServices.AddScoped<IStreamParser>(_ => vParser);

        var vRunner = new RepoSyncRunner(
            vServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            vInvalidator,
            vOptions,
            NullLogger<RepoSyncRunner>.Instance);

        return new SyncHarness(vRunner, vFetcher, vStore, vParser);
    }

    /// <summary>The runner under test and the doubles behind it.</summary>
    /// <param name="Runner">The runner under test.</param>
    /// <param name="Fetcher">The scripted GitHub client.</param>
    /// <param name="Store">The in-memory store.</param>
    /// <param name="Parser">The deterministic parser.</param>
    private sealed record SyncHarness(
        RepoSyncRunner Runner,
        FakeGitHubStreamFetcher Fetcher,
        FakeTelemetryStore Store,
        FakeStreamParser Parser);
}
