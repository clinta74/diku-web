using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// A template key is an authoring identifier and must never be the word a player reads.
/// </summary>
/// <remarks>
/// Reported from play: <c>talk corun</c> answered <em>"ossara-innkeeper has nothing to say about
/// quests"</em> about a character the room had introduced one line earlier as Corun, who keeps the
/// fire. The fallback to the key exists — a nameless mob is unmatchable by every verb that takes
/// one, so the key at least tells you what to type — but it was written out by hand at a dozen call
/// sites and missed at two of them. It now lives on <c>Mob.DisplayName</c>, and these are the two
/// sites that were wrong.
/// </remarks>
public sealed class MobDisplayNameTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static (WorldHarness Harness, Engine.World.PlayerActor Player) WithInnkeeper(
        string name = "Corun, who keeps the fire")
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("ossara-innkeeper", Room, name: name);

        // Somebody else's quest, so the cache counts as loaded. Without one `talk` short-circuits
        // on "Quests are not available" and never reaches the wording under test.
        harness.DefineItem("ledger", "dusty ledger", slot: null);
        harness.DefineQuest("someone-elses", giverMobKey: "elder", requiredItemKey: "ledger");

        var kael = harness.AddPlayer("Kael", Room);
        harness.Drain(kael);

        return (harness, kael);
    }

    [Fact]
    public void An_npc_with_no_quests_is_refused_by_name()
    {
        var (harness, kael) = WithInnkeeper();

        harness.Execute(kael, "talk corun");

        var text = harness.DrainText(kael);
        Assert.Contains("Corun, who keeps the fire has nothing to say", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ossara-innkeeper", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_npc_whose_quests_are_all_unavailable_is_refused_by_name()
    {
        // The second wording, reached when the giver has quests but none of them are offerable -
        // prerequisites unmet, or already completed. Separately worded, so separately wrong.
        var (harness, kael) = WithInnkeeper();
        var quest = harness.DefineQuest(
            "second-errand",
            giverMobKey: "ossara-innkeeper",
            requiredItemKey: "ledger");
        quest.PrerequisiteQuestKeys = ["an-errand-never-run"];

        harness.Execute(kael, "talk corun");

        var text = harness.DrainText(kael);
        Assert.Contains("Corun, who keeps the fire has nothing to say", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ossara-innkeeper", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nameless_mob_still_falls_back_to_its_key()
    {
        // The fallback the property keeps. Without it the refusal names nothing at all, and the
        // player has no string to type at the verb that just refused them.
        var (harness, kael) = WithInnkeeper(name: "");

        harness.Execute(kael, "talk ossara-innkeeper");

        Assert.Contains(
            "ossara-innkeeper has nothing to say",
            harness.DrainText(kael),
            StringComparison.Ordinal);
    }
}
