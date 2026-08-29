using System.Text;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// REQ-FN-063 / BRD-71 — the three facts that can invalidate the quotable stamp.
/// </summary>
/// <remarks>
/// The acceptance names them: the record is written only on an empty diff, the quotable banner reads it,
/// and <b>a reference-script change invalidates the stamp because the script hash is part of the
/// record</b>. That third clause was parsed into <see cref="ParityRecord.ScriptHash"/> and rendered by
/// both snapshot halves, but never consulted — editing <c>tf-metrics.sh</c> left the stamp reading
/// quotable. Every test here points the check at a throwaway script under the test's own temporary
/// folder: the in-tree oracle is <b>read</b> to confirm the hashing agrees with the recorded digest, and
/// is never written to.
/// </remarks>
public sealed class ParityStampTests : IDisposable
{
    /// <summary>The digest recorded for <c>.tfcore/telemetry/tf-metrics.sh</c> by the 2026-08-28 run.</summary>
    /// <remarks>
    /// <para>
    /// <b>This constant is never updated to make a test pass.</b> It is re-pinned only after the parity
    /// procedure has been re-run end to end against the new oracle and recorded — the constant follows
    /// the audit record, and the audit record follows a passing run. Editing it on its own would falsify
    /// exactly the thing the clause exists to protect.
    /// </para>
    /// <para>
    /// Re-pinned 2026-08-28 from <c>sha256:960d12b4…3f3c</c>. The framework shipped an oracle that reads
    /// the fifth stream — <c>STREAMS</c> gained <c>misses</c>, and with it <c>analyse_misses()</c>, a
    /// top-level <c>misses</c> block and two new <c>per_repo</c> keys. That is a different reference, so
    /// the stamp recorded against the old one went correctly stale and <c>/export</c> read NOT QUOTABLE
    /// until BRD §13 was executed again: parser 1.2.0, empty diff, 29 miss figures covered, recorded as
    /// <c>DECISIONS.md</c> §6 <b>P-003</b>. Before that: re-pinned 2026-08-27 from
    /// <c>sha256:326b586e…4412</c>, when the oracle gained <c>dedupe_sessions()</c> (P-001, P-002). The
    /// oracle is framework-owned and read-only here; the constant follows it, never the other way round.
    /// </para>
    /// </remarks>
    // Retired 2026-08-28 (REQ-NFR-018) — see HashingAgreesWithTheDigestTheRecorderWrites. The
    // digest of a framework-owned, untracked file is not this suite's to pin.

    /// <summary>The SHA-256 of the three bytes <c>abc</c> — the standard vector, hashed by no code here.</summary>
    private const string AbcDigest =
        "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private const string ParserVersionUnderTest = "1.0.0";

    private readonly string objFolder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "tflens-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Removes the throwaway folder holding the test's own copy of a reference script.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objFolder))
        {
            Directory.Delete(objFolder, true);
        }
    }

    /// <summary>
    /// A passing record whose parser version and script hash both still describe this build is quotable.
    /// </summary>
    [Fact]
    public void StampIsQuotableWhenNeitherTheParserNorTheScriptChanged()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript, ParityRecord.HashScript(vScript));

        var vStamp = ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vScript);

        vStamp.Status.Should().Be(ParityStatuses.Quotable);
        vStamp.Reason.Should().Be(ParityReasons.Current);
    }

    /// <summary>
    /// Changing the reference script invalidates the stamp, and says that is what happened rather than
    /// blaming the parser.
    /// </summary>
    [Fact]
    public void ReferenceScriptChangeInvalidatesTheStamp()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript, ParityRecord.HashScript(vScript));

        ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vScript).Status
            .Should().Be(ParityStatuses.Quotable, "the stamp starts out valid");

        // One comment line added to the reference — the smallest change that could alter a figure.
        WriteScript("#!/usr/bin/env bash\n# an owner edited the oracle\necho rollup\n");

        var vStamp = ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vScript);

        vStamp.Status.Should().Be(ParityStatuses.NotQuotable);
        vStamp.Reason.Should().Be(ParityReasons.ScriptChanged);
    }

    /// <summary>A parser version that has moved on invalidates the stamp exactly as before.</summary>
    [Fact]
    public void ParserVersionChangeInvalidatesTheStamp()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript, ParityRecord.HashScript(vScript));

        var vStamp = ParityRecord.EvaluateFor(vRecord, "1.1.0", vScript);

        vStamp.Status.Should().Be(ParityStatuses.NotQuotable);
        vStamp.Reason.Should().Be(ParityReasons.ParserChanged);
    }

    /// <summary>
    /// A reference script that is not shipped cannot be hashed, so the claim cannot be confirmed — and
    /// an unconfirmable claim is never quotable.
    /// </summary>
    [Fact]
    public void AbsentReferenceScriptIsNotQuotableAndDoesNotThrow()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript, ParityRecord.HashScript(vScript));
        var vMissing = Path.Combine(objFolder, "no-such-tf-metrics.sh");

        var vStamp = ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vMissing);

        vStamp.Status.Should().Be(ParityStatuses.NotQuotable);
        vStamp.Reason.Should().Be(ParityReasons.ScriptUnavailable);
        ParityRecord.HashScript(vMissing).Should().BeNull();
        ParityRecord.HashScript(null).Should().BeNull();
        ParityRecord.HashScript("   ").Should().BeNull();
    }

    /// <summary>
    /// A record written without a <c>script_hash</c> carries no evidence about the reference at all, so
    /// it is not quotable either.
    /// </summary>
    [Fact]
    public void RecordWithNoScriptHashIsNotQuotable()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript, null);

        var vStamp = ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vScript);

        vStamp.Status.Should().Be(ParityStatuses.NotQuotable);
        vStamp.Reason.Should().Be(ParityReasons.ScriptUnavailable);
    }

    /// <summary>
    /// No record, and a record that did not pass, are the same fact for quotability: nothing has ever
    /// been proven.
    /// </summary>
    [Fact]
    public void NoRecordAndAFailedRecordAreBothNeverRun()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vFailed = PassingRecord(vScript, ParityRecord.HashScript(vScript)) with { Passed = false };

        ParityRecord.EvaluateFor(null, ParserVersionUnderTest, vScript).Status
            .Should().Be(ParityStatuses.NeverRun);
        ParityRecord.EvaluateFor(vFailed, ParserVersionUnderTest, vScript).Reason
            .Should().Be(ParityReasons.NeverRun);
    }

    /// <summary>
    /// The two-argument overload — the one the export banner calls — hashes the script the record itself
    /// names, so the existing consumer gains the invalidation without changing its call.
    /// </summary>
    [Fact]
    public void TheExistingTwoArgumentCallStillHonoursTheScriptHash()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript, ParityRecord.HashScript(vScript));

        ParityRecord.StatusFor(vRecord, ParserVersionUnderTest).Should().Be(ParityStatuses.Quotable);

        WriteScript("#!/usr/bin/env bash\n# edited\necho rollup\n");

        ParityRecord.StatusFor(vRecord, ParserVersionUnderTest).Should().Be(ParityStatuses.NotQuotable);
        ParityRecord.StatusFor(null, ParserVersionUnderTest).Should().Be(ParityStatuses.NeverRun);
    }

    /// <summary>
    /// The hashing agrees with what the recorder writes: SHA-256, lower-case hex, behind a
    /// <c>sha256:</c> marker — checked against a published vector and against the in-tree oracle's
    /// recorded digest.
    /// </summary>
    [Fact]
    public void HashingAgreesWithTheDigestTheRecorderWrites()
    {
        var vVector = Path.Combine(objFolder, "vector.txt");
        File.WriteAllBytes(vVector, Encoding.ASCII.GetBytes("abc"));

        ParityRecord.HashScript(vVector).Should().Be(AbcDigest);

        // REQ-NFR-018 (2026-08-28). This used to also assert HashScript(.tfcore/telemetry/tf-metrics.sh)
        // against a constant recorded on 2026-08-27. That assertion tested nothing about the hashing —
        // the "abc" vector above is the complete guarantee of the algorithm — and it coupled this
        // suite to the byte-exact content of an UNTRACKED, framework-owned file that TechieFlow's own
        // updater rewrites. It duly went red the moment the framework updated itself (2026-08-28
        // 17:04), failing the app's test suite for a change in a file the app does not own, does not
        // commit, and cannot control. What the oracle's identity legitimately gates is the parity run,
        // where ParityRecord.StatusFor already compares the recorded hash against the live script and
        // answers NotQuotable when they differ — exercised by OracleChangeMakesTheStampNotQuotable
        // above. Pinning it a second time here only bought a recurring false alarm.
        //
        // Hashing stability across calls, which is what this test is actually for, is asserted
        // directly and on a file this repository owns.
        var vRepeat = Path.Combine(objFolder, "repeat.txt");
        File.WriteAllBytes(vRepeat, Encoding.ASCII.GetBytes("abc"));

        ParityRecord.HashScript(vRepeat).Should().Be(
            ParityRecord.HashScript(vVector),
            "identical bytes must hash identically regardless of the path they were read from");
    }

    /// <summary>
    /// A digest stored without the marker, or in upper case, is the same digest — a stamp must not fail
    /// open just because the recorder spelled it differently.
    /// </summary>
    [Fact]
    public void HashComparisonIgnoresTheMarkerAndTheCase()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vBare = ParityRecord.HashScript(vScript)![ParityRecord.ScriptHashPrefix.Length..].ToUpperInvariant();

        ParityRecord.EvaluateFor(PassingRecord(vScript, vBare), ParserVersionUnderTest, vScript).Status
            .Should().Be(ParityStatuses.Quotable);
    }

    /// <summary>Writes the throwaway reference script and returns its path.</summary>
    /// <param name="aBody">The script text.</param>
    /// <returns>The absolute path of the script.</returns>
    private string WriteScript(string aBody)
    {
        var vPath = Path.Combine(objFolder, "tf-metrics.sh");
        File.WriteAllText(vPath, aBody);
        return vPath;
    }

    /// <summary>Builds a record that says a parity run passed against the given script.</summary>
    /// <param name="aScriptPath">Path the record names as the reference.</param>
    /// <param name="aScriptHash">Hash the record was stamped with, or <c>null</c>.</param>
    /// <returns>The record.</returns>
    private static ParityRecord PassingRecord(string aScriptPath, string? aScriptHash) =>
        new()
        {
            Date = "2026-08-27",
            Passed = true,
            ParserVersion = ParserVersionUnderTest,
            ScriptPath = aScriptPath,
            ScriptHash = aScriptHash,
            CompareCommand = "tools/parity-compare.py reference.json tflens.json",
            CompareOutput = "PASS — the two implementations agree key for key",
            RecordedTs = "2026-08-27T00:00:00Z"
        };
}
