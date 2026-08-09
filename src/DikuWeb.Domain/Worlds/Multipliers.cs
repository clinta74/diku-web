namespace DikuWeb.Domain.Worlds;

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

    /// <summary>Item stat bonuses (weapon damage, armor).</summary>
    public decimal ItemPower { get; set; } = 1.0m;

    /// <summary>Spawner target counts — makes a zone crowded, not just tougher.</summary>
    public decimal SpawnDensity { get; set; } = 1.0m;

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
        ItemPower = ItemPower,
        SpawnDensity = SpawnDensity,
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
            MultiplierType.ItemPower => Math.Max(1, Round(baseValue * world.ItemPower * zone.ItemPower)),
            MultiplierType.SpawnDensity => Math.Max(1, Round(baseValue * world.SpawnDensity * zone.SpawnDensity)),
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
    ItemPower,
    SpawnDensity,
}
