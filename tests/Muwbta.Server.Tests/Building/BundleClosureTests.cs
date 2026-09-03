using Muwbta.Engine.Inhabitants;
using Muwbta.Server.Building;

namespace Muwbta.Server.Tests.Building;

/// <summary>
/// A scoped bundle carries every template the zone it describes actually needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule <c>WorldExporter</c> states about itself:</b> a mob template has no zone, so scoping
/// templates is a closure over references rather than a filter, and the result has to be "a bundle
/// that stands up on its own in an empty database".
/// </para>
/// <para>
/// <b>Shop stock was missing from that closure and nothing noticed.</b> The exporter followed
/// spawners, quest giver/turn-in/required/reward, and loot — but not a shopkeeper's <c>sells</c>
/// list, which is the only thing referencing most of Gatetown's sixteen items. A fresh export of
/// that zone carried zero item templates while the file committed in <c>content/</c> carried all
/// sixteen, because the file predated the closure. Neither was obviously wrong, and a world rebuilt
/// from the fresh one would have imported four shopkeepers with nothing in stock.
/// </para>
/// <para>
/// Asserted against the authored files rather than against a live export, so it needs no database:
/// the question is whether each bundle is self-sufficient, and that is answerable from the bundle.
/// </para>
/// </remarks>
public sealed class BundleClosureTests
{
    /// <summary>The authored bundles, shared with <see cref="BundleFormatTests"/>.</summary>
    public static TheoryData<string> ScopedBundles() => BundleFormatTests.ContentFiles();

    private static WorldBundle Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Muwbta.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var json = File.ReadAllText(Path.Combine(dir!.FullName, relativePath));

        Assert.True(BundleFormat.TryRead(json, out var bundle, out var error), error);
        return bundle!;
    }

    [Theory]
    [MemberData(nameof(ScopedBundles))]
    public void Every_item_a_shopkeeper_sells_travels_with_it(string relativePath)
    {
        var bundle = Read(relativePath);

        var carried = bundle.ItemTemplates
            .Select(i => i.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();

        foreach (var mob in bundle.MobTemplates)
        {
            foreach (var sold in MobBehavior.SellsOf(mob.Behavior))
            {
                if (!carried.Contains(sold))
                {
                    missing.Add($"{mob.Key} sells '{sold}'");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// And every item a mob drops, which is the hop that was already there.
    /// </summary>
    /// <remarks>
    /// Asserted beside the one that was missing rather than trusted, because the two are the same
    /// kind of reference and only one of them had ever been checked.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScopedBundles))]
    public void Every_item_a_mob_drops_travels_with_it(string relativePath)
    {
        var bundle = Read(relativePath);

        var carried = bundle.ItemTemplates
            .Select(i => i.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();

        foreach (var mob in bundle.MobTemplates)
        {
            foreach (var entry in mob.Loot ?? [])
            {
                if (entry.TryGetValue("itemTemplateKey", out var value) &&
                    value?.ToString() is { Length: > 0 } key &&
                    !carried.Contains(key))
                {
                    missing.Add($"{mob.Key} drops '{key}'");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>Every mob a quest names travels with it, and every item it asks for or pays out.</summary>
    [Theory]
    [MemberData(nameof(ScopedBundles))]
    public void Every_template_a_quest_names_travels_with_it(string relativePath)
    {
        var bundle = Read(relativePath);

        var mobs = bundle.MobTemplates.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = bundle.ItemTemplates.Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();

        foreach (var quest in bundle.Quests)
        {
            Check(mobs, quest.GiverMobKey, $"{quest.Key} is given by");
            Check(mobs, quest.TurninMobKey, $"{quest.Key} turns in to");
            Check(items, quest.RequiredItemKey, $"{quest.Key} requires");
            Check(items, quest.RewardItemKey, $"{quest.Key} rewards");
        }

        Assert.Empty(missing);

        void Check(HashSet<string> carried, string? key, string what)
        {
            if (!string.IsNullOrWhiteSpace(key) && !carried.Contains(key))
            {
                missing.Add($"{what} '{key}'");
            }
        }
    }
}
