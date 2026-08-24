using DikuWeb.Domain.Items;

namespace DikuWeb.Engine;

/// <summary>
/// The item-instance state bag's vocabulary, alongside <see cref="Inhabitants.MobBehavior"/>.
/// </summary>
public static class ItemState
{
    /// <summary>The state key marking an instance as bound to a quest.</summary>
    public const string QuestItemKey = "questItem";

    /// <summary>The state key listing the character ids a fresh drop belongs to.</summary>
    /// <remarks>See <see cref="LootClaim"/> for why a drop belongs to anyone at all.</remarks>
    public const string LootClaimKey = "lootClaim";

    /// <summary>The state key naming the killer, for the refusal line.</summary>
    /// <remarks>
    /// Written down rather than looked up when the refusal is worded: the claim outlives a
    /// disconnect, and a sentence with a hole where the name should be is worse than no rule.
    /// </remarks>
    public const string LootClaimByKey = "lootClaimBy";

    /// <summary>The state key holding when the claim lapses, as a round-trip timestamp.</summary>
    public const string LootClaimUntilKey = "lootClaimUntil";

    /// <summary>The state key holding when an untaken drop leaves the world.</summary>
    /// <remarks>
    /// Only mob loot carries this; see <see cref="GroundDecay"/>. An item without it is on no
    /// clock at all, which is the fate of everything a player drops.
    /// </remarks>
    public const string DecaysAtKey = "decaysAt";

    /// <summary>
    /// True when this instance is a quest item, which PLAN.md §4.9 defines as *cannot be sold or
    /// destroyed* - droppable, but not disposable for good.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="JsonBag"/> because this rule fails open: a persisted instance whose
    /// flag did not survive the round trip is one the shop will happily buy, stranding the quest.
    /// </remarks>
    public static bool IsQuestItem(ItemInstance? item) =>
        JsonBag.Boolean(item?.State, QuestItemKey);
}
