namespace DikuWeb.Domain.Accounts;

/// <summary>
/// One row per administrative action taken against an account (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// Deliberately not <c>content_audit</c>. An account is not content, and merging the two makes
/// both questions harder to answer: "who edited this room" would have to filter out promotions,
/// and "who made this person a builder" would have to filter out every room save ever made.
///
/// Phase 6's mute, kick, and ban write here too.
/// </remarks>
public sealed class AdminAudit
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Who did it. Null only for actions taken by the system rather than a person.</summary>
    public Guid? ActorAccountId { get; init; }

    public required Guid TargetAccountId { get; init; }

    public required AdminAction Action { get; init; }

    /// <summary>Prior value, as plain text. A role name today; a mute duration later.</summary>
    public string? Before { get; init; }

    public string? After { get; init; }

    /// <summary>Optional free text, for the moderation actions that will want one.</summary>
    public string? Reason { get; init; }

    public required DateTimeOffset At { get; init; }
}

public enum AdminAction
{
    RoleChanged = 0,
}
