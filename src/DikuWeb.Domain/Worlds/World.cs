namespace DikuWeb.Domain.Worlds;

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

    public ICollection<Zone> Zones { get; init; } = [];
}
