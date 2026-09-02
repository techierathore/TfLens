using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;

namespace TfLens.Core.Tests.Parsing;

/// <summary>
/// The three SCHEMA §2.6 <c>runs.jsonl</c> fields, and the rule that an unknown producer field is
/// preserved rather than rejected (REQ-FN-088, BRD-145, BRD-29).
/// </summary>
/// <remarks>
/// <para>
/// The whole point of these fields is that <c>null</c> and <c>0</c> are different facts. A
/// <c>main</c>-scope window never read the sub-agent transcripts, so it did not report "no sub-agents
/// ran" — it reported nothing at all, and a parser that answered <c>0</c> would turn "we did not look"
/// into a measurement. So the load-bearing assertion here is the <b>absent</b> case, not the present
/// one (ADR-026).
/// </para>
/// <para>
/// The second rule matters for a different reason: the producer ships ahead of TfLens, so a field TfLens
/// has never heard of is a normal event, not a corrupt line. Counting it as invalid would make an
/// upgrade upstream look like data loss down here.
/// </para>
/// </remarks>
public sealed class PhaseEffortFieldTests
{
    private const int TestUserId = 4242;
    private const string TestRepo = "techierathore/TechieFlow";
    private const string TestSha = "b17c0de";

    private readonly StreamParser objParser = new();

    /// <summary>A run carrying all three §2.6 fields parses each of them onto its own column.</summary>
    [Fact]
    public void RunCarryingTheThreeFieldsParsesThem()
    {
        var vLine = """
            {"v":1,"ts":"2026-08-31T10:00:00Z","app":"tflens","cmd":"build-phase","tokens_out":900,
             "tokens_scope":"tree","subagent_runs":4,"tokens_out_subagents":610,
             "model_tokens_out":{"claude-opus-5":700,"claude-haiku-4":200}}
            """.ReplaceLineEndings(" ");

        var vRun = Parse(vLine).Runs.Single();

        vRun.SubagentRuns.Should().Be(4);
        vRun.TokensOutSubagents.Should().Be(610);
        vRun.ModelTokensOut.Should().NotBeNull();
        vRun.ModelTokensOut!.Should().HaveCount(2);
        vRun.ModelTokensOut["claude-opus-5"].Should().Be(700L);
        vRun.ModelTokensOut["claude-haiku-4"].Should().Be(200L);
    }

    /// <summary>
    /// A run written without the three fields leaves them <c>null</c> — never zero, and never an empty map.
    /// </summary>
    [Fact]
    public void RunWithoutTheThreeFieldsLeavesThemNull()
    {
        var vLine = """
            {"v":1,"ts":"2026-08-20T10:00:00Z","app":"tflens","cmd":"build-phase","tokens_out":900,
             "tokens_scope":"main"}
            """.ReplaceLineEndings(" ");

        var vRun = Parse(vLine).Runs.Single();

        vRun.SubagentRuns.Should().BeNull("an absent count is 'not captured', which is not a measured zero");
        vRun.TokensOutSubagents.Should().BeNull();
        vRun.ModelTokensOut.Should().BeNull("an empty map would claim a split was measured and named nothing");
    }

    /// <summary>A measured zero survives as zero, so the null case cannot be read as "always absent".</summary>
    [Fact]
    public void MeasuredZeroSubagentRunsIsKeptAsZero()
    {
        var vLine = """
            {"v":1,"ts":"2026-08-31T10:00:00Z","app":"tflens","cmd":"verify-phase",
             "tokens_scope":"tree","subagent_runs":0,"tokens_out_subagents":0}
            """.ReplaceLineEndings(" ");

        var vRun = Parse(vLine).Runs.Single();

        vRun.SubagentRuns.Should().Be(0);
        vRun.TokensOutSubagents.Should().Be(0);
    }

    /// <summary>
    /// A field the parser does not know lands in <c>Overflow</c> and does <b>not</b> increment
    /// <see cref="ParseResult.InvalidLines"/>.
    /// </summary>
    [Fact]
    public void UnknownProducerFieldOverflowsAndIsNotCountedInvalid()
    {
        var vLine = """
            {"v":1,"ts":"2026-08-31T10:00:00Z","app":"tflens","cmd":"build-phase",
             "subagent_runs":2,"a_field_tflens_has_never_heard_of":"keep me"}
            """.ReplaceLineEndings(" ");

        var vResult = Parse(vLine);
        var vRun = vResult.Runs.Single();

        vResult.InvalidLines.Should().Be(0, "a producer that shipped a new field has not written a bad line");
        vRun.SubagentRuns.Should().Be(2, "the fields TfLens does know still resolve to their columns");
        vRun.Overflow.Should().NotBeNull();

        using var vOverflow = JsonDocument.Parse(vRun.Overflow!);
        vOverflow.RootElement.GetProperty("a_field_tflens_has_never_heard_of").GetString().Should().Be("keep me");
        vResult.UnknownFields.Should().Equal("a_field_tflens_has_never_heard_of");
    }

    /// <summary>
    /// The three §2.6 names are documented, so they never surface as "fields SCHEMA.md does not
    /// document" on Coverage and never land in <c>Overflow</c>.
    /// </summary>
    [Fact]
    public void TheThreeFieldsAreDocumentedAndDoNotOverflow()
    {
        foreach (var vField in new[] { "subagent_runs", "tokens_out_subagents", "model_tokens_out" })
        {
            StreamParser.IsDocumented(StreamKind.Runs, vField).Should().BeTrue();
        }

        var vLine = """
            {"v":1,"ts":"2026-08-31T10:00:00Z","app":"tflens","cmd":"build-phase",
             "subagent_runs":1,"tokens_out_subagents":5,"model_tokens_out":{"m":5}}
            """.ReplaceLineEndings(" ");

        var vResult = Parse(vLine);

        vResult.Runs.Single().Overflow.Should().BeNull();
        vResult.UnknownFields.Should().BeEmpty();
    }

    /// <summary>Parses one runs line through the real parser.</summary>
    /// <param name="aLine">The JSONL line.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(string aLine) =>
        objParser.Parse(TestUserId, TestRepo, TestSha, StreamKind.Runs, aLine);
}
