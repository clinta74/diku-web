namespace DikuWeb.Domain.Quests;

/// <summary>
/// A quest: a task offered by one NPC (giver) and completed at another (turnin).
/// Rewards XP, gold, and optionally an item. Chains via prerequisites.
/// Deliberately uses string keys for mobs/items (not FKs) so quests can be
/// authored before content exists (PLAN.md §7.4).
/// </summary>
public sealed class Quest
{
    /// <summary>Unique key like "aldenmoor.rat-infestation".</summary>
    public required string Key { get; init; }

    /// <summary>Zone this quest belongs to.</summary>
    public required string ZoneKey { get; init; }

    /// <summary>Display name: "Rat Infestation".</summary>
    public required string Name { get; set; }

    /// <summary>One-line summary for quest log.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Full description shown in quest detail view.</summary>
    public string Description { get; set; } = "";

    /// <summary>Mob template key that offers this quest (e.g. "guard").</summary>
    public required string GiverMobKey { get; set; }

    /// <summary>Mob template key that accepts the turnin (e.g. "captain").</summary>
    public required string TurninMobKey { get; set; }

    /// <summary>Item template key required to complete (e.g. "rat-tail"). Can be null if quest has no item requirement.</summary>
    public string? RequiredItemKey { get; set; }

    /// <summary>Number of the required item to collect. Default 1.</summary>
    public int RequiredCount { get; set; } = 1;

    /// <summary>XP reward on completion (before multipliers).</summary>
    public int RewardXp { get; set; }

    /// <summary>Gold reward on completion.</summary>
    public int RewardGold { get; set; }

    /// <summary>Optional item template key to spawn as reward (e.g. "leather-boots").</summary>
    public string? RewardItemKey { get; set; }

    /// <summary>Number of reward items to spawn. Default 1.</summary>
    public int RewardItemCount { get; set; } = 1;

    /// <summary>Quest keys that must be completed before this one can be started. Empty = no prerequisites.</summary>
    public List<string> PrerequisiteQuestKeys { get; set; } = [];

    /// <summary>Whether this quest can be completed multiple times.</summary>
    public bool IsRepeatable { get; set; }

    /// <summary>Dialogue strings: giverOffer, giverInProgress, giverComplete, turninReady.</summary>
    public Dictionary<string, string> Dialogue { get; set; } = [];

    /// <summary>Display order in quest log.</summary>
    public int SortOrder { get; set; }
}
