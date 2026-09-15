using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TfLens.Core.Metrics;

/// <summary>
/// The providers a rate comes from, and how each one is kept current
/// (SCHEMA.md §2.5b, REQ-FN-139, REQ-UI-071).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why TfLens owns this and the framework does not.</b> The framework records what it measured —
/// tokens, per model, per run — and nothing priced is ever written to a record. Turning tokens into
/// money is a reporting job, so the price list lives here. That separation is what lets a rate change
/// and every run ever recorded re-price, with no record having been wrong.
/// </para>
/// <para>
/// <b>Two ways a rate arrives, and the page says which.</b> A provider marked
/// <see cref="PriceFetch.Api"/> is refreshed from its own endpoint; one marked
/// <see cref="PriceFetch.Manual"/> holds rates a person typed from the provider's published page.
/// Both are legitimate and they are <b>not</b> the same claim, so each rate carries its source and the
/// date it was last checked rather than appearing as bare authority.
/// </para>
/// <para>
/// <b>It is kept apart from <c>prices.json</c> on purpose.</b> That file is the flat, effective rate
/// card every figure is priced from, and the parity gate reads figures built on it; this file is the
/// editable provenance behind it. Applying a change here rewrites the flat card, so there is exactly
/// one answer to "what does this model cost" and a visible record of where it came from.
/// </para>
/// </remarks>
public static class PriceProviders
{
    /// <summary>The banner written into the file so an editor cannot mistake what it is.</summary>
    public const string FileNote =
        "PRICE PROVIDERS — OPERATOR-EDITABLE INPUT, NOT A MEASUREMENT. These are published list rates, "
        + "used to price measured tokens. Nobody was billed these amounts; the only measured dollars in "
        + "TfLens are cost_usd on records whose provider billed per token. Applying a change here "
        + "rewrites data/prices.json, which is the flat card every figure is priced from.";

    /// <summary>OpenRouter's public model endpoint — the one provider that publishes rates as data.</summary>
    public const string OpenRouterModelsUrl = "https://openrouter.ai/api/v1/models";

    /// <summary>Tokens the per-million rates are quoted against.</summary>
    private const decimal TokensPerMillion = 1_000_000m;

    /// <summary>
    /// Reads the provider book, seeding the four the estate already runs on when the file is absent.
    /// </summary>
    /// <param name="aPath">Path of <c>price-providers.json</c>.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The book; never <c>null</c>, and never empty on a first run.</returns>
    public static async Task<IReadOnlyList<PriceProvider>> LoadAsync(
        string aPath,
        CancellationToken aCancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aPath);

        if (!File.Exists(aPath))
        {
            var vSeeded = Seed();
            await SaveAsync(aPath, vSeeded, aCancellationToken).ConfigureAwait(false);

            return vSeeded;
        }

        try
        {
            var vText = await File.ReadAllTextAsync(aPath, aCancellationToken).ConfigureAwait(false);

            return Parse(vText);
        }
        catch (JsonException)
        {
            // A malformed file is not an empty price list. Returning the seed keeps every figure priced
            // while the operator repairs it, and the page shows what it is reading from.
            return Seed();
        }
        catch (IOException)
        {
            return Seed();
        }
    }

    /// <summary>Parses a provider book.</summary>
    /// <param name="aText">The JSON text.</param>
    /// <returns>The providers, in file order.</returns>
    /// <exception cref="JsonException">The text is not JSON at all.</exception>
    public static IReadOnlyList<PriceProvider> Parse(string aText)
    {
        var vRoot = JsonNode.Parse(aText) as JsonObject;
        var vProviders = new List<PriceProvider>();

        foreach (var vNode in vRoot?["providers"] as JsonArray ?? [])
        {
            if (vNode is not JsonObject vEntry)
            {
                continue;
            }

            var vModels = new List<PriceProviderModel>();

            foreach (var vModel in vEntry["models"] as JsonObject ?? [])
            {
                if (vModel.Value is JsonObject vLine)
                {
                    vModels.Add(new PriceProviderModel(
                        vModel.Key,
                        Rate(vLine, "input"),
                        Rate(vLine, "output"),
                        Rate(vLine, "cache_read"),
                        Rate(vLine, "cache_write")));
                }
            }

            vProviders.Add(new PriceProvider(
                Text(vEntry, "id") ?? string.Empty,
                Text(vEntry, "name") ?? string.Empty,
                Text(vEntry, "source_url"),
                Text(vEntry, "api_url"),
                string.Equals(Text(vEntry, "fetch"), nameof(PriceFetch.Api), StringComparison.OrdinalIgnoreCase)
                    ? PriceFetch.Api
                    : PriceFetch.Manual,
                Text(vEntry, "last_checked"),
                vModels));
        }

        return vProviders;
    }

    /// <summary>
    /// Writes the provider book.
    /// </summary>
    /// <param name="aPath">Path of <c>price-providers.json</c>.</param>
    /// <param name="aProviders">The providers to store.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The running write.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A rate is negative.</exception>
    public static async Task SaveAsync(
        string aPath,
        IReadOnlyList<PriceProvider> aProviders,
        CancellationToken aCancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aPath);
        ArgumentNullException.ThrowIfNull(aProviders);

        foreach (var vModel in aProviders.SelectMany(aProvider => aProvider.Models))
        {
            if (vModel.InputPerMillion < 0m || vModel.OutputPerMillion < 0m
                || vModel.CacheReadPerMillion < 0m || vModel.CacheWritePerMillion < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(aProviders),
                    vModel.Model,
                    "A published rate cannot be negative.");
            }
        }

        var vFolder = Path.GetDirectoryName(Path.GetFullPath(aPath));

        if (!string.IsNullOrEmpty(vFolder))
        {
            Directory.CreateDirectory(vFolder);
        }

        var vArray = new JsonArray();

        foreach (var vProvider in aProviders)
        {
            var vModels = new JsonObject();

            foreach (var vModel in vProvider.Models.OrderBy(aM => aM.Model, StringComparer.Ordinal))
            {
                vModels[vModel.Model] = new JsonObject
                {
                    ["input"] = vModel.InputPerMillion,
                    ["output"] = vModel.OutputPerMillion,
                    ["cache_read"] = vModel.CacheReadPerMillion,
                    ["cache_write"] = vModel.CacheWritePerMillion
                };
            }

            vArray.Add(new JsonObject
            {
                ["id"] = vProvider.Id,
                ["name"] = vProvider.Name,
                ["source_url"] = vProvider.SourceUrl,
                ["api_url"] = vProvider.ApiUrl,
                ["fetch"] = vProvider.Fetch.ToString(),
                ["last_checked"] = vProvider.LastChecked,
                ["models"] = vModels
            });
        }

        var vDocument = new JsonObject
        {
            ["note"] = FileNote,
            ["units"] = RateCard.Units,
            ["providers"] = vArray
        };

        await File.WriteAllTextAsync(
            aPath,
            vDocument.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            aCancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Flattens the book into the effective rate card and writes it to <c>prices.json</c>.
    /// </summary>
    /// <remarks>
    /// Where two providers price the same model id, the <b>first</b> in the list wins and the second is
    /// ignored rather than averaged: a rate is a published fact from one provider, and the mean of two
    /// published rates is a number nobody publishes. The page shows the clash so it can be resolved by
    /// a person rather than by precedence nobody chose.
    /// </remarks>
    /// <param name="aPricesPath">Path of <c>prices.json</c>.</param>
    /// <param name="aProviders">The providers.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The flat card as it now stands on disk.</returns>
    public static Task<RateCard> ApplyAsync(
        string aPricesPath,
        IReadOnlyList<PriceProvider> aProviders,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aProviders);

        var vFlat = new Dictionary<string, ModelRate>(StringComparer.OrdinalIgnoreCase);

        foreach (var vModel in aProviders.SelectMany(aProvider => aProvider.Models))
        {
            if (!vFlat.ContainsKey(vModel.Model))
            {
                vFlat[vModel.Model] = new ModelRate(
                    vModel.InputPerMillion,
                    vModel.OutputPerMillion,
                    vModel.CacheReadPerMillion,
                    vModel.CacheWritePerMillion);
            }
        }

        return RateCard.SaveAsync(aPricesPath, vFlat, aCancellationToken);
    }

    /// <summary>
    /// Model ids priced by more than one provider, which the operator should settle.
    /// </summary>
    /// <param name="aProviders">The providers.</param>
    /// <returns>The clashing model ids, sorted.</returns>
    public static IReadOnlyList<string> Clashes(IReadOnlyList<PriceProvider> aProviders)
    {
        ArgumentNullException.ThrowIfNull(aProviders);

        return aProviders
            .SelectMany(aProvider => aProvider.Models.Select(aModel => aModel.Model))
            .GroupBy(aModel => aModel, StringComparer.OrdinalIgnoreCase)
            .Where(aGroup => aGroup.Count() > 1)
            .Select(aGroup => aGroup.Key)
            .OrderBy(aModel => aModel, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Reads OpenRouter's published model list into provider rates.
    /// </summary>
    /// <remarks>
    /// OpenRouter quotes <b>per token</b> as strings; these are multiplied to the per-million unit every
    /// other rate here uses. A model whose price cannot be read is skipped rather than stored at zero —
    /// a model priced at nothing would make every run that used it look free. So is one the endpoint
    /// prices at <c>-1</c>, which is how it says the price is variable or not disclosed.
    /// </remarks>
    /// <param name="aJson">The endpoint's response body.</param>
    /// <returns>The rates it could read, by model id.</returns>
    /// <exception cref="JsonException">The body is not JSON at all.</exception>
    public static IReadOnlyList<PriceProviderModel> ReadOpenRouter(string aJson)
    {
        var vRoot = JsonNode.Parse(aJson) as JsonObject;
        var vModels = new List<PriceProviderModel>();

        foreach (var vNode in vRoot?["data"] as JsonArray ?? [])
        {
            if (vNode is not JsonObject vEntry
                || vEntry["id"]?.GetValue<string>() is not { Length: > 0 } vId
                || vEntry["pricing"] is not JsonObject vPricing)
            {
                continue;
            }

            var vIn = PerToken(vPricing, "prompt");
            var vOut = PerToken(vPricing, "completion");
            var vCacheRead = PerToken(vPricing, "input_cache_read");
            var vCacheWrite = PerToken(vPricing, "input_cache_write");

            // Both prices are required. Skipping only when BOTH were unreadable stored a single missing
            // price as 0 below, so every output (or input) token on that model priced as free — the very
            // thing the summary above rules out (REQ-UI-072; found 2026-09-14, fixed 2026-09-15).
            if (vIn is null || vOut is null)
            {
                continue;
            }

            // OpenRouter publishes `-1` for a model whose price is variable or not disclosed. That is
            // not a cheap model, it is an UNPRICED one — the endpoint's own way of saying it cannot
            // answer. Storing it would either poison the rate card with a negative rate or, worse,
            // coerce it to zero and make every run on that model look free. It is skipped, and the run
            // that used it reports in `list_usd_unpriced_n` where it belongs.
            if (vIn < 0m || vOut < 0m || vCacheRead < 0m || vCacheWrite < 0m)
            {
                continue;
            }

            vModels.Add(new PriceProviderModel(
                vId,
                vIn ?? 0m,
                vOut ?? 0m,
                vCacheRead ?? 0m,
                vCacheWrite ?? 0m));
        }

        return vModels;
    }

    /// <summary>
    /// The four providers the estate already runs on, with the rates the framework's own lookup returns.
    /// </summary>
    /// <remarks>
    /// Seeded rather than left empty so a first run prices something rather than reporting an estate of
    /// unpriced models. Every rate is editable and every one says where it came from.
    /// </remarks>
    /// <returns>The seeded providers.</returns>
    public static IReadOnlyList<PriceProvider> Seed() =>
    [
        new PriceProvider(
            "anthropic",
            "Anthropic (Claude)",
            "https://www.anthropic.com/pricing",
            null,
            PriceFetch.Manual,
            null,
            [
                new PriceProviderModel("claude-opus-5", 5m, 25m, 0.5m, 6.25m),
                new PriceProviderModel("claude-sonnet-5", 2m, 10m, 0.2m, 2.5m),
                new PriceProviderModel("claude-fable-5", 10m, 50m, 1m, 12.5m),
                new PriceProviderModel("claude-fable-5-1", 10m, 50m, 0.25m, 12.5m),
                new PriceProviderModel("claude-haiku-4-5-20251001", 1m, 5m, 0.1m, 1.25m),
                new PriceProviderModel("claude-haiku-4-5", 1m, 5m, 0.1m, 1.25m),

                // Superseded models are kept priced. A record naming one is history and still has to
                // report what it cost; dropping the rate would silently move those runs into
                // `list_usd_unpriced_n` and make a past phase look cheaper than it was.
                new PriceProviderModel("claude-opus-4-8", 5m, 25m, 0.5m, 6.25m),
                new PriceProviderModel("claude-opus-4-7", 5m, 25m, 0.5m, 6.25m),
                new PriceProviderModel("claude-opus-4-6", 5m, 25m, 0.5m, 6.25m),
                new PriceProviderModel("claude-sonnet-4-6", 3m, 15m, 0.3m, 3.75m)
            ]),
        new PriceProvider(
            "openai",
            "OpenAI",
            "https://openai.com/api/pricing",
            null,
            PriceFetch.Manual,
            null,
            [new PriceProviderModel("gpt-5.6-sol", 4m, 20m, 0.4m, 5m)]),
        new PriceProvider(
            "opencode-go",
            "OpenCode Go",
            "https://opencode.ai/pricing",
            null,
            PriceFetch.Manual,
            null,
            [new PriceProviderModel("opencode-go/glm-5.3", 1.4m, 4.4m, 0.26m, 0m)]),
        new PriceProvider(
            "openrouter",
            "OpenRouter",
            "https://openrouter.ai/models",
            OpenRouterModelsUrl,
            PriceFetch.Api,
            null,
            [])
    ];

    /// <summary>Reads one per-token price string and converts it to the per-million unit.</summary>
    /// <param name="aPricing">The pricing object.</param>
    /// <param name="aKey">Which price.</param>
    /// <returns>The rate per million tokens, or <c>null</c> when it cannot be read.</returns>
    private static decimal? PerToken(JsonObject aPricing, string aKey)
    {
        var vRaw = aPricing[aKey]?.ToString();

        return decimal.TryParse(vRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var vValue)
            ? vValue * TokensPerMillion
            : null;
    }

    /// <summary>Reads one rate, defaulting an absent one to zero.</summary>
    /// <param name="aLine">The model's rate object.</param>
    /// <param name="aKey">Which rate.</param>
    /// <returns>The rate.</returns>
    private static decimal Rate(JsonObject aLine, string aKey) =>
        aLine[aKey] is { } vNode && decimal.TryParse(
            vNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var vValue)
            ? vValue
            : 0m;

    /// <summary>Reads one string property.</summary>
    /// <param name="aEntry">The object.</param>
    /// <param name="aKey">The property.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static string? Text(JsonObject aEntry, string aKey)
    {
        var vValue = aEntry[aKey]?.ToString();

        return string.IsNullOrWhiteSpace(vValue) ? null : vValue;
    }
}

/// <summary>How a provider's rates are kept current.</summary>
public enum PriceFetch
{
    /// <summary>Typed from the provider's published page; the only option where no endpoint exists.</summary>
    Manual = 0,

    /// <summary>Read from the provider's own endpoint.</summary>
    Api = 1
}

/// <summary>
/// One provider of published rates (REQ-FN-139).
/// </summary>
/// <param name="Id">Stable key, lower-case.</param>
/// <param name="Name">What the page calls it.</param>
/// <param name="SourceUrl">The published page a person can check the rates against.</param>
/// <param name="ApiUrl">The endpoint, where one exists.</param>
/// <param name="Fetch">Whether the rates are refreshed from that endpoint or typed.</param>
/// <param name="LastChecked">
/// When the rates were last confirmed, or <c>null</c> for never. Shown beside every figure a rate feeds,
/// because a price list nobody has checked for a year is a different claim from one checked today.
/// </param>
/// <param name="Models">The rates, per model id.</param>
public sealed record PriceProvider(
    string Id,
    string Name,
    string? SourceUrl,
    string? ApiUrl,
    PriceFetch Fetch,
    string? LastChecked,
    IReadOnlyList<PriceProviderModel> Models);

/// <summary>
/// One model's published rates, in USD per million tokens.
/// </summary>
/// <param name="Model">The model id exactly as a record names it; the rate is matched on this.</param>
/// <param name="InputPerMillion">Input rate.</param>
/// <param name="OutputPerMillion">Output rate.</param>
/// <param name="CacheReadPerMillion">Cache-read rate.</param>
/// <param name="CacheWritePerMillion">Cache-write rate.</param>
public sealed record PriceProviderModel(
    string Model,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal CacheReadPerMillion,
    decimal CacheWritePerMillion);
