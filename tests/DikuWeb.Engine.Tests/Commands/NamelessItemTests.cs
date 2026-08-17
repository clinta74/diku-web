using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// An instance with no name still reads as something (BUGS.md #25).
/// </summary>
/// <remarks>
/// <see cref="ItemInstance.DisplayName"/> exists because the mob half of the same fallback "was
/// written out by hand in a dozen places and missed in two", which is how a player was told
/// "ossara-innkeeper has nothing to say about quests". The item half went on being hand-written at
/// 29 sites, all of them feeding <c>NarrationHelper</c> — and there the failure is worse than a key
/// leak: <c>WithDefiniteArticle("")</c> returns the empty string, so `get` narrated
/// <b>"You take ."</b>
///
/// `ItemSpawner` stamps the name at spawn, so this only bites on an instance built by hand, landed
/// by an import, or loaded from a row written before that — which is precisely the case the
/// property was written to cover.
/// </remarks>
public sealed class NamelessItemTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>An instance carrying no cached name, as a legacy row would arrive.</summary>
    private static ItemInstance Nameless(WorldHarness harness, RoomKey at)
    {
        harness.DefineItem("relic", "a nameless relic", slot: null);

        var item = new ItemInstance
        {
            TemplateKey = "relic",
            TemplateName = string.Empty,
            RoomKey = at.ToString(),
        };

        harness.World.AddItem(item);
        return item;
    }

    [Fact]
    public void Taking_one_does_not_narrate_an_empty_noun()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        Nameless(harness, Room);
        harness.Drain(kael);

        harness.Execute(kael, "get relic");

        var text = harness.DrainText(kael);
        Assert.DoesNotContain("You take .", text, StringComparison.Ordinal);
        Assert.Contains("relic", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Dropping_one_does_not_either()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        Nameless(harness, Room);
        harness.Execute(kael, "get relic");
        harness.Drain(kael);

        harness.Execute(kael, "drop relic");

        var text = harness.DrainText(kael);
        Assert.DoesNotContain("You drop .", text, StringComparison.Ordinal);
        Assert.Contains("relic", text, StringComparison.Ordinal);
    }
}
