using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace DikuWeb.Engine.Tests.Spawning;

/// <summary>
/// How rare a thing is (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// <c>Spawner.RespawnSeconds</c> existed from the start and was read by nothing, so the real
/// respawn delay for everything in the game was the sweep's own cadence — nought to fifteen
/// seconds, whatever the builder typed. A player could stand in one room and kill the same mob
/// forever instead of going to look for another (BUGS.md #17).
/// </para>
/// <para>
/// The design has three parts and each is asserted below: a cold spawner fills at once, a
/// replacement waits the authored window, and only <b>one</b> arrives per window — so clearing a
/// room of four buys four windows rather than one.
/// </para>
/// </remarks>
public sealed class RespawnDelayTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private const int Minute = 240;

    private sealed class FakeSpawnerRepository(params Spawner[] spawners) : ISpawnerRepository
    {
        public Task<IReadOnlyList<Spawner>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Spawner>>(spawners);

        public Task<IReadOnlyList<Spawner>> GetByZoneAsync(string zoneKey, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Spawner>>([.. spawners.Where(s => s.ZoneKey == zoneKey)]);
    }

    private sealed class FakeMobTemplateRepository(MobTemplate template) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([template]);
    }

    private sealed class FakeItemTemplateRepository(ItemTemplate? template = null) : IItemTemplateRepository
    {
        public Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template?.Key == key ? template : null);

        public Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ItemTemplate>>(template is null ? [] : [template]);
    }

    private static Spawner MobSpawnerFor(int target, int respawnSeconds) => new()
    {
        ZoneKey = "test.zone",
        TemplateKey = "rat",
        TemplateKind = TemplateKind.Mob,
        RoomKeys = [West.ToString()],
        TargetCount = target,
        RespawnSeconds = respawnSeconds,
    };

    private static SpawnerSystem SystemFor(WorldHarness harness, Spawner spawner) =>
        new(
            new FakeSpawnerRepository(spawner),
            new FakeMobTemplateRepository(new MobTemplate
            {
                Key = "rat",
                Name = "a rat",
                Icon = "r",
                Level = 1,
            }),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    // -----------------------------------------------------------------------
    // Cold start
    // -----------------------------------------------------------------------

    /// <summary>
    /// A world that has just loaded is not a world where everything has just died.
    /// </summary>
    /// <remarks>
    /// Without this an hourly boss would leave its room empty for an hour after every restart, and
    /// a fresh database would come up as an empty world that slowly filled.
    /// </remarks>
    [Fact]
    public async Task A_cold_spawner_fills_to_target_at_once_however_rare_it_is()
    {
        var harness = Loaded();
        var system = SystemFor(harness, MobSpawnerFor(target: 4, respawnSeconds: 3600));

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);

        Assert.Equal(4, harness.World.AllMobs.Count());
    }

    // -----------------------------------------------------------------------
    // The window
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_loss_is_not_replaced_inside_the_window()
    {
        var harness = Loaded();
        var system = SystemFor(harness, MobSpawnerFor(target: 1, respawnSeconds: 60));

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        harness.World.RemoveMob(Assert.Single(harness.World.AllMobs));

        await system.RunAsync(harness.World, pulse: Minute, CancellationToken.None);

        Assert.Empty(harness.World.AllMobs);
    }

    [Fact]
    public async Task A_loss_is_replaced_once_the_window_has_passed()
    {
        var harness = Loaded();
        var system = SystemFor(harness, MobSpawnerFor(target: 1, respawnSeconds: 60));

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        harness.World.RemoveMob(Assert.Single(harness.World.AllMobs));

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        await system.RunAsync(harness.World, pulse: Minute + 1, CancellationToken.None);

        Assert.Single(harness.World.AllMobs);
    }

    /// <summary>
    /// An hour is an hour: the same sequence that refills a minute-spawner leaves a boss dead.
    /// </summary>
    [Fact]
    public async Task An_hourly_spawner_is_still_empty_after_ten_minutes()
    {
        var harness = Loaded();
        var system = SystemFor(harness, MobSpawnerFor(target: 1, respawnSeconds: 3600));

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        harness.World.RemoveMob(Assert.Single(harness.World.AllMobs));
        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);

        await system.RunAsync(harness.World, pulse: 10 * Minute, CancellationToken.None);
        Assert.Empty(harness.World.AllMobs);

        await system.RunAsync(harness.World, pulse: 61 * Minute, CancellationToken.None);
        Assert.Single(harness.World.AllMobs);
    }

    // -----------------------------------------------------------------------
    // One per window, which is what makes clearing a room worth anything
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Clearing_a_room_of_four_refills_one_at_a_time()
    {
        var harness = Loaded();
        var system = SystemFor(harness, MobSpawnerFor(target: 4, respawnSeconds: 60));

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        Assert.Equal(4, harness.World.AllMobs.Count());

        foreach (var mob in harness.World.AllMobs.ToList())
        {
            harness.World.RemoveMob(mob);
        }

        // Notices the loss and starts the clock.
        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        Assert.Empty(harness.World.AllMobs);

        // One per window, not a refill to target.
        await system.RunAsync(harness.World, pulse: 1 * Minute, CancellationToken.None);
        Assert.Single(harness.World.AllMobs);

        await system.RunAsync(harness.World, pulse: 2 * Minute, CancellationToken.None);
        Assert.Equal(2, harness.World.AllMobs.Count());

        await system.RunAsync(harness.World, pulse: 3 * Minute, CancellationToken.None);
        await system.RunAsync(harness.World, pulse: 4 * Minute, CancellationToken.None);
        Assert.Equal(4, harness.World.AllMobs.Count());

        // And it stops at the target rather than continuing every window.
        await system.RunAsync(harness.World, pulse: 5 * Minute, CancellationToken.None);
        Assert.Equal(4, harness.World.AllMobs.Count());
    }

    /// <summary>
    /// Zero seconds means the next sweep, which is what an author who wants no delay would expect —
    /// and is the only setting that reproduces the old behaviour.
    /// </summary>
    [Fact]
    public async Task Zero_seconds_replaces_on_the_sweep_that_notices()
    {
        var harness = Loaded();
        var system = SystemFor(harness, MobSpawnerFor(target: 1, respawnSeconds: 0));

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        harness.World.RemoveMob(Assert.Single(harness.World.AllMobs));

        await system.RunAsync(harness.World, pulse: 60, CancellationToken.None);

        Assert.Single(harness.World.AllMobs);
    }

    /// <summary>
    /// A window opened by a loss is closed by the gap being filled another way — a builder spawning
    /// one by hand, or the target being lowered — rather than being spent on an extra placement.
    /// </summary>
    [Fact]
    public async Task A_pending_window_is_abandoned_when_the_gap_closes_itself()
    {
        var harness = Loaded();
        var spawner = MobSpawnerFor(target: 2, respawnSeconds: 60);
        var system = SystemFor(harness, spawner);

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);
        harness.World.RemoveMob(harness.World.AllMobs.First());

        // Notices one missing and starts the clock.
        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);

        // The target drops to what is standing, so nothing is owed.
        spawner.TargetCount = 1;

        await system.RunAsync(harness.World, pulse: 10 * Minute, CancellationToken.None);

        Assert.Single(harness.World.AllMobs);
    }
}
