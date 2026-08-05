namespace DikuWeb.Domain.Spawning;

/// <summary>
/// PLAN.md §4.8: A declarative population-maintenance rule. "Keep N instances of template T
/// alive in rooms R, respawn D seconds after each dies or is picked up."
/// Spawners are global; templates are global. One spawner can maintain mobs or items.
/// </summary>
public sealed class Spawner
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Which zone owns this spawner rule.</summary>
    public required string ZoneKey { get; init; }

    /// <summary>Which template to spawn: ItemTemplate or MobTemplate key.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>What kind of template: Item or Mob.</summary>
    public TemplateKind TemplateKind { get; init; }

    /// <summary>Rooms where this spawner places instances. Defaults to all rooms in zone if empty.</summary>
    public List<string> RoomKeys { get; set; } = new();

    /// <summary>Target population (before spawn density multiplier).</summary>
    public int TargetCount { get; set; } = 1;

    /// <summary>Seconds to wait before respawning a dead mob or dropped item.</summary>
    public int RespawnSeconds { get; set; } = 30;
}

/// <summary>What kind of thing a spawner creates.</summary>
public enum TemplateKind
{
    Item,
    Mob,
}
