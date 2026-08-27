using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// REQ-FN-070 / BRD-110 — the snapshot export writes one snapshot <b>per framework</b>, and the
/// <c>playbook</c> one carries the Playbook-native report set rather than an empty TechieFlow shape.
/// </summary>
/// <remarks>
/// The exporter is the production type composed over the production
/// <see cref="TfLens.Core.Playbook.PlaybookReportBuilder"/>; only storage is a fixture. The two axes are
/// checked to stay disjoint <i>inside one written document</i>, which is where REQ-FN-066 is easiest to
/// break by accident: a shared key would pool a process gate with an assertion gate without any query
/// ever joining the two tables.
/// </remarks>
public sealed class PlaybookSnapshotTests : IDisposable
{
    private const int UserId = 11;
    private const string Repo = "techierathore/AI-First-Playbook";

    private static readonly DateOnly Date = new(2026, 8, 27);

    private readonly string objDataRoot = ExportFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>A Playbook snapshot lands under its own framework folder, both halves written.</summary>
    [Fact]
    public async Task PlaybookSnapshotIsWrittenUnderItsOwnFrameworkFolder()
    {
        var vResult = await ExportAsync(FrameworkNames.Playbook);

        vResult.Framework.Should().Be(FrameworkNames.Playbook);
        Path.GetFileName(Path.GetDirectoryName(vResult.JsonPath)).Should().Be(FrameworkNames.Playbook);
        File.Exists(vResult.MarkdownPath).Should().BeTrue();
        File.Exists(vResult.JsonPath).Should().BeTrue();
    }

    /// <summary>Exporting both frameworks for one date leaves two snapshots, not one.</summary>
    [Fact]
    public async Task OneSnapshotIsWrittenPerFramework()
    {
        var vExporter = ExportFixture.Exporter(objDataRoot, SeededStore());

        await vExporter.ExportAsync(UserId, FrameworkNames.TechieFlow, Date);
        await vExporter.ExportAsync(UserId, FrameworkNames.Playbook, Date);

        var vListed = await vExporter.ListAsync(UserId);

        vListed.Select(aR => aR.Framework)
            .Should().BeEquivalentTo([FrameworkNames.TechieFlow, FrameworkNames.Playbook]);
    }

    /// <summary>The Playbook half of the JSON carries real figures, not an empty state.</summary>
    [Fact]
    public async Task PlaybookJsonCarriesTheReportSet()
    {
        var vPlaybook = (await JsonAsync(FrameworkNames.Playbook)).GetProperty("playbook");

        vPlaybook.GetProperty("events_total").GetInt32().Should().Be(6);
        vPlaybook.GetProperty("framework").GetString().Should().Be(FrameworkNames.Playbook);
        vPlaybook.GetProperty("phase_gates").GetArrayLength().Should().Be(2);
        vPlaybook.GetProperty("tokens_by_model").GetArrayLength().Should().Be(1);
        vPlaybook.GetProperty("agent_split").GetProperty("main_sessions").GetInt32().Should().Be(1);
        vPlaybook.GetProperty("agent_split").GetProperty("subagent_sessions").GetInt32().Should().Be(1);
    }

    /// <summary>A process gate whose events carried no cost writes a JSON null, never a zero.</summary>
    [Fact]
    public async Task AbsentCostIsWrittenAsNull()
    {
        var vGates = (await JsonAsync(FrameworkNames.Playbook)).GetProperty("playbook").GetProperty("phase_gates");

        var vFree = vGates.EnumerateArray().Single(aG => aG.GetProperty("phase_gate").GetString() == "gap-report");
        vFree.GetProperty("cost_usd").ValueKind.Should().Be(JsonValueKind.Null);

        var vPaid = vGates.EnumerateArray().Single(aG => aG.GetProperty("phase_gate").GetString() == "verify");
        vPaid.GetProperty("cost_usd").GetDecimal().Should().Be(0.30m);
    }

    /// <summary>The markdown renders the em dash for an absent cost rather than <c>$0.00</c>.</summary>
    [Fact]
    public async Task AbsentCostRendersAnEmDashInTheMarkdown()
    {
        var vResult = await ExportAsync(FrameworkNames.Playbook);
        var vMarkdown = await File.ReadAllTextAsync(vResult.MarkdownPath);

        vMarkdown.Should().Contain("## Playbook (phase_gate axis)");
        vMarkdown.Should().Contain("| `gap-report` | 2 | 1 | 40 | — |");
        vMarkdown.Should().NotContain("$0.00");
    }

    /// <summary>A TechieFlow snapshot has no Playbook block at all — the axes never share a document.</summary>
    [Fact]
    public async Task TechieFlowSnapshotCarriesNoPlaybookBlock()
    {
        var vJson = await JsonAsync(FrameworkNames.TechieFlow);

        vJson.TryGetProperty("playbook", out _).Should().BeFalse();

        var vResult = await ExportAsync(FrameworkNames.TechieFlow);
        var vMarkdown = await File.ReadAllTextAsync(vResult.MarkdownPath);
        vMarkdown.Should().NotContain("phase_gate");
    }

    /// <summary>
    /// No process gate leaks into the TechieFlow keys of the same document, and no assertion gate into
    /// the Playbook block (REQ-FN-066).
    /// </summary>
    [Fact]
    public async Task ProcessGatesAndAssertionGatesStayInSeparateSubtrees()
    {
        var vJson = await JsonAsync(FrameworkNames.Playbook);

        var vPlaybookText = vJson.GetProperty("playbook").GetRawText();
        vPlaybookText.Should().Contain("verify").And.Contain("gap-report");

        foreach (var vKey in new[] { "live", "backfilled", "pooled" })
        {
            var vText = vJson.GetProperty(vKey).GetRawText();
            vText.Should().NotContain("gap-report");
            vText.Should().NotContain("phase_gate");
        }
    }

    /// <summary>The schema caveat travels into the export, so a snapshot cannot drop it.</summary>
    [Fact]
    public async Task SchemaCaveatTravelsIntoTheSnapshot()
    {
        var vPlaybook = (await JsonAsync(FrameworkNames.Playbook)).GetProperty("playbook");

        vPlaybook.GetProperty("schema_status").GetString().Should().Be(
            PlaybookSchemaStatus.EmitterSourceDerived.ToString());
        vPlaybook.GetProperty("provisional_notes").GetArrayLength().Should().BeGreaterThan(0);
    }

    /// <summary>Reads back the JSON half of one framework's snapshot.</summary>
    /// <param name="aFramework">The framework to export.</param>
    /// <returns>The parsed document root.</returns>
    private async Task<JsonElement> JsonAsync(string aFramework)
    {
        var vResult = await ExportAsync(aFramework);
        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));
        return vDocument.RootElement.Clone();
    }

    /// <summary>Runs one export over the seeded store.</summary>
    /// <param name="aFramework">The framework to export.</param>
    /// <returns>Where the pair was written.</returns>
    private Task<SnapshotResult> ExportAsync(string aFramework) =>
        ExportFixture.Exporter(objDataRoot, SeededStore()).ExportAsync(UserId, aFramework, Date);

    /// <summary>
    /// A store carrying one Playbook repository's events and nothing on the TechieFlow axis.
    /// </summary>
    /// <remarks>
    /// Two process gates, one of which carries no cost at all; one main session and one sub-agent
    /// session linked by <c>parentID</c>.
    /// </remarks>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore SeededStore() =>
        new FixtureTelemetryStore().SeedPbEvents(UserId, Repo,
        [
            Event(PlaybookEventKinds.PhaseStart, "verify", "ses-main", null, null, null, null),
            Event(PlaybookEventKinds.Turn, "verify", "ses-main", null, "msg-1", 100, 0.20m),
            Event(PlaybookEventKinds.Turn, "verify", "ses-sub", "ses-main", "msg-2", 60, 0.10m),
            Event(PlaybookEventKinds.PhaseEnd, "verify", "ses-main", null, null, null, null),
            Event(PlaybookEventKinds.PhaseStart, "gap-report", "ses-main", null, null, null, null),
            Event(PlaybookEventKinds.Turn, "gap-report", "ses-main", null, "msg-3", 40, null)
        ]);

    /// <summary>Builds one Playbook event record.</summary>
    /// <param name="aKind">The record kind.</param>
    /// <param name="aPhaseGate">The process gate the record belongs to.</param>
    /// <param name="aSessionId">The session.</param>
    /// <param name="aParentId">The parent session, or <c>null</c> for a main session.</param>
    /// <param name="aMessageId">The message id, or <c>null</c> for a marker record.</param>
    /// <param name="aOutput">Output tokens, or <c>null</c>.</param>
    /// <param name="aCost">Measured spend, or <c>null</c> when the event carried none.</param>
    /// <returns>The record.</returns>
    private static PbEventRecord Event(
        string aKind,
        string aPhaseGate,
        string aSessionId,
        string? aParentId,
        string? aMessageId,
        int? aOutput,
        decimal? aCost) =>
        new()
        {
            UserId = UserId,
            Repo = Repo,
            SourceSha = "0d7e6a3b",
            Ts = "2026-08-26T22:04:29Z",
            Kind = aKind,
            PhaseGate = aPhaseGate,
            SessionId = aSessionId,
            ParentId = aParentId,
            MessageId = aMessageId,
            Model = aMessageId is null ? null : "anthropic/claude-opus-4",
            TokensOutput = aOutput,
            CostUsd = aCost
        };
}
