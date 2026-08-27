using System.Text.Json;
using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// Reads a raw Playbook stream file and reports the field names it actually carries (REQ-FN-068).
/// </summary>
/// <remarks>
/// <para>
/// ADR-010 makes schema discovery the adapter's <b>first</b> task: no <c>"PbEvent"</c> column and no
/// chart may be fixed from the brief's prose, only from a real file. This probe is how that rule is kept
/// mechanically rather than by memory — the adapter runs it on the archived bytes before the parser sees
/// them, and its output is what goes into <c>DECISIONS.md</c> §Playbook.
/// </para>
/// <para>
/// The probe is deliberately not a parser: it maps nothing to a column and rejects nothing, it only
/// counts what is there. It therefore cannot bias the discovery towards the columns TfLens happens to
/// have guessed.
/// </para>
/// </remarks>
public static class PlaybookSchemaProbe
{
    /// <summary>How many distinct sample values are kept per field for the DECISIONS.md entry.</summary>
    private const int MaxSamplesPerField = 5;

    /// <summary>How many characters of a sample value are kept; longer values are elided.</summary>
    private const int MaxSampleLength = 60;

    /// <summary>
    /// Observes every field name, value kind and sample value in one NDJSON file.
    /// </summary>
    /// <param name="aText">The raw file text, exactly as archived.</param>
    /// <returns>The observation, with fields ordered most frequent first then by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aText"/> is <c>null</c>.</exception>
    public static PlaybookSchemaObservation Observe(string aText)
    {
        ArgumentNullException.ThrowIfNull(aText);

        var vFields = new Dictionary<string, FieldTally>(StringComparer.Ordinal);
        var vRecords = 0;
        var vInvalid = 0;

        foreach (var vLine in aText.Split('\n'))
        {
            var vTrimmed = vLine.Trim();
            if (vTrimmed.Length == 0)
            {
                continue;
            }

            if (!TryTally(vTrimmed, vFields))
            {
                vInvalid++;
                continue;
            }

            vRecords++;
        }

        var vObserved = vFields
            .OrderByDescending(aPair => aPair.Value.Occurrences)
            .ThenBy(aPair => aPair.Key, StringComparer.Ordinal)
            .Select(aPair => new ObservedField(
                aPair.Key,
                aPair.Value.Occurrences,
                aPair.Value.Kinds.OrderBy(aK => aK, StringComparer.Ordinal).ToList(),
                aPair.Value.Samples.ToList()))
            .ToList();

        return new PlaybookSchemaObservation(vRecords, vInvalid, vObserved);
    }

    /// <summary>
    /// Renders an observation as the Markdown block REQ-FN-068 requires in <c>DECISIONS.md</c>.
    /// </summary>
    /// <param name="aObservation">What the probe saw.</param>
    /// <param name="aSource">Where the file came from — repository and SHA, or a fixture path.</param>
    /// <returns>A Markdown table of the observed fields, ready to paste under <c>§Playbook</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aObservation"/> is <c>null</c>.</exception>
    public static string ToDecisionsMarkdown(PlaybookSchemaObservation aObservation, string aSource)
    {
        ArgumentNullException.ThrowIfNull(aObservation);

        var vBuilder = new System.Text.StringBuilder();
        vBuilder.AppendLine($"Source: `{aSource}` — {aObservation.Records} records, {aObservation.InvalidLines} invalid lines.");
        vBuilder.AppendLine();
        vBuilder.AppendLine("| Field | Occurrences | JSON kinds | Sample values |");
        vBuilder.AppendLine("|-------|-------------|------------|---------------|");

        foreach (var vField in aObservation.Fields)
        {
            var vKinds = string.Join(", ", vField.JsonKinds);
            var vSamples = string.Join(" · ", vField.SampleValues.Select(aS => "`" + aS + "`"));
            vBuilder.AppendLine($"| `{vField.Name}` | {vField.Occurrences} | {vKinds} | {vSamples} |");
        }

        return vBuilder.ToString();
    }

    /// <summary>
    /// Tallies one line into the field table.
    /// </summary>
    /// <param name="aLine">One trimmed NDJSON line.</param>
    /// <param name="aFields">The running tally, mutated in place.</param>
    /// <returns><c>false</c> when the line is not a JSON object.</returns>
    private static bool TryTally(string aLine, Dictionary<string, FieldTally> aFields)
    {
        JsonDocument vDocument;
        try
        {
            vDocument = JsonDocument.Parse(aLine);
        }
        catch (JsonException)
        {
            return false;
        }

        using (vDocument)
        {
            if (vDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var vProperty in vDocument.RootElement.EnumerateObject())
            {
                Record(aFields, vProperty);
            }
        }

        return true;
    }

    /// <summary>
    /// Records one property occurrence, its value kind and, when there is room, a sample of its value.
    /// </summary>
    /// <param name="aFields">The running tally.</param>
    /// <param name="aProperty">The property seen.</param>
    private static void Record(Dictionary<string, FieldTally> aFields, JsonProperty aProperty)
    {
        if (!aFields.TryGetValue(aProperty.Name, out var vTally))
        {
            vTally = new FieldTally();
            aFields[aProperty.Name] = vTally;
        }

        vTally.Occurrences++;
        vTally.Kinds.Add(aProperty.Value.ValueKind.ToString());

        if (vTally.Samples.Count >= MaxSamplesPerField)
        {
            return;
        }

        var vRaw = aProperty.Value.ValueKind == JsonValueKind.String
            ? aProperty.Value.GetString() ?? string.Empty
            : aProperty.Value.GetRawText();

        var vSample = vRaw.Length > MaxSampleLength ? vRaw[..MaxSampleLength] + "…" : vRaw;
        vTally.Samples.Add(vSample);
    }

    /// <summary>What the probe accumulates for one field name.</summary>
    private sealed class FieldTally
    {
        /// <summary>How many records carried the field.</summary>
        public int Occurrences { get; set; }

        /// <summary>Distinct JSON value kinds seen for it.</summary>
        public HashSet<string> Kinds { get; } = new(StringComparer.Ordinal);

        /// <summary>Distinct sample values, capped.</summary>
        public HashSet<string> Samples { get; } = new(StringComparer.Ordinal);
    }
}
