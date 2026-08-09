using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Whether a player may do something hostile to a target here (PLAN.md §4.11).
/// </summary>
/// <remarks>
/// One gate for every hostile action, because there is more than one way to start a fight and
/// they were not agreeing. <c>kill</c> refused in a <c>peaceful</c> room, refused a non-combatant
/// outright, and checked <c>pvp</c> before touching another player. <c>cast</c> checked none of
/// the three — so a Bolt in a safe room worked, and an Adept could kill the shopkeeper who hands
/// out the zone's quests from a standing start.
///
/// Area effects had already grown their own copy of these rules, because gathering targets and
/// refusing them are the same question asked over a set instead of over one thing. That copy now
/// answers through here as well, so there is one place where "may I hit this" is decided and one
/// place to change when the answer does.
/// </remarks>
public static class HostileActionGate
{
    /// <summary>
    /// Why this mob may not be attacked, or null if it may be.
    /// </summary>
    public static string? RefuseMob(
        WorldState world,
        MobTemplateCache? templates,
        RoomKey roomKey,
        Mob mob)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mob);

        var displayName = string.IsNullOrEmpty(mob.TemplateName) ? mob.TemplateKey : mob.TemplateName;

        // Checked before the room rules, because an NPC is unattackable everywhere rather than
        // merely in peaceful rooms. Quest givers, turn-ins, and shopkeepers are all NPCs, and
        // killing one strands whoever was mid-quest until the spawner comes back around (§7.4).
        if (MobBehavior.IsNonCombatant(templates?.Get(mob.TemplateKey)?.Behavior))
        {
            return $"{NarrationHelper.WithDefiniteArticle(displayName, capitalize: true)} " +
                   "is not someone you can fight.";
        }

        var validation = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Mob,
            displayName,
            world.IsFlagSet(roomKey, RoomFlags.Peaceful),
            world.IsFlagSet(roomKey, RoomFlags.Pvp));

        return validation.IsAllowed ? null : validation.RefusalReason ?? "You cannot attack that.";
    }

    /// <summary>
    /// Why this player may not be attacked, or null if they may be.
    /// </summary>
    public static string? RefusePlayer(WorldState world, RoomKey roomKey, string targetName)
    {
        ArgumentNullException.ThrowIfNull(world);

        var validation = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Player,
            targetName,
            world.IsFlagSet(roomKey, RoomFlags.Peaceful),
            world.IsFlagSet(roomKey, RoomFlags.Pvp));

        return validation.IsAllowed ? null : validation.RefusalReason ?? "You cannot attack that.";
    }

    /// <summary>
    /// Whether this mob may be swept up by an area effect cast in <paramref name="roomKey"/>.
    /// </summary>
    /// <remarks>
    /// The same question as <see cref="RefuseMob"/>, asked without a reason — an area effect
    /// narrates once for the cast, not once per thing it declined to burn.
    /// </remarks>
    public static bool MayStrikeMob(
        WorldState world,
        MobTemplateCache? templates,
        RoomKey roomKey,
        Mob mob) =>
        RefuseMob(world, templates, roomKey, mob) is null;
}
