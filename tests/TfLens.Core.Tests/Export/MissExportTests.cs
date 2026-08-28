using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// REQ-FN-080 / BRD-128 / BRD-129 — the export's <c>misses</c> section, and the parity coverage over it.
/// </summary>
/// <remarks>
/// <para>
/// Every expected value in this class was produced by running the oracle itself,
/// <c>.tfcore/telemetry/tf-metrics.sh --rollup --json</c>, over a <c>misses.jsonl</c> holding exactly the
/// records <see cref="MissExportFixture"/> seeds. They are not hand-derived: this is the same discipline
/// <c>Fixtures/Engine/reference.json</c> follows, because a hand-written expectation proves only that two
/// people made the same mistake.
/// </para>
/// <para>
/// The fixture spans two project types deliberately. The <c>misses</c> block is the shape the reference
/// computes and the reference does not segment the miss stream; the tables beneath it are TfLens's own
/// per-<c>project_type</c> shape. A single-type fixture would let a segmented block pass as an
/// unsegmented one, so <see cref="TheMissBlockIsNotSegmentedByProjectType"/> pins a value that neither
/// segment can produce on its own.
/// </para>
/// </remarks>
public sealed class MissExportTests : IDisposable
{
    /// <summary>The figures BRD-129 requires the export to carry and the compare to diff.</summary>
    private static readonly string[] ParityKeys =
    [
        "misses_total", "miss_fixes_total", "orphan_fixes", "open_misses", "wont_fix",
        "resolved_misses", "why_missed_n", "why_missed", "escapes_missing_why", "why_missed_eligible",
        "why_missed_predates_field", "amendments_applied", "orphan_amends", "class_distribution",
        "found_by", "design_miss_share", "escape_share", "attributed_n", "attribution_excluded",
        "by_origin_phase", "by_origin_model", "by_origin_agent", "cost_sole_n", "cost_shared_n",
        "cost_unattributable_n", "tokens_per_miss_measured", "tokens_per_miss_apportioned",
        "cost_usd_per_miss_measured", "cost_usd_records"
    ];

    private readonly string objDataRoot = ExportFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>Every figure BRD-129 names is on the document, so the compare has something to diff.</summary>
    /// <remarks>
    /// <c>tools/parity-compare.py</c> makes the same assertion from the other side. Absence produces no
    /// diff on either document, so "absent on both" would otherwise pass as agreement.
    /// </remarks>
    [Fact]
    public async Task TheJsonCarriesEveryFigureParityCovers()
    {
        var vMisses = (await JsonAsync()).GetProperty("misses");
        var vMissing = ParityKeys.Where(aKey => !vMisses.TryGetProperty(aKey, out _)).ToList();

        vMissing.Should().BeEmpty(
            "BRD-129 lists these by name, and no miss figure ships marked unverified");
    }

    /// <summary>Every value in the block is the one the oracle produced for the same records.</summary>
    [Fact]
    public async Task EveryFigureAgreesWithTheOracle()
    {
        var vMisses = (await JsonAsync()).GetProperty("misses");

        vMisses.GetProperty("misses_total").GetInt32().Should().Be(4);
        vMisses.GetProperty("miss_fixes_total").GetInt32().Should().Be(4);
        vMisses.GetProperty("orphan_fixes").GetInt32().Should().Be(0);
        vMisses.GetProperty("open_misses").GetInt32().Should().Be(1, "`deferred` is outstanding work");
        vMisses.GetProperty("wont_fix").GetInt32().Should().Be(1, "a decision, never folded into open");
        vMisses.GetProperty("resolved_misses").GetInt32().Should().Be(2);
        vMisses.GetProperty("escapes_missing_why").GetInt32().Should().Be(1);
        vMisses.GetProperty("amendments_applied").GetInt32().Should().Be(0);
        vMisses.GetProperty("orphan_amends").GetInt32().Should().Be(0);
        vMisses.GetProperty("why_missed_n").GetInt32().Should().Be(1);
        vMisses.GetProperty("why_missed_eligible").GetInt32().Should().Be(4);
        vMisses.GetProperty("why_missed_predates_field").GetInt32().Should().Be(0);
        vMisses.GetProperty("design_miss_share").GetString().Should().Be("25%");
        vMisses.GetProperty("escape_share").GetString().Should().Be("50%");
        vMisses.GetProperty("attributed_n").GetInt32().Should().Be(3, "only `linked` records count");
        vMisses.GetProperty("attribution_excluded").GetInt32().Should().Be(1);

        Distribution(vMisses, "why_missed").Should().Equal(
            new Dictionary<string, int> { ["instruction-ignored"] = 1 });
        Distribution(vMisses, "by_origin_phase").Should().Equal(
            new Dictionary<string, int> { ["build-phase"] = 2, ["verify-phase"] = 1 });
        Distribution(vMisses, "by_origin_agent").Should().Equal(
            new Dictionary<string, int> { ["flow-master"] = 2, ["verifier"] = 1 });
    }

    /// <summary>
    /// A category nobody recorded is the reference's <c>?</c> bucket — present, and not an invented value.
    /// </summary>
    /// <remarks>
    /// The engine keeps a <c>null</c> out of every distribution so it can never inflate a share
    /// (BRD-119). The reference reports the same records under <c>?</c>. Both are true at once: the row
    /// exists so the reader can see how much was never assessed, and it is not counted into any share.
    /// </remarks>
    [Fact]
    public async Task UnrecordedCategoriesAreTheReferencesQuestionMarkBucket()
    {
        var vMisses = (await JsonAsync()).GetProperty("misses");

        Distribution(vMisses, "class_distribution").Should().Equal(
            new Dictionary<string, int>
            {
                ["unspecified-gap"] = 1, ["wrong-behaviour"] = 1, ["other"] = 1, ["?"] = 1
            });
        Distribution(vMisses, "found_by").Should().Equal(
            new Dictionary<string, int>
            {
                ["owner"] = 1, ["agent-review"] = 1, ["production"] = 1, ["?"] = 1
            });
        Distribution(vMisses, "by_origin_model").Should().Equal(
            new Dictionary<string, int>
            {
                ["claude-opus-5"] = 1, ["claude-sonnet-4"] = 1, ["?"] = 1
            });
    }

    /// <summary>
    /// The attribution split stays three distinct keys, and no key blends the two token columns.
    /// </summary>
    /// <remarks>
    /// BRD-128 and REQ-NFR-013 clause 1. A run that repaired three misses has one token window; dividing
    /// it three ways is arithmetic, not measurement, and the two must never be summed. The engine makes
    /// the blend unrepresentable (<c>MissCost</c> has no such property); this fixes the same property on
    /// the wire, where a later "tidy-up" would be tempting.
    /// </remarks>
    [Fact]
    public async Task TheAttributionSplitStaysThreeDistinctKeys()
    {
        var vMisses = (await JsonAsync()).GetProperty("misses");

        vMisses.GetProperty("cost_sole_n").GetInt32().Should().Be(3);
        vMisses.GetProperty("cost_shared_n").GetInt32().Should().Be(1);
        vMisses.GetProperty("cost_unattributable_n").GetInt32().Should().Be(0);

        vMisses.GetProperty("tokens_per_miss_measured").GetDouble().Should().Be(
            600d, "(300 + 600 + 900) / 3 sole records — the measured column, over sole records only");
        vMisses.GetProperty("tokens_per_miss_apportioned").ValueKind.Should().Be(
            JsonValueKind.Null, "one shared record is below the minimum, and a refusal is not a zero");

        var vKeys = vMisses.EnumerateObject().Select(aProperty => aProperty.Name).ToList();
        vKeys.Should().NotContain(
            aKey => aKey.Contains("tokens_per_miss_total", StringComparison.Ordinal),
            "measured and apportioned tokens are never summed into one figure");
    }

    /// <summary>
    /// Measured dollars are bounded by the cost attribution, exactly as the token columns are.
    /// </summary>
    /// <remarks>
    /// The fixture's only <c>cost_usd</c> sits on a <c>shared:2</c> record. The reference counts measured
    /// dollars over <c>sole</c> records only, so the honest answer is zero measuring records — reporting
    /// one would present an apportioned repair as a measured one.
    /// </remarks>
    [Fact]
    public async Task MeasuredDollarsAreBoundedByTheCostAttribution()
    {
        var vMisses = (await JsonAsync()).GetProperty("misses");

        vMisses.GetProperty("cost_usd_records").GetInt32().Should().Be(
            0, "the only measured record is `shared:2`, and the reference bounds this figure to `sole`");
        vMisses.GetProperty("cost_usd_per_miss_measured").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The block pools the project types, because the reference does — and the tables below do not.
    /// </summary>
    /// <remarks>
    /// 25% is the pooled design-miss share over all four misses. The <c>app</c> segment alone would say
    /// 33% and the <c>library</c> segment would refuse to say anything, so this value can only come from
    /// an unsegmented computation. That is what BRD-129 requires of this one block, and TfLens's own
    /// per-type shape is preserved beside it rather than replaced by it.
    /// </remarks>
    [Fact]
    public async Task TheMissBlockIsNotSegmentedByProjectType()
    {
        var vJson = await JsonAsync();
        var vMisses = vJson.GetProperty("misses");

        vMisses.GetProperty("design_miss_share").GetString().Should().Be("25%");
        vMisses.EnumerateObject().Select(aProperty => aProperty.Name)
            .Should().NotContain(["app", "library", "by_project_type"]);
    }

    /// <summary>Every rate-card money key ends <c>_usd_estimate</c>; measured ones do not.</summary>
    /// <remarks>
    /// BRD-128's naming clause, checked as a property of the whole document rather than of one key: a
    /// reader scanning for money must be able to tell an estimate from a measurement by the key alone.
    /// </remarks>
    [Fact]
    public async Task EveryRateCardMoneyKeyEndsUsdEstimate()
    {
        var vJson = await JsonAsync();
        var vRepricing = vJson.GetProperty("extras").GetProperty("misses_repricing");

        vRepricing.GetProperty("estimate").GetBoolean().Should().BeTrue();
        vRepricing.GetProperty("estimate_label").GetString().Should().NotBeNullOrWhiteSpace();

        foreach (var vRow in vRepricing.GetProperty("by_harness").EnumerateArray())
        {
            var vNames = vRow.EnumerateObject().Select(aProperty => aProperty.Name).ToList();
            vNames.Where(aName => aName.Contains("usd", StringComparison.Ordinal))
                .Should().OnlyContain(
                    aName => aName.EndsWith("_usd_estimate", StringComparison.Ordinal),
                    "every dollar figure under repricing is tokens × rate card, never spend");
        }

        vJson.GetProperty("misses").EnumerateObject().Select(aProperty => aProperty.Name)
            .Where(aName => aName.Contains("usd", StringComparison.Ordinal))
            .Should().OnlyContain(
                aName => !aName.EndsWith("_usd_estimate", StringComparison.Ordinal),
                "the miss block's dollars are OpenCode measurements and must not read as estimates");
    }

    /// <summary>
    /// A rate-card figure is refused when no token count was recorded, rather than priced at zero.
    /// </summary>
    /// <remarks>
    /// SCHEMA.md §2.5: a sum over records that all carried <c>null</c> is <c>0</c>, and "0 tokens spent"
    /// and "no counts recorded" are different facts. Pricing the second would manufacture a $0.00 rework
    /// cost out of missing data — the plausible wrong number in its purest form.
    /// </remarks>
    [Fact]
    public async Task AHarnessWithNoTokenCountsIsNotPricedAtZero()
    {
        var vRows = (await JsonAsync()).GetProperty("extras").GetProperty("misses_repricing")
            .GetProperty("by_harness").EnumerateArray().ToList();

        foreach (var vRow in vRows.Where(aRow => aRow.GetProperty("token_records").GetInt32() == 0))
        {
            vRow.GetProperty("rework_at_max_usd_estimate").ValueKind.Should().Be(
                JsonValueKind.Null, "no token count was recorded, so nothing can be priced");
        }
    }

    /// <summary>The Markdown half carries the section, the split and the per-type tables.</summary>
    [Fact]
    public async Task TheMarkdownCarriesTheMissSection()
    {
        var vResult = await ExportAsync(MissExportFixture.Store());
        var vMarkdown = await File.ReadAllTextAsync(vResult.MarkdownPath);

        vMarkdown.Should().Contain("## Misses and rework");
        vMarkdown.Should().Contain("| Misses | 4 |");
        vMarkdown.Should().Contain("| Escape share | 50% |");
        vMarkdown.Should().Contain("### Rework cost");
        vMarkdown.Should().Contain("never summed");
        vMarkdown.Should().Contain("### By project type (TfLens, live only)");
        vMarkdown.Should().Contain("| app |");
        vMarkdown.Should().Contain("| library |");
    }

    /// <summary>
    /// A framework with no miss stream reports zeros and an honest refusal, never an absent section.
    /// </summary>
    /// <remarks>
    /// The engine returns no segment at all when nothing is live, so there is no figure to render and the
    /// block is built from nothing. The reference prints <c>insufficient data (n=0)</c> for both shares in
    /// that state; matching its wording is what keeps the compare clean on an empty stream.
    /// </remarks>
    [Fact]
    public async Task AnEmptyMissStreamReportsZerosRatherThanAbsence()
    {
        var vMisses = (await JsonAsync(ExportFixture.Store())).GetProperty("misses");

        vMisses.GetProperty("misses_total").GetInt32().Should().Be(0);
        vMisses.GetProperty("design_miss_share").GetString().Should().Be("insufficient data (n=0)");
        vMisses.GetProperty("escape_share").GetString().Should().Be("insufficient data (n=0)");
        vMisses.GetProperty("tokens_per_miss_measured").ValueKind.Should().Be(JsonValueKind.Null);
        vMisses.GetProperty("class_distribution").EnumerateObject().Should().BeEmpty();
    }

    /// <summary>The per-repository miss count the reference emits is on the document.</summary>
    [Fact]
    public async Task ThePerRepoBlockCountsMisses()
    {
        var vRepos = (await JsonAsync()).GetProperty("per_repo").EnumerateArray()
            .ToDictionary(aRepo => aRepo.GetProperty("repo").GetString()!, aRepo => aRepo);

        vRepos[MissExportFixture.AppRepo].GetProperty("misses").GetInt32().Should().Be(3);
        vRepos[MissExportFixture.LibraryRepo].GetProperty("misses").GetInt32().Should().Be(1);
        vRepos["acme/beta"].GetProperty("misses").GetInt32().Should().Be(0);
        vRepos["acme/beta"].GetProperty("stale_types").EnumerateArray().Should().BeEmpty();
    }

    /// <summary>Reads one distribution object back as a dictionary.</summary>
    /// <param name="aMisses">The <c>misses</c> block.</param>
    /// <param name="aKey">The distribution's key.</param>
    /// <returns>The category counts.</returns>
    private static Dictionary<string, int> Distribution(JsonElement aMisses, string aKey) =>
        aMisses.GetProperty(aKey).EnumerateObject()
            .ToDictionary(aProperty => aProperty.Name, aProperty => aProperty.Value.GetInt32());

    /// <summary>Exports the miss fixture over a throwaway data root.</summary>
    /// <param name="aStore">The store to export from.</param>
    /// <returns>The snapshot result.</returns>
    private Task<SnapshotResult> ExportAsync(FixtureTelemetryStore aStore) =>
        ExportFixture.Exporter(objDataRoot, aStore)
            .ExportAsync(ExportFixture.UserId, ExportFixture.Framework, ExportFixture.Date);

    /// <summary>Exports and reads <c>tflens.json</c> back.</summary>
    /// <param name="aStore">The store to export from, or <c>null</c> for the miss fixture.</param>
    /// <returns>The document root.</returns>
    private async Task<JsonElement> JsonAsync(FixtureTelemetryStore? aStore = null)
    {
        var vResult = await ExportAsync(aStore ?? MissExportFixture.Store());
        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));

        return vDocument.RootElement.Clone();
    }
}
