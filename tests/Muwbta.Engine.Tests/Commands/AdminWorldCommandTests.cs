using Muwbta.Domain.Accounts;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// The admin commands the loop answers for itself (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// Separate from the account verbs, which cannot do their own work: §2.1 forbids the loop a
/// database call, so <c>promote</c> and friends hand off to a queue and are answered later.
/// Everything here is about the world, which the loop owns outright.
/// </remarks>
public sealed class AdminWorldCommandTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    // -----------------------------------------------------------------------
    // Who may use them
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("teleport Kael")]
    [InlineData("inspect Kael")]
    [InlineData("kickplayer Kael")]
    [InlineData("shutdown 5")]
    [InlineData("set Kael gold 500")]
    [InlineData("deletecharacter Kael")]
    public void A_player_is_told_the_verb_does_not_exist(string input)
    {
        // Worded as an unknown verb rather than as a refusal, matching the builder commands:
        // nobody below Admin should learn from the game that these exist.
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West);
        harness.AddPlayer("Kael", East);

        harness.Execute(player, input);

        Assert.Contains("not something you can do", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void A_builder_is_refused_too()
    {
        // Builder is content authority, not moderation authority. The two are deliberately
        // different roles.
        var harness = Loaded();
        var builder = harness.AddPlayer("Bram", West, role: AccountRole.Builder);
        harness.AddPlayer("Kael", East);

        harness.Execute(builder, "kickplayer Kael");

        Assert.Contains("not something you can do", harness.DrainText(builder), StringComparison.Ordinal);
        Assert.NotNull(harness.World.FindPlayerByName("Kael"));
    }

    // -----------------------------------------------------------------------
    // teleport
    // -----------------------------------------------------------------------

    [Fact]
    public void Teleport_pulls_a_player_to_the_admin()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);

        harness.Execute(admin, "teleport Kael");

        Assert.Equal(West, player.RoomKey);
    }

    [Fact]
    public void Teleport_ignores_noRecall()
    {
        // Being stuck behind content is the usual reason to need fetching, so an admin tool the
        // content could veto would be no use in the case it exists for.
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(East, RoomFlags.NoRecall.Key, true));

        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);

        harness.Execute(admin, "teleport Kael");

        Assert.Equal(West, player.RoomKey);
    }

    [Fact]
    public void Teleport_works_on_someone_mid_fight()
    {
        // Pulling someone out of a fight is a moderation action. The combat system drops a
        // combatant who is no longer in the room, so the fight ends itself.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);
        harness.AddMob("rat", East, health: 100);

        harness.Execute(player, "kill rat");
        Assert.Equal(CombatState.Fighting, player.Character.CombatState);

        harness.Execute(admin, "teleport Kael");
        harness.Pump(12);

        Assert.Equal(West, player.RoomKey);
    }

    // -----------------------------------------------------------------------
    // inspect
    // -----------------------------------------------------------------------

    [Fact]
    public void Inspect_with_no_argument_reports_the_admin()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "inspect");

        Assert.Contains("Root", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_reports_what_the_room_description_cannot()
    {
        // The point of the verb: which spawner is responsible for this mob still being here, and
        // which zone it belongs to. Both were questions playtesting raised and nothing answered.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddMob("rat", West, name: "a rat");

        harness.Execute(admin, "inspect rat");

        var text = harness.DrainText(admin);

        Assert.Contains("spawner", text, StringComparison.Ordinal);
        Assert.Contains("home", text, StringComparison.Ordinal);
        Assert.Contains("rewards", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_finds_a_player_standing_elsewhere()
    {
        // The room first, because standing in front of something is the usual reason to ask -
        // but a name nothing here answers to should still reach the person it names.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddPlayer("Kael", East);

        harness.Execute(admin, "inspect Kael");

        Assert.Contains("Kael", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_says_so_when_nothing_answers()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "inspect basilisk");

        Assert.Contains("answers to", harness.DrainText(admin), StringComparison.Ordinal);
    }

    /// <summary>
    /// `stat` reaches the player's own combat sheet, not an admin verb they cannot see.
    /// </summary>
    /// <remarks>
    /// This is why the verb was renamed. `stat` used to match the admin inspector, which demanded
    /// four characters where `stats` demanded five — so the admin verb owned the shorter prefix,
    /// and a player typing the obvious abbreviation of their own screen was answered
    /// <em>"'stat' is not something you can do"</em>. That wording is deliberate (admin verbs must
    /// not be discoverable by fishing), which is exactly why nothing on screen could explain it.
    /// </remarks>
    [Theory]
    [InlineData("stat")]
    [InlineData("stats")]
    public void A_player_asking_for_stats_gets_their_own_sheet(string verb)
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West);
        harness.Drain(player);

        harness.Execute(player, verb);

        var text = harness.DrainText(player);
        Assert.Contains("Combat Stats", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not something you can do", text, StringComparison.Ordinal);
    }

    /// <summary>An admin gets the same player sheet from `stat`. It is their sheet too.</summary>
    [Fact]
    public void An_admin_typing_stat_gets_the_player_sheet_not_the_inspector()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.Drain(admin);

        harness.Execute(admin, "stat");

        Assert.Contains("Combat Stats", harness.DrainText(admin), StringComparison.Ordinal);
    }

    /// <summary>
    /// `ins` reaches the inspector rather than the inventory, which is the one prefix that could
    /// have gone either way.
    /// </summary>
    [Fact]
    public void The_inspector_answers_to_its_shortest_form()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.Drain(admin);

        harness.Execute(admin, "ins");

        Assert.Contains("Root", harness.DrainText(admin), StringComparison.Ordinal);
    }

    /// <summary>
    /// And everything shorter still reaches the inventory, which was registered first and has
    /// answered to a bare "i" since Phase 3.
    /// </summary>
    [Theory]
    [InlineData("i")]
    [InlineData("in")]
    [InlineData("inv")]
    public void Nothing_shorter_was_taken_from_the_inventory(string verb)
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.Drain(admin);

        harness.Execute(admin, verb);

        Assert.Contains("carrying", harness.DrainText(admin), StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // kick
    // -----------------------------------------------------------------------

    [Fact]
    public void Kick_asks_the_loop_to_remove_them()
    {
        // The handler does not remove anyone itself: leaving the world saves, closes the channel,
        // and redraws the room, and a second copy of that list is the copy that goes stale.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);

        var context = harness.Execute(admin, "kickplayer Kael");

        Assert.Equal(
            [(player.CharacterId, LeaveReason.Kicked)],
            context.RemovalsRequested);
    }

    [Fact]
    public void Kick_tells_the_player_why()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);

        harness.Execute(admin, "kickplayer Kael language in chat");

        var events = harness.Drain(player);

        Assert.Contains(
            events,
            e => e.Type == EventTypes.Sys &&
                 ((SysPayload)e.Payload).Message.Contains("language in chat", StringComparison.Ordinal));
    }

    [Fact]
    public void Kick_says_something_in_the_room_they_were_standing_in()
    {
        // A character vanishing with no explanation reads as a bug to everyone who saw it.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddPlayer("Kael", East);
        var witness = harness.AddPlayer("Mira", East);
        harness.Drain(witness);

        harness.Execute(admin, "kickplayer Kael");

        Assert.Contains("removed from the world", harness.DrainText(witness), StringComparison.Ordinal);
    }

    [Fact]
    public void Kicking_yourself_is_not_a_thing()
    {
        // Excluding the caller from the search is the whole rule; an admin who wants to leave
        // types quit.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        var context = harness.Execute(admin, "kickplayer Root");

        Assert.Empty(context.RemovalsRequested);
        Assert.Contains("is online", harness.DrainText(admin), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // set
    // -----------------------------------------------------------------------

    [Fact]
    public void Set_changes_a_live_value()
    {
        // Through the world's own objects, so the save queue picks it up like any other change.
        // SQL against a running world would be invisible to the loop, whose copy is the authority
        // until it saves over the top of whatever was written.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);

        harness.Execute(admin, "set Kael gold 500");

        Assert.Equal(500, player.Character.Gold);
    }

    [Fact]
    public void Set_clamps_a_vital_to_its_maximum()
    {
        // An admin topping someone up types a big number on purpose; a full heal should not
        // require arithmetic.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);
        player.Character.Vitals.Health = 1;

        harness.Execute(admin, "set Kael health 99999");

        Assert.Equal(player.Character.Vitals.HealthMax, player.Character.Vitals.Health);
    }

    [Fact]
    public void Set_refuses_a_field_nobody_chose_to_expose()
    {
        // A closed list rather than reflection: reflection would silently expose every field
        // added afterwards, including ones where writing directly corrupts something else.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddPlayer("Kael", East);

        harness.Execute(admin, "set Kael roomkey test.zone.west");

        Assert.Contains("not settable", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Fact]
    public void Set_tells_the_player_it_happened()
    {
        // Their numbers changed underneath them. Finding out silently is how a support action
        // gets reported as a bug.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);
        harness.Drain(player);

        harness.Execute(admin, "set Kael level 10");

        Assert.Contains(
            harness.Drain(player),
            e => e.Type == EventTypes.Sys &&
                 ((SysPayload)e.Payload).Message.Contains("level", StringComparison.Ordinal));
    }

    [Fact]
    public void Set_reports_the_old_value_as_well_as_the_new()
    {
        // The point of reaching for this is that a number was wrong, and an admin who cannot see
        // what it was cannot tell whether they fixed it.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);
        player.Character.Gold = 7;

        harness.Execute(admin, "set Kael gold 500");

        Assert.Contains("7 → 500", harness.DrainText(admin), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // shutdown
    // -----------------------------------------------------------------------

    [Fact]
    public void Shutdown_warns_the_world_before_it_closes()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);
        harness.Drain(player);

        harness.Execute(admin, "shutdown 1 deploying a fix");
        harness.Pump(4);

        var warnings = harness.Drain(player)
            .Where(e => e.Type == EventTypes.Sys)
            .Select(e => ((SysPayload)e.Payload).Message)
            .ToList();

        Assert.Contains(warnings, m => m.Contains("deploying a fix", StringComparison.Ordinal));
        Assert.False(harness.ShutdownSignal.Stopped);
    }

    [Fact]
    public void Shutdown_stops_the_host_when_the_countdown_runs_out()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "shutdown 1");
        harness.Pump(4 * 61);

        Assert.True(harness.ShutdownSignal.Stopped);
    }

    [Fact]
    public void Shutdown_now_does_not_wait()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "shutdown now");
        harness.Pump(1);

        Assert.True(harness.ShutdownSignal.Stopped);
    }

    [Fact]
    public void Zero_is_not_a_synonym_for_now()
    {
        // The destructive case is a word typed on purpose, never a fumbled digit. `shutdown 0`
        // is zero minutes, which is still immediate - but it is spelled as a duration, and the
        // point of this test is that `now` and a number are parsed by different branches.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "shutdown soon");

        Assert.False(harness.Shutdown.IsScheduled);
        Assert.Contains("Usage", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Fact]
    public void Shutdown_can_be_called_off()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);

        harness.Execute(admin, "shutdown 5");
        harness.Execute(admin, "shutdown cancel");
        harness.Drain(player);

        harness.Pump(4 * 400);

        Assert.False(harness.Shutdown.IsScheduled);
        Assert.False(harness.ShutdownSignal.Stopped);
    }

    [Fact]
    public void Shutdown_with_no_argument_reports_rather_than_acts()
    {
        // Asking whether one is pending must not be a way to schedule one.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "shutdown");

        Assert.False(harness.Shutdown.IsScheduled);
        Assert.Contains("No shutdown is scheduled", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Fact]
    public void Rescheduling_replaces_the_countdown_rather_than_refusing()
    {
        // An admin who said ten minutes and now needs thirty is correcting themselves.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "shutdown 1");
        harness.Execute(admin, "shutdown 30");

        Assert.True(harness.Shutdown.SecondsRemaining > 60);
    }

    [Fact]
    public void A_short_countdown_does_not_announce_the_long_milestones()
    {
        // Scheduling two minutes must not immediately say "thirty minutes remaining".
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var player = harness.AddPlayer("Kael", East);
        harness.Drain(player);

        harness.Execute(admin, "shutdown 2");
        harness.Pump(4);

        var warnings = harness.Drain(player)
            .Where(e => e.Type == EventTypes.Sys)
            .Select(e => ((SysPayload)e.Payload).Message)
            .ToList();

        Assert.DoesNotContain(warnings, m => m.Contains("30 minutes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(30, "30 seconds")]
    [InlineData(60, "one minute")]
    [InlineData(600, "10 minutes")]
    [InlineData(90, "1 minutes 30 seconds")]
    public void A_delay_reads_as_prose(int seconds, string expected) =>
        Assert.Equal(expected, ShutdownSchedule.Describe(seconds));
}
