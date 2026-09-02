using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// The shape of the three schema-2 phase tables and of the cross-edition miss columns
/// (REQ-FN-095, REQ-FN-104, REQ-FN-103, BRD-154, BRD-164, BRD-165, ADR-024, ADR-025).
/// </summary>
/// <remarks>
/// <para>
/// These are the contracts three other clusters bind to, so they are asserted structurally rather than
/// behaviourally: a record whose cost is a <c>double</c> or a table whose index forgets its
/// <c>WHERE</c> clause is wrong the moment it is written, not the first time someone looks at a number.
/// </para>
/// <para>
/// Money is the sharp edge. A binary float cannot represent a tenth of a cent exactly, so summing
/// provider costs across a few hundred phase executions drifts — slowly, plausibly and invisibly. The
/// contract says fixed precision, and the type is where that is enforceable.
/// </para>
/// </remarks>
public sealed class PhaseTableContractTests
{
    /// <summary>Every provider-cost member on the three phase records is <c>decimal?</c>, never a float.</summary>
    [Fact]
    public void ProviderCostIsDecimalOnEveryPhaseRecord()
    {
        foreach (var vType in new[]
                 {
                     typeof(PbPhaseExecutionRecord),
                     typeof(PbPhaseModelUsageRecord),
                     typeof(PbPhaseSubagentRecord)
                 })
        {
            var vCost = vType.GetProperty("CostUsd");
            vCost.Should().NotBeNull($"{vType.Name} carries a provider cost");
            vCost!.PropertyType.Should().Be(
                typeof(decimal?),
                "money is fixed precision; a binary float drifts silently as costs are summed");
        }
    }

    /// <summary>No member of any phase record is a binary floating-point type.</summary>
    [Fact]
    public void NoPhaseRecordMemberIsABinaryFloat()
    {
        foreach (var vType in new[]
                 {
                     typeof(PbPhaseExecutionRecord),
                     typeof(PbPhaseModelUsageRecord),
                     typeof(PbPhaseSubagentRecord)
                 })
        {
            foreach (var vProperty in vType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var vBare = Nullable.GetUnderlyingType(vProperty.PropertyType) ?? vProperty.PropertyType;
                vBare.Should().NotBe(typeof(double), $"{vType.Name}.{vProperty.Name}");
                vBare.Should().NotBe(typeof(float), $"{vType.Name}.{vProperty.Name}");
            }
        }
    }

    /// <summary>Token and turn counters that can accumulate over a phase tree are 64-bit.</summary>
    [Fact]
    public void TokenCountersAre64Bit()
    {
        foreach (var vName in new[]
                 {
                     "TokensInput", "TokensOutput", "TokensReasoning", "TokensCacheRead",
                     "TokensCacheWrite", "TokensIn", "TokensOut", "ElapsedMs", "ObservedActiveMs",
                     "AssistantElapsedMs", "ToolElapsedMs"
                 })
        {
            typeof(PbPhaseExecutionRecord).GetProperty(vName)!.PropertyType
                .Should().Be(typeof(long?), $"PbPhaseExecutionRecord.{vName} accumulates over a whole tree");
        }
    }

    /// <summary>
    /// Every phase record is scoped by user and repository, and keyed on the execution id — isolation is
    /// a column on the data, not a filter someone remembers (ADR-013).
    /// </summary>
    [Fact]
    public void EveryPhaseRecordCarriesItsIsolationColumns()
    {
        foreach (var vType in new[]
                 {
                     typeof(PbPhaseExecutionRecord),
                     typeof(PbPhaseModelUsageRecord),
                     typeof(PbPhaseSubagentRecord)
                 })
        {
            vType.GetProperty("UserId")!.PropertyType.Should().Be(typeof(int));
            vType.GetProperty("Repo")!.PropertyType.Should().Be(typeof(string));
            vType.GetProperty("PhaseExecutionId")!.PropertyType.Should().Be(typeof(string));
        }
    }

    /// <summary>
    /// Wall clock, observed-active time and the two diagnostics are four separate members, and there is
    /// no human-effort member at all (ADR-027).
    /// </summary>
    [Fact]
    public void TimingIsThreeTypesAndHumanEffortHasNoColumn()
    {
        var vType = typeof(PbPhaseExecutionRecord);

        vType.GetProperty("ElapsedMs").Should().NotBeNull();
        vType.GetProperty("ObservedActiveMs").Should().NotBeNull();
        vType.GetProperty("AssistantElapsedMs").Should().NotBeNull();
        vType.GetProperty("ToolElapsedMs").Should().NotBeNull();

        var vNames = vType.GetProperties().Select(aP => aP.Name).ToList();
        vNames.Should().NotContain(
            aName => aName.Contains("Human", StringComparison.Ordinal),
            "neither framework captures human effort, and a column that exists gets populated by inference");
    }

    /// <summary>The DDL declares all three phase tables with their unique and read indexes.</summary>
    [Fact]
    public void TheDdlDeclaresTheThreePhaseTablesAndTheirIndexes()
    {
        var vSchema = ReadSchema();

        foreach (var vTable in new[] { "PbPhaseExecution", "PbPhaseModelUsage", "PbPhaseSubagent" })
        {
            vSchema.Should().Contain($"CREATE TABLE IF NOT EXISTS \"{vTable}\"");
        }

        foreach (var vIndex in new[]
                 {
                     "UcPbPhaseExecUserRepoId", "UcPbPhaseModelUserRepoIdModel",
                     "UcPbPhaseSubUserRepoIdSession", "IxPbPhaseExecUserRepo",
                     "IxPbPhaseExecPhase", "IxPbPhaseSubParent"
                 })
        {
            vSchema.Should().Contain($"\"{vIndex}\"");
        }
    }

    /// <summary>Every phase table's cost column is <c>numeric</c>, and none of them is a float type.</summary>
    [Fact]
    public void EveryPhaseTableStoresCostAsNumeric()
    {
        var vSchema = ReadSchema();

        foreach (var vTable in new[] { "PbPhaseExecution", "PbPhaseModelUsage", "PbPhaseSubagent" })
        {
            var vBody = TableBody(vSchema, vTable);
            vBody.Should().MatchRegex(
                "\"CostUsd\"\\s+numeric",
                $"{vTable} stores provider cost as exact decimal");
            vBody.Should().NotMatchRegex(
                "\"\\w+\"\\s+(real|double precision)\\b",
                $"{vTable} must not store any column as a binary float");
        }
    }

    /// <summary>
    /// <c>UcMissUserRepoSourceLine</c> is <b>partial</b>: without the predicate the Playbook key would
    /// be indexed over every TechieFlow row, which carries no hash at all (ADR-024).
    /// </summary>
    [Fact]
    public void TheSourceLineHashIndexIsPartial()
    {
        var vSchema = ReadSchema();

        var vMatch = Regex.Match(
            vSchema,
            "CREATE UNIQUE INDEX IF NOT EXISTS \"UcMissUserRepoSourceLine\"(?<body>[^;]*);",
            RegexOptions.Singleline);

        vMatch.Success.Should().BeTrue("the Playbook's natural key must be declared");
        vMatch.Groups["body"].Value.Should().Contain(
            "WHERE \"SourceLineHash\" IS NOT NULL",
            "TechieFlow rows carry no hash and must not be governed by this key");
        vMatch.Groups["body"].Value.Should().NotContain(
            "COALESCE",
            "coalescing the hash would collide every TechieFlow row on one empty-string key");

        vSchema.Should().Contain(
            "\"UcMissUserRepoMissId\"",
            "the TechieFlow natural key goes on governing its own edition");
    }

    /// <summary>
    /// The cross-edition axes exist as their own nullable members, and the two gate axes are two
    /// members rather than one (REQ-FN-104, BRD-165).
    /// </summary>
    [Fact]
    public void TheCrossEditionAxesAreDistinctNullableMembers()
    {
        var vMiss = typeof(MissRecord);

        foreach (var vName in new[] { "ReqId", "ItemId", "FoundGate", "FoundPhaseGate", "SourceLineHash" })
        {
            var vProperty = vMiss.GetProperty(vName);
            vProperty.Should().NotBeNull($"MissRecord carries {vName}");
            vProperty!.PropertyType.Should().Be(typeof(string), $"{vName} is nullable reference text");
        }

        var vSchema = ReadSchema();
        foreach (var vColumn in new[] { "ItemId", "FoundPhaseGate", "SourceLineHash" })
        {
            vSchema.Should().MatchRegex(
                $"ALTER TABLE \"Miss\"\\s+ADD COLUMN IF NOT EXISTS \"{vColumn}\"",
                "an additive change to a shipped table has to migrate an existing database");
        }
    }

    /// <summary>
    /// <c>SourceLineHash</c> reaches all three miss tables, so the ingest half has somewhere to key on
    /// in each of them (REQ-FN-103).
    /// </summary>
    [Fact]
    public void SourceLineHashReachesAllThreeMissTables()
    {
        var vSchema = ReadSchema();

        foreach (var vTable in new[] { "Miss", "MissFix", "MissAmend" })
        {
            vSchema.Should().MatchRegex(
                $"ALTER TABLE \"{vTable}\"\\s+ADD COLUMN IF NOT EXISTS \"SourceLineHash\"");
        }

        typeof(MissRecord).GetProperty("SourceLineHash").Should().NotBeNull();
        typeof(MissFixRecord).GetProperty("SourceLineHash").Should().NotBeNull();
        typeof(MissAmendRecord).GetProperty("SourceLineHash").Should().NotBeNull();
    }

    /// <summary>
    /// The three §2.6 <c>"Run"</c> columns are added as guarded ALTERs and are nullable, so an existing
    /// database migrates and a pre-2026-08-31 row keeps saying nothing rather than saying zero.
    /// </summary>
    [Fact]
    public void TheRunColumnsAreAdditiveAndNullable()
    {
        var vSchema = ReadSchema();

        foreach (var vColumn in new[] { "SubagentRuns", "TokensOutSubagents", "ModelTokensOut" })
        {
            vSchema.Should().MatchRegex(
                $"ALTER TABLE \"Run\" ADD COLUMN IF NOT EXISTS \"{vColumn}\"\\s+\\w+(\\s|,)*NULL;",
                $"{vColumn} is nullable by design");
        }

        vSchema.Should().Contain("\"IxRunUserCmd\"", "/effort groups runs by cmd across repositories");
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
    /// <remarks>
    /// Terminated on the closing paren at the <b>start of a line</b> rather than on the first
    /// semicolon: the column comments in this DDL contain semicolons, and a semicolon-terminated
    /// pattern silently matches nothing rather than failing loudly.
    /// </remarks>
    private static string TableBody(string aSchema, string aTable)
    {
        var vMatch = Regex.Match(
            aSchema,
            $"CREATE TABLE IF NOT EXISTS \"{aTable}\"\\s*\\((?<body>.*?)\\r?\\n\\);",
            RegexOptions.Singleline);

        vMatch.Success.Should().BeTrue($"the DDL must declare the \"{aTable}\" table");
        return vMatch.Groups["body"].Value;
    }

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
