namespace Muwbta.Domain.Worlds;

/// <summary>
/// PLAN.md §4.4: Difficulty scaling factors. All fractional, default 1.0.
/// Composition: effective = round(base × world.mult × zone.mult).
/// Persisted as jsonb so the set can grow without migrations.
/// </summary>
public sealed class Multipliers
{
    /// <summary>Master difficulty dial: scales health AND damage together.</summary>
    public decimal Strength { get; set; } = 1.0m;

    /// <summary>Fine-tune health on top of Strength.</summary>
    public decimal Health { get; set; } = 1.0m;

    /// <summary>Fine-tune damage on top of Strength.</summary>
    public decimal Damage { get; set; } = 1.0m;

    /// <summary>XP awarded on kill. Can be 0 for stingy zones.</summary>
    public decimal Xp { get; set; } = 1.0m;

    /// <summary>Coin drops on loot.</summary>
    public decimal Gold { get; set; } = 1.0m;

    /// <summary>Vendor value of items.</summary>
    public decimal ItemValue { get; set; } = 1.0m;

    // `ItemPower` and `SpawnDensity` used to sit here. Both were authored - itemPower on four
    // worlds and zones up to 1.4, spawnDensity on eight zones from 0.6 to 1.4 - editable in the
    // builder, previewed, exported, and applied by nothing at all: neither was ever passed to
    // Resolve from production code, and the only caller was a unit test asserting the arithmetic
    // of a function nothing invoked, which is what made them look alive.
    //
    // Deleted rather than implemented (BUGS.md #17). Wiring them up would have changed the balance
    // of content that has never been played, which is a tuning decision needing play behind it;
    // leaving them would have kept two dials in the builder that read as tuned and did nothing.

    /// <summary>
    /// A detached copy, for the same reason <c>FlagSet.Clone</c> exists: a mutation primitive is
    /// held for replay into the database, so the in-memory world must not share the instance the
    /// change is carrying, or a later edit to one would silently rewrite the other.
    /// </summary>
    public Multipliers Clone() => new()
    {
        Strength = Strength,
        Health = Health,
        Damage = Damage,
        Xp = Xp,
        Gold = Gold,
        ItemValue = ItemValue,
    };

    /// <summary>
    /// Resolve a base value through world and zone multipliers.
    /// PLAN.md §4.4: round(base × world × zone), with guards per multiplier type.
    /// </summary>
    public static int Resolve(decimal baseValue, Multipliers world, Multipliers zone, MultiplierType type) =>
        type switch
        {
            MultiplierType.Health => Math.Max(1, Round(baseValue * world.Health * zone.Health)),
            MultiplierType.Damage => Math.Max(1, Round(baseValue * world.Damage * zone.Damage)),
            MultiplierType.Strength => Math.Max(1, Round(baseValue * world.Strength * zone.Strength)),
            MultiplierType.Xp => Math.Max(0, Round(baseValue * world.Xp * zone.Xp)),
            MultiplierType.Gold => Math.Max(0, Round(baseValue * world.Gold * zone.Gold)),
            MultiplierType.ItemValue => Math.Max(0, Round(baseValue * world.ItemValue * zone.ItemValue)),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    /// <summary>Round half-away-from-zero, per PLAN.md §4.4.</summary>
    private static int Round(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>Which multiplier applies when resolving stats.</summary>
public enum MultiplierType
{
    Strength,
    Health,
    Damage,
    Xp,
    Gold,
    ItemValue,
}
