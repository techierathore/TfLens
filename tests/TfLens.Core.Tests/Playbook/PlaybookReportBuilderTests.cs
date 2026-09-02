using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Playbook;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-067 / BRD-75 — the Playbook-native report data: phase totals keyed by the process gate, the
/// main-vs-subagent split via <c>parentID</c>, and tokens by model.
/// </summary>
/// <remarks>
/// Every figure here is computed from <c>Fixtures/Playbook/events-synthetic.ndjson</c>, which is
/// hand-written to the emitter's exact shape and is <b>not</b> a captured run — see that directory's
/// README and <c>DECISIONS.md</c> S-001. The expected numbers are worked out in the README so a change
/// to the fixture cannot quietly move them.
/// </remarks>
public sealed class PlaybookReportBuilderTests
{
    /// <summary>The parsed fixture, deduped exactly as a real sync would leave it in <c>"PbEvent"</c>.</summary>
    private static readonly ParseResult Parsed = new StreamParser().Parse(
        Fixtures.DemoUserId,
        "techierathore/AI-First-Playbook",
        Fixtures.SourceSha,
        StreamKind.Events,
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Playbook", "events-synthetic.ndjson")));

    /// <summary>The analysis under test.</summary>
    private static readonly PlaybookAnalysis Analysis =
        PlaybookReportBuilder.Build(Fixtures.DemoUserId, Parsed.PbEvents);

    /// <summary>A streaming message's repeated turn rows collapse to one, so tokens are not multiplied.</summary>
    [Fact]
    public void RepeatedTurnRowsCollapseOnMessageId()
    {
        Parsed.PbEvents.Should().HaveCount(7);
        Parsed.DuplicatesCollapsed.Should().Be(1);
    }

    /// <summary>A line that is not JSON is counted and skipped rather than failing the parse.</summary>
    [Fact]
    public void MalformedLineIsCountedAndSkipped()
    {
        Parsed.InvalidLines.Should().Be(1);
    }

    /// <summary>The collapsed turn keeps the largest snapshot, not the first partial one.</summary>
    [Fact]
    public void CollapsedTurnKeepsTheLargestSnapshot()
    {
        var vTurn = Parsed.PbEvents.Single(aE => aE.MessageId == "msg-01");

        vTurn.TokensOutput.Should().Be(200);
        vTurn.CostUsd.Should().Be(0.04m);
    }

    /// <summary>The process gate is carried from the enclosing <c>phase-start</c> onto every record.</summary>
    [Fact]
    public void PhaseGateIsDerivedFromTheEnclosingPhaseStart()
    {
        Parsed.PbEvents.Single(aE => aE.MessageId == "msg-01").PhaseGate.Should().Be("verify");
        Parsed.PbEvents.Single(aE => aE.MessageId == "msg-02").PhaseGate.Should().Be("verify");
        Parsed.PbEvents.Single(aE => aE.MessageId == "msg-03").PhaseGate.Should().Be("plan-review");
    }

    /// <summary>Phase totals are keyed by the process gate, busiest first.</summary>
    [Fact]
    public void PhaseTotalsAreKeyedByProcessGate()
    {
        Analysis.PhaseTotals.Select(aT => aT.PhaseGate.Name).Should().Equal("verify", "plan-review");
    }

    /// <summary>The verify phase totals its events, sessions, tokens and measured spend.</summary>
    [Fact]
    public void VerifyPhaseTotalsAreComputed()
    {
        var vVerify = Analysis.PhaseTotals.Single(aT => aT.PhaseGate.Name == "verify");

        vVerify.Events.Should().Be(4);
        vVerify.Sessions.Should().Be(2);
        vVerify.Tokens.Should().Be(505);
        vVerify.CostUsd.Should().Be(0.045m);
    }

    /// <summary>The plan-review phase totals its events, sessions, tokens and measured spend.</summary>
    [Fact]
    public void PlanReviewPhaseTotalsAreComputed()
    {
        var vPlan = Analysis.PhaseTotals.Single(aT => aT.PhaseGate.Name == "plan-review");

        vPlan.Events.Should().Be(3);
        vPlan.Sessions.Should().Be(1);
        vPlan.Tokens.Should().Be(360);
        vPlan.CostUsd.Should().Be(0.020m);
    }

    /// <summary>A session carrying a parent id is counted as a sub-agent, its parent as main.</summary>
    [Fact]
    public void ParentIdSplitsMainFromSubagent()
    {
        Analysis.AgentSplit.MainSessions.Should().Be(2);
        Analysis.AgentSplit.SubagentSessions.Should().Be(1);
        Analysis.AgentSplit.UnresolvedParentSessions.Should().Be(0);
    }

    /// <summary>Tokens follow the split, using the Playbook joiner's own input and output legs.</summary>
    [Fact]
    public void SplitTokensFollowTheJoinerLegs()
    {
        Analysis.AgentSplit.MainTokens.Should().Be(725);
        Analysis.AgentSplit.SubagentTokens.Should().Be(140);
        Analysis.AgentSplit.TokensTotal.Should().Be(865);
    }

    /// <summary>The sub-agent share renders as the reference's whole percentage.</summary>
    [Fact]
    public void SubagentShareRendersAsAPercentage()
    {
        Analysis.AgentSplit.SubagentTokenShare.Display().Should().Be("16%");
    }

    /// <summary>A sub-agent whose parent no event ever reports is counted, not promoted to main.</summary>
    [Fact]
    public void OrphanParentChainIsCountedUnresolved()
    {
        var vEvents = new[]
        {
            Event("turn", "ses-lost", "ses-never-seen", "msg-x", 10),
            Event("turn", "ses-lost", "ses-never-seen", "msg-y", 20)
        };

        var vSplit = PlaybookReportBuilder.Build(Fixtures.DemoUserId, vEvents).AgentSplit;

        vSplit.MainSessions.Should().Be(0);
        vSplit.SubagentSessions.Should().Be(1);
        vSplit.UnresolvedParentSessions.Should().Be(1);
    }

    /// <summary>A sub-agent of a sub-agent resolves through the chain to the main session.</summary>
    [Fact]
    public void NestedSubagentResolvesThroughTheChain()
    {
        var vEvents = new[]
        {
            Event(PlaybookEventKinds.PhaseStart, "ses-root", null, null, 0),
            Event("turn", "ses-root", null, "msg-a", 10),
            Event("turn", "ses-child", "ses-root", "msg-b", 20),
            Event("turn", "ses-grandchild", "ses-child", "msg-c", 30)
        };

        var vSplit = PlaybookReportBuilder.Build(Fixtures.DemoUserId, vEvents).AgentSplit;

        vSplit.MainSessions.Should().Be(1);
        vSplit.SubagentSessions.Should().Be(2);
        vSplit.UnresolvedParentSessions.Should().Be(0);
    }

    /// <summary>Tokens are totalled per observed model, heaviest first.</summary>
    [Fact]
    public void TokensAreTotalledByModel()
    {
        Analysis.TokensByModel.Select(aM => aM.Model)
            .Should().Equal("anthropic/claude-sonnet-5", "anthropic/claude-haiku-5");
        Analysis.TokensByModel[0].Total.Should().Be(725);
        Analysis.TokensByModel[1].Total.Should().Be(140);
    }

    /// <summary>The three questions render an em dash, because the stream carries no verdict at all.</summary>
    [Fact]
    public void GateOutcomesAreNotApplicableWithoutAVerdictField()
    {
        Analysis.PhaseQuestions.Should().NotBeEmpty();
        Analysis.PhaseQuestions.Should().OnlyContain(aQ =>
            aQ.FirstPassRate.Kind == FigureKind.NotApplicable
            && aQ.CatchShare.Kind == FigureKind.NotApplicable
            && aQ.EscapeRate.Kind == FigureKind.NotApplicable
            && aQ.UnavailableReason != null);
    }

    /// <summary>
    /// Below the minimum n the sub-agent share refuses to state a number, in the engine's own words.
    /// </summary>
    /// <remarks>
    /// The refusal is the <see cref="Figure"/> type's, not a check repeated here: REQ-FN-067's acceptance
    /// is that the Playbook figures obey the <i>same</i> minimum-n rule as the TechieFlow engine, and
    /// they do so by being the same type rather than by agreeing with it.
    /// </remarks>
    [Fact]
    public void ShareBelowTheMinimumNRefusesToStateANumber()
    {
        var vEvents = new[]
        {
            Event(PlaybookEventKinds.PhaseStart, "ses-root", null, null, 0),
            Event("turn", "ses-root", null, "msg-a", 80),
            Event("turn", "ses-child", "ses-root", "msg-b", 20)
        };

        var vSplit = PlaybookReportBuilder.Build(Fixtures.DemoUserId, vEvents).AgentSplit;

        vSplit.SessionsTotal.Should().BeLessThan(MetricsConstants.MinN);
        vSplit.SubagentTokenShare.Kind.Should().Be(FigureKind.InsufficientData);
        vSplit.SubagentTokenShare.Display().Should().Be("insufficient data (n=2)");
    }

    /// <summary>At the minimum n the same share states the number, so the refusal is the rule and not a wall.</summary>
    [Fact]
    public void ShareAtTheMinimumNStatesTheNumber()
    {
        var vEvents = new[]
        {
            Event(PlaybookEventKinds.PhaseStart, "ses-root", null, null, 0),
            Event("turn", "ses-root", null, "msg-a", 80),
            Event("turn", "ses-child", "ses-root", "msg-b", 10),
            Event("turn", "ses-other", "ses-root", "msg-c", 10)
        };

        var vSplit = PlaybookReportBuilder.Build(Fixtures.DemoUserId, vEvents).AgentSplit;

        vSplit.SessionsTotal.Should().Be(MetricsConstants.MinN);
        vSplit.SubagentTokenShare.Display().Should().Be("20%");
    }

    /// <summary>A phase whose events carry no cost renders an em dash, never a manufactured zero.</summary>
    [Fact]
    public void AbsentCostRendersAsAnEmDash()
    {
        var vEvents = new[] { Event("turn", "ses-a", null, "msg-a", 10) };

        var vTotals = PlaybookReportBuilder.Build(Fixtures.DemoUserId, vEvents).PhaseTotals.Single();

        vTotals.CostUsd.Should().BeNull();
    }

    /// <summary>The result names its own framework and can never claim to be a TechieFlow one.</summary>
    [Fact]
    public void AnalysisIsAlwaysTaggedPlaybook()
    {
        Analysis.Framework.Should().Be(FrameworkNames.Playbook);
        typeof(PlaybookAnalysis).GetProperty("Framework")!.CanWrite.Should().BeFalse();
    }

    /// <summary>The result carries its schema caveat so a page or an export cannot drop it.</summary>
    [Fact]
    public void AnalysisCarriesItsSchemaCaveat()
    {
        Analysis.SchemaStatus.Should().Be(PlaybookSchemaStatus.EmitterSourceDerived);
        Analysis.ProvisionalNotes.Should().NotBeEmpty();
    }

    /// <summary>The export payload keys are all Playbook-scoped, so a snapshot cannot collide with TechieFlow's.</summary>
    [Fact]
    public void ExportPayloadKeysAreFrameworkScoped()
    {
        var vPayload = Analysis.ToExportPayload();

        vPayload.Should().NotBeEmpty();
        vPayload.Should().OnlyContain(aP => aP.Key.StartsWith("playbook.", StringComparison.Ordinal));
        vPayload.Should().Contain(aP => aP.Key == "playbook.phaseGate.verify.tokens" && aP.Value == "505");
    }

    /// <summary>
    /// Builds one event record for the split tests.
    /// </summary>
    /// <param name="aKind">The record kind.</param>
    /// <param name="aSessionId">The session the record belongs to.</param>
    /// <param name="aParentId">The parent session, or <c>null</c> for a main session.</param>
    /// <param name="aMessageId">The message id, or <c>null</c> for a marker record.</param>
    /// <param name="aOutput">Output tokens.</param>
    /// <returns>The record.</returns>
    private static PbEventRecord Event(
        string aKind, string aSessionId, string? aParentId, string? aMessageId, int aOutput) =>
        new()
        {
            UserId = Fixtures.DemoUserId,
            Repo = "techierathore/AI-First-Playbook",
            SourceSha = Fixtures.SourceSha,
            Ts = "2026-08-26T09:00:00.000Z",
            Kind = aKind,
            PhaseGate = "verify",
            SessionId = aSessionId,
            ParentId = aParentId,
            MessageId = aMessageId,
            TokensOutput = aOutput
        };
}
