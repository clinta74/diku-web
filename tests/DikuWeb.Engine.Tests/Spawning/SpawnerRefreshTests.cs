using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DikuWeb.Engine.Tests.Spawning;

/// <summary>
/// A spawn must reach the map and contents panels, not just the scrollback. Narrating
/// "A rat appears." while the rat is absent from the room view is worse than silence - the
/// player is told about something they cannot see or attack until they move.
/// </summary>
public sealed class SpawnerRefreshTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

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

    private static Spawner MobSpawnerFor(string templateKey) => new()
    {
        ZoneKey = "test.zone",
        TemplateKey = templateKey,
        TemplateKind = TemplateKind.Mob,
        RoomKeys = [West.ToString()],
        TargetCount = 1,
    };

    [Fact]
    public async Task Spawning_a_mob_refreshes_the_map_and_contents_for_occupants()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var system = new SpawnerSystem(
            new FakeSpawnerRepository(MobSpawnerFor("rat")),
            new FakeMobTemplateRepository(Rat()),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        await system.RunAsync(harness.World, CancellationToken.None);

        var events = harness.Drain(kael);

        // The prose is the part that already worked; the panels are the regression.
        Assert.Contains(events, e => e.Type == EventTypes.Text);
        Assert.Contains(events, e => e.Type == EventTypes.Map);
        Assert.Contains(events, e => e.Type == EventTypes.Contents);
    }

    [Fact]
    public async Task One_room_gets_one_refresh_however_many_mobs_land_in_it()
    {
        // Three mobs into a single room must not send three map redraws.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var spawner = MobSpawnerFor("rat");
        spawner.TargetCount = 3;

        var system = new SpawnerSystem(
            new FakeSpawnerRepository(spawner),
            new FakeMobTemplateRepository(Rat()),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        await system.RunAsync(harness.World, CancellationToken.None);

        var events = harness.Drain(kael);

        Assert.Equal(3, harness.World.MobsIn(West).Count);
        Assert.Equal(3, events.Count(e => e.Type == EventTypes.Text));
        Assert.Equal(1, events.Count(e => e.Type == EventTypes.Map));
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_population_target_is_already_met()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);

        var system = new SpawnerSystem(
            new FakeSpawnerRepository(MobSpawnerFor("rat")),
            new FakeMobTemplateRepository(Rat()),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        await system.RunAsync(harness.World, CancellationToken.None);
        harness.Drain(kael);

        // Second sweep: the target is met, so it must be silent - no prose, no redraw.
        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Empty(harness.Drain(kael));
    }

    [Fact]
    public async Task A_container_built_system_still_refreshes_the_room()
    {
        // PlayerView is an optional constructor parameter, matching the other tick systems. If
        // the container ever stopped supplying it, spawns would quietly go back to narrating
        // without redrawing - the system would still "work", just invisibly. Resolving through
        // a real container and asserting the redraw covers the wiring, not just the logic.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISpawnerRepository>(new FakeSpawnerRepository(MobSpawnerFor("rat")));
        services.AddSingleton<IMobTemplateRepository>(new FakeMobTemplateRepository(Rat()));
        services.AddSingleton<IItemTemplateRepository>(new FakeItemTemplateRepository());
        services.AddSingleton<MobSpawner>();
        services.AddSingleton<ItemSpawner>();
        services.AddSingleton<RoomLayoutService>();
        services.AddSingleton<PlayerView>();
        services.AddSingleton<SpawnerSystem>();

        using var provider = services.BuildServiceProvider();
        var system = provider.GetRequiredService<SpawnerSystem>();

        await system.RunAsync(harness.World, CancellationToken.None);

        var events = harness.Drain(kael);
        Assert.Contains(events, e => e.Type == EventTypes.Map);
        Assert.Contains(events, e => e.Type == EventTypes.Contents);
    }
}
