using DikuWeb.Domain.Characters;

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

    /// <summary>
    /// A character flag granted on completion, or null (PLAN.md §4.15) — how attunement to a realm
    /// is earned, and the only thing in the game that writes <see cref="Characters.Character.Flags"/>.
    /// </summary>
    /// <remarks>
    /// Granted rather than toggled: a capability is never taken back by finishing a quest again, so
    /// a repeatable quest re-granting what the character already holds is a no-op rather than a
    /// second copy.
    /// </remarks>
    public string? RewardFlagKey { get; set; }

    /// <summary>Quest keys that must be completed before this one can be started. Empty = no prerequisites.</summary>
    public List<string> PrerequisiteQuestKeys { get; set; } = [];

    /// <summary>Whether this quest can be completed multiple times.</summary>
    public bool IsRepeatable { get; set; }

    /// <summary>
    /// The Paths this quest is for. Empty means anyone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same shape as <c>ItemTemplate.Paths</c>, and for the same reason.</b> Empty rather
    /// than null-or-all, so an authored quest is unrestricted until a builder opts in - which keeps
    /// every quest that existed before this field behaving exactly as it did.
    /// </para>
    /// <para>
    /// It exists because the four epic chains have Path-locked rewards and one giver. Vesh handed
    /// every character all four, so a Shade finished the Adept chain and received a stormrod they
    /// could not wield - and, being lore and no-drop, could not get rid of either. A quest whose
    /// reward only one Path can use is a quest only that Path should be offered.
    /// </para>
    /// <para>
    /// This gates <em>being offered and being finished</em>, not being held. A character already
    /// carrying a quest their Path cannot use keeps it in the journal and is told plainly, rather
    /// than having progress removed from under them - <c>abandon</c> is how they clear it.
    /// </para>
    /// </remarks>
    public List<CharacterPath> Paths { get; set; } = [];

    /// <summary>
    /// Starts by itself the moment its prerequisites are all complete, with no <c>talk</c>.
    /// </summary>
    /// <remarks>
    /// Declared by the quest that would be started rather than as a list on the one that starts
    /// it, so <see cref="PrerequisiteQuestKeys"/> stays the only description of what follows
    /// what. A second list of chain edges would be a second graph over the same quests, invisible
    /// to the storyline panel and with nothing making the two agree.
    /// It also matches the direction content is authored in (PLAN.md §7.4): a builder writes this
    /// quest knowing what it follows, not the earlier one knowing what comes after it.
    /// </remarks>
    public bool AutoStart { get; set; }

    /// <summary>Dialogue strings: giverOffer, giverInProgress, giverComplete, turninReady.</summary>
    public Dictionary<string, string> Dialogue { get; set; } = [];

    /// <summary>Display order in quest log.</summary>
    public int SortOrder { get; set; }
}
