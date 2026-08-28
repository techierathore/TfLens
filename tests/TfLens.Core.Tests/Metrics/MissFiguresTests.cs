using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The miss figures (REQ-FN-077, BRD-118, BRD-119, BRD-120).
/// </summary>
/// <remarks>
/// The live dataset is four records, so almost every figure there legitimately answers
/// <c>insufficient data (n=…)</c>. These tests seed a larger set so the arithmetic itself is covered —
/// a figure that is only ever exercised below the minimum has never been checked at all.
/// </remarks>
public sealed class MissFiguresTests
{
    private const string Framework = "techieflow";

    /// <summary>Misses segment by project type exactly as the three questions do, with no "all" bucket.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MissFiguresAreSegmentedByProjectTypeAndNeverPooled()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1"),
                MissFixtures.Miss("M2"),
                MissFixtures.Miss("M3", aProjectType: "library"),
                MissFixtures.Miss("M4", aProjectTypeInferred: true)
            ],
            []);

        vAnalysis.Misses.MissesTotal.Should().Be(4);
        vAnalysis.Misses.Live["app"].Misses.Should().Be(2);
        vAnalysis.Misses.Live["library"].Misses.Should().Be(1);
        vAnalysis.Misses.Live[MetricsConstants.Unclassified].Misses.Should().Be(1);
        vAnalysis.Misses.Live.Keys.Should().NotContain("all");
        vAnalysis.Misses.Live.Keys.Should().NotContain("total");
    }

    /// <summary>A backfilled miss reaches no figure and is reported rather than dropped.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task BackfilledMissesReachNoFigureAndAreCounted()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1"),
                MissFixtures.Miss("M2", aBackfilled: true)
            ],
            [MissFixtures.Fix("M2", aBackfilled: true)]);

        vAnalysis.Misses.MissesTotal.Should().Be(1);
        vAnalysis.Misses.BackfilledMissesExcluded.Should().Be(1);
        vAnalysis.Misses.MissFixesTotal.Should().Be(0);
        vAnalysis.Misses.BackfilledMissFixesExcluded.Should().Be(1);
    }

    /// <summary>The failed-practice denominator is records carrying the field, never the miss count.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task FailedPracticeDenominatorIsRecordsCarryingTheField()
    {
        var vAnalysis = await AnalyseAsync(SixMisses(), []);
        var vSegment = vAnalysis.Misses.Live["app"];

        vSegment.Misses.Should().Be(6);
        vSegment.WhyMissedN.Should().Be(3, "three of the six carry why_missed");
        vSegment.FailedPracticeDistribution
            .Single(aRow => aRow.Key == "instruction-ignored").Share
            .Should().Be("67%", "two of the three assessed, not two of the six recorded");
        vSegment.WhyMissedEligibility.Assessed.Should().Be(3);
        vSegment.WhyMissedEligibility.Eligible.Should().Be(6);
        vSegment.FailedPracticeNote.Should().BeNull();
    }

    /// <summary>A <c>why_missed</c> that only an amendment supplies still reaches the distribution.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AWhyMissedSuppliedOnlyByAnAmendReachesTheFailedPracticeDistribution()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1", aWhyMissed: "other"),
                MissFixtures.Miss("M2", aWhyMissed: "other"),
                MissFixtures.Miss("M3")
            ],
            [],
            [MissFixtures.Amend("M3", "ambiguous-acceptance")]);

        var vSegment = vAnalysis.Misses.Live["app"];

        vAnalysis.Misses.AmendmentsApplied.Should().Be(1);
        vSegment.WhyMissedN.Should().Be(3);
        vSegment.FailedPracticeDistribution.Should().Contain(aRow => aRow.Key == "ambiguous-acceptance");
    }

    /// <summary>Design-miss share is <c>unspecified-gap</c> over every miss.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task DesignMissShareIsUnspecifiedGapOverAllMisses()
    {
        var vAnalysis = await AnalyseAsync(SixMisses(), []);

        vAnalysis.Misses.Live["app"].DesignMissShare.Display().Should().Be("33%");
    }

    /// <summary>
    /// The miss escape share is a second figure beside the gates escape rate, computed from a different
    /// stream and never merged into it.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MissEscapeShareIsAdjacentToTheGatesEscapeRate()
    {
        var vStore = new FixtureTelemetryStore()
            .Seed(MissFixtures.UserId, MissFixtures.Repo, Framework, [
                GateFixtures.Gate(aReqId: "REQ-FN-001", aVerdict: "FAIL", aGate: MetricsConstants.Escaped),
                GateFixtures.Gate(aReqId: "REQ-FN-002", aVerdict: "FAIL", aGate: "build"),
                GateFixtures.Gate(aReqId: "REQ-FN-003", aVerdict: "FAIL", aGate: "render")
            ])
            .SeedMisses(MissFixtures.UserId, MissFixtures.Repo, Framework, SixMisses());

        var vAnalysis = await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);

        vAnalysis.Live["app"].EscapeRate.Display().Should().Be("33%", "one of three failing REQs escaped");
        vAnalysis.Misses.Live["app"].EscapeShare.Display().Should().Be("33%", "two of six misses reached a human");
        vAnalysis.Live["app"].EscapeRate.SupportingRecords.Should().Be(3, "the gates figure counts failing REQs");
        vAnalysis.Misses.Live["app"].EscapeShare.SupportingRecords.Should().Be(6, "the miss figure counts misses");
    }

    /// <summary>A declined miss is its own figure and never joins the backlog.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task WontFixIsNeverFoldedIntoOpen()
    {
        var vAnalysis = await AnalyseAsync(SixMisses(), SixFixes());
        var vSegment = vAnalysis.Misses.Live["app"];

        vSegment.WontFix.Should().Be(1);
        vSegment.OpenMisses.Should().Be(4);
        vSegment.ResolvedMisses.Should().Be(1);
        (vSegment.OpenMisses + vSegment.WontFix + vSegment.ResolvedMisses).Should().Be(6);
    }

    /// <summary>A deferred miss is outstanding work and stays open; a miss with no fix is open too.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task DeferredAndUntouchedMissesStayOpen()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1"),
                MissFixtures.Miss("M2"),
                MissFixtures.Miss("M3")
            ],
            [
                MissFixtures.Fix("M1", aVerdictAfter: "deferred"),
                MissFixtures.Fix("M2", aVerdictAfter: "Needs re-verify")
            ]);

        vAnalysis.Misses.OpenMisses.Should().Be(3);
        vAnalysis.Misses.WontFix.Should().Be(0);
    }

    /// <summary>The latest fix decides the lifecycle, so a reopened miss is open again.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AReopenedMissIsOpenAgain()
    {
        var vAnalysis = await AnalyseAsync(
            [MissFixtures.Miss("M1")],
            [
                MissFixtures.Fix("M1", aVerdictAfter: "Verified", aTs: "2026-08-28T12:00:00Z"),
                MissFixtures.Fix("M1", aVerdictAfter: "FAIL", aTs: "2026-08-29T12:00:00Z", aFixAttempt: 2)
            ]);

        vAnalysis.Misses.OpenMisses.Should().Be(1);
        vAnalysis.Misses.ResolvedMisses.Should().Be(0);
    }

    /// <summary>Median time-to-close is timed over verified misses only, in hours.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MedianTimeToCloseIsTimedOverVerifiedMissesOnly()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1", aTs: "2026-08-28T00:00:00Z"),
                MissFixtures.Miss("M2", aTs: "2026-08-28T00:00:00Z"),
                MissFixtures.Miss("M3", aTs: "2026-08-28T00:00:00Z"),
                MissFixtures.Miss("M4", aTs: "2026-08-28T00:00:00Z")
            ],
            [
                MissFixtures.Fix("M1", aTs: "2026-08-28T02:00:00Z"),
                MissFixtures.Fix("M2", aTs: "2026-08-28T04:00:00Z"),
                MissFixtures.Fix("M3", aTs: "2026-08-28T06:00:00Z"),
                MissFixtures.Fix("M4", aVerdictAfter: "wont-fix", aTs: "2026-08-28T23:00:00Z")
            ]);

        var vMedian = vAnalysis.Misses.Live["app"].MedianTimeToCloseHours;

        vMedian.Display().Should().Be("4");
        vMedian.SupportingRecords.Should().Be(3, "the declined miss was never closed");
    }

    /// <summary>The per-phase rate reads misses against the runs of that command.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MissRatePerOriginPhaseCountsRunsOfThatCommand()
    {
        var vStore = new FixtureTelemetryStore()
            .Seed(MissFixtures.UserId, MissFixtures.Repo, Framework, aRuns:
            [
                GateFixtures.Run(),
                GateFixtures.Run(),
                GateFixtures.Run(),
                GateFixtures.Run()
            ])
            .SeedMisses(MissFixtures.UserId, MissFixtures.Repo, Framework,
            [
                MissFixtures.Miss("M1", aOriginPhase: "build-phase", aOriginConfidence: "linked"),
                MissFixtures.Miss("M2", aOriginPhase: "build-phase", aOriginConfidence: "linked")
            ]);

        var vAnalysis = await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);

        var vRate = vAnalysis.Misses.Live["app"].Attribution.MissRatePerOriginPhase.Single();

        vRate.Phase.Should().Be("build-phase");
        vRate.Misses.Should().Be(2);
        vRate.Runs.Should().Be(4);
        vRate.Rate.Display().Should().Be("50%");
    }

    /// <summary>Below the minimum every rate refuses to be a number, and carries the count instead.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EveryRateRefusesToBeANumberBelowTheMinimum()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1", aMissClass: "unspecified-gap", aFoundBy: "owner"),
                MissFixtures.Miss("M2")
            ],
            []);

        var vSegment = vAnalysis.Misses.Live["app"];

        vSegment.DesignMissShare.Kind.Should().Be(FigureKind.InsufficientData);
        vSegment.DesignMissShare.Display().Should().Be("insufficient data (n=2)");
        vSegment.DesignMissShare.TryGetValue(out _).Should().BeFalse();
        vSegment.EscapeShare.Kind.Should().Be(FigureKind.InsufficientData);
        vSegment.MedianTimeToCloseHours.Kind.Should().Be(FigureKind.InsufficientData);
        vSegment.ClassDistributionNote.Should().Be("insufficient data (n=1)");
    }

    /// <summary>A fix naming no stored miss is counted as an orphan rather than dropped.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task OrphanFixesAreCountedNeverDropped()
    {
        var vAnalysis = await AnalyseAsync(
            [MissFixtures.Miss("M1")],
            [
                MissFixtures.Fix("M1"),
                MissFixtures.Fix("M-NOBODY")
            ]);

        vAnalysis.Misses.MissFixesTotal.Should().Be(2);
        vAnalysis.Misses.OrphanFixes.Should().Be(1);
    }

    /// <summary>An escape arriving with no <c>why_missed</c> is counted as the data-quality fact it is.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EscapesMissingWhyAreCounted()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1", aFoundBy: "owner"),
                MissFixtures.Miss("M2", aFoundBy: "production", aWhyMissed: "other"),
                MissFixtures.Miss("M3", aFoundBy: "gate")
            ],
            []);

        vAnalysis.Misses.EscapesMissingWhy.Should().Be(1);
    }

    /// <summary>A miss carrying no class is not assessed, and is neither bucketed nor counted.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AMissCarryingNoClassIsNeverBucketed()
    {
        var vAnalysis = await AnalyseAsync(
            [
                MissFixtures.Miss("M1", aMissClass: "unspecified-gap"),
                MissFixtures.Miss("M2", aMissClass: "unspecified-gap"),
                MissFixtures.Miss("M3", aMissClass: "wrong-behaviour"),
                MissFixtures.Miss("M4")
            ],
            []);

        var vSegment = vAnalysis.Misses.Live["app"];

        vSegment.ClassDistributionN.Should().Be(3);
        vSegment.ClassNotRecorded.Should().Be(1);
        vSegment.ClassDistribution.Should().NotContain(aRow =>
            aRow.Key == "other" || aRow.Key == "unknown" || aRow.Key == string.Empty);
        vSegment.ClassDistribution.Single(aRow => aRow.Key == "unspecified-gap").Share.Should().Be("67%");
    }

    /// <summary>Six misses covering every distribution the segment reports.</summary>
    /// <returns>The records.</returns>
    private static IReadOnlyList<MissRecord> SixMisses() =>
    [
        MissFixtures.Miss("M1", aMissClass: "unspecified-gap", aWhyMissed: "instruction-ignored", aFoundBy: "owner"),
        MissFixtures.Miss("M2", aMissClass: "unspecified-gap", aWhyMissed: "instruction-ignored", aFoundBy: "gate"),
        MissFixtures.Miss("M3", aMissClass: "wrong-behaviour", aWhyMissed: "other", aFoundBy: "gate"),
        MissFixtures.Miss("M4", aMissClass: "wrong-behaviour", aFoundBy: "production"),
        MissFixtures.Miss("M5", aMissClass: "missing-behaviour", aFoundBy: "gate"),
        MissFixtures.Miss("M6", aMissClass: "missing-behaviour", aFoundBy: "gate")
    ];

    /// <summary>The fixes that put those six misses into every lifecycle state at once.</summary>
    /// <returns>The records.</returns>
    private static IReadOnlyList<MissFixRecord> SixFixes() =>
    [
        MissFixtures.Fix("M1", aVerdictAfter: "Verified"),
        MissFixtures.Fix("M2", aVerdictAfter: "wont-fix"),
        MissFixtures.Fix("M3", aVerdictAfter: "deferred"),
        MissFixtures.Fix("M5", aVerdictAfter: "FAIL"),
        MissFixtures.Fix("M6", aVerdictAfter: "Verified", aTs: "2026-08-28T12:00:00Z"),
        MissFixtures.Fix("M6", aVerdictAfter: "FAIL", aTs: "2026-08-29T12:00:00Z", aFixAttempt: 2)
    ];

    /// <summary>Runs the engine over miss records seeded into one repository.</summary>
    /// <param name="aMisses">The <c>miss</c> records.</param>
    /// <param name="aFixes">The <c>miss-fix</c> records.</param>
    /// <param name="aAmends">The <c>miss-amend</c> records.</param>
    /// <returns>The analysis.</returns>
    private static async Task<AnalysisResult> AnalyseAsync(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<MissAmendRecord>? aAmends = null)
    {
        var vStore = new FixtureTelemetryStore()
            .SeedMisses(MissFixtures.UserId, MissFixtures.Repo, Framework, aMisses, aFixes, aAmends);

        return await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);
    }
}
