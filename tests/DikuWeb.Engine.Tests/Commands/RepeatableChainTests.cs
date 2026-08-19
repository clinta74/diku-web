using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// When a repeatable quest may be taken again.
/// </summary>
/// <remarks>
/// Repeatability used to be a property of one quest, which is the wrong unit once quests chain.
/// A player who had finished the first leg and was carrying what it paid out could take that leg
/// again while the second was still open — resetting a step whose consequences were still in
/// play, and leaving the journal describing a state the story cannot be in.
///
/// The rule is the chain, not the leg: repeatable once nothing downstream is still Active. The
/// two ways to clear that are the two the player already has — finish it, or abandon it.
/// </remarks>
public sealed class RepeatableChainTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private const string First = "test.fresh-drink";
    private const string Second = "test.glass-back";
    private const string Third = "test.last-word";

    /// <summary>
    /// A three-link chain, so "downstream" is tested as transitive rather than as one hop.
    /// </summary>
    private static WorldHarness Chain(bool firstRepeatable = true)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        // Every mob and item the chain names has to exist, or the quests are *dormant* (§7.4) and
        // the giver says nothing at all - which looks exactly like the gate under test refusing.
        harness.MobTemplates.Put(new Domain.Inhabitants.MobTemplate
        {
            Key = "other",
            Name = "old man",
            Icon = "o",
        });

        foreach (var key in new[] { "beer", "glass", "note" })
        {
            harness.ItemTemplates.Put(new Domain.Items.ItemTemplate
            {
                Key = key,
                Name = key,
                Icon = "i",
            });
        }

        harness.Quests.Put(new Quest
        {
            Key = First,
            ZoneKey = "test.zone",
            Name = "A Fresh Drink",
            Summary = "Take the old man a beer.",
            GiverMobKey = "giver",
            TurninMobKey = "giver",
            RequiredItemKey = "beer",
            IsRepeatable = firstRepeatable,
            Dialogue = { ["giverOffer"] = "Take him a fresh one." },
        });

        harness.Quests.Put(new Quest
        {
            Key = Second,
            ZoneKey = "test.zone",
            Name = "The Empty Glass",
            Summary = "Return the glass.",
            GiverMobKey = "other",
            TurninMobKey = "giver",
            RequiredItemKey = "glass",
            PrerequisiteQuestKeys = [First],
            IsRepeatable = true,
        });

        harness.Quests.Put(new Quest
        {
            Key = Third,
            ZoneKey = "test.zone",
            Name = "The Last Word",
            Summary = "One more errand.",
            GiverMobKey = "other",
            TurninMobKey = "giver",
            RequiredItemKey = "note",
            PrerequisiteQuestKeys = [Second],
            IsRepeatable = true,
        });

        return harness;
    }

    /// <summary>The giver stands in the room so `talk` has something to reach.</summary>
    private static PlayerActor PlayerWith(
        WorldHarness harness, params (string Key, QuestStatus Status)[] states)
    {
        harness.AddMob("giver", Room, name: "bar maiden");

        var player = harness.AddPlayer("Kael", Room);

        foreach (var (key, status) in states)
        {
            harness.World.SetQuestState(player.Character.Id, key, new CharacterQuest
            {
                CharacterId = player.Character.Id,
                QuestKey = key,
                Status = status,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = status == QuestStatus.Completed ? DateTimeOffset.UtcNow : null,
                TimesCompleted = status == QuestStatus.Completed ? 1 : 0,
            });
        }

        harness.Drain(player);
        return player;
    }

    [Fact]
    public void The_chain_can_be_run_again_once_all_of_it_is_finished()
    {
        var harness = Chain();
        var player = PlayerWith(
            harness,
            (First, QuestStatus.Completed),
            (Second, QuestStatus.Completed),
            (Third, QuestStatus.Completed));

        harness.TakeQuest(player, "maiden", First);

        Assert.Contains("Take him a fresh one", harness.DrainText(player), StringComparison.Ordinal);
        Assert.Equal(
            QuestStatus.Active,
            harness.World.GetQuestState(player.Character.Id, First)?.Status);
    }

    [Fact]
    public void It_cannot_be_taken_again_while_the_next_leg_is_open()
    {
        var harness = Chain();
        var player = PlayerWith(
            harness,
            (First, QuestStatus.Completed),
            (Second, QuestStatus.Active));

        harness.TakeQuest(player, "maiden", First);

        var text = harness.DrainText(player);
        Assert.Contains("finish The Empty Glass first", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Take him a fresh one", text, StringComparison.Ordinal);

        // And the state is untouched - a refusal that half-reset the quest would be worse than
        // no refusal at all.
        Assert.Equal(
            QuestStatus.Completed,
            harness.World.GetQuestState(player.Character.Id, First)?.Status);
    }

    [Fact]
    public void A_leg_two_steps_down_blocks_it_as_well()
    {
        // Downstream is transitive: the third quest never names the first, but it is still in the
        // story the first one starts.
        var harness = Chain();
        var player = PlayerWith(
            harness,
            (First, QuestStatus.Completed),
            (Second, QuestStatus.Completed),
            (Third, QuestStatus.Active));

        harness.TakeQuest(player, "maiden", First);

        Assert.Contains("finish The Last Word first", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Abandoning_the_open_leg_releases_it()
    {
        // The player's other way out, and the one that makes the refusal fair rather than a trap.
        var harness = Chain();
        var player = PlayerWith(
            harness,
            (First, QuestStatus.Completed),
            (Second, QuestStatus.Active));

        harness.Execute(player, "abandon empty glass");
        harness.Drain(player);
        harness.TakeQuest(player, "maiden", First);

        Assert.Contains("Take him a fresh one", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void A_quest_that_is_not_repeatable_stays_finished_whatever_the_chain_does()
    {
        // The second half of the requirement: finishing the whole story must not quietly reopen
        // the parts that were meant to happen once.
        var harness = Chain(firstRepeatable: false);
        var player = PlayerWith(
            harness,
            (First, QuestStatus.Completed),
            (Second, QuestStatus.Completed),
            (Third, QuestStatus.Completed));

        harness.TakeQuest(player, "maiden", First);

        var text = harness.DrainText(player);
        Assert.DoesNotContain("Take him a fresh one", text, StringComparison.Ordinal);
        Assert.Equal(
            QuestStatus.Completed,
            harness.World.GetQuestState(player.Character.Id, First)?.Status);
    }

    [Fact]
    public void A_middle_leg_is_not_offered_again_until_the_one_above_has_run_again()
    {
        // Re-entering a chain in the middle. The old man would otherwise hand out the second
        // errand to somebody holding no glass - and taking the first errand is then blocked by
        // this one being active, so the player has to work out that abandoning is the way out.
        var harness = Chain();
        harness.AddMob("other", Room, name: "old man");

        var player = PlayerWith(
            harness,
            (First, QuestStatus.Completed),
            (Second, QuestStatus.Completed));

        harness.TakeQuest(player, "old man", Second);

        var text = harness.DrainText(player);
        Assert.Contains("that comes later", text, StringComparison.Ordinal);
        Assert.Equal(
            QuestStatus.Completed,
            harness.World.GetQuestState(player.Character.Id, Second)?.Status);
    }

    [Fact]
    public void The_middle_leg_opens_once_the_head_has_been_run_again()
    {
        // TimesCompleted is the counter that says which run you are on: the head at 2 against a
        // middle leg at 1 is a player who has fetched a second beer and delivered it.
        var harness = Chain();
        harness.AddMob("other", Room, name: "old man");

        var player = PlayerWith(harness);

        SetState(harness, player, First, QuestStatus.Completed, timesCompleted: 2);
        SetState(harness, player, Second, QuestStatus.Completed, timesCompleted: 1);
        harness.Drain(player);

        harness.TakeQuest(player, "old man", Second);

        Assert.Equal(
            QuestStatus.Active,
            harness.World.GetQuestState(player.Character.Id, Second)?.Status);
    }

    private static void SetState(
        WorldHarness harness, PlayerActor player, string key, QuestStatus status, int timesCompleted)
        => harness.World.SetQuestState(player.Character.Id, key, new CharacterQuest
        {
            CharacterId = player.Character.Id,
            QuestKey = key,
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            TimesCompleted = timesCompleted,
        });

    [Fact]
    public void A_chain_that_was_never_started_is_offered_normally()
    {
        // The gate must only apply to the re-offer. A first offer has no downstream state to
        // consult, and blocking it would make the whole chain unstartable.
        var harness = Chain();
        var player = PlayerWith(harness);

        harness.TakeQuest(player, "maiden", First);

        Assert.Contains("Take him a fresh one", harness.DrainText(player), StringComparison.Ordinal);
    }
}
