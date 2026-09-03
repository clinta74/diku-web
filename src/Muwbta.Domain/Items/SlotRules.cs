namespace Muwbta.Domain.Items;

/// <summary>
/// Where an item may go, and what stops it going there (PLAN.md §4.19).
/// </summary>
/// <remarks>
/// <para>
/// One place, because these questions get asked three times over: by <c>wear</c> and <c>wield</c>
/// when a player equips something, by the builder when it validates an authored template, and by
/// <c>check-bundle</c> before an import. A rule enforced in one of those and not the others is a
/// rule you get around by picking a different door.
/// </para>
/// <para>
/// Everything here reads the <em>template</em>. What an instance is currently equipped in stays
/// <c>ItemInstance.EquippedSlot</c>, a single slot, and is untouched by any of this - an item is
/// in one place at a time however many places it could have gone.
/// </para>
/// </remarks>
public static class SlotRules
{
    /// <summary>The two hand slots, in the order they are reached for.</summary>
    /// <remarks>
    /// Main hand first, and it falls out of the enum order rather than being asserted here, so a
    /// slot added to <see cref="ItemSlot"/> cannot silently sort itself between the hands.
    /// </remarks>
    public static readonly IReadOnlyList<ItemSlot> Hands = [ItemSlot.MainHand, ItemSlot.OffHand];

    /// <summary>Whether a slot is held rather than worn.</summary>
    public static bool IsHand(ItemSlot slot) => slot is ItemSlot.MainHand or ItemSlot.OffHand;

    /// <summary>
    /// The slots a template declares, in enum order and without repeats.
    /// </summary>
    /// <remarks>
    /// Order is the preference order, so normalising here is what makes "reaches for the main hand
    /// first" true everywhere at once rather than at each call site. The same argument as
    /// <c>CHARACTER_PATHS</c> on the Paths list: the stored order is the enum's, so what a builder
    /// ticked first cannot change what the game does.
    /// </remarks>
    public static IReadOnlyList<ItemSlot> Normalize(IEnumerable<ItemSlot>? slots) =>
        slots is null ? [] : [.. slots.Distinct().OrderBy(s => (int)s)];

    /// <summary>The hand slots this may be held in, in reach order.</summary>
    public static IReadOnlyList<ItemSlot> HandSlots(ItemTemplate? template) =>
        template is null ? [] : HandSlotsIn(template.Slots);

    /// <summary>The same, for a bare list - what a bundle carries before it is a template.</summary>
    public static IReadOnlyList<ItemSlot> HandSlotsIn(IEnumerable<ItemSlot>? slots) =>
        [.. Normalize(slots).Where(IsHand)];

    /// <summary>The body slots this may be worn in, in enum order.</summary>
    public static IReadOnlyList<ItemSlot> WornSlots(ItemTemplate? template) =>
        template is null ? [] : [.. Normalize(template.Slots).Where(s => !IsHand(s))];

    /// <summary>Whether this can be equipped anywhere at all.</summary>
    public static bool IsEquippable(ItemTemplate? template) =>
        template is not null && template.Slots.Count > 0;

    /// <summary>Whether holding this leaves no off hand to fill.</summary>
    /// <remarks>
    /// Reads the flag <em>and</em> the slots, so a template that has drifted into an impossible
    /// combination - two-handed but not a main-hand item - does not silently lock a hand it was
    /// never going to occupy. The builder refuses to author that; this is what keeps a row already
    /// in a database from acting on it.
    /// </remarks>
    public static bool ClaimsBothHands(ItemTemplate? template) =>
        template is { IsTwoHanded: true } && template.Slots.Contains(ItemSlot.MainHand);

    /// <summary>
    /// Why this template could never be equipped as authored, or null if it is coherent.
    /// </summary>
    /// <remarks>
    /// Written as a refusal rather than a bool so the one caller that has to explain itself - the
    /// builder - does not have to reconstruct the reason from a false.
    /// </remarks>
    public static string? Incoherent(IReadOnlyList<ItemSlot> slots, bool isTwoHanded)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (!isTwoHanded)
        {
            return null;
        }

        if (slots.Count == 0)
        {
            return "A two-handed item needs a slot: it goes in the main hand and denies the off hand.";
        }

        // Exactly the main hand, not merely including it. `[MainHand, OffHand] + twoHanded` reads
        // as "either hand, and also both", which is not a thing an item can be - and "[Chest] +
        // twoHanded" is a leftover flag on something that was retyped as armour.
        if (slots.Count != 1 || slots[0] != ItemSlot.MainHand)
        {
            return "A two-handed item can only be a main-hand item — it claims the off hand, "
                + "so it cannot also be worn or held there.";
        }

        return null;
    }

    /// <summary>A human-readable name for a slot, for prose like "your main hand".</summary>
    public static string Name(ItemSlot slot) => slot switch
    {
        ItemSlot.MainHand => "main hand",
        ItemSlot.OffHand => "off hand",
        _ => slot.ToString().ToLowerInvariant(),
    };
}
