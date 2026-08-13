using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.Time;

namespace DikuWeb.Engine.Tests.Inhabitants;

/// <summary>
/// A wandering mob stays in the zone it spawned in (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// A zone is the unit difficulty is authored in (§4.4), so a mob that strolls across a border
/// carries numbers resolved from somewhere else's multipliers — a crypt rat wandering into the
/// starting meadow is not a wandering rat, it is a difficulty spike with no author.
///
/// The alternative was fencing by geography: flag every border room <c>noMob</c>, and remember to
/// do it again every time a builder digs a new exit. Fencing by origin is a property of the mob,
/// so it cannot be forgotten.
/// </remarks>
public sealed class MobWanderZoneTests
{
    private static readonly RoomKey Home = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Neighbour = RoomKey.Parse("test.beyond.hall");

    private sealed class FakeMobTemplateRepository(MobTemplate template) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([template]);
    }

    /// <summary>
    /// Two zones, one room each, joined by a single exit — so the only move available is the one
    /// across the border. Anything else would leave the assertion able to pass by luck.
    /// </summary>
    private static WorldHarness TwoZones()
    {
        var harness = new WorldHarness();

        var home = WorldHarness.NewRoom("west");
        var beyond = new Room
        {
            Key = Neighbour,
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

        return harness;
    }

    private static MobTemplate Rat(bool roams)
    {
        var behavior = roams
            ? WorldHarness.AsPersisted(new Dictionary<string, object> { ["roams"] = true })
            : [];

        return new MobTemplate
        {
            Key = "rat",
            Name = "a rat",
            Icon = "r",
            Level = 1,
            WanderIntervalPulses = 1,
            Behavior = behavior,
        };
    }

    /// <summary>
    /// Spawns through <see cref="MobSpawner"/> rather than building a <see cref="Mob"/> inline,
    /// because the home zone is recorded at spawn — a hand-built mob would test nothing.
    /// </summary>
    private static Mob SpawnAtHome(WorldHarness harness, MobTemplate template)
    {
        var mob = new MobSpawner().Spawn(
            template,
            harness.World.FindZone("test.zone")!,
            harness.World.FindWorld("test")!,
            Home,
            // These tests ask where a wandering mob may go, so it has to be one. Standing still
            // is the default now, and a mob that never moves would pass every border assertion
            // for the wrong reason.
            wanders: true);

        harness.World.AddMob(mob);
        return mob;
    }

    /// <summary>
    /// Runs the AI until the mobs have had a turn to wander.
    /// </summary>
    /// <remarks>
    /// Two sweeps, not one. The first sweep a mob is seen on schedules its next wander rather than
    /// firing one, which is what stops a spawner's three rats leaving together; the template's
    /// one-pulse interval makes the second sweep due for certain.
    /// </remarks>
    private static async Task WanderAsync(WorldHarness harness, MobTemplate template)
    {
        var ai = new MobAiSystem(
            new FakeMobTemplateRepository(template),
            new SeededRandomSource(42),
            harness.Clock,
            harness.View);

        for (var sweep = 0; sweep < 2; sweep++)
        {
            // Past the wander interval, so the attempt actually happens.
            harness.Clock.AdvancePulses(8);
            await ai.RunAsync(harness.World, CancellationToken.None);
        }
    }

    [Fact]
    public async Task It_turns_back_at_the_zone_border()
    {
        var harness = TwoZones();
        var template = Rat(roams: false);
        var mob = SpawnAtHome(harness, template);

        await WanderAsync(harness, template);

        Assert.Equal(Home.ToString(), mob.RoomKey);
    }

    [Fact]
    public async Task The_roams_flag_lets_it_out()
    {
        var harness = TwoZones();
        var template = Rat(roams: true);
        var mob = SpawnAtHome(harness, template);

        await WanderAsync(harness, template);

        Assert.Equal(Neighbour.ToString(), mob.RoomKey);
    }

    [Fact]
    public async Task It_still_wanders_freely_inside_its_own_zone()
    {
        // Confinement must not read as "does not wander". The test world's three rooms are all
        // one zone, so an unconfined move is available and has to be taken.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Rat(roams: false);
        var mob = SpawnAtHome(harness, template);

        await WanderAsync(harness, template);

        Assert.NotEqual(Home.ToString(), mob.RoomKey);
        Assert.Equal("test.zone", RoomKey.Parse(mob.RoomKey).ZoneKey);
    }

    [Fact]
    public async Task A_mob_from_before_the_key_existed_stays_where_it_is()
    {
        // Rows written before homeZone was recorded have no home. Absence must resolve to the
        // confining value, the same way an absent room flag resolves to the safe one — reading it
        // as "anywhere" would set every existing mob loose on the next restart, which is exactly
        // what the key was added to stop. Written before the code was, and it failed.
        var harness = TwoZones();
        var template = Rat(roams: false);

        var mob = SpawnAtHome(harness, template);
        mob.State.Remove(MobState.HomeZoneKey);

        await WanderAsync(harness, template);

        Assert.Equal(Home.ToString(), mob.RoomKey);
    }

    [Fact]
    public void A_spawn_records_the_zone_it_happened_in()
    {
        var harness = TwoZones();
        var mob = SpawnAtHome(harness, Rat(roams: false));

        Assert.Equal("test.zone", MobState.HomeZoneOf(mob));
    }
}
