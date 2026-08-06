namespace DikuWeb.Domain.Combat;

/// <summary>
/// Character's current combat engagement state.
/// </summary>
public enum CombatState
{
    /// <summary>Not in combat.</summary>
    Idle = 0,

    /// <summary>Actively engaged in combat, attacking a target.</summary>
    Fighting = 1,

    /// <summary>Attempting to flee combat (ends at start of next round).</summary>
    Fleeing = 2,
}
