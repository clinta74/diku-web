namespace Muwbta.Domain.Worlds;

/// <summary>
/// Top of the hierarchy (PLAN.md §4.1): a realm such as "aldenmoor" or "the-underdark".
/// Worlds are real partitions - travel between them is deliberate and rare, via portals
/// rather than walking.
/// </summary>
public sealed class World
{
    /// <summary>Single lowercase segment, e.g. "aldenmoor".</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>
    /// Difficulty multipliers: applied on top of every zone inside this world (PLAN.md §4.4).
    /// Persisted as jsonb.
    /// </summary>
    public Multipliers Multipliers { get; set; } = new();

    /// <summary>
    /// Flag defaults for every zone and room in this world (PLAN.md §4.10). The outermost
    /// level of the chain: an arena realm sets <c>pvp</c> once, here.
    /// </summary>
    public FlagSet Flags { get; set; } = new();

    public ICollection<Zone> Zones { get; init; } = [];
}
