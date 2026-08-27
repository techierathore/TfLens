using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// REQ-FN-063 — the export reports <c>pooled.session_duplicates_collapsed</c>, the figure the reference
/// emits and TfLens used to be missing.
/// </summary>
/// <remarks>
/// The number cannot be derived from the records the store serves. TfLens collapses duplicate sessions
/// on the way <i>in</i>, on the <c>UcSessionUserRepoId</c> index, so by the time the engine reads them
/// the duplicates are gone and a read-time count would always be zero. Ingest therefore records what it
/// discarded in <c>"SyncState"</c>, and these tests fix the whole path from that row to the exported
/// key: it is summed over the framework's repositories only, it is emitted beside its commit sibling in
/// the position <c>--rollup --json</c> puts it, and it reaches the Markdown half as well.
/// </remarks>
public sealed class SessionCollapseExportTests : IDisposable
{
    private const int AlphaCollapses = 2;
    private const int BetaCollapses = 3;

    private readonly string objDataRoot = ExportFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>The exported JSON carries the key, totalled across the framework's repositories.</summary>
    [Fact]
    public async Task ExportedJsonCarriesTheSessionCollapseCount()
    {
        var vPooled = (await JsonAsync(FrameworkNames.TechieFlow)).GetProperty("pooled");

        vPooled.TryGetProperty("session_duplicates_collapsed", out var vCollapsed).Should().BeTrue(
            "the reference emits this key, and a key the reference emits and TfLens does not is a parity failure");
        vCollapsed.GetInt32().Should().Be(
            AlphaCollapses + BetaCollapses,
            "the figure pools over every repository on the framework, as every pooled figure does");
    }

    /// <summary>
    /// The key sits immediately after <c>commit_duplicates_collapsed</c>, as it does in the reference.
    /// </summary>
    /// <remarks>
    /// The export's whole layout mirrors <c>tf-metrics.sh --rollup --json</c> key for key and in order,
    /// so that the two documents can be read side by side. The two collapse figures are siblings and the
    /// reference prints them adjacently; putting the new one anywhere else would still pass the compare
    /// but would quietly break the property the layout exists for.
    /// </remarks>
    [Fact]
    public async Task TheSessionCollapseKeyFollowsItsCommitSibling()
    {
        var vKeys = (await JsonAsync(FrameworkNames.TechieFlow))
            .GetProperty("pooled")
            .EnumerateObject()
            .Select(aProperty => aProperty.Name)
            .ToList();

        var vCommits = vKeys.IndexOf("commit_duplicates_collapsed");

        vCommits.Should().BeGreaterThan(-1);
        vKeys[vCommits + 1].Should().Be("session_duplicates_collapsed");
    }

    /// <summary>A framework with no repositories reports zero, not another framework's total.</summary>
    /// <remarks>
    /// <c>"SyncState"</c> has no framework column, so the scoping is done against <c>"UserRepo"</c> in
    /// the engine. If that scoping were dropped, this export — whose framework owns no repository at all
    /// — would inherit the TechieFlow total and pool a figure across the one axis ADR-016 forbids.
    /// </remarks>
    [Fact]
    public async Task AnotherFrameworkDoesNotInheritTheCount()
    {
        var vPooled = (await JsonAsync(FrameworkNames.Playbook)).GetProperty("pooled");

        vPooled.GetProperty("session_duplicates_collapsed").GetInt32().Should().Be(0);
    }

    /// <summary>The Markdown half reports the same figure, beside the commit one.</summary>
    [Fact]
    public async Task TheMarkdownReportsTheSessionCollapseCount()
    {
        var vResult = await ExportAsync(FrameworkNames.TechieFlow);
        var vMarkdown = await File.ReadAllTextAsync(vResult.MarkdownPath);

        vMarkdown.Should().Contain($"| Session duplicates collapsed | {AlphaCollapses + BetaCollapses} |");
    }

    /// <summary>
    /// Builds the engine fixture store with a recorded collapse count on two of its repositories.
    /// </summary>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore SeededStore() =>
        ExportFixture.Store()
            .WithSessionCollapses(ExportFixture.UserId, "acme/alpha", AlphaCollapses)
            .WithSessionCollapses(ExportFixture.UserId, "acme/beta", BetaCollapses);

    /// <summary>Exports one framework over the seeded fixture store.</summary>
    /// <param name="aFramework">The provenance axis to export.</param>
    /// <returns>The snapshot result.</returns>
    private Task<SnapshotResult> ExportAsync(string aFramework) =>
        ExportFixture.Exporter(objDataRoot, SeededStore())
            .ExportAsync(ExportFixture.UserId, aFramework, ExportFixture.Date);

    /// <summary>Exports one framework and reads its <c>tflens.json</c> back.</summary>
    /// <param name="aFramework">The provenance axis to export.</param>
    /// <returns>The document root.</returns>
    private async Task<JsonElement> JsonAsync(string aFramework)
    {
        var vResult = await ExportAsync(aFramework);
        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));

        return vDocument.RootElement.Clone();
    }
}
