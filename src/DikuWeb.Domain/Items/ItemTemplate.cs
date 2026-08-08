namespace DikuWeb.Domain.Items;

/// <summary>
/// PLAN.md §4.8: A baseline item definition. Global, not zone-scoped. One template appears
/// at many power tiers via zone multipliers. Never mutated; spawned instances carry the
/// multiplier-resolved stats.
/// </summary>
public sealed class ItemTemplate
{
    /// <summary>Single lowercase segment, e.g. "rusty-dagger".</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Single character icon for the map display.</summary>
    public required string Icon { get; set; }

    /// <summary>Equipment slot this fills, if any. null for ground items.</summary>
    public ItemSlot? Slot { get; set; }

    /// <summary>Weight in grams, for encumbrance calculation.</summary>
    public int Weight { get; set; }

    /// <summary>Base vendor value before multipliers. Persisted as jsonb.</summary>
    public int BaseValue { get; set; }

    /// <summary>
    /// Base stats before multipliers: damage dice, armor values, attribute bonuses.
    /// Persisted as jsonb object (free-form, validated by game logic only).
    /// </summary>
    public Dictionary<string, object> BaseStats { get; set; } = new();

    /// <summary>
    /// Pulses between swings when this is wielded. Null means it declares no speed: in the main
    /// hand that is the 8-pulse default, and in the off hand it means the item is not a weapon at
    /// all and never strikes - which is what keeps shields from punching.
    /// </summary>
    /// <remarks>
    /// A column rather than a <see cref="BaseStats"/> key on purpose. The builder coerces every
    /// base stat to a number, which would destroy a verb, and a floor of 4 needs the server to be
    /// able to refuse a save.
    /// </remarks>
    public int? AttackDelayPulses { get; set; }

    /// <summary>
    /// Base-form verb describing how this weapon strikes: "slash" for a sword, "crush" for a club.
    /// Null narrates as "hit". See <see cref="Narration.NarrationHelper.ThirdPerson"/>.
    /// </summary>
    public string? AttackVerb { get; set; }
}

/// <summary>Equippable item slots.</summary>
public enum ItemSlot
{
    Head,
    Chest,
    Hands,
    Legs,
    Feet,
    MainHand,
    OffHand,
    Trinket,
}
