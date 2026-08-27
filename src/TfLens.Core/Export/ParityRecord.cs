using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using TfLens.Core.Contracts;

namespace TfLens.Core.Export;

/// <summary>
/// The record of the last parity run — the stamp that decides whether a figure may be quoted.
/// </summary>
/// <remarks>
/// REQ-FN-063 / BRD-71. <c>data/parity-last.json</c> is written by <c>tools/parity-compare.py --record</c>
/// and <b>only on an empty diff</b>, so its existence is itself the claim that the two implementations
/// agreed. It carries the four things that can invalidate that claim: the date, the dataset SHAs the
/// comparison ran against, the hash of <c>tf-metrics.sh</c> (a reference change invalidates the stamp
/// just as surely as a TfLens change), and the TfLens parser version the run validated. The export
/// banner compares that version against <see cref="ParserVersion.Current"/> <b>and</b> the recorded
/// <see cref="ScriptHash"/> against the SHA-256 of the reference script named by
/// <see cref="TfLensOptions.ReferenceScriptPath"/>: both current is
/// <see cref="ParityStatuses.Quotable"/>, either moved on is <see cref="ParityStatuses.NotQuotable"/>,
/// no passing record at all is <see cref="ParityStatuses.NeverRun"/>. There is no code path that
/// upgrades a status by any other means — nothing in TfLens can declare its own numbers quotable, and a
/// reference script that cannot be hashed degrades to not-quotable rather than being assumed unchanged.
/// </remarks>
public sealed record ParityRecord
{
    /// <summary>The algorithm marker <c>tools/parity-compare.py</c> writes in front of the digest.</summary>
    public const string ScriptHashPrefix = "sha256:";

    /// <summary>ISO-8601 date the parity run was performed.</summary>
    public string? Date { get; init; }

    /// <summary>Whether the compare found an empty diff; the file is only written when it did.</summary>
    public bool Passed { get; init; }

    /// <summary>The TfLens parser version the run validated.</summary>
    public string? ParserVersion { get; init; }

    /// <summary>Hash of the reference script, so reference drift invalidates the stamp.</summary>
    public string? ScriptHash { get; init; }

    /// <summary>Path of the reference script the hash was taken over.</summary>
    public string? ScriptPath { get; init; }

    /// <summary>The dataset the comparison pinned — repository to commit SHA.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> DatasetShas { get; init; } = [];

    /// <summary>The exact compare invocation, so the run can be reproduced.</summary>
    public string? CompareCommand { get; init; }

    /// <summary>What the compare printed — the evidence, not a summary of it.</summary>
    public string? CompareOutput { get; init; }

    /// <summary>ISO-8601 timestamp the record was written.</summary>
    public string? RecordedTs { get; init; }

    /// <summary>
    /// Reads the parity record from disk.
    /// </summary>
    /// <param name="aPath">Path of <c>parity-last.json</c>, from <see cref="TfLensOptions.ParityLastPath"/>.</param>
    /// <returns>The record, or <c>null</c> when no parity run has ever been recorded or the file is unreadable.</returns>
    public static ParityRecord? Read(string aPath)
    {
        if (!File.Exists(aPath))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(aPath)) is not JsonObject vRoot)
            {
                return null;
            }

            return new ParityRecord
            {
                Date = ReadText(vRoot, "date"),
                Passed = vRoot["passed"]?.GetValue<bool>() ?? false,
                ParserVersion = ReadText(vRoot, "parser_version"),
                ScriptHash = ReadText(vRoot, "script_hash"),
                ScriptPath = ReadText(vRoot, "script_path"),
                DatasetShas = ReadShas(vRoot["dataset_shas"] as JsonObject),
                CompareCommand = ReadText(vRoot, "compare_command"),
                CompareOutput = ReadText(vRoot, "compare_output"),
                RecordedTs = ReadText(vRoot, "recorded_ts")
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decides whether the figures a given parser produced may be quoted, and says why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REQ-FN-063 names three things that can invalidate a stamp, and all three are checked here. A
    /// record that did not pass is treated exactly like no record at all: only an empty diff against the
    /// reference makes a figure quotable (BRD §13). A parser version that has moved on since the run
    /// invalidates it. And <b>a reference-script change invalidates it too, because the script hash is
    /// part of the record</b> — the comparison is only evidence of agreement between the two
    /// implementations that were actually compared, so if either side has changed the evidence is stale.
    /// </para>
    /// <para>
    /// The script is hashed on every call rather than cached: the file is a few kilobytes, the check
    /// runs once per page load or export, and a cached hash would let an edit made while the process is
    /// running go unnoticed — which is the exact failure this clause exists to close.
    /// </para>
    /// <para>
    /// <b>Absent is never quotable.</b> Many deployments will not ship <c>tf-metrics.sh</c>, and a
    /// record that carries no <c>script_hash</c> is equally unverifiable. Either way the hash cannot be
    /// confirmed, so the stamp reads <see cref="ParityStatuses.NotQuotable"/> with reason
    /// <see cref="ParityReasons.ScriptUnavailable"/> — distinguishable from a real drift, and never
    /// silently upgraded to quotable. Nothing here throws: an unreadable file is a reason, not a crash.
    /// </para>
    /// </remarks>
    /// <param name="aRecord">The last parity record, or <c>null</c>.</param>
    /// <param name="aParserVersion">The parser version that produced the figures.</param>
    /// <param name="aReferenceScriptPath">
    /// Path of the reference script, from <see cref="TfLensOptions.ReferenceScriptPath"/>; when blank
    /// the record's own <see cref="ScriptPath"/> is used, because that is the file it was hashed over.
    /// </param>
    /// <returns>The status and the reason behind it.</returns>
    public static ParityStamp EvaluateFor(
        ParityRecord? aRecord, string aParserVersion, string? aReferenceScriptPath)
    {
        if (aRecord is null || !aRecord.Passed)
        {
            return new ParityStamp(ParityStatuses.NeverRun, ParityReasons.NeverRun);
        }

        if (!string.Equals(aRecord.ParserVersion, aParserVersion, StringComparison.Ordinal))
        {
            return new ParityStamp(ParityStatuses.NotQuotable, ParityReasons.ParserChanged);
        }

        var vRecorded = NormaliseHash(aRecord.ScriptHash);
        var vPath = string.IsNullOrWhiteSpace(aReferenceScriptPath) ? aRecord.ScriptPath : aReferenceScriptPath;
        var vCurrent = NormaliseHash(HashScript(vPath));

        if (vRecorded is null || vCurrent is null)
        {
            return new ParityStamp(ParityStatuses.NotQuotable, ParityReasons.ScriptUnavailable);
        }

        return string.Equals(vRecorded, vCurrent, StringComparison.Ordinal)
            ? new ParityStamp(ParityStatuses.Quotable, ParityReasons.Current)
            : new ParityStamp(ParityStatuses.NotQuotable, ParityReasons.ScriptChanged);
    }

    /// <summary>
    /// Decides whether the figures a given parser produced may be quoted, against a configured script.
    /// </summary>
    /// <param name="aRecord">The last parity record, or <c>null</c>.</param>
    /// <param name="aParserVersion">The parser version that produced the figures.</param>
    /// <param name="aReferenceScriptPath">Path of the reference script.</param>
    /// <returns>One of the <see cref="ParityStatuses"/> constants.</returns>
    public static string StatusFor(ParityRecord? aRecord, string aParserVersion, string? aReferenceScriptPath) =>
        EvaluateFor(aRecord, aParserVersion, aReferenceScriptPath).Status;

    /// <summary>
    /// Decides whether the figures a given parser produced may be quoted, against the script the record
    /// itself names.
    /// </summary>
    /// <remarks>
    /// The record carries <see cref="ScriptPath"/> beside <see cref="ScriptHash"/>, so a caller with no
    /// configuration to hand can still perform the full REQ-FN-063 check: the stamp says which file it
    /// was taken over, and that file is re-hashed. This overload exists so every existing consumer keeps
    /// the script-hash guarantee without changing its call.
    /// </remarks>
    /// <param name="aRecord">The last parity record, or <c>null</c>.</param>
    /// <param name="aParserVersion">The parser version that produced the figures.</param>
    /// <returns>One of the <see cref="ParityStatuses"/> constants.</returns>
    public static string StatusFor(ParityRecord? aRecord, string aParserVersion) =>
        EvaluateFor(aRecord, aParserVersion, aRecord?.ScriptPath).Status;

    /// <summary>
    /// Hashes the reference script exactly as <c>tools/parity-compare.py --record</c> does.
    /// </summary>
    /// <remarks>
    /// SHA-256 over the file's bytes, rendered lower-case hex behind the <see cref="ScriptHashPrefix"/>
    /// marker, so the value this returns is byte-for-byte the value the record stores. A missing or
    /// unreadable file yields <c>null</c> — the caller turns that into a not-quotable stamp rather than
    /// an exception, because a deployment without the oracle must still render its pages.
    /// </remarks>
    /// <param name="aPath">Path of the script, or <c>null</c>.</param>
    /// <returns>The prefixed digest, or <c>null</c> when the file cannot be read.</returns>
    public static string? HashScript(string? aPath)
    {
        if (string.IsNullOrWhiteSpace(aPath) || !File.Exists(aPath))
        {
            return null;
        }

        try
        {
            using var vStream = File.OpenRead(aPath);
            return ScriptHashPrefix + Convert.ToHexStringLower(SHA256.HashData(vStream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reduces a stored or computed hash to the bare digest so the two can be compared safely.
    /// </summary>
    /// <remarks>
    /// The <c>sha256:</c> marker is part of the stored value but is not part of the evidence, and an
    /// upper-case digest is the same digest. Comparing the normalised forms means a record written by a
    /// slightly different hand still invalidates correctly instead of failing open.
    /// </remarks>
    /// <param name="aHash">The hash as stored or computed, or <c>null</c>.</param>
    /// <returns>The lower-case digest with no marker, or <c>null</c> when there is nothing to compare.</returns>
    private static string? NormaliseHash(string? aHash)
    {
        if (string.IsNullOrWhiteSpace(aHash))
        {
            return null;
        }

        var vValue = aHash.Trim();
        if (vValue.StartsWith(ScriptHashPrefix, StringComparison.OrdinalIgnoreCase))
        {
            vValue = vValue[ScriptHashPrefix.Length..];
        }

        return vValue.Length == 0 ? null : vValue.ToLowerInvariant();
    }

    /// <summary>Reads one optional string property.</summary>
    /// <param name="aRoot">The record object.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The value, or <c>null</c> when absent or not a string.</returns>
    private static string? ReadText(JsonObject aRoot, string aName) =>
        aRoot.TryGetPropertyValue(aName, out var vNode) && vNode is JsonValue vValue
            ? vValue.ToString()
            : null;

    /// <summary>Reads the dataset SHA map in a stable order.</summary>
    /// <param name="aNode">The <c>dataset_shas</c> object, or <c>null</c>.</param>
    /// <returns>Repository to SHA pairs, ordinal by repository.</returns>
    private static IReadOnlyList<KeyValuePair<string, string>> ReadShas(JsonObject? aNode) =>
        aNode is null
            ? []
            : aNode
                .Where(aP => aP.Value is not null)
                .Select(aP => new KeyValuePair<string, string>(aP.Key, aP.Value!.ToString()))
                .OrderBy(aP => aP.Key, StringComparer.Ordinal)
                .ToList();
}
