using System.Reflection;
using System.Text.RegularExpressions;
using TfLens.Core.Abstractions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-022 (BRD-168) — no TfLens surface groups, ranks or compares any figure by <c>actor</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Playbook stream carries an <c>actor</c> on its records (SCHEMA.md §11) and both AIFP contracts
/// forbid reporting on it. A reader who has not met the rule reasonably asks why a producer would
/// forbid a grouping it is perfectly able to emit, so the answer is written into every failure message
/// this class can raise: the field records <b>whose machine a record came from</b>, which is provenance,
/// and the moment it becomes an axis it stops answering that question and starts answering "who is
/// worse", which the data cannot support. Quality, misses, rework, effort, tokens, time and cost are
/// all confounded by <i>which work went to whom</i>; a per-actor column reads as a scoreboard however
/// it is captioned, and the caption is the first thing a screenshot loses.
/// </para>
/// <para>
/// So the prohibition is structural rather than editorial. There is deliberately no query parameter,
/// no filter, no toggle, no <c>GROUP BY</c>, no <c>.GroupBy(</c>, no actor-keyed dictionary and no
/// route parameter anywhere in the tree that could produce such a grouping — because a rule that lives
/// only in a review comment is one merge away from gone.
/// </para>
/// <para>
/// <b>The nuance that keeps this test alive.</b> Storing, parsing, exporting and displaying the
/// <c>actor</c> of a single record is ALLOWED and expected — that is the provenance the field exists
/// for. Only <i>grouping, ranking and comparison</i> are refused. The checks therefore never look for
/// the identifier on its own: they look for it in the key position of an aggregation
/// (<c>GroupBy</c>, <c>ToLookup</c>, <c>ToDictionary</c>, <c>OrderBy</c>, <c>GROUP BY</c>,
/// <c>ORDER BY</c>, <c>PARTITION BY</c>), in the name of a member that can only be an aggregate
/// (<c>…ByActor</c>, <c>ActorTotals</c>, <c>ActorLeaderboard</c>), in a route or query parameter, or as
/// a column on an aggregate result type. A check that banned the column outright would be wrong, would
/// fire the first time someone needs to show where a record came from, and would be deleted that day.
/// </para>
/// <para>
/// One escape hatch, declared in the guarded source itself and costing a sentence of justification:
/// a trailing <c>REQ-NFR-022 provenance: why</c> comment on the offending line. It exists for the
/// legitimate case — a deterministic <c>OrderBy</c> over a raw record listing, where the actor is being
/// shown, not scored. It is explicit rather than inferred on purpose: a check that quietly forgave
/// anything it could not classify would forgive the very defect it was written for.
/// </para>
/// <para>
/// These are static checks over the working tree, as every guardrail in this project is, because
/// "no surface can group by actor" is a statement about every code path including the ones no test
/// reaches. A negative is only provable against the source.
/// </para>
/// </remarks>
public sealed class ActorGroupingTests
{
    /// <summary>The documented escape hatch, so every deliberate exception is one grep away.</summary>
    private const string Waiver = "REQ-NFR-022 provenance:";

    /// <summary>One sentence of WHY, appended to every finding so the message stands alone.</summary>
    private const string Why =
        "`actor` is a provenance field — it records whose machine a record came from — and is never a "
        + "grouping key on any TfLens surface (BRD-168). Grouping it turns a fact about origin into a "
        + "comparison the data cannot support, because every figure it would key is confounded by which "
        + "work went to whom.";

    /// <summary>
    /// An aggregation, lookup or ordering whose key expression names the actor.
    /// </summary>
    /// <remarks>
    /// The key expression is bounded to a single parenthesis-free run so the match stays inside the one
    /// call being judged; a later argument in a longer chain cannot drag an innocent line in.
    /// </remarks>
    private static readonly Regex ActorKeyedAggregation = new(
        @"\.(?:GroupBy|ToLookup|ToDictionary|CountBy|AggregateBy|OrderBy|OrderByDescending|ThenBy"
        + @"|ThenByDescending|DistinctBy|MaxBy|MinBy|Partition)\s*\(\s*[^()\n]{0,160}\bActor\w*\b",
        RegexOptions.Compiled);

    /// <summary>
    /// A member or type whose NAME can only be an actor-keyed aggregate.
    /// </summary>
    /// <remarks>
    /// <c>TokensByActor</c>, <c>PerActor</c>, <c>ActorTotals</c> and <c>ActorLookup</c> are all the
    /// grouping arriving through a dictionary rather than through LINQ. A bare <c>Actor</c> property is
    /// deliberately absent from this pattern — that one is the provenance the field exists for.
    /// </remarks>
    private static readonly Regex ActorNamedAggregate = new(
        @"\b\w*(?:By|Per)Actors?\b"
        + @"|\b\w*Actors?(?:Breakdown|Leaderboard|Ranking|Rankings|Scoreboard|Totals|Total|Summary|Split"
        + @"|Comparison|Share|Rate|Rollup|Distribution|Grouping|Group|Groups|Bucket|Buckets|Series"
        + @"|Map|Index|Lookup|Table|Counts|Count|Figures|Metrics)\b",
        RegexOptions.Compiled);

    /// <summary>A route segment or query parameter named for the actor.</summary>
    /// <remarks>
    /// Matched against the RAW line rather than the literal-stripped one, because every shape here —
    /// a route template, a query string, a <c>[SupplyParameterFromQuery]</c> name — lives inside a
    /// string literal, which is exactly what <see cref="RepoTree.StripLiterals"/> throws away.
    /// </remarks>
    private static readonly Regex ActorRouteOrQuery = new(
        @"@page\s+""[^""]*\{[^}]*[Aa]ctor"
        + @"|[?&]actor="
        + @"|\bName\s*=\s*""actor""",
        RegexOptions.Compiled);

    /// <summary>An attribute that binds a route segment or query parameter onto a property.</summary>
    private static readonly Regex BindingAttribute = new(
        @"\[(?:Parameter|SupplyParameterFromQuery|FromQuery|FromRoute)\b",
        RegexOptions.Compiled);

    /// <summary>A property declaration named for the actor.</summary>
    private static readonly Regex ActorProperty = new(
        @"\bpublic\s+[\w<>?,\s\[\]]+\s+Actors?\w*\s*\{", RegexOptions.Compiled);

    /// <summary>An SQL clause that groups, orders or partitions on the actor column.</summary>
    private static readonly Regex ActorGroupingInSql = new(
        @"(?:GROUP|ORDER|PARTITION)\s+BY\s+[^;)]{0,200}?""?actor""?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Type-name suffixes that mark a type as an aggregate rather than a raw record.</summary>
    /// <remarks>
    /// A stream record type legitimately carries the actor — that is the provenance. An aggregate
    /// cannot: by the time a figure has been computed the actor can only be an axis.
    /// </remarks>
    private static readonly string[] AggregateSuffixes =
    [
        "Figures", "Metrics", "Analysis", "Totals", "Summary", "Rollup", "Breakdown", "Distribution",
        "Report", "Coverage", "Window", "Observation"
    ];

    /// <summary>
    /// No C# or Razor source keys an aggregation, an ordering or a dictionary on the actor.
    /// </summary>
    /// <remarks>
    /// This is the clause that would fail first if a "team view" were ever added: the natural first
    /// keystroke is <c>.GroupBy(aRun =&gt; aRun.Actor)</c>, and it fails the build before it reaches a
    /// reviewer.
    /// </remarks>
    [Fact]
    public void NoSourceGroupsRanksOrKeysAnythingByActor()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            vFindings.AddRange(FindingsIn(RepoTree.Relative(vPath), File.ReadAllLines(vPath)));
        }

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// The schema groups, orders and partitions on no actor column.
    /// </summary>
    /// <remarks>
    /// The prohibition has to reach the database or it is only half a rule: a view or an index built to
    /// serve a per-actor grouping is the grouping, whatever the C# above it does.
    /// </remarks>
    [Fact]
    public void NoSqlGroupsOrOrdersByActor()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.sql", "database"))
        {
            var vLines = File.ReadAllLines(vPath);
            var vRelative = RepoTree.Relative(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (IsComment(vLine) || vLine.Contains(Waiver, StringComparison.Ordinal))
                {
                    continue;
                }

                var vMatch = ActorGroupingInSql.Match(vLine);

                if (vMatch.Success)
                {
                    vFindings.Add(Finding(vRelative, vIndex + 1, vMatch.Value.Trim(), vLine));
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// No aggregate result type carries an actor column.
    /// </summary>
    /// <remarks>
    /// The engine's contracts are the last place the rule can be enforced by shape rather than by
    /// discipline, and it is the cheapest: a page cannot render an axis its result type has no property
    /// for. Stream record types are untouched — <c>PbEventRecord</c> and its siblings are exactly where
    /// the provenance belongs.
    /// </remarks>
    [Fact]
    public void NoAggregateResultTypeCarriesAnActorColumn()
    {
        var vOffenders = typeof(ITelemetryStore).Assembly
            .GetTypes()
            .Where(aType => aType.IsPublic)
            .Where(aType => AggregateSuffixes.Any(aSuffix =>
                aType.Name.EndsWith(aSuffix, StringComparison.Ordinal)))
            .SelectMany(aType => aType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(aProperty => aProperty.Name.Contains("Actor", StringComparison.Ordinal))
                .Select(aProperty => $"{aType.Name}.{aProperty.Name} — {Why}"))
            .ToList();

        Assert.True(vOffenders.Count == 0, Report(vOffenders));
    }

    /// <summary>
    /// The checks refuse comparison and permit provenance, demonstrated on both.
    /// </summary>
    /// <remarks>
    /// This is the test that keeps the other three honest. The whole value of REQ-NFR-022 rests on the
    /// scan being able to tell "show me where this record came from" apart from "rank these people",
    /// and a claim in an XML doc is not a demonstration. The allowed samples below are the real shapes
    /// the product needs — a stored column, a local read, an exported key, a rendered cell — and each is
    /// asserted to pass, so narrowing this guardrail into uselessness would fail here first.
    /// </remarks>
    [Fact]
    public void ProvenanceIsPermittedAndOnlyComparisonIsRefused()
    {
        string[] vAllowed =
        [
            "    public string? Actor { get; init; }",
            "        var vActor = vRecord.Actor;",
            "                [\"actor\"] = vEvent.Actor,",
            "        <span class=\"font-mono\">@vRow.Actor</span>",
            "        vRows = vRows.Where(aRow => aRow.Actor is not null).ToList();",
            "        aCommand.Parameters.AddWithValue(\"Actor\", vEvent.Actor);"
        ];

        string[] vRefused =
        [
            "        var vRows = aRuns.GroupBy(aRun => aRun.Actor).ToList();",
            "        var vTop = aRuns.OrderByDescending(aRun => aRun.Actor).First();",
            "    public IReadOnlyList<Figure> TokensByActor { get; init; } = [];",
            "    private Dictionary<string, int> objActorTotals = new(StringComparer.Ordinal);",
            "@page \"/effort/{Actor}\"",
            "    [SupplyParameterFromQuery(Name = \"actor\")]"
        ];

        foreach (var vLine in vAllowed)
        {
            Assert.True(
                FindingsIn("sample.cs", [vLine]).Count == 0,
                "REQ-NFR-022 must permit reading and showing the actor for provenance, and this check "
                + $"refused it — the guardrail has been narrowed into a ban on the column: {vLine}");
        }

        foreach (var vLine in vRefused)
        {
            Assert.True(
                FindingsIn("sample.cs", [vLine]).Count > 0,
                "REQ-NFR-022 must refuse an actor-keyed grouping, ranking, dictionary, route or query "
                + $"parameter, and this check let it through: {vLine}");
        }

        // SQL is judged on the raw line: PostgreSQL folds unquoted identifiers to lower case, so every
        // column here is double-quoted and a literal-stripping scan would blank the column name.
        Assert.Matches(ActorGroupingInSql, "SELECT \"Actor\", COUNT(*) FROM \"PbEvent\" GROUP BY \"Actor\";");
        Assert.DoesNotMatch(ActorGroupingInSql, "SELECT \"Actor\" FROM \"PbEvent\" ORDER BY \"Ts\";");

        // The escape hatch works, and only where it is declared.
        Assert.Empty(FindingsIn(
            "sample.cs",
            ["        var vSorted = aRows.OrderBy(aRow => aRow.Actor); // REQ-NFR-022 provenance: stable "
             + "ordering of a raw record listing, no figure is keyed on it"]));
    }

    /// <summary>
    /// Judges one file's lines and yields a finding for each violation.
    /// </summary>
    /// <param name="aRelativePath">The repository-relative path, for the message.</param>
    /// <param name="aLines">The file's lines.</param>
    /// <returns>One finding per offending line.</returns>
    private static IReadOnlyList<string> FindingsIn(string aRelativePath, IReadOnlyList<string> aLines)
    {
        var vFindings = new List<string>();

        for (var vIndex = 0; vIndex < aLines.Count; vIndex++)
        {
            var vLine = aLines[vIndex];

            if (IsComment(vLine) || vLine.Contains(Waiver, StringComparison.Ordinal))
            {
                continue;
            }

            var vCode = RepoTree.StripLiterals(vLine);

            var vMatch = ActorKeyedAggregation.Match(vCode);
            vMatch = vMatch.Success ? vMatch : ActorNamedAggregate.Match(vCode);
            vMatch = vMatch.Success ? vMatch : ActorRouteOrQuery.Match(vLine);

            if (vMatch.Success)
            {
                vFindings.Add(Finding(aRelativePath, vIndex + 1, vMatch.Value.Trim(), vLine));
                continue;
            }

            if (BindsAnActorParameter(aLines, vIndex))
            {
                vFindings.Add(Finding(aRelativePath, vIndex + 1, aLines[vIndex].Trim(), vLine));
            }
        }

        return vFindings;
    }

    /// <summary>
    /// Tells whether a route or query binding attribute is attached to an actor property.
    /// </summary>
    /// <remarks>
    /// The attribute and the property it decorates sit on different lines, so a line-at-a-time regex
    /// cannot see the pair. The window is two lines because an attribute may carry an XML doc line of
    /// its own between it and the declaration.
    /// </remarks>
    /// <param name="aLines">The file's lines.</param>
    /// <param name="aIndex">The index of the attribute line.</param>
    /// <returns><c>true</c> when the attribute binds an actor-named property.</returns>
    private static bool BindsAnActorParameter(IReadOnlyList<string> aLines, int aIndex)
    {
        if (!BindingAttribute.IsMatch(aLines[aIndex]))
        {
            return false;
        }

        for (var vAhead = aIndex + 1; vAhead <= aIndex + 2 && vAhead < aLines.Count; vAhead++)
        {
            if (ActorProperty.IsMatch(aLines[vAhead]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Renders one finding with its location, the text that matched and the reason.</summary>
    /// <param name="aRelativePath">The repository-relative path.</param>
    /// <param name="aLine">The 1-based line number.</param>
    /// <param name="aMatched">The text the pattern matched.</param>
    /// <param name="aSource">The whole source line.</param>
    /// <returns>The finding.</returns>
    private static string Finding(string aRelativePath, int aLine, string aMatched, string aSource) =>
        $"{aRelativePath}:{aLine} — matched `{aMatched}` in `{aSource.Trim()}`. {Why} "
        + $"If this line genuinely shows the actor rather than scoring it, say so on the line: "
        + $"`// {Waiver} why`.";

    /// <summary>Tells whether a source line is a comment and so cannot violate anything.</summary>
    /// <param name="aLine">One source line.</param>
    /// <returns><c>true</c> when the line is a comment.</returns>
    private static bool IsComment(string aLine)
    {
        var vTrimmed = aLine.TrimStart();

        return vTrimmed.StartsWith("//", StringComparison.Ordinal)
            || vTrimmed.StartsWith("*", StringComparison.Ordinal)
            || vTrimmed.StartsWith("@*", StringComparison.Ordinal)
            || vTrimmed.StartsWith("--", StringComparison.Ordinal);
    }

    /// <summary>Renders a finding list into a failure message a reader can act on.</summary>
    /// <param name="aFindings">Every violation found.</param>
    /// <returns>The message.</returns>
    private static string Report(IReadOnlyList<string> aFindings) =>
        $"REQ-NFR-022 (BRD-168) — {aFindings.Count} actor-grouped reporting surface(s):"
        + Environment.NewLine
        + string.Join(Environment.NewLine, aFindings);
}
