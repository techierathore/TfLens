using System.Reflection;
using FluentAssertions;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-FN-087 / BRD-136 / ADR-021 — origin is <b>displayed everywhere and pooled on nowhere</b>.
/// </summary>
/// <remarks>
/// <para>
/// A record's <c>backfilled</c>, <c>project_type</c>, <c>harness</c> and <c>origin_confidence</c> mean
/// exactly the same thing whether the line was fetched from GitHub or lifted out of an uploaded bundle,
/// so <c>source_kind</c> is a fact about <i>delivery</i> and not a fifth segmentation axis. TfLens
/// already has four axes that legitimately divide figures (live/backfilled, <c>project_type</c>,
/// framework, and the attribution confidence bound); a fifth added by accident would halve every
/// denominator on a mixed dataset and no reader would be able to tell.
/// </para>
/// <para>
/// The discipline is easy to state and easy to break by adding one parameter, so it is proven
/// structurally rather than by exercising today's code paths: the engine cannot take origin as an
/// argument, no result type carries a key that could be split by it, and the word does not appear in the
/// metrics code at all. The behavioural half — a mixed dataset producing exactly the figures a uniform
/// one produces — is in <c>TfLens.Core.Tests/Export/SourceKindExportTests.cs</c>.
/// </para>
/// </remarks>
public sealed class SourceKindIsNeverASegmentTests
{
    /// <summary>Spellings that would mean a figure had learned about origin.</summary>
    private static readonly string[] OriginWords = ["sourcekind", "bundlesha", "imported", "isimport"];

    /// <summary>
    /// Every result type the engine hands out, and every type reachable from one of them.
    /// </summary>
    /// <remarks>
    /// Listed rather than crawled: a crawl would silently stop covering a type the day somebody replaced
    /// a property with an interface, and this list failing to compile is the notification that a new
    /// result type needs a decision about origin.
    /// </remarks>
    private static readonly Type[] ResultTypes =
    [
        typeof(AnalysisResult), typeof(PerRepoFacts), typeof(SegmentFigures), typeof(PooledMetrics),
        typeof(GateCount), typeof(LateGateCoverage), typeof(FieldEligibility),
        typeof(MissAnalysis), typeof(MissSegmentFigures), typeof(MissCategoryCount),
        typeof(MissAttributionFigures), typeof(MissAttributionExclusion), typeof(MissPhaseRate),
        typeof(MissCost), typeof(MissMoney), typeof(MissHarnessCost)
    ];

    /// <summary>
    /// No method on the engine's own surface takes a source kind, under any spelling.
    /// </summary>
    /// <remarks>
    /// This is the acceptance clause verbatim. If origin can never be passed <i>in</i>, no figure can
    /// come out divided by it, whatever a future caller intends.
    /// </remarks>
    [Fact]
    public void NoEngineMethodTakesASourceKindParameter()
    {
        var vOffenders = new List<string>();

        foreach (var vType in EngineTypes())
        {
            foreach (var vMethod in vType.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var vParameter in vMethod.GetParameters())
                {
                    if (MentionsOrigin(vParameter.Name) || MentionsOrigin(vParameter.ParameterType.Name))
                    {
                        vOffenders.Add($"{vType.FullName}.{vMethod.Name}({vParameter.Name})");
                    }
                }
            }
        }

        vOffenders.Should().BeEmpty(
            "ADR-021 — origin is a property of delivery. An engine that can be handed it is an engine "
            + "that can divide a figure by it, and REQ-FN-087 forbids exactly that");
    }

    /// <summary>
    /// No key on any result type is split by origin — not a property, not a dictionary key.
    /// </summary>
    /// <remarks>
    /// The second acceptance clause. Two shapes would break it: a property named for origin, and a map
    /// keyed by it. The first is caught by name; the second by the fact that every keyed collection on a
    /// result type is keyed by <c>project_type</c> or a category label, and a new one keyed by origin
    /// would have to be declared here.
    /// </remarks>
    [Fact]
    public void NoResultTypeKeyIsSplitBySourceKind()
    {
        var vOffenders = new List<string>();

        foreach (var vType in ResultTypes)
        {
            foreach (var vProperty in vType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (MentionsOrigin(vProperty.Name) || MentionsOrigin(vProperty.PropertyType.Name))
                {
                    vOffenders.Add($"{vType.Name}.{vProperty.Name}");
                }
            }
        }

        vOffenders.Should().BeEmpty(
            "BRD-136 — a figure segmented by how its data arrived would halve every denominator on a "
            + "mixed dataset, and nothing in the output would say so");
    }

    /// <summary>
    /// The metrics code does not mention origin at all, so it cannot branch on it.
    /// </summary>
    /// <remarks>
    /// Reflection proves the shape of the surface; this proves the inside. <c>SourceKinds</c> lives in
    /// <c>Contracts</c> and is read by the pages, the repository list and the export — never by anything
    /// that computes a number.
    /// </remarks>
    [Fact]
    public void TheMetricsCodeNeverMentionsOrigin()
    {
        var vOffenders = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", Path.Combine("src", "TfLens.Core", "Metrics")))
        {
            var vLines = File.ReadAllLines(vPath);
            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vCode = RepoTree.StripLiterals(vLines[vIndex]);
                if (vCode.Contains("SourceKind", StringComparison.Ordinal)
                    || vCode.Contains("ImportedSourceRules", StringComparison.Ordinal))
                {
                    vOffenders.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1}");
                }
            }
        }

        vOffenders.Should().BeEmpty(
            "nothing on the figure path may read how a source's data arrived (ADR-021)");
    }

    /// <summary>
    /// Origin still reaches the reader: the export's per-repo block carries it and the badge names it.
    /// </summary>
    /// <remarks>
    /// The other half of "displayed everywhere, pooled nowhere". A guardrail that only forbade the
    /// segmentation would be satisfied by deleting the feature, which is the wrong answer: BRD-136 wants
    /// the fact visible on every surface precisely <i>because</i> it divides nothing.
    /// </remarks>
    [Fact]
    public void OriginIsStillCarriedIntoTheExport()
    {
        var vExport = Path.Combine(
            RepoTree.Root.FullName, "src", "TfLens.Core", "Export", "SnapshotJson.cs");

        File.ReadAllText(vExport).Should().Contain(
            "\"source_kind\"",
            "BRD-136 requires the export's per-repo block to name how each source's data arrived");

        SourceKinds.DisplayName(SourceKinds.Api).Should().Be(SourceKinds.ApiLabel);
        SourceKinds.DisplayName(SourceKinds.Import).Should().Be(SourceKinds.ImportLabel);
    }

    /// <summary>The types that compute figures: the metrics namespace plus the engine interface.</summary>
    /// <returns>The engine surface.</returns>
    private static IEnumerable<Type> EngineTypes() =>
        typeof(AnalysisResult).Assembly
            .GetTypes()
            .Where(aType => aType.Namespace is not null
                && aType.Namespace.StartsWith("TfLens.Core.Metrics", StringComparison.Ordinal))
            .Append(typeof(IMetricsEngine))
            .Append(typeof(IExtraMetrics));

    /// <summary>Whether an identifier names origin under any of its spellings.</summary>
    /// <param name="aName">The identifier.</param>
    /// <returns><c>true</c> when it does.</returns>
    private static bool MentionsOrigin(string? aName) =>
        aName is not null
        && OriginWords.Any(aWord => aName.Contains(aWord, StringComparison.OrdinalIgnoreCase));
}
