using System.Reflection;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Playbook;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-103 / REQ-FN-104 (BRD-164, BRD-165, ADR-024) — the Playbook's normalized miss export lands in
/// the existing miss tables, keyed on the source line, with both cross-edition axes kept apart.
/// </summary>
/// <remarks>
/// The six numbered tests below are the producer contract's own acceptance list
/// (<c>docs/Miss-Telemetry-TfLens-From-AIFP.md</c> §5) taken verbatim; acceptance 6 — no actor-grouped
/// reporting — is a property of the whole tree rather than of one class and is proved by the repo-wide
/// <c>ActorGroupingTests</c> guardrail, which scans this code too.
/// </remarks>
public sealed class PlaybookMissNormalizerTests
{
    /// <summary>The user every fixture export is attributed to.</summary>
    private const int UserId = 41;

    /// <summary>The bundle sha256 the fixture export arrives under.</summary>
    private const string Sha = "bundle-sha";

    /// <summary>A window and quality block the producer vouched for entirely.</summary>
    private const string GoodWindow =
        "\"source_window\":{\"complete\":true,\"valid\":true},"
        + "\"data_quality\":{\"valid\":true,\"cost_status\":\"complete\"}";

    /// <summary>
    /// Acceptance 1 — re-importing an unchanged export produces the same rows, not twice as many.
    /// </summary>
    [Fact]
    public void ReimportDoesNotDuplicateLifecycleRecords()
    {
        var vExport = string.Join('\n', MissLine("PB-1"), FixLine("PB-1", "sole"), AmendLine("PB-1", "other"));

        var vFirst = PlaybookMissNormalizer.Normalize(Repo(), Sha, vExport);
        var vSecond = PlaybookMissNormalizer.Normalize(Repo(), Sha, vExport);

        vSecond.Parsed.Misses.Select(aM => aM.SourceLineHash)
            .Should().Equal(vFirst.Parsed.Misses.Select(aM => aM.SourceLineHash));
    }

    /// <summary>
    /// Acceptance 1, the within-file half — a line repeated inside one export collapses and is counted.
    /// </summary>
    [Fact]
    public void RepeatedSourceLineCollapsesAndIsCounted()
    {
        var vLine = MissLine("PB-1");

        var vResult = PlaybookMissNormalizer.Normalize(Repo(), Sha, vLine + "\n" + vLine);

        vResult.Parsed.Misses.Should().HaveCount(1);
        vResult.DuplicateSourceLines.Should().Be(1);
    }

    /// <summary>
    /// The key is the source line, not the miss id — a corrected line is a second fact, not a collision.
    /// </summary>
    [Fact]
    public void ChangedLineForTheSameMissIdKeepsItsOwnIdentity()
    {
        var vExport = string.Join(
            '\n',
            MissLine("PB-1", aMissClass: "unspecified-gap"),
            MissLine("PB-1", aMissClass: "wrong-implementation"));

        var vResult = PlaybookMissNormalizer.Normalize(Repo(), Sha, vExport);

        vResult.Parsed.Misses.Select(aM => aM.SourceLineHash).Distinct().Should().HaveCount(2);
        vResult.Parsed.Misses[0].MissClass.Should().Be("unspecified-gap", "stream order is preserved");
    }

    /// <summary>
    /// Acceptance 2 — an amendment reaches the <c>why_missed</c> distribution, so folding ran first.
    /// </summary>
    [Fact]
    public void AmendmentsFoldBeforeWhyMissedDistributions()
    {
        var vReport = ReportOf(
            MissLine("PB-1"),
            AmendLine("PB-1", "instruction-ignored"));

        vReport.WhyMissedDistribution.Should().ContainSingle()
            .Which.Key.Should().Be("instruction-ignored");
    }

    /// <summary>
    /// Acceptance 3 — an invalid amendment, an orphan amendment, an orphan fix and an attempted
    /// overwrite all stay visible instead of being applied or dropped.
    /// </summary>
    [Fact]
    public void InvalidOrphanAndOverwriteAmendmentsRemainVisibleDiagnostics()
    {
        var vReport = ReportOf(
            MissLine("PB-1", aWhyMissed: "other"),
            AmendLine("PB-1", "instruction-ignored"),
            AmendLine("PB-1", "not-a-vocabulary-value", aTs: "2026-08-29T10:00:00Z"),
            AmendLine("PB-NOBODY", "other"),
            FixLine("PB-NOBODY", "sole"));

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vReport.Diagnostics.OverwriteAmendmentsIgnored.Should().Be(1, "why_missed already carried a value");
        vReport.Diagnostics.OrphanAmends.Select(aO => aO.Reason).Should().BeEquivalentTo(
            [MissAmendOrphanReasons.ValueOutsideVocabulary, MissAmendOrphanReasons.UnknownMiss]);
        vReport.Diagnostics.OrphanFixCount.Should().Be(1);
        vReport.WhyMissedDistribution.Should().ContainSingle()
            .Which.Key.Should().Be("other", "an amend completes a record and never alters a fact");
    }

    /// <summary>
    /// Acceptance 4 — <c>sole</c>, <c>shared:n</c> and <c>none</c> land in three cohorts, and the
    /// headline token figure carries the sole records' window undivided.
    /// </summary>
    [Fact]
    public void SoleSharedAndNoneNeverEnterTheSameHeadlineCostCohort()
    {
        var vLines = new List<string>();
        for (var vAt = 1; vAt <= 3; vAt++)
        {
            vLines.Add(MissLine($"PB-S{vAt}"));
            vLines.Add(FixLine($"PB-S{vAt}", "sole", aTokensOut: 300, aFixRunId: $"run-s{vAt}"));
            vLines.Add(MissLine($"PB-H{vAt}"));
            vLines.Add(FixLine($"PB-H{vAt}", "shared:3", aTokensOut: 300, aFixRunId: $"run-h{vAt}"));
            vLines.Add(MissLine($"PB-N{vAt}"));
            vLines.Add(FixLine($"PB-N{vAt}", "none", aTokensOut: 300, aFixRunId: $"run-n{vAt}"));
        }

        var vCost = ReportOf([.. vLines]).Cost;

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vCost.HeadlineRecords.Should().Be(3);
        vCost.ApportionedRecords.Should().Be(3);
        vCost.ExcludedRecords.Should().Be(3);
        vCost.HeadlineTokens.Sole.TryGetValue(out var vSole).Should().BeTrue();
        vSole.Should().Be(300d, "a sole window is never divided");
        vCost.HeadlineTokens.Apportioned.TryGetValue(out var vShared).Should().BeTrue();
        vShared.Should().Be(100d, "a shared:3 window divides by its own n and stays in its own column");
    }

    /// <summary>
    /// Acceptance 5 — a rate-card <c>*_usd_estimate</c> never becomes a measured dollar.
    /// </summary>
    /// <remarks>
    /// Proved at ingest, which is the only place the substitution could happen: the estimate is preserved
    /// in the record's overflow for rebuild fidelity and reaches no column any figure reads.
    /// </remarks>
    [Fact]
    public void MeasuredAndEstimatedDollarsNeverShareASeriesOrTotal()
    {
        var vLine = "{\"kind\":\"miss-fix\",\"ts\":\"2026-08-30T09:00:00Z\",\"miss_id\":\"PB-1\","
            + "\"fix_run_id\":\"run-1\",\"cost_attribution\":\"sole\",\"tokens_out\":100,"
            + "\"cost_usd_estimate\":4.25," + GoodWindow + "}";

        var vResult = PlaybookMissNormalizer.Normalize(Repo(), Sha, MissLine("PB-1") + "\n" + vLine);
        var vFix = vResult.Parsed.MissFixes.Should().ContainSingle().Subject;

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vFix.CostUsd.Should().BeNull("an estimate is not a measurement");
        vFix.Overflow.Should().Contain("cost_usd_estimate", "it is preserved, not discarded");
        PlaybookMissNormalizer
            .Read(FrameworkNames.Playbook, vResult.Parsed.Misses, vResult.Parsed.MissFixes, [])
            .Cost.MeasuredUsdTotal.Should().BeNull();
    }

    /// <summary>
    /// BRD-165 — the Playbook process gate is read from its own column and never from the assertion gate.
    /// </summary>
    [Fact]
    public void ProcessGateAndAssertionGateNeverShareADistribution()
    {
        var vReport = ReportOf(
            MissLine("PB-1", aFoundPhaseGate: "plan-review"),
            MissLine("PB-2", aFoundGate: "build"));

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vReport.ByFoundPhaseGate.Should().ContainSingle().Which.Key.Should().Be("plan-review");
        vReport.ByFoundPhaseGate.Should().NotContain(aRow => aRow.Key == "build");
    }

    /// <summary>
    /// BRD-165 — the Playbook report graph carries no member that could hold TechieFlow assertion-gate
    /// or <c>req_id</c> aggregates, so no chart bound to it can pool the two axes.
    /// </summary>
    [Fact]
    public void ReportExposesNoTechieFlowAxisAggregate()
    {
        var vAggregates = typeof(PlaybookMissReport)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(aProperty => aProperty.Name)
            .Where(aName => aName.StartsWith("By", StringComparison.Ordinal))
            .ToList();

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vAggregates.Should().Contain("ByItemId").And.Contain("ByFoundPhaseGate");
        vAggregates.Should().NotContain("ByReqId").And.NotContain("ByFoundGate");
    }

    /// <summary>
    /// BRD-165 — the two requirement-axis names stay in their own columns rather than merging.
    /// </summary>
    [Fact]
    public void ItemIdIsCarriedBesideReqIdRatherThanIntoIt()
    {
        var vLine = "{\"kind\":\"miss\",\"ts\":\"2026-08-30T09:00:00Z\",\"miss_id\":\"PB-1\","
            + "\"item_id\":\"ITEM-9\",\"req_id\":\"REQ-FN-1\"," + GoodWindow + "}";

        var vMiss = PlaybookMissNormalizer.Normalize(Repo(), Sha, vLine).Parsed.Misses.Single();

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vMiss.ItemId.Should().Be("ITEM-9");
        vMiss.ReqId.Should().Be("REQ-FN-1");
    }

    /// <summary>
    /// ADR-016 — a Playbook export cannot be normalized into a TechieFlow source.
    /// </summary>
    [Fact]
    public void NormalizeRefusesATechieFlowSource()
    {
        var vRepo = Repo() with { Framework = FrameworkNames.TechieFlow };

        var vAct = () => PlaybookMissNormalizer.Normalize(vRepo, Sha, MissLine("PB-1"));

        vAct.Should().Throw<ArgumentException>().WithParameterName("aRepo");
    }

    /// <summary>
    /// ADR-016 — reading the Playbook guards over another framework's rows is refused, not permitted.
    /// </summary>
    [Fact]
    public void ReadRefusesAnyFrameworkButThePlaybook()
    {
        var vAct = () => PlaybookMissNormalizer.Read(FrameworkNames.TechieFlow, [], [], []);

        vAct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("aFramework");
    }

    /// <summary>
    /// ADR-016 — no read path on this class is expressible without naming the framework.
    /// </summary>
    /// <remarks>
    /// The architecture's stated residual risk is "a query that forgets the framework filter". A default
    /// value or an overload without the parameter would be exactly that forgetting made easy, so the
    /// signature itself is asserted rather than the behaviour behind it.
    /// </remarks>
    [Fact]
    public void EveryReadPathTakesTheFrameworkAsARequiredParameter()
    {
        var vReads = typeof(PlaybookMissNormalizer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(aMethod => aMethod.Name.StartsWith("Read", StringComparison.Ordinal))
            .ToList();

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vReads.Should().NotBeEmpty();
        foreach (var vRead in vReads)
        {
            var vParameter = vRead.GetParameters().SingleOrDefault(aP => aP.Name == "aFramework");
            vParameter.Should().NotBeNull($"{vRead.Name} must name the framework it reads");
            vParameter!.HasDefaultValue.Should().BeFalse(
                $"{vRead.Name} must not let a caller leave the framework alone");
        }
    }

    /// <summary>
    /// A TechieFlow row (no source-line hash) and a Playbook row coexist without colliding on
    /// <c>NULL</c>, and only the Playbook rows reach the Playbook read.
    /// </summary>
    [Fact]
    public async Task TechieFlowAndPlaybookMissRowsCoexistWithoutColliding()
    {
        var vPlaybook = PlaybookMissNormalizer
            .Normalize(Repo(), Sha, MissLine("PB-1", aFoundPhaseGate: "verify"))
            .Parsed.Misses;

        var vStore = new FixtureTelemetryStore()
            .SeedMisses(UserId, "acme/flow", FrameworkNames.TechieFlow, [MissFixtures.Miss("MISS-1")])
            .SeedMisses(UserId, "acme/book", FrameworkNames.Playbook, vPlaybook);

        var vReport = await PlaybookMissNormalizer.ReadAsync(vStore, UserId, FrameworkNames.Playbook);

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vReport.Misses.Should().ContainSingle().Which.MissId.Should().Be("PB-1");
        vReport.Misses.Single().SourceLineHash.Should().NotBeNull();
        vReport.ByFoundPhaseGate.Should().ContainSingle().Which.Key.Should().Be("verify");
    }

    /// <summary>A malformed line is counted and skipped rather than failing the whole export.</summary>
    [Fact]
    public void MalformedAndUnknownKindLinesAreCountedAndSkipped()
    {
        var vResult = PlaybookMissNormalizer.Normalize(
            Repo(),
            Sha,
            string.Join('\n', MissLine("PB-1"), "{not json", "{\"kind\":\"phase-metric\",\"ts\":\"x\"}"));

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vResult.Parsed.Misses.Should().HaveCount(1);
        vResult.Parsed.InvalidLines.Should().Be(2);
        vResult.UnknownKinds.Should().Be(1);
    }

    /// <summary>Runs one export through ingest and then through the Playbook read.</summary>
    /// <param name="aLines">The export lines.</param>
    /// <returns>The report.</returns>
    private static PlaybookMissReport ReportOf(params string[] aLines)
    {
        var vParsed = PlaybookMissNormalizer.Normalize(Repo(), Sha, string.Join('\n', aLines)).Parsed;
        return PlaybookMissNormalizer.Read(
            FrameworkNames.Playbook, vParsed.Misses, vParsed.MissFixes, vParsed.MissAmends);
    }

    /// <summary>The Playbook source every fixture export is ingested against.</summary>
    /// <returns>The connected repository.</returns>
    private static UserRepo Repo() => new()
    {
        UserId = UserId,
        Repo = "acme/book",
        Owner = "acme",
        Name = "book",
        Branch = "main",
        Kind = FrameworkNames.Playbook,
        Framework = FrameworkNames.Playbook,
        ConnectedTs = "2026-09-01T00:00:00Z"
    };

    /// <summary>Builds one exported <c>miss</c> line.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aWhyMissed">Which practice failed, or <c>null</c> for not assessed.</param>
    /// <param name="aMissClass">What was missed.</param>
    /// <param name="aFoundGate">A TechieFlow assertion gate, for the separation tests.</param>
    /// <param name="aFoundPhaseGate">The Playbook process gate.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <returns>The line.</returns>
    internal static string MissLine(
        string aMissId,
        string? aWhyMissed = null,
        string? aMissClass = null,
        string? aFoundGate = null,
        string? aFoundPhaseGate = null,
        string aTs = "2026-08-30T09:00:00Z") =>
        "{\"kind\":\"miss\",\"ts\":\"" + aTs + "\",\"miss_id\":\"" + aMissId + "\""
        + Optional("why_missed", aWhyMissed)
        + Optional("miss_class", aMissClass)
        + Optional("found_gate", aFoundGate)
        + Optional("found_phase_gate", aFoundPhaseGate)
        + "," + GoodWindow + "}";

    /// <summary>Builds one exported <c>miss-fix</c> line.</summary>
    /// <param name="aMissId">The miss it repairs.</param>
    /// <param name="aCostAttribution"><c>sole</c> · <c>shared:n</c> · <c>none</c>.</param>
    /// <param name="aTokensOut">The window's output tokens.</param>
    /// <param name="aFixRunId">The repairing run.</param>
    /// <returns>The line.</returns>
    internal static string FixLine(
        string aMissId,
        string aCostAttribution,
        int aTokensOut = 100,
        string aFixRunId = "run-1") =>
        "{\"kind\":\"miss-fix\",\"ts\":\"2026-08-30T10:00:00Z\",\"miss_id\":\"" + aMissId + "\","
        + "\"fix_run_id\":\"" + aFixRunId + "\",\"cost_attribution\":\"" + aCostAttribution + "\","
        + "\"tokens_out\":" + aTokensOut + "," + GoodWindow + "}";

    /// <summary>Builds one exported <c>miss-amend</c> line.</summary>
    /// <param name="aMissId">The miss it completes.</param>
    /// <param name="aValue">The value it sets on <c>why_missed</c>.</param>
    /// <param name="aTs">The record timestamp; amendments fold oldest first.</param>
    /// <returns>The line.</returns>
    internal static string AmendLine(string aMissId, string aValue, string aTs = "2026-08-28T12:00:00Z") =>
        "{\"kind\":\"miss-amend\",\"ts\":\"" + aTs + "\",\"miss_id\":\"" + aMissId + "\","
        + "\"field\":\"why_missed\",\"value\":\"" + aValue + "\"}";

    /// <summary>Appends one optional wire key, or nothing when the value is absent.</summary>
    /// <param name="aName">The wire key.</param>
    /// <param name="aValue">The value, or <c>null</c>.</param>
    /// <returns>The JSON fragment.</returns>
    private static string Optional(string aName, string? aValue) =>
        aValue is null ? string.Empty : ",\"" + aName + "\":\"" + aValue + "\"";
}
