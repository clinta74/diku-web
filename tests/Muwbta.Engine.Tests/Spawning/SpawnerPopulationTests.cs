using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Muwbta.Engine.Tests.Spawning;

/// <summary>
/// What a spawner counts before deciding to spawn again (PLAN.md §4.7).
/// </summary>
/// <remarks>
/// The sweep used to count what was standing in the spawner's own rooms. A mob that wandered one
/// room east stopped counting, so the spawner replaced it — and the replacement wandered off too.
/// A spawner set to three could populate a whole zone given long enough, which is the flooding
/// these tests exist to prevent. Found in playtesting, not by a test, which is why the count is
/// now asserted rather than assumed.
/// </remarks>
public sealed class SpawnerPopulationTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private sealed class FakeSpawnerRepository(params Spawner[] spawners) : ISpawnerRepository
    {
        public Task<IReadOnlyList<Spawner>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Spawner>>(spawners);
    }

    private sealed class FakeMobTemplateRepository(MobTemplate? template = null) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template?.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>(template is null ? [] : [template]);
    }

    private sealed class FakeItemTemplateRepository(ItemTemplate? template = null) : IItemTemplateRepository
    {
        public Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template?.Key == key ? template : null);

        public Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ItemTemplate>>(template is null ? [] : [template]);
    }

    private static MobTemplate Rat() => new()
    {
        Key = "rat",
        Name = "a rat",
        Icon = "r",
        Level = 1,
    };

    private static Spawner RatSpawner(int target = 1) => new()
    {
        ZoneKey = "test.zone",
        TemplateKey = "rat",
        TemplateKind = TemplateKind.Mob,
        RoomKeys = [West.ToString()],
        TargetCount = target,
    };

    private static SpawnerSystem SystemFor(WorldHarness harness, Spawner spawner) =>
        new(
            new FakeSpawnerRepository(spawner),
            new FakeMobTemplateRepository(Rat()),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

    [Fact]
    public async Task A_mob_that_wandered_out_still_counts()
    {
        // The whole bug. Before this the second sweep saw an empty room and refilled it.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var spawner = RatSpawner();
        var system = SystemFor(harness, spawner);

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        var rat = Assert.Single(harness.World.MobsIn(West));

        harness.World.MoveMob(rat, East);
        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);

        Assert.Single(harness.World.AllMobs);
        Assert.Empty(harness.World.MobsIn(West));
    }

    [Fact]
    public async Task Sweeping_repeatedly_never_exceeds_the_target()
    {
        // The flooding case, run out. Each sweep scatters what it spawned and sweeps again.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var spawner = RatSpawner(target: 3);
        var system = SystemFor(harness, spawner);

        for (var sweep = 0; sweep < 10; sweep++)
        {
            await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);

            foreach (var mob in harness.World.AllMobs.ToList())
            {
                harness.World.MoveMob(mob, sweep % 2 == 0 ? Middle : East);
            }
        }

        Assert.Equal(3, harness.World.AllMobs.Count());
    }

    /// <summary>
    /// The count must fall as well as rise, or a spawner stops working the first time something it
    /// made dies — but the replacement waits out the spawner's own window (PLAN.md §4.8).
    /// </summary>
    /// <remarks>
    /// This used to sweep twice at the same pulse and assert the rat was back, which passed because
    /// the sweep refilled to target unconditionally. That was the defect: every spawner in the game
    /// respawned on the sweep's 15-second cadence whatever it was authored at, so a player could
    /// stand in one room and kill the same mob forever (BUGS.md #17).
    /// </remarks>
    [Fact]
    public async Task A_killed_mob_is_replaced_after_its_respawn_window()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        // The default, 60 seconds, which is 240 pulses.
        var system = SystemFor(harness, RatSpawner());

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        var rat = Assert.Single(harness.World.AllMobs);

        harness.World.RemoveMob(rat);

        // The sweep that notices the loss starts the clock rather than filling the gap.
        await system.RunAsync(harness.World, pulse: 60, CancellationToken.None);
        Assert.Empty(harness.World.AllMobs);

        // Still inside the window.
        await system.RunAsync(harness.World, pulse: 240, CancellationToken.None);
        Assert.Empty(harness.World.AllMobs);

        // Past it.
        await system.RunAsync(harness.World, pulse: 300, CancellationToken.None);
        Assert.Single(harness.World.AllMobs);
    }

    [Fact]
    public async Task One_spawner_does_not_count_another_spawners_mobs()
    {
        // Two spawners of the same template in the same zone are ordinary content. Counting by
        // template rather than by spawner would have each one see the other's work as its own.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var first = RatSpawner();
        var second = RatSpawner();
        second.RoomKeys = [East.ToString()];

        await SystemFor(harness, first).RunAsync(harness.World, pulse: 0, CancellationToken.None);
        await SystemFor(harness, second).RunAsync(harness.World, pulse: 0, CancellationToken.None);

        Assert.Equal(2, harness.World.AllMobs.Count());
    }

    [Fact]
    public async Task A_mob_nothing_spawned_is_nobody_is_responsibility()
    {
        // A builder's `spawn` command makes a mob with no spawner id. It must not satisfy a
        // spawner's target, or placing one rat by hand would silently switch a spawner off.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("rat", West);

        await SystemFor(harness, RatSpawner()).RunAsync(harness.World, pulse: 0, CancellationToken.None);

        Assert.Equal(2, harness.World.MobsIn(West).Count);
    }
}
