using System.Text;
using System.Text.Json;
using Muwbta.Domain.Worlds;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Muwbta.Persistence.Converters;

/// <summary>
/// Reads and writes a <see cref="FlagSet"/> as jsonb, by hand.
/// </summary>
/// <remarks>
/// Same reason as <see cref="LegendConverter"/>: Npgsql will not serialise an arbitrary object
/// into a jsonb parameter without dynamic JSON enabled, so EF is handed a plain string.
///
/// The round trip is deliberately lossless for values the registry knows nothing about
/// (PLAN.md §4.10). A flag written by a newer binary - whatever shape it has - comes back out
/// byte-identical, because anything that is not a bool, number, or string is carried as raw
/// JSON. Silently dropping it would mean a rollback quietly erased a builder's work.
/// </remarks>
public static class FlagSetJson
{
    public static string Serialize(FlagSet? flags)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            foreach (var (key, value) in flags?.Values ?? new Dictionary<string, FlagValue>())
            {
                writer.WritePropertyName(key);

                if (value.TryAsBoolean(out var boolean))
                {
                    writer.WriteBooleanValue(boolean);
                }
                else if (value.TryAsNumber(out var number))
                {
                    writer.WriteNumberValue(number);
                }
                else if (value.TryAsText(out var text))
                {
                    writer.WriteStringValue(text);
                }
                else if (value.TryAsRawJson(out var raw))
                {
                    writer.WriteRawValue(raw, skipInputValidation: false);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static FlagSet Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new FlagSet();
        }

        var set = new FlagSet();

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return set;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                set.Set(property.Name, Read(property.Value));
            }
        }
        catch (JsonException)
        {
            // Unparseable jsonb should not stop the world loading. Every flag resolves to its
            // registry default, which is the safe value - so the room behaves as unflagged
            // rather than taking the server down at boot (PLAN.md §7.4).
            return new FlagSet();
        }

        return set;
    }

    private static FlagValue Read(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => FlagValue.Of(true),
        JsonValueKind.False => FlagValue.Of(false),
        JsonValueKind.String => FlagValue.Of(element.GetString() ?? string.Empty),
        JsonValueKind.Number when element.TryGetDouble(out var number) => FlagValue.Of(number),
        _ => FlagValue.RawJson(element.GetRawText()),
    };
}

internal sealed class FlagSetConverter() : ValueConverter<FlagSet, string>(
    flags => FlagSetJson.Serialize(flags),
    json => FlagSetJson.Deserialize(json));

/// <summary>
/// Change tracking for the converted flag set. Without it EF compares by reference and would
/// miss an edit that toggled a flag in place - the save would silently do nothing.
/// </summary>
internal sealed class FlagSetComparer() : ValueComparer<FlagSet>(
    (left, right) => left != null && left.ContentEquals(right),
    flags => Hash(flags),
    flags => flags.Clone())
{
    private static int Hash(FlagSet? flags)
    {
        if (flags is null)
        {
            return 0;
        }

        // Order-independent: dictionary enumeration order is not guaranteed.
        var hash = 0;
        foreach (var (key, value) in flags.Values)
        {
            hash ^= HashCode.Combine(key, value);
        }

        return hash;
    }
}
