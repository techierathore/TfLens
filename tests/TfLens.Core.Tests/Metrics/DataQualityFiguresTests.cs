using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Coverage's <b>Data quality</b> counts (REQ-UI-039, BRD-127 as amended 2026-09-08).
/// </summary>
/// <remarks>
/// Two rules are on trial here and they pull in opposite directions. Everything the stored records can
/// answer must be <i>counted</i> — an unrecognised value that is filtered away is a wrong total that looks
/// entirely normal. Everything they cannot answer must be <c>null</c> and never <c>0</c> — a zero for a
/// field nobody has read is a measurement claim nobody has made. The records are built inline rather than
/// through the shared fixtures because these tests are about fields the fixtures deliberately fix.
/// </remarks>
public sealed class DataQualityFiguresTests
{
    private const int UserId = 7;
    private const string Sha = "fixture";

    /// <summary>Misses are counted per repository, with an All-repos total row last.</summary>
    [Fact]
    public void FieldCompletenessCountsMissesPerRepositoryAndTotals()
    {
        var vResult = DataQualityFigures.Compute(
            [Miss("M1", "acme/alpha"), Miss("M2", "acme/alpha"), Miss("M3", "acme/beta")],
            [], [], [], [],
            MissAnalysis.Empty);

        vResult.FieldCompleteness.Should().HaveCount(3);
        vResult.FieldCompleteness[0].Repo.Should().Be("acme/alpha");
        vResult.FieldCompleteness[0].Misses.Should().Be(2);
        vResult.FieldCompleteness[1].Repo.Should().Be("acme/beta");
        vResult.FieldCompleteness[1].Misses.Should().Be(1);
        vResult.FieldCompleteness[^1].Repo.Should().Be(DataQualityFigures.AllReposRow);
        vResult.FieldCompleteness[^1].Misses.Should().Be(3);
    }

    /// <summary>
    /// A record written before the field existed <b>predates</b> it and is never pooled with one that
    /// was eligible and left it empty — the two are separate columns (BRD-172).
    /// </summary>
    [Fact]
    public void PredatingTheFieldIsCountedApartFromLackingIt()
    {
        var vResult = DataQualityFigures.Compute(
            [
                // Before the 2026-09-07 floor: it could not have carried either field.
                Miss("M1", "acme/alpha", aTs: "2026-08-01T09:00:00Z"),
                // Eligible and answered.
                Miss("M2", "acme/alpha", aTs: "2026-09-08T09:00:00Z", aSort: "weak-check", aWhat: "a sentence"),
                // Eligible and left empty.
                Miss("M3", "acme/alpha", aTs: "2026-09-08T09:00:00Z")
            ],
            [], [], [], [],
            MissAnalysis.Empty);

        var vRow = vResult.FieldCompleteness[0];

        vRow.Misses.Should().Be(3);
        vRow.Eligible.Should().Be(2);
        vRow.PredatesField.Should().Be(1);
        vRow.MissingSort.Should().Be(2);
        vRow.MissingWhat.Should().Be(2);
    }

    /// <summary>
    /// An amendment that completes <c>sort</c> is folded before the count is taken, so a field that was
    /// answered later does not read as never answered (BRD-116).
    /// </summary>
    [Fact]
    public void AFieldCompletedByAnAmendmentReadsAsCarried()
    {
        var vResult = DataQualityFigures.Compute(
            [Miss("M1", "acme/alpha", aTs: "2026-09-08T09:00:00Z")],
            [Amend("M1", "sort", "ignored")],
            [], [], [],
            MissAnalysis.Empty);

        vResult.FieldCompleteness[0].MissingSort.Should().Be(0);
        vResult.FieldCompleteness[0].MissingWhat.Should().Be(1);
    }

    /// <summary>
    /// Review records are counted here <b>because they are not misses</b> — they never enter a miss
    /// figure (BRD-174).
    /// </summary>
    [Fact]
    public void ReviewRecordsAreCountedAndNeverEnterAMissFigure()
    {
        var vResult = DataQualityFigures.Compute(
            [Miss("M1", "acme/alpha")],
            [],
            [Review("build-review"), Review("verify-review")],
            [], [],
            MissAnalysis.Empty);

        vResult.Diagnostics.ReviewRecords.Should().Be(2);
        vResult.FieldCompleteness[0].Misses.Should().Be(1);
        vResult.FieldCompleteness[^1].Misses.Should().Be(1);
    }

    /// <summary>
    /// A run is derivable only when it omits its duration and carries the window it needs; a run that
    /// carries a duration is never counted, and one with no start is not derivable at all.
    /// </summary>
    [Fact]
    public void DurationIsDerivableOnlyFromRecordsThatCarryTheWindow()
    {
        var vResult = DataQualityFigures.Compute(
            [], [], [],
            [
                Run(aDurationS: 120, aStarted: "2026-09-01T09:00:00Z", aEnded: "2026-09-01T09:02:00Z"),
                Run(aDurationS: null, aStarted: "2026-09-01T09:00:00Z", aEnded: "2026-09-01T09:02:00Z"),
                Run(aDurationS: null, aStarted: "2026-09-01T09:00:00Z", aEnded: null),
                Run(aDurationS: null, aStarted: null, aEnded: "2026-09-01T09:02:00Z")
            ],
            [],
            MissAnalysis.Empty);

        var vDuration = vResult.Derived.Single(aRow => aRow.Field == "duration_s");

        // One: the record whose `ts` stands in for an absent `ended`, exactly as SCHEMA.md defines it.
        //
        // The record that omits `duration_s` but carries both clocks is NOT counted, and that is the
        // amended rule (BRD-179, 2026-09-10) rather than a regression: the timestamps now win outright,
        // so a record carrying both of them is simply READ. Deriving is what is left for a run whose
        // window cannot be closed any other way. Counting every timestamped record here would tell the
        // reader that almost the whole stream was worked out rather than measured.
        //
        // The record carrying a duration whose clocks agree with it is untouched, and the record with no
        // start cannot be answered without a guess — which is what this page exists to refuse.
        vDuration.Derivable.Should().Be(1);
    }

    /// <summary>A gate that carries an attempt is never altered; only the ones that omit it are counted.</summary>
    [Fact]
    public void AttemptIsDerivableOnlyForGatesThatOmitIt()
    {
        var vResult = DataQualityFigures.Compute(
            [], [], [], [],
            [Gate(aAttempt: 1), Gate(aAttempt: null), Gate(aAttempt: null)],
            MissAnalysis.Empty);

        vResult.Derived.Single(aRow => aRow.Field == "attempt").Derivable.Should().Be(2);
    }

    /// <summary>
    /// A value outside a documented vocabulary is kept and counted, never coerced to the nearest legal
    /// one and never dropped.
    /// </summary>
    [Fact]
    public void UnrecognisedValuesAreCountedAndNamedWhereTheyWereSeen()
    {
        var vResult = DataQualityFigures.Compute(
            [], [], [],
            [Run(aCmd: "build-phase"), Run(aCmd: "rename-page"), Run(aCmd: "rename-page")],
            [Gate(aVerdict: "Verified"), Gate(aVerdict: "PASS")],
            MissAnalysis.Empty);

        vResult.UnrecognisedValues.Should().HaveCount(2);
        vResult.UnrecognisedValues[0].Where.Should().Be("runs.cmd");
        vResult.UnrecognisedValues[0].Value.Should().Be("rename-page");
        vResult.UnrecognisedValues[0].Records.Should().Be(2);
        vResult.UnrecognisedValues[1].Where.Should().Be("gates.verdict");
        vResult.UnrecognisedValues[1].Value.Should().Be("PASS");
        vResult.UnrecognisedRecordTotal.Should().Be(3);
    }

    /// <summary>
    /// An absent value is absence, not an unrecognised value — the two are reported by different counts
    /// and pooling them would invent a finding.
    /// </summary>
    [Fact]
    public void AnAbsentValueIsNotAnUnrecognisedValue()
    {
        var vResult = DataQualityFigures.Compute(
            [], [], [],
            [Run(aCmd: null, aHarness: null)],
            [Gate(aVerdict: null)],
            MissAnalysis.Empty);

        vResult.UnrecognisedValues.Should().BeEmpty();
        vResult.UnrecognisedRecordTotal.Should().Be(0);
    }

    /// <summary>
    /// The framework's own verdicts are counted so they can be kept out of every application figure
    /// (BRD-177).
    /// </summary>
    [Fact]
    public void FrameworkOwnVerdictsAreCountedSeparatelyFromApplicationClasses()
    {
        var vResult = DataQualityFigures.Compute(
            [], [], [], [],
            [Gate(aReqClass: "FR"), Gate(aReqClass: "FR"), Gate(aReqClass: "UI"), Gate(aReqClass: null)],
            MissAnalysis.Empty);

        vResult.Diagnostics.FrameworkOwnVerdicts.Should().Be(2);
    }

    /// <summary>The link-integrity counts are read from the miss engine, never recomputed here.</summary>
    [Fact]
    public void DiagnosticsCarryTheMissEnginesOwnLinkIntegrityCounts()
    {
        var vAnalysis = MissAnalysis.Empty with
        {
            EscapesMissingWhy = 6,
            OrphanFixes = 2,
            OrphanAmends = 4,
            AmendmentsIgnored = 1,
            AmendmentsApplied = 53
        };

        var vResult = DataQualityFigures.Compute([], [], [], [], [], vAnalysis);

        vResult.Diagnostics.EscapesMissingWhy.Should().Be(6);
        vResult.Diagnostics.OrphanFixes.Should().Be(2);
        vResult.Diagnostics.OrphanAmends.Should().Be(4);
        vResult.Diagnostics.AmendmentsFolded.Should().Be(53);
        vResult.Diagnostics.OrphanRecordTotal.Should().Be(7);
    }

    /// <summary>A framework with no records reports nothing at all, rather than a page of zeros.</summary>
    [Fact]
    public void AnEmptyStreamReportsNoRowsRatherThanZeroRows()
    {
        var vResult = DataQualityFigures.Compute([], [], [], [], [], MissAnalysis.Empty);

        vResult.FieldCompleteness.Should().BeEmpty();
        vResult.UnrecognisedValues.Should().BeEmpty();
        vResult.Derived.Should().HaveCount(2);
        vResult.Derived.Should().OnlyContain(aRow => aRow.Derivable == 0);
    }

    /// <summary>Builds one miss record with only the fields these tests are about.</summary>
    /// <param name="aMissId">The link key.</param>
    /// <param name="aRepo">The repository it was read from.</param>
    /// <param name="aTs">The record timestamp, which decides eligibility for the 2026-09-07 fields.</param>
    /// <param name="aSort">Whose gap it was; <c>null</c> means the record does not carry it.</param>
    /// <param name="aWhat">The one free-text sentence; <c>null</c> means the record does not carry it.</param>
    /// <returns>The record.</returns>
    private static MissRecord Miss(
        string aMissId,
        string aRepo,
        string aTs = "2026-09-08T09:00:00Z",
        string? aSort = null,
        string? aWhat = null) => new()
    {
        UserId = UserId,
        Repo = aRepo,
        SourceSha = Sha,
        Ts = aTs,
        MissId = aMissId,
        Sort = aSort,
        What = aWhat
    };

    /// <summary>Builds one amendment against a miss in <c>acme/alpha</c>.</summary>
    /// <param name="aMissId">The miss it completes.</param>
    /// <param name="aField">The wire field name it fills.</param>
    /// <param name="aValue">The value it supplies.</param>
    /// <returns>The record.</returns>
    private static MissAmendRecord Amend(string aMissId, string aField, string aValue) => new()
    {
        UserId = UserId,
        Repo = "acme/alpha",
        SourceSha = Sha,
        Ts = "2026-09-08T10:00:00Z",
        MissId = aMissId,
        Field = aField,
        Value = aValue
    };

    /// <summary>Builds one review record — the fourth kind, which is never a miss.</summary>
    /// <param name="aReviewPhase">Which review phase produced it.</param>
    /// <returns>The record.</returns>
    private static MissReviewRecord Review(string aReviewPhase) => new()
    {
        UserId = UserId,
        Repo = "acme/alpha",
        SourceSha = Sha,
        Ts = "2026-09-08T09:00:00Z",
        ReviewPhase = aReviewPhase
    };

    /// <summary>Builds one run record with only the fields these tests are about.</summary>
    /// <param name="aCmd">The command that ran.</param>
    /// <param name="aHarness">The detected harness.</param>
    /// <param name="aDurationS">The recorded duration, or <c>null</c> where the record omits it.</param>
    /// <param name="aStarted">When the task began.</param>
    /// <param name="aEnded">When it ended, or <c>null</c> where only <c>ts</c> says so.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(
        string? aCmd = "build-phase",
        string? aHarness = "claude-code",
        int? aDurationS = 60,
        string? aStarted = "2026-09-01T09:00:00Z",
        string? aEnded = "2026-09-01T09:01:00Z") => new()
    {
        UserId = UserId,
        Repo = "acme/alpha",
        SourceSha = Sha,
        Ts = "2026-09-01T09:01:00Z",
        Cmd = aCmd,
        Harness = aHarness,
        DurationS = aDurationS,
        Started = aStarted,
        Ended = aEnded
    };

    /// <summary>Builds one gate record with only the fields these tests are about.</summary>
    /// <param name="aVerdict">The verdict the record carries.</param>
    /// <param name="aAttempt">The attempt number, or <c>null</c> where the record omits it.</param>
    /// <param name="aReqClass">The requirement class.</param>
    /// <returns>The record.</returns>
    private static GateRecord Gate(
        string? aVerdict = "Verified",
        int? aAttempt = 1,
        string? aReqClass = "UI") => new()
    {
        UserId = UserId,
        Repo = "acme/alpha",
        SourceSha = Sha,
        Ts = "2026-09-01T09:00:00Z",
        Verdict = aVerdict,
        Attempt = aAttempt,
        ReqClass = aReqClass,
        Harness = "claude-code"
    };
}
