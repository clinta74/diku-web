using System.Text.Json;
using Muwbta.Domain.Worlds;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Muwbta.Persistence.Converters;

internal sealed class MultipliersConverter : ValueConverter<Multipliers, string>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public MultipliersConverter()
        : base(
            m => Serialize(m),
            json => Deserialize(json))
    {
    }

    private static string Serialize(Multipliers m) =>
        JsonSerializer.Serialize(
            new
            {
                m.Strength,
                m.Health,
                m.Damage,
                m.Xp,
                m.Gold,
                m.ItemValue,
            },
            JsonOptions);

    /// <summary>
    /// Reads each multiplier independently so a column written before a multiplier existed
    /// still loads, with the missing ones defaulting to 1.0. A column that is not a JSON
    /// object at all falls back wholesale, which is the same result as an empty one.
    /// </summary>
    private static Multipliers Deserialize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Multipliers();
            }

            return new Multipliers
            {
                Strength = GetDecimal(root, "Strength", 1.0m),
                Health = GetDecimal(root, "Health", 1.0m),
                Damage = GetDecimal(root, "Damage", 1.0m),
                Xp = GetDecimal(root, "Xp", 1.0m),
                Gold = GetDecimal(root, "Gold", 1.0m),
                ItemValue = GetDecimal(root, "ItemValue", 1.0m),
            };
        }
        catch (JsonException)
        {
            return new Multipliers();
        }
    }

    private static decimal GetDecimal(JsonElement element, string propertyName, decimal defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.TryGetDecimal(out var value))
        {
            return value;
        }

        return defaultValue;
    }
}
