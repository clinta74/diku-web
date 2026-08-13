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

    /// <summary>
    /// Whether mobs from this spawner wander. <b>Null defers to the template</b>, which is the
    /// default and the usual answer; true and false override it for these mobs only.
    /// </summary>
    /// <remarks>
    /// Three-valued because the template now carries the default (PLAN.md §4.8) and the spawner
    /// still has the last word. Two values could not express *"whatever this mob normally does"*,
    /// so every spawner had to restate a decision that belongs on the thing being spawned — and
    /// restating it is how a shopkeeper placed by a second spawner ends up strolling out of its
    /// own shop.
    ///
    /// <b>Named for what it permits, not for what it forbids.</b> This was <c>Sentinel</c>, whose
    /// polarity was the opposite of the template's <c>wanders</c> key — and the one thing this
    /// codebase has inverted before is a pair of flags that mean the same thing in opposite
    /// directions (HISTORY.md, 5.1e: every "weaken" in the game made its target harder to kill).
    /// One direction, both places, so the resolution reads <c>spawner.Wanders ?? template</c>.
    /// </remarks>
    public bool? Wanders { get; set; }
}

/// <summary>What kind of thing a spawner creates.</summary>
public enum TemplateKind
{
    Item,
    Mob,
}
