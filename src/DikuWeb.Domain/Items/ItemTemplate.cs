using DikuWeb.Domain.Characters;

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

    /// <summary>
    /// Marks instances of this template as bound to a quest: they cannot be sold or destroyed,
    /// but can still be dropped (PLAN.md §4.9).
    /// </summary>
    /// <remarks>
    /// A column rather than a <see cref="BaseStats"/> key, for the same reason as
    /// <see cref="AttackDelayPulses"/>: it is a rule the server enforces, not a number to scale.
    /// The flag is stamped onto each instance at spawn time by <c>ItemSpawner</c> rather than read
    /// from the template at the point of sale, so an item already in a pack keeps the rule it was
    /// created under even if a builder later unsets it.
    /// </remarks>
    public bool IsQuestItem { get; set; }

    /// <summary>
    /// One only. A character may not hold a second copy in inventory or equipment.
    /// </summary>
    /// <remarks>
    /// The rule an epic reward needs: without it one player farms an act boss and equips five
    /// friends. Checked wherever an item enters a pack — picking it up, buying it, being given it,
    /// and being handed it as a quest reward — because a lore item that only the loot path refuses
    /// is a lore item you buy two of.
    /// </remarks>
    public bool IsLore { get; set; }

    /// <summary>
    /// Cannot be dropped or given away. It <em>can</em> be destroyed.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="IsQuestItem"/>, which is the opposite pair: a quest item cannot be
    /// sold or destroyed but can be put down. This one is about who ends up holding it rather than
    /// about it surviving, so the way out is deliberate destruction — leaving no way at all to be
    /// rid of a bound item is how a pack fills up with things a character will never use again.
    /// </remarks>
    public bool IsNoDrop { get; set; }

    /// <summary>
    /// Lights the room it is worn or wielded in.
    /// </summary>
    /// <remarks>
    /// <b>Any slot, not a slot of its own.</b> A lantern in the off hand, a helm with a lamp on it,
    /// a pendant that glows — the fiction is the item's, and reserving a hand for light would make
    /// every dark room a choice between seeing and carrying a shield. Carrying one in the pack is
    /// deliberately not enough: a light you have not taken out is a light that is not lit.
    ///
    /// A column rather than a <see cref="BaseStats"/> key, for the reason
    /// <see cref="AttackDelayPulses"/> gives — the builder coerces every base stat to a number, and
    /// this is a rule rather than a quantity. It is read from the template rather than stamped onto
    /// the instance, so a builder who lights a lamp lights the ones already out in the world too.
    /// </remarks>
    public bool IsLightSource { get; set; }

    /// <summary>
    /// The Paths that may wear or wield this. Empty means anyone.
    /// </summary>
    /// <remarks>
    /// Empty rather than null-or-all, so the default state of an authored item is "no restriction"
    /// and a builder has to opt in. A list rather than a single Path because the useful case is
    /// "the two martial Paths" as often as it is one.
    ///
    /// This restricts <em>equipping</em> only. Carrying, selling and handing over are deliberately
    /// left alone: a Shade should be able to pick up a Warden's shield and take it to the Warden.
    /// </remarks>
    public List<CharacterPath> Paths { get; set; } = [];
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
