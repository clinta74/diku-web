namespace Muwbta.Domain.Worlds;

/// <summary>
/// An authored area within a world (PLAN.md §4.1). The unit of content ownership, spawning,
/// level range, and - from Phase 3 - difficulty scaling.
/// </summary>
public sealed class Zone
{
    /// <summary>Two segments, "world.zone", e.g. "aldenmoor.millbrook".</summary>
    public required string Key { get; init; }

    public required string WorldKey { get; init; }

    public World? World { get; init; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public int MinLevel { get; set; } = 1;

    public int MaxLevel { get; set; } = 50;

    /// <summary>
    /// Difficulty multipliers applied to mobs and items spawned in this zone (PLAN.md §4.4).
    /// Composed with world-level multipliers. Persisted as jsonb.
    /// </summary>
    public Multipliers Multipliers { get; set; } = new();

    /// <summary>
    /// Flag defaults for every room in this zone (PLAN.md §4.10). Setting <c>pvp</c> here is
    /// how a duelling zone is authored, rather than flagging forty rooms one at a time.
    /// </summary>
    public FlagSet Flags { get; set; } = new();

    public ICollection<Room> Rooms { get; init; } = [];
}
