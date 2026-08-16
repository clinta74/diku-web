using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Presentation;

/// <summary>
/// The group roster the client draws under a player's own meters (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// The frame is compared rather than pushed, for the reason <c>SendVitalsIfChanged</c> gives: what
/// moves it is every event that touches any member's vitals, times everyone who can see them. So
/// the tests that matter are the negative ones — an unchanged group must produce no traffic at all,
/// or a party of six is thirty frames a second between them.
/// </remarks>
public sealed class PartyFrameTests
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

    private static PartyPayload? LastParty(WorldHarness harness, PlayerActor actor) =>
        harness.Drain(actor)
            .Where(e => e.Type == EventTypes.Party)
            .Select(e => (PartyPayload)e.Payload)
            .LastOrDefault();

    [Fact]
    public void An_ungrouped_player_is_sent_nothing()
    {
        var harness = Loaded();
        var alone = harness.AddPlayer("Bram", West);

        PlayerView.SendPartyIfChanged(harness.World, alone);

        Assert.Null(LastParty(harness, alone));
    }

    [Fact]
    public void A_grouped_player_is_sent_the_whole_roster_including_themselves()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        Group(harness, leader, member);
        harness.Drain(leader);

        PlayerView.SendPartyIfChanged(harness.World, leader);

        var party = LastParty(harness, leader);
        Assert.NotNull(party);
        Assert.Equal(["Bram", "Kael"], party.Members.Select(m => m.Name));
        Assert.True(party.Members[0].IsLeader);
        Assert.False(party.Members[1].IsLeader);
    }

    [Fact]
    public void Every_vital_is_carried()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        Group(harness, leader, member);

        var vitals = member.Character.Vitals;
        vitals.Health = 11;
        vitals.Focus = 7;
        vitals.Stamina = 33;

        harness.Drain(leader);
        PlayerView.SendPartyIfChanged(harness.World, leader);

        var kael = LastParty(harness, leader)!.Members.Single(m => m.Name == "Kael");
        Assert.Equal(11, kael.Health);
        Assert.Equal(7, kael.Focus);
        Assert.Equal(33, kael.Stamina);
        Assert.Equal(vitals.HealthMax, kael.HealthMax);
        Assert.Equal(vitals.FocusMax, kael.FocusMax);
        Assert.Equal(vitals.StaminaMax, kael.StaminaMax);
    }

    /// <summary>
    /// The whole point of comparing rather than pushing. A group standing still costs nothing.
    /// </summary>
    [Fact]
    public void An_unchanged_roster_is_not_resent()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        Group(harness, leader, member);

        PlayerView.SendPartyIfChanged(harness.World, leader);
        harness.Drain(leader);

        PlayerView.SendPartyIfChanged(harness.World, leader);
        PlayerView.SendPartyIfChanged(harness.World, leader);

        Assert.Null(LastParty(harness, leader));
    }

    [Fact]
    public void A_member_taking_damage_moves_the_frame()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        Group(harness, leader, member);

        PlayerView.SendPartyIfChanged(harness.World, leader);
        harness.Drain(leader);

        member.Character.Vitals.Health -= 1;
        PlayerView.SendPartyIfChanged(harness.World, leader);

        Assert.NotNull(LastParty(harness, leader));
    }

    /// <summary>
    /// <c>Here</c> is answered from the viewer's room, so the two of them get different frames.
    /// </summary>
    [Fact]
    public void Whether_a_member_is_present_is_asked_of_the_viewer()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        // Grouped in one room and then split up: an invitation is something said to someone
        // standing in front of you, so there is no way to form this party across two rooms.
        Group(harness, leader, member);
        harness.World.Move(member, East);

        harness.Drain(leader);
        harness.Drain(member);
        PlayerView.SendPartyIfChanged(harness.World, leader);
        PlayerView.SendPartyIfChanged(harness.World, member);

        var asLeaderSeesIt = LastParty(harness, leader)!.Members;
        var asMemberSeesIt = LastParty(harness, member)!.Members;

        Assert.True(asLeaderSeesIt.Single(m => m.Name == "Bram").Here);
        Assert.False(asLeaderSeesIt.Single(m => m.Name == "Kael").Here);

        Assert.False(asMemberSeesIt.Single(m => m.Name == "Bram").Here);
        Assert.True(asMemberSeesIt.Single(m => m.Name == "Kael").Here);
    }

    /// <summary>
    /// An empty roster is how leaving arrives, and it is what clears the panel.
    /// </summary>
    [Fact]
    public void Leaving_sends_an_empty_roster_rather_than_nothing()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        Group(harness, leader, member);

        PlayerView.SendPartyIfChanged(harness.World, member);
        harness.Drain(member);

        harness.Execute(member, "group leave");
        PlayerView.SendPartyIfChanged(harness.World, member);

        var party = LastParty(harness, member);
        Assert.NotNull(party);
        Assert.Empty(party.Members);
    }

    /// <summary>
    /// A member who left the world entirely is skipped rather than drawn as a blank row.
    /// </summary>
    [Fact]
    public void A_member_no_longer_in_the_world_is_left_out()
    {
        var harness = Loaded();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);
        var third = harness.AddPlayer("Mira", West);
        harness.Execute(leader, $"group invite {member.Name}");
        harness.Execute(member, "group accept");
        harness.Execute(leader, $"group invite {third.Name}");
        harness.Execute(third, "group accept");

        harness.World.Remove(third);

        harness.Drain(leader);
        PlayerView.SendPartyIfChanged(harness.World, leader);

        var party = LastParty(harness, leader);
        Assert.NotNull(party);
        Assert.Equal(["Bram", "Kael"], party.Members.Select(m => m.Name));
    }
}
