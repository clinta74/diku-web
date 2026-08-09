namespace DikuWeb.Domain.Entities;

/// <summary>
/// Combat and effects address players and mobs through one string space, so a combatant list
/// can hold both. An ID is a two-character kind prefix followed by the entity's GUID.
/// </summary>
/// <remarks>
/// The prefix test is deliberately ordinal. These are wire identifiers, never displayed and
/// never sorted for a reader, so culture-sensitive comparison would be both wrong and slower.
///
/// Note that IDs are built in two GUID formats across the codebase: dashed ("D", the default)
/// for combat and targeting, undashed ("N") for effect sources and map entities.
/// <see cref="ToGuid"/> reads either, and the two formats are never compared against each
/// other today, but that is a property worth preserving deliberately rather than by luck.
/// </remarks>
public static class EntityId
{
    /// <summary>Prefix marking a player character.</summary>
    public const string CharacterPrefix = "c_";

    /// <summary>Prefix marking a mob.</summary>
    public const string MobPrefix = "m_";

    private const int PrefixLength = 2;

    /// <summary>Builds a character ID in the dashed GUID form used by combat and targeting.</summary>
    public static string ForCharacter(Guid characterId) => $"{CharacterPrefix}{characterId}";

    /// <summary>Builds a mob ID in the dashed GUID form used by combat and targeting.</summary>
    public static string ForMob(Guid mobId) => $"{MobPrefix}{mobId}";

    /// <summary>Whether the ID names a player character.</summary>
    public static bool IsCharacter(string entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        return entityId.StartsWith(CharacterPrefix, StringComparison.Ordinal);
    }

    /// <summary>Whether the ID names a mob.</summary>
    public static bool IsMob(string entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        return entityId.StartsWith(MobPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Strips the prefix and parses the GUID. Accepts either GUID format. Throws on an ID that
    /// carries no recognised prefix, which would be a construction bug rather than bad input.
    /// </summary>
    public static Guid ToGuid(string entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        if (!IsCharacter(entityId) && !IsMob(entityId))
        {
            throw new ArgumentException(
                $"'{entityId}' does not start with '{CharacterPrefix}' or '{MobPrefix}'.",
                nameof(entityId));
        }

        return Guid.Parse(entityId.AsSpan(PrefixLength));
    }

    /// <summary>
    /// Whether this string is an ID <see cref="ToGuid"/> can actually read.
    /// </summary>
    /// <remarks>
    /// For the places an ID arrives from storage rather than from <see cref="ForCharacter"/> -
    /// an effect's recorded source, most of all, which round-trips through jsonb and outlives the
    /// cast that set it. <see cref="ToGuid"/> throws on a malformed one, and the combat loop is
    /// the worst possible place to find that out: a single bad ID in a hate list would take down
    /// the tick, and a dead loop is a dead world for everyone connected. Check before admitting
    /// an ID to a structure the loop iterates.
    /// </remarks>
    public static bool IsWellFormed(string? entityId) =>
        entityId is not null &&
        (entityId.StartsWith(CharacterPrefix, StringComparison.Ordinal) ||
         entityId.StartsWith(MobPrefix, StringComparison.Ordinal)) &&
        Guid.TryParse(entityId.AsSpan(PrefixLength), out _);
}
