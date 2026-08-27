using FluentAssertions;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Fakes;

namespace TfLens.Core.Tests.Metrics.Extras;

/// <summary>
/// ADR-017 and BRD-51..BRD-55 — the rules the harness comparison exists to enforce.
/// </summary>
/// <remarks>
/// The expected numbers are not guesses: they were obtained independently from the raw JSONL with
/// <c>python3</c> before this file was written, and the same counts are recorded in <c>DECISIONS.md</c>
/// as the REQ-FN-064 hand spot-check. These metrics have no parity oracle, so a hand count is the only
/// check there is, and it belongs in a test as well as in the record.
/// </remarks>
public sealed class HarnessComparisonTests : IDisposable
{
    private readonly string objDataRoot = ExtrasFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>
    /// The three detected harnesses each get a column, always in the order claude-code, opencode,
    /// codex, and there is never a fourth column.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ColumnsAreTheThreeDetectedHarnessesInOrder()
    {
        var vComparison = await ExtrasFixture.Extras(objDataRoot)
            .CompareHarnessesAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vComparison.Columns.Select(aC => aC.Harness).Should().Equal("claude-code", "opencode", "codex");
    }

    /// <summary>
    /// A harness with no records at all still yields a column of zeros rather than disappearing, so a
    /// harness that has stopped emitting is visible instead of absent.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task HarnessWithNoRecordsStillYieldsAColumn()
    {
        var vStore = new ParsedFixtureStore(
            ExtrasFixture.UserId, ExtrasFixture.Repo, ExtrasFixture.Framework,
            ExtrasFixture.MetricsFolder(), ExtrasFixture.SourceSha);

        // Reading under a framework the fixture does not sit on yields no records at all — the
        // strongest form of "this harness emitted nothing".
        var vComparison = await new ExtraMetrics(vStore, ExtrasFixture.Options(objDataRoot))
            .CompareHarnessesAsync(ExtrasFixture.UserId, "playbook");

        vComparison.Columns.Should().HaveCount(3);
        vComparison.Columns.Should().OnlyContain(aC => aC.Runs == 0 && aC.GateRecords == 0 && aC.Sessions == 0);
        vComparison.Columns.Should().OnlyContain(aC => aC.TokensPerVerifiedReq.Display() == "insufficient data (n=0)");
    }

    /// <summary>
    /// Records whose harness is null are counted in the footnote and are not added to any column —
    /// six of them across runs, gates and sessions in the fixture.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NullHarnessRecordsLandInTheFootnoteAndNotInAColumn()
    {
        var vStore = ExtrasFixture.Store();
        var vComparison = await new ExtraMetrics(vStore, ExtrasFixture.Options(objDataRoot))
            .CompareHarnessesAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vComparison.NotDetectedRecords.Should().Be(6);

        var vColumnRuns = vComparison.Columns.Sum(aC => aC.Runs);
        var vColumnGates = vComparison.Columns.Sum(aC => aC.GateRecords);
        var vColumnSessions = vComparison.Columns.Sum(aC => aC.Sessions);

        // Nothing is dropped: every record is either in a column or in the footnote, never both.
        (vColumnRuns + vColumnGates + vColumnSessions + vComparison.NotDetectedRecords)
            .Should().Be(vStore.Runs.Count + vStore.Gates.Count + vStore.Sessions.Count);
    }

    /// <summary>
    /// The per-harness counts and token totals equal the values counted by hand from the raw JSONL.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ColumnCountsMatchTheHandCountFromRawJsonl()
    {
        var vComparison = await ExtrasFixture.Extras(objDataRoot)
            .CompareHarnessesAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        var vClaude = vComparison.Columns[0];
        vClaude.Runs.Should().Be(5);
        vClaude.GateRecords.Should().Be(6);
        vClaude.Sessions.Should().Be(2);
        vClaude.TokensIn.Should().Be(438000);
        vClaude.TokensOut.Should().Be(65500);
        vClaude.TokensCacheRead.Should().Be(1512000);
        vClaude.TokensCacheWrite.Should().Be(196000);
        vClaude.RunsByCmd.Should().Equal(
            new KeyValuePair<string, int>("build-phase", 3),
            new KeyValuePair<string, int>("triage-issues", 1),
            new KeyValuePair<string, int>("verify-phase", 1));
        vClaude.VerdictMix.Should().Equal(
            new KeyValuePair<string, int>("Verified", 4),
            new KeyValuePair<string, int>("FAIL", 2));
    }

    /// <summary>
    /// Tokens per Verified REQ is a number where at least three verdicts support it (claude-code) and
    /// refuses to be one where fewer do (opencode, codex).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TokensPerVerifiedRefusesToBeANumberBelowMinN()
    {
        var vComparison = await ExtrasFixture.Extras(objDataRoot)
            .CompareHarnessesAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vComparison.Columns[0].TokensPerVerifiedReq.Display().Should().Be("125875.0");
        vComparison.Columns[1].TokensPerVerifiedReq.Display().Should().Be("insufficient data (n=2)");
        vComparison.Columns[2].TokensPerVerifiedReq.Display().Should().Be("insufficient data (n=1)");
    }

    /// <summary>
    /// Measured dollars exist only for OpenCode, and the contract offers no member that could hold a
    /// total across harnesses — the absence is structural, not a convention.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MeasuredDollarsExistOnlyForOpenCodeAndNeverTotal()
    {
        var vComparison = await ExtrasFixture.Extras(objDataRoot)
            .CompareHarnessesAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vComparison.OpenCodeCostUsd.Should().Be(2.05m);

        var vMoneyMembers = vComparison.GetType().GetProperties()
            .Where(aP => aP.PropertyType == typeof(decimal?) || aP.PropertyType == typeof(decimal))
            .Select(aP => aP.Name);
        vMoneyMembers.Should().Equal(nameof(vComparison.OpenCodeCostUsd));

        typeof(TfLens.Core.Contracts.HarnessColumn).GetProperties()
            .Should().NotContain(aP => aP.PropertyType == typeof(decimal) || aP.PropertyType == typeof(decimal?));
    }
}
