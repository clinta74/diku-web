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
    /// Apply regen to all characters in the world.
    /// Returns the number of characters who regained vitals.
    /// </summary>
    public static int Tick(WorldState world)
    {
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
                vitalityModifier);

            if (regenApplied)
            {
                count++;
            }
        }

        return count;
    }
}
