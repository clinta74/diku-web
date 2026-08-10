using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Finding where the item name ends and the recipient begins in <c>give a b c</c>.
/// </summary>
/// <remarks>
/// Both halves can be several words and there is no separator between them, so the split has to
/// be found rather than assumed. It used to take the first two whitespace-separated words and
/// discard the rest: <c>give empty glass maiden</c> became item "empty", recipient "glass", and
/// answered "There is no one named glass here." while ignoring the word that named the actual
/// recipient.
///
/// The reason it survived is the more interesting half. <c>give beer old man</c> worked
/// perfectly — "old" prefix-matches the old man, so the discarded word never mattered — and so
/// did every single-word item. Only a two-word item name exposed it.
/// </remarks>
public sealed class GiveParsingTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static PlayerActor Holding(WorldHarness harness, string name, params string[] itemNames)
    {
        var actor = harness.AddPlayer(name, Room);

        foreach (var itemName in itemNames)
        {
            harness.ItemTemplates.Put(new ItemTemplate
            {
                Key = itemName.Replace(' ', '-'),
                Name = itemName,
                Icon = "$",
            });

            harness.GiveItem(actor, harness.ItemTemplates.Get(itemName.Replace(' ', '-'))!);
        }

        return actor;
    }

    /// <summary>
    /// A mob that will accept an item, which means a quest: <c>give</c> hands an item to a mob
    /// only through a turn-in, and to anyone else only if they are a player.
    /// </summary>
    private static void QuestTakerCalled(WorldHarness harness, string mobKey, string mobName, string itemKey)
    {
        harness.AddMob(mobKey, Room, name: mobName);
        harness.Quests.Put(new Quest
        {
            Key = $"test.{mobKey}-errand",
            ZoneKey = "test.zone",
            Name = "An Errand",
            GiverMobKey = mobKey,
            TurninMobKey = mobKey,
            RequiredItemKey = itemKey,
        });
    }

    private static void Accepted(WorldHarness harness, PlayerActor actor, string questKey)
        => harness.World.SetQuestState(actor.Character.Id, questKey, new CharacterQuest
        {
            CharacterId = actor.Character.Id,
            QuestKey = questKey,
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        });

    [Fact]
    public void A_two_word_item_reaches_a_one_word_recipient()
    {
        // The reported shape, end to end.
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", Room);
        var kael = Holding(harness, "Kael", "empty glass");
        harness.Drain(kael);

        harness.Execute(kael, "give empty glass Mira");

        Assert.DoesNotContain("no one named", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Empty(harness.World.InventoryOf(kael.Character.Id));
        Assert.Single(harness.World.InventoryOf(mira.Character.Id));
    }

    [Fact]
    public void A_two_word_item_reaches_a_two_word_recipient()
    {
        var harness = Loaded();
        QuestTakerCalled(harness, "maiden", "bar maiden", "empty-glass");
        var kael = Holding(harness, "Kael", "empty glass");
        Accepted(harness, kael, "test.maiden-errand");
        harness.Drain(kael);

        harness.Execute(kael, "give empty glass bar maiden");

        var text = harness.DrainText(kael);
        Assert.DoesNotContain("no one named", text, StringComparison.Ordinal);
        Assert.Empty(harness.World.InventoryOf(kael.Character.Id));
    }

    [Fact]
    public void A_one_word_item_still_reaches_a_two_word_recipient()
    {
        // What used to work only by luck, kept working.
        var harness = Loaded();
        QuestTakerCalled(harness, "oldman", "old man", "beer");
        var kael = Holding(harness, "Kael", "beer");
        Accepted(harness, kael, "test.oldman-errand");
        harness.Drain(kael);

        harness.Execute(kael, "give beer old man");

        var text = harness.DrainText(kael);
        Assert.DoesNotContain("no one named", text, StringComparison.Ordinal);
        Assert.Empty(harness.World.InventoryOf(kael.Character.Id));
    }

    [Fact]
    public void A_recipient_who_is_not_here_is_named_in_full()
    {
        // The complaint has to quote what the player typed rather than the fragment a bad split
        // left behind. This is the exact message the report showed: it said "glass", because
        // "glass" was word two and everything after it had been thrown away.
        var harness = Loaded();
        var kael = Holding(harness, "Kael", "empty glass");
        harness.Drain(kael);

        harness.Execute(kael, "give empty glass bar maiden");

        Assert.Contains(
            "There is no one named bar maiden here.",
            harness.DrainText(kael),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_you_do_not_have_is_named_in_full()
    {
        var harness = Loaded();
        harness.AddPlayer("Mira", Room);
        var kael = Holding(harness, "Kael", "sharp stick");
        harness.Drain(kael);

        harness.Execute(kael, "give purple banana Mira");

        Assert.Contains(
            "You don't have purple banana.",
            harness.DrainText(kael),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Give_with_only_one_word_still_asks_for_both()
    {
        var harness = Loaded();
        var kael = Holding(harness, "Kael", "beer");
        harness.Drain(kael);

        harness.Execute(kael, "give beer");

        Assert.Contains("Give what to whom?", harness.DrainText(kael), StringComparison.Ordinal);
    }
}
