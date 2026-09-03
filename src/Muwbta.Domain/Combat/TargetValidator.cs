namespace Muwbta.Domain.Combat;

/// <summary>
/// The result of validating whether an attack is allowed. Separate from the damage formula
/// on purpose: this gate checks authority (peaceful flag, pvp flag, etc.) and the damage
/// formula stays a pure function of two combatants' stats (PLAN.md §4.6).
/// </summary>
/// <param name="IsAllowed">Whether the attack is permitted.</param>
/// <param name="RefusalReason">Why the attack is refused, or null if allowed. Used for narration.</param>
public sealed record TargetValidationResult(bool IsAllowed, string? RefusalReason);

/// <summary>
/// Validates whether an attack is allowed, given the attacker, target, and room flags
/// (PLAN.md §4.11). Returned refusal reasons are human-readable for narration to the player.
///
/// Rules:
/// - Peaceful forbids all combat
/// - Player-versus-player requires the pvp flag
/// - Mobs can be attacked everywhere except peaceful rooms
/// - Party members are never valid targets, pvp room or not (Phase 5.3)
/// - Area effects must filter per target, not per room
/// </summary>
public static class TargetValidator
{
    /// <summary>
    /// Checks if a target can be attacked given room flags.
    /// </summary>
    /// <param name="attackerType">The attacker's type (Player or Mob).</param>
    /// <param name="targetType">The target's type (Player or Mob).</param>
    /// <param name="targetName">The target's name for narration if refused.</param>
    /// <param name="roomIsPeaceful">Whether the room has the peaceful flag set.</param>
    /// <param name="roomIsPvp">Whether the room has the pvp flag set.</param>
    /// <param name="sameParty">
    /// Whether these two are grouped together. Optional because most callers are asking about a
    /// mob, where the question does not arise; whoever knows the answer passes it, and this stays
    /// a pure function of its arguments with no registry to reach into.
    /// </param>
    /// <returns>
    /// IsAllowed=true if the attack is permitted, or IsAllowed=false with a RefusalReason
    /// for narration.
    /// </returns>
    public static TargetValidationResult ValidateTarget(
        CombatantType attackerType,
        CombatantType targetType,
        string targetName,
        bool roomIsPeaceful,
        bool roomIsPvp,
        bool sameParty = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        // Peaceful beats everything: no combat at all.
        if (roomIsPeaceful)
        {
            return new TargetValidationResult(
                false,
                $"You cannot attack {targetName} here.");
        }

        // Checked before the pvp flag rather than after it, because being grouped is the stronger
        // statement: an arena is exactly where two party members are most likely to be standing
        // when one of them drops an area effect (PLAN.md §4.11).
        if (sameParty)
        {
            return new TargetValidationResult(
                false,
                $"{targetName} is in your group.");
        }

        // Mobs can be attacked everywhere except peaceful (already checked above).
        if (targetType == CombatantType.Mob)
        {
            return new TargetValidationResult(true, null);
        }

        // Target is a player. Player-versus-player requires the pvp flag.
        if (attackerType == CombatantType.Player && targetType == CombatantType.Player)
        {
            if (!roomIsPvp)
            {
                return new TargetValidationResult(
                    false,
                    $"You cannot attack {targetName} here.");
            }
        }

        return new TargetValidationResult(true, null);
    }
}
