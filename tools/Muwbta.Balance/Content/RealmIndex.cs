using System.Text.RegularExpressions;
using Muwbta.Server.Building;

namespace Muwbta.Balance.Content;

/// <summary>
/// Which realm an item template belongs to — the tier of the progression it was authored for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing on an item says this, and it has to be inferred.</b> A
/// <c>BundleItemTemplate</c> carries no level, no tier and no realm: the only things placing a
/// sword in the progression are the file it sits in and the convention its key follows. Both are
/// conventions rather than data, so this class is where the guessing is done, once, in the open —
/// and the report prints what it decided so a wrong guess is visible rather than baked into a
/// number.
/// </para>
/// <para>
/// <b>The key wins over the directory.</b> A whole-world export — which is what you get from
/// <c>GET /api/builder/export</c>, and the only way to measure content edited in the builder since
/// the last time somebody wrote <c>content/</c> — is a single file in no realm's directory at all.
/// A model that trusted the directory would work on the repo and silently collapse every item into
/// one tier the moment it was pointed at a live export, which is the run that matters most.
/// </para>
/// </remarks>
public static class RealmIndex
{
    /// <summary>The epic line: <c>epic-warden-3</c> is the third tier's Warden reward.</summary>
    private static readonly Regex EpicKey = new(
        @"^epic-(?:warden|temper|adept|hallow)-(\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Builds the item-to-realm map from the merged bundle, falling back to per-file directory
    /// hints for anything the keys cannot place.
    /// </summary>
    /// <param name="bundle">The merged world.</param>
    /// <param name="directoryHints">Item key to containing directory, gathered before the merge.</param>
    public static Dictionary<string, string> Build(
        WorldBundle bundle,
        IReadOnlyDictionary<string, string> directoryHints)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(directoryHints);

        // Worlds in the order the author put them in, which is the order of the progression.
        // SortOrder is what the client lists them by, so it is already the authored ranking
        // rather than a second one invented here.
        var tiers = bundle.Worlds
            .OrderBy(w => w.SortOrder)
            .Select(w => w.Key)
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in bundle.ItemTemplates)
        {
            result[item.Key] = Resolve(item.Key, tiers, directoryHints);
        }

        return result;
    }

    private static string Resolve(
        string key,
        List<string> tiers,
        IReadOnlyDictionary<string, string> directoryHints)
    {
        // The epic line names its own tier. These are the four Path rewards per realm, and they
        // are the items most likely to be retuned - so getting them into the right tier matters
        // more than for anything else in the file.
        var epic = EpicKey.Match(key);

        if (epic.Success &&
            int.TryParse(epic.Groups[1].Value, out var tier) &&
            tier >= 1 && tier <= tiers.Count)
        {
            return tiers[tier - 1];
        }

        // Otherwise the key's own prefix, against each world key and against that key with a
        // leading article dropped: the world is `the-unlit` and its blades are `unlit-long-blade`.
        foreach (var world in tiers)
        {
            if (key.StartsWith(world + "-", StringComparison.OrdinalIgnoreCase))
            {
                return world;
            }

            var bare = world.StartsWith("the-", StringComparison.OrdinalIgnoreCase)
                ? world[4..]
                : world;

            if (key.StartsWith(bare + "-", StringComparison.OrdinalIgnoreCase))
            {
                return world;
            }
        }

        // Last, the file it arrived in - which is right for the repo and absent for an export.
        if (directoryHints.TryGetValue(key, out var directory))
        {
            var match = tiers.FirstOrDefault(w =>
                string.Equals(w, directory, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    w.StartsWith("the-", StringComparison.OrdinalIgnoreCase) ? w[4..] : w,
                    directory,
                    StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        // Genuinely realm-less: starter kit, quest rewards, anything shared. Named rather than
        // guessed at, so the loadout picker can leave it alone instead of putting a level 1
        // tunic on a level 50 Warden because it happened to sort first.
        return Unplaced;
    }

    /// <summary>An item no realm claims — starter gear, quest items, shared tat.</summary>
    public const string Unplaced = "(unplaced)";
}
