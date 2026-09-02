using System.Data;
using System.Text.Json;
using Dapper;

namespace TfLens.Core.Storage;

/// <summary>
/// Round-trips a <c>{key: number}</c> map between a PostgreSQL <c>jsonb</c> column and
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> (REQ-FN-088, ADR-025).
/// </summary>
/// <remarks>
/// <para>
/// <c>"Run"."ModelTokensOut"</c> is the one column in the schema that is a structured value rather than
/// a scalar or an opaque overflow blob. Npgsql hands a <c>jsonb</c> column to Dapper as text, and Dapper
/// has no built-in conversion from text to a dictionary, so without this handler the column would either
/// have to become a child table it does not need or be smuggled through a second string property that
/// every consumer would then have to deserialize for itself.
/// </para>
/// <para>
/// <b>Null is preserved in both directions.</b> Dapper never calls <see cref="Parse"/> for a
/// <c>DBNull</c> column, so an absent split stays <c>null</c> on the record; a <c>null</c> map is written
/// as SQL <c>NULL</c> rather than as <c>"{}"</c>. That distinction is the whole point of the column — "no
/// per-model split was captured" and "a split was captured naming no models" are different facts.
/// </para>
/// </remarks>
public sealed class JsonMapTypeHandler : SqlMapper.TypeHandler<IReadOnlyDictionary<string, long>>
{
    /// <summary>Registers the handler; safe to call repeatedly, and called from the store's type initializer.</summary>
    /// <remarks>
    /// Dapper's handler table is global, so registration belongs at a point every store path passes
    /// through exactly once rather than at each call site.
    /// </remarks>
    public static void Register() => SqlMapper.AddTypeHandler(new JsonMapTypeHandler());

    /// <summary>
    /// Reads the stored <c>jsonb</c> text back into a map.
    /// </summary>
    /// <param name="aValue">The value Npgsql produced for the column — <c>jsonb</c> arrives as text.</param>
    /// <returns>The map, or <c>null</c> when the stored text is absent or not a JSON object.</returns>
    /// <exception cref="JsonException">The column holds text that is not valid JSON.</exception>
    public override IReadOnlyDictionary<string, long>? Parse(object aValue)
    {
        if (aValue is null || aValue is DBNull)
        {
            return null;
        }

        var vText = aValue as string ?? aValue.ToString();
        if (string.IsNullOrWhiteSpace(vText))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, long>>(vText);
    }

    /// <summary>
    /// Writes the map as JSON text; the insert statement casts it to <c>jsonb</c>.
    /// </summary>
    /// <param name="aParameter">The command parameter being filled.</param>
    /// <param name="aValue">The map, or <c>null</c> when the producer captured no split.</param>
    public override void SetValue(IDbDataParameter aParameter, IReadOnlyDictionary<string, long>? aValue)
    {
        ArgumentNullException.ThrowIfNull(aParameter);

        aParameter.DbType = DbType.String;
        aParameter.Value = aValue is null ? DBNull.Value : JsonSerializer.Serialize(aValue);
    }
}
