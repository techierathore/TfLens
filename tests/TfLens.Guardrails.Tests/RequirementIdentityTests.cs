using System.Text.RegularExpressions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-FN-110 / REQ-FN-111 (BRD-177, BRD-178) — a requirement is keyed by <c>(project, req_id)</c>
/// everywhere, and framework requirement verdicts never pool with application ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a guardrail rather than only unit tests.</b> The unit tests prove the figures are right today.
/// This one proves the <i>shape</i> that keeps them right: a single site that groups, joins or tests
/// membership on a bare <c>ReqId</c> silently fuses two projects' requirements into one, and the
/// resulting number looks entirely normal — the framework's own combined first-pass rate read 72% under
/// exactly that bug and 48% once keyed correctly. Nothing about the 72% announced itself.
/// </para>
/// <para>
/// <b>Precision over reach.</b> The pattern below is keyed to the identity constructs — a
/// <c>HashSet</c>, a <c>GroupBy</c>, a <c>Distinct</c>/<c>DistinctBy</c>, a <c>Contains</c> or a
/// dictionary key built directly off <c>.ReqId</c> — and not to reading the property, which is correct
/// and common wherever a REQ is being displayed rather than counted.
/// </para>
/// </remarks>
public sealed class RequirementIdentityTests
{
    /// <summary>
    /// Identity constructs built straight off a bare <c>.ReqId</c>: a set, a grouping, a distinct, a
    /// membership test or a dictionary key.
    /// </summary>
    private static readonly Regex BareReqIdIdentity = new(
        @"(HashSet<[^>]*>\s*\([^)]*\.ReqId|GroupBy\s*\([^)]*\.ReqId|Distinct(By)?\s*\([^)]*\.ReqId"
        + @"|Contains\s*\(\s*[A-Za-z]+\.ReqId\s*\)|\[\s*[A-Za-z]+\.ReqId\s*\])",
        RegexOptions.Compiled);

    /// <summary>
    /// No file under <c>src/</c> uses a bare <c>ReqId</c> as an identity — every such site goes through
    /// <see cref="ReqKey"/>, which has no constructor that takes an id on its own.
    /// </summary>
    [Fact]
    public void NoBareReqIdIsUsedAsAnIdentity()
    {
        var vOffenders = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src")
                     .Concat(RepoTree.Files("*.razor", "src")))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vCode = RepoTree.StripLiterals(vLines[vIndex]);

                if (BareReqIdIdentity.IsMatch(vCode))
                {
                    vOffenders.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1}  {vLines[vIndex].Trim()}");
                }
            }
        }

        Assert.True(
            vOffenders.Count == 0,
            "BRD-178 — a requirement is keyed by (project, req_id), never by req_id alone. Use "
            + "ReqKey.Of(record). Offending sites:" + Environment.NewLine
            + string.Join(Environment.NewLine, vOffenders));
    }

    /// <summary>
    /// <see cref="ReqKey"/> offers no way to build a key from an id alone, so the wrong key is
    /// unrepresentable rather than merely discouraged.
    /// </summary>
    [Fact]
    public void ReqKeyCannotBeBuiltFromAnIdAlone()
    {
        var vConstructors = typeof(ReqKey).GetConstructors();

        Assert.All(vConstructors, aConstructor => Assert.Equal(2, aConstructor.GetParameters().Length));
        Assert.Null(typeof(ReqKey).GetMethod("Of", [typeof(string)]));
    }

    /// <summary>
    /// Two projects sharing a <c>req_id</c> are two keys; the same project's repeated id is one.
    /// </summary>
    [Fact]
    public void TwoProjectsSharingAnIdAreTwoRequirements()
    {
        var vKeys = new HashSet<ReqKey>
        {
            new("TfLens", "REQ-UI-001"),
            new("TechieBlog", "REQ-UI-001"),
            new("TfLens", "REQ-UI-001")
        };

        Assert.Equal(2, vKeys.Count);
    }

    /// <summary>
    /// A <c>req_class: "FR"</c> verdict takes the framework-requirement segment whatever its
    /// <c>project_type</c> says, so no application segment can contain one (BRD-177).
    /// </summary>
    [Theory]
    [InlineData("app")]
    [InlineData("library")]
    [InlineData("docs")]
    [InlineData("framework")]
    public void FrameworkRequirementVerdictsNeverJoinAnApplicationSegment(string aProjectType)
    {
        var vRecord = new GateRecord
        {
            UserId = 1,
            Repo = "acme/alpha",
            SourceSha = "guardrail",
            Ts = "2026-09-08T00:00:00Z",
            App = "AlphaApp",
            ProjectType = aProjectType,
            ReqId = "BRD-177",
            ReqClass = MetricsConstants.FrameworkReqClass
        };

        Assert.Equal(MetricsConstants.FrameworkRequirements, Segment.KeyFor(vRecord));
    }
}
