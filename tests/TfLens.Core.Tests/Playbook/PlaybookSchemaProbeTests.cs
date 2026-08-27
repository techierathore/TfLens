using FluentAssertions;
using TfLens.Core.Playbook;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-068 / ADR-010 — the probe reports what a real <c>events.ndjson</c> actually carries, so no
/// column is ever fixed from a description.
/// </summary>
public sealed class PlaybookSchemaProbeTests
{
    /// <summary>The synthetic fixture, faithful to the emitter shape (see Fixtures/Playbook/README.md).</summary>
    private static readonly string FixtureText = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Playbook", "events-synthetic.ndjson"));

    /// <summary>Every top-level field name in the file is reported.</summary>
    [Fact]
    public void ProbeReportsEveryTopLevelFieldName()
    {
        var vObservation = PlaybookSchemaProbe.Observe(FixtureText);

        vObservation.FieldNames.Should().BeEquivalentTo(
            ["kind", "ts", "command", "arguments", "sessionID", "parentID", "messageID", "model", "tokens", "cost"]);
    }

    /// <summary>Records and malformed lines are counted separately.</summary>
    [Fact]
    public void ProbeCountsRecordsAndMalformedLines()
    {
        var vObservation = PlaybookSchemaProbe.Observe(FixtureText);

        vObservation.Records.Should().Be(8);
        vObservation.InvalidLines.Should().Be(1);
    }

    /// <summary>A field that is sometimes null reports both value kinds, so the nullability is visible.</summary>
    [Fact]
    public void ProbeReportsEveryValueKindSeenForAField()
    {
        var vObservation = PlaybookSchemaProbe.Observe(FixtureText);

        var vParent = vObservation.Fields.Single(aF => aF.Name == "parentID");
        vParent.JsonKinds.Should().BeEquivalentTo(["Null", "String"]);
    }

    /// <summary>A nested object is reported as an object, not flattened away.</summary>
    [Fact]
    public void ProbeReportsNestedTokensAsAnObject()
    {
        var vObservation = PlaybookSchemaProbe.Observe(FixtureText);

        vObservation.Fields.Single(aF => aF.Name == "tokens").JsonKinds.Should().Equal("Object");
    }

    /// <summary>Fields are ordered most frequent first, so the always-present ones lead.</summary>
    /// <remarks>
    /// <c>kind</c>, <c>sessionID</c> and <c>ts</c> appear on all three record kinds; ties are broken by
    /// name so the ordering is deterministic and the DECISIONS.md table is stable across runs.
    /// </remarks>
    [Fact]
    public void ProbeOrdersFieldsByFrequency()
    {
        var vObservation = PlaybookSchemaProbe.Observe(FixtureText);

        vObservation.Fields.Select(aF => aF.Name).Take(3)
            .Should().Equal("kind", "sessionID", "ts");
        vObservation.Fields.Select(aF => aF.Occurrences)
            .Should().BeInDescendingOrder();
    }

    /// <summary>The Markdown rendering carries the field table the DECISIONS.md entry needs.</summary>
    [Fact]
    public void MarkdownRenderingCarriesTheFieldTable()
    {
        var vMarkdown = PlaybookSchemaProbe.ToDecisionsMarkdown(
            PlaybookSchemaProbe.Observe(FixtureText), "fixture");

        vMarkdown.Should().Contain("| Field | Occurrences | JSON kinds | Sample values |");
        vMarkdown.Should().Contain("`messageID`");
    }

    /// <summary>An empty file yields an empty observation rather than throwing.</summary>
    [Fact]
    public void EmptyFileYieldsAnEmptyObservation()
    {
        var vObservation = PlaybookSchemaProbe.Observe(string.Empty);

        vObservation.Records.Should().Be(0);
        vObservation.Fields.Should().BeEmpty();
    }
}
