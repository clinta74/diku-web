using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Credits damage to a mob's hate list, whatever delivered it (PLAN.md §4.2).
/// </summary>
/// <remarks>
/// A mob picks its target with <c>GetTopHater</c>, which reads cumulative damage - so the hate
/// list is already a damage meter and already reorders itself when someone out-damages the
/// current top. What it was missing was most of the damage.
///
/// Only landed melee swings called <c>AddToHateList</c>. Ability damage went straight into
/// <c>Vitals.Health</c> from <c>AbilitySystem</c>, and damage-over-time ticks went straight in
/// from the combat loop, neither touching the list. The result inverted the design: <b>the Adept,
/// the Path built to deal the largest damage in the game, was the one Path that could never pull
/// a mob off anyone.</b> Cataclysm could take three quarters of a boss's health and leave the
/// boss still chewing on the Warden who had scratched it twice.
///
/// Every point of damage now counts once, through here, so "whoever hurts it most gets hit" is
/// true of the whole game rather than of one damage source.
/// </remarks>
public static class ThreatCredit
{
    /// <summary>
    /// Credits <paramref name="damage"/> against <paramref name="targetId"/> to
    /// <paramref name="attackerId"/>, if the target is a mob and a fight is under way.
    /// </summary>
    /// <remarks>
    /// Silent when there is no combat in the room rather than creating one. Starting a fight is a
    /// decision with narration, engagement timing, and a target attached, and it belongs to the
    /// caller that knows why the damage happened - see <see cref="CombatEngagement"/>.
    /// </remarks>
    public static void Credit(
        WorldState world,
        RoomKey roomKey,
        string attackerId,
        string targetId,
        int damage)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Zero is not a no-op worth recording, and a negative would hand the target's own hate
        // list back to them.
        if (damage <= 0 || !EntityId.IsMob(targetId) || string.IsNullOrEmpty(attackerId))
        {
            return;
        }

        // A mob does not hate itself for its own damage-over-time, and a mob hurting another mob
        // is not a case the hate list is written for.
        //
        // Well-formed is checked as well as prefixed, because an effect's source survives a jsonb
        // round trip and outlives the cast that set it. Admitting an unparseable ID here would
        // make it the mob's top hater, and the next tick would try to find that entity's room and
        // throw inside the combat loop - a dead loop is a dead world for everyone connected.
        if (!EntityId.IsCharacter(attackerId) || !EntityId.IsWellFormed(attackerId))
        {
            return;
        }

        world.FindCombat(roomKey)?.AddToHateList(targetId, attackerId, damage);
    }

    /// <summary>
    /// Credits a damage-over-time tick to whoever applied the effect, not to whoever is standing
    /// nearby.
    /// </summary>
    /// <remarks>
    /// <see cref="Domain.Abilities.Effects.ActiveEffect.SourceEntityId"/> is the only record of
    /// who is responsible once the cast is over - the caster may have fled the room, and the
    /// bleed keeps working either way. Crediting the victim's own list to nobody would make a
    /// Shade's Ambush, which is most of that Path's damage, worth no threat at all.
    /// </remarks>
    public static void CreditTick(
        WorldState world,
        RoomKey roomKey,
        string? sourceEntityId,
        string targetId,
        int damage) =>
        Credit(world, roomKey, sourceEntityId ?? string.Empty, targetId, damage);
}
