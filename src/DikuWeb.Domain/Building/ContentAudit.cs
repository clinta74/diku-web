namespace DikuWeb.Domain.Building;

/// <summary>
/// One row per world-content mutation, with before and after (PLAN.md §6, §7.3).
/// </summary>
/// <remarks>
/// This is what replaced git history when content moved into Postgres. It is honestly weaker -
/// no branches, no diffs across a whole change set, no blame - which is the acknowledged price
/// of the Postgres-only choice (PLAN.md §10). It does answer the question that actually gets
/// asked after a bad live edit: who changed this room, when, and what did it say before.
/// </remarks>
public sealed class ContentAudit
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Who made the edit. Null only for edits made by the seeder or a migration.</summary>
    public Guid? AccountId { get; init; }

    /// <summary>"world", "zone", "room", "exit".</summary>
    public required string EntityKind { get; init; }

    public required string EntityKey { get; init; }

    public required ContentAction Action { get; init; }

    /// <summary>
    /// Raw JSON, or null for a create. Held as text rather than a typed shape on purpose: an
    /// audit row has to stay readable after the entity's own shape has moved on, and a typed
    /// column would either block that or quietly fail to deserialise old rows.
    /// </summary>
    public string? Before { get; init; }

    /// <summary>Raw JSON, or null for a delete.</summary>
    public string? After { get; init; }

    public required DateTimeOffset At { get; init; }
}

public enum ContentAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
}
