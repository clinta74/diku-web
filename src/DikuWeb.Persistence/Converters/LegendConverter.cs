using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DikuWeb.Persistence.Converters;

/// <summary>
/// Serialises a room's glyph legend to jsonb by hand.
/// </summary>
/// <remarks>
/// Npgsql 8+ refuses to write an arbitrary Dictionary to a jsonb parameter unless dynamic
/// JSON serialisation is explicitly enabled, because that path needs unbounded reflection and
/// breaks trimming and AOT. Converting to a string here is the narrower fix: EF hands Npgsql
/// a plain string, which it already knows how to write to jsonb.
///
/// The Character stat blocks avoid this entirely by using OwnsOne(...).ToJson(), where EF
/// builds the JSON itself.
/// </remarks>
internal sealed class LegendConverter() : ValueConverter<Dictionary<string, string>, string>(
    legend => JsonSerializer.Serialize(legend, (JsonSerializerOptions?)null),
    json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null)
            ?? new Dictionary<string, string>());

/// <summary>
/// Change tracking for the converted dictionary. Without this EF compares by reference and
/// would miss an edit that mutated the legend in place.
/// </summary>
internal sealed class LegendComparer() : ValueComparer<Dictionary<string, string>>(
    (left, right) => Equivalent(left, right),
    legend => Hash(legend),
    legend => new Dictionary<string, string>(legend, StringComparer.Ordinal))
{
    private static bool Equivalent(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int Hash(Dictionary<string, string>? legend)
    {
        if (legend is null)
        {
            return 0;
        }

        // Order-independent, because dictionary enumeration order is not guaranteed.
        var hash = 0;
        foreach (var (key, value) in legend)
        {
            hash ^= HashCode.Combine(key, value);
        }

        return hash;
    }
}
