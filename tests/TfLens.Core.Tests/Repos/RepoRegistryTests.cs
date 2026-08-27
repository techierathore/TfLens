using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Repos;

namespace TfLens.Core.Tests.Repos;

/// <summary>
/// Covers <see cref="RepoRegistry"/> against the real PostgreSQL database and a hand-controlled
/// GitHub client: validation, connect, duplicate handling, removal and cross-user isolation.
/// </summary>
/// <remarks>
/// Two fixed test user ids stand in for two signed-in users. Both are wiped before and after every
/// test, and every assertion about "user 1 cannot see user 2's data" is made against real SQL rather
/// than an in-memory double, because the claim being tested is a claim about the predicates.
/// </remarks>
public sealed class RepoRegistryTests : IAsyncLifetime
{
    /// <summary>The first test user — the one the registry is usually called for.</summary>
    private const int UserOne = 990001;

    /// <summary>The second test user — the one whose data must never be reachable from the first.</summary>
    private const int UserTwo = 990002;

    private readonly PostgresRepoTestStore objStore;
    private readonly string objDataRoot;

    /// <summary>
    /// Creates the fixture: one store over the live database and a throwaway data root for the raw
    /// archive assertions.
    /// </summary>
    public RepoRegistryTests()
    {
        objStore = new PostgresRepoTestStore(ConnectionString());
        objDataRoot = Path.Combine(Path.GetTempPath(), "tflens-repotests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Reads the database connection string from the environment, falling back to the documented local
    /// compose service.
    /// </summary>
    /// <returns>The connection string to test against.</returns>
    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("TfLensDbConnection")
        ?? "Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await objStore.PurgeUserAsync(UserOne);
        await objStore.PurgeUserAsync(UserTwo);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await objStore.PurgeUserAsync(UserOne);
        await objStore.PurgeUserAsync(UserTwo);
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>
    /// Builds a registry over the shared store and a given GitHub stub.
    /// </summary>
    /// <param name="aFetcher">The GitHub stub the registry should see.</param>
    /// <param name="aRunner">The sync runner the registry should hand the first sync to.</param>
    /// <param name="aToken">The optional server PAT, when the test is about the PAT.</param>
    /// <returns>The registry under test.</returns>
    private RepoRegistry NewRegistry(
        StubGitHubStreamFetcher aFetcher,
        IRepoSyncRunner? aRunner = null,
        string? aToken = null)
    {
        var vOptions = Options.Create(new TfLensOptions { DataRoot = objDataRoot, GitHubToken = aToken });
        return new RepoRegistry(
            objStore,
            aFetcher,
            () => aRunner,
            vOptions,
            NullLogger<RepoRegistry>.Instance);
    }

    /// <summary>
    /// A stub carrying one public TechieFlow repository on its default branch.
    /// </summary>
    /// <returns>The configured stub.</returns>
    private static StubGitHubStreamFetcher TechieFlowRepo() =>
        new StubGitHubStreamFetcher()
            .WithRepo("techierathore", "TrBlazeUI")
            .WithPath("techierathore", "TrBlazeUI", "docs/metrics");

    // ------------------------------------------------------------------ validation

    /// <summary>A private repository is refused with the release's exact public-repos-only wording.</summary>
    [Fact]
    public async Task ValidateRefusesPrivateRepo()
    {
        var vFetcher = new StubGitHubStreamFetcher().WithRepo("acme", "secret", aIsPrivate: true);

        var vResult = await NewRegistry(vFetcher).ValidateAsync(UserOne, "acme/secret");

        vResult.Exists.Should().BeTrue();
        vResult.IsPublic.Should().BeFalse();
        vResult.Message.Should().Be(RepoRegistry.PrivateRepoMessage);
        vResult.IsConnectable.Should().BeFalse();
    }

    /// <summary>
    /// The optional server PAT raises the rate limit only: even when GitHub answers with a repository
    /// the PAT can see, a private repository is still refused and its contents are never probed.
    /// </summary>
    [Fact]
    public async Task ServerTokenNeverReachesAPrivateRepo()
    {
        var vFetcher = new StubGitHubStreamFetcher()
            .WithRepo("acme", "secret", aIsPrivate: true)
            .WithPath("acme", "secret", "docs/metrics");

        var vResult = await NewRegistry(vFetcher, aToken: "ghp-a-server-pat").ValidateAsync(UserOne, "acme/secret");

        vResult.IsPublic.Should().BeFalse();
        vResult.IsConnectable.Should().BeFalse();
        vFetcher.ProbedPaths.Should().BeEmpty();
    }

    /// <summary>A repository that does not resolve on GitHub is refused as not found.</summary>
    [Fact]
    public async Task ValidateRefusesMissingRepo()
    {
        var vResult = await NewRegistry(new StubGitHubStreamFetcher()).ValidateAsync(UserOne, "acme/nope");

        vResult.Exists.Should().BeFalse();
        vResult.IsConnectable.Should().BeFalse();
        vResult.Message.Should().Contain("acme/nope");
    }

    /// <summary>
    /// A public repository carrying neither framework's telemetry directory is refused, and the reason
    /// names both paths that were looked for.
    /// </summary>
    [Fact]
    public async Task ValidateRefusesRepoWithoutTelemetry()
    {
        var vFetcher = new StubGitHubStreamFetcher().WithRepo("acme", "plain");

        var vResult = await NewRegistry(vFetcher).ValidateAsync(UserOne, "acme/plain");

        vResult.Exists.Should().BeTrue();
        vResult.IsPublic.Should().BeTrue();
        vResult.TelemetryPath.Should().BeNull();
        vResult.Framework.Should().BeNull();
        vResult.IsConnectable.Should().BeFalse();
        vResult.Message.Should().Contain("docs/metrics").And.Contain("verification/telemetry");
    }

    /// <summary>A <c>docs/metrics</c> directory identifies the repository as TechieFlow.</summary>
    [Fact]
    public async Task ValidateDetectsTechieFlow()
    {
        var vResult = await NewRegistry(TechieFlowRepo()).ValidateAsync(UserOne, "techierathore/TrBlazeUI");

        vResult.Framework.Should().Be(FrameworkNames.TechieFlow);
        vResult.TelemetryPath.Should().Be("docs/metrics");
        vResult.Branch.Should().Be("main");
        vResult.IsConnectable.Should().BeTrue();
    }

    /// <summary>A <c>verification/telemetry</c> directory identifies the repository as Playbook.</summary>
    [Fact]
    public async Task ValidateDetectsPlaybook()
    {
        var vFetcher = new StubGitHubStreamFetcher()
            .WithRepo("acme", "playbookrepo", aDefaultBranch: "master")
            .WithPath("acme", "playbookrepo", "verification/telemetry", "master");

        var vResult = await NewRegistry(vFetcher).ValidateAsync(UserOne, "acme/playbookrepo");

        vResult.Framework.Should().Be(FrameworkNames.Playbook);
        vResult.TelemetryPath.Should().Be("verification/telemetry");
        vResult.Branch.Should().Be("master");
        vResult.IsConnectable.Should().BeTrue();
    }

    /// <summary>A branch the caller names is probed instead of the repository's default branch.</summary>
    [Fact]
    public async Task ValidateHonoursCallerBranch()
    {
        var vFetcher = new StubGitHubStreamFetcher()
            .WithRepo("acme", "branched")
            .WithPath("acme", "branched", "docs/metrics", "telemetry");

        var vResult = await NewRegistry(vFetcher).ValidateAsync(UserOne, "acme/branched", "telemetry");

        vResult.Branch.Should().Be("telemetry");
        vResult.Framework.Should().Be(FrameworkNames.TechieFlow);
    }

    /// <summary>
    /// A kind override narrows the probe to that framework, so a repository carrying both telemetry
    /// directories can still be connected as the framework the user chose (REQ-UI-012's Kind select).
    /// </summary>
    [Fact]
    public async Task ValidateHonoursKindOverride()
    {
        var vFetcher = new StubGitHubStreamFetcher()
            .WithRepo("acme", "both")
            .WithPath("acme", "both", "docs/metrics")
            .WithPath("acme", "both", "verification/telemetry");
        var vRegistry = NewRegistry(vFetcher);

        var vAuto = await vRegistry.ValidateAsync(UserOne, "acme/both");
        var vForced = await vRegistry.ValidateAsync(UserOne, "acme/both", null, FrameworkNames.Playbook);

        vAuto.Framework.Should().Be(FrameworkNames.TechieFlow);
        vForced.Framework.Should().Be(FrameworkNames.Playbook);
        vForced.TelemetryPath.Should().Be("verification/telemetry");
    }

    /// <summary>
    /// A kind override the repository cannot satisfy is refused, and the reason names only the path
    /// that was actually looked for.
    /// </summary>
    [Fact]
    public async Task ValidateRefusesUnsatisfiableKindOverride()
    {
        var vRegistry = NewRegistry(TechieFlowRepo());

        var vResult = await vRegistry.ValidateAsync(
            UserOne,
            "techierathore/TrBlazeUI",
            null,
            FrameworkNames.Playbook);

        vResult.IsConnectable.Should().BeFalse();
        vResult.Message.Should().Contain("verification/telemetry").And.NotContain("docs/metrics");
    }

    /// <summary>A kind that names no known framework is rejected rather than silently auto-detected.</summary>
    [Fact]
    public async Task ValidateRejectsUnknownKind()
    {
        var vRegistry = NewRegistry(TechieFlowRepo());

        var vAct = () => vRegistry.ValidateAsync(UserOne, "techierathore/TrBlazeUI", null, "scrum");

        await vAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>An unparseable input is refused with a message and without any GitHub call.</summary>
    [Fact]
    public async Task ValidateRefusesUnparseableInputWithoutCallingGitHub()
    {
        var vFetcher = new StubGitHubStreamFetcher();

        var vResult = await NewRegistry(vFetcher).ValidateAsync(UserOne, "https://gitlab.com/acme/plain");

        vResult.Exists.Should().BeFalse();
        vResult.Message.Should().Contain("github.com");
        vFetcher.RequestedRepos.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ connect

    /// <summary>Connecting writes the row for that user and hands the repository to the sync runner.</summary>
    [Fact]
    public async Task ConnectWritesRowAndQueuesFirstSync()
    {
        var vRunner = new StubRepoSyncRunner();
        var vRegistry = NewRegistry(TechieFlowRepo(), vRunner);

        var vRepo = await vRegistry.ConnectAsync(UserOne, "https://github.com/techierathore/TrBlazeUI.git");

        vRepo.UserId.Should().Be(UserOne);
        vRepo.Repo.Should().Be("techierathore/TrBlazeUI");
        vRepo.Framework.Should().Be(FrameworkNames.TechieFlow);
        vRepo.IsPublic.Should().BeTrue();

        var vStored = await vRegistry.ListAsync(UserOne);
        vStored.Should().ContainSingle().Which.Repo.Should().Be("techierathore/TrBlazeUI");

        await vRunner.FirstCall.WaitAsync(TimeSpan.FromSeconds(5));
        vRunner.Synced.Should().Contain($"{UserOne}:techierathore/TrBlazeUI");
    }

    /// <summary>A repository the connect checks refuse never reaches the store.</summary>
    [Fact]
    public async Task ConnectRefusesPrivateRepoAndWritesNothing()
    {
        var vFetcher = new StubGitHubStreamFetcher().WithRepo("acme", "secret", aIsPrivate: true);
        var vRegistry = NewRegistry(vFetcher);

        var vAct = () => vRegistry.ConnectAsync(UserOne, "acme/secret");

        await vAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(RepoRegistry.PrivateRepoMessage);
        (await vRegistry.ListAsync(UserOne)).Should().BeEmpty();
    }

    /// <summary>The same repository cannot be connected twice by the same user (BRD-104).</summary>
    [Fact]
    public async Task ConnectRejectsDuplicateForSameUser()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserOne, "techierathore/TrBlazeUI");

        var vAct = () => vRegistry.ConnectAsync(UserOne, "https://github.com/techierathore/TrBlazeUI");

        await vAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already connected*");
        (await vRegistry.ListAsync(UserOne)).Should().HaveCount(1);
    }

    /// <summary>
    /// The duplicate check is scoped to the caller: a second user may connect the same public
    /// repository, and each ends up with their own independent row (BRD-104).
    /// </summary>
    [Fact]
    public async Task ConnectAllowsSameRepoForAnotherUser()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserOne, "techierathore/TrBlazeUI");

        var vSecond = await vRegistry.ConnectAsync(UserTwo, "techierathore/TrBlazeUI");

        vSecond.UserId.Should().Be(UserTwo);
        (await vRegistry.ListAsync(UserOne)).Should().ContainSingle();
        (await vRegistry.ListAsync(UserTwo)).Should().ContainSingle();
    }

    // ------------------------------------------------------------------ list with counts

    /// <summary>The Repos grid's read joins each repository to its per-stream record counts (BRD-98).</summary>
    [Fact]
    public async Task ListWithCountsJoinsSyncState()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserOne, "techierathore/TrBlazeUI");
        await objStore.WriteSyncStateAsync(new SyncState
        {
            UserId = UserOne,
            Repo = "techierathore/TrBlazeUI",
            LastSha = "abc1234",
            LastSyncTs = DateTimeOffset.UtcNow.ToString("O"),
            RunsCount = 7,
            GatesCount = 11,
            SessionsCount = 3,
            CommitsCount = 5
        });

        var vItems = await vRegistry.ListWithCountsAsync(UserOne);

        vItems.Should().ContainSingle();
        vItems[0].RecordCount.Should().Be(26);
        vItems[0].Status.Should().Be(RepoSyncStatuses.Synced);
        vItems[0].LastSha.Should().Be("abc1234");
    }

    /// <summary>A repository that has never synced reports pending with a zero count, not an error.</summary>
    [Fact]
    public async Task ListWithCountsReportsPendingBeforeFirstSync()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserOne, "techierathore/TrBlazeUI");

        var vItems = await vRegistry.ListWithCountsAsync(UserOne);

        vItems.Should().ContainSingle();
        vItems[0].RecordCount.Should().Be(0);
        vItems[0].Status.Should().Be(RepoSyncStatuses.Pending);
    }

    // ------------------------------------------------------------------ remove

    /// <summary>
    /// Removing purges the repository row, its sync state, its stream rows and its raw archive folder
    /// for that user (BRD-101).
    /// </summary>
    [Fact]
    public async Task RemovePurgesRowsAndRawArchive()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserOne, "techierathore/TrBlazeUI");
        await objStore.SeedRunAsync(UserOne, "techierathore/TrBlazeUI", "2026-08-26T10:00:00Z");
        var vArchive = Path.Combine(objDataRoot, "raw", UserOne.ToString(), "techierathore__TrBlazeUI");
        Directory.CreateDirectory(vArchive);
        await File.WriteAllTextAsync(Path.Combine(vArchive, "runs-abc.jsonl"), "{}");

        await vRegistry.RemoveAsync(UserOne, "techierathore/TrBlazeUI");

        (await vRegistry.ListAsync(UserOne)).Should().BeEmpty();
        (await objStore.CountRunsAsync(UserOne, "techierathore/TrBlazeUI")).Should().Be(0);
        Directory.Exists(vArchive).Should().BeFalse();
    }

    /// <summary>Removing a repository the caller never connected is refused, and nothing is deleted.</summary>
    [Fact]
    public async Task RemoveRefusesRepoTheUserNeverConnected()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());

        var vAct = () => vRegistry.RemoveAsync(UserOne, "techierathore/TrBlazeUI");

        await vAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not connected to your account*");
    }

    // ------------------------------------------------------------------ isolation (REQ-FN-017 / REQ-NFR-010)

    /// <summary>One user's list never contains another user's repositories.</summary>
    [Fact]
    public async Task ListNeverCrossesUsers()
    {
        var vFetcher = TechieFlowRepo()
            .WithRepo("acme", "other")
            .WithPath("acme", "other", "docs/metrics");
        var vRegistry = NewRegistry(vFetcher, new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserOne, "techierathore/TrBlazeUI");
        await vRegistry.ConnectAsync(UserTwo, "acme/other");

        var vOne = await vRegistry.ListAsync(UserOne);
        var vTwo = await vRegistry.ListAsync(UserTwo);

        vOne.Should().ContainSingle().Which.Repo.Should().Be("techierathore/TrBlazeUI");
        vTwo.Should().ContainSingle().Which.Repo.Should().Be("acme/other");
    }

    /// <summary>
    /// A user naming another user's repository is told it is not connected — the same answer they get
    /// for a repository nobody has, so the call cannot be used to discover what other users track.
    /// </summary>
    [Fact]
    public async Task RemoveCannotReachAnotherUsersRepo()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserTwo, "techierathore/TrBlazeUI");
        await objStore.SeedRunAsync(UserTwo, "techierathore/TrBlazeUI", "2026-08-26T10:00:00Z");

        var vAct = () => vRegistry.RemoveAsync(UserOne, "techierathore/TrBlazeUI");

        await vAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not connected to your account*");
        (await vRegistry.ListAsync(UserTwo)).Should().ContainSingle();
        (await objStore.CountRunsAsync(UserTwo, "techierathore/TrBlazeUI")).Should().Be(1);
    }

    /// <summary>
    /// Removing one user's copy of a shared public repository leaves the other user's rows, counts and
    /// raw archive untouched (BRD-101, BRD-104).
    /// </summary>
    [Fact]
    public async Task RemoveLeavesTheOtherUsersCopyIntact()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserOne, "techierathore/TrBlazeUI");
        await vRegistry.ConnectAsync(UserTwo, "techierathore/TrBlazeUI");
        await objStore.SeedRunAsync(UserTwo, "techierathore/TrBlazeUI", "2026-08-26T10:00:00Z");
        var vArchiveTwo = Path.Combine(objDataRoot, "raw", UserTwo.ToString(), "techierathore__TrBlazeUI");
        Directory.CreateDirectory(vArchiveTwo);

        await vRegistry.RemoveAsync(UserOne, "techierathore/TrBlazeUI");

        (await vRegistry.ListAsync(UserOne)).Should().BeEmpty();
        (await vRegistry.ListAsync(UserTwo)).Should().ContainSingle();
        (await objStore.CountRunsAsync(UserTwo, "techierathore/TrBlazeUI")).Should().Be(1);
        Directory.Exists(vArchiveTwo).Should().BeTrue();
    }

    /// <summary>
    /// The duplicate check is per user in both directions: a repository another user has connected does
    /// not read as already connected for the caller.
    /// </summary>
    [Fact]
    public async Task ValidateDoesNotReportAnotherUsersRepoAsConnected()
    {
        var vRegistry = NewRegistry(TechieFlowRepo(), new StubRepoSyncRunner());
        await vRegistry.ConnectAsync(UserTwo, "techierathore/TrBlazeUI");

        var vResult = await vRegistry.ValidateAsync(UserOne, "techierathore/TrBlazeUI");

        vResult.AlreadyConnected.Should().BeFalse();
        vResult.IsConnectable.Should().BeTrue();
    }
}
