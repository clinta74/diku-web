using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// A quest item can be destroyed unless a quest you are on is counting it (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// The flag alone used to refuse. That protected the ledger of a quest the character had never
/// met, would never take, and — since the Path gate — in the epic chains <b>could not</b> take.
/// </para>
/// <para>
/// <b>The case that forced it</b> is the one <c>QuestPathGateTests</c> described and could not fix:
/// an epic reward is a quest item <em>and</em> no-drop <em>and</em> Path-locked, so a Shade holding
/// an Adept stormrod could not wield it, drop it, sell it or destroy it. A pack slot with nothing
/// that could ever be done about it.
/// </para>
/// </remarks>
public sealed class QuestItemDisposalTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static UpsertQuest Fetch(string key, string name, string requiredItem) =>
        new(key, "test.zone", name, $"Do {name}.", string.Empty,
            GiverMobKey: "vesh",
            TurninMobKey: "vesh",
            RequiredItemKey: requiredItem,
            RequiredCount: 1,
            RewardXp: 10,
            RewardGold: 0,
            RewardItemKey: null,
            RewardItemCount: 1,
            RewardFlagKey: null,
            PrerequisiteQuestKeys: [],
            IsRepeatable: false,
            AutoStart: false,
            Paths: [],
            Dialogue: new Dictionary<string, string>(StringComparer.Ordinal),
            SortOrder: 0);

    private static (WorldHarness Harness, PlayerActor Actor) AtTheSmith(
        CharacterPath path = CharacterPath.Shade)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("vesh", West, name: "vesh");
        return (harness, harness.AddPlayer("Kaeda", West, path: path, level: 20));
    }

    /// <summary>An item template that spawns stamped as a quest item.</summary>
    private static ItemTemplate QuestItem(WorldHarness harness, string key, string name)
    {
        var template = harness.DefineItem(key, name, slot: null);
        template.IsQuestItem = true;
        return template;
    }

    /// <summary>
    /// Gives the character one, stamped the way the spawner stamps it — the flag lives on the
    /// instance, not the template, so a hand-made instance would not be under test at all.
    /// </summary>
    private static ItemInstance Carry(WorldHarness harness, PlayerActor actor, ItemTemplate template)
    {
        var item = harness.GiveItem(actor, template);
        item.State = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [ItemState.QuestItemKey] = true,
        };
        return item;
    }

    private static bool StillCarried(WorldHarness harness, PlayerActor actor, string key) =>
        harness.World.InventoryOf(actor.CharacterId)
            .Any(i => string.Equals(i.TemplateKey, key, StringComparison.Ordinal));

    // -----------------------------------------------------------------------
    // Spoken for
    // -----------------------------------------------------------------------

    /// <summary>The protection that must survive: an Active quest still refuses.</summary>
    [Fact]
    public void An_item_an_active_quest_is_counting_cannot_be_destroyed()
    {
        var (harness, actor) = AtTheSmith();
        var ember = QuestItem(harness, "ember", "a banked ember");
        harness.Mutate(Fetch("errand", "The Banked Ember", "ember"));

        harness.Execute(actor, "talk vesh");
        Carry(harness, actor, ember);

        harness.Drain(actor);
        harness.Execute(actor, "destroy ember");

        Assert.True(StillCarried(harness, actor, "ember"));
        Assert.Contains("stays your hand", harness.DrainText(actor), StringComparison.Ordinal);
    }

    /// <summary>And it says which quest, which is the difference between a wall and a reason.</summary>
    [Fact]
    public void The_refusal_names_the_quest_that_wants_it()
    {
        var (harness, actor) = AtTheSmith();
        var ember = QuestItem(harness, "ember", "a banked ember");
        harness.Mutate(Fetch("errand", "The Banked Ember", "ember"));

        harness.Execute(actor, "talk vesh");
        Carry(harness, actor, ember);

        harness.Drain(actor);
        harness.Execute(actor, "destroy ember");

        Assert.Contains("The Banked Ember", harness.DrainText(actor), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Not spoken for
    // -----------------------------------------------------------------------

    /// <summary>A quest item for a quest you have never taken is yours to be rid of.</summary>
    [Fact]
    public void A_quest_item_for_a_quest_you_are_not_on_can_be_destroyed()
    {
        var (harness, actor) = AtTheSmith();
        var ember = QuestItem(harness, "ember", "a banked ember");
        harness.Mutate(Fetch("errand", "The Banked Ember", "ember"));

        // Never talked to Vesh, so the quest exists and is not theirs.
        Carry(harness, actor, ember);

        harness.Drain(actor);
        harness.Execute(actor, "destroy ember");

        Assert.False(StillCarried(harness, actor, "ember"));
        Assert.Contains("gone for good", harness.DrainText(actor), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The stormrod.</b> A reward is a quest item that no quest ever asks you to keep holding,
    /// so it is destroyable — which is the whole point, since a Path-locked reward on the wrong
    /// character is otherwise permanent.
    /// </summary>
    [Fact]
    public void A_reward_item_is_never_spoken_for_even_while_its_quest_is_active()
    {
        var (harness, actor) = AtTheSmith();
        var stormrod = QuestItem(harness, "stormrod", "an unproven stormrod");

        // A quest that *pays* the stormrod and asks for something else entirely.
        harness.Mutate(Fetch("epic", "The Stormrod", "ember"));
        QuestItem(harness, "ember", "a banked ember");
        harness.Execute(actor, "talk vesh");

        Carry(harness, actor, stormrod);

        harness.Drain(actor);
        harness.Execute(actor, "destroy stormrod");

        Assert.False(StillCarried(harness, actor, "stormrod"));
    }

    /// <summary>
    /// A quest finished no longer counts. Only Active does — a Completed one has already been paid
    /// and cannot be stranded.
    /// </summary>
    [Fact]
    public void A_completed_quests_item_can_be_destroyed()
    {
        var (harness, actor) = AtTheSmith();
        var ember = QuestItem(harness, "ember", "a banked ember");
        harness.Mutate(Fetch("errand", "The Banked Ember", "ember"));

        harness.Execute(actor, "talk vesh");

        var state = harness.World.GetQuestState(actor.CharacterId, "errand");
        Assert.NotNull(state);
        state!.Status = QuestStatus.Completed;

        Carry(harness, actor, ember);

        harness.Drain(actor);
        harness.Execute(actor, "destroy ember");

        Assert.False(StillCarried(harness, actor, "ember"));
    }

    /// <summary>
    /// Somebody else's quest is not yours. Two characters, one holding the quest, and the item in
    /// the other's pack is not protected by it.
    /// </summary>
    [Fact]
    public void Another_characters_quest_does_not_protect_your_copy()
    {
        var (harness, onQuest) = AtTheSmith();
        var ember = QuestItem(harness, "ember", "a banked ember");
        harness.Mutate(Fetch("errand", "The Banked Ember", "ember"));

        harness.Execute(onQuest, "talk vesh");

        var bystander = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 20);
        Carry(harness, bystander, ember);

        harness.Drain(bystander);
        harness.Execute(bystander, "destroy ember");

        Assert.False(StillCarried(harness, bystander, "ember"));
    }

    // -----------------------------------------------------------------------
    // What did not change
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The shop still refuses every quest item.</b> Destroying is disposal and selling is
    /// profit: relaxing the counter too would make any quest item a thing to farm and vendor.
    /// </summary>
    [Fact]
    public void A_shop_still_refuses_a_quest_item_nobody_is_waiting_for()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var actor = harness.AddPlayer("Kaeda", West, path: CharacterPath.Shade, level: 20);

        // Authored the way the database delivers it: the bag goes through AsPersisted,
        // because a bool out of jsonb arrives as a JsonElement and a hand-built bag would
        // not be a shopkeeper at all.
        harness.AddMob("berrin", West, name: "berrin", behavior: WorldHarness.AsPersisted(
            new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["type"] = "npc",
                ["sells"] = new List<object>(),
            }));

        var ember = QuestItem(harness, "ember", "a banked ember");
        Carry(harness, actor, ember);

        harness.Drain(actor);
        harness.Execute(actor, "sell ember");

        Assert.True(StillCarried(harness, actor, "ember"));
    }

    /// <summary>An equipped item is still refused first, whatever the quest situation.</summary>
    [Fact]
    public void An_equipped_quest_item_is_still_refused_for_being_equipped()
    {
        var (harness, actor) = AtTheSmith();
        var charm = QuestItem(harness, "charm", "a bone charm");
        charm.Slots = [ItemSlot.Trinket];

        var item = harness.GiveItem(actor, charm);
        item.State = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [ItemState.QuestItemKey] = true,
        };
        harness.Execute(actor, "wear charm");

        harness.Drain(actor);
        harness.Execute(actor, "destroy charm");

        Assert.True(StillCarried(harness, actor, "charm"));
        Assert.Contains("remove", harness.DrainText(actor), StringComparison.Ordinal);
    }

    /// <summary>An ordinary item is untouched by any of this.</summary>
    [Fact]
    public void An_ordinary_item_is_still_destroyed()
    {
        var (harness, actor) = AtTheSmith();
        harness.GiveItem(actor, harness.DefineItem("rag", "an oily rag", slot: null));

        harness.Execute(actor, "destroy rag");

        Assert.False(StillCarried(harness, actor, "rag"));
    }
}
