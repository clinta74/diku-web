using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Quest payout: the full talk → collect → give loop, with the zone's difficulty dial applied
/// (PLAN.md §4, §7.5).
/// </summary>
/// <remarks>
/// Combat awards <c>mob.ResolvedXp</c>, so a zone that triples XP triples what its mobs are
/// worth. Quest rewards were paid raw, which made quests the one thing in a zone the dial did
/// not move - invisible unless you compared the two side by side.
/// </remarks>
public sealed class QuestRewardTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>A giver in the room, an accepted quest, and the required item in hand.</summary>
    private static (WorldHarness Harness, Muwbta.Engine.World.PlayerActor Player) ReadyToTurnIn(
        Action<WorldHarness> configure,
        int rewardXp = 0,
        int rewardGold = 0,
        string? rewardItemKey = null,
        int rewardItemCount = 1)
    {
        var harness = Loaded();
        configure(harness);

        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("elder", Room, name: "elder");
        harness.DefineQuest(
            "fetch-ledger",
            giverMobKey: "elder",
            requiredItemKey: "ledger",
            rewardXp: rewardXp,
            rewardGold: rewardGold,
            rewardItemKey: rewardItemKey,
            rewardItemCount: rewardItemCount);

        var ledger = harness.DefineItem("ledger", "dusty ledger", slot: null);
        harness.GiveItem(kael, ledger);

        harness.TakeQuest(kael, "elder", "fetch-ledger");
        harness.Drain(kael);

        return (harness, kael);
    }

    [Fact]
    public void Talking_to_the_giver_starts_the_quest()
    {
        // Guards the harness itself: QuestCache used to be null here, so Talk returned on its
        // first line and every quest test below would have passed without running any of this.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("elder", Room, name: "elder");

        // The ledger has to exist, or the quest is dormant and correctly never offered (§7.4).
        harness.DefineItem("ledger", "dusty ledger", slot: null);
        harness.DefineQuest("fetch-ledger", giverMobKey: "elder", requiredItemKey: "ledger");
        harness.Drain(kael);

        harness.TakeQuest(kael, "elder", "fetch-ledger");

        Assert.NotNull(harness.World.GetQuestState(kael.CharacterId, "fetch-ledger"));
    }

    [Fact]
    public void Xp_and_gold_are_scaled_by_the_zone()
    {
        var (harness, kael) = ReadyToTurnIn(
            h => h.SetZoneMultipliers(m =>
            {
                m.Xp = 3m;
                m.Gold = 2m;
            }),
            rewardXp: 100,
            rewardGold: 50);

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(300, kael.Character.Xp);
        Assert.Equal(100, kael.Character.Gold);
    }

    [Fact]
    public void A_neutral_zone_pays_the_authored_numbers()
    {
        var (harness, kael) = ReadyToTurnIn(_ => { }, rewardXp: 100, rewardGold: 50);

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(100, kael.Character.Xp);
        Assert.Equal(50, kael.Character.Gold);
    }

    [Fact]
    public void The_scaled_amounts_are_the_ones_narrated()
    {
        // Paying one number and announcing another is worse than not scaling at all.
        var (harness, kael) = ReadyToTurnIn(
            h => h.SetZoneMultipliers(m => m.Xp = 3m),
            rewardXp: 100);

        harness.Execute(kael, "give ledger elder");

        Assert.Contains("300 experience", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // The relevance window (PLAN.md §4.7)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>Half the rule was in place and half was not.</b> A level 50 killing a level 28 mob earns
    /// a fraction; the same level 50 turning in that zone's quest was earning all of it, so a chain
    /// of them was the best experience in the game for somebody with no business being there.
    /// </summary>
    [Fact]
    public void An_over_level_character_earns_a_fraction_of_a_quest()
    {
        var (harness, kael) = ReadyToTurnIn(h => Band(h, 8, 15), rewardXp: 1000);

        kael.Character.Level = 20;
        var before = kael.Character.Xp;

        harness.Execute(kael, "give ledger elder");

        // Floor(20) is 10, so a level 15 zone sits at (15-10+1)/(20-10+1) of the way up the taper.
        Assert.Equal(545, kael.Character.Xp - before);
    }

    /// <summary>
    /// <b>The top of the band decides, not the bottom.</b> A quest belongs to the whole range its
    /// author declared, so measuring against <c>MinLevel</c> would dock a player for having
    /// finished the zone the quest is in — the opposite of the intent.
    /// </summary>
    [Fact]
    public void A_character_at_the_top_of_the_zones_band_is_still_paid_in_full()
    {
        var (harness, kael) = ReadyToTurnIn(h => Band(h, 8, 15), rewardXp: 1000);

        kael.Character.Level = 15;
        var before = kael.Character.Xp;

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(1000, kael.Character.Xp - before);
    }

    /// <summary>Under-level is never penalised — there is no bonus for punching up, and no tax.</summary>
    [Fact]
    public void A_character_below_the_band_is_paid_in_full()
    {
        var (harness, kael) = ReadyToTurnIn(h => Band(h, 8, 15), rewardXp: 1000);

        kael.Character.Level = 8;
        var before = kael.Character.Xp;

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(1000, kael.Character.Xp - before);
    }

    /// <summary>
    /// Gold is not touched by the window, matching kills (§4.7): experience is credit for the
    /// fight and gold is payment for being there.
    /// </summary>
    [Fact]
    public void Gold_is_not_reduced_for_an_over_level_character()
    {
        var (harness, kael) = ReadyToTurnIn(h => Band(h, 8, 15), rewardXp: 1000, rewardGold: 200);

        kael.Character.Level = 20;
        var before = kael.Character.Gold;

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(200, kael.Character.Gold - before);
    }

    /// <summary>Declares the zone's intended level range, which is what the window measures against.</summary>
    private static void Band(WorldHarness harness, int min, int max)
    {
        harness.Zone.MinLevel = min;
        harness.Zone.MaxLevel = max;
    }

    [Fact]
    public void A_reward_item_arrives_once_not_once_per_copy()
    {
        var (harness, kael) = ReadyToTurnIn(
            h => h.DefineItem("boots", "leather boots", slot: null),
            rewardItemKey: "boots",
            rewardItemCount: 3);

        harness.Execute(kael, "give ledger elder");

        var text = harness.DrainText(kael);
        var announcements = text.Split("You receive").Length - 1;

        // The Reply sat inside the spawn loop, so three boots announced themselves three times.
        Assert.Equal(1, announcements);
        Assert.Contains("3 x leather boots", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_full_count_of_reward_items_reaches_the_pack()
    {
        var (harness, kael) = ReadyToTurnIn(
            h => h.DefineItem("boots", "leather boots", slot: null),
            rewardItemKey: "boots",
            rewardItemCount: 3);

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(
            3,
            harness.World.InventoryOf(kael.CharacterId).Count(i => i.TemplateKey == "boots"));
    }

    [Fact]
    public void A_single_reward_item_is_narrated_with_an_article()
    {
        var (harness, kael) = ReadyToTurnIn(
            h => h.DefineItem("boots", "leather boots", slot: null),
            rewardItemKey: "boots",
            rewardItemCount: 1);

        harness.Execute(kael, "give ledger elder");

        // "You receive 1 x leather boots" is how a spreadsheet talks.
        Assert.Contains("You receive a leather boots", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reward_item_whose_template_is_gone_says_so_instead_of_paying_nothing()
    {
        // The old code returned in silence here, so the quest completed and the promised reward
        // simply never appeared - indistinguishable from the quest being designed that way.
        var (harness, kael) = ReadyToTurnIn(_ => { }, rewardItemKey: "ghost-boots");

        harness.Execute(kael, "give ledger elder");

        var text = harness.DrainText(kael);
        Assert.Contains("ghost-boots", text, StringComparison.Ordinal);
        Assert.Contains("Tell a builder", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_quest_still_completes_when_its_reward_item_is_missing()
    {
        // Refusing to complete would strand the player holding an item the giver will not take.
        var (harness, kael) = ReadyToTurnIn(_ => { }, rewardXp: 10, rewardItemKey: "ghost-boots");

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(
            Muwbta.Domain.Quests.QuestStatus.Completed,
            harness.World.GetQuestState(kael.CharacterId, "fetch-ledger")!.Status);
        Assert.Equal(10, kael.Character.Xp);
    }

    [Fact]
    public void The_required_item_is_taken_and_deleted_from_storage()
    {
        var (harness, kael) = ReadyToTurnIn(_ => { });

        harness.Execute(kael, "give ledger elder");

        Assert.DoesNotContain(
            harness.World.InventoryOf(kael.CharacterId),
            i => i.TemplateKey == "ledger");
        Assert.Single(harness.ItemSaves.Deleted);
    }

    [Fact]
    public void A_zone_multiplier_of_zero_pays_nothing_rather_than_the_base_amount()
    {
        // A builder who dials a zone to zero means it. Falling back to the authored number here
        // would make the dial look broken.
        var (harness, kael) = ReadyToTurnIn(
            h => h.SetZoneMultipliers(m => m.Xp = 0m),
            rewardXp: 100);

        harness.Execute(kael, "give ledger elder");

        Assert.Equal(0, kael.Character.Xp);
    }
}
