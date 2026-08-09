using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Inhabitants;

/// <summary>
/// Mobs sharing a template do not wander in step (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// The old check was <c>pulse - lastWanderPulse >= interval</c> against a counter starting at
/// zero, and the AI sweep itself only runs every sixteen pulses. Both together meant every mob in
/// the world read one clock: a fresh one was already overdue, so it moved on the first sweep that
/// saw it and re-armed from that pulse. Two rats from one spawner left together and arrived
/// together for as long as they lived, which reads as a patrol rather than as two animals — and
/// no amount of choosing exits at random fixes it, because the departures are what is in step.
/// </remarks>
public sealed class MobWanderCadenceTests
{
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");

    private sealed class FakeMobTemplateRepository(MobTemplate template) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([template]);
    }

    /// <summary>The shipped default cadence, because that is the one that was in lockstep.</summary>
    private static MobTemplate Rat() => new()
    {
        Key = "rat",
        Name = "a rat",
        Icon = "r",
        Level = 1,
        WanderIntervalPulses = 24,
    };

    private static MobAiSystem AiFor(WorldHarness harness, MobTemplate template) =>
        new(new FakeMobTemplateRepository(template), new SeededRandomSource(42), harness.Clock, harness.View);

    private static List<Mob> SpawnPack(WorldHarness harness, MobTemplate template, int count)
    {
        var pack = new List<Mob>();

        for (var i = 0; i < count; i++)
        {
            var mob = new MobSpawner().Spawn(
                template, harness.World.FindZone("test.zone")!, harness.World.FindWorld("test")!, Middle);

            harness.World.AddMob(mob);
            pack.Add(mob);
        }

        return pack;
    }

    [Fact]
    public async Task A_mob_does_not_bolt_on_the_first_sweep_that_sees_it()
    {
        // A spawner fills its slots between sweeps, so "overdue the moment you exist" put every
        // mob it created on the road at the same instant, one sweep after it appeared.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat();
        var mob = SpawnPack(harness, template, 1)[0];

        harness.Clock.AdvancePulses(64);
        await AiFor(harness, template).RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(Middle.ToString(), mob.RoomKey);
        Assert.NotNull(MobState.WanderNextOf(mob));
    }

    [Fact]
    public async Task Mobs_from_one_spawn_get_their_own_clocks()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat();
        var pack = SpawnPack(harness, template, 4);

        await AiFor(harness, template).RunAsync(harness.World, CancellationToken.None);

        var schedules = pack.Select(MobState.WanderNextOf).Distinct().ToList();

        Assert.DoesNotContain(null, schedules);
        Assert.True(
            schedules.Count > 1,
            "Four mobs scheduled off one interval all landed on the same pulse - the jitter is gone.");
    }

    [Fact]
    public async Task A_sweep_that_is_not_this_mob_s_turn_leaves_its_deadline_alone()
    {
        // Redrawing on every sweep rather than on every move looks like the same code and is not:
        // the deadline is pushed ahead of the sweep that was about to meet it, so the mob moves
        // only when a draw lands inside one sweep's worth of pulses. The cadence would then come
        // from how often the AI runs, not from the number the template authored.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat();
        var mob = SpawnPack(harness, template, 1)[0];
        var ai = AiFor(harness, template);

        await ai.RunAsync(harness.World, CancellationToken.None);
        var scheduled = MobState.WanderNextOf(mob);

        // Well short of the twelve-pulse floor, so this sweep cannot be its turn.
        harness.Clock.AdvancePulses(4);
        await ai.RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(scheduled, MobState.WanderNextOf(mob));
    }

    [Fact]
    public async Task The_interval_is_the_average_rather_than_the_period()
    {
        // Half the authored interval either side. The bound matters as much as the spread does: a
        // rat authored to move every six seconds must not disappear for a minute.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat();
        var mob = SpawnPack(harness, template, 1)[0];

        harness.Clock.AdvancePulses(100);
        await AiFor(harness, template).RunAsync(harness.World, CancellationToken.None);

        var scheduled = MobState.WanderNextOf(mob)!.Value - harness.Clock.CurrentPulse;

        Assert.InRange(scheduled, 12, 36);
    }

    [Fact]
    public async Task A_mob_that_cannot_leave_re_arms_rather_than_retrying_every_sweep()
    {
        // Boxed in by its own zone border. Without the re-arm it is due forever, rescanning its
        // exits on every sweep for the rest of its life to reach the same answer.
        var harness = new WorldHarness();

        var home = WorldHarness.NewRoom("west");
        var beyond = new Room
        {
            Key = RoomKey.Parse("test.beyond.hall"),
            ZoneKey = "test.beyond",
            Title = "A hall beyond",
            Description = "The next zone over.",
            Grid = [],
            Legend = [],
        };

        WorldHarness.Link(home, Direction.East, beyond);

        harness.World.Load(
            [new Domain.Worlds.World { Key = "test", Name = "Test" }],
            [
                new Zone { Key = "test.zone", WorldKey = "test", Name = "Home" },
                new Zone { Key = "test.beyond", WorldKey = "test", Name = "Beyond" },
            ],
            [home, beyond]);

        var template = Rat();
        var mob = new MobSpawner().Spawn(
            template, harness.World.FindZone("test.zone")!, harness.World.FindWorld("test")!, home.Key);

        harness.World.AddMob(mob);

        var ai = AiFor(harness, template);
        await ai.RunAsync(harness.World, CancellationToken.None);

        harness.Clock.AdvancePulses(64);
        await ai.RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(home.Key.ToString(), mob.RoomKey);
        Assert.True(MobState.WanderNextOf(mob) > harness.Clock.CurrentPulse);
    }
}
