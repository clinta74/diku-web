using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Server.Building;

namespace DikuWeb.Server.Tests.Building;

/// <summary>
/// A quest turn-in is worth a fraction of a level, everywhere in the game.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the test that was missing.</b> Quest rewards were authored already-scaled — 900 XP in
/// the Rimwalk to 420,000 in the Regard, a 466× spread across fifty levels — and then
/// <c>AwardRewards</c> multiplied them again by the zone's dial. Mob <c>baseXp</c> was authored the
/// other way round, flat from 110 to 799, letting the dial supply the depth; that is why kills land
/// at a steady 42–58 per level in every world and quests did not. One quest in the Regard paid
/// 1,974,000, which at level 48 is forty-one levels.
/// </para>
/// <para>
/// <b>Measured after the multipliers, because that is what the player receives.</b> An assertion on
/// the authored number alone would have passed happily on all of it: 420,000 looks like a large
/// number for a large level, and the ×4.7 that turned it into two million is in a different file.
/// </para>
/// <para>
/// <b>Against the level band, not against a constant.</b> <c>XpForLevel</c> is
/// <c>1000·L·(L−1)/2</c>, so a level costs <c>1000·L</c> and a reward that means the same thing at
/// level 8 and level 48 is a percentage rather than an amount.
/// </para>
/// </remarks>
public sealed class QuestRewardBalanceTests
{
    /// <summary>
    /// The band a turn-in has to land in, as a share of one level.
    /// </summary>
    /// <remarks>
    /// Wide, because these are two different jobs: an opening fetch quest should be smaller than
    /// the finale of a chain and both are correct. The point is not to pin the number but to
    /// notice when one is off by an order of magnitude, which is the failure that actually
    /// happened. Today's content spans 15%–48%.
    /// </remarks>
    private const double Floor = 0.05;

    private const double Ceiling = 0.75;

    /// <summary>
    /// The shipped content, merged the way the importer merges it.
    /// </summary>
    /// <remarks>
    /// Walked up to the solution file rather than hard-coded, matching the eight other content
    /// tests beside this one. That loop being written nine times is worth extracting one day; not
    /// in the same change as a balance fix.
    /// </remarks>
    private static WorldBundle World()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DikuWeb.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var sources = Directory
            .EnumerateFiles(Path.Combine(dir!.FullName, "content"), "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(path =>
            {
                Assert.True(BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var error), error);
                return new BundleSource(path, bundle!);
            })
            .ToList();

        var merged = BundleMerge.Merge(sources);
        Assert.True(merged.Ok, string.Join("\n", merged.Errors));
        return merged.Bundle!;
    }

    /// <summary>What one level costs at <paramref name="level"/>: <c>XpForLevel(L+1) − XpForLevel(L)</c>.</summary>
    private static long Band(int level) =>
        XpProgression.XpForLevel(level + 1) - XpProgression.XpForLevel(level);

    [Fact]
    public void Every_quest_pays_a_sensible_share_of_a_level()
    {
        var bundle = World();

        var worlds = bundle.Worlds.ToDictionary(w => w.Key, StringComparer.Ordinal);
        var zones = bundle.Zones.ToDictionary(z => z.Key, StringComparer.Ordinal);

        var offences = new List<string>();

        foreach (var quest in bundle.Quests)
        {
            Assert.True(
                zones.TryGetValue(quest.ZoneKey, out var zone),
                $"{quest.Key} names a zone that is not in the content: {quest.ZoneKey}");

            Assert.True(
                worlds.TryGetValue(zone!.WorldKey, out var world),
                $"{zone.Key} names a world that is not in the content: {zone.WorldKey}");

            // Through the same path the reward takes, rather than a hand-multiplied copy of it -
            // a test that did its own arithmetic would agree with itself and not with the game.
            var paid = Multipliers.Resolve(
                quest.RewardXp,
                Read(world!.Multipliers),
                Read(zone.Multipliers),
                MultiplierType.Xp);

            var share = (double)paid / Band(zone.MinLevel);

            if (share < Floor || share > Ceiling)
            {
                offences.Add(
                    $"{quest.Key} ({zone.Key}, levels {zone.MinLevel}-{zone.MaxLevel}): "
                    + $"authored {quest.RewardXp} pays {paid}, which is {share * 100:F0}% of a level");
            }
        }

        Assert.True(
            offences.Count == 0,
            $"Quest rewards outside {Floor * 100:F0}%-{Ceiling * 100:F0}% of a level:\n  "
            + string.Join("\n  ", offences));
    }

    /// <summary>
    /// The multiplier is the whole reason the authored numbers must stay small, so it is worth one
    /// assertion of its own: the deepest world scales experience several times over, and an author
    /// who writes a "level-appropriate" number into that is writing it twice.
    /// </summary>
    [Fact]
    public void The_deepest_world_really_does_scale_experience()
    {
        var bundle = World();

        var multipliers = bundle.Worlds
            .Select(w => Read(w.Multipliers).Xp)
            .ToList();

        Assert.Contains(multipliers, m => m > 1m);
    }

    private static Multipliers Read(IReadOnlyDictionary<string, decimal> bag) =>
        new()
        {
            Strength = Value(bag, "strength"),
            Health = Value(bag, "health"),
            Damage = Value(bag, "damage"),
            Xp = Value(bag, "xp"),
            Gold = Value(bag, "gold"),
            ItemValue = Value(bag, "itemValue"),
        };

    private static decimal Value(IReadOnlyDictionary<string, decimal> bag, string key) =>
        bag.TryGetValue(key, out var value) ? value : 1m;
}
