using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Combat;
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

    public CharacterRestState RestState { get; set; } = CharacterRestState.Stand;

    /// <summary>Current combat engagement state (idle, fighting, fleeing).</summary>
    public CombatState CombatState { get; set; } = CombatState.Idle;

    /// <summary>Entity ID of current target (character ID, mob ID, or null).</summary>
    public string? CurrentTarget { get; set; }

    /// <summary>Respawn point set by bind command in a respawn-flagged room (PLAN.md §4.12).</summary>
    public RoomKey? RespawnRoomKey { get; set; }

    /// <summary>Gold wallet from mob kills (PLAN.md §4.8).</summary>
    public long Gold { get; set; }

    /// <summary>
    /// Capabilities this character has earned, usually as a quest reward — attunement to a realm,
    /// a rank, a pardon (PLAN.md §4.15). A conditional exit names one of these to decide whether
    /// this character may pass.
    /// </summary>
    /// <remarks>
    /// <b>On the character, never the account.</b> An account-level flag would hand a fresh alt
    /// attunement to the last realm at level 1, which is the whole gate defeated by the
    /// character-select screen.
    ///
    /// <b>A set of keys rather than a <see cref="Worlds.FlagSet"/>, and that is not an
    /// inconsistency.</b> A room flag needs three states, because absent must fall through to the
    /// zone while an explicit false must override it. Nothing inherits from a character, so a flag
    /// is held or it is not, and a map of key to <c>true</c> would be a second way of writing the
    /// same fact — the shape that let <c>sentinel</c> and <c>wanders</c> disagree (§4.8).
    ///
    /// A concrete List because Npgsql maps <c>List&lt;string&gt;</c> onto <c>text[]</c> directly,
    /// as it already does for prerequisite quest keys and spawner room keys.
    /// </remarks>
    public List<string> Flags { get; set; } = [];

    /// <summary>Whether this character holds a capability. Absent is always the closed answer.</summary>
    public bool HasFlag(string? key) =>
        key is not null && Flags.Contains(key, StringComparer.Ordinal);

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastPlayedAt { get; set; }

    public long PlaytimeSeconds { get; set; }

    /// <summary>Soft delete: characters are never hard-deleted, so names stay reserved.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
