using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// REQ-FN-087 / BRD-136 / ADR-021 — <c>source_kind</c> is in the export, and divides nothing in it.
/// </summary>
/// <remarks>
/// <para>
/// The structural half of this requirement — no engine method takes origin, no result type carries a key
/// that could be split by it — is proven by reflection in
/// <c>TfLens.Guardrails.Tests/SourceKindIsNeverASegmentTests.cs</c>. This is the behavioural half, and it
/// is the one that would catch a regression the reflection cannot see: a caller that groups by origin
/// without ever naming it in a signature.
/// </para>
/// <para>
/// The proof is a whole-document comparison rather than a spot check on a few figures. Two exports are
/// produced from the same records — one where every source is fetched, one where half arrived as
/// uploaded bundles — and the two documents must be identical <b>everywhere except</b> the
/// <c>source_kind</c> key itself. Anything else that moved would be a figure that had learned how its
/// data arrived.
/// </para>
/// </remarks>
public sealed class SourceKindExportTests : IDisposable
{
    private readonly string objDataRoot = ExportFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>The per-repo block names how each source's data arrived, in the stored vocabulary.</summary>
    /// <remarks>
    /// BRD-132 fixes the stored values as <c>api</c> | <c>import</c> and the badge wording as
    /// <i>Synced</i> | <i>Imported</i>. The export carries the stored value, so rewording a badge is never
    /// a schema change for a consumer reading <c>tflens.json</c>.
    /// </remarks>
    [Fact]
    public async Task TheExportCarriesTheStoredSourceKindPerRepo()
    {
        var vRepos = await ReposAsync(MixedStore());

        vRepos["acme/alpha"].Should().Be(SourceKinds.Api);
        vRepos["acme/gamma"].Should().Be(SourceKinds.Import);
        vRepos.Values.Should().OnlyContain(
            aKind => aKind == SourceKinds.Api || aKind == SourceKinds.Import,
            "BRD-132 fixes the wire vocabulary; the badge words never reach the document");
    }

    /// <summary>
    /// A dataset mixing fetched and imported sources produces exactly the figures a uniform one produces.
    /// </summary>
    /// <remarks>
    /// The acceptance clause, stated as a document diff. If any figure anywhere had grown an origin
    /// dimension — a halved denominator, a split distribution, an extra segment — this comparison would
    /// name the path it happened on.
    /// </remarks>
    [Fact]
    public async Task AMixedDatasetProducesTheSameFiguresAsAUniformOne()
    {
        var vFetched = await JsonAsync(MissExportFixture.Store());
        var vMixed = await JsonAsync(MixedStore());

        var vDifferences = new List<string>();
        Compare(vFetched, vMixed, string.Empty, vDifferences);

        vDifferences.Should().BeEmpty(
            "BRD-136 — origin is a property of delivery. A record's backfilled, project_type, harness "
            + "and origin_confidence mean the same thing whichever way the line arrived, so nothing but "
            + "the source_kind key itself may move when half the sources are imported");
    }

    /// <summary>Both source kinds still reach the reader, whichever the dataset holds.</summary>
    /// <remarks>
    /// "Displayed everywhere and pooled nowhere" is two claims. A guardrail that only forbade the pooling
    /// would be satisfied by removing the key, which is the wrong answer: the fact is shown precisely
    /// because it divides nothing.
    /// </remarks>
    [Fact]
    public async Task TheKeyIsPresentWhicheverWayTheDataArrived()
    {
        (await ReposAsync(MissExportFixture.Store())).Values
            .Should().OnlyContain(aKind => aKind == SourceKinds.Api).And.NotBeEmpty();

        (await ReposAsync(MixedStore())).Values
            .Should().Contain(SourceKinds.Import);
    }

    /// <summary>The Markdown half shows the badge wording rather than the stored value.</summary>
    [Fact]
    public async Task TheMarkdownShowsTheBadgeWording()
    {
        var vResult = await ExportAsync(MixedStore());
        var vMarkdown = await File.ReadAllTextAsync(vResult.MarkdownPath);

        vMarkdown.Should().Contain("| Source |");
        vMarkdown.Should().Contain(SourceKinds.ImportLabel);
        vMarkdown.Should().Contain("divides no figure anywhere in this document");
    }

    /// <summary>The miss fixture with one of its two repositories delivered as an uploaded bundle.</summary>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore MixedStore() =>
        MissExportFixture.Store()
            .WithSourceKind(ExportFixture.UserId, MissExportFixture.LibraryRepo, SourceKinds.Import)
            .WithSourceKind(ExportFixture.UserId, "acme/beta", SourceKinds.Import);

    /// <summary>
    /// Walks two exported documents and records every path whose value differs.
    /// </summary>
    /// <remarks>
    /// Two paths are excluded and only two: <c>source_kind</c>, which is the key under test, and
    /// <c>generated_ts</c>, which is a clock reading rather than a figure.
    /// </remarks>
    /// <param name="aLeft">The all-fetched document.</param>
    /// <param name="aRight">The mixed document.</param>
    /// <param name="aPath">The dotted path reached so far.</param>
    /// <param name="aDifferences">Collects the differing paths.</param>
    private static void Compare(JsonNode? aLeft, JsonNode? aRight, string aPath, List<string> aDifferences)
    {
        if (aPath.EndsWith("source_kind", StringComparison.Ordinal)
            || aPath.EndsWith("generated_ts", StringComparison.Ordinal))
        {
            return;
        }

        if (aLeft is JsonObject vLeftObject && aRight is JsonObject vRightObject)
        {
            foreach (var vName in vLeftObject.Select(aPair => aPair.Key)
                         .Union(vRightObject.Select(aPair => aPair.Key), StringComparer.Ordinal))
            {
                Compare(vLeftObject[vName], vRightObject[vName], aPath + "." + vName, aDifferences);
            }

            return;
        }

        if (aLeft is JsonArray vLeftArray && aRight is JsonArray vRightArray)
        {
            if (vLeftArray.Count != vRightArray.Count)
            {
                aDifferences.Add($"{aPath}: {vLeftArray.Count} vs {vRightArray.Count} entries");
                return;
            }

            for (var vIndex = 0; vIndex < vLeftArray.Count; vIndex++)
            {
                Compare(vLeftArray[vIndex], vRightArray[vIndex], $"{aPath}[{vIndex}]", aDifferences);
            }

            return;
        }

        var vLeftText = aLeft?.ToJsonString() ?? "null";
        var vRightText = aRight?.ToJsonString() ?? "null";

        if (!string.Equals(vLeftText, vRightText, StringComparison.Ordinal))
        {
            aDifferences.Add($"{aPath}: {vLeftText} vs {vRightText}");
        }
    }

    /// <summary>Exports over a throwaway data root.</summary>
    /// <param name="aStore">The store to export from.</param>
    /// <returns>The snapshot result.</returns>
    private Task<SnapshotResult> ExportAsync(FixtureTelemetryStore aStore) =>
        ExportFixture.Exporter(objDataRoot, aStore)
            .ExportAsync(ExportFixture.UserId, ExportFixture.Framework, ExportFixture.Date);

    /// <summary>Exports and reads <c>tflens.json</c> back as a mutable node tree.</summary>
    /// <param name="aStore">The store to export from.</param>
    /// <returns>The document root.</returns>
    private async Task<JsonNode> JsonAsync(FixtureTelemetryStore aStore)
    {
        var vResult = await ExportAsync(aStore);

        return JsonNode.Parse(await File.ReadAllTextAsync(vResult.JsonPath))!;
    }

    /// <summary>Exports and reads the per-repository source kinds back.</summary>
    /// <param name="aStore">The store to export from.</param>
    /// <returns>Repository to stored source kind.</returns>
    private async Task<Dictionary<string, string>> ReposAsync(FixtureTelemetryStore aStore)
    {
        var vResult = await ExportAsync(aStore);
        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));

        return vDocument.RootElement.GetProperty("per_repo").EnumerateArray()
            .ToDictionary(
                aRepo => aRepo.GetProperty("repo").GetString()!,
                aRepo => aRepo.GetProperty("source_kind").GetString()!);
    }
}
