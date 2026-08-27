using System.Text.Json;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Reads a gate record's <c>gates_run</c> array — the honest denominator for a late-added gate.
/// </summary>
/// <remarks>
/// The column stores the array verbatim as JSON text (SCHEMA.md §3.5). A missing, empty or malformed
/// value reads as "no gates ran", which is the conservative answer: it can only ever shrink a late
/// gate's denominator, never inflate it.
/// </remarks>
public static class GatesRun
{
    private static readonly string[] EmptyGates = [];

    /// <summary>
    /// Tests whether a record recorded that it ran a given gate.
    /// </summary>
    /// <param name="aRecord">The gate record.</param>
    /// <param name="aGate">The gate name, e.g. <c>perf</c>.</param>
    /// <returns><c>true</c> when the record's <c>gates_run</c> array contains the gate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRecord"/> is <c>null</c>.</exception>
    public static bool Contains(GateRecord aRecord, string aGate)
    {
        ArgumentNullException.ThrowIfNull(aRecord);

        return Parse(aRecord.GatesRun).Contains(aGate, StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses a stored <c>gates_run</c> value into gate names.
    /// </summary>
    /// <param name="aStored">The stored JSON array text, or <c>null</c>.</param>
    /// <returns>The gate names, or an empty list when the value is absent, empty or not a JSON array of strings.</returns>
    public static IReadOnlyList<string> Parse(string? aStored)
    {
        if (string.IsNullOrWhiteSpace(aStored))
        {
            return EmptyGates;
        }

        try
        {
            using var vDocument = JsonDocument.Parse(aStored);
            if (vDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                return EmptyGates;
            }

            var vGates = new List<string>();
            foreach (var vElement in vDocument.RootElement.EnumerateArray())
            {
                if (vElement.ValueKind == JsonValueKind.String)
                {
                    vGates.Add(vElement.GetString()!);
                }
            }

            return vGates;
        }
        catch (JsonException)
        {
            return EmptyGates;
        }
    }
}
