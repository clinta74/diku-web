namespace DikuWeb.Domain.Worlds;

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

    public Room? FromRoom { get; init; }
}
