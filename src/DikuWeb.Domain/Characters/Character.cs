using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Characters;

/// <summary>
/// A playable character. Note there is deliberately no X/Y here: map position is
/// presentation state owned by RoomLayoutService, and Domain must not be able to read it
/// (PLAN.md §4.2). An architecture test enforces this.
/// </summary>
public sealed class Character
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid AccountId { get; init; }

    public Account? Account { get; init; }

    /// <summary>Stored as citext and globally unique across all accounts.</summary>
    public required string Name { get; set; }

    public required CharacterPath Path { get; init; }

    public int Level { get; set; } = 1;

    public long Xp { get; set; }

    public required AttributeSet Attributes { get; set; }

    public required Vitals Vitals { get; set; }

    /// <summary>
    /// Location as a "world.zone.room" key (PLAN.md §4.1). Stored as text and deliberately
    /// NOT a foreign key, for the same reason room_exits.to_room_key is not: live editing
    /// means a builder can delete a room out from under a saved character, and that must
    /// degrade gracefully rather than fail the write (PLAN.md §7.4). A character whose room
    /// no longer exists is relocated to the zone entrance on login.
    /// </summary>
    public required RoomKey RoomKey { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastPlayedAt { get; set; }

    public long PlaytimeSeconds { get; set; }

    /// <summary>Soft delete: characters are never hard-deleted, so names stay reserved.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
