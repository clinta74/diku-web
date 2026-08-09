using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Inhabitants;

/// <summary>
/// Idle prose, and how often each line is heard (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// Every mob in the game used to share one interval — sixteen pulses, four seconds — so a
/// shopkeeper's sales pitch and a rat's squeak arrived at the same rate, and anything with an
/// emote list said something roughly fifteen times a minute. A cadence per line is what makes a
/// room feel inhabited rather than clockwork.
///
/// The bag is authored in two shapes and both have to keep working: a bare string is every emote
/// written before timing existed, and refusing them would have silenced all of them.
/// </remarks>
public sealed class MobEmoteTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private sealed class FakeMobTemplateRepository(MobTemplate template) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([template]);
    }

    private static Dictionary<string, object> Row(string text, int min, int max) => new()
    {
        ["text"] = text,
        ["minSeconds"] = min,
        ["maxSeconds"] = max,
    };

    private static MobTemplate Rat(params object[] emotes) => new()
    {
        Key = "rat",
        Name = "a rat",
        Icon = "r",
        Level = 1,
        // Sentinel: a wander narration would otherwise satisfy a loose text assertion, and the
        // rooms in the test world are all connected.
        Behavior = WorldHarness.AsPersisted(new Dictionary<string, object>
        {
            ["emotes"] = emotes.ToList(),
        }),
    };

    private static MobAiSystem AiFor(WorldHarness harness, MobTemplate template) =>
        new(new FakeMobTemplateRepository(template), new SeededRandomSource(42), harness.Clock, harness.View);

    private static Mob Sentinel(WorldHarness harness)
    {
        var mob = harness.AddMob("rat", West, name: "a rat");
        mob.State["sentinel"] = true;
        return mob;
    }

    // -----------------------------------------------------------------------
    // Reading the two authored shapes
    // -----------------------------------------------------------------------

    [Fact]
    public void A_bare_string_takes_the_default_cadence()
    {
        var schedule = MobBehavior.EmoteScheduleOf(
            WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["emotes"] = new List<object> { "squeaks" },
            }));

        var emote = Assert.Single(schedule);

        Assert.Equal("squeaks", emote.Text);
        Assert.Equal(MobEmote.DefaultMinSeconds * 4, emote.MinPulses);
        Assert.Equal(MobEmote.DefaultMaxSeconds * 4, emote.MaxPulses);
    }

    [Fact]
    public void A_row_carries_its_own_cadence()
    {
        var schedule = MobBehavior.EmoteScheduleOf(
            WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["emotes"] = new List<object> { Row("has the best fish in town", 120, 300) },
            }));

        var emote = Assert.Single(schedule);

        Assert.Equal("has the best fish in town", emote.Text);
        Assert.Equal(120 * 4, emote.MinPulses);
        Assert.Equal(300 * 4, emote.MaxPulses);
    }

    [Fact]
    public void The_two_shapes_mix_in_one_list()
    {
        // Which is what happens the moment a builder edits the timing of one line on a template
        // whose others were written before timing existed.
        var schedule = MobBehavior.EmoteScheduleOf(
            WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["emotes"] = new List<object> { "squeaks", Row("gnaws at a crate", 5, 9) },
            }));

        Assert.Equal(2, schedule.Count);
        Assert.Equal(MobEmote.DefaultMinSeconds * 4, schedule[0].MinPulses);
        Assert.Equal(5 * 4, schedule[1].MinPulses);
    }

    [Fact]
    public void A_row_with_no_text_is_dropped()
    {
        // A half-filled builder row. "a rat ." is worse than silence.
        var schedule = MobBehavior.EmoteScheduleOf(
            WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["emotes"] = new List<object>
                {
                    new Dictionary<string, object> { ["minSeconds"] = 5 },
                    "squeaks",
                },
            }));

        Assert.Equal("squeaks", Assert.Single(schedule).Text);
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_reads_as_exactly_the_lower_number()
    {
        // Two numbers side by side is an easy slip, and there is an obvious reading that is not
        // a crash.
        var emote = MobEmote.FromSeconds("squeaks", 30, 10);

        Assert.Equal(30 * 4, emote.MinPulses);
        Assert.Equal(30 * 4, emote.MaxPulses);
    }

    [Fact]
    public void A_zero_second_cadence_is_lifted_to_one()
    {
        // Zero would mean every pulse, which is four lines a second.
        var emote = MobEmote.FromSeconds("squeaks", 0, 0);

        Assert.Equal(4, emote.MinPulses);
    }

    // -----------------------------------------------------------------------
    // When they are actually said
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_freshly_spawned_mob_does_not_greet_the_room()
    {
        // It is scheduled, not fired. A spawner filling three slots would otherwise produce three
        // lines on the same pulse.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat("squeaks");
        Sentinel(harness);
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        await AiFor(harness, template).RunAsync(harness.World, CancellationToken.None);

        Assert.DoesNotContain("squeaks", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_is_said_once_its_range_has_passed()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat(Row("squeaks", 1, 1));
        Sentinel(harness);
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var ai = AiFor(harness, template);

        await ai.RunAsync(harness.World, CancellationToken.None);
        harness.Clock.AdvancePulses(8);
        await ai.RunAsync(harness.World, CancellationToken.None);

        Assert.Contains("a rat squeaks.", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slow_line_stays_quiet_while_a_fast_one_talks()
    {
        // The whole point. A shopkeeper's pitch every few minutes and a mutter every few seconds
        // share a mob without sharing a clock.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat(Row("mutters", 1, 1), Row("announces the catch of the day", 600, 900));
        Sentinel(harness);
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var ai = AiFor(harness, template);

        for (var sweep = 0; sweep < 6; sweep++)
        {
            await ai.RunAsync(harness.World, CancellationToken.None);
            harness.Clock.AdvancePulses(8);
        }

        var text = harness.DrainText(kael);

        Assert.Contains("mutters", text, StringComparison.Ordinal);
        Assert.DoesNotContain("catch of the day", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_one_line_lands_per_sweep()
    {
        // Two mobs answering each other is atmosphere; one mob saying two things at once is a
        // glitch. Both lines are long overdue here, and only the more overdue one is said.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat(Row("squeaks", 1, 1), Row("scratches", 1, 1));
        Sentinel(harness);
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var ai = AiFor(harness, template);

        await ai.RunAsync(harness.World, CancellationToken.None);
        harness.Clock.AdvancePulses(200);
        await ai.RunAsync(harness.World, CancellationToken.None);

        var text = harness.DrainText(kael);

        Assert.True(
            text.Contains("squeaks", StringComparison.Ordinal) ^
            text.Contains("scratches", StringComparison.Ordinal),
            $"Expected exactly one line, got: {text}");
    }

    [Fact]
    public async Task Renaming_a_line_does_not_leave_the_old_one_in_the_mobs_state()
    {
        // The schedule is keyed by the text itself, so fixing a typo makes a new key. Without
        // pruning, every correction would accrue in the state of every mob of that template.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mob = Sentinel(harness);

        await AiFor(harness, Rat("squeeks")).RunAsync(harness.World, CancellationToken.None);
        Assert.Contains("squeeks", MobState.EmoteScheduleIn(mob).Keys);

        await AiFor(harness, Rat("squeaks")).RunAsync(harness.World, CancellationToken.None);

        var schedule = MobState.EmoteScheduleIn(mob);

        Assert.Contains("squeaks", schedule.Keys);
        Assert.DoesNotContain("squeeks", schedule.Keys);
    }

    [Fact]
    public async Task A_mob_with_nothing_to_say_stores_nothing()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mob = Sentinel(harness);

        await AiFor(harness, Rat()).RunAsync(harness.World, CancellationToken.None);

        Assert.DoesNotContain(MobState.EmoteScheduleKey, mob.State.Keys);
    }

    [Fact]
    public async Task A_schedule_survives_the_json_round_trip_the_database_puts_it_through()
    {
        // The bug class this codebase keeps finding: a value written as a long comes back from
        // jsonb as a JsonElement, and a reader that pattern-matches the C# type sees nothing. A
        // schedule that failed to survive would silently reset every restart, so every line would
        // be rescheduled rather than due and the world would go quiet.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mob = Sentinel(harness);
        var template = Rat(Row("squeaks", 1, 1));

        await AiFor(harness, template).RunAsync(harness.World, CancellationToken.None);

        var before = MobState.EmoteScheduleIn(mob);
        mob.State = WorldHarness.AsPersisted(mob.State);
        var after = MobState.EmoteScheduleIn(mob);

        Assert.Equal(before, after);
        Assert.NotEmpty(after);
    }
}
