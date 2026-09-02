using FluentAssertions;
using FluentAssertions.Execution;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Playbook;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-105 (BRD-166, ADR-024) — the Playbook edition's reporting guards, which are stricter than the
/// TechieFlow stream's and are never relaxed to match it.
/// </summary>
/// <remarks>
/// Every test here fixes shut one route by which the stricter guard could be quietly softened: dropping
/// the window check, dropping the observed-model check, admitting a <c>zero-unverified</c> cost, or
/// letting a refused record reappear under an "unknown model" heading. The last is the one worth stating
/// twice — a bucket named for a model is a claim about a model, and the producer declined to make it.
/// </remarks>
public sealed class PlaybookMissGuardTests
{
    /// <summary>A window and quality block the producer vouched for entirely.</summary>
    private const string GoodQuality =
        "{\"source_window\":{\"complete\":true,\"valid\":true},"
        + "\"data_quality\":{\"valid\":true,\"cost_status\":\"complete\"}}";

    /// <summary>A window the producer explicitly did not vouch for.</summary>
    private const string InvalidWindow =
        "{\"source_window\":{\"complete\":true,\"valid\":false},"
        + "\"data_quality\":{\"valid\":false,\"cost_status\":\"complete\"}}";

    /// <summary>A vouched window whose cost figure the producer could not verify.</summary>
    private const string UnverifiedCost =
        "{\"source_window\":{\"complete\":true,\"valid\":true},"
        + "\"data_quality\":{\"valid\":true,\"cost_status\":\"zero-unverified\"}}";

    /// <summary>
    /// A model attribution needs all three conditions, and the first failure is the reported reason.
    /// </summary>
    [Theory]
    [InlineData("inferred", "gpt-5", GoodQuality, PlaybookGuardReasons.NotLinked)]
    [InlineData("linked", "gpt-5", InvalidWindow, PlaybookGuardReasons.WindowNotCompleteAndValid)]
    [InlineData("linked", null, GoodQuality, PlaybookGuardReasons.NoObservedModel)]
    [InlineData("linked", "gpt-5", GoodQuality, null)]
    public void AttributionRequiresLinkedCompleteWindowAndObservedModel(
        string aConfidence,
        string? aModel,
        string aQuality,
        string? aExpected)
    {
        var vMiss = Miss("PB-1", aConfidence, aModel, aQuality);

        PlaybookMissGuards.RefuseAttribution(vMiss).Should().Be(aExpected);
    }

    /// <summary>
    /// A record carrying no data-quality block at all fails every guard — absent is never a pass.
    /// </summary>
    [Fact]
    public void AbsentQualityBlockFailsEveryGuard()
    {
        var vQuality = PlaybookMissGuards.QualityOf(null);

        using var vScope = new AssertionScope();
        vQuality.Should().Be(PlaybookMissQuality.Absent);
        vQuality.IsCompleteValidWindow.Should().BeFalse();
        PlaybookMissGuards.RefuseAttribution(Miss("PB-1", "linked", "gpt-5", null))
            .Should().Be(PlaybookGuardReasons.WindowNotCompleteAndValid);
    }

    /// <summary>
    /// Malformed overflow JSON refuses the record rather than throwing and losing the whole report.
    /// </summary>
    [Fact]
    public void MalformedQualityJsonRefusesRatherThanThrows()
    {
        PlaybookMissGuards.QualityOf("{not json").Should().Be(PlaybookMissQuality.Absent);
    }

    /// <summary>
    /// The schema-2 phase spelling — top-level <c>complete</c> beside <c>data_quality.valid</c> — is
    /// accepted as the documented fallback when no <c>source_window</c> object is present.
    /// </summary>
    [Fact]
    public void PhaseContractSpellingOfTheWindowIsAccepted()
    {
        var vQuality = PlaybookMissGuards.QualityOf(
            "{\"complete\":true,\"data_quality\":{\"valid\":true,\"cost_status\":\"complete\"}}");

        using var vScope = new AssertionScope();
        vQuality.IsCompleteValidWindow.Should().BeTrue();
        vQuality.CostStatus.Should().Be(PlaybookMissGuards.CostStatusComplete);
    }

    /// <summary>
    /// An inferred or unknown origin never reaches an "unknown model" performance bucket.
    /// </summary>
    /// <remarks>
    /// The distribution is asserted to be empty rather than merely to lack a row called <c>unknown</c>,
    /// because the failure this guards against is not a badly named bucket — it is a refused record
    /// appearing in the model chart at all, under whatever heading the page happens to pick.
    /// </remarks>
    [Fact]
    public void InferredOriginNeverEntersAnUnknownModelBucket()
    {
        var vReport = PlaybookMissNormalizer.Read(
            FrameworkNames.Playbook,
            [
                Miss("PB-1", "inferred", null, GoodQuality),
                Miss("PB-2", "unknown", null, GoodQuality),
                Miss("PB-3", "linked", null, GoodQuality)
            ],
            [],
            []);

        using var vScope = new AssertionScope();
        vReport.Attribution.ByOriginModel.Should().BeEmpty();
        vReport.Attribution.AttributedN.Should().Be(0);
        vReport.Attribution.RefusedN.Should().Be(3);
        vReport.Attribution.Refused.Select(aR => aR.Reason).Should().BeEquivalentTo(
            [PlaybookGuardReasons.NotLinked, PlaybookGuardReasons.NoObservedModel]);
    }

    /// <summary>
    /// A headline cost figure needs <c>sole</c>, a complete valid window and <c>cost_status:"complete"</c>.
    /// </summary>
    [Theory]
    [InlineData("sole", GoodQuality, PlaybookCostCohort.Headline, null)]
    [InlineData("sole", UnverifiedCost, PlaybookCostCohort.Refused, PlaybookGuardReasons.CostStatusNotComplete)]
    [InlineData("sole", InvalidWindow, PlaybookCostCohort.Refused, PlaybookGuardReasons.WindowNotCompleteAndValid)]
    [InlineData("shared:4", GoodQuality, PlaybookCostCohort.Apportioned, null)]
    [InlineData("shared:4", InvalidWindow, PlaybookCostCohort.Refused, PlaybookGuardReasons.WindowNotCompleteAndValid)]
    [InlineData("none", GoodQuality, PlaybookCostCohort.Excluded, PlaybookGuardReasons.NoneAttribution)]
    [InlineData("whatever", GoodQuality, PlaybookCostCohort.Refused, PlaybookGuardReasons.UnknownAttribution)]
    public void HeadlineCostRequiresSoleCompleteWindowAndCompleteCostStatus(
        string aAttribution,
        string aQuality,
        PlaybookCostCohort aCohort,
        string? aReason)
    {
        var vVerdict = PlaybookMissGuards.ClassifyCost(Fix("PB-1", aAttribution, aQuality));

        using var vScope = new AssertionScope();
        vVerdict.Cohort.Should().Be(aCohort);
        vVerdict.Reason.Should().Be(aReason);
    }

    /// <summary>
    /// The Playbook guard is strictly stronger than the TechieFlow one and is never unified downward.
    /// </summary>
    /// <remarks>
    /// The same record — <c>linked</c>, but with no window the producer vouched for — is attributable
    /// under <see cref="MissAttributionTaint"/> and refused here. If someone ever "simplifies" the two
    /// into one guard, this test is the one that says which claim was lost.
    /// </remarks>
    [Fact]
    public void PlaybookGuardIsStricterThanTheTechieFlowGuardOnTheSameRecord()
    {
        var vMiss = Miss("PB-1", "linked", "gpt-5", null);

        using var vScope = new AssertionScope();
        MissAttributionTaint.Partition([vMiss]).AttributedN.Should().Be(1, "TechieFlow asks only for linked");
        PlaybookMissGuards.RefuseAttribution(vMiss).Should().NotBeNull("the Playbook also asks for a window");
    }

    /// <summary>
    /// <c>FIELD_SINCE</c> is applied before the optional-field denominator, and the report states
    /// <c>n of N assessed</c>.
    /// </summary>
    [Fact]
    public void FieldSinceIsAppliedBeforeTheAssessedDenominator()
    {
        var vReport = PlaybookMissNormalizer.Read(
            FrameworkNames.Playbook,
            [
                Miss("PB-OLD", "linked", "gpt-5", GoodQuality, aTs: "2026-08-01T09:00:00Z"),
                Miss("PB-1", "linked", "gpt-5", GoodQuality) with { WhyMissed = "other" },
                Miss("PB-2", "linked", "gpt-5", GoodQuality)
            ],
            [],
            []);

        using var vScope = new AssertionScope();
        vReport.WhyMissedEligibility.PredatesField.Should().Be(1);
        vReport.WhyMissedEligibility.Eligible.Should().Be(2);
        vReport.WhyMissedAssessed.Should().Be("1 of 2 assessed");
    }

    /// <summary>
    /// A comparative figure resting on fewer than three records is <c>insufficient data</c>, never a number.
    /// </summary>
    [Fact]
    public void ComparativeMetricsBelowThreeRecordsAreInsufficientData()
    {
        var vCost = PlaybookMissNormalizer.Read(
            FrameworkNames.Playbook,
            [Miss("PB-1", "linked", "gpt-5", GoodQuality)],
            [Fix("PB-1", "sole", GoodQuality) with { TokensOut = 500 }],
            []).Cost;

        using var vScope = new AssertionScope();
        vCost.HeadlineRecords.Should().Be(1);
        vCost.HeadlineTokens.Sole.Kind.Should().Be(FigureKind.InsufficientData);
        vCost.HeadlineTokens.Sole.Display().Should().Be("insufficient data (n=1)");
    }

    /// <summary>
    /// The cost result carries no member that could hold measured and estimated dollars together.
    /// </summary>
    [Fact]
    public void CostResultHasNoBlendedMoneyMember()
    {
        var vNames = typeof(PlaybookCostSplit).GetProperties().Select(aProperty => aProperty.Name).ToList();

        using var vScope = new AssertionScope();
        vNames.Should().Contain("MeasuredUsdTotal");
        vNames.Should().NotContain(aName =>
            aName.Contains("Estimate", StringComparison.Ordinal)
            || aName.Contains("Blended", StringComparison.Ordinal)
            || aName.Contains("CombinedUsd", StringComparison.Ordinal));
    }

    /// <summary>Builds one Playbook miss row.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aConfidence">The <c>origin_confidence</c>.</param>
    /// <param name="aModel">The observed origin model, or <c>null</c>.</param>
    /// <param name="aQuality">The preserved overflow JSON carrying the window and quality block.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <returns>The record.</returns>
    private static MissRecord Miss(
        string aMissId,
        string aConfidence,
        string? aModel,
        string? aQuality,
        string aTs = "2026-08-30T09:00:00Z") => new()
    {
        UserId = 41,
        Repo = "acme/book",
        SourceSha = "bundle-sha",
        Ts = aTs,
        MissId = aMissId,
        OriginPhase = "build-phase",
        OriginConfidence = aConfidence,
        OriginModel = aModel,
        SourceLineHash = aMissId,
        Overflow = aQuality
    };

    /// <summary>Builds one Playbook fix row.</summary>
    /// <param name="aMissId">The miss it repairs.</param>
    /// <param name="aAttribution">The <c>cost_attribution</c>.</param>
    /// <param name="aQuality">The preserved overflow JSON carrying the window and quality block.</param>
    /// <returns>The record.</returns>
    private static MissFixRecord Fix(string aMissId, string aAttribution, string? aQuality) => new()
    {
        UserId = 41,
        Repo = "acme/book",
        SourceSha = "bundle-sha",
        Ts = "2026-08-30T10:00:00Z",
        MissId = aMissId,
        FixRunId = "run-1",
        CostAttribution = aAttribution,
        SourceLineHash = aMissId + "-fix",
        Overflow = aQuality
    };
}
