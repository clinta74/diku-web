using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Whether a character may pass through a conditional exit — a locked door, or a gate between
/// Reaches (PLAN.md §4.15).
/// </summary>
/// <remarks>
/// <b>One place, because there will be a second caller.</b> Movement is the only walking path
/// today, but the reason <c>noRecall</c> needed a test per reader is that a later verb landing and
/// forgetting to ask is how a restriction becomes a lie. Anything that carries a character along an
/// exit asks here.
///
/// <b>Fails closed on a requirement it cannot satisfy, including one that makes no sense.</b> An
/// exit naming a flag no quest grants, or an item template a builder has since deleted, refuses.
/// That is the opposite of how an unrecognised <em>room</em> flag behaves (§4.10), and deliberately
/// so: a room flag defaults to the harmless value because forgetting it must not be dangerous,
/// whereas here the dangerous direction is *open*. A wall players cannot pass is reported within
/// the hour; a gate to the endgame that swung open for everyone is reported never.
/// </remarks>
public static class ExitGate
{
    /// <summary>
    /// What a character is told when an exit refuses them and its author wrote nothing. Vague on
    /// purpose: the fiction of any particular door is the author's to supply, and a default that
    /// guessed — "It is locked." — would be wrong for every gate that is not a door.
    /// </summary>
    public const string GenericRefusal = "Something here will not let you pass.";

    /// <summary>
    /// Why this character cannot use this exit, or null if they can.
    /// </summary>
    public static string? Refuse(WorldState world, Character character, RoomExit exit)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(exit);

        if (!exit.IsConditional)
        {
            return null;
        }

        if (exit.RequiredFlagKey is { } flag && !character.HasFlag(flag))
        {
            return exit.RefusalMessage ?? GenericRefusal;
        }

        // Held, never consumed - a key that dissolved on first use would be a toll, and this is
        // not one. Equipped items count: WorldState.InventoryOf is ownership rather than what is
        // loose in the pack, so a sword drawn is still a sword carried.
        if (exit.RequiredItemKey is { } item && !Carries(world, character.Id, item))
        {
            return exit.RefusalMessage ?? GenericRefusal;
        }

        return null;
    }

    private static bool Carries(WorldState world, Guid characterId, string templateKey) =>
        world.InventoryOf(characterId)
            .Any(i => string.Equals(i.TemplateKey, templateKey, StringComparison.Ordinal));
}
