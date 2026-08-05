using DikuWeb.Domain.Worlds;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DikuWeb.Persistence.Converters;

/// <summary>
/// Stores <see cref="RoomKey"/> as its "world.zone.room" text form. The column stays a plain
/// varchar, so nothing about the schema changes - only the CLR type gains its validation.
/// </summary>
internal sealed class RoomKeyConverter() : ValueConverter<RoomKey, string>(
    key => key.ToString(),
    value => RoomKey.Parse(value));

/// <summary>
/// Nullable variant for optional locations.
/// </summary>
internal sealed class NullableRoomKeyConverter() : ValueConverter<RoomKey?, string?>(
    key => key == null ? null : key.Value.ToString(),
    value => value == null ? null : RoomKey.Parse(value));
