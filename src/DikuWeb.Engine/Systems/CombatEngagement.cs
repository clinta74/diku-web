using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Puts a character and a mob into a fight with each other.
/// </summary>
/// <remarks>
/// Extracted from <c>kill</c> because three things now start fights - the verb, a taunt, and a
/// damaging ability landing on something not yet engaged - and each of them has to do the same
/// six steps. Written out three times they would drift, and the way they would drift is silent:
/// forget the seed hate and the mob stands there; forget <c>AddCombatant</c> and it never gets a
/// hate list at all, so <c>AddToHateList</c> is a no-op and every point of threat after it
/// vanishes.
/// </remarks>
public static class CombatEngagement
{
    /// <summary>
    /// The threat a fight opens with, before anyone has landed anything.
    /// </summary>
    /// <remarks>
    /// One, not zero: <c>GetTopHater</c> returns null for an empty list, so a mob with no threat
    /// recorded has no target and does not swing. This is what makes it fight back from the
    /// moment it is engaged rather than from the moment it is first hurt - which matters more now
    /// that weapons swing on their own clocks and a slow opener can leave a long gap.
    /// </remarks>
    public const int OpeningThreat = 1;

    /// <summary>
    /// Engages <paramref name="character"/> and <paramref name="mob"/>, and returns the fight.
    /// </summary>
    /// <param name="retarget">
    /// Whether to point the character's own attacks at this mob. True for <c>kill</c>, where
    /// choosing a target is the whole command. False for a taunt or a stray area effect, which
    /// change what the <em>mob</em> is doing and must not silently swing the player's sword
    /// around at something they did not choose.
    /// </param>
    public static Combat Engage(
        WorldState world,
        Character character,
        Mob mob,
        bool retarget = true)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(mob);

        var combat = world.GetOrCreateCombat(character.RoomKey);

        var attackerId = EntityId.ForCharacter(character.Id);
        var targetId = EntityId.ForMob(mob.Id);

        combat.AddCombatant(attackerId);
        combat.AddCombatant(targetId);

        character.CombatState = CombatState.Fighting;
        mob.CombatState = CombatState.Fighting;

        if (retarget)
        {
            character.CurrentTarget = targetId;
            combat.PlayerTargets[character.Id] = targetId;
        }

        // Only ever a floor. Adding it unconditionally would hand a point of threat to whoever
        // re-engaged last, which is a way to steal a mob by walking in and out of a fight.
        if (combat.HateOf(targetId, attackerId) == 0)
        {
            combat.AddToHateList(targetId, attackerId, OpeningThreat);
        }

        return combat;
    }
}
