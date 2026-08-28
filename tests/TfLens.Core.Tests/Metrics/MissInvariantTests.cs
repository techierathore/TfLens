using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The miss invariants (REQ-NFR-013, BRD-130) — the NFR sibling of REQ-NFR-009 / BRD-89.
/// </summary>
/// <remarks>
/// Seven clauses, each pinned by a test rather than by prose. The technique is the one ADR-007 and
/// ADR-019 already use: where a rule can be enforced by the shape of a result type it is asserted as a
/// shape, because a shape survives a refactor that deletes a comment. Clause 7 — TfLens writes to no
/// repository and emits into no stream — is a static property of the tree and is pinned in the
/// guardrails project, where the other "prove a negative" checks live.
/// </remarks>
public sealed class MissInvariantTests
{
    private const string Framework = "techieflow";

    /// <summary>
    /// Clause 1 — measured and apportioned rework cost are never blended into one figure.
    /// </summary>
    /// <remarks>
    /// Asserted as a shape: <see cref="MissCost"/> exposes exactly three members, none of which could
    /// hold a blend, so a page, an export or a parity comparison has nothing blended to bind. This is the
    /// acceptance itself — a type-shape requirement, not a convention.
    /// </remarks>
    [Fact]
    public void MissCostExposesNoPropertyThatCouldHoldABlendedFigure()
    {
        var vProperties = typeof(MissCost)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(aProperty => aProperty.Name != "EqualityContract")
            .Select(aProperty => aProperty.Name)
            .OrderBy(aName => aName, StringComparer.Ordinal)
            .ToList();

        vProperties.Should().Equal("Apportioned", "NoneCount", "Sole");

        typeof(MissCost).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should().NotContain(aProperty => aProperty.PropertyType == typeof(bool),
                "an IsApportioned flag beside one Cost property is exactly the shape ADR-019 refuses");
    }

    /// <summary>Clause 1 — no miss result type anywhere carries a total, blend or combined figure.</summary>
    [Fact]
    public void NoMissResultTypeCarriesATotalOrBlendedFigure()
    {
        var vForbidden = new[] { "Total", "Blend", "Combined", "Overall", "Merged" };

        var vOffenders = typeof(MissCost).Assembly
            .GetTypes()
            .Where(aType => aType.IsPublic && aType.Name.StartsWith("Miss", StringComparison.Ordinal))
            .SelectMany(aType => aType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(aProperty => aProperty.PropertyType == typeof(Figure))
                .Select(aProperty => aType.Name + "." + aProperty.Name))
            .Where(aName => vForbidden.Any(aWord => aName.Contains(aWord, StringComparison.Ordinal)))
            .ToList();

        vOffenders.Should().BeEmpty("a blended rework cost must be unrepresentable, not merely forbidden");
    }

    /// <summary>
    /// Clause 2 — no per-model or per-agent figure is computed from <c>inferred</c> attributions, and the
    /// excluded count is never hidden.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task InferredAttributionsReachNoPerOriginFigureAndTheExclusionIsVisible()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1", aOriginModel: "linked-model", aOriginAgent: "linked-agent", aOriginPhase: "build-phase", aOriginConfidence: "linked"),
                MissFixtures.Miss("M2", aOriginModel: "guessed-model", aOriginAgent: "guessed-agent", aOriginPhase: "author-brd", aOriginConfidence: "inferred")
            ],
            []);

        var vAttribution = vAnalysis.Misses.Live["app"].Attribution;

        vAttribution.ByOriginModel.Should().NotContain(aRow => aRow.Key == "guessed-model");
        vAttribution.ByOriginAgent.Should().NotContain(aRow => aRow.Key == "guessed-agent");
        vAttribution.ByOriginPhase.Should().NotContain(aRow => aRow.Key == "author-brd");
        vAttribution.AttributionExcluded.Should().Be(1);
        vAttribution.ExclusionReason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Clause 3 — the <c>why_missed</c> distribution is never rendered over all misses; the denominator
    /// is records carrying the field, and it is on the result's face.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheWhyMissedDistributionIsNeverReadAgainstTheMissCount()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1", aWhyMissed: "instruction-ignored"),
                MissFixtures.Miss("M2", aWhyMissed: "instruction-ignored"),
                MissFixtures.Miss("M3", aWhyMissed: "other"),
                MissFixtures.Miss("M4"),
                MissFixtures.Miss("M5"),
                MissFixtures.Miss("M6")
            ],
            []);

        var vSegment = vAnalysis.Misses.Live["app"];

        vSegment.Misses.Should().Be(6);
        vSegment.WhyMissedN.Should().Be(3);
        vSegment.FailedPracticeDistribution.Single(aRow => aRow.Key == "instruction-ignored").Share
            .Should().Be("67%").And.NotBe("33%", "33% is the share read against the miss count");
        vSegment.FailedPracticeDistribution.Sum(aRow => aRow.Count).Should().Be(vSegment.WhyMissedN);
    }

    /// <summary>
    /// Clause 4 — <c>wont-fix</c> is never folded into open, and the two open-predicates are never
    /// reconciled.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task WontFixIsNeverFoldedIntoOpenAndThePredicatesAreNotReconciled()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1"),
                MissFixtures.Miss("M2"),
                MissFixtures.Miss("M3")
            ],
            [
                MissFixtures.Fix("M1", aVerdictAfter: "wont-fix"),
                MissFixtures.Fix("M2", aVerdictAfter: "deferred"),
                MissFixtures.Fix("M3", aVerdictAfter: "Verified")
            ]);

        // TfLens's backlog predicate: wont-fix is out, deferred is in.
        vAnalysis.Misses.OpenMisses.Should().Be(1);
        vAnalysis.Misses.WontFix.Should().Be(1);

        // The producer's collapse check keeps wont-fix live, so it would answer 2. The two figures are
        // deliberately allowed to disagree — reconciling them would break one of them (BRD-120).
        var vStillLive = vAnalysis.Misses.OpenMisses + vAnalysis.Misses.WontFix;
        vStillLive.Should().Be(2);
        vAnalysis.Misses.OpenMisses.Should().NotBe(vStillLive);
    }

    /// <summary>
    /// Clause 5 — rate-card dollars are never presented as spend; a harness whose dollars could only be
    /// an estimate carries <c>RateCard.EstimateLabel</c> and no measured total.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task RateCardDollarsAreNeverPresentedAsSpend()
    {
        var vAnalysis = await AnalyseAsync(
            [MissFixtures.Miss("M1"), MissFixtures.Miss("M2")],
            [
                MissFixtures.Fix("M1", aHarness: "claude-code", aTokensOut: 500, aCostUsd: 42m),
                MissFixtures.Fix("M2", aHarness: "codex", aTokensOut: 500, aCostUsd: 42m)
            ]);

        foreach (var vRow in vAnalysis.Misses.Live["app"].Cost.ByHarness
                     .Where(aRow => aRow.Harness != MissFigures.OpenCodeHarness))
        {
            vRow.MeasuredUsdTotal.Should().BeNull("only OpenCode measures dollars");
            vRow.MeasuredUsdRecords.Should().Be(0);
            vRow.MeasuredUsdPerMiss.Kind.Should().Be(FigureKind.NotApplicable);
            vRow.EstimateLabel.Should().Be(RateCard.EstimateLabel);
        }
    }

    /// <summary>
    /// Clause 6 — no miss record is ever folded into the <c>gates</c>-derived escape rate.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NoMissRecordIsFoldedIntoTheGatesEscapeRate()
    {
        var vGates = new[]
        {
            GateFixtures.Gate(aReqId: "REQ-FN-001", aVerdict: "FAIL", aGate: MetricsConstants.Escaped),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aVerdict: "FAIL", aGate: "build"),
            GateFixtures.Gate(aReqId: "REQ-FN-003", aVerdict: "FAIL", aGate: "render")
        };

        var vWithoutMisses = await new MetricsEngine(
                new FixtureTelemetryStore().Seed(MissFixtures.UserId, MissFixtures.Repo, Framework, vGates),
                NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);

        var vWithMisses = await new MetricsEngine(
                new FixtureTelemetryStore()
                    .Seed(MissFixtures.UserId, MissFixtures.Repo, Framework, vGates)
                    .SeedMisses(MissFixtures.UserId, MissFixtures.Repo, Framework,
                    [
                        MissFixtures.Miss("M1", aFoundBy: "owner"),
                        MissFixtures.Miss("M2", aFoundBy: "production"),
                        MissFixtures.Miss("M3", aFoundBy: "owner"),
                        MissFixtures.Miss("M4", aFoundBy: "production")
                    ]),
                NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);

        vWithMisses.Live["app"].EscapeRate.Should().Be(vWithoutMisses.Live["app"].EscapeRate,
            "the gates-derived escape rate keeps its definition and its source untouched");
        vWithMisses.Live["app"].EscapeRate.Display().Should().Be("33%");
        vWithMisses.Misses.Live["app"].EscapeShare.Display().Should().Be("100%");
    }

    /// <summary>
    /// The "no switch" half of the clause list: nothing in the miss engine takes a flag that would relax
    /// any of the seven rules.
    /// </summary>
    /// <remarks>
    /// The same argument REQ-NFR-009 makes for the provenance rules. The engine's only public entry point
    /// takes a user, a framework and a cancellation token; the miss calculators take records; and no
    /// public member on either takes a <c>bool</c>, so there is no parameter a caller could set to merge
    /// two columns or admit an inferred attribution.
    /// </remarks>
    [Fact]
    public void NoMissFigureTakesASwitchThatWouldRelaxAnyClause()
    {
        var vEntry = typeof(MetricsEngine).GetMethod(nameof(MetricsEngine.AnalyseAsync))!;

        vEntry.GetParameters().Select(aParameter => aParameter.Name)
            .Should().Equal("aUserId", "aFramework", "aCancellationToken");

        foreach (var vType in new[] { typeof(MissFigures), typeof(MissAttributionTaint) })
        {
            vType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(aMethod => aMethod.GetParameters())
                .Should().NotContain(aParameter => aParameter.ParameterType == typeof(bool),
                    $"{vType.Name} must expose no toggle");
        }
    }

    /// <summary>Runs the engine over miss records seeded into one repository.</summary>
    /// <param name="aMisses">The <c>miss</c> records.</param>
    /// <param name="aFixes">The <c>miss-fix</c> records.</param>
    /// <returns>The analysis.</returns>
    private static async Task<AnalysisResult> AnalyseAsync(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes)
    {
        var vStore = new FixtureTelemetryStore()
            .SeedMisses(MissFixtures.UserId, MissFixtures.Repo, Framework, aMisses, aFixes);

        return await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);
    }
}
