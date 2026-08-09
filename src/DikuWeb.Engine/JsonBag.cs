using System.Globalization;
using System.Text.Json;

namespace DikuWeb.Engine;

/// <summary>
/// Reads the free-form <c>Dictionary&lt;string, object&gt;</c> bags that content carries -
/// mob behavior, item state, and anything else PLAN.md §4.8 left deliberately schemaless.
/// </summary>
/// <remarks>
/// These bags are stored as jsonb and cross the builder API as JSON, so a value authored in C#
/// as a <see cref="bool"/> or a <c>List&lt;object&gt;</c> comes back as a <see cref="JsonElement"/> -
/// System.Text.Json has nowhere else to put it. Every call site that pattern-matched the C# shape
/// (<c>is bool</c>, <c>is List&lt;object&gt;</c>) was therefore false for every value that had
/// round-tripped, which is all of them outside a unit test.
///
/// That had already silently killed three features: shopkeeper detection, shop inventories, and
/// mob idle emotes. It also disabled the "quest items cannot be sold" rule, which fails open.
/// <c>StatReader</c> is the same guard for the numeric bags; this is the one for the rest.
///
/// Read bags through here, never by pattern-matching them directly.
/// </remarks>
public static class JsonBag
{
    /// <summary>
    /// Reads a boolean, accepting the JSON shape as well as the native one. The word "true" as a
    /// string counts too - these bags are hand-authorable, and someone typing it means it.
    /// </summary>
    public static bool Boolean(IReadOnlyDictionary<string, object>? bag, string key)
    {
        if (bag is null || !bag.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return raw switch
        {
            bool native => native,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var parsed) && parsed,
        };
    }

    /// <summary>
    /// Reads a whole number, falling back when the key is absent or is not one.
    /// </summary>
    /// <remarks>
    /// Parses the text rather than pattern-matching the runtime type, which is what makes it
    /// work for a <see cref="JsonElement"/>. A reader written as <c>value is int</c> is false for
    /// every value that came out of jsonb, and silently returns its fallback - so a mob template
    /// with 250 health reads as whatever number the call site happened to pick as a default.
    /// </remarks>
    public static int Int32(IReadOnlyDictionary<string, object>? bag, string key, int fallback = 0) =>
        Text(bag, key) is { } text &&
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>Reads a 64-bit whole number, falling back when the key is absent or is not one.</summary>
    public static long Int64(IReadOnlyDictionary<string, object>? bag, string key, long fallback = 0) =>
        Text(bag, key) is { } text &&
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>Reads a string, or null when the key is absent, null, or blank.</summary>
    public static string? Text(IReadOnlyDictionary<string, object>? bag, string key)
    {
        if (bag is null || !bag.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        // JsonElement.ToString() on a string yields the unquoted text, which is the only reason
        // the aggression check survived while its neighbours did not.
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Reads a list of strings, accepting a JSON array, a native list, or a single bare string.
    /// Blank entries are dropped, so a half-filled row in the builder is not a shop item.
    /// </summary>
    public static IReadOnlyList<string> Strings(IReadOnlyDictionary<string, object>? bag, string key)
    {
        if (bag is null || !bag.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        // Order matters: string is itself IEnumerable, and JsonElement is a struct that would
        // otherwise fall to the catch-all and stringify as raw JSON.
        var values = raw switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } array =>
                array.EnumerateArray().Select(element => element.ToString()),
            JsonElement { ValueKind: JsonValueKind.String } single =>
                [single.ToString()],
            JsonElement =>
                Enumerable.Empty<string>(),
            string text =>
                [text],
            System.Collections.IEnumerable list =>
                list.Cast<object?>()
                    .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            _ =>
                [Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty],
        };

        return [.. values.Select(value => value.Trim()).Where(value => value.Length > 0)];
    }
}
