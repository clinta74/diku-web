using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Quests;

/// <summary>
/// Which of a character's own quests an item is actually holding a place for (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// The quest-item flag is a <em>protection</em>: it exists so a player cannot destroy the thing
/// their chain depends on. Read as a property of the item alone it protects far more than that —
/// it protects the ledger of a quest you have never met, will never take, and in the Path-gated
/// chains <b>cannot</b> take.
/// </para>
/// <para>
/// The worst of it is the epic rewards, which are quest items <em>and</em> no-drop <em>and</em>
/// Path-locked. A Shade who finished the Adept chain before the Path gate existed holds a stormrod
/// they cannot wield, cannot drop, cannot sell and cannot destroy — a pack slot with nothing that
/// can ever be done about it.
/// </para>
/// <para>
/// So the question is not "is this a quest item" but "is this item spoken for by a quest
/// <em>this character is on</em>". Only an <see cref="QuestStatus.Active"/> quest can be stranded
/// by a destruction, because only an Active quest is counting.
/// </para>
/// </remarks>
public static class QuestBinding
{
    /// <summary>
    /// Whether the question can be answered at all. A cache that has not loaded knows nothing, and
    /// on a protection that has to read as "spoken for" rather than as "free".
    /// </summary>
    public static bool CanAsk(QuestCache? quests) => quests is { IsLoaded: true };

    /// <summary>
    /// The Active quest of this character that this item is being fetched for, or null if none is.
    /// </summary>
    /// <remarks>
    /// Only the <em>required</em> item counts, never the reward. No quest asks you to go on holding
    /// what it has already paid you, so a reward answers to nothing — which is exactly the
    /// stormrod, and exactly the case this exists to release.
    /// </remarks>
    public static Quest? SpokenFor(
        QuestCache? quests,
        WorldState world,
        Guid characterId,
        string? templateKey)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (templateKey is null || !CanAsk(quests))
        {
            return null;
        }

        foreach (var held in world.QuestsFor(characterId))
        {
            if (held.Status == QuestStatus.Active
                && quests!.Get(held.QuestKey) is { } quest
                && string.Equals(quest.RequiredItemKey, templateKey, StringComparison.Ordinal))
            {
                return quest;
            }
        }

        return null;
    }

    /// <summary>
    /// Why this item may not be destroyed, or null when it may be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shaped like <see cref="ItemRules.RefusePath"/> — a refusal rather than a bool — because the
    /// interesting half is the sentence. <em>"Something stays your hand: the ledger is bound to a
    /// quest"</em> was true of an item no quest of yours had ever wanted; naming the quest is what
    /// turns a refusal into something the player can act on.
    /// </para>
    /// <para>
    /// <b>Fails closed</b>, unlike the restrictions in <see cref="ItemRules"/>, and for the reason
    /// they fail open: those are restrictions, where a cache miss costs one rule unenforced once,
    /// and this is a protection, where it costs a chain that cannot be finished or re-earned.
    /// </para>
    /// </remarks>
    public static string? RefuseDestroy(
        QuestCache? quests,
        WorldState world,
        Guid characterId,
        ItemInstance? item,
        string article)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!ItemState.IsQuestItem(item))
        {
            return null;
        }

        if (!CanAsk(quests))
        {
            return $"Something stays your hand: {article} is bound to a quest.";
        }

        return SpokenFor(quests, world, characterId, item?.TemplateKey) is { } quest
            ? $"Something stays your hand: {article} is what {quest.Name} asks for."
            : null;
    }
}
