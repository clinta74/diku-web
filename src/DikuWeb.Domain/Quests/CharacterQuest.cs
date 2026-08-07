namespace DikuWeb.Domain.Quests;

/// <summary>
/// Tracks a character's progress on a single quest: active, completed, repeatable.
/// Composite key: (CharacterId, QuestKey).
/// </summary>
public sealed class CharacterQuest
{
    /// <summary>The character who is pursuing this quest.</summary>
    public required Guid CharacterId { get; init; }

    /// <summary>The quest being pursued.</summary>
    public required string QuestKey { get; init; }

    /// <summary>Active = in progress, Completed = finished (may be repeatable).</summary>
    public QuestStatus Status { get; set; } = QuestStatus.Active;

    /// <summary>When the character started this quest.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the character completed this quest (null if Active).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>How many times this character has completed this quest.</summary>
    public int TimesCompleted { get; set; }
}

public enum QuestStatus
{
    Active,
    Completed,
}
