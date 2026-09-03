namespace Muwbta.Domain.Worlds;

/// <summary>
/// A directed edge between two rooms. Exits are room-to-room and are deliberately not tied
/// to grid cells - the room map is cosmetic (PLAN.md §4.2).
/// </summary>
public sealed class RoomExit
{
    public required RoomKey FromRoomKey { get; init; }

    public required Direction Direction { get; init; }

    /// <summary>
    /// Held as a key rather than a navigation property, and NOT a foreign key in the
    /// database. Live editing means a builder links an exit before creating its destination,
    /// and a FK would reject that save (PLAN.md §6, §7.4). An exit whose target does not
    /// exist fails closed at movement time with "The way is blocked."
    /// </summary>
    public required RoomKey ToRoomKey { get; set; }

    /// <summary>
    /// A character flag required to pass this way, or null when anyone may (PLAN.md §4.15).
    /// </summary>
    /// <remarks>
    /// Names the <em>capability</em> rather than the quest that grants it, which is what lets a
    /// chain be re-authored, split, or given a second route without touching a single exit. Held as
    /// a plain string for the same reason <see cref="ToRoomKey"/> is: content may reference what
    /// does not exist yet, and a builder must be able to author the gate before the quest.
    /// </remarks>
    public string? RequiredFlagKey { get; set; }

    /// <summary>
    /// An item template key the character must be carrying, or null (PLAN.md §4.15). Checked
    /// against direct inventory and equipment; never consumed.
    /// </summary>
    /// <remarks>
    /// The losable half of the pair. A flag survives being robbed and this does not, which is
    /// exactly why attunement to a realm is a flag and a vault key is an item.
    /// </remarks>
    public string? RequiredItemKey { get; set; }

    /// <summary>
    /// What a character who cannot pass is told, or null for the generic refusal.
    /// </summary>
    /// <remarks>
    /// Authored rather than derived, because <em>"The gate does not know you."</em> and <em>"It is
    /// locked."</em> are different sentences and choosing between them from whichever requirement
    /// failed gets worse the moment an exit has both. The author knows what the door is.
    /// </remarks>
    public string? RefusalMessage { get; set; }

    /// <summary>True when anything at all gates this exit.</summary>
    public bool IsConditional => RequiredFlagKey is not null || RequiredItemKey is not null;

    public Room? FromRoom { get; init; }
}
