namespace DikuWeb.Domain.Quests;

/// <summary>
/// A quest offered by an NPC (giver) and turned in to another NPC (turnin).
/// Consists of collecting a required item and receiving XP/gold/item rewards.
/// </summary>
public sealed class Quest
{
    /// <summary>Unique quest identifier, e.g., "warden.slay-goblins".</summary>
    public required string Key { get; init; }

    /// <summary>Zone this quest belongs to, e.g., "starting.zone".</summary>
    public required string ZoneKey { get; init; }

    /// <summary>Display name shown to players, e.g., "Slay the Goblins".</summary>
    public required string Name { get; set; }

    /// <summary>Short summary, e.g., "Defeat 5 goblins in the forest".</summary>
    public string Summary { get; set; } = "";

    /// <summary>Full quest description/flavor text.</summary>
    public string Description { get; set; } = "";

    /// <summary>Template key of the mob offering the quest, e.g., "warden.mentor".</summary>
    public required string GiverMobKey { get; set; }

    /// <summary>Template key of the mob accepting the quest turn-in, e.g., "warden.commander".</summary>
    public required string TurninMobKey { get; set; }

    /// <summary>Template key of the item to collect, e.g., "goblin-ear".</summary>
    public required string RequiredItemKey { get; set; }

    /// <summary>How many of the item must be collected before turn-in.</summary>
    public int RequiredCount { get; set; } = 1;

    /// <summary>XP awarded on completion.</summary>
    public int RewardXp { get; set; }

    /// <summary>Gold awarded on completion.</summary>
    public int RewardGold { get; set; }

    /// <summary>Optional item template key awarded as quest reward, e.g., "iron-sword".</summary>
    public string? RewardItemKey { get; set; }

    /// <summary>How many reward items to give if RewardItemKey is set.</summary>
    public int RewardItemCount { get; set; } = 1;

    /// <summary>List of prerequisite quest keys that must be completed first.</summary>
    public List<string> PrerequisiteQuestKeys { get; set; } = [];

    /// <summary>Whether this quest can be completed multiple times by the same character.</summary>
    public bool IsRepeatable { get; set; }

    /// <summary>
    /// Dialogue strings indexed by situation:
    /// - "giverOffer": when offering the quest for the first time
    /// - "giverInProgress": when the player hasn't yet completed the objective
    /// - "giverComplete": when the quest is done (non-repeatable) or repeatable quests already completed
    /// - "turninReady": when the player brings the required items to turn in
    /// </summary>
    public Dictionary<string, string> Dialogue { get; set; } = [];

    /// <summary>Display order for listing quests in the same zone.</summary>
    public int SortOrder { get; set; }
}
