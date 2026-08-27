using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Services.Sync;

namespace TfLens.Core.Tests.Sync;

/// <summary>Covers the background poller: configured interval, resilience and clean shutdown.</summary>
public sealed class RepoSyncServiceTests
{
    /// <summary>The interval comes from configuration (REQ-FN-020).</summary>
    [Fact]
    public void IntervalComesFromConfiguration()
    {
        RepoSyncService.ResolveInterval(new TfLensOptions()).Should().Be(TimeSpan.FromMinutes(15));
        RepoSyncService.ResolveInterval(new TfLensOptions { PollIntervalMinutes = 5 })
            .Should().Be(TimeSpan.FromMinutes(5));
    }

    /// <summary>A zero or negative interval is floored rather than spinning the timer.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveIntervalIsFloored(int aMinutes)
    {
        RepoSyncService.ResolveInterval(new TfLensOptions { PollIntervalMinutes = aMinutes })
            .Should().Be(RepoSyncService.MinimumInterval);
    }

    /// <summary>A pass runs the whole-estate sync, which is the poller's scope (REQ-FN-018).</summary>
    [Fact]
    public async Task PassSyncsEveryUser()
    {
        var vRunner = new RecordingRepoSyncRunner();
        var vService = BuildService(vRunner);

        await vService.RunPassAsync(CancellationToken.None);

        vRunner.Calls.Should().ContainSingle().Which.Should().BeNull();
    }

    /// <summary>A pass that throws is absorbed so the poller keeps ticking (REQ-FN-020).</summary>
    [Fact]
    public async Task FailedPassIsAbsorbed()
    {
        var vRunner = new RecordingRepoSyncRunner { Failure = new InvalidOperationException("database down") };
        var vService = BuildService(vRunner);

        var vAct = async () => await vService.RunPassAsync(CancellationToken.None);

        await vAct.Should().NotThrowAsync();
    }

    /// <summary>The service starts with the host and stops on the host's cancellation token (REQ-FN-020).</summary>
    [Fact]
    public async Task ServiceStartsAndStopsCleanly()
    {
        var vService = BuildService(new RecordingRepoSyncRunner());

        await vService.StartAsync(CancellationToken.None);
        var vStop = vService.StopAsync(CancellationToken.None);

        await vStop.WaitAsync(TimeSpan.FromSeconds(5));
        vStop.IsCompletedSuccessfully.Should().BeTrue();
    }

    /// <summary>Builds the poller over a container holding one recording runner.</summary>
    /// <param name="aRunner">The runner the pass resolves.</param>
    /// <returns>The service under test.</returns>
    private static RepoSyncService BuildService(IRepoSyncRunner aRunner)
    {
        var vServices = new ServiceCollection();
        vServices.AddScoped(_ => aRunner);

        return new RepoSyncService(
            vServices.BuildServiceProvider(),
            Options.Create(new TfLensOptions()),
            NullLogger<RepoSyncService>.Instance);
    }

    /// <summary>A runner that records the scope it was asked to sync, and optionally fails.</summary>
    private sealed class RecordingRepoSyncRunner : IRepoSyncRunner
    {
        /// <summary>The user id each call was given; <c>null</c> means the whole estate.</summary>
        public List<int?> Calls { get; } = [];

        /// <summary>When set, every call throws it.</summary>
        public Exception? Failure { get; set; }

        /// <inheritdoc />
        public Task<SyncReport> SyncAsync(int? aUserId = null, CancellationToken aCancellationToken = default)
        {
            Calls.Add(aUserId);

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new SyncReport(aUserId, [], "start", "end"));
        }

        /// <inheritdoc />
        public Task<RepoSyncResult> SyncRepoAsync(
            int aUserId,
            string aRepo,
            CancellationToken aCancellationToken = default) =>
            Task.FromResult(new RepoSyncResult(aRepo, SyncOutcome.Skipped, null, 0, null));
    }
}
