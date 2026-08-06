namespace DikuWeb.Domain.Quests;

/// <summary>
/// The state of a quest for a specific character: whether it's active, completed, etc.
/// One row per character per quest, created on first acceptance.
/// </summary>
public sealed class CharacterQuest
{
    /// <summary>The character who accepted/completed this quest.</summary>
    public required Guid CharacterId { get; init; }

    /// <summary>The quest key this state belongs to, e.g., "warden.slay-goblins".</summary>
    public required string QuestKey { get; init; }

    /// <summary>Current state: Active or Completed.</summary>
    public QuestStatus Status { get; set; } = QuestStatus.Active;

    /// <summary>When this quest was first accepted by the character.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the quest was completed, if at all.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Number of times this character has completed the quest (for repeatables).</summary>
    public int TimesCompleted { get; set; }
}

/// <summary>Quest progress state for a character.</summary>
public enum QuestStatus
{
    Active = 0,
    Completed = 1,
}
