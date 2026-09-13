using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The by-origin-model rate the <c>/misses</c> "who was running" card carries — misses per 100 runs of
/// that model, over <c>linked</c> records only (REQ-UI-038; BRD-121, BRD-124).
/// </summary>
/// <remarks>
/// The card answers "which model to route to", so it needs the runs each model did as well as the misses
/// naming it. Three things are pinned: the numerator is the same linked-only count as
/// <c>by_origin_model</c>, the denominator is the runs whose observed <c>model</c> matches, and a row
/// standing on too few records refuses to be a rate rather than printing one.
/// </remarks>
public sealed class MissModelRateTests
{
    /// <summary>The segment every fixture record falls into.</summary>
    private const string AppSegment = "app";

    /// <summary>The model most fixtures name.</summary>
    private const string Opus = "claude-opus-5";

    /// <summary>
    /// Three linked misses naming a model that did six runs read 50.0 per 100 runs, and runs of another
    /// model never enter the denominator.
    /// </summary>
    [Fact]
    public void ModelRateReadsMissesAgainstThatModelsRuns()
    {
        var vRate = RatesOf(
            [Linked("M1", Opus), Linked("M2", Opus), Linked("M3", Opus)],
            [.. Runs(Opus, 6), .. Runs("claude-sonnet-5", 4)]).Single();

        vRate.Model.Should().Be(Opus);
        vRate.Misses.Should().Be(3);
        vRate.Runs.Should().Be(6, "a run of another model is not a run of this one");
        vRate.PerHundredRuns.Display().Should().Be("50.0");
    }

    /// <summary>An inferred attribution reaches neither the count nor the rate (BRD-121).</summary>
    [Fact]
    public void ModelRateCountsLinkedMissesOnly()
    {
        var vRates = RatesOf(
            [
                Linked("M1", Opus),
                Linked("M2", Opus),
                Linked("M3", Opus),
                MissFixtures.Miss("M4", aOriginModel: Opus, aOriginConfidence: "inferred")
            ],
            Runs(Opus, 10));

        vRates.Single().Misses.Should().Be(3, "an inferred record is held out of every per-origin figure");
    }

    /// <summary>A model named by fewer misses than the minimum is not a rate, whatever its runs.</summary>
    [Fact]
    public void ModelRateRefusesBelowTheMinimumMisses()
    {
        var vRate = RatesOf([Linked("M1", Opus), Linked("M2", Opus)], Runs(Opus, 40)).Single();

        vRate.PerHundredRuns.HasValue.Should().BeFalse();
        vRate.PerHundredRuns.Display().Should().Be("insufficient data (n=2)");
    }

    /// <summary>A model with too few runs is not a rate either, and says which count was short.</summary>
    [Fact]
    public void ModelRateRefusesBelowTheMinimumRuns()
    {
        var vRate = RatesOf(
            [Linked("M1", Opus), Linked("M2", Opus), Linked("M3", Opus)],
            Runs(Opus, 2)).Single();

        vRate.PerHundredRuns.Display().Should().Be("insufficient data (n=2 runs)");
    }

    /// <summary>A model that did no live run has no rate — an em dash, never a zero or an infinity.</summary>
    [Fact]
    public void ModelRateIsNotApplicableWithoutRuns()
    {
        var vRate = RatesOf([Linked("M1", Opus), Linked("M2", Opus), Linked("M3", Opus)], []).Single();

        vRate.Runs.Should().Be(0);
        vRate.PerHundredRuns.Kind.Should().Be(FigureKind.NotApplicable);
        vRate.PerHundredRuns.Display().Should().Be("—");
    }

    /// <summary>The rate rows and the parity count rows name the same models with the same counts.</summary>
    [Fact]
    public void ModelRateRowsMatchTheByOriginModelCounts()
    {
        var vAttribution = MissFigures.Compute(
            [Linked("M1", Opus), Linked("M2", Opus), Linked("M3", "gpt-5-codex")],
            [],
            [],
            Runs(Opus, 5)).Live[AppSegment].Attribution;

        vAttribution.MissRatePerOriginModel
            .Select(aRow => (aRow.Model, aRow.Misses))
            .Should().Equal(vAttribution.ByOriginModel.Select(aRow => (aRow.Key, aRow.Count)));
    }

    /// <summary>Runs the engine over the fixtures and returns the app segment's model rates.</summary>
    /// <param name="aMisses">The misses.</param>
    /// <param name="aRuns">The runs.</param>
    /// <returns>One rate row per origin model.</returns>
    private static IReadOnlyList<MissModelRate> RatesOf(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<RunRecord> aRuns) =>
        MissFigures.Compute(aMisses, [], [], aRuns).Live[AppSegment].Attribution.MissRatePerOriginModel;

    /// <summary>A <c>linked</c> miss naming a model.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aModel">The origin model.</param>
    /// <returns>The miss.</returns>
    private static MissRecord Linked(string aMissId, string aModel) =>
        MissFixtures.Miss(aMissId, aOriginPhase: "build-phase", aOriginModel: aModel, aOriginConfidence: "linked");

    /// <summary>Live runs whose observed model is the one given.</summary>
    /// <param name="aModel">The model the runs ran on.</param>
    /// <param name="aCount">How many runs.</param>
    /// <returns>The runs.</returns>
    private static IReadOnlyList<RunRecord> Runs(string aModel, int aCount) =>
        Enumerable.Range(0, aCount).Select(aIndex => GateFixtures.Run() with { Model = aModel }).ToList();
}
