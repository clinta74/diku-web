using System.Text.Json;
using System.Text.Json.Serialization;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// Custom JSON converter for nullable enums that gracefully handles null, empty strings, and invalid values.
/// </summary>
public sealed class NullableEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                var stringValue = reader.GetString();
                if (string.IsNullOrEmpty(stringValue))
                {
                    return null;
                }

                if (Enum.TryParse<T>(stringValue, ignoreCase: true, out var result))
                {
                    return result;
                }

                // Invalid enum value - return null instead of throwing
                return null;

            default:
                throw new JsonException($"Unexpected token type {reader.TokenType} when parsing nullable enum.");
        }
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value.ToString());
        }
    }
}
