using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Provenance;

namespace TfLens.Core.Tests.Provenance;

/// <summary>
/// REQ-NFR-019 clause 1 / BRD-143 — no path produces a stream row with provenance nobody obtained.
/// </summary>
/// <remarks>
/// <para>
/// Every producer of stream rows — the sync, the import, the rebuild replay and the Playbook adapter —
/// reaches the store through <c>IStreamParser.Parse</c>, so the rule is asserted where they all pass.
/// It is a refusal rather than a warning because ADR-007 puts integrity rules in the shape of the
/// result: a parse that cannot say where its bytes came from has no safe partial answer to return, and
/// there is no setting that downgrades it (BRD-89 / REQ-NFR-009).
/// </para>
/// <para>
/// The rule is about <b>presence</b>, not shape, and one test says so out loud. A shape test would have
/// had to admit a 7-character abbreviation, a 40-character commit SHA and a 64-character bundle hash,
/// and would still have admitted <c>a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b</c> — which is
/// well-formed hex and was invented. What separates a real identity from a fabricated one is whether an
/// ingest path recorded obtaining it, which is <c>ProvenanceAudit</c>'s job.
/// </para>
/// </remarks>
public sealed class ProvenanceWritePathTests
{
    private const int UserId = 90007;
    private const string Repo = "acme/alpha";

    private const string GateLine =
        """{"v":1,"ts":"2026-08-01T10:00:00Z","app":"AlphaApp","req_id":"REQ-FN-001","verdict":"Verified"}""";

    private readonly StreamParser objParser = new();

    /// <summary>A parse with a blank source SHA is refused rather than storing an empty provenance.</summary>
    /// <param name="aSourceSha">The blank forms a caller could pass.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ParseRefusesABlankSourceSha(string aSourceSha)
    {
        var vAct = () => objParser.Parse(UserId, Repo, aSourceSha, StreamKind.Gates, GateLine);

        vAct.Should().Throw<ProvenanceException>()
            .WithMessage("*REQ-NFR-019*")
            .WithMessage($"*{Repo}*");
    }

    /// <summary>A parse that names the SHA it was fetched at produces its rows exactly as before.</summary>
    [Fact]
    public void ParseWithProvenanceStillProducesRows()
    {
        var vResult = objParser.Parse(
            UserId, Repo, "696b5eb2e5df9ee11ac02dbead27f72b8fd33e3c", StreamKind.Gates, GateLine);

        vResult.Gates.Should().ContainSingle()
            .Which.SourceSha.Should().Be("696b5eb2e5df9ee11ac02dbead27f72b8fd33e3c");
    }

    /// <summary>
    /// The rule admits every real identity shape the product uses — an abbreviated fixture SHA, a commit
    /// SHA and a bundle sha256 — because it tests presence and nothing else.
    /// </summary>
    /// <param name="aSourceSha">A real dataset identity of each length the product writes.</param>
    [Theory]
    [InlineData("f1e2d3c")]
    [InlineData("696b5eb2e5df9ee11ac02dbead27f72b8fd33e3c")]
    [InlineData("1b4f0e9851971998e732078544c96b36c3d01cedf7caa332359d6f1d83567014")]
    public void ParseAcceptsEveryRealIdentityShape(string aSourceSha)
    {
        objParser.Invoking(aP => aP.Parse(UserId, Repo, aSourceSha, StreamKind.Gates, GateLine))
            .Should().NotThrow();
    }

    /// <summary>
    /// Well-formed hex is not evidence: the two SHAs the 2026-08-29 re-run found still parse, and are
    /// caught by the audit rather than by a shape test that would give false confidence.
    /// </summary>
    [Fact]
    public void AWellFormedButInventedShaIsNotStoppedHereByDesign()
    {
        objParser.Invoking(aP => aP.Parse(
                UserId, Repo, "a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b", StreamKind.Gates, GateLine))
            .Should().NotThrow("shape says nothing; ProvenanceAudit answers the real question");

        ProvenanceAudit
            .Compare(
                [new StoredProvenance(UserId, Repo, "a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b", "Gate", 1)],
                [])
            .HasOrphans.Should().BeTrue();
    }

    /// <summary>The ledger itself cannot be written without a SHA, so it can never launder a blank row.</summary>
    [Fact]
    public void RequireObtainedGuardsTheLedgerToo()
    {
        var vAct = () => ProvenanceRules.RequireObtained(UserId, Repo, null);

        vAct.Should().Throw<ProvenanceException>();

        var vObtained = () => ProvenanceRules.RequireObtained(UserId, Repo, "abc");
        vObtained.Should().NotThrow();
    }
}
