using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// A chain step that starts itself when the step before it is handed in.
/// </summary>
/// <remarks>
/// Declared by the quest that gets started rather than as a list of triggers on the one that
/// starts it, so <c>PrerequisiteQuestKeys</c> remains the only description of what follows what —
/// the storyline panel draws that graph, and a second set of edges would be invisible there with
/// nothing keeping the two in agreement.
///
/// The property that matters most is the last one here: a quest that starts itself must not be
/// able to reach a state the player could not have reached by asking for it. Every rule
/// <c>talk</c> applies is applied.
/// </remarks>
public sealed class AutoStartQuestTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private const string First = "test.fresh-drink";
    private const string Second = "test.glass-back";

    private static WorldHarness Chain(bool autoStart = true, bool repeatable = false)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        foreach (var key in new[] { "beer", "glass" })
        {
            harness.ItemTemplates.Put(new ItemTemplate { Key = key, Name = key, Icon = "i" });
        }

        harness.Quests.Put(new Quest
        {
            Key = First,
            ZoneKey = "test.zone",
            Name = "A Fresh Drink",
            Summary = "Take the old man a beer.",
            GiverMobKey = "oldman",
            TurninMobKey = "oldman",
            RequiredItemKey = "beer",
            IsRepeatable = repeatable,
            Dialogue = { ["turninReady"] = "He sets it down untouched and presses the old glass into your hand." },
        });

        harness.Quests.Put(new Quest
        {
            Key = Second,
            ZoneKey = "test.zone",
            Name = "The Empty Glass",
            Summary = "Return the glass.",
            GiverMobKey = "oldman",
            TurninMobKey = "oldman",
            RequiredItemKey = "glass",
            PrerequisiteQuestKeys = [First],
            IsRepeatable = repeatable,
            AutoStart = autoStart,
            Dialogue = { ["giverOffer"] = "She'll want that back." },
        });

        return harness;
    }

    /// <summary>A player standing with the NPC, holding the beer, mid-way through the first leg.</summary>
    private static PlayerActor ReadyToHandIn(WorldHarness harness)
    {
        harness.AddMob("oldman", Room, name: "old man");
        var player = harness.AddPlayer("Kael", Room);

        harness.World.SetQuestState(player.Character.Id, First, new CharacterQuest
        {
            CharacterId = player.Character.Id,
            QuestKey = First,
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        });

        harness.GiveItem(player, harness.ItemTemplates.Get("beer")!);
        harness.Drain(player);
        return player;
    }

    [Fact]
    public void Handing_in_the_first_leg_opens_the_next_one()
    {
        var harness = Chain();
        var player = ReadyToHandIn(harness);

        harness.Execute(player, "give beer old man");

        var text = harness.DrainText(player);
        Assert.Contains("presses the old glass into your hand", text, StringComparison.Ordinal);
        Assert.Contains("She'll want that back", text, StringComparison.Ordinal);

        Assert.Equal(
            QuestStatus.Active,
            harness.World.GetQuestState(player.Character.Id, Second)?.Status);
    }

    /// <summary>
    /// A step that starts itself speaks its offer, with the markers taken out.
    /// </summary>
    /// <remarks>
    /// The offer is the right line here — nobody pitched this one, so the in-progress instruction
    /// would arrive without its setup — but the words a giver marked are an invitation to take a
    /// quest on, and this one is already in the journal. So they are shown as the prose they are.
    /// </remarks>
    [Fact]
    public void A_step_that_starts_itself_shows_no_markers()
    {
        var harness = Chain();
        harness.Quests.Get(Second)!.Dialogue["giverOffer"] = "She'll want that <back>.";

        var player = ReadyToHandIn(harness);
        harness.Execute(player, "give beer old man");

        var text = harness.DrainText(player);

        Assert.Contains("She'll want that back.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_offer_comes_after_the_turn_in_rather_than_inside_it()
    {
        // Ordering is the whole readability argument: the next errand should read as the
        // consequence of handing this one in, not as an interruption of its rewards.
        var harness = Chain();
        var player = ReadyToHandIn(harness);

        harness.Execute(player, "give beer old man");

        var text = harness.DrainText(player);
        Assert.True(
            text.IndexOf("He sets it down", StringComparison.Ordinal)
            < text.IndexOf("She'll want that back", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_flag_the_next_leg_still_waits_for_a_talk()
    {
        // The opt-out, which is the point of it being per-quest.
        var harness = Chain(autoStart: false);
        var player = ReadyToHandIn(harness);

        harness.Execute(player, "give beer old man");

        Assert.DoesNotContain("She'll want that back", harness.DrainText(player), StringComparison.Ordinal);
        Assert.Null(harness.World.GetQuestState(player.Character.Id, Second));
    }

    [Fact]
    public void It_does_not_start_a_quest_whose_content_has_gone()
    {
        // Dormancy (§7.4) is checked for the same reason `talk` checks it: handing somebody a
        // quest for an item that no longer exists is worse than saying nothing.
        var harness = Chain();
        harness.ItemTemplates.Remove("glass");
        var player = ReadyToHandIn(harness);

        harness.Execute(player, "give beer old man");

        Assert.Null(harness.World.GetQuestState(player.Character.Id, Second));
    }

    [Fact]
    public void It_does_not_start_a_quest_whose_other_prerequisites_are_open()
    {
        // Two prerequisites, only one of them met. The ordinary check does this work, which is
        // exactly why the flag lives on the quest being started rather than on a trigger list.
        var harness = Chain();
        var second = harness.Quests.Get(Second)!;
        second.PrerequisiteQuestKeys = [First, "test.some-other-errand"];
        harness.Quests.Put(second);

        var player = ReadyToHandIn(harness);

        harness.Execute(player, "give beer old man");

        Assert.Null(harness.World.GetQuestState(player.Character.Id, Second));
    }

    [Fact]
    public void It_does_not_restart_a_quest_that_is_already_in_the_journal()
    {
        var harness = Chain();
        var player = ReadyToHandIn(harness);

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        harness.World.SetQuestState(player.Character.Id, Second, new CharacterQuest
        {
            CharacterId = player.Character.Id,
            QuestKey = Second,
            Status = QuestStatus.Active,
            StartedAt = startedAt,
        });

        harness.Execute(player, "give beer old man");

        // Restarting would reset the clock on a quest the player is part-way through.
        Assert.Equal(startedAt, harness.World.GetQuestState(player.Character.Id, Second)?.StartedAt);
    }

    [Fact]
    public void A_finished_leg_that_is_not_repeatable_is_not_started_again()
    {
        var harness = Chain(repeatable: false);
        var player = ReadyToHandIn(harness);

        harness.World.SetQuestState(player.Character.Id, Second, new CharacterQuest
        {
            CharacterId = player.Character.Id,
            QuestKey = Second,
            Status = QuestStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            TimesCompleted = 1,
        });

        harness.Execute(player, "give beer old man");

        Assert.Equal(
            QuestStatus.Completed,
            harness.World.GetQuestState(player.Character.Id, Second)?.Status);
    }

    [Fact]
    public void A_repeatable_leg_starts_again_on_the_next_run_through_the_chain()
    {
        // The second lap. The head has just been handed in for the second time, so the counters
        // say this really is a fresh run rather than a re-entry.
        var harness = Chain(repeatable: true);
        var player = ReadyToHandIn(harness);

        harness.World.SetQuestState(player.Character.Id, First, new CharacterQuest
        {
            CharacterId = player.Character.Id,
            QuestKey = First,
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            TimesCompleted = 1,
        });

        harness.World.SetQuestState(player.Character.Id, Second, new CharacterQuest
        {
            CharacterId = player.Character.Id,
            QuestKey = Second,
            Status = QuestStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            TimesCompleted = 1,
        });

        harness.Drain(player);
        harness.Execute(player, "give beer old man");

        Assert.Equal(
            QuestStatus.Active,
            harness.World.GetQuestState(player.Character.Id, Second)?.Status);

        // And the history is carried, not reset - it is what the repeat gates read.
        Assert.Equal(1, harness.World.GetQuestState(player.Character.Id, Second)?.TimesCompleted);
    }
}
