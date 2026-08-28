using System.Reflection;
using System.Text.RegularExpressions;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// The structural half of the miss invariants (REQ-NFR-013, BRD-130).
/// </summary>
/// <remarks>
/// The behavioural clauses are pinned by <c>MissInvariantTests</c> in the engine project, where a figure
/// can actually be computed. These are the ones that can only be proved as a negative over the working
/// tree — clause 7, "TfLens writes to no repository and emits into no stream, including
/// <c>misses.jsonl</c>" — plus the result-type shapes the other clauses rest on, asserted here as well so
/// a refactor that moves a property cannot quietly pass by moving it out of the engine project's sight.
/// </remarks>
public sealed class MissInvariantContractTests
{
    /// <summary>The three record kinds a producer writes and TfLens only ever reads.</summary>
    private static readonly Type[] MissRecordTypes =
        [typeof(MissRecord), typeof(MissFixRecord), typeof(MissAmendRecord)];

    /// <summary>
    /// Clause 7 — nothing in the app can run the producer's emitter, so it cannot append to any stream.
    /// </summary>
    /// <remarks>
    /// <c>tf-emit.sh</c> and <c>*log-miss</c> are shell commands. A product that never starts a process
    /// cannot invoke one, which makes "TfLens emits into no stream" a property of the tree rather than a
    /// promise about intent.
    /// </remarks>
    [Fact]
    public void TheAppStartsNoProcessAndSoCanRunNoEmitter()
    {
        var vProcessStart = new Regex(@"\bProcess\s*\.\s*Start\b|\bnew\s+ProcessStartInfo\b", RegexOptions.Compiled);
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            var vLines = File.ReadAllLines(vPath);
            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                if (IsComment(vLines[vIndex]))
                {
                    continue;
                }

                if (vProcessStart.IsMatch(RepoTree.StripLiterals(vLines[vIndex])))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLines[vIndex].Trim()}");
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("a process launch", vFindings));
    }

    /// <summary>
    /// Clause 7 — the miss stream has three reads and no write, so no code path can append a record.
    /// </summary>
    /// <remarks>
    /// The store persists parsed rows into PostgreSQL through <c>UpsertAsync</c>; there is deliberately no
    /// <c>WriteMissAsync</c>, no <c>EmitAsync</c> and no member anywhere that takes one of the three miss
    /// record types as an input to be published. A miss reaches TfLens by being fetched or imported, and
    /// by no other door.
    /// </remarks>
    [Fact]
    public void NoMemberAnywhereEmitsAMissRecord()
    {
        var vEmitting = new[] { "Emit", "Publish", "Append", "Push", "Post", "Send" };

        var vOffenders = typeof(ITelemetryStore).Assembly
            .GetTypes()
            .Where(aType => aType.IsPublic)
            .SelectMany(aType => aType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(aMethod => vEmitting.Any(aWord => aMethod.Name.StartsWith(aWord, StringComparison.Ordinal)))
                .Where(aMethod => aMethod.GetParameters()
                    .Any(aParameter => MissRecordTypes.Contains(Unwrap(aParameter.ParameterType))))
                .Select(aMethod => aType.Name + "." + aMethod.Name))
            .ToList();

        Assert.True(vOffenders.Count == 0, Report("a member that emits a miss record", vOffenders));
    }

    /// <summary>
    /// Clause 7 — the only file TfLens writes a stream's bytes into is its own raw archive.
    /// </summary>
    /// <remarks>
    /// Two writers exist: the sync runner archiving what GitHub answered, and the import service
    /// archiving what was uploaded. Both build the path from the user's raw root, and the import path is
    /// additionally confined by <c>UploadBounds</c> (REQ-NFR-014). Neither writes anywhere a repository
    /// working copy could be, so no stream file in any repository can be touched.
    /// </remarks>
    [Fact]
    public void StreamBytesAreOnlyEverWrittenIntoTheRawArchive()
    {
        var vWrite = new Regex(
            @"File\s*\.\s*(?:WriteAll|AppendAll)\w*\(|new\s+StreamWriter\s*\(",
            RegexOptions.Compiled);

        var vWriters = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src"))
        {
            var vLines = File.ReadAllLines(vPath);
            if (vLines.Any(aLine => !IsComment(aLine) && vWrite.IsMatch(aLine)))
            {
                vWriters.Add(RepoTree.Relative(vPath));
            }
        }

        // The full, deliberately short list. Adding a writer here is a decision someone has to make on
        // purpose, which is the point: a sixth file appearing in this list is the review moment.
        Assert.Equal(
            [
                "src/TfLens.Core/Export/SnapshotExporter.cs",
                "src/TfLens.Core/Import/TelemetryImportService.cs",
                "src/TfLens.Core/Metrics/RateCard.cs",
                "src/TfLens.Core/Playbook/PlaybookAdapter.cs",
                "src/TfLens/Services/Sync/RepoSyncRunner.cs"
            ],
            vWriters.OrderBy(aPath => aPath, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Clause 1 — the cost result type has no property a blended figure could live in.
    /// </summary>
    [Fact]
    public void MissCostCarriesTheSplitAndNothingElse()
    {
        var vNames = typeof(MissCost)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(aProperty => aProperty.Name != "EqualityContract")
            .Select(aProperty => aProperty.Name)
            .OrderBy(aName => aName, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Apportioned", "NoneCount", "Sole"], vNames);
        Assert.Equal(typeof(Figure), typeof(MissCost).GetProperty(nameof(MissCost.Sole))!.PropertyType);
        Assert.Equal(typeof(Figure), typeof(MissCost).GetProperty(nameof(MissCost.Apportioned))!.PropertyType);
        Assert.Equal(typeof(int), typeof(MissCost).GetProperty(nameof(MissCost.NoneCount))!.PropertyType);
    }

    /// <summary>
    /// Clause 2 — the exclusion is engine output, so a page cannot render the figures without it.
    /// </summary>
    [Fact]
    public void TheAttributionExclusionIsCarriedOnTheResultType()
    {
        foreach (var vName in new[] { "AttributedN", "AttributionExcluded", "ExclusionReason", "ExcludedByConfidence" })
        {
            Assert.NotNull(typeof(MissAttributionFigures).GetProperty(vName));
        }

        Assert.False(string.IsNullOrWhiteSpace(MissAttributionTaint.ExclusionReason));
        Assert.Equal("linked", MissAttributionTaint.Linked);
    }

    /// <summary>
    /// Clause 3 — the failed-practice denominator is a property in its own right, not a derived guess.
    /// </summary>
    [Fact]
    public void TheFailedPracticeDenominatorIsOnTheResultType()
    {
        Assert.NotNull(typeof(MissSegmentFigures).GetProperty("WhyMissedN"));
        Assert.Equal(
            typeof(FieldEligibility),
            typeof(MissSegmentFigures).GetProperty("WhyMissedEligibility")!.PropertyType);

        // FieldEligibility deliberately carries no rate — dividing assessed by the record total is the
        // wrong number this whole rule exists to refuse (REQ-FN-076).
        Assert.DoesNotContain(
            typeof(FieldEligibility).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            aProperty => aProperty.PropertyType == typeof(Figure));
    }

    /// <summary>
    /// Clause 4 — open and declined are two properties, so nothing can fold one into the other.
    /// </summary>
    [Fact]
    public void OpenAndDeclinedAreTwoSeparateFigures()
    {
        foreach (var vType in new[] { typeof(MissAnalysis), typeof(MissSegmentFigures) })
        {
            Assert.NotNull(vType.GetProperty("OpenMisses"));
            Assert.NotNull(vType.GetProperty("WontFix"));
        }
    }

    /// <summary>
    /// Clause 5 — measured dollars and the estimate label are different properties on the harness row.
    /// </summary>
    [Fact]
    public void MeasuredDollarsAndTheEstimateLabelNeverShareAProperty()
    {
        Assert.Equal(
            typeof(decimal?),
            typeof(MissHarnessCost).GetProperty(nameof(MissHarnessCost.MeasuredUsdTotal))!.PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(MissHarnessCost).GetProperty(nameof(MissHarnessCost.EstimateLabel))!.PropertyType);
        Assert.Contains("not measured spend", RateCard.EstimateLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Clause 6 — the miss escape share and the gates escape rate live on different result types.
    /// </summary>
    [Fact]
    public void TheMissEscapeShareIsNotTheGatesEscapeRate()
    {
        Assert.NotNull(typeof(SegmentFigures).GetProperty("EscapeRate"));
        Assert.Null(typeof(SegmentFigures).GetProperty("EscapeShare"));
        Assert.NotNull(typeof(MissSegmentFigures).GetProperty("EscapeShare"));
        Assert.Null(typeof(MissSegmentFigures).GetProperty("EscapeRate"));

        // EscapeRate.Compute takes two REQ counts and nothing else; there is no overload a miss record
        // could be handed to.
        var vOverloads = typeof(EscapeRate).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Single(vOverloads);
        Assert.Equal([typeof(int), typeof(int)], vOverloads[0].GetParameters().Select(aParameter => aParameter.ParameterType));
    }

    /// <summary>Unwraps a collection parameter to the element type it carries.</summary>
    /// <param name="aType">The parameter's declared type.</param>
    /// <returns>The element type for a generic collection, else the type itself.</returns>
    private static Type Unwrap(Type aType) =>
        aType.IsGenericType && aType.GetGenericArguments().Length == 1
            ? aType.GetGenericArguments()[0]
            : aType;

    /// <summary>Tells whether a source line is a comment and so cannot violate anything.</summary>
    /// <param name="aLine">One source line.</param>
    /// <returns><c>true</c> when the line is a comment.</returns>
    private static bool IsComment(string aLine)
    {
        var vTrimmed = aLine.TrimStart();
        return vTrimmed.StartsWith("//", StringComparison.Ordinal)
            || vTrimmed.StartsWith("*", StringComparison.Ordinal);
    }

    /// <summary>Renders a finding list into a failure message a reader can act on.</summary>
    /// <param name="aWhat">What was found.</param>
    /// <param name="aFindings">Where it was found.</param>
    /// <returns>The message.</returns>
    private static string Report(string aWhat, IReadOnlyList<string> aFindings) =>
        $"REQ-NFR-013: found {aFindings.Count} instance(s) of {aWhat}:{Environment.NewLine}"
        + string.Join(Environment.NewLine, aFindings);
}
