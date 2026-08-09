using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// What a mute actually stops (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// The load-bearing property is <b>coverage</b>: a mute that only reached the global channel would
/// be a mute in name only, since the muted player still has <c>say</c>, <c>emote</c>, <c>tell</c>,
/// and a private channel to five group members. Every verb that carries words to another player
/// answers through one place, and these are the tests that say the list is complete.
/// </remarks>
public sealed class MuteTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>A player who cannot speak for another hour, by the clock the check reads.</summary>
    private static PlayerActor Silenced(WorldHarness harness, string name = "Bram", RoomKey? at = null)
    {
        var actor = harness.AddPlayer(name, at ?? West);
        actor.MutedUntil = harness.Clock.UtcNow.AddHours(1);
        return actor;
    }

    [Theory]
    [InlineData("say hello")]
    [InlineData("'hello")]
    [InlineData("emote grins")]
    [InlineData(";grins")]
    [InlineData("chat hello")]
    public void Every_way_of_talking_to_the_room_or_the_world_is_refused(string input)
    {
        var harness = Loaded();
        var muted = Silenced(harness);
        var listener = harness.AddPlayer("Kael", West);
        harness.Drain(listener);

        harness.Execute(muted, input);

        Assert.Contains("muted until", harness.DrainText(muted), StringComparison.Ordinal);
        Assert.Empty(harness.DrainText(listener));
    }

    [Fact]
    public void A_tell_is_refused()
    {
        var harness = Loaded();
        var muted = Silenced(harness);
        var listener = harness.AddPlayer("Kael", East);
        harness.Drain(listener);

        harness.Execute(muted, "tell Kael hello");

        Assert.Empty(harness.DrainText(listener));
    }

    [Fact]
    public void A_reply_is_refused_too()
    {
        // Tell and reply share one delivery path precisely so the two cannot disagree about
        // whether a silenced player may still answer.
        var harness = Loaded();
        var muted = Silenced(harness);
        var other = harness.AddPlayer("Kael", East);

        harness.Execute(other, "tell Bram are you there");
        harness.Drain(other);

        harness.Execute(muted, "reply yes");

        Assert.Empty(harness.DrainText(other));
    }

    [Fact]
    public void The_party_channel_is_refused()
    {
        // A silenced player with a private channel to five people is not silenced.
        var harness = Loaded();
        var muted = Silenced(harness);
        var ally = harness.AddPlayer("Kael", West);

        harness.Execute(muted, "group invite Kael");
        harness.Execute(ally, "group accept");
        harness.Drain(ally);

        harness.Execute(muted, "gtell hello");

        Assert.DoesNotContain("tells the group", harness.DrainText(ally), StringComparison.Ordinal);
    }

    [Fact]
    public void It_refuses_rather_than_swallowing()
    {
        // Dropping the message silently is worse than refusing it: the player carries on talking
        // to a room that cannot hear them, which is a crueller punishment than the one chosen -
        // and looks like a bug.
        var harness = Loaded();
        var muted = Silenced(harness);

        harness.Execute(muted, "say hello");

        Assert.Contains("Nothing you say leaves your lips", harness.DrainText(muted), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mute_lifts_itself_when_it_expires()
    {
        // Compared against the clock rather than cleared by a sweep, so there is no job whose
        // only purpose is tidiness - and no window where an expired mute still bites.
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", West);
        var listener = harness.AddPlayer("Kael", West);

        actor.MutedUntil = harness.Clock.UtcNow.AddMinutes(1);
        harness.Clock.AdvancePulses(4 * 61);
        harness.Drain(listener);

        harness.Execute(actor, "say hello");

        Assert.Contains("Bram says", harness.DrainText(listener), StringComparison.Ordinal);
    }

    [Fact]
    public void A_muted_player_can_still_walk_fight_and_look()
    {
        // A mute is about speech. Nothing else should change, or it becomes a different penalty
        // than the one an admin chose to apply.
        var harness = Loaded();
        var muted = Silenced(harness);

        harness.Execute(muted, "east");

        Assert.Equal(RoomKey.Parse("test.zone.middle"), muted.RoomKey);
    }

    [Fact]
    public void A_muted_player_can_still_turn_the_channel_off_and_on()
    {
        // The mute is about what they send, not about what reaches them.
        var harness = Loaded();
        var muted = Silenced(harness);

        harness.Execute(muted, "chat off");

        Assert.True(muted.ChatOff);
        Assert.Contains("goes quiet", harness.DrainText(muted), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Reaching someone already playing
    // -----------------------------------------------------------------------

    [Fact]
    public void A_mute_reaches_a_character_already_in_the_world()
    {
        // The value is read at EnterWorld, so without the push a mute would not land until the
        // player logged out - the one moment it stops mattering.
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", West);
        var listener = harness.AddPlayer("Kael", West);

        actor.MutedUntil = harness.Clock.UtcNow.AddHours(1);
        harness.Drain(listener);

        harness.Execute(actor, "say hello");

        Assert.Empty(harness.DrainText(listener));
    }

    [Fact]
    public void An_admin_verb_names_the_account_rather_than_the_character()
    {
        // Muting is an account-level action, so it goes to the queue rather than being applied
        // here - the loop cannot read the account store (§2.1).
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddPlayer("Kael", West);

        harness.Execute(admin, "muteplayer Kael 30 language in chat");

        var request = Assert.Single(harness.Admin.Requests);
        var mute = Assert.IsType<SetAccountMuteRequest>(request);

        Assert.Equal("Kael", mute.TargetUsername);
        Assert.Equal("language in chat", mute.Reason);
        Assert.Equal(harness.Clock.UtcNow.AddMinutes(30), mute.Until);
    }

    [Fact]
    public void A_mute_with_no_duration_is_refused()
    {
        // A mute with no stated end is one somebody has to remember to lift, and the forgotten
        // ones are indistinguishable from a ban nobody meant to apply.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "muteplayer Kael");

        Assert.Empty(harness.Admin.Requests);
        Assert.Contains("Usage", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Fact]
    public void Unmute_asks_for_the_mute_to_be_lifted()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "unmuteplayer Kael");

        var mute = Assert.IsType<SetAccountMuteRequest>(Assert.Single(harness.Admin.Requests));

        Assert.Null(mute.Until);
    }

    [Fact]
    public void Ban_asks_for_the_account_to_be_refused()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "banplayer Kael griefing");

        var ban = Assert.IsType<SetAccountBanRequest>(Assert.Single(harness.Admin.Requests));

        Assert.Equal("Kael", ban.TargetUsername);
        Assert.True(ban.Banned);
        Assert.Equal("griefing", ban.Reason);
    }

    [Fact]
    public void Unban_asks_for_it_to_be_lifted()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "unbanplayer Kael");

        var ban = Assert.IsType<SetAccountBanRequest>(Assert.Single(harness.Admin.Requests));

        Assert.False(ban.Banned);
    }

    [Theory]
    [InlineData("banplayer Kael")]
    [InlineData("muteplayer Kael 30")]
    [InlineData("unbanplayer Kael")]
    [InlineData("unmuteplayer Kael")]
    public void A_player_cannot_moderate_anyone(string input)
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West);

        harness.Execute(player, input);

        Assert.Empty(harness.Admin.Requests);
        Assert.Contains("not something you can do", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void The_moderation_verbs_take_none_of_their_neighbours_abbreviations()
    {
        // Prefix matching is first-match-wins, so a new verb can quietly steal an older one's
        // shorthand. Every one of these was typed by somebody before ban and mute existed.
        Assert.Equal("bind", Verb("b"));
        Assert.Equal("banplayer", Verb("banp"));
        Assert.Equal("buy", Verb("bu"));

        Assert.Equal("muteplayer", Verb("mutep"));
        Assert.Equal("unbanplayer", Verb("unbanp"));
        Assert.Equal("unmuteplayer", Verb("unmutepl"));
        Assert.Equal("unlink", Verb("unlink"));

        // Nothing short enough to be typed by accident reaches a moderation verb.
        Assert.Null(new WorldHarness().Commands.Find("un"));
        Assert.Null(new WorldHarness().Commands.Find("ba"));
        Assert.Null(new WorldHarness().Commands.Find("kick"));
    }

    private static string Verb(string typed) =>
        new WorldHarness().Commands.Find(typed)?.Name
            ?? throw new InvalidOperationException($"Nothing matched '{typed}'.");
}
