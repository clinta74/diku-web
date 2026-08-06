using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;
using DomainCombatSystem = DikuWeb.Domain.Combat.CombatSystem;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Combat tick system: 8 pulses = 2 seconds per round.
/// Runs attack resolution, damage application, and combat cleanup.
/// </summary>
public sealed class CombatSystem(EngineOptions options, IMobTemplateRepository? mobTemplates = null, IItemTemplateRepository? itemTemplates = null, ItemSpawner? itemSpawner = null)
{
    public const int TickIntervalPulses = 8; // 2 seconds at 250 ms/pulse

    /// <summary>
    /// Process all active combats for one round.
    /// Returns the number of characters/mobs involved in combat this round.
    /// </summary>
    public int Tick(WorldState world)
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

            // Handle death and remove dead combatants
            var deadCombatants = new List<string>();
            foreach (var combatantId in combat.Combatants)
            {
                bool isDead = false;
                if (combatantId.StartsWith("c_"))
                {
                    var charId = Guid.Parse(combatantId.Substring(2));
                    var character = world.GetCharacter(charId);
                    if (character?.Vitals.Health <= 0)
                    {
                        isDead = true;
                        HandleCharacterDeath(world, combat, character, combatantId);
                    }
                }
                else if (combatantId.StartsWith("m_"))
                {
                    var mobId = Guid.Parse(combatantId.Substring(2));
                    var mob = world.GetMob(mobId);
                    if (mob?.Vitals.Health <= 0)
                    {
                        isDead = true;
                        HandleMobDeath(world, combat, mob, combatantId);
                    }
                }
                if (isDead)
                {
                    deadCombatants.Add(combatantId);
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

    private void ResolveAttack(WorldState world, Combat combat, string attackerId)
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

        // Resolve attacker and target names/types for narration and validation
        var (attackerType, attackerName, attackerActo) = ResolveCombatantInfo(world, attackerId);
        var (targetType, targetName, targetActor) = ResolveCombatantInfo(world, targetId);
        if (attackerType == null || targetType == null)
            return;

        // Check target validity (peaceful, pvp, etc.)
        bool peaceful = world.IsFlagSet(combat.RoomKey, RoomFlags.Peaceful);
        bool pvp = world.IsFlagSet(combat.RoomKey, RoomFlags.Pvp);
        var validation = TargetValidator.ValidateTarget(attackerType.Value, targetType.Value, targetName, peaceful, pvp);

        if (!validation.IsAllowed)
        {
            // Refusal: send to attacker if player, end their engagement
            if (attackerActo is PlayerActor attackerPlayer)
            {
                attackerPlayer.SendText(validation.RefusalReason ?? "The attack is not allowed.", "bad");
            }
            combat.RemoveCombatant(attackerId);
            if (attackerId.StartsWith("c_"))
            {
                var charId = Guid.Parse(attackerId.Substring(2));
                var character = world.GetCharacter(charId);
                if (character != null)
                {
                    character.CombatState = CombatState.Idle;
                    character.CurrentTarget = null;
                }
            }
            return;
        }

        // Get combatants' stats
        var (attacker, defender) = GetCombatantPair(world, attackerId, targetId);
        if (attacker == null || defender == null)
            return;

        // Use Domain combat system to execute the round (includes narration)
        int targetHealth = targetId.StartsWith("c_")
            ? world.GetCharacter(Guid.Parse(targetId.Substring(2)))?.Vitals.Health ?? 0
            : world.GetMob(Guid.Parse(targetId.Substring(2)))?.Vitals.Health ?? 0;

        var round = DomainCombatSystem.ExecuteRound(
            attackerType.Value,
            attackerName,
            attacker,
            targetType.Value,
            targetName,
            defender,
            targetHealth,
            validation,
            world.Random);

        // Send narration
        if (attackerActo is PlayerActor player)
            player.SendText(round.AttackerNarration, "combat");

        if (targetActor is PlayerActor targetPlayer)
            targetPlayer.SendText(round.TargetNarration, "combat");

        foreach (var occupant in world.OccupantsOf(combat.RoomKey))
        {
            if (occupant.CharacterId != (attackerActo as PlayerActor)?.CharacterId &&
                occupant.CharacterId != (targetActor as PlayerActor)?.CharacterId)
            {
                occupant.SendText(round.RoomNarration, "combat");
            }
        }

        // Apply damage if hit
        if (round.Damage.Hit)
        {
            ApplyDamage(world, targetId, round.Damage.DamageDealt);

            // Add to hate list if target is a mob
            if (targetId.StartsWith("m_"))
            {
                combat.AddToHateList(targetId, attackerId, round.Damage.DamageDealt);
            }
        }
    }

    /// <summary>
    /// Resolves a combatant's type, name, and actor reference for display and narration.
    /// </summary>
    private (CombatantType?, string, object?) ResolveCombatantInfo(WorldState world, string entityId)
    {
        if (entityId.StartsWith("c_"))
        {
            var charId = Guid.Parse(entityId.Substring(2));
            var actor = world.FindByCharacter(charId);
            if (actor != null)
                return (CombatantType.Player, actor.Name, actor);
        }
        else if (entityId.StartsWith("m_"))
        {
            var mobId = Guid.Parse(entityId.Substring(2));
            var mob = world.FindMob(mobId);
            if (mob != null)
                return (CombatantType.Mob, mob.TemplateKey, mob);
        }
        return (null, "", null);
    }

    private (AttackerStats?, DefenderStats?) GetCombatantPair(
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

    private void ApplyDamage(WorldState world, string targetId, int damage)
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

    private bool IsCombatActive(Combat combat)
    {
        // Combat is active if there are at least 2 combatants (allows PvP and PvE)
        return combat.Combatants.Count >= 2;
    }

    private void HandleCharacterDeath(WorldState world, Combat combat, Character character, string combatantId)
    {
        var actor = world.FindByCharacter(character.Id);
        if (actor == null) return;

        // Determine if this was a PvP kill
        var killerType = CombatantType.Mob;
        foreach (var other in combat.Combatants)
        {
            if (other == combatantId) continue;
            if (other.StartsWith("c_"))
            {
                killerType = CombatantType.Player;
                break;
            }
        }

        bool isPvpDeath = killerType == CombatantType.Player;

        // Apply XP loss (PLAN.md §4.12)
        if (character.Level >= options.XpLossMinLevel && (!isPvpDeath || options.PvpCostsXp))
        {
            var xpBand = DikuWeb.Domain.Characters.XpProgression.XpForLevel(character.Level + 1) -
                         DikuWeb.Domain.Characters.XpProgression.XpForLevel(character.Level);
            var xpLoss = (long)Math.Round(xpBand * options.XpLossPercent);
            var levelThreshold = DikuWeb.Domain.Characters.XpProgression.XpForLevel(character.Level);
            character.Xp = Math.Max(levelThreshold, character.Xp - xpLoss);
        }

        // Resolve respawn location
        var respawnRoom = character.RespawnRoomKey ?? options.StartingRoom;
        if (world.FindRoom(respawnRoom) == null)
            respawnRoom = options.StartingRoom;

        // Move and reset vitals
        world.Move(actor, respawnRoom);
        character.Vitals.Health = Math.Max(1, (int)(character.Vitals.HealthMax * options.RespawnHealthPercent));
        character.Vitals.Focus = 0;
        character.Vitals.Stamina = 0;
        character.CombatState = CombatState.Idle;
        character.CurrentTarget = null;

        // Narrate at death location and respawn location
        var deathRoom = combat.RoomKey;
        foreach (var occupant in world.OccupantsOf(deathRoom))
        {
            if (occupant.CharacterId != character.Id)
                occupant.SendText($"{actor.Name} falls.", "death");
        }
        actor.SendText($"You died. Respawned at {respawnRoom}.", "death");
        foreach (var occupant in world.OccupantsOf(respawnRoom))
        {
            if (occupant.CharacterId != character.Id)
                occupant.SendText($"{actor.Name} appears.", "arrival");
        }
    }

    private void HandleMobDeath(WorldState world, Combat combat, Mob mob, string combatantId)
    {
        // Find the top damager (killer)
        var killerId = combat.GetTopHater(combatantId);
        if (killerId == null) return;

        // Extract killer's character if it's a player
        Character? killerChar = null;
        if (killerId.StartsWith("c_"))
        {
            var killCharId = Guid.Parse(killerId.Substring(2));
            killerChar = world.GetCharacter(killCharId);
        }

        // Award XP to killer
        if (killerChar != null)
        {
            killerChar.Xp += mob.ResolvedXp;
            // Level up loop
            while (DikuWeb.Domain.Characters.CharacterProgression.TryLevelUp(
                killerChar.Level, killerChar.Xp, killerChar.Attributes, killerChar.Path, killerChar.Vitals) is var result && result != null)
            {
                killerChar.Level = result.NewLevel;
                killerChar.Attributes = result.NewAttributes;
                killerChar.Vitals = result.NewVitals;
                var actor = world.FindByCharacter(killerChar.Id);
                if (actor != null)
                    actor.SendText($"You advance to level {result.NewLevel}!", "levelup");
            }

            // Award gold
            killerChar.Gold += mob.ResolvedGold;
        }

        // Roll loot (basic: just roll against each entry's chance)
        if (mobTemplates != null && itemTemplates != null && itemSpawner != null)
        {
            // Note: This would need async support; for MVP, skip loot rolling
            // TODO: implement loot rolling via ItemSpawner
        }

        // Remove mob from world
        world.RemoveMob(mob);
    }

    private void EndCombatFor(WorldState world, string combatantId)
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
