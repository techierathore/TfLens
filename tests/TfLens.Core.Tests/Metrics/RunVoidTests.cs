using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Corrections that take a wrong run out of the figures without deleting it
/// (SCHEMA.md §2.7, REQ-FN-135).
/// </summary>
/// <remarks>
/// The defect these pin is the one that made TechieFlow read <b>81 live runs against the reference's
/// 75</b>: a <c>run-void</c> record was counted as a run, and the run it corrected stayed inside every
/// duration and token total. Both halves of that are wrong in the direction that inflates the figures,
/// and neither looks wrong on the page.
/// </remarks>
public sealed class RunVoidTests
{
    /// <summary>A void removes both itself and the run it names.</summary>
    [Fact]
    public void AVoidRemovesItselfAndTheRunItNames()
    {
        var vRead = RunVoid.Apply(
        [
            Run("build-phase", "10:00:00"),
            Run("log-miss", "11:00:00"),
            Void("build-phase", "10:00:00", "the start time was typed, not measured")
        ]);

        vRead.Kept.Should().ContainSingle().Which.Cmd.Should().Be("log-miss");
        vRead.VoidedN.Should().Be(1);
        vRead.OrphanedN.Should().Be(0);
        vRead.Reasons.Should().ContainSingle()
            .Which.Should().Be("alpha build-phase 2026-09-01T10:00:00Z — the start time was typed, not measured");
    }

    /// <summary>
    /// A void naming no run on this stream is an orphan: counted and reported, never silently dropped.
    /// </summary>
    [Fact]
    public void AVoidNamingNoRunIsCountedAsAnOrphan()
    {
        var vRead = RunVoid.Apply(
        [
            Run("build-phase", "10:00:00"),
            Void("verify-phase", "23:00:00", "names a run this stream does not hold")
        ]);

        vRead.Kept.Should().ContainSingle("the void removed nothing, so nothing left the figures");
        vRead.OrphanedN.Should().Be(1);
        vRead.VoidedN.Should().Be(0);
    }

    /// <summary>
    /// Two voids written in the same second naming different runs remove <b>both</b> runs.
    /// </summary>
    /// <remarks>
    /// This is the case the old identity key lost. Keyed on <c>(ts, app, cmd)</c> the two voids collapsed
    /// into one, so one corrected run quietly returned to every figure — and nothing on the page could
    /// have shown it.
    /// </remarks>
    [Fact]
    public void TwoVoidsInTheSameSecondNamingDifferentRunsBothApply()
    {
        var vRead = RunVoid.Apply(
        [
            Run("framework-reset", "10:18:17"),
            Run("framework-reset", "11:38:31"),
            Run("framework-reset", "14:00:00"),
            Void("framework-reset", "10:18:17", "the start time was typed") with { Ts = "2026-09-01T14:46:27Z" },
            Void("framework-reset", "11:38:31", "the start time was typed") with { Ts = "2026-09-01T14:46:27Z" }
        ]);

        vRead.VoidedN.Should().Be(2);
        vRead.Kept.Should().ContainSingle().Which.Started.Should().Be("2026-09-01T14:00:00Z");
    }

    /// <summary>A void is matched within one repository and never across the estate.</summary>
    /// <remarks>
    /// Two repositories can each hold a <c>build-phase</c> that started at the same minute. A void
    /// written in one must not silence the other's run — that would be a correction reaching a project
    /// nobody made it about.
    /// </remarks>
    [Fact]
    public void AVoidNeverReachesAnotherRepositorysRun()
    {
        var vRead = RunVoid.Apply(
        [
            Run("build-phase", "10:00:00"),
            Run("build-phase", "10:00:00") with { Repo = "acme/beta" },
            Void("build-phase", "10:00:00", "alpha's run was wrong")
        ]);

        vRead.VoidedN.Should().Be(1);
        vRead.Kept.Should().ContainSingle().Which.Repo.Should().Be("acme/beta");
        vRead.OrphanedN.Should().Be(0);
    }

    /// <summary>
    /// The stream is append-only: applying a void never edits or deletes the records it read.
    /// </summary>
    [Fact]
    public void ApplyingAVoidNeverEditsTheRecords()
    {
        var vRun = Run("build-phase", "10:00:00");
        var vVoid = Void("build-phase", "10:00:00", "wrong");

        RunVoid.Apply([vRun, vVoid]);

        vRun.Cmd.Should().Be("build-phase", "the corrected record stays exactly as it arrived");
        vVoid.Kind.Should().Be(RunVoid.Kind, "and so does the correction; only the READ excludes them");
    }

    /// <summary>One ordinary run record.</summary>
    /// <param name="aCmd">The command.</param>
    /// <param name="aStarted">Its start, as a clock time on the fixture's day.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(string aCmd, string aStarted) => new()
    {
        UserId = 7,
        Repo = "acme/alpha",
        SourceSha = "fixture",
        Ts = $"2026-09-01T{aStarted}Z",
        App = "alpha",
        Cmd = aCmd,
        Started = $"2026-09-01T{aStarted}Z",
        Ended = $"2026-09-01T{aStarted}Z"
    };

    /// <summary>One correction naming a run by the pair every run carries.</summary>
    /// <param name="aCmd">The voided run's command.</param>
    /// <param name="aStarted">The voided run's start.</param>
    /// <param name="aReason">Why it should not be counted.</param>
    /// <returns>The record.</returns>
    private static RunRecord Void(string aCmd, string aStarted, string aReason) =>
        Run(aCmd, aStarted) with { Kind = RunVoid.Kind, VoidReason = aReason };
}
