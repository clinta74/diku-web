namespace DikuWeb.Domain.Worlds;

/// <summary>
/// The atomic location. Note there are no occupant coordinates here: entity positions are
/// presentation state owned by RoomLayoutService and are invisible to Domain (PLAN.md §4.2).
/// </summary>
public sealed class Room
{
    public required RoomKey Key { get; init; }

    public required string ZoneKey { get; init; }

    public Zone? Zone { get; init; }

    public required string Title { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Room-level flags (PLAN.md §4.10). An absent key falls through to the zone, then the
    /// world, then the registry default - so this holds only what this room overrides.
    /// </summary>
    public FlagSet Flags { get; set; } = new();

    /// <summary>
    /// Optional ASCII terrain art, one string per row (PLAN.md §4.4). Decoration and
    /// spawn-placement surface only - walls block nothing, because there is nothing to block.
    /// A room with no grid renders a plain rectangle, so art is an upgrade rather than a tax.
    /// </summary>
    /// <remarks>
    /// A concrete List rather than IReadOnlyList because Npgsql maps List&lt;string&gt;
    /// directly onto a text[] column; the interface has no such mapping.
    /// </remarks>
    public List<string> Grid { get; set; } = [];

    /// <summary>Maps a grid character to a tile name, e.g. '#' to "wall".</summary>
    public Dictionary<string, string> Legend { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Builder canvas position within the zone (PLAN.md §7.2). Editor-only layout metadata:
    /// players never see a zone map and no rule reads this.
    /// </summary>
    public int? EditorX { get; set; }

    public int? EditorY { get; set; }

    public ICollection<RoomExit> Exits { get; init; } = [];

    public bool HasGrid => Grid.Count > 0;

    public RoomExit? ExitTo(Direction direction) =>
        Exits.FirstOrDefault(e => e.Direction == direction);
}
