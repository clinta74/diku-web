using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Whether a room can be seen (PLAN.md §4.10, the <c>dark</c> flag).
/// </summary>
/// <remarks>
/// <b>Light belongs to the room, not to the viewer.</b> Anyone standing in a lit room can see, so
/// one player with a lantern lights it for the party — which is what makes carrying one a thing a
/// group organises rather than a thing five people each buy. It is also why this takes a room and
/// not a character.
///
/// <b>Worn or wielded only.</b> A lamp in the pack is a lamp you have not taken out. Any slot
/// counts, so a helm with a lamp on it and a pendant that glows both work, and no hand has to be
/// reserved for light (<see cref="Domain.Items.ItemTemplate.IsLightSource"/>).
///
/// <b>Mobs carry nothing.</b> A mob's inventory is loot rather than equipment, so a room with only
/// mobs in it is dark. That is deliberate: a lit room whose light is standing in the corner
/// waiting to be killed would be a light source a player cannot take with them.
/// </remarks>
public static class RoomLight
{
    /// <summary>
    /// Whether this room is dark to everyone in it right now.
    /// </summary>
    /// <remarks>
    /// The flag is asked first and the answer is almost always no, which matters:
    /// <see cref="WorldState.EquipmentOf"/> scans every item in the world, so the occupant sweep
    /// below is only affordable because the rooms that reach it are the handful flagged dark.
    /// </remarks>
    public static bool IsDark(WorldState world, ItemTemplateCache? templates, RoomKey roomKey)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!world.IsFlagSet(roomKey, RoomFlags.Dark))
        {
            return false;
        }

        foreach (var occupant in world.OccupantsOf(roomKey))
        {
            if (CarriesLight(world, templates, occupant.CharacterId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether this character has a light source worn or wielded.</summary>
    public static bool CarriesLight(WorldState world, ItemTemplateCache? templates, Guid characterId)
    {
        ArgumentNullException.ThrowIfNull(world);

        return world.EquipmentOf(characterId)
            .Any(item => ItemRules.IsLightSource(templates, item.TemplateKey));
    }
}
