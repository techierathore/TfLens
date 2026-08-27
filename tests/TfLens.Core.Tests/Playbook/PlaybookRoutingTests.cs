using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Playbook;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-069 / BRD-109 — a Playbook repository that has converged on schema v1 flows through the same
/// parser, engine and pages as a TechieFlow one, tagged <c>playbook</c>, with no adapter involved.
/// </summary>
/// <remarks>
/// The whole requirement is one distinction: the framework tag says whose process produced the work and
/// keeps the figures from pooling (ADR-016); the telemetry layout says which files exist and therefore
/// which code reads them. Conflating the two is what would force a second parser into existence.
/// </remarks>
public sealed class PlaybookRoutingTests
{
    /// <summary>A converged Playbook repository is read by the schema-v1 path, not the adapter.</summary>
    [Fact]
    public void ConvergedPlaybookRepoRoutesToTheSharedParser()
    {
        var vRepo = Repo(FrameworkNames.Playbook, FrameworkNames.TechieFlow);

        PlaybookRouting.RouteFor(vRepo).Should().Be(TelemetryRoute.SchemaV1Streams);
        PlaybookRouting.UsesAdapter(vRepo).Should().BeFalse();
    }

    /// <summary>A converged Playbook repository is read from <c>docs/metrics</c>, like any TechieFlow repository.</summary>
    [Fact]
    public void ConvergedPlaybookRepoReadsTheTechieFlowPath()
    {
        var vRepo = Repo(FrameworkNames.Playbook, FrameworkNames.TechieFlow);

        PlaybookRouting.TelemetryPathFor(vRepo).Should().Be("docs/metrics");
    }

    /// <summary>A converged Playbook repository fetches the same four streams a TechieFlow repository does.</summary>
    [Fact]
    public void ConvergedPlaybookRepoFetchesTheFourStreams()
    {
        var vPlaybook = Repo(FrameworkNames.Playbook, FrameworkNames.TechieFlow);
        var vTechieFlow = Repo(FrameworkNames.TechieFlow, FrameworkNames.TechieFlow);

        PlaybookRouting.StreamsFor(vPlaybook).Should().Equal(StreamNames.TechieFlow);
        PlaybookRouting.StreamsFor(vPlaybook).Should().Equal(PlaybookRouting.StreamsFor(vTechieFlow));
    }

    /// <summary>The framework tag survives the shared route, so its figures never pool with TechieFlow's.</summary>
    [Fact]
    public void ConvergedPlaybookRepoKeepsItsFrameworkTag()
    {
        Repo(FrameworkNames.Playbook, FrameworkNames.TechieFlow).Framework
            .Should().Be(FrameworkNames.Playbook);
    }

    /// <summary>Only an <c>events.ndjson</c> layout reaches the adapter.</summary>
    [Fact]
    public void EventsLayoutRoutesToTheAdapter()
    {
        var vRepo = Repo(FrameworkNames.Playbook, FrameworkNames.Playbook);

        PlaybookRouting.RouteFor(vRepo).Should().Be(TelemetryRoute.PlaybookAdapter);
        PlaybookRouting.TelemetryPathFor(vRepo).Should().Be("verification/telemetry");
        PlaybookRouting.StreamsFor(vRepo).Should().Equal(StreamNames.Playbook);
    }

    /// <summary>A TechieFlow repository is unaffected by any of this.</summary>
    [Fact]
    public void TechieFlowRepoRoutesToTheSharedParser()
    {
        PlaybookRouting.RouteFor(Repo(FrameworkNames.TechieFlow, FrameworkNames.TechieFlow))
            .Should().Be(TelemetryRoute.SchemaV1Streams);
    }

    /// <summary>An unknown layout is refused rather than silently defaulted to one of the two.</summary>
    [Fact]
    public void UnknownLayoutIsRefused()
    {
        var vAct = () => PlaybookRouting.RouteForKind("something-else");

        vAct.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Builds a connected repository row.
    /// </summary>
    /// <param name="aFramework">The provenance tag.</param>
    /// <param name="aKind">The detected telemetry layout.</param>
    /// <returns>The row.</returns>
    private static UserRepo Repo(string aFramework, string aKind) => new()
    {
        UserId = 2,
        Repo = "techierathore/AI-First-Playbook",
        Owner = "techierathore",
        Name = "AI-First-Playbook",
        Branch = "main",
        Kind = aKind,
        Framework = aFramework,
        ConnectedTs = "2026-08-26T09:00:00.000Z"
    };
}
