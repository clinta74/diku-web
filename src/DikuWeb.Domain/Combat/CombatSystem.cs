using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Combat;

/// <summary>
/// Executes one round of combat: validates the engagement, rolls an attack, applies damage,
/// and produces narration for the Engine layer (PLAN.md §4.3).
///
/// This is a pure function taking all necessary combatant stats, flags, and randomness.
/// The Engine layer applies the results to the world and sends narration to players.
/// </summary>
public static class CombatSystem
{
    /// <summary>
    /// Executes one combat round between an attacker and target.
    /// </summary>
    /// <param name="attacker">The attacking combatant's type (Player or Mob).</param>
    /// <param name="attackerName">Attacker's name for narration.</param>
    /// <param name="attackerStats">Attacker's combat stats.</param>
    /// <param name="target">The target's type (Player or Mob).</param>
    /// <param name="targetName">Target's name for narration.</param>
    /// <param name="targetStats">Target's combat stats.</param>
    /// <param name="targetCurrentHealth">Target's current health before the attack.</param>
    /// <param name="targetValidation">Whether the target is a valid combat target in this room.</param>
    /// <param name="random">Randomness source for d20 rolls.</param>
    /// <returns>
    /// A CombatRound describing what happened: hit/miss, damage, narration. The Engine layer
    /// determines if the target is defeated by checking remaining health.
    /// </returns>
    public static CombatRound ExecuteRound(
        CombatantType attacker,
        string attackerName,
        AttackerStats attackerStats,
        CombatantType target,
        string targetName,
        DefenderStats targetStats,
        int targetCurrentHealth,
        TargetValidationResult targetValidation,
        IRandomSource random)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attackerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(attackerStats);
        ArgumentNullException.ThrowIfNull(targetStats);
        ArgumentNullException.ThrowIfNull(targetValidation);
        ArgumentNullException.ThrowIfNull(random);

        // If target validation failed, describe the refusal and end the engagement.
        if (!targetValidation.IsAllowed)
        {
            var refusal = targetValidation.RefusalReason ?? "The attack is not allowed.";
            return new CombatRound(
                AttackerId: attackerName,
                TargetId: targetName,
                AttackerName: attackerName,
                TargetName: targetName,
                Damage: new DamageResult(Hit: false, IsCritical: false, DamageDealt: 0, NaturalRoll: 0),
                TargetDefeated: false,
                AttackerNarration: refusal,
                TargetNarration: $"{attackerName} tried to attack you but cannot here.",
                RoomNarration: refusal);
        }

        // Target is valid. Roll the attack.
        var damage = DamageCalculator.CalculateDamage(attackerStats, targetStats, random);

        // Build narration based on hit/miss/crit.
        var (attackerText, targetText, roomText) = BuildNarration(
            attackerName, targetName, damage);

        // Check if target is defeated (would drop to 0 or below health).
        bool isDefeated = targetCurrentHealth - damage.DamageDealt <= 0;

        return new CombatRound(
            AttackerId: attackerName,
            TargetId: targetName,
            AttackerName: attackerName,
            TargetName: targetName,
            Damage: damage,
            TargetDefeated: isDefeated,
            AttackerNarration: attackerText,
            TargetNarration: targetText,
            RoomNarration: roomText);
    }

    /// <summary>
    /// Builds human-readable narration for a combat result.
    /// </summary>
    private static (string AttackerText, string TargetText, string RoomText) BuildNarration(
        string attackerName,
        string targetName,
        DamageResult damage)
    {
        if (!damage.Hit)
        {
            var missText = $"You miss {targetName}.";
            return (
                AttackerText: missText,
                TargetText: $"{attackerName} misses you.",
                RoomText: $"{attackerName} misses {targetName}.");
        }

        string critMarker = damage.IsCritical ? " **CRITICAL**" : string.Empty;
        string damageText = $"{damage.DamageDealt} damage{critMarker}";

        var attackerNarration = $"You hit {targetName} for {damageText}.";
        var targetNarration = $"{attackerName} hits you for {damageText}.";
        var roomNarration = $"{attackerName} hits {targetName} for {damageText}.";

        return (attackerNarration, targetNarration, roomNarration);
    }
}
