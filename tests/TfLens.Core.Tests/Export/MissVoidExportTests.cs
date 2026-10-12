using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// The export half of the withdrawal of a miss (SCHEMA.md §5.5.11, REQ-FN-143).
/// </summary>
/// <remarks>
/// Seeds <see cref="MissExportFixture"/> — four misses, three of them in <c>acme/alpha</c> — plus a void
/// of <c>MISS-02</c> and a void naming a miss that does not exist, then reads <c>tflens.json</c> back.
/// </remarks>
public sealed class MissVoidExportTests : IDisposable
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

    /// <summary>
    /// The misses block carries <c>misses_voided_n</c>, <c>misses_voided</c> and
    /// <c>miss_voids_orphaned_n</c> with the reference's values.
    /// </summary>
    [Fact]
    public async Task ExportCarriesTheThreeKeys()
    {
        var vMisses = (await JsonAsync(VoidedStore())).GetProperty("misses");

        vMisses.GetProperty("misses_voided_n").GetInt32().Should().Be(1);
        vMisses.GetProperty("misses_voided").EnumerateArray().Select(aLine => aLine.GetString())
            .Should().Equal("MISS-02 — logged against the wrong requirement");
        vMisses.GetProperty("miss_voids_orphaned_n").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// The withdrawn miss leaves <c>misses_total</c>, its fix leaves <c>miss_fixes_total</c>, and its
    /// <c>wont-fix</c> verdict leaves <c>wont_fix</c>; the per-repository count drops with it.
    /// </summary>
    [Fact]
    public async Task VoidedMissLeavesEveryFigure()
    {
        var vJson = await JsonAsync(VoidedStore());
        var vMisses = vJson.GetProperty("misses");
        var vRepos = vJson.GetProperty("per_repo").EnumerateArray()
            .ToDictionary(aRepo => aRepo.GetProperty("repo").GetString()!, aRepo => aRepo);

        vMisses.GetProperty("misses_total").GetInt32().Should().Be(3);
        vMisses.GetProperty("miss_fixes_total").GetInt32().Should().Be(4);
        vMisses.GetProperty("wont_fix").GetInt32().Should().Be(0);
        vRepos[MissExportFixture.AppRepo].GetProperty("misses").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// With no voids the three keys are still published, as zero and an empty list.
    /// </summary>
    [Fact]
    public async Task NoVoidsPublishesZeros()
    {
        var vMisses = (await JsonAsync(MissExportFixture.Store())).GetProperty("misses");

        vMisses.GetProperty("misses_voided_n").GetInt32().Should().Be(0);
        vMisses.GetProperty("misses_voided").GetArrayLength().Should().Be(0);
        vMisses.GetProperty("miss_voids_orphaned_n").GetInt32().Should().Be(0);
    }

    /// <summary>The miss fixture with one real void and one orphan.</summary>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore VoidedStore() =>
        MissExportFixture.Store().SeedMissVoids(
            ExportFixture.UserId,
            MissExportFixture.AppRepo,
            ExportFixture.Framework,
            [
                Void("MISS-02", "logged against the wrong requirement"),
                Void("MISS-99", "names nothing")
            ]);

    /// <summary>Builds one void in the app repository.</summary>
    /// <param name="aMissId">The miss withdrawn.</param>
    /// <param name="aReason">Why.</param>
    /// <returns>The record.</returns>
    private static MissVoidRecord Void(string aMissId, string aReason) => new()
    {
        UserId = ExportFixture.UserId,
        Repo = MissExportFixture.AppRepo,
        SourceSha = "fixture",
        Ts = "2026-09-22T10:00:00Z",
        MissId = aMissId,
        Reason = aReason
    };

    /// <summary>Exports and reads <c>tflens.json</c> back.</summary>
    /// <param name="aStore">The store to export from.</param>
    /// <returns>The document root.</returns>
    private async Task<JsonElement> JsonAsync(FixtureTelemetryStore aStore)
    {
        var vResult = await ExportFixture.Exporter(objDataRoot, aStore)
            .ExportAsync(ExportFixture.UserId, ExportFixture.Framework, ExportFixture.Date);
        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));

        return vDocument.RootElement.Clone();
    }
}
