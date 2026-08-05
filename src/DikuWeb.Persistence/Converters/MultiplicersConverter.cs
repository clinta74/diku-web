using System.Text.Json;
using DikuWeb.Domain.Worlds;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DikuWeb.Persistence.Converters;

internal sealed class MultiplicersConverter : ValueConverter<Multipliers, string>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public MultiplicersConverter()
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
                m.ItemPower,
                m.SpawnDensity,
            },
            JsonOptions);

    private static Multipliers Deserialize(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new Multipliers
            {
                Strength = GetDecimal(root, "Strength", 1.0m),
                Health = GetDecimal(root, "Health", 1.0m),
                Damage = GetDecimal(root, "Damage", 1.0m),
                Xp = GetDecimal(root, "Xp", 1.0m),
                Gold = GetDecimal(root, "Gold", 1.0m),
                ItemValue = GetDecimal(root, "ItemValue", 1.0m),
                ItemPower = GetDecimal(root, "ItemPower", 1.0m),
                SpawnDensity = GetDecimal(root, "SpawnDensity", 1.0m),
            };
        }
        catch
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
