using System.Text.Json;
using System.Text.Json.Nodes;

namespace TfLens.Core.Metrics;

/// <summary>
/// The operator-editable rate card behind the counterfactual repricing — <b>an input, not a measurement</b>.
/// </summary>
/// <remarks>
/// ADR-009 makes <c>data/prices.json</c> the only editable input in the product, and SCHEMA.md §4
/// forbids presenting anything computed from it as measured spend. Every figure this type feeds carries
/// <see cref="EstimateLabel"/> in the UI, in the markdown snapshot and in <c>tflens.json</c>, and every
/// JSON key that holds one of those figures ends in <c>_usd_estimate</c>. Measured dollars exist in
/// exactly one place in TfLens: <c>cost_usd</c> on OpenCode records, which this type never touches.
/// </remarks>
public sealed class RateCard
{
    /// <summary>The wording BRD-59 fixes for every repricing figure, wherever it appears.</summary>
    public const string EstimateLabel = "estimate — tokens × rate card, not measured spend";

    /// <summary>The unit the rates are quoted in.</summary>
    public const string Units = "USD per 1,000,000 tokens";

    /// <summary>The banner written into a generated <c>prices.json</c> so an editor cannot mistake what it is.</summary>
    public const string FileNote =
        "RATE CARD — OPERATOR-EDITABLE INPUT, NOT A MEASUREMENT. TfLens multiplies observed token counts "
        + "by these rates to produce the counterfactual repricing figures, which are labelled '"
        + EstimateLabel
        + "' everywhere they appear. Nobody was billed these amounts. The only measured dollars in TfLens "
        + "are cost_usd on OpenCode records, which never come from this file. Edit the rates freely; "
        + "delete a model to drop it from the estimate (TfLens will name it as an unpriced observed model).";

    private readonly Dictionary<string, ModelRate> objModels;

    /// <summary>
    /// Creates a rate card over a model-to-rate map.
    /// </summary>
    /// <param name="aModels">Model id to rate; the keys are matched case-insensitively.</param>
    /// <param name="aSourcePath">Where the card was read from, for the export's provenance line.</param>
    public RateCard(IReadOnlyDictionary<string, ModelRate> aModels, string aSourcePath)
    {
        objModels = new Dictionary<string, ModelRate>(StringComparer.OrdinalIgnoreCase);
        foreach (var vEntry in aModels)
        {
            objModels[vEntry.Key] = vEntry.Value;
        }

        SourcePath = aSourcePath;
    }

    /// <summary>Where this card was loaded from; carried into the export so a figure can be traced.</summary>
    public string SourcePath { get; }

    /// <summary>The model ids the card prices, in ordinal order.</summary>
    public IReadOnlyList<string> ModelIds => objModels.Keys.OrderBy(aK => aK, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Finds the rate for an observed model id.
    /// </summary>
    /// <remarks>
    /// Observed model ids arrive in two shapes (SCHEMA.md §2.5): Claude Code reports a bare id
    /// (<c>claude-sonnet-4-6</c>), OpenCode reports <c>providerID/modelID</c>
    /// (<c>anthropic/claude-sonnet-4-6</c>). The provider prefix is stripped on a miss so one rate-card
    /// line serves both. A miss is reported to the user by name, never silently priced at zero.
    /// </remarks>
    /// <param name="aModel">The observed model id.</param>
    /// <returns>The rate, or <c>null</c> when the card does not price this model.</returns>
    public ModelRate? Find(string? aModel)
    {
        if (string.IsNullOrWhiteSpace(aModel))
        {
            return null;
        }

        if (objModels.TryGetValue(aModel, out var vRate))
        {
            return vRate;
        }

        var vSlash = aModel.LastIndexOf('/');
        return vSlash >= 0 && vSlash < aModel.Length - 1 && objModels.TryGetValue(aModel[(vSlash + 1)..], out var vTail)
            ? vTail
            : null;
    }

    /// <summary>
    /// Reads the rate card, writing the shipped default first when the file does not exist yet.
    /// </summary>
    /// <param name="aPath">Path of <c>prices.json</c>, from <see cref="TfLensOptions.PricesPath"/>.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The card on disk, or the built-in default when the file is missing or unreadable.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<RateCard> LoadAsync(string aPath, CancellationToken aCancellationToken = default)
    {
        await EnsureDefaultsAsync(aPath, aCancellationToken).ConfigureAwait(false);

        if (!File.Exists(aPath))
        {
            return Default(aPath);
        }

        try
        {
            var vText = await File.ReadAllTextAsync(aPath, aCancellationToken).ConfigureAwait(false);
            return Parse(vText, aPath);
        }
        catch (JsonException)
        {
            return Default(aPath);
        }
        catch (IOException)
        {
            return Default(aPath);
        }
    }

    /// <summary>
    /// Parses a rate-card document.
    /// </summary>
    /// <param name="aText">The JSON text of a <c>prices.json</c>.</param>
    /// <param name="aSourcePath">Where the text came from, for provenance.</param>
    /// <returns>The parsed card; models the document cannot express are skipped rather than guessed.</returns>
    /// <exception cref="JsonException">The text is not JSON at all.</exception>
    public static RateCard Parse(string aText, string aSourcePath)
    {
        var vRoot = JsonNode.Parse(aText) as JsonObject;
        var vModels = vRoot?["models"] as JsonObject;
        var vResult = new Dictionary<string, ModelRate>(StringComparer.OrdinalIgnoreCase);

        foreach (var vEntry in vModels ?? [])
        {
            if (vEntry.Value is not JsonObject vLine)
            {
                continue;
            }

            vResult[vEntry.Key] = new ModelRate(
                ReadRate(vLine, "input"),
                ReadRate(vLine, "output"),
                ReadRate(vLine, "cache_read", "cacheRead"),
                ReadRate(vLine, "cache_write", "cacheWrite"));
        }

        return new RateCard(vResult, aSourcePath);
    }

    /// <summary>
    /// Writes an edited rate card back to <c>prices.json</c>, which stays the source of truth.
    /// </summary>
    /// <remarks>
    /// BRD-61 / ADR-009: the edit dialog on the Routing page edits the file, it does not replace it with
    /// an in-memory card. The operator note, the units, the <c>estimate_only</c> flag and the estimate
    /// label are re-written verbatim on every save, so a rate card can never lose the one sentence that
    /// keeps it from being read as measured spend. A negative rate is refused rather than stored,
    /// because a negative rate would make the counterfactual delta lie about what routing saved.
    /// </remarks>
    /// <param name="aPath">Path of <c>prices.json</c>, from <see cref="TfLensOptions.PricesPath"/>.</param>
    /// <param name="aModels">Model id to rate; this becomes the whole <c>models</c> block.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The card as it now stands on disk.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A rate is negative.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<RateCard> SaveAsync(
        string aPath,
        IReadOnlyDictionary<string, ModelRate> aModels,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aModels);

        foreach (var vEntry in aModels)
        {
            if (vEntry.Value.InputPerMillion < 0m
                || vEntry.Value.OutputPerMillion < 0m
                || vEntry.Value.CacheReadPerMillion < 0m
                || vEntry.Value.CacheWritePerMillion < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(aModels),
                    vEntry.Key,
                    "A rate-card rate cannot be negative.");
            }
        }

        var vFolder = Path.GetDirectoryName(Path.GetFullPath(aPath));
        if (!string.IsNullOrEmpty(vFolder))
        {
            Directory.CreateDirectory(vFolder);
        }

        var vModels = new JsonObject();
        foreach (var vEntry in aModels.OrderBy(aE => aE.Key, StringComparer.Ordinal))
        {
            vModels[vEntry.Key] = new JsonObject
            {
                ["input"] = vEntry.Value.InputPerMillion,
                ["output"] = vEntry.Value.OutputPerMillion,
                ["cache_read"] = vEntry.Value.CacheReadPerMillion,
                ["cache_write"] = vEntry.Value.CacheWritePerMillion
            };
        }

        var vRoot = new JsonObject
        {
            ["note"] = FileNote,
            ["units"] = Units,
            ["estimate_only"] = true,
            ["estimate_label"] = EstimateLabel,
            ["models"] = vModels
        };

        var vText = vRoot.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }) + Environment.NewLine;

        await File.WriteAllTextAsync(aPath, vText, aCancellationToken).ConfigureAwait(false);

        return Parse(vText, aPath);
    }

    /// <summary>
    /// Writes the default <c>prices.json</c> and its operator note when they do not exist yet.
    /// </summary>
    /// <remarks>
    /// Never overwrites: the file is the operator's, and BRD-61 makes it the source of truth that the
    /// edit dialog merely edits. The companion <c>README.md</c> says so beside it, so an operator who
    /// finds the folder without finding the docs still learns the file is a rate card and not a bill.
    /// </remarks>
    /// <param name="aPath">Path of <c>prices.json</c>.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes once the defaults are in place.</returns>
    public static async Task EnsureDefaultsAsync(string aPath, CancellationToken aCancellationToken = default)
    {
        var vFolder = Path.GetDirectoryName(Path.GetFullPath(aPath));
        if (string.IsNullOrEmpty(vFolder))
        {
            return;
        }

        Directory.CreateDirectory(vFolder);

        if (!File.Exists(aPath))
        {
            await File.WriteAllTextAsync(aPath, DefaultDocument(), aCancellationToken).ConfigureAwait(false);
        }

        var vReadme = Path.Combine(vFolder, "README.md");
        if (!File.Exists(vReadme))
        {
            await File.WriteAllTextAsync(vReadme, DefaultReadme(), aCancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The rates TfLens ships with, used when <c>prices.json</c> is absent or unreadable.
    /// </summary>
    /// <param name="aSourcePath">The path the card stands in for.</param>
    /// <returns>The built-in card.</returns>
    public static RateCard Default(string aSourcePath = "(built-in default)") =>
        Parse(DefaultDocument(), aSourcePath);

    /// <summary>Reads one rate, accepting either spelling of the cache keys.</summary>
    /// <param name="aLine">The model's rate object.</param>
    /// <param name="aNames">Accepted key spellings, most preferred first.</param>
    /// <returns>The rate, or zero when the document names none.</returns>
    private static decimal ReadRate(JsonObject aLine, params string[] aNames)
    {
        foreach (var vName in aNames)
        {
            if (aLine.TryGetPropertyValue(vName, out var vNode) && vNode is not null
                && decimal.TryParse(vNode.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var vValue))
            {
                return vValue;
            }
        }

        return 0m;
    }

    /// <summary>
    /// The default rate-card document — comment-free JSON, because JSON has no comments.
    /// </summary>
    /// <remarks>
    /// The warning that these are rates and not measurements is therefore carried in real string fields
    /// (<c>note</c>, <c>units</c>, <c>estimate_only</c>) that survive any editor, rather than in a
    /// comment syntax a strict parser would reject. Rates are Anthropic first-party list prices per
    /// million tokens; cache-read is a tenth of input and cache-write is 1.25× input, per the published
    /// multipliers. They are a starting point for the operator to correct — subscription and partner
    /// pricing differ, which is precisely why the file is editable.
    /// </remarks>
    /// <returns>The JSON text.</returns>
    private static string DefaultDocument()
    {
        var vModels = new JsonObject();
        foreach (var vLine in DefaultRates)
        {
            vModels[vLine.Key] = new JsonObject
            {
                ["input"] = vLine.Value.InputPerMillion,
                ["output"] = vLine.Value.OutputPerMillion,
                ["cache_read"] = vLine.Value.CacheReadPerMillion,
                ["cache_write"] = vLine.Value.CacheWritePerMillion
            };
        }

        var vRoot = new JsonObject
        {
            ["note"] = FileNote,
            ["units"] = Units,
            ["estimate_only"] = true,
            ["estimate_label"] = EstimateLabel,
            ["models"] = vModels
        };

        // The relaxed encoder matters here: the whole point of the note is that an operator opening
        // prices.json reads "NOT A MEASUREMENT" and the estimate label in plain words. The default
        // encoder escapes the em dash and the multiplication sign to — and ×, which turns
        // the one sentence that keeps a rate card from being mistaken for measured spend into noise.
        return vRoot.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }) + Environment.NewLine;
    }

    /// <summary>The note dropped beside <c>prices.json</c> the first time the data folder is written.</summary>
    /// <returns>The markdown text.</returns>
    private static string DefaultReadme() =>
        """
        # TfLens data folder

        Runtime state only. Nothing here is committed (`/data/` is gitignored) and nothing here is
        derived data that TfLens depends on — every figure is recomputed from the stream tables at
        request time (REQ-FN-046).

        | Path | What it is | Who edits it |
        |---|---|---|
        | `raw/<userId>/` | The verbatim JSONL archive, written before parsing. A rebuild replays it. | TfLens only |
        | `reports/<userId>/<date>/<framework>/` | `snapshot.md` + `tflens.json` for one export. | TfLens only |
        | `prices.json` | **The rate card. YOU edit this.** | The operator |
        | `parity-last.json` | The record of the last passing parity run. Written by `tools/parity-compare.py --record`. | The parity procedure |

        ## `prices.json` — a rate card, not a bill

        `prices.json` is the one editable input in the product (ADR-009). It lists, per model, the USD
        rate per 1,000,000 input / output / cache-read / cache-write tokens.

        **Everything TfLens computes from it is an estimate — tokens × rate card, not measured spend.**
        That wording (SCHEMA.md §4, BRD-59) appears beside every figure derived from this file, on the
        Routing & economics page, in `snapshot.md`, and in `tflens.json`, where each such value's key
        ends in `_usd_estimate`. Nobody was billed these amounts.

        The only *measured* dollars in TfLens are `cost_usd` on OpenCode records, which the harness
        itself reports. They never come from this file and are never totalled with anything from it.

        Edit the rates to match what you actually pay. Delete a model to drop it from the estimate —
        TfLens will then list it as an unpriced observed model rather than quietly pricing it at zero.
        Add a model by copying a block and changing its key; the key is the model id as the telemetry
        observed it (a `provider/model` id also matches a bare `model` line).

        """;

    /// <summary>The shipped starting rates, in USD per million tokens.</summary>
    private static readonly IReadOnlyList<KeyValuePair<string, ModelRate>> DefaultRates =
    [
        new("claude-fable-5", new ModelRate(10.00m, 50.00m, 1.00m, 12.50m)),
        new("claude-opus-5", new ModelRate(5.00m, 25.00m, 0.50m, 6.25m)),
        new("claude-opus-4-8", new ModelRate(5.00m, 25.00m, 0.50m, 6.25m)),
        new("claude-opus-4-7", new ModelRate(5.00m, 25.00m, 0.50m, 6.25m)),
        new("claude-opus-4-6", new ModelRate(5.00m, 25.00m, 0.50m, 6.25m)),
        new("claude-sonnet-5", new ModelRate(2.00m, 10.00m, 0.20m, 2.50m)),
        new("claude-sonnet-4-6", new ModelRate(3.00m, 15.00m, 0.30m, 3.75m)),
        new("claude-haiku-4-5", new ModelRate(1.00m, 5.00m, 0.10m, 1.25m))
    ];
}
