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
