using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The attribution taint (REQ-FN-078, BRD-121) — the miss-stream sibling of the backfill taint set.
/// </summary>
/// <remarks>
/// The rule is the same shape as <c>TaintSet</c>'s: exclude, then <b>say so</b>. A per-model figure built
/// on guessed attributions drives a routing decision on a guess, and an exclusion the reader cannot see
/// is indistinguishable from a bug — so the count and the reason are asserted here as engine output, not
/// as page markup.
/// </remarks>
public sealed class MissAttributionTaintTests
{
    private const string Framework = "techieflow";

    /// <summary>Only <c>linked</c> records reach the per-phase, per-model and per-agent figures.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ANonLinkedRecordNeverReachesAPerOriginFigure()
    {
        var vAttribution = await AttributionAsync(
        [
            MissFixtures.Miss("M1", aOriginPhase: "build-phase", aOriginModel: "claude-opus-5", aOriginAgent: "builder", aOriginConfidence: "linked"),
            MissFixtures.Miss("M2", aOriginPhase: "build-phase", aOriginModel: "claude-opus-5", aOriginAgent: "builder", aOriginConfidence: "linked"),
            MissFixtures.Miss("M3", aOriginPhase: "verify-phase", aOriginModel: "claude-sonnet-5", aOriginAgent: "verifier", aOriginConfidence: "linked"),
            MissFixtures.Miss("M4", aOriginPhase: "author-brd", aOriginModel: "ghost-model", aOriginAgent: "ghost-agent", aOriginConfidence: "inferred"),
            MissFixtures.Miss("M5", aOriginPhase: "author-brd", aOriginModel: "ghost-model", aOriginAgent: "ghost-agent", aOriginConfidence: "unknown"),
            MissFixtures.Miss("M6", aOriginPhase: "author-brd")
        ]);

        vAttribution.AttributedN.Should().Be(3);
        vAttribution.ByOriginModel.Should().NotContain(aRow => aRow.Key == "ghost-model");
        vAttribution.ByOriginAgent.Should().NotContain(aRow => aRow.Key == "ghost-agent");
        vAttribution.ByOriginPhase.Should().NotContain(aRow => aRow.Key == "author-brd");
    }

    /// <summary>The excluded count and the reason are returned by the engine, not left to the page.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheExclusionIsReturnedByTheEngineWithItsReason()
    {
        var vAttribution = await AttributionAsync(
        [
            MissFixtures.Miss("M1", aOriginConfidence: "linked"),
            MissFixtures.Miss("M2", aOriginConfidence: "inferred"),
            MissFixtures.Miss("M3", aOriginConfidence: "inferred"),
            MissFixtures.Miss("M4", aOriginConfidence: "unknown"),
            MissFixtures.Miss("M5")
        ]);

        vAttribution.AttributionExcluded.Should().Be(4);
        vAttribution.ExclusionReason.Should().Be(MissAttributionTaint.ExclusionReason);
        vAttribution.ExclusionReason.Should().NotBeNullOrWhiteSpace();
        vAttribution.ExcludedByConfidence.Should().BeEquivalentTo(
        [
            new MissAttributionExclusion("inferred", 2),
            new MissAttributionExclusion(MissAttributionTaint.NotRecorded, 1),
            new MissAttributionExclusion("unknown", 1)
        ]);
    }

    /// <summary>An absent confidence is reported as absent, never folded into the <c>unknown</c> value.</summary>
    [Fact]
    public void AnAbsentConfidenceIsNeverFoldedIntoUnknown()
    {
        var vSet = MissAttributionTaint.Partition(
        [
            MissFixtures.Miss("M1"),
            MissFixtures.Miss("M2", aOriginConfidence: "unknown")
        ]);

        vSet.ExcludedByConfidence.Should().HaveCount(2);
        vSet.ExcludedByConfidence.Should().Contain(aRow => aRow.Confidence == MissAttributionTaint.NotRecorded);
        MissAttributionTaint.NotRecorded.Should().NotBe("unknown");
    }

    /// <summary>Per-agent is computed alongside per-model; neither substitutes for the other.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ThePerAgentFigureIsComputedAlongsideThePerModelOne()
    {
        var vAttribution = await AttributionAsync(
        [
            MissFixtures.Miss("M1", aOriginModel: "claude-opus-5", aOriginAgent: "builder", aOriginConfidence: "linked"),
            MissFixtures.Miss("M2", aOriginModel: "claude-opus-5", aOriginAgent: "verifier", aOriginConfidence: "linked"),
            MissFixtures.Miss("M3", aOriginModel: "claude-sonnet-5", aOriginAgent: "verifier", aOriginConfidence: "linked")
        ]);

        vAttribution.ByOriginModel.Single(aRow => aRow.Key == "claude-opus-5").Count.Should().Be(2);
        vAttribution.ByOriginAgent.Single(aRow => aRow.Key == "verifier").Count.Should().Be(2);
        vAttribution.ByOriginModel.Single(aRow => aRow.Key == "claude-sonnet-5").Share.Should().Be("33%");
    }

    /// <summary>An attribution split over nothing is empty rather than absent.</summary>
    [Fact]
    public void AnEmptySplitIsEmptyRatherThanAbsent()
    {
        MissAttributionSet.Empty.AttributedN.Should().Be(0);
        MissAttributionSet.Empty.AttributionExcluded.Should().Be(0);
        MissAttributionSet.Empty.Reason.Should().Be(MissAttributionTaint.ExclusionReason);
    }

    /// <summary>Runs the engine and returns the <c>app</c> segment's attribution block.</summary>
    /// <param name="aMisses">The miss records to seed.</param>
    /// <returns>The attribution figures.</returns>
    private static async Task<MissAttributionFigures> AttributionAsync(IReadOnlyList<MissRecord> aMisses)
    {
        var vStore = new FixtureTelemetryStore()
            .SeedMisses(MissFixtures.UserId, MissFixtures.Repo, Framework, aMisses);

        var vAnalysis = await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);

        return vAnalysis.Misses.Live["app"].Attribution;
    }
}
