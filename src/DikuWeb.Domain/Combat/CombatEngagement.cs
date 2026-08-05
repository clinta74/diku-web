namespace DikuWeb.Domain.Combat;

/// <summary>
/// A single combatant engaged in a fight. Tracks who is attacking whom and when the last
/// attack occurred (for cooldown calculations). Immutable so combat state changes require
/// creating a new engagement (PLAN.md §2.1).
/// </summary>
/// <param name="AttackerId">The character or mob initiating the attack (unique ID as string).</param>
/// <param name="TargetId">The target being attacked (unique ID as string).</param>
/// <param name="LastAttackPulse">The game pulse at which the attacker last attacked, used for cooldown.</param>
public sealed record CombatEngagement(string AttackerId, string TargetId, long LastAttackPulse);
