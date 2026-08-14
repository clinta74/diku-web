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
        // One template reused across zones of different difficulty is what multipliers are for, so
        // a level 1 rat in a level 40 zone with heavy multipliers is a level 40 encounter wearing
        // a level 1 label. The zone's own band is the author's statement of who the content is
        // for, and it is the number that decides.
        //
        // This is the first thing in the codebase that reads Zone.MinLevel.
        var harness = Loaded();
        harness.Zone.MinLevel = 40;
        var player = harness.AddPlayer("Bram", West, level: 40);

        Assert.Equal(1000, Kill(harness, player, mobLevel: 1));
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
    public void A_member_too_far_behind_earns_no_experience()
    {
        // Floor(50) is 25, so a level 24 is outside the window and a level 25 would not be.
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 50);
        var carried = harness.AddPlayer("Kael", West, level: 24);
        Group(harness, killer, carried);

        var mob = harness.AddMob("rat", West, health: 1, level: 50);
        mob.ResolvedXp = 1000;
        mob.ResolvedGold = 40;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(0, carried.Character.Xp);
    }

    [Fact]
    public void A_member_just_inside_the_window_earns_their_share()
    {
        // "a level 50 can group with a level 25 and the level 25 would get exp on kills but a
        // level 24 would not" - PlayTestingNotes, and the reason the floor pays rather than being
        // the first level that does not.
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 50);
        var ally = harness.AddPlayer("Kael", West, level: 25);
        Group(harness, killer, ally);

        var mob = harness.AddMob("rat", West, health: 1, level: 50);
        mob.ResolvedXp = 1000;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.True(ally.Character.Xp > 0);

        // Their half, then tapered by how far the mob was above... below them: the mob is level 50
        // and they are 25, so it is at or above their level and pays in full.
        Assert.Equal(500, ally.Character.Xp);
    }

    [Fact]
    public void Carrying_somebody_does_not_shrink_your_own_share()
    {
        // Members outside the window are dropped before the split, not zeroed after it. Zeroing
        // after would mean the level 50 keeps half of a kill they made alone, which punishes the
        // group for the company it keeps and would make "leave your friend outside" the correct
        // play.
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 50);
        var carried = harness.AddPlayer("Kael", West, level: 10);
        Group(harness, killer, carried);

        var mob = harness.AddMob("rat", West, health: 1, level: 50);
        mob.ResolvedXp = 1000;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(1000, killer.Character.Xp);
    }

    [Fact]
    public void Gold_is_still_split_with_everyone_who_was_there()
    {
        // Experience is credit for the fight; gold is payment for being present. Only the first
        // is level-gated, so a carried member walks away with coin and no experience - which is
        // also the shape that keeps the two rules independently tunable.
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 50);
        var carried = harness.AddPlayer("Kael", West, level: 10);
        Group(harness, killer, carried);

        var mob = harness.AddMob("rat", West, health: 1, level: 50);
        mob.ResolvedXp = 1000;
        mob.ResolvedGold = 40;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(20, carried.Character.Gold);
        Assert.Equal(20, killer.Character.Gold);
        Assert.Equal(0, carried.Character.Xp);
    }

    [Fact]
    public void Being_carried_costs_the_low_level_even_when_they_land_the_blow()
    {
        // The window is set by the highest level present, not by the killer, so a level 24 who
        // finishes a mob while a level 50 does the work still learns nothing. Without this the
        // rule is one line of coordination away from doing nothing at all.
        var harness = Loaded();
        var killer = harness.AddPlayer("Kael", West, level: 24);
        var carrier = harness.AddPlayer("Bram", West, level: 50);
        Group(harness, carrier, killer);

        var mob = harness.AddMob("rat", West, health: 1, level: 40);
        mob.ResolvedXp = 1000;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(0, killer.Character.Xp);
        Assert.Contains("far beyond you", harness.DrainText(killer), StringComparison.Ordinal);
    }

    [Fact]
    public void A_high_level_in_another_room_does_not_set_the_window()
    {
        // Highest level *present*. A party member who is not at the fight already shares nothing,
        // and letting them set the floor anyway would mean one level 50 sitting in town switches
        // off their friends' experience across the whole map.
        var harness = Loaded();
        var killer = harness.AddPlayer("Kael", West, level: 20);
        var elsewhere = harness.AddPlayer("Bram", West, level: 50);
        Group(harness, elsewhere, killer);
        harness.World.Move(elsewhere, East);

        var mob = harness.AddMob("rat", West, health: 1, level: 20);
        mob.ResolvedXp = 1000;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(1000, killer.Character.Xp);
    }
}
