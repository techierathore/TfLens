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
/// banner compares that version against <see cref="ParserVersion.Current"/>: equal is
/// <see cref="ParityStatuses.Quotable"/>, different is <see cref="ParityStatuses.NotQuotable"/>, absent
/// is <see cref="ParityStatuses.NeverRun"/>. There is no code path that upgrades a status by any other
/// means — nothing in TfLens can declare its own numbers quotable.
/// </remarks>
public sealed record ParityRecord
{
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
    /// Decides whether the figures a given parser produced may be quoted.
    /// </summary>
    /// <remarks>
    /// A record that did not pass is treated exactly like no record at all for quotability: only an
    /// empty diff against the reference makes a figure quotable (BRD §13).
    /// </remarks>
    /// <param name="aRecord">The last parity record, or <c>null</c>.</param>
    /// <param name="aParserVersion">The parser version that produced the figures.</param>
    /// <returns>One of the <see cref="ParityStatuses"/> constants.</returns>
    public static string StatusFor(ParityRecord? aRecord, string aParserVersion)
    {
        if (aRecord is null || !aRecord.Passed)
        {
            return ParityStatuses.NeverRun;
        }

        return string.Equals(aRecord.ParserVersion, aParserVersion, StringComparison.Ordinal)
            ? ParityStatuses.Quotable
            : ParityStatuses.NotQuotable;
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
