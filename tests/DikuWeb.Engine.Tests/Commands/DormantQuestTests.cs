using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// A quest whose content has been deleted (PLAN.md §7.4).
/// </summary>
/// <remarks>
/// Live editing invites exactly this: a builder deletes a mob, and every quest pointing at it is
/// suddenly unfinishable. The rule is that the quest goes quiet and the *player's row survives* —
/// progress is never wiped to tidy up after a content edit.
///
/// Dormancy is derived from what exists rather than stored on the row, so putting the mob back
/// makes the quest live again with no repair pass.
/// </remarks>
public sealed class DormantQuestTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    /// <summary>A giver in the room offering one fetch quest, and a player who can take it.</summary>
    private static (WorldHarness Harness, Engine.World.PlayerActor Player) Ready()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.AddMob("elder", Room, name: "elder");
        harness.DefineItem("ledger", "dusty ledger", slot: null);
        harness.DefineQuest("fetch-ledger", giverMobKey: "elder", requiredItemKey: "ledger");

        var kael = harness.AddPlayer("Kael", Room);
        harness.Drain(kael);

        return (harness, kael);
    }

    [Fact]
    public void A_quest_whose_giver_is_deleted_is_no_longer_offered()
    {
        var (harness, kael) = Ready();
        harness.MobTemplates.Remove("elder");

        harness.TakeQuest(kael, "elder", "fetch-ledger");

        Assert.Null(harness.World.GetQuestState(kael.CharacterId, "fetch-ledger"));
    }

    [Fact]
    public void A_quest_whose_required_item_is_deleted_is_no_longer_offered()
    {
        var (harness, kael) = Ready();
        harness.ItemTemplates.Remove("ledger");

        harness.TakeQuest(kael, "elder", "fetch-ledger");

        Assert.Null(harness.World.GetQuestState(kael.CharacterId, "fetch-ledger"));
    }

    [Fact]
    public void An_in_progress_quest_survives_its_content_being_deleted()
    {
        // The whole point. Deleting a mob must not reach into anybody's journal.
        var (harness, kael) = Ready();
        harness.TakeQuest(kael, "elder", "fetch-ledger");
        Assert.NotNull(harness.World.GetQuestState(kael.CharacterId, "fetch-ledger"));

        harness.ItemTemplates.Remove("ledger");

        var state = harness.World.GetQuestState(kael.CharacterId, "fetch-ledger");
        Assert.NotNull(state);
        Assert.Equal(QuestStatus.Active, state.Status);
    }

    [Fact]
    public void The_journal_marks_a_dormant_quest_unavailable()
    {
        var (harness, kael) = Ready();
        harness.TakeQuest(kael, "elder", "fetch-ledger");
        harness.ItemTemplates.Remove("ledger");
        harness.Drain(kael);

        harness.Execute(kael, "quests");

        var text = harness.DrainText(kael);
        Assert.Contains("fetch-ledger", text, StringComparison.Ordinal);
        Assert.Contains("unavailable", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_journal_does_not_mark_a_healthy_quest_unavailable()
    {
        var (harness, kael) = Ready();
        harness.TakeQuest(kael, "elder", "fetch-ledger");
        harness.Drain(kael);

        harness.Execute(kael, "quests");

        Assert.DoesNotContain("unavailable", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void The_giver_says_the_business_is_closed_rather_than_repeating_the_brief()
    {
        var (harness, kael) = Ready();
        harness.TakeQuest(kael, "elder", "fetch-ledger");
        harness.ItemTemplates.Remove("ledger");
        harness.Drain(kael);

        // Plain talk, not TakeQuest: this reads what the giver says, and the answer is the thing
        // under test.
        harness.Execute(kael, "talk elder");

        Assert.Contains("closed for now", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// Putting the content back revives the quest. This is what deriving dormancy buys: a stored
    /// status would still be marked unavailable here, and would need a repair pass to clear.
    /// </summary>
    [Fact]
    public void Restoring_the_content_makes_the_quest_live_again()
    {
        var (harness, kael) = Ready();
        harness.ItemTemplates.Remove("ledger");
        harness.TakeQuest(kael, "elder", "fetch-ledger");
        Assert.Null(harness.World.GetQuestState(kael.CharacterId, "fetch-ledger"));

        harness.DefineItem("ledger", "dusty ledger", slot: null);
        harness.TakeQuest(kael, "elder", "fetch-ledger");

        Assert.NotNull(harness.World.GetQuestState(kael.CharacterId, "fetch-ledger"));
    }

    [Fact]
    public void A_quest_with_no_required_item_is_not_dormant()
    {
        // RequiredItemKey is nullable on purpose: a talk-only quest is legitimate, and must not
        // read as pointing at a deleted item.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("elder", Room, name: "elder");
        harness.DefineQuest("greet-elder", giverMobKey: "elder", requiredItemKey: null);

        var kael = harness.AddPlayer("Kael", Room);

        harness.TakeQuest(kael, "elder", "greet-elder");

        Assert.NotNull(harness.World.GetQuestState(kael.CharacterId, "greet-elder"));
    }
}
