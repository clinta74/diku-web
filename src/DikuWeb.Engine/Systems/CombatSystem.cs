using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Combat tick system: 8 pulses = 2 seconds per round.
/// Runs attack resolution, damage application, and combat cleanup.
/// </summary>
public static class CombatSystem
{
    public const int TickIntervalPulses = 8; // 2 seconds at 250 ms/pulse

    /// <summary>
    /// Process all active combats for one round.
    /// Returns the number of characters/mobs involved in combat this round.
    /// </summary>
    public static int Tick(WorldState world)
    {
        var combatCount = 0;

        // Get all rooms with active combats
        var roomsWithCombat = world.AllCombats.Where(c => c.Combatants.Count > 0).ToList();

        foreach (var combat in roomsWithCombat)
        {
            // Process each combatant's attack
            var combatantsThisRound = new List<string>(combat.Combatants);
            foreach (var combatantId in combatantsThisRound)
            {
                ResolveAttack(world, combat, combatantId);
            }

            // Remove dead combatants
            var deadCombatants = new List<string>();
            foreach (var combatantId in combat.Combatants)
            {
                if (combatantId.StartsWith("c_"))
                {
                    var charId = Guid.Parse(combatantId.Substring(2));
                    var character = world.GetCharacter(charId);
                    if (character?.Vitals.Health <= 0)
                    {
                        deadCombatants.Add(combatantId);
                    }
                }
                else if (combatantId.StartsWith("m_"))
                {
                    var mobId = Guid.Parse(combatantId.Substring(2));
                    var mob = world.GetMob(mobId);
                    if (mob?.Vitals.Health <= 0)
                    {
                        deadCombatants.Add(combatantId);
                    }
                }
            }

            foreach (var deadId in deadCombatants)
            {
                combat.RemoveCombatant(deadId);
            }

            // Check if combat should end (only one side left)
            if (!IsCombatActive(combat))
            {
                // End combat for all remaining combatants
                foreach (var combatantId in combat.Combatants)
                {
                    EndCombatFor(world, combatantId);
                }
                combat.Combatants.Clear();
            }

            combatCount += combat.Combatants.Count;
        }

        // Increment round numbers
        foreach (var combat in roomsWithCombat.Where(c => c.Combatants.Count > 0))
        {
            combat.RoundNumber++;
        }

        return combatCount;
    }

    private static void ResolveAttack(WorldState world, Combat combat, string attackerId)
    {
        // Determine target
        string? targetId = null;

        if (attackerId.StartsWith("c_"))
        {
            // Player: use their chosen target
            var charId = Guid.Parse(attackerId.Substring(2));
            if (combat.PlayerTargets.TryGetValue(charId, out var target))
            {
                targetId = target;
            }
        }
        else if (attackerId.StartsWith("m_"))
        {
            // Mob: use top hater
            targetId = combat.GetTopHater(attackerId);
        }

        if (string.IsNullOrEmpty(targetId) || targetId == attackerId)
            return;

        // Get combatants' stats
        var (attacker, defender) = GetCombatantPair(world, attackerId, targetId);
        if (attacker == null || defender == null)
            return;

        // Resolve attack
        var result = DamageCalculator.CalculateDamage(attacker, defender, world.Random);

        if (result.Hit)
        {
            // Apply damage
            ApplyDamage(world, targetId, result.DamageDealt);

            // Add to hate list if target is a mob
            if (targetId.StartsWith("m_"))
            {
                combat.AddToHateList(targetId, attackerId, result.DamageDealt);
            }
        }
    }

    private static (AttackerStats?, DefenderStats?) GetCombatantPair(
        WorldState world,
        string attackerId,
        string targetId)
    {
        AttackerStats? attacker = null;
        DefenderStats? defender = null;

        // Resolve attacker
        if (attackerId.StartsWith("c_"))
        {
            var charId = Guid.Parse(attackerId.Substring(2));
            var character = world.GetCharacter(charId);
            if (character != null)
                attacker = DamageCalculator.StatsFrom(character);
        }
        else if (attackerId.StartsWith("m_"))
        {
            var mobId = Guid.Parse(attackerId.Substring(2));
            var mob = world.GetMob(mobId);
            if (mob != null)
                attacker = DamageCalculator.StatsFrom(mob);
        }

        // Resolve defender
        if (targetId.StartsWith("c_"))
        {
            var charId = Guid.Parse(targetId.Substring(2));
            var character = world.GetCharacter(charId);
            if (character != null)
                defender = DamageCalculator.DefenderStatsFrom(character);
        }
        else if (targetId.StartsWith("m_"))
        {
            var mobId = Guid.Parse(targetId.Substring(2));
            var mob = world.GetMob(mobId);
            if (mob != null)
                defender = DamageCalculator.DefenderStatsFrom(mob);
        }

        return (attacker, defender);
    }

    private static void ApplyDamage(WorldState world, string targetId, int damage)
    {
        if (targetId.StartsWith("c_"))
        {
            var charId = Guid.Parse(targetId.Substring(2));
            var character = world.GetCharacter(charId);
            if (character != null)
            {
                character.Vitals.Health = Math.Max(0, character.Vitals.Health - damage);
            }
        }
        else if (targetId.StartsWith("m_"))
        {
            var mobId = Guid.Parse(targetId.Substring(2));
            var mob = world.GetMob(mobId);
            if (mob != null)
            {
                mob.Vitals.Health = Math.Max(0, mob.Vitals.Health - damage);
            }
        }
    }

    private static bool IsCombatActive(Combat combat)
    {
        // Combat is active if there are combatants from more than one "side"
        // Simple heuristic: at least one player or one mob on each side
        var hasPlayer = combat.Combatants.Any(c => c.StartsWith("c_"));
        var hasMob = combat.Combatants.Any(c => c.StartsWith("m_"));
        return combat.Combatants.Count > 1 && hasPlayer && hasMob;
    }

    private static void EndCombatFor(WorldState world, string combatantId)
    {
        if (combatantId.StartsWith("c_"))
        {
            var charId = Guid.Parse(combatantId.Substring(2));
            var character = world.GetCharacter(charId);
            if (character != null)
            {
                character.CombatState = CombatState.Idle;
                character.CurrentTarget = null;
            }
        }
        else if (combatantId.StartsWith("m_"))
        {
            var mobId = Guid.Parse(combatantId.Substring(2));
            var mob = world.GetMob(mobId);
            if (mob != null)
            {
                mob.CombatState = CombatState.Idle;
                mob.CurrentTarget = null;
            }
        }
    }
}
