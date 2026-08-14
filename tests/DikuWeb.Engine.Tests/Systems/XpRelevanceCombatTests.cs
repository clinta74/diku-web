using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// What a kill is actually worth, through the world rather than through the formula
/// (PLAN.md §5.3). <c>XpRelevanceTests</c> covers the arithmetic; this covers the wiring — which
/// level reaches the rule, and in what order the zone's multipliers are applied.
/// </summary>
public sealed class XpRelevanceCombatTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static void Group(WorldHarness harness, PlayerActor leader, PlayerActor member)
    {
        harness.Execute(leader, $"group invite {member.Name}");
        harness.Execute(member, "group accept");
    }

    private static long Kill(WorldHarness harness, PlayerActor killer, int mobLevel, int xp = 1000)
    {
        var mob = harness.AddMob("rat", West, health: 1, level: mobLevel);
        mob.ResolvedXp = xp;

        var before = killer.Character.Xp;
        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        return killer.Character.Xp - before;
    }

    // -----------------------------------------------------------------------
    // The mob
    // -----------------------------------------------------------------------

    [Fact]
    public void A_level_ten_learns_nothing_from_a_level_one()
    {
        // The note that started this. Floor(10) is 5, and 1 is well under it.
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West, level: 10);

        Assert.Equal(0, Kill(harness, player, mobLevel: 1));
    }

    [Fact]
    public void A_worthless_kill_says_so()
    {
        // Zero with no explanation is a bug report. The player has to be able to tell "that was
        // beneath you" from "the reward is broken", because only one of them changes what they do.
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West, level: 10);

        Kill(harness, player, mobLevel: 1);

        Assert.Contains(
            "nothing left for you to learn",
            harness.DrainText(player),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_fight_at_your_level_pays_in_full()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West, level: 10);

        Assert.Equal(1000, Kill(harness, player, mobLevel: 10));
    }

    [Fact]
    public void Inside_the_window_it_tapers()
    {
        // Floor(10) is 5, so a level 8 is worth (8-5+1)/(10-5+1) = 4/6 of full.
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West, level: 10);

        Assert.Equal(667, Kill(harness, player, mobLevel: 8));
    }

    [Fact]
    public void A_generous_zone_cannot_make_a_trivial_kill_worth_farming()
    {
        // The load-bearing ordering. Multipliers are resolved into ResolvedXp at spawn (§4.4) and
        // the window is applied to that result - so an experience-multiplied starter zone scales a
        // reward and cannot resurrect a worthless one. Reverse the two and the best experience per
        // hour in the game is a level 50 standing in the newbie field.
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West, level: 50);

        Assert.Equal(0, Kill(harness, player, mobLevel: 2, xp: 250_000));
    }

    // -----------------------------------------------------------------------
    // The zone's band
    // -----------------------------------------------------------------------

    [Fact]
    public void A_zone_that_declares_a_level_band_lifts_the_mobs_in_it()
    {
        // The band is the author's statement of who the content is for, and a flavour critter
        // dropped into a level 40 zone should not read as prey. This is the first thing in the
        // codebase that reads Zone.MinLevel.
        var harness = Loaded();
        harness.Zone.MinLevel = 40;
        var player = harness.AddPlayer("Bram", West, level: 40);

        Assert.Equal(1000, Kill(harness, player, mobLevel: 1));
    }

    [Fact]
    public void Scaling_a_zone_raises_the_level_of_what_spawns_in_it()
    {
        // The other half, and the one the band cannot express: a zone that leaves its band alone
        // and doubles strength has still made its mobs a harder fight, and the level has to follow
        // or the reward is judged against a label nobody is fighting.
        //
        // Strength 4 scales health and damage together, so a level 5 mob is sixteen times the
        // problem it was - four times the level, by the same quadratic the XP curve uses.
        var harness = Loaded();
        harness.Zone.Multipliers.Strength = 4m;
        var player = harness.AddPlayer("Bram", West, level: 20);

        Assert.Equal(1000, Kill(harness, player, mobLevel: 5));
    }

    [Fact]
    public void An_unscaled_zone_leaves_the_authored_level_alone()
    {
        // Every multiplier at 1 has to be exactly the identity, or the derivation quietly retunes
        // every zone nobody has touched.
        var beneath = Loaded();
        Assert.Equal(0, Kill(beneath, beneath.AddPlayer("Bram", West, level: 12), mobLevel: 5));

        var matched = Loaded();
        Assert.Equal(1000, Kill(matched, matched.AddPlayer("Ilse", West, level: 6), mobLevel: 6));
    }

    [Fact]
    public void A_boss_above_its_zones_band_keeps_its_own_level()
    {
        // Floored, not clamped. An author who levels a mob above the band meant it, and pulling it
        // back down would be the rule overruling them in the one direction they were explicit
        // about. Asserted through a low band rather than a high one.
        var harness = Loaded();
        harness.Zone.MinLevel = 1;
        var player = harness.AddPlayer("Bram", West, level: 20);

        Assert.Equal(1000, Kill(harness, player, mobLevel: 45));
    }

    // -----------------------------------------------------------------------
    // The party
    // -----------------------------------------------------------------------

    [Fact]
    public void Help_with_a_fight_you_could_have_taken_is_worth_the_same()
    {
        // The example that removed the party floor. A level 9 beside a level 20 kills a level 19
        // mob: the mob is above the level 9, so alone they would have been paid in full. Being
        // helped cannot be worth less than that, and the earlier rule made it worth nothing.
        var harness = Loaded();
        var carrier = harness.AddPlayer("Bram", West, level: 20);
        var junior = harness.AddPlayer("Kael", West, level: 9);
        Group(harness, carrier, junior);

        var mob = harness.AddMob("rat", West, health: 1, level: 19);
        mob.ResolvedXp = 1000;

        harness.Execute(carrier, "attack rat");
        harness.Pump(20);

        // Half the pot, undiminished: the mob is above the level 9's own level.
        Assert.Equal(500, junior.Character.Xp);
    }

    [Fact]
    public void Each_share_is_scaled_by_that_persons_own_distance_from_the_mob()
    {
        // The split is even; what differs is what the kill was worth to each of them. Level 30
        // against a level 30 mob is full value; level 50 against the same mob is 6/26 of it.
        var harness = Loaded();
        var senior = harness.AddPlayer("Bram", West, level: 50);
        var peer = harness.AddPlayer("Kael", West, level: 30);
        Group(harness, senior, peer);

        var mob = harness.AddMob("rat", West, health: 1, level: 30);
        mob.ResolvedXp = 1000;

        harness.Execute(senior, "attack rat");
        harness.Pump(20);

        Assert.Equal(500, peer.Character.Xp);
        Assert.Equal(115, senior.Character.Xp);
    }

    [Fact]
    public void Gold_is_split_evenly_whatever_the_levels()
    {
        // Gold is payment for being there and is not level-scaled at all, so it stays an even
        // split even where the experience is wildly uneven.
        var harness = Loaded();
        var senior = harness.AddPlayer("Bram", West, level: 50);
        var junior = harness.AddPlayer("Kael", West, level: 10);
        Group(harness, senior, junior);

        var mob = harness.AddMob("rat", West, health: 1, level: 10);
        mob.ResolvedXp = 1000;
        mob.ResolvedGold = 40;

        harness.Execute(senior, "attack rat");
        harness.Pump(20);

        Assert.Equal(20, junior.Character.Gold);
        Assert.Equal(20, senior.Character.Gold);

        // And the level 50 learns nothing from a level 10, group or no group.
        Assert.Equal(500, junior.Character.Xp);
        Assert.Equal(0, senior.Character.Xp);
    }

    [Fact]
    public void A_group_member_in_another_room_still_shares_nothing()
    {
        // Unchanged by any of this: present means standing where it died.
        var harness = Loaded();
        var killer = harness.AddPlayer("Kael", West, level: 20);
        var elsewhere = harness.AddPlayer("Bram", West, level: 50);
        Group(harness, elsewhere, killer);
        harness.World.Move(elsewhere, East);

        var mob = harness.AddMob("rat", West, health: 1, level: 20);
        mob.ResolvedXp = 1000;

        harness.Execute(killer, "attack rat");
        harness.Pump(20);

        Assert.Equal(1000, killer.Character.Xp);
        Assert.Equal(0, elsewhere.Character.Xp);
    }
}
