using System.Globalization;
using System.Reflection;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Import;
using TfLens.Core.Parsing;
using TfLens.Core.Playbook;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-094 … REQ-FN-102 (BRD-153 … BRD-163, ADR-023, ADR-025, ADR-027) — the schema-2
/// <c>phase-metric</c> ingest, its invariants and quarantine, and the cohorts every figure rests on.
/// </summary>
/// <remarks>
/// The cases below are the producer contract's own acceptance list
/// (<c>docs/Phase-Efficiency-TfLens-Contract.md</c> §9) plus the two the requirements add: that an
/// unreadable window is never a zero-valued run, and that the diagnostic component sums cannot be added
/// at all. Acceptance 11 — no actor-grouped reporting — is a property of the whole tree and is proved by
/// the repo-wide <c>ActorGroupingTests</c> guardrail, which scans this code too.
/// </remarks>
public sealed class PlaybookPhaseAdapterTests
{
    /// <summary>The user every fixture line is attributed to.</summary>
    private const int UserId = 47;

    /// <summary>The repository every fixture line is attributed to.</summary>
    private const string Repo = "techierathore/AI-First-Playbook";

    /// <summary>The bundle sha256 the fixture export arrives under.</summary>
    private const string Sha = "bundle-sha";

    /// <summary>The harness that has a normalized schema-2 producer today.</summary>
    private const string OpenCode = "opencode";

    /// <summary>The single-model array the canonical contract example carries.</summary>
    private const string SoleModel =
        """[{"model":"anthropic/claude-sonnet-5","turns":12,"tokens":{"input":31203,"output":7900,"reasoning":1220,"cache_read":16000,"cache_write":1010},"tokens_in":48213,"tokens_out":9120,"cost_usd":0.41,"cost_status":"complete","active_ms":78000}]""";

    /// <summary>
    /// Acceptance 1 — re-importing the same NDJSON upserts one execution rather than duplicating it.
    /// </summary>
    [Fact]
    public void ReimportOfTheSameExecutionDoesNotDuplicate()
    {
        var vText = string.Join('\n', Line(), Line());

        var vFirst = Parse(vText);
        var vSecond = Parse(vText);

        vFirst.PhaseExecutions.Should().ContainSingle("the exporter re-emits every readable window");
        vFirst.DuplicatesCollapsed.Should().BeGreaterThan(0);
        vSecond.PhaseExecutions.Single().PhaseExecutionId
            .Should().Be(vFirst.PhaseExecutions.Single().PhaseExecutionId);
    }

    /// <summary>Invariant 1 — <c>tokens_in</c> that is not the sum of its legs quarantines the row.</summary>
    [Fact]
    public void TokensInMismatchQuarantinesTheRow()
    {
        var vRow = Single(Line(aTokensIn: 999));

        PlaybookPhaseInvariants.Validate(vRow).Reasons
            .Should().Contain(aR => aR.Code == PlaybookPhaseInvariants.TokensInMismatch);
    }

    /// <summary>Invariant 2 — <c>tokens_out</c> that is not <c>output + reasoning</c> quarantines the row.</summary>
    [Fact]
    public void TokensOutMismatchQuarantinesTheRow()
    {
        var vRow = Single(Line(aTokensOut: 1));

        PlaybookPhaseInvariants.Validate(vRow).Reasons
            .Should().Contain(aR => aR.Code == PlaybookPhaseInvariants.TokensOutMismatch);
    }

    /// <summary>Invariant 3 — a session cannot contribute tokens without having been spawned.</summary>
    [Fact]
    public void SpawnedBelowContributorsQuarantinesTheRow()
    {
        var vRow = Single(Line(aSpawned: 1, aContributors: 2));

        PlaybookPhaseInvariants.Validate(vRow).Reasons
            .Should().Contain(aR => aR.Code == PlaybookPhaseInvariants.SpawnedBelowContributors);
    }

    /// <summary>Invariant 4 — observed activity cannot exceed the window it was observed in.</summary>
    [Fact]
    public void ActiveTimeAboveElapsedQuarantinesTheRow()
    {
        var vRow = Single(Line(aElapsedMs: 1000, aActiveMs: 84000));

        PlaybookPhaseInvariants.Validate(vRow).Reasons
            .Should().Contain(aR => aR.Code == PlaybookPhaseInvariants.ActiveOutsideWindow);
    }

    /// <summary>Invariant 5 — an incomplete window must be EOF-shaped, with no end and no duration.</summary>
    [Fact]
    public void IncompleteWindowCarryingAnEndBoundaryQuarantinesTheRow()
    {
        var vRow = Single(Line(aComplete: false, aEndReason: "idle", aElapsedMs: 120000));

        PlaybookPhaseInvariants.Validate(vRow).Reasons
            .Should().Contain(aR => aR.Code == PlaybookPhaseInvariants.IncompleteWindowNotEof);
    }

    /// <summary>
    /// Acceptance 12 — a start/end window with no finalized assistant turn is quarantined rather than
    /// displayed as a free run.
    /// </summary>
    [Fact]
    public void AWindowWithNoAssistantTurnIsQuarantinedRatherThanShownAsAFreeRun()
    {
        var vRow = Single(Line(aTurns: 0, aInput: 0, aOutput: 0, aReasoning: 0, aCacheRead: 0,
            aCacheWrite: 0, aCostUsd: 0m));

        var vReport = Report([vRow]);

        PlaybookPhaseInvariants.Validate(vRow).Reasons
            .Should().Contain(aR => aR.Code == PlaybookPhaseInvariants.NoFinalizedAssistantTurn);
        vReport.Executions.Single().IsQuarantined.Should().BeTrue();
        vReport.Tokens.Input.Kind.Should().Be(
            PhaseValueKind.Unavailable, "a quarantined zero is not a measured zero");
    }

    /// <summary>
    /// Acceptance 8 — a quarantined row stays visible with its reason and contributes to no total, even
    /// though the producer left zero-valued compatibility totals on it.
    /// </summary>
    [Fact]
    public void AQuarantinedRowStaysVisibleAndEntersNoTotal()
    {
        var vClean = Rows(Line("PE-1"), Line("PE-2"), Line("PE-3"));
        var vWithInvalid = Rows(
            Line("PE-1"), Line("PE-2"), Line("PE-3"),
            Line("PE-bad", aValid: false, aInput: 0, aOutput: 0, aReasoning: 0, aCacheRead: 0,
                aCacheWrite: 0, aTokensIn: 0, aTokensOut: 0));

        var vBefore = Report(vClean);
        var vAfter = Report(vWithInvalid);

        vAfter.Executions.Should().HaveCount(4, "a quarantined row is displayed, never dropped");
        vAfter.Executions.Single(aE => aE.PhaseExecutionId == "PE-bad").QuarantineReasons
            .Should().NotBeEmpty();
        vAfter.Tokens.Input.Display().Should().Be(vBefore.Tokens.Input.Display());
        vAfter.Tokens.N.Should().Be(vBefore.Tokens.N);
    }

    /// <summary>Acceptance 2 — an EOF window has no elapsed value and enters no duration cohort.</summary>
    [Fact]
    public void AnEofWindowContributesNoDuration()
    {
        var vRows = Rows(
            Line("PE-1"),
            Line("PE-eof", aComplete: false, aEndReason: "eof", aElapsedMs: null, aEndedAt: null));

        var vReport = Report(vRows);
        var vOpen = vReport.Executions.Single(aE => aE.PhaseExecutionId == "PE-eof");

        vOpen.IsQuarantined.Should().BeFalse("an open window is honest, not invalid");
        vOpen.ElapsedMs.Kind.Should().Be(PhaseValueKind.Unavailable);
        vOpen.ElapsedMs.Display().Should().Be(PlaybookPhaseVocabulary.Unavailable);
        vOpen.DataQualityNote.Should().Be(PlaybookPhaseVocabulary.OpenWindowMessage);
        vReport.ElapsedMsMedian.N.Should().Be(1);
        vReport.ElapsedMsMedian.Exclusions.Should().Contain(aE => aE.Code == "incomplete_window");
    }

    /// <summary>
    /// Acceptance 3 / ADR-027 — the two diagnostic component sums are published as text, so their sum is
    /// a compile-time absence rather than a rule someone has to remember.
    /// </summary>
    [Fact]
    public void TheDiagnosticComponentSumsCannotBeAdded()
    {
        var vDiagnostic = typeof(PhaseDiagnostic);

        vDiagnostic.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(aP => aP.PropertyType != typeof(string) && aP.PropertyType != typeof(Type))
            .Should().BeEmpty("a diagnostic carries no number, so no aggregate can accept one");
        vDiagnostic.GetMethod("op_Addition").Should().BeNull();

        var vView = typeof(PhaseExecutionView);
        vView.GetProperty("AssistantElapsed")!.PropertyType.Should().Be(vDiagnostic);
        vView.GetProperty("ToolElapsed")!.PropertyType.Should().Be(vDiagnostic);
        vView.GetProperty("ObservedActiveMs")!.PropertyType.Should().Be(typeof(PhaseValue),
            "the producer's union is the only active-time number published");
    }

    /// <summary>
    /// REQ-FN-097 — no member anywhere in the engine offers human effort, CPU time or utilization.
    /// </summary>
    [Fact]
    public void NothingPublishesHumanEffortCpuTimeOrUtilization()
    {
        string[] vForbidden = ["HumanEffort", "CpuTime", "CpuMs", "Utilisation", "Utilization"];

        var vOffenders = typeof(PlaybookPhaseReport).Assembly.GetTypes()
            .Where(aT => aT.IsPublic)
            .SelectMany(aT => aT.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(aM => $"{aT.Name}.{aM.Name}"))
            .Where(aName => vForbidden.Any(aWord => aName.Contains(aWord, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        vOffenders.Should().BeEmpty(
            "neither framework captures human effort, and a member that exists is one something "
            + "eventually populates by inference from wall-clock time (ADR-027)");
    }

    /// <summary>
    /// Acceptance 13 — a zero provider cost against non-zero tokens is <c>zero-unverified</c>: out of
    /// every measured total, and never rendered as <c>$0</c> or as free.
    /// </summary>
    [Fact]
    public void AZeroUnverifiedCostIsExcludedAndNeverRenderedAsZeroDollars()
    {
        var vRows = Rows(Line("PE-zero", aCostUsd: 0m, aCostStatus: "zero-unverified",
            aModelsJson: Models(("m-1", 0m, "zero-unverified"))));

        var vReport = Report(vRows);
        var vCost = vReport.Executions.Single().Cost;

        vCost.IsMeasured.Should().BeFalse();
        vCost.Display().Should().Be("zero-unverified");
        vCost.Display().Should().NotContain("0.00");
        vCost.Caveat.Should().Be(PlaybookPhaseVocabulary.ZeroUnverifiedCaveat);
        vReport.MeasuredCostUsd.Usd.Should().BeNull("nothing measured is not the same as zero dollars");
        vReport.MeasuredCostUsd.Exclusions
            .Should().Contain(aE => aE.Code == "cost_status_zero_unverified" && aE.Records == 1);
    }

    /// <summary>One model short of <c>complete</c> makes the whole phase's cost partial, not complete.</summary>
    [Fact]
    public void OneIncompleteModelCostMakesThePhaseCostPartial()
    {
        var vParsed = Parse(Line("PE-mixed",
            aModelsJson: Models(("m-1", 0.30m, "complete"), ("m-2", 0m, "zero-unverified"))));

        var vReport = PlaybookPhaseReport.Build(
            OpenCode, vParsed.PhaseExecutions, vParsed.PhaseModelUsages, vParsed.PhaseSubagents);

        PlaybookPhaseReport.CostStatusOf(vParsed.PhaseExecutions[0], vParsed.PhaseModelUsages)
            .Should().Be("partial");
        vReport.Executions.Single().Cost.IsMeasured.Should().BeFalse();
        vReport.MeasuredCostUsd.Usd.Should().BeNull();
    }

    /// <summary>
    /// Acceptance 5 — a mixed-model phase contributes each model's own tokens, and no figure reads the
    /// dominant-model label.
    /// </summary>
    [Fact]
    public void AMixedModelExecutionContributesEachModelsOwnTokens()
    {
        var vParsed = Parse(Line("PE-mixed",
            aModelsJson: Models(("dominant", 0.30m, "complete", 700L), ("quiet", 0.11m, "complete", 200L))));

        var vReport = PlaybookPhaseReport.Build(
            OpenCode, vParsed.PhaseExecutions, vParsed.PhaseModelUsages, vParsed.PhaseSubagents);

        vReport.Models.Should().HaveCount(2);
        vReport.Models.Single(aM => aM.Model == "dominant").TokensOut.Should().Be(700L);
        vReport.Models.Single(aM => aM.Model == "quiet").TokensOut.Should().Be(200L);
        vReport.Models.Sum(aM => aM.TokensOut).Should().Be(900L,
            "each model carries its own tokens, never the whole phase under its winner");
    }

    /// <summary>A model filter matches any <c>models[]</c> member, not only the dominant one.</summary>
    [Fact]
    public void AModelFilterMatchesTheNonDominantModel()
    {
        var vParsed = Parse(Line("PE-mixed", aDominantModel: "dominant",
            aModelsJson: Models(("dominant", 0.30m, "complete", 700L), ("quiet", 0.11m, "complete", 200L))));

        var vReport = PlaybookPhaseReport.Build(
            OpenCode, vParsed.PhaseExecutions, vParsed.PhaseModelUsages, vParsed.PhaseSubagents);

        vReport.WhereModel("quiet").Should().ContainSingle(aE => aE.PhaseExecutionId == "PE-mixed");
        vReport.WhereModel("dominant").Should().ContainSingle();
    }

    /// <summary>
    /// Acceptance 6 — three spawned children with one contributor display <c>1 / 3</c>, all three appear
    /// in the detail, and the difference is a zero-token child rather than an inferred failure.
    /// </summary>
    [Fact]
    public void SpawnedMinusContributorsIsAZeroTokenChildNeverAFailure()
    {
        var vParsed = Parse(Line("PE-fanout", aSpawned: 3, aContributors: 1,
            aSessionsJson: """
                [{"session_id":"c1","parent_session_id":"root","agent":"builder","tokens_out":120},
                 {"session_id":"c2","parent_session_id":"root","tokens_out":0},
                 {"session_id":"c3","parent_session_id":"root","tokens_out":0}]
                """));

        var vReport = PlaybookPhaseReport.Build(
            OpenCode, vParsed.PhaseExecutions, vParsed.PhaseModelUsages, vParsed.PhaseSubagents);

        var vFanout = vReport.Executions.Single().Fanout;

        vFanout.Display().Should().Be("1 / 3");
        vFanout.NonContributing.Should().Be(2);
        vFanout.NonContributingLabel.Should().Be(PlaybookPhaseVocabulary.NonContributingChildLabel);
        vFanout.NonContributingLabel.Should().NotContain("fail");
        vFanout.Tree.Should().HaveCount(3, "a zero-token child is still a spawned child");
        vFanout.Tree.Single(aN => aN.SessionId == "c2").AgentDisplay
            .Should().Be(PlaybookPhaseVocabulary.Unavailable, "an absent agent type is never inferred");
    }

    /// <summary>
    /// Acceptance 7 — a recursive grandchild renders beneath its parent and is counted exactly once;
    /// child usage is never summed onto the phase totals, which already contain it.
    /// </summary>
    [Fact]
    public void ARecursiveGrandchildIsCountedExactlyOnce()
    {
        var vParsed = Parse(Line("PE-tree", aSpawned: 2, aContributors: 2,
            aSessionsJson: """
                [{"session_id":"c1","parent_session_id":"root","tokens_out":120,
                  "sessions":[{"session_id":"g1","tokens_out":40}]}]
                """));

        var vWithout = Report(Rows(Line("PE-tree", aSpawned: 2, aContributors: 2)));
        var vReport = PlaybookPhaseReport.Build(
            OpenCode, vParsed.PhaseExecutions, vParsed.PhaseModelUsages, vParsed.PhaseSubagents);

        vParsed.PhaseSubagents.Should().HaveCount(2);
        var vTree = vReport.Executions.Single().Fanout.Tree;
        vTree.Should().ContainSingle("the grandchild renders beneath its parent, not beside it");
        vTree.Single().Children.Single().SessionId.Should().Be("g1");
        vReport.Tokens.Output.Display().Should().Be(vWithout.Tokens.Output.Display(),
            "child usage is already inside the phase totals and is never added again");
    }

    /// <summary>
    /// REQ-FN-094 / BRD-163 — absence, EOF, malformed input and an unsupported harness each yield no
    /// run at all, never a run of zeroes.
    /// </summary>
    [Fact]
    public void AbsenceMalformedInputAndAnUnsupportedHarnessYieldNoRun()
    {
        var vAbsent = Parse(string.Empty);
        var vMalformed = Parse("{not json\n{\"kind\":\"something-else\"}");

        vAbsent.PhaseExecutions.Should().BeEmpty();
        vMalformed.PhaseExecutions.Should().BeEmpty();
        vMalformed.InvalidLines.Should().Be(2);

        var vReport = PlaybookPhaseReport.Build("claude-code", [], [], []);

        vReport.Harness.IsSupported.Should().BeFalse();
        vReport.Harness.Message.Should().Be(PlaybookPhaseVocabulary.UnsupportedHarnessMessage);
        vReport.ElapsedMsMedian.Value.Kind.Should().Be(PhaseValueKind.Unavailable);
        vReport.ObservedActiveMsTotal.Value.Kind.Should().Be(PhaseValueKind.Unavailable);
        vReport.Tokens.Output.Display().Should().Be(PlaybookPhaseVocabulary.Unavailable);
        vReport.MeasuredCostUsd.Usd.Should().BeNull();
    }

    /// <summary>
    /// REQ-FN-098 — the dimension is the <b>command phase</b>, and no window is split between
    /// conceptual stages.
    /// </summary>
    [Fact]
    public void TheDimensionIsTheCommandPhase()
    {
        var vReport = Report(Rows(Line("PE-1", aPhase: "implement"), Line("PE-2", aPhase: "verify")));

        PhaseCommandGroup.DimensionLabel.Should().Be("Command phase");
        PhaseCommandGroup.DimensionKey.Should().Be("command_phase");
        vReport.CommandPhases.Select(aG => aG.CommandPhase).Should().BeEquivalentTo(["implement", "verify"]);
        vReport.CommandPhases.Sum(aG => aG.Executions).Should().Be(2,
            "one command window is one row and is never divided between the stages inside it");
    }

    /// <summary>
    /// REQ-FN-098 — a whole-task total needs an explicit cohort; a reused session id is not one.
    /// </summary>
    [Fact]
    public void AWholeTaskTotalRequiresAnExplicitCohort()
    {
        var vRows = Rows(Line("PE-1"), Line("PE-2"));

        PlaybookPhaseReport.TaskElapsedMsTotal(null, vRows).Value.Kind
            .Should().Be(PhaseValueKind.Unavailable);

        PhaseTaskCohort.TryCreate(Repo, null, ["PE-1"], null, null, out _)
            .Should().BeFalse("a checklist identity is part of the cohort");
        PhaseTaskCohort.TryCreate(Repo, "F-EFFORT", null, null, null, out _)
            .Should().BeFalse("a session id is not a cohort, and neither is nothing at all");

        PhaseTaskCohort.TryCreate(Repo, "F-EFFORT", ["PE-1", "PE-2"], null, null, out var vCohort)
            .Should().BeTrue();
        PlaybookPhaseReport.TaskElapsedMsTotal(vCohort, vRows).Value.TryGetValue(out var vTotal)
            .Should().BeTrue();
        vTotal.Should().Be(240000d);
    }

    /// <summary>
    /// REQ-FN-097 — partial coverage renders an explicit lower bound and unavailable coverage renders no
    /// figure at all; neither reaches an active-time comparison.
    /// </summary>
    [Fact]
    public void PartialCoverageIsALowerBoundAndUnavailableCoverageRendersNothing()
    {
        var vReport = Report(Rows(
            Line("PE-complete"),
            Line("PE-partial", aCoverage: "partial"),
            Line("PE-none", aCoverage: "unavailable")));

        var vPartial = vReport.Executions.Single(aE => aE.PhaseExecutionId == "PE-partial");
        var vNone = vReport.Executions.Single(aE => aE.PhaseExecutionId == "PE-none");

        vPartial.ObservedActiveMs.Display().Should().StartWith(PlaybookPhaseVocabulary.LowerBoundPrefix);
        vPartial.DataQualityNote.Should().Be(PlaybookPhaseVocabulary.PartialCoverageMessage);
        vNone.ObservedActiveMs.Kind.Should().Be(PhaseValueKind.Unavailable);
        vReport.ObservedActiveMsTotal.N.Should().Be(1, "only complete coverage enters the comparison");
        vReport.ObservedActiveMsTotal.Exclusions
            .Should().Contain(aE => aE.Code == "active_coverage_partial" && aE.Records == 1);
        vReport.Executions.Should().HaveCount(3, "every row stays visible in the table");
    }

    /// <summary>REQ-FN-102 — a comparative cohort below three records refuses to be a number.</summary>
    [Fact]
    public void ACohortBelowThreeRecordsRendersInsufficientData()
    {
        var vTwo = Report(Rows(Line("PE-1"), Line("PE-2")));
        var vThree = Report(Rows(Line("PE-1"), Line("PE-2"), Line("PE-3")));

        vTwo.ElapsedMsMedian.Value.Kind.Should().Be(PhaseValueKind.InsufficientData);
        vTwo.ElapsedMsMedian.Value.Display().Should().Be("insufficient data (n=2)");
        vThree.ElapsedMsMedian.Value.Kind.Should().Be(PhaseValueKind.Measured);
        vThree.ElapsedMsMedian.Caption.Should().StartWith("n=3");
    }

    /// <summary>
    /// REQ-FN-102 — a <c>legacy-unverified</c> schema-1 row stays reachable and out of every schema-2
    /// comparison.
    /// </summary>
    [Fact]
    public void LegacyUnverifiedRowsStayOutOfSchemaTwoComparisons()
    {
        var vRows = Rows(Line("PE-1"), Line("PE-legacy", aSchema: 1));
        var vReport = Report(vRows);

        vRows.Single(aR => aR.PhaseExecutionId == "PE-legacy").TokenStatus
            .Should().Be(PlaybookPhaseAdapter.LegacyUnverified);
        vReport.Executions.Should().HaveCount(2, "a legacy row is reachable by drill-down");
        vReport.Tokens.N.Should().Be(1, "and absent from a schema-2 token total");
        vReport.Tokens.Exclusions.Should().Contain(aE => aE.Code == "legacy_unverified" && aE.Records == 1);
    }

    /// <summary>
    /// REQ-FN-094 — every normalized row retains the schema, the harness, the importer version, the
    /// repository identity and the import timestamp.
    /// </summary>
    [Fact]
    public void EveryRowRetainsItsProvenance()
    {
        var vRow = Single(Line());

        vRow.SourceSchema.Should().Be(2);
        vRow.SourceHarness.Should().Be(OpenCode);
        vRow.UserId.Should().Be(UserId);
        vRow.Repo.Should().Be(Repo);
        vRow.ImportedAt.Should().NotBeNullOrWhiteSpace();
        vRow.ImportedAt.Should().EndWith("Z", "storage and filtering are UTC; only display is localized");
        vRow.Overflow.Should().Contain(PlaybookPhaseAdapter.ImporterVersion);
        vRow.Overflow.Should().Contain(Sha);
        vRow.Overflow.Should().Contain("timestamp", "a field with no column is preserved verbatim");
    }

    /// <summary>
    /// REQ-FN-094 / BRD-132 — the phase stream is one entry in the import file-name table, on the
    /// Playbook axis, and it reaches the store through the one shared parser.
    /// </summary>
    [Fact]
    public void ThePhaseStreamIsOneEntryInTheImportTable()
    {
        ImportStreamCatalog.TryRecognise("verification/telemetry/phase-metrics.ndjson", out var vStream)
            .Should().BeTrue();
        vStream.Should().Be(StreamNames.PhaseMetrics);
        ImportStreamCatalog.TryResolveKind(vStream, out var vKind).Should().BeTrue();
        vKind.Should().Be(StreamKind.PhaseMetrics);
        ImportStreamCatalog.TryResolveFramework([vStream], out var vFramework).Should().BeTrue();
        vFramework.Should().Be(FrameworkNames.Playbook);
        ImportStreamCatalog.TryResolveFramework([vStream, StreamNames.Runs], out _)
            .Should().BeFalse("a bundle mixing the two axes describes two sources");

        StreamNames.Playbook.Should().NotContain(StreamNames.PhaseMetrics,
            "the fetch path has nothing to fetch — the producer's input file is transient (ADR-023)");
        PlaybookStreamFiles.Files.Should().NotContain("phase-metrics.ndjson",
            "there is no second fetch or ingest code path for this record type (BRD-132)");
    }

    /// <summary>Parses one NDJSON text through the shared parser, exactly as an import does.</summary>
    /// <param name="aText">The NDJSON text.</param>
    /// <returns>The parse result.</returns>
    private static ParseResult Parse(string aText) =>
        new StreamParser().Parse(UserId, Repo, Sha, StreamKind.PhaseMetrics, aText);

    /// <summary>Parses several lines into their execution rows.</summary>
    /// <param name="aLines">The NDJSON lines.</param>
    /// <returns>The execution rows.</returns>
    private static IReadOnlyList<PbPhaseExecutionRecord> Rows(params string[] aLines) =>
        Parse(string.Join('\n', aLines)).PhaseExecutions;

    /// <summary>Parses one line into its single execution row.</summary>
    /// <param name="aLine">The NDJSON line.</param>
    /// <returns>The execution row.</returns>
    private static PbPhaseExecutionRecord Single(string aLine) => Rows(aLine).Single();

    /// <summary>Builds a report over execution rows with no child rows.</summary>
    /// <param name="aRows">The execution rows.</param>
    /// <returns>The report.</returns>
    private static PlaybookPhaseReport Report(IReadOnlyList<PbPhaseExecutionRecord> aRows) =>
        PlaybookPhaseReport.Build(OpenCode, aRows, [], []);

    /// <summary>Renders a <c>models[]</c> array from name, cost, status and optional output tokens.</summary>
    /// <param name="aModels">The models to render.</param>
    /// <returns>The JSON array text.</returns>
    private static string Models(params (string Name, decimal Cost, string Status)[] aModels) =>
        Models(aModels.Select(aM => (aM.Name, aM.Cost, aM.Status, 900L)).ToArray());

    /// <summary>Renders a <c>models[]</c> array including each model's own output tokens.</summary>
    /// <param name="aModels">The models to render.</param>
    /// <returns>The JSON array text.</returns>
    private static string Models(params (string Name, decimal Cost, string Status, long TokensOut)[] aModels) =>
        "[" + string.Join(",", aModels.Select(aM =>
            $$"""
            {"model":"{{aM.Name}}","turns":6,"tokens":{"input":1,"output":1,"reasoning":0,"cache_read":0,"cache_write":0},"tokens_in":1,"tokens_out":{{aM.TokensOut}},"cost_usd":{{Money(aM.Cost)}},"cost_status":"{{aM.Status}}","active_ms":100}
            """)) + "]";

    /// <summary>Renders a decimal the way the producer writes one.</summary>
    /// <param name="aValue">The amount.</param>
    /// <returns>The invariant text.</returns>
    private static string Money(decimal aValue) => aValue.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds one canonical schema-2 <c>phase-metric</c> line, with every clause overridable.
    /// </summary>
    /// <param name="aId">The phase execution id.</param>
    /// <param name="aPhase">The command that ran.</param>
    /// <param name="aComplete">Whether the window closed.</param>
    /// <param name="aEndReason">Why it ended.</param>
    /// <param name="aEndedAt">The end boundary, or <c>null</c> on an open window.</param>
    /// <param name="aElapsedMs">Wall-clock duration, or <c>null</c> on an open window.</param>
    /// <param name="aInput">Input tokens.</param>
    /// <param name="aOutput">Output tokens.</param>
    /// <param name="aReasoning">Reasoning tokens.</param>
    /// <param name="aCacheRead">Cache-read tokens.</param>
    /// <param name="aCacheWrite">Cache-write tokens.</param>
    /// <param name="aTokensIn">The input-side compatibility total; defaults to the true sum.</param>
    /// <param name="aTokensOut">The output-side compatibility total; defaults to the true sum.</param>
    /// <param name="aTurns">Finalized assistant turns.</param>
    /// <param name="aActiveMs">The producer's unioned observed active time.</param>
    /// <param name="aCoverage">The active coverage word.</param>
    /// <param name="aValid">The producer's own verdict.</param>
    /// <param name="aTokenStatus">Completeness of the token window.</param>
    /// <param name="aCostStatus">Completeness of the cost figure.</param>
    /// <param name="aCostUsd">Provider cost.</param>
    /// <param name="aSpawned">Sub-agent sessions launched.</param>
    /// <param name="aContributors">Sub-agent sessions that produced tokens.</param>
    /// <param name="aModelsJson">The <c>models[]</c> array.</param>
    /// <param name="aSessionsJson">The <c>subagents.sessions[]</c> array.</param>
    /// <param name="aDominantModel">The dominant-model label.</param>
    /// <param name="aHarness">The harness that produced the record.</param>
    /// <param name="aSchema">The declared schema version.</param>
    /// <returns>One NDJSON line.</returns>
    private static string Line(
        string aId = "PE-1",
        string aPhase = "verify",
        bool aComplete = true,
        string? aEndReason = "idle",
        string? aEndedAt = "2026-08-31T09:12:00.000Z",
        long? aElapsedMs = 120000,
        long aInput = 31203,
        long aOutput = 7900,
        long aReasoning = 1220,
        long aCacheRead = 16000,
        long aCacheWrite = 1010,
        long? aTokensIn = null,
        long? aTokensOut = null,
        int aTurns = 14,
        long aActiveMs = 84000,
        string aCoverage = "complete",
        bool aValid = true,
        string aTokenStatus = "complete",
        string aCostStatus = "complete",
        decimal aCostUsd = 0.41m,
        int aSpawned = 3,
        int aContributors = 2,
        string? aModelsJson = null,
        string aSessionsJson = "[]",
        string aDominantModel = "anthropic/claude-sonnet-5",
        string aHarness = OpenCode,
        int aSchema = 2) =>
        $$"""
        {"schema":{{aSchema}},"kind":"phase-metric","phase_execution_id":"{{aId}}","phase":"{{aPhase}}",
         "started_at":"2026-08-31T09:10:00.000Z","ended_at":{{Json(aEndedAt)}},
         "elapsed_ms":{{aElapsedMs?.ToString(CultureInfo.InvariantCulture) ?? "null"}},
         "complete":{{(aComplete ? "true" : "false")}},"end_reason":{{Json(aEndReason)}},
         "model":"{{aDominantModel}}","models":{{aModelsJson ?? SoleModel}},
         "tokens":{"input":{{aInput}},"output":{{aOutput}},"reasoning":{{aReasoning}},"cache_read":{{aCacheRead}},"cache_write":{{aCacheWrite}}},
         "tokens_in":{{aTokensIn ?? aInput + aCacheRead + aCacheWrite}},
         "tokens_out":{{aTokensOut ?? aOutput + aReasoning}},
         "cost_usd":{{Money(aCostUsd)}},"attempt":2,"gate_verdict":"FAIL","project_type":"dotnet-react",
         "timestamp":"2026-08-31T09:12:00.000Z","session_id":"ses_123","harness":"{{aHarness}}",
         "granularity":"message","turns":{{aTurns}},"tier":"heavy",
         "observed_active_effort":{"assistant_elapsed_ms":78000,"tool_elapsed_ms":31000,"observed_active_ms":{{aActiveMs}},"coverage":"{{aCoverage}}"},
         "data_quality":{"valid":{{(aValid ? "true" : "false")}},"issues":[],"token_status":"{{aTokenStatus}}","cost_status":"{{aCostStatus}}"},
         "tokens_scope":"tree",
         "subagents":{"count":{{aContributors}},"spawned":{{aSpawned}},"contributors":{{aContributors}},"cost_status":"{{aCostStatus}}","sessions":{{aSessionsJson}} } }
        """.ReplaceLineEndings(" ");

    /// <summary>Renders an optional string as a JSON value.</summary>
    /// <param name="aValue">The value, or <c>null</c>.</param>
    /// <returns>The quoted text, or <c>null</c>.</returns>
    private static string Json(string? aValue) => aValue is null ? "null" : $"\"{aValue}\"";
}
