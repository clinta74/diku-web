using System.Text.Json;
using System.Text.Json.Serialization;
using Muwbta.Domain.Spawning;

namespace Muwbta.Persistence.Converters;

/// <summary>
/// Converts TemplateKind enum to/from JSON strings instead of numbers.
/// Allows the frontend to send enum values as strings like "Mob" and "Item".
/// </summary>
public sealed class TemplateKindConverter : JsonConverter<TemplateKind>
{
    public override TemplateKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Unexpected token {reader.TokenType} when parsing enum.");
        }

        var value = reader.GetString();
        return value switch
        {
            "Mob" => TemplateKind.Mob,
            "Item" => TemplateKind.Item,
            _ => throw new JsonException($"Unknown TemplateKind value: {value}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, TemplateKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
