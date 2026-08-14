using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Social;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Parties — forming one, what it changes, and what ends it (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// Session-scoped by decision: a party lives in <see cref="WorldState"/> beside combat and active
/// effects, and nothing persists it. That is why the tests about leaving are as important as the
/// ones about joining — a party holding a character who is no longer in the world would be a
/// dangling reference the §4.11 gate would then consult.
/// </remarks>
public sealed class PartyTests
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

    // -----------------------------------------------------------------------
    // Forming one
    // -----------------------------------------------------------------------

    [Fact]
    public void An_invitation_accepted_makes_a_party_of_two()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        Group(harness, leader, member);

        Assert.True(harness.World.Parties.SameParty(leader.CharacterId, member.CharacterId));
        Assert.True(harness.World.Parties.Of(leader.CharacterId)!.IsLeader(leader.CharacterId));
    }

    [Fact]
    public void Nothing_happens_until_it_is_accepted()
    {
        // An invitation is an offer, not a conscription.
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        harness.Execute(leader, "group invite Kael");

        Assert.False(harness.World.Parties.IsGrouped(member.CharacterId));
        Assert.False(harness.World.Parties.IsGrouped(leader.CharacterId));
    }

    [Fact]
    public void You_can_only_invite_someone_standing_with_you()
    {
        // Otherwise an invitation is a way to reach a stranger anywhere in the world, and there
        // is no way for them to stop you sending it.
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        harness.AddPlayer("Kael", East);

        harness.Execute(leader, "group invite Kael");

        Assert.Contains("don't see", harness.DrainText(leader), StringComparison.Ordinal);
    }

    [Fact]
    public void An_invitation_goes_stale()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        harness.Execute(leader, "group invite Kael");
        harness.Clock.AdvancePulses(PartyRegistry.InvitationLifetimePulses + 1);
        harness.Execute(member, "group accept");

        Assert.False(harness.World.Parties.IsGrouped(member.CharacterId));
    }

    [Fact]
    public void Declining_leaves_nothing_behind()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        harness.Execute(leader, "group invite Kael");
        harness.Execute(member, "group decline");
        harness.Execute(member, "group accept");

        Assert.False(harness.World.Parties.IsGrouped(member.CharacterId));
    }

    [Fact]
    public void Someone_already_grouped_cannot_be_poached()
    {
        var harness = Loaded();
        var first = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        var second = harness.AddPlayer("Vurn", West);

        Group(harness, first, member);
        harness.Execute(second, "group invite Kael");

        Assert.Contains("already in a group", harness.DrainText(second), StringComparison.Ordinal);
        Assert.True(harness.World.Parties.SameParty(first.CharacterId, member.CharacterId));
    }

    [Fact]
    public void A_party_holds_six()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);

        for (var i = 0; i < Party.MaxMembers - 1; i++)
        {
            var member = harness.AddPlayer($"Member{i}", West);
            Group(harness, leader, member);
        }

        var seventh = harness.AddPlayer("Late", West);
        harness.Execute(leader, "group invite Late");

        Assert.Equal(Party.MaxMembers, harness.World.Parties.Of(leader.CharacterId)!.Count);
        Assert.False(harness.World.Parties.IsGrouped(seventh.CharacterId));
    }

    // -----------------------------------------------------------------------
    // Leaving
    // -----------------------------------------------------------------------

    [Fact]
    public void The_last_two_leaving_ends_the_party()
    {
        // A party of one is not a party. Leaving someone leading themselves would be a state you
        // could not tell from being grouped, and it changes how a helpful area effect gathers.
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        Group(harness, leader, member);
        harness.Execute(member, "group leave");

        Assert.False(harness.World.Parties.IsGrouped(leader.CharacterId));
        Assert.False(harness.World.Parties.IsGrouped(member.CharacterId));
    }

    [Fact]
    public void Leadership_passes_when_the_leader_walks_out()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var second = harness.AddPlayer("Kael", West);
        var third = harness.AddPlayer("Vurn", West);

        Group(harness, leader, second);
        Group(harness, leader, third);
        harness.Execute(leader, "group leave");

        var party = harness.World.Parties.Of(second.CharacterId);

        Assert.NotNull(party);
        Assert.True(party.IsLeader(second.CharacterId));
        Assert.False(harness.World.Parties.IsGrouped(leader.CharacterId));
    }

    [Fact]
    public void Only_the_leader_may_disband()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        Group(harness, leader, member);
        harness.Execute(member, "group disband");

        Assert.True(harness.World.Parties.SameParty(leader.CharacterId, member.CharacterId));

        harness.Execute(leader, "group disband");

        Assert.False(harness.World.Parties.IsGrouped(leader.CharacterId));
        Assert.False(harness.World.Parties.IsGrouped(member.CharacterId));
    }

    [Fact]
    public void Leaving_the_world_leaves_the_party()
    {
        // WorldState.Remove is the one door out, which is why the cleanup lives there rather than
        // at the four call sites that lead to it. A party still holding a departed character would
        // be a dangling reference the §4.11 gate consults on every hostile action.
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        var third = harness.AddPlayer("Vurn", West);

        Group(harness, leader, member);
        Group(harness, leader, third);

        harness.World.Remove(member);

        Assert.False(harness.World.Parties.IsGrouped(member.CharacterId));
        Assert.False(harness.World.Parties.SameParty(leader.CharacterId, member.CharacterId));
        Assert.True(harness.World.Parties.SameParty(leader.CharacterId, third.CharacterId));
    }

    // -----------------------------------------------------------------------
    // What being grouped changes — §4.11
    // -----------------------------------------------------------------------

    [Fact]
    public void You_cannot_swing_at_someone_in_your_group_even_in_a_pvp_room()
    {
        // The rule §4.11 has always stated and nothing enforced: party members are never valid
        // targets, pvp room or not.
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Pvp.Key, true));

        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        Group(harness, leader, member);
        harness.Drain(leader);

        harness.Execute(leader, "kill Kael");

        Assert.Contains("in your group", harness.DrainText(leader), StringComparison.Ordinal);
        Assert.Equal(CombatState.Idle, leader.Character.CombatState);
    }

    [Fact]
    public void A_rival_in_the_same_pvp_room_is_still_fair_game()
    {
        // The guard has to be about the pair, not about the room, or grouping up would turn the
        // arena off for everyone standing in it.
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Pvp.Key, true));

        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        var rival = harness.AddPlayer("Vurn", West);

        Group(harness, leader, member);
        harness.Execute(leader, "kill Vurn");

        Assert.Equal(CombatState.Fighting, leader.Character.CombatState);
        Assert.NotNull(rival);
    }

    [Fact]
    public void Grouping_up_mid_duel_ends_the_duel()
    {
        // Whether a fight is allowed is re-checked every round, so this follows from the same
        // rule that ends a duel when a builder clears the pvp flag under it.
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Pvp.Key, true));

        var one = harness.AddPlayer("Bram", West);
        var two = harness.AddPlayer("Kael", West);

        harness.Execute(one, "kill Kael");
        Assert.Equal(CombatState.Fighting, one.Character.CombatState);

        Group(harness, one, two);
        harness.Pump(12);

        Assert.Equal(CombatState.Idle, one.Character.CombatState);
    }

    // -----------------------------------------------------------------------
    // The split
    // -----------------------------------------------------------------------

    [Fact]
    public void A_kill_is_split_with_the_group_in_the_room()
    {
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 5);
        var ally = harness.AddPlayer("Kael", West, level: 5);

        Group(harness, killer, ally);

        var mob = harness.AddMob("rat", West, health: 1, level: 5);
        mob.ResolvedXp = 100;
        mob.ResolvedGold = 30;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(50, killer.Character.Xp);
        Assert.Equal(50, ally.Character.Xp);
        Assert.Equal(15, killer.Character.Gold);
        Assert.Equal(15, ally.Character.Gold);
    }

    [Fact]
    public void A_group_member_in_another_room_shares_nothing()
    {
        // Present means standing where it died. A group that could farm by scattering across the
        // map would make the split an exploit rather than a convenience.
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 5);
        var absent = harness.AddPlayer("Kael", West, level: 5);

        Group(harness, killer, absent);
        harness.World.Move(absent, East);

        var mob = harness.AddMob("rat", West, health: 1, level: 5);
        mob.ResolvedXp = 100;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(100, killer.Character.Xp);
        Assert.Equal(0, absent.Character.Xp);
    }

    [Fact]
    public void An_ungrouped_killer_keeps_all_of_it()
    {
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 5);
        harness.AddPlayer("Kael", West, level: 5);

        var mob = harness.AddMob("rat", West, health: 1, level: 5);
        mob.ResolvedXp = 100;
        mob.ResolvedGold = 30;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(100, killer.Character.Xp);
        Assert.Equal(30, killer.Character.Gold);
    }

    [Fact]
    public void An_odd_reward_goes_to_whoever_landed_the_blow()
    {
        // The reward must not shrink by being divided.
        var harness = Loaded();
        var killer = harness.AddPlayer("Bram", West, level: 5);
        var ally = harness.AddPlayer("Kael", West, level: 5);

        Group(harness, killer, ally);

        var mob = harness.AddMob("rat", West, health: 1, level: 5);
        mob.ResolvedXp = 7;

        harness.Execute(killer, "kill rat");
        harness.Pump(20);

        Assert.Equal(4, killer.Character.Xp);
        Assert.Equal(3, ally.Character.Xp);
    }

    // -----------------------------------------------------------------------
    // The party channel
    // -----------------------------------------------------------------------

    [Fact]
    public void Group_chat_reaches_a_member_in_another_room()
    {
        // Which is the point of having a channel that is not the room.
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        Group(harness, leader, member);
        harness.World.Move(member, East);
        harness.Drain(member);

        harness.Execute(leader, "gtell regrouping at the gate");

        Assert.Contains(
            "Bram tells the group, 'regrouping at the gate'",
            harness.DrainText(member),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Group_chat_reaches_nobody_outside_the_group()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        var stranger = harness.AddPlayer("Vurn", West);

        Group(harness, leader, member);
        harness.Drain(stranger);

        harness.Execute(leader, "gtell regrouping at the gate");

        Assert.DoesNotContain("regrouping", harness.DrainText(stranger), StringComparison.Ordinal);
    }
}
