namespace DikuWeb.Domain.Combat;

/// <summary>
/// Describes what happens in a single combat round (2 seconds). Contains damage results
/// and narration for the players involved. Created by CombatSystem.ExecuteRound for use by
/// the Engine to update the world and send messages (PLAN.md §4.3).
/// </summary>
/// <param name="AttackerId">The attacker's ID.</param>
/// <param name="TargetId">The target's ID.</param>
/// <param name="AttackerName">The attacker's name for narration.</param>
/// <param name="TargetName">The target's name for narration.</param>
/// <param name="Damage">Result of the attack, with hit/miss/crit info.</param>
/// <param name="TargetDefeated">Whether the target is dead after this round.</param>
/// <param name="AttackerNarration">What the attacker sees (e.g., "You slash Kael for 12 damage.").</param>
/// <param name="TargetNarration">What the target sees (e.g., "Theron slashes you for 12 damage.").</param>
/// <param name="RoomNarration">What bystanders see (e.g., "Theron slashes Kael for 12 damage.").</param>
public sealed record CombatRound(
    string AttackerId,
    string TargetId,
    string AttackerName,
    string TargetName,
    DamageResult Damage,
    bool TargetDefeated,
    string AttackerNarration,
    string TargetNarration,
    string RoomNarration);
