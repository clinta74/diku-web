using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Giving up a quest, and reading one in detail.
/// </summary>
/// <remarks>
/// Both come from the same report. A chain makes an abandoned leg a soft-lock built out of
/// dialogue: prerequisites block everything behind it, the journal lists it Active for ever, and
/// the giver answers with its in-progress line. Nothing in the game could clear that state.
///
/// The detail half is the older bug. <c>quest &lt;name&gt;</c> resolved to the <c>quests</c>
/// journal, so it printed the whole list whatever you asked about — see
/// <see cref="VerbReachabilityTests"/> for why, and for the guard that stops it recurring.
/// </remarks>
public sealed class AbandonQuestTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private const string Key = "test.fresh-drink";

    private static WorldHarness Harness()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Quests.Put(new Quest
        {
            Key = Key,
            ZoneKey = "test.zone",
            Name = "A Fresh Drink",
            Summary = "Take the old man a beer.",
            GiverMobKey = "bar-maiden",
            TurninMobKey = "old-man",
            RequiredItemKey = "beer",
            RequiredCount = 1,
            RewardXp = 10,
        });

        return harness;
    }

    private static PlayerActor WithQuest(
        WorldHarness harness, QuestStatus status, int timesCompleted = 0)
    {
        var player = harness.AddPlayer("Kael", Room);

        harness.World.SetQuestState(player.Character.Id, Key, new CharacterQuest
        {
            CharacterId = player.Character.Id,
            QuestKey = Key,
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = status == QuestStatus.Completed ? DateTimeOffset.UtcNow : null,
            TimesCompleted = timesCompleted,
        });

        harness.Drain(player);
        return player;
    }

    [Fact]
    public void Abandoning_an_active_quest_returns_it_to_never_started()
    {
        var harness = Harness();
        var player = WithQuest(harness, QuestStatus.Active);

        harness.Execute(player, "abandon fresh");

        // §6 spells "not started" as the absence of a row, which is what makes the giver offer it
        // again with no new status and no migration.
        Assert.Null(harness.World.GetQuestState(player.Character.Id, Key));
        Assert.Contains("give up on A Fresh Drink", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Abandoning_reaches_storage_as_a_delete()
    {
        var harness = Harness();
        var player = WithQuest(harness, QuestStatus.Active);

        harness.Execute(player, "abandon fresh");

        // Forgetting it in memory alone would have the quest reappear Active on the next load,
        // which is the same bug the turn-in path already carries a comment about.
        Assert.Contains((player.Character.Id, Key), harness.QuestSaves.Deleted);
    }

    [Fact]
    public void A_repeatable_quest_already_finished_keeps_its_history()
    {
        var harness = Harness();
        var player = WithQuest(harness, QuestStatus.Active, timesCompleted: 3);

        harness.Execute(player, "abandon fresh");

        // Deleting the row would erase TimesCompleted, so this one reverts rather than vanishing.
        var state = harness.World.GetQuestState(player.Character.Id, Key);
        Assert.NotNull(state);
        Assert.Equal(QuestStatus.Completed, state.Status);
        Assert.Equal(3, state.TimesCompleted);
        Assert.DoesNotContain((player.Character.Id, Key), harness.QuestSaves.Deleted);
    }

    [Fact]
    public void A_finished_quest_cannot_be_abandoned()
    {
        var harness = Harness();
        var player = WithQuest(harness, QuestStatus.Completed);

        harness.Execute(player, "abandon fresh");

        var text = harness.DrainText(player);
        Assert.Contains("already finished", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(harness.World.GetQuestState(player.Character.Id, Key));
    }

    [Fact]
    public void Abandoning_a_quest_you_do_not_have_says_so()
    {
        var harness = Harness();
        var player = harness.AddPlayer("Kael", Room);
        harness.Drain(player);

        harness.Execute(player, "abandon fresh");

        Assert.Contains("don't have that quest", harness.DrainText(player), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Abandon_with_no_argument_asks_rather_than_guessing()
    {
        var harness = Harness();
        var player = WithQuest(harness, QuestStatus.Active);

        harness.Execute(player, "abandon");

        Assert.Contains("which quest", harness.DrainText(player), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(harness.World.GetQuestState(player.Character.Id, Key));
    }

    [Fact]
    public void Abandon_keeps_its_abbreviation_clear_of_abilities()
    {
        // "ab" is documented as the abbreviation for `abilities`, so abandon asks for three
        // characters. Losing a quest to a stray keypress meant for a spell list would be a poor
        // trade for two saved letters.
        var registry = new WorldHarness().Commands;

        Assert.Equal("abilities", registry.Find("ab")?.Name);
        Assert.Equal("abandon", registry.Find("aba")?.Name);
    }

    [Fact]
    public void Quest_by_name_shows_that_quest_rather_than_the_journal()
    {
        var harness = Harness();
        var player = WithQuest(harness, QuestStatus.Active);

        harness.Execute(player, "quest fresh");

        var text = harness.DrainText(player);
        Assert.Contains("A Fresh Drink", text, StringComparison.Ordinal);
        Assert.Contains("Take the old man a beer", text, StringComparison.Ordinal);

        // The journal's own heading is what used to come back instead.
        Assert.DoesNotContain("=== Your Quests ===", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Bare_quest_shows_the_journal()
    {
        // The two verbs are one family and the argument distinguishes them, so the shorter word
        // gives the more useful answer rather than "Which quest?".
        var harness = Harness();
        var player = WithQuest(harness, QuestStatus.Active);

        harness.Execute(player, "quest");

        Assert.Contains("=== Your Quests ===", harness.DrainText(player), StringComparison.Ordinal);
    }
}
