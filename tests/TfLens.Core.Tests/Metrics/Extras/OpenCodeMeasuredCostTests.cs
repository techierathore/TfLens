using FluentAssertions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics.Extras;

/// <summary>
/// BRD-27 / BRD-53 / BRD-54 and SCHEMA.md §4 — where the only measured dollars in TfLens come from.
/// </summary>
/// <remarks>
/// These tests exist because the figure was read off the wrong record type. <c>runs.jsonl</c> carries no
/// <c>cost_usd</c> at all — the OpenCode plugin writes the measured price onto <c>sessions.jsonl</c> —
/// so summing runs made <c>extras.harness.opencode_cost_usd</c> a structural <c>null</c> and the page
/// reported "not measured" over a dataset that had in fact been measured. Every test here seeds the two
/// streams with <b>different</b> money so no assertion can pass by coincidence.
/// </remarks>
public sealed class OpenCodeMeasuredCostTests
{
    private const int UserId = 11;
    private const string Repo = "tflens-tests/opencode-cost";
    private const string SourceSha = "0f1e2d3c4b5a69788796a5b4c3d2e1f009182736";

    /// <summary>
    /// The measured total is the sum over OpenCode <b>session</b> records, and run records contribute
    /// nothing even when they carry a <c>cost_usd</c> the schema says they cannot.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MeasuredDollarsComeFromSessionRecordsAndNeverFromRuns()
    {
        // The two real measurements in the shipped dataset, and a run-borne decoy an order of magnitude
        // larger: if the sum ever reads runs again, this test says so loudly rather than subtly.
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aRuns: [Run("2026-08-20T06:47:00Z", "opencode", 9.99m)],
            aSessions:
            [
                Session("ses-a", "2026-08-20T06:47:30Z", "opencode", 252, 0.019918m),
                Session("ses-b", "2026-08-20T06:48:01Z", "opencode", 423, 0.017749m)
            ]);

        var vComparison = await Extras(vStore).CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        // 0.019918 + 0.017749 = 0.037667, banker's-rounded to two places.
        vComparison.OpenCodeCostUsd.Should().Be(0.04m);
    }

    /// <summary>
    /// The figure's stated basis counts the <b>session</b> records it was summed over, not runs.
    /// </summary>
    /// <remarks>
    /// The Harness card prints this count verbatim ("Σ cost_usd over N opencode sessions"). While the
    /// sum was read off runs the caption said "over 12 opencode runs" for a figure that came from two
    /// session records — a true number under a false basis, which is worse than a missing one because
    /// it invites the reader to check the wrong stream. The count therefore travels with the figure and
    /// is asserted beside it: a denominator nobody tests is a denominator that drifts.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheStatedBasisCountsTheSessionsTheSumWasComputedOver()
    {
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aRuns:
            [
                Run("2026-08-20T06:47:00Z", "opencode", 9.99m),
                Run("2026-08-20T06:52:00Z", "opencode", 9.99m),
                Run("2026-08-20T06:57:00Z", "opencode", 9.99m)
            ],
            aSessions:
            [
                Session("ses-a", "2026-08-20T06:47:30Z", "opencode", 252, 0.019918m),
                Session("ses-b", "2026-08-20T06:48:01Z", "opencode", 423, 0.017749m),

                // Measured by a different harness, and unmeasured OpenCode work: neither is part of the
                // basis, because neither contributed a dollar to the sum.
                Session("ses-c", "2026-08-20T06:49:00Z", "claude-code", 900, 5.00m),
                Session("ses-d", "2026-08-20T06:50:00Z", "opencode", 111, null)
            ]);

        var vComparison = await Extras(vStore).CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        vComparison.OpenCodeCostSessions.Should().Be(2);
        vComparison.OpenCodeCostSessions.Should().NotBe(vComparison.Columns
            .Single(aColumn => aColumn.Harness == "opencode").Runs);
    }

    /// <summary>
    /// Nothing measured means no basis to state — the count is zero exactly when the figure is null.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnUnmeasuredDatasetStatesNoBasisAtAll()
    {
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aRuns: [Run("2026-08-20T06:47:00Z", "opencode", 9.99m)],
            aSessions: [Session("ses-a", "2026-08-20T06:47:30Z", "opencode", 252, null)]);

        var vComparison = await Extras(vStore).CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        vComparison.OpenCodeCostUsd.Should().BeNull();
        vComparison.OpenCodeCostSessions.Should().Be(0);
    }

    /// <summary>
    /// A session that idled several times appends a cumulative snapshot each time (SCHEMA.md §4), and
    /// its dollars are counted once — the largest snapshot's, per the BRD-27 dedupe.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task DuplicateSessionSnapshotsCountTheirDollarsOnce()
    {
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aSessions:
            [
                Session("ses-dup", "2026-08-20T06:40:00Z", "opencode", 100, 0.10m),
                Session("ses-dup", "2026-08-20T06:47:30Z", "opencode", 423, 0.25m),
                Session("ses-dup", "2026-08-20T06:44:00Z", "opencode", 300, 0.18m)
            ]);

        var vComparison = await Extras(vStore).CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        // The complete snapshot is the one with the most output tokens; the naive sum would be 0.53.
        vComparison.OpenCodeCostUsd.Should().Be(0.25m);
    }

    /// <summary>
    /// OpenCode sessions that carry no measurement leave the figure <c>null</c>, never <c>0</c> — the
    /// page must read "no OpenCode records yet" rather than "$0.00" (BRD-53).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task GenuineAbsenceStaysNullAndNeverBecomesZero()
    {
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aRuns: [Run("2026-08-20T06:47:00Z", "opencode", null)],
            aSessions: [Session("ses-none", "2026-08-20T06:47:30Z", "opencode", 252, null)]);

        var vComparison = await Extras(vStore).CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        vComparison.OpenCodeCostUsd.Should().BeNull();
        vComparison.Columns.Single(aC => aC.Harness == "opencode").Sessions.Should().Be(1);
    }

    /// <summary>
    /// The dollars are attributed to <c>opencode</c> alone, by the same harness detection the columns
    /// use: a Claude Code or Codex session carrying a cost is not pooled into the figure (BRD-54).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task CostIsAttributedToOpenCodeAloneAndNeverPooledAcrossHarnesses()
    {
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aSessions:
            [
                Session("ses-claude", "2026-08-20T06:40:00Z", "claude-code", 900, 5.00m),
                Session("ses-codex", "2026-08-20T06:41:00Z", "codex", 800, 3.00m),
                Session("ses-none", "2026-08-20T06:42:00Z", null, 700, 1.00m),
                Session("ses-open", "2026-08-20T06:43:00Z", "opencode", 423, 0.25m)
            ]);

        var vComparison = await Extras(vStore).CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow);

        vComparison.OpenCodeCostUsd.Should().Be(0.25m);
    }

    /// <summary>
    /// Dollars never cross the provenance axis: an OpenCode measurement on TechieFlow is invisible to
    /// the Playbook view (ADR-016).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MeasuredDollarsNeverCrossTheFrameworkAxis()
    {
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            FrameworkNames.TechieFlow,
            aSessions: [Session("ses-open", "2026-08-20T06:43:00Z", "opencode", 423, 0.25m)]);

        var vPlaybook = await Extras(vStore).CompareHarnessesAsync(UserId, FrameworkNames.Playbook);

        vPlaybook.OpenCodeCostUsd.Should().BeNull();
    }

    /// <summary>Builds the extras service over a seeded store and a throwaway data root.</summary>
    /// <param name="aStore">The seeded store.</param>
    /// <returns>The service under test.</returns>
    private static ExtraMetrics Extras(FixtureTelemetryStore aStore) =>
        new(aStore, Options.Create(new TfLensOptions
        {
            DataRoot = Path.Combine(Path.GetTempPath(), "tflens-tests", Guid.NewGuid().ToString("N"))
        }));

    /// <summary>Builds one session record carrying only the fields these tests are about.</summary>
    /// <param name="aSessionId">The dedupe key.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <param name="aHarness">The detected harness, or <c>null</c> for not detected.</param>
    /// <param name="aOutputTokens">Output tokens — the dedupe tie-breaker.</param>
    /// <param name="aCostUsd">The measured spend, or <c>null</c> when nothing measured it.</param>
    /// <returns>The record.</returns>
    private static SessionRecord Session(
        string aSessionId, string aTs, string? aHarness, int aOutputTokens, decimal? aCostUsd) =>
        new()
        {
            UserId = UserId,
            Repo = Repo,
            SourceSha = SourceSha,
            Ts = aTs,
            SessionId = aSessionId,
            Harness = aHarness,
            OutputTokens = aOutputTokens,
            CostUsd = aCostUsd
        };

    /// <summary>Builds one run record, used only as a decoy for the money that must not be read.</summary>
    /// <param name="aTs">The record timestamp.</param>
    /// <param name="aHarness">The detected harness.</param>
    /// <param name="aCostUsd">A cost the schema says a run never carries.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(string aTs, string? aHarness, decimal? aCostUsd) =>
        new()
        {
            UserId = UserId,
            Repo = Repo,
            SourceSha = SourceSha,
            Ts = aTs,
            Harness = aHarness,
            Cmd = "build-phase",
            CostUsd = aCostUsd
        };
}
