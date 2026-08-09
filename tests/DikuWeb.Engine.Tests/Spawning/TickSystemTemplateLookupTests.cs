using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace DikuWeb.Engine.Tests.Spawning;

/// <summary>
/// The tick systems read templates out of memory, not out of the database.
/// </summary>
/// <remarks>
/// <c>MobAiSystem</c> called <c>GetByKeyAsync</c> once per mob every 16 pulses and
/// <c>SpawnerSystem</c> once per spawner per kind every 60, each opening a fresh
/// <c>DbContext</c> for a single-row read that nothing memoized. A world with twenty mobs made
/// twenty round-trips every four seconds with nobody logged in. The caches already held every
/// template, loaded at boot and kept live by the applier — these two systems predate them and
/// were never migrated.
///
/// Counting the calls is the only assertion that can catch this coming back. Behaviour is
/// identical either way, which is precisely why it went unnoticed until the SQL log was read.
/// </remarks>
public sealed class TickSystemTemplateLookupTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private sealed class CountingMobRepository(params MobTemplate[] templates) : IMobTemplateRepository
    {
        public int Queries { get; private set; }

        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct)
        {
            Queries++;
            return Task.FromResult(templates.FirstOrDefault(t => t.Key == key));
        }

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>(templates);
    }

    private sealed class CountingItemRepository(params ItemTemplate[] templates) : IItemTemplateRepository
    {
        public int Queries { get; private set; }

        public Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct)
        {
            Queries++;
            return Task.FromResult(templates.FirstOrDefault(t => t.Key == key));
        }

        public Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ItemTemplate>>(templates);
    }

    private sealed class CountingSpawnerRepository(params Spawner[] spawners) : ISpawnerRepository
    {
        public int Queries { get; private set; }

        public Task<IReadOnlyList<Spawner>> GetAllAsync(CancellationToken ct)
        {
            Queries++;
            return Task.FromResult<IReadOnlyList<Spawner>>(spawners);
        }
    }

    private static MobTemplate Rat(Dictionary<string, object>? behavior = null) => new()
    {
        Key = "rat",
        Name = "a rat",
        Icon = "r",
        Level = 1,
        Behavior = behavior ?? [],
    };

    private static ItemTemplate Coin() => new()
    {
        Key = "coin",
        Name = "a coin",
        Description = "A small coin.",
        Icon = "$",
    };

    private static Spawner MobSpawnerFor(string templateKey, int target = 1) => new()
    {
        ZoneKey = "test.zone",
        TemplateKey = templateKey,
        TemplateKind = TemplateKind.Mob,
        RoomKeys = [West.ToString()],
        TargetCount = target,
    };

    private static Spawner ItemSpawnerFor(string templateKey) => new()
    {
        ZoneKey = "test.zone",
        TemplateKey = templateKey,
        TemplateKind = TemplateKind.Item,
        RoomKeys = [West.ToString()],
        TargetCount = 1,
    };

    // -----------------------------------------------------------------------
    // The spawner sweep
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_spawner_sweep_asks_the_database_for_nothing_when_the_caches_are_loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mobRepo = new CountingMobRepository(Rat());
        var itemRepo = new CountingItemRepository(Coin());
        var spawnerRepo = new CountingSpawnerRepository(MobSpawnerFor("rat"), ItemSpawnerFor("coin"));

        var mobCache = new MobTemplateCache();
        await mobCache.LoadAsync(mobRepo, CancellationToken.None);
        var itemCache = new ItemTemplateCache();
        await itemCache.LoadAsync(itemRepo, CancellationToken.None);
        var spawnerCache = new SpawnerCache();
        await spawnerCache.LoadAsync(spawnerRepo, CancellationToken.None);

        var system = new SpawnerSystem(
            spawnerRepo,
            mobRepo,
            itemRepo,
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View,
            mobCache,
            itemCache,
            spawnerCache);

        // Four sweeps is a minute of wall-clock at the real cadence.
        for (var i = 0; i < 4; i++)
        {
            await system.RunAsync(harness.World, CancellationToken.None);
        }

        // GetAllAsync during the load is not a per-entity query and is not counted; what must be
        // zero is every read the sweep itself makes - the per-key template lookups and the
        // spawner table it used to re-read whole, every sweep, forever.
        Assert.Equal(0, mobRepo.Queries);
        Assert.Equal(0, itemRepo.Queries);
        Assert.Equal(1, spawnerRepo.Queries);
        Assert.Single(harness.World.MobsIn(West));
    }

    [Fact]
    public async Task The_sweep_reads_the_spawner_table_when_the_cache_was_never_loaded()
    {
        // Same gate as the templates: an unloaded cache must mean "read through", not "no
        // spawners", or a container that supplied the cache without populating it would empty
        // the world.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mobRepo = new CountingMobRepository(Rat());
        var mobCache = new MobTemplateCache();
        await mobCache.LoadAsync(mobRepo, CancellationToken.None);
        var spawnerRepo = new CountingSpawnerRepository(MobSpawnerFor("rat"));

        var system = new SpawnerSystem(
            spawnerRepo,
            mobRepo,
            new CountingItemRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View,
            mobCache,
            new ItemTemplateCache(),
            new SpawnerCache());

        await system.RunAsync(harness.World, CancellationToken.None);
        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(2, spawnerRepo.Queries);
        Assert.Single(harness.World.MobsIn(West));
    }

    [Fact]
    public async Task A_spawner_saved_in_the_builder_reaches_the_next_sweep()
    {
        // Reading the cache rather than the table must not cost the "edits are live" guarantee:
        // a spawner created in the builder has to populate its rooms on the very next sweep,
        // with no restart and no query.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mobRepo = new CountingMobRepository(Rat());
        var mobCache = new MobTemplateCache();
        await mobCache.LoadAsync(mobRepo, CancellationToken.None);

        var spawnerRepo = new CountingSpawnerRepository();
        var spawnerCache = new SpawnerCache();
        await spawnerCache.LoadAsync(spawnerRepo, CancellationToken.None);

        var system = new SpawnerSystem(
            spawnerRepo,
            mobRepo,
            new CountingItemRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View,
            mobCache,
            new ItemTemplateCache(),
            spawnerCache);

        await system.RunAsync(harness.World, CancellationToken.None);
        Assert.Empty(harness.World.MobsIn(West));

        var spawner = MobSpawnerFor("rat");
        spawnerCache.Put(spawner);
        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Single(harness.World.MobsIn(West));

        // And a deleted spawner stops being enforced - though what it already placed stays put,
        // exactly as it did when the sweep read the table.
        spawnerCache.Remove(spawner.Id);
        harness.World.RemoveMob(harness.World.MobsIn(West).Single());
        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Empty(harness.World.MobsIn(West));
        Assert.Equal(1, spawnerRepo.Queries);
    }

    [Fact]
    public async Task The_spawner_sweep_reads_through_when_the_cache_was_never_loaded()
    {
        // An unloaded cache must not mean a world with no spawns in it. This is the failure the
        // IsLoaded gate exists for: a container that supplied the cache but never populated it
        // would otherwise silently answer "no such template" for everything.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mobRepo = new CountingMobRepository(Rat());

        var system = new SpawnerSystem(
            new CountingSpawnerRepository(MobSpawnerFor("rat")),
            mobRepo,
            new CountingItemRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View,
            new MobTemplateCache(),
            new ItemTemplateCache());

        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(1, mobRepo.Queries);
        Assert.Single(harness.World.MobsIn(West));
    }

    [Fact]
    public async Task The_spawner_sweep_still_works_with_no_caches_at_all()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var system = new SpawnerSystem(
            new CountingSpawnerRepository(MobSpawnerFor("rat")),
            new CountingMobRepository(Rat()),
            new CountingItemRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Single(harness.World.MobsIn(West));
    }

    [Fact]
    public async Task A_missing_template_is_not_retried_against_the_database_every_sweep()
    {
        // A spawner pointing at a template a builder deleted misses forever. Falling back per
        // miss would look like the safer choice and behave worse: it would issue the doomed query
        // on every sweep for the life of the process, which is the pathology being removed.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mobRepo = new CountingMobRepository(Rat());
        var mobCache = new MobTemplateCache();
        await mobCache.LoadAsync(mobRepo, CancellationToken.None);

        var system = new SpawnerSystem(
            new CountingSpawnerRepository(MobSpawnerFor("gone")),
            mobRepo,
            new CountingItemRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View,
            mobCache,
            new ItemTemplateCache());

        for (var i = 0; i < 5; i++)
        {
            await system.RunAsync(harness.World, CancellationToken.None);
        }

        Assert.Equal(0, mobRepo.Queries);
        Assert.Empty(harness.World.MobsIn(West));
    }

    [Fact]
    public async Task A_builder_edit_reaches_the_next_sweep()
    {
        // The cache is kept live by the applier, so reading it rather than the database must not
        // cost the "edits are live" guarantee - it is what earns it.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mobRepo = new CountingMobRepository();
        var mobCache = new MobTemplateCache();
        await mobCache.LoadAsync(mobRepo, CancellationToken.None);

        var system = new SpawnerSystem(
            new CountingSpawnerRepository(MobSpawnerFor("rat")),
            mobRepo,
            new CountingItemRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View,
            mobCache,
            new ItemTemplateCache());

        await system.RunAsync(harness.World, CancellationToken.None);
        Assert.Empty(harness.World.MobsIn(West));

        // The template is created in the builder, which puts it straight into the cache.
        mobCache.Put(Rat());
        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Single(harness.World.MobsIn(West));
        Assert.Equal(0, mobRepo.Queries);
    }

    // -----------------------------------------------------------------------
    // The mob AI sweep
    // -----------------------------------------------------------------------

    private static MobAiSystem AiSystem(
        WorldHarness harness,
        IMobTemplateRepository repo,
        MobTemplateCache? cache = null) =>
        new(repo, new SeededRandomSource(42), harness.Clock, harness.View, cache);

    [Fact]
    public async Task The_ai_sweep_asks_the_database_for_nothing_when_the_cache_is_loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var repo = new CountingMobRepository(Rat());
        var cache = new MobTemplateCache();
        await cache.LoadAsync(repo, CancellationToken.None);

        // Twenty mobs is the number in the plan's worked example: twenty round-trips every four
        // seconds with nobody logged in.
        for (var i = 0; i < 20; i++)
        {
            harness.AddMob("rat", West);
        }

        var system = AiSystem(harness, repo, cache);

        for (var sweep = 0; sweep < 4; sweep++)
        {
            await system.RunAsync(harness.World, CancellationToken.None);
        }

        Assert.Equal(0, repo.Queries);
    }

    [Fact]
    public async Task The_ai_sweep_reads_through_when_the_cache_was_never_loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var repo = new CountingMobRepository(Rat());
        harness.AddMob("rat", West);
        harness.AddMob("rat", West);

        var system = AiSystem(harness, repo, new MobTemplateCache());

        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(2, repo.Queries);
    }

    [Fact]
    public async Task The_ai_sweep_reads_the_cache_rather_than_a_stale_row()
    {
        // Same key, two different templates: only the cached one emotes. If this system ever
        // goes back to the repository, the room falls silent and the test says so.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var silent = Rat();
        var chatty = Rat(WorldHarness.AsPersisted(new Dictionary<string, object>
        {
            // Authored with its own cadence, at one second, so the schedule below is exact
            // rather than a guess at where inside the default range the roll landed.
            ["emotes"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["text"] = "twitches its whiskers",
                    ["minSeconds"] = 1,
                    ["maxSeconds"] = 1,
                },
            },
        }));

        var cache = new MobTemplateCache();
        cache.Put(chatty);

        harness.AddMob("rat", West);
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var system = AiSystem(harness, new CountingMobRepository(silent), cache);

        // The first sweep schedules rather than speaks: a mob greeting the room the instant it
        // spawns is what the per-line schedule exists to stop. The line is due one second later.
        await system.RunAsync(harness.World, CancellationToken.None);
        harness.Clock.AdvancePulses(8);

        await system.RunAsync(harness.World, CancellationToken.None);

        // The emote text itself, not merely "some text" - a wander narration would satisfy the
        // looser assertion without proving anything about which template was read.
        Assert.Contains("twitches its whiskers", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_ai_sweep_still_works_with_no_cache_at_all()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var repo = new CountingMobRepository(Rat());
        harness.AddMob("rat", West);

        var system = AiSystem(harness, repo);

        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(1, repo.Queries);
    }
}
