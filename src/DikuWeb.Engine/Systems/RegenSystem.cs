using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Runs every 60 seconds (240 pulses). Applies vital regeneration based on character
/// rest state and Vitality attribute.
/// Note: Combat auto-transitions to Stand state will be integrated in Phase 4.
/// </summary>
public static class RegenSystem
{
    public const long TickIntervalPulses = 240; // 60 seconds at 250 ms/pulse

    /// <summary>
    /// Apply regen to everything in the world that has vitals.
    /// Returns the number of entities that regained any.
    /// </summary>
    /// <remarks>
    /// Characters and mobs both, since mobs stopped keeping their wounds forever (PLAN.md §4.6).
    /// The count is discarded at the only call site, so it means "entities" rather than "characters"
    /// at no cost to anyone.
    /// </remarks>
    public static int Tick(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var count = 0;

        foreach (var actor in world.AllPlayers)
        {
            var character = actor.Character;

            // Skip regen while fighting (PLAN.md §4.5)
            if (character.CombatState == CombatState.Fighting)
            {
                continue;
            }

            var vitalityModifier = character.Attributes.VitalityModifier;

            var regenApplied = RegenCalculator.ApplyRegen(
                character.RestState,
                character.Vitals,
                vitalityModifier,
                character.Path);

            if (regenApplied)
            {
                count++;
            }
        }

        count += TickMobs(world);

        return count;
    }

    /// <summary>
    /// Wounded mobs trend back to full while they are not fighting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mostly redundant, deliberately.</b> <see cref="DikuWeb.Domain.Inhabitants.Mob.Disengage"/>
    /// already restores a mob that leaves a fight alive, so in ordinary play a mob is whole before
    /// this ever sees it. That one is the behaviour a player feels; this is the invariant underneath
    /// it. Neither replaces the other: a rule kept only by healing at every exit is a promise
    /// enforced by remembering to find them all, and any disengage path missed today or added later
    /// leaves a mob wounded for the life of the process — which is exactly how this bug existed
    /// (BUGS.md #25).
    /// </para>
    /// <para>
    /// <b>Health only, and at the standing rate.</b> A mob has no rest state and no Path, and nothing
    /// reads its focus or stamina — see <see cref="RegenCalculator.HealthFor"/>. Zero vitality
    /// modifier, because a mob's attributes are resolved into its damage rather than into how fast it
    /// heals up between fights.
    /// </para>
    /// <para>
    /// Skips the dead as well as the fighting. Health is the death test everywhere else in combat,
    /// and a corpse still standing in a room while the sweep runs must not be topped back up.
    /// </para>
    /// </remarks>
    private static int TickMobs(WorldState world)
    {
        var count = 0;

        foreach (var mob in world.AllMobs)
        {
            if (mob.CombatState == CombatState.Fighting || mob.Vitals.Health <= 0)
            {
                continue;
            }

            var regen = RegenCalculator.HealthFor(
                CharacterRestState.Stand,
                mob.Vitals,
                vitalityModifier: 0);

            var before = mob.Vitals.Health;
            mob.Vitals.Health = Math.Min(mob.Vitals.Health + regen, mob.Vitals.HealthMax);

            if (mob.Vitals.Health != before)
            {
                count++;
            }
        }

        return count;
    }
}
