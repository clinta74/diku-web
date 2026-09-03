namespace Muwbta.Domain.Combat;

/// <summary>Which of a combatant's attacks a timer belongs to.</summary>
public enum AttackSlotKind
{
    MainHand = 0,
    OffHand = 1,
    MobAttack = 2,
}

/// <summary>
/// One attack a combatant owns, and therefore one clock. A player has a main hand and an off
/// hand; a mob has one per entry in its template's attack list.
/// </summary>
/// <param name="Kind">Which kind of attack this is.</param>
/// <param name="Index">Position in the mob's attack list. Always 0 for the two hands.</param>
public readonly record struct AttackSlot(AttackSlotKind Kind, int Index)
{
    public static AttackSlot MainHand { get; } = new(AttackSlotKind.MainHand, 0);

    public static AttackSlot OffHand { get; } = new(AttackSlotKind.OffHand, 0);

    /// <summary>
    /// The mob attack at this position. Keyed by index rather than by verb because two entries
    /// may share a verb; the cost is that reordering a template mid-fight reshuffles which timer
    /// belongs to which attack, which is acceptable for an action only a builder can take.
    /// </summary>
    public static AttackSlot Mob(int index) => new(AttackSlotKind.MobAttack, index);
}
