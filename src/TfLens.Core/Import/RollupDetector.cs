using System.Text;
using System.Text.Json;

namespace TfLens.Core.Import;

/// <summary>
/// Refuses a precomputed rollup, a <c>tflens.json</c> or an exported snapshot (REQ-FN-086, BRD-140).
/// </summary>
/// <remarks>
/// <para>
/// TfLens computes every figure from raw records at request time (BRD-30). A rollup is the *answer*,
/// not the evidence; accepting one would put a plausible wrong number one upload away, which is the
/// single failure this product exists to prevent. The refusal is therefore <b>structural</b> — there
/// is no flag, no query parameter and no confirmation dialog that lets one through.
/// </para>
/// <para>
/// Detection has two halves, because either alone is evadable. By <b>name</b>: the two files the
/// export writes and the file the reference script's <c>--json</c> output is usually redirected to.
/// By <b>shape</b>: a payload that parses as one whole JSON document whose root object carries at
/// least two of the rollup's top-level keys. Raw telemetry never looks like that — it is JSON Lines,
/// one record per line, and a record carries <c>v</c> and <c>ts</c> and none of these keys — so a
/// <c>tflens.json</c> renamed to <c>runs.jsonl</c> is caught by the second half.
/// </para>
/// </remarks>
public static class RollupDetector
{
    /// <summary>File names that are a rollup or a snapshot whatever they contain.</summary>
    public static readonly IReadOnlyList<string> RefusedFileNames =
        ["tflens.json", "snapshot.md", "rollup.json"];

    /// <summary>
    /// The top-level keys <c>tf-metrics.sh --rollup --json</c> and <c>tflens.json</c> share.
    /// </summary>
    /// <remarks>
    /// Verified against live output on 2026-08-28: the reference's rollup and the export's snapshot
    /// both open with <c>per_repo</c>, <c>tainted_reqs</c>, <c>live</c>, <c>backfilled</c> and
    /// <c>pooled</c>; the snapshot adds <c>extras</c> and <c>parity</c>.
    /// </remarks>
    public static readonly IReadOnlyList<string> RollupKeys =
        ["per_repo", "tainted_reqs", "live", "backfilled", "pooled", "extras", "parity"];

    /// <summary>How many rollup keys a root object must carry before it is judged a rollup.</summary>
    private const int RollupKeyThreshold = 2;

    /// <summary>The message every rollup refusal carries. It names what to upload instead.</summary>
    public const string Message =
        "That file holds figures TfLens has already computed, not the records they were computed from. "
        + "TfLens recalculates every number from raw records at request time, so it cannot accept a "
        + "rollup, a tflens.json or an exported snapshot — importing conclusions instead of evidence is "
        + "exactly how a plausible wrong number gets in. Upload the telemetry directory instead: zip "
        + "docs/metrics/ for TechieFlow, or verification/telemetry/ for the Playbook. That directory "
        + "holds runs.jsonl, gates.jsonl, sessions.jsonl, commits.jsonl, misses.jsonl and "
        + "events.ndjson, which are what TfLens reads.";

    /// <summary>The refusal, ready to return.</summary>
    public static ImportRefusal Refusal { get; } =
        new(ImportRefusalReason.PrecomputedRollup, Message);

    /// <summary>
    /// Tests whether a file name is one of the export's or the reference's own output files.
    /// </summary>
    /// <param name="aEntryName">An upload or archive entry name.</param>
    /// <returns><c>true</c> when the name alone is enough to refuse it.</returns>
    public static bool IsRefusedName(string? aEntryName)
    {
        if (string.IsNullOrWhiteSpace(aEntryName))
        {
            return false;
        }

        var vFileName = ImportStreamCatalog.FileNameOf(aEntryName);

        return RefusedFileNames.Any(aN => string.Equals(aN, vFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tests whether a payload is a precomputed rollup by its shape.
    /// </summary>
    /// <param name="aBytes">The file's bytes.</param>
    /// <returns><c>true</c> when the payload is one JSON object carrying the rollup's keys.</returns>
    public static bool IsRollupPayload(byte[]? aBytes)
    {
        if (aBytes is null || aBytes.Length == 0)
        {
            return false;
        }

        // A rollup is pretty-printed and small enough to be read whole; a stream file is not JSON at
        // all as a whole document, so the parse below fails on it immediately and costs nothing.
        string vText;

        try
        {
            vText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
                .GetString(aBytes)
                .TrimStart('﻿')
                .Trim();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (vText.Length == 0 || vText[0] != '{')
        {
            return false;
        }

        try
        {
            using var vDocument = JsonDocument.Parse(
                vText, new JsonDocumentOptions { AllowTrailingCommas = true });

            if (vDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var vHits = RollupKeys.Count(aKey => vDocument.RootElement.TryGetProperty(aKey, out _));
            return vHits >= RollupKeyThreshold;
        }
        catch (JsonException)
        {
            // JSON Lines with more than one line is not a valid document, which is the common case.
            return false;
        }
    }

    /// <summary>
    /// Judges one file by both halves of the rule.
    /// </summary>
    /// <param name="aEntryName">The upload or archive entry name.</param>
    /// <param name="aBytes">The file's bytes, or <c>null</c> when only the name is known.</param>
    /// <returns><see cref="Refusal"/> when the file is a rollup, otherwise <c>null</c>.</returns>
    public static ImportRefusal? Detect(string? aEntryName, byte[]? aBytes) =>
        IsRefusedName(aEntryName) || IsRollupPayload(aBytes) ? Refusal : null;
}
