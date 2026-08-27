using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-066 / SCHEMA.md §11 — Playbook process-gates and TechieFlow assertion-gates are different
/// axes and never share a table, a column or a chart.
/// </summary>
/// <remarks>
/// <para>
/// These tests exist to fail. The separation is easy to state and easy to erode: someone adds a
/// <c>Gate</c> column to <c>"PbEvent"</c> to "reuse the chart", or types a Playbook result member as
/// <c>string</c> so it can be dropped into an existing table component, and nothing complains until a
/// report quietly pools two incomparable things. Each test below fixes one of those routes shut.
/// </para>
/// <para>
/// The first line of defence is the type system: <see cref="PhaseGateKey"/> is a distinct struct with no
/// conversion to or from <see cref="string"/>, and every TechieFlow gate member is a
/// <see cref="string"/>, so neither can be assigned into the other's slot. These tests guard the parts
/// the compiler cannot: the shape of the result graphs, the DDL, and the SQL.
/// </para>
/// </remarks>
public sealed class PlaybookAxisSeparationTests
{
    /// <summary>The TechieFlow types that carry assertion-gate data.</summary>
    private static readonly Type[] TechieFlowGateTypes =
        [typeof(GateCount), typeof(LateGateCoverage), typeof(SegmentFigures), typeof(AnalysisResult)];

    /// <summary>The Playbook types that carry process-gate data.</summary>
    private static readonly Type[] PlaybookGateTypes =
        [typeof(PhaseGateKey), typeof(PhaseGateTotals), typeof(PhaseGateQuestions), typeof(PlaybookAnalysis)];

    /// <summary>
    /// The Playbook result graph reaches no TechieFlow assertion-gate type, so no chart bound to a
    /// Playbook result can be fed assertion-gate data.
    /// </summary>
    [Fact]
    public void PlaybookAnalysisGraphHoldsNoAssertionGateType()
    {
        var vReached = Reachable(typeof(PlaybookAnalysis));

        vReached.Should().NotIntersectWith(
            TechieFlowGateTypes,
            "a Playbook result must not be able to hold TechieFlow assertion-gate data (SCHEMA.md §11)");
    }

    /// <summary>
    /// The TechieFlow result graph reaches no Playbook process-gate type, so no chart bound to an
    /// analysis can be fed phase-gate data.
    /// </summary>
    [Fact]
    public void AnalysisResultGraphHoldsNoProcessGateType()
    {
        var vReached = Reachable(typeof(AnalysisResult));

        vReached.Should().NotIntersectWith(
            PlaybookGateTypes,
            "a TechieFlow result must not be able to hold Playbook process-gate data (SCHEMA.md §11)");
    }

    /// <summary>Every gate-named member of a TechieFlow result is a string, never the Playbook key type.</summary>
    [Fact]
    public void TechieFlowGateMembersAreNotPhaseGateKeys()
    {
        var vMembers = TechieFlowGateTypes
            .SelectMany(aT => aT.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(aP => aP.Name.Contains("Gate", StringComparison.Ordinal))
            .ToList();

        vMembers.Should().NotBeEmpty("the test would pass vacuously if the gate members were renamed");
        vMembers.Should().NotContain(aP => aP.PropertyType == typeof(PhaseGateKey));
    }

    /// <summary>Every gate-named member of a Playbook result is the key type, never a bare string.</summary>
    /// <remarks>
    /// The direction that actually protects anything: a <c>string</c> here would be assignable straight
    /// into a TechieFlow gate slot, and the separation would survive only as a naming convention.
    /// </remarks>
    [Fact]
    public void PlaybookGateMembersAreNotBareStrings()
    {
        var vMembers = new[] { typeof(PhaseGateTotals), typeof(PhaseGateQuestions) }
            .SelectMany(aT => aT.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(aP => aP.Name.Contains("Gate", StringComparison.Ordinal))
            .ToList();

        vMembers.Should().NotBeEmpty("the test would pass vacuously if the gate members were renamed");
        vMembers.Should().OnlyContain(aP => aP.PropertyType == typeof(PhaseGateKey));
    }

    /// <summary><see cref="PhaseGateKey"/> offers no conversion to or from a TechieFlow gate string.</summary>
    [Fact]
    public void PhaseGateKeyHasNoStringConversion()
    {
        var vConversions = typeof(PhaseGateKey)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(aM => aM.Name is "op_Implicit" or "op_Explicit")
            .ToList();

        vConversions.Should().BeEmpty(
            "an implicit or explicit string conversion would let a phase-gate flow into an assertion-gate slot");
    }

    /// <summary>The Playbook table has no assertion-gate column and the gate table has no process-gate column.</summary>
    [Fact]
    public void GateColumnsAreNotSharedBetweenTables()
    {
        var vSchema = ReadSchema();

        var vPbEvent = TableBody(vSchema, "PbEvent");
        var vGate = TableBody(vSchema, "Gate");

        vPbEvent.Should().NotContain("\"Gate\"", "the Playbook table must carry no assertion-gate column");
        vPbEvent.Should().NotContain("\"GatesRun\"");
        vGate.Should().NotContain("\"PhaseGate\"", "the gate table must carry no process-gate column");
    }

    /// <summary>The Playbook table really does carry the process-gate column, so the test above is not vacuous.</summary>
    [Fact]
    public void PbEventTableCarriesThePhaseGateColumn()
    {
        TableBody(ReadSchema(), "PbEvent").Should().Contain("\"PhaseGate\"");
    }

    /// <summary>No SQL statement anywhere in the product joins the gate table to the Playbook table.</summary>
    /// <remarks>
    /// A join is the one construct that could put both axes into a single result set. Listing both tables
    /// in a <c>TRUNCATE</c> or a rebuild is fine and deliberately not flagged.
    /// </remarks>
    [Fact]
    public void NoSqlStatementJoinsGateToPbEvent()
    {
        var vOffenders = new List<string>();

        foreach (var vFile in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (vFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var vText = File.ReadAllText(vFile);
            var vHasJoin = vText.Contains("JOIN", StringComparison.OrdinalIgnoreCase);
            if (vHasJoin && vText.Contains("\"PbEvent\"", StringComparison.Ordinal)
                && Regex.IsMatch(vText, "\"\"Gate\"\"|\\\\\"Gate\\\\\""))
            {
                vOffenders.Add(vFile);
            }
        }

        vOffenders.Should().BeEmpty("no query may join the process-gate axis to the assertion-gate axis");
    }

    /// <summary>
    /// Collects every type reachable from a result type's public properties.
    /// </summary>
    /// <param name="aRoot">The result type to walk from.</param>
    /// <returns>Every type in the graph, including collection element types.</returns>
    private static HashSet<Type> Reachable(Type aRoot)
    {
        var vSeen = new HashSet<Type>();
        var vQueue = new Queue<Type>();
        vQueue.Enqueue(aRoot);

        while (vQueue.Count > 0)
        {
            var vType = Unwrap(vQueue.Dequeue());
            if (vType.Assembly != typeof(AnalysisResult).Assembly || !vSeen.Add(vType))
            {
                continue;
            }

            foreach (var vProperty in vType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                vQueue.Enqueue(vProperty.PropertyType);
            }
        }

        return vSeen;
    }

    /// <summary>
    /// Reduces a nullable or collection type to the type actually carried.
    /// </summary>
    /// <param name="aType">The declared property type.</param>
    /// <returns>The element or underlying type.</returns>
    private static Type Unwrap(Type aType)
    {
        var vType = Nullable.GetUnderlyingType(aType) ?? aType;
        return vType.IsGenericType ? vType.GetGenericArguments()[^1] : vType;
    }

    /// <summary>Reads the DDL script the store applies at startup.</summary>
    /// <returns>The whole script text.</returns>
    private static string ReadSchema() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "database", "001-schema.sql"));

    /// <summary>
    /// Extracts one <c>CREATE TABLE</c> body from the DDL.
    /// </summary>
    /// <param name="aSchema">The whole script.</param>
    /// <param name="aTable">The table name, unquoted.</param>
    /// <returns>The text between the table's parentheses.</returns>
    private static string TableBody(string aSchema, string aTable)
    {
        var vMatch = Regex.Match(
            aSchema,
            $"CREATE TABLE IF NOT EXISTS \"{aTable}\"\\s*\\((?<body>[^;]*?)\\)\\s*;",
            RegexOptions.Singleline);

        vMatch.Success.Should().BeTrue($"the DDL must declare the \"{aTable}\" table");
        return vMatch.Groups["body"].Value;
    }

    /// <summary>Absolute path of the <c>src</c> directory.</summary>
    /// <returns>The source root.</returns>
    private static string SourceRoot() => Path.Combine(RepoRoot(), "src");

    /// <summary>
    /// Walks up from the test binary to the repository root.
    /// </summary>
    /// <returns>The directory holding <c>TfLens.slnx</c>.</returns>
    /// <exception cref="InvalidOperationException">The root could not be located.</exception>
    private static string RepoRoot()
    {
        var vDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (vDirectory is not null && !File.Exists(Path.Combine(vDirectory.FullName, "TfLens.slnx")))
        {
            vDirectory = vDirectory.Parent;
        }

        return vDirectory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test binary.");
    }
}
