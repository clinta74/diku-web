using DikuWeb.Domain.Items;

namespace DikuWeb.Engine;

/// <summary>
/// The item-instance state bag's vocabulary, alongside <see cref="Inhabitants.MobBehavior"/>.
/// </summary>
public static class ItemState
{
    /// <summary>The state key marking an instance as bound to a quest.</summary>
    public const string QuestItemKey = "questItem";

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
