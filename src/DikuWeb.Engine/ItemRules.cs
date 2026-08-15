using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine;

/// <summary>
/// The three restrictions an item template can carry: lore, no-drop, and Path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read from the template rather than stamped onto the instance</b>, which is the opposite of
/// what <see cref="ItemState.IsQuestItem"/> does, and the difference is worth stating. Quest-item
/// is a <em>protection</em>: it exists so a player cannot lose the thing their chain depends on,
/// so it has to survive a builder later unsetting it. These three are <em>restrictions</em>, and a
/// restriction should follow the template — a builder who widens a Path list or lifts a lore flag
/// means it to apply to the copies already out there, not only to ones minted afterwards.
/// </para>
/// <para>
/// They fail open when the template cannot be found. That is the safe direction for a restriction:
/// an unknown template blocking equipment would take a character's gear off them over a cache
/// miss, where the worst case here is one rule not being enforced for one item.
/// </para>
/// </remarks>
public static class ItemRules
{
    /// <summary>Whether a second copy of this template may enter a character's possession.</summary>
    public static bool IsLore(ItemTemplateCache? templates, string? templateKey) =>
        templateKey is not null && templates?.Get(templateKey)?.IsLore == true;

    /// <summary>Whether this item refuses to be put down or handed over.</summary>
    public static bool IsNoDrop(ItemTemplateCache? templates, string? templateKey) =>
        templateKey is not null && templates?.Get(templateKey)?.IsNoDrop == true;

    /// <summary>
    /// Why this Path may not equip this item, or null when they may.
    /// </summary>
    public static string? RefusePath(
        ItemTemplateCache? templates,
        string? templateKey,
        CharacterPath path,
        string article)
    {
        if (templateKey is null || templates?.Get(templateKey) is not { } template)
        {
            return null;
        }

        var allowed = template.Paths;
        if (allowed.Count == 0 || allowed.Contains(path))
        {
            return null;
        }

        // Named, rather than "you cannot use this". A player holding a thing they cannot equip
        // wants to know who can - it is the difference between a dead end and a thing to carry
        // back to somebody.
        var names = allowed.Count == 1
            ? $"a {allowed[0]}"
            : string.Join(" or ", allowed.Select(p => $"a {p}"));

        return $"{article} is not made for you — only {names} can use it.";
    }

    /// <summary>
    /// Whether this character already holds one, counting worn and wielded as well as carried.
    /// </summary>
    /// <remarks>
    /// Equipment counts. Otherwise "one at a time" means one in the pack and one on each hand,
    /// which is the loophole the whole flag exists to close.
    /// </remarks>
    public static bool AlreadyHolds(WorldState world, Guid characterId, string templateKey)
    {
        ArgumentNullException.ThrowIfNull(world);

        return world.InventoryOf(characterId)
            .Any(i => string.Equals(i.TemplateKey, templateKey, StringComparison.Ordinal));
    }
}
