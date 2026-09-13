using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Fakes;

namespace TfLens.Core.Tests.Metrics.Extras;

/// <summary>
/// REQ-FN-140 — a <c>run-void</c> and the run it names leave the harness comparison and the routing
/// figures exactly as they leave the engine's live run count (SCHEMA.md §2.7).
/// </summary>
/// <remarks>
/// The defect these pin was one snapshot stating two run counts: <c>runs_live</c> came from the engine,
/// which applied the voids, while the harness columns re-read the raw stream and counted both the
/// correction and the run it corrected. The harness columns then summed to more than <c>runs_live</c>,
/// and the voided run's tokens stayed inside its harness's totals.
/// </remarks>
public sealed class HarnessRunVoidTests : IDisposable
{
    private const int UserId = 11;
    private const string Repo = "acme/voided";
    private const string Harness = "claude-code";

    private readonly string objDataRoot = ExtrasFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>
    /// A harness column counts neither the void record nor the run it names, and neither appears in its
    /// top commands.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task HarnessColumnCountsNeitherAVoidNorTheRunItNames()
    {
        var vColumn = (await Extras().CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow))
            .Columns.Single(aC => aC.Harness == Harness);

        vColumn.Runs.Should().Be(2);
        vColumn.RunsByCmd.Select(aP => aP.Key).Should().NotContain("build-phase");
    }

    /// <summary>The voided run's tokens leave its harness's token totals.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task HarnessTokensExcludeTheVoidedRun()
    {
        var vColumn = (await Extras().CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow))
            .Columns.Single(aC => aC.Harness == Harness);

        vColumn.TokensIn.Should().Be(200, "only the two surviving runs' 100 input tokens each remain");
    }

    /// <summary>
    /// The harness columns sum to exactly the live run count the engine publishes as <c>runs_live</c> —
    /// the same figure the verifier compares them against.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task HarnessColumnsSumToTheEngineLiveRunCount()
    {
        var vStore = SeededStore();
        var vAnalysis = await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(UserId, FrameworkNames.TechieFlow);
        var vComparison = await new ExtraMetrics(vStore, ExtrasFixture.Options(objDataRoot))
            .CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        vComparison.Columns.Sum(aC => aC.Runs).Should().Be(vAnalysis.Phases.RunsLive);
    }

    /// <summary>
    /// A void record carrying no harness is not a run, so it is not counted in the not-detected footnote
    /// either.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AVoidWithNoHarnessIsNotInTheNotDetectedFootnote()
    {
        var vComparison = await Extras().CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        vComparison.NotDetectedRecords.Should().Be(0);
    }

    /// <summary>The routing view's tokens by model exclude the voided run.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task RoutingTokensByModelExcludeTheVoidedRun()
    {
        var vRouting = await Extras().AnalyseRoutingAsync(UserId, FrameworkNames.TechieFlow);

        vRouting.TokensByModel.Should().ContainSingle().Which.TokensIn.Should().Be(200);
    }

    /// <summary>The extras service over the seeded store.</summary>
    /// <returns>The service under test.</returns>
    private ExtraMetrics Extras() => new(SeededStore(), ExtrasFixture.Options(objDataRoot));

    /// <summary>
    /// Three claude-code runs, one of them voided by a correction that carries no harness of its own.
    /// </summary>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore SeededStore() =>
        new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aRuns:
            [
                Run("build-phase", "10:00:00"),
                Run("log-miss", "11:00:00"),
                Run("verify-phase", "12:00:00"),
                Run("build-phase", "10:00:00") with
                {
                    Kind = RunVoid.Kind,
                    VoidReason = "the start time was typed, not measured",
                    Harness = null,
                    Model = null,
                    TokensIn = null
                }
            ]);

    /// <summary>One claude-code run carrying 100 input tokens on one model.</summary>
    /// <param name="aCmd">The command.</param>
    /// <param name="aStarted">Its start, as a clock time on the fixture's day.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(string aCmd, string aStarted) => new()
    {
        UserId = UserId,
        Repo = Repo,
        SourceSha = "fixture",
        Ts = $"2026-09-01T{aStarted}Z",
        App = "voided",
        Cmd = aCmd,
        Started = $"2026-09-01T{aStarted}Z",
        Ended = $"2026-09-01T{aStarted}Z",
        Harness = Harness,
        Model = "claude-sonnet-4-5",
        TokensIn = 100
    };
}
