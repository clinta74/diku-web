namespace DikuWeb.Domain.Inhabitants;

/// <summary>
/// PLAN.md §4.8: A baseline mob definition. Global, not zone-scoped. One template appears
/// at many power tiers via zone multipliers. Immutable content.
/// </summary>
public sealed class MobTemplate
{
    /// <summary>Single lowercase segment, e.g. "kobold-sentry".</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Single character icon for the map display.</summary>
    public required string Icon { get; set; }

    /// <summary>Base level before any adjustments.</summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// Base stats: health, damage (as dice string), attributes.
    /// Persisted as jsonb object. Example: { "health": 40, "damage": "4-7", "might": 12, "agility": 10 }
    /// </summary>
    public Dictionary<string, object> BaseStats { get; set; } = new();

    /// <summary>XP awarded on kill before multipliers.</summary>
    public int BaseXp { get; set; }

    /// <summary>Gold dropped on loot before multipliers.</summary>
    public int BaseGold { get; set; }

    /// <summary>
    /// Behavior flags: e.g., { "type": "aggressive", "emotes": ["snarls", "growls", "bares teeth"] }
    /// Persisted as jsonb for extensibility.
    /// </summary>
    public Dictionary<string, object> Behavior { get; set; } = new();

    /// <summary>
    /// Loot table: list of items this mob drops on death with percentage chance.
    /// Example: [{ "itemTemplateKey": "rusty-dagger", "chance": 0.5 }, ...]
    /// Persisted as jsonb.
    /// </summary>
    public List<Dictionary<string, object>> Loot { get; set; } = new();
}
