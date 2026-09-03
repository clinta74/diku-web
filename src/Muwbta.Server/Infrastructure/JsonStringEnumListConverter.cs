using System.Text.Json;
using System.Text.Json.Serialization;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// A list of enums, on the wire as a list of names.
/// </summary>
/// <remarks>
/// <para>
/// The list-shaped counterpart to <see cref="NullableEnumConverter{T}"/>, and it exists for the
/// same reason that one does: without it an enum serialises as its integer while the browser reads
/// a string, and <c>ItemSlot.Head</c> - being 0 - is falsy the whole way through. An attribute on a
/// <c>List&lt;T&gt;</c> property has to convert the <em>list</em>, so a per-element converter cannot
/// be attached to it.
/// </para>
/// <para>
/// <b>Unknown names are dropped, not thrown on</b>, matching the nullable converter's behaviour. A
/// bundle authored against a build that knew one more slot should import the slots this build does
/// know rather than failing the whole entity - and <c>check-bundle</c> is where a name nothing
/// recognises gets reported, before any of this runs.
/// </para>
/// </remarks>
public sealed class JsonStringEnumListConverter<T> : JsonConverter<List<T>> where T : struct, Enum
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Unexpected token type {reader.TokenType} when parsing an enum list.");
        }

        var values = new List<T>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Unexpected token type {reader.TokenType} inside an enum list.");
            }

            if (Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var parsed))
            {
                values.Add(parsed);
            }
        }

        return values;
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartArray();

        foreach (var item in value)
        {
            writer.WriteStringValue(item.ToString());
        }

        writer.WriteEndArray();
    }
}
