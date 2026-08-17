using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace DikuWeb.Engine.Tests.Spawning;

/// <summary>
/// Whether a spawned mob wanders: the template carries the default and the spawner has the last
/// word (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// <b>Standing still is the default, and that is the change these pin.</b> Wandering used to be
/// what any mob did unless its spawner ticked <c>sentinel</c>, which is backwards: most authored
/// mobs are shopkeepers, quest givers, and guards that belong somewhere specific. Worse, the
/// decision lived only on the spawner, so the same shopkeeper placed by two spawners could wander
/// from one and not the other.
///
/// <b>Asserted through a real sweep rather than on the coalesce.</b> The resolution is one
/// expression, and a test of that expression would pass whether or not anything called it - the
/// property worth holding is that the answer reaches the mob's state, which is where
/// <see cref="MobAiSystem"/> reads it back. This is §12's rule about call sites, applied to a
/// one-liner.
/// </remarks>
public sealed class SpawnerWanderResolutionTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private sealed class FakeSpawnerRepository(params Spawner[] spawners) : ISpawnerRepository
    {
        public Task<IReadOnlyList<Spawner>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Spawner>>(spawners);
    }

    private sealed class FakeMobTemplateRepository(MobTemplate template) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([template]);
    }

    private sealed class FakeItemTemplateRepository : IItemTemplateRepository
    {
        public Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult<ItemTemplate?>(null);

        public Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ItemTemplate>>([]);
    }

    /// <summary>
    /// A template whose bag is round-tripped through storage, because a hand-built
    /// <c>Dictionary&lt;string, object&gt;</c> holds a real <c>bool</c> where the running game
    /// holds a <c>JsonElement</c> - the trap §12 keeps rediscovering.
    /// </summary>
    private static MobTemplate Rat(bool? wanders) => new()
    {
        Key = "rat",
        Name = "a rat",
        Icon = "r",
        Level = 1,
        Behavior = wanders is null
            ? []
            : WorldHarness.AsPersisted(new Dictionary<string, object> { ["wanders"] = wanders }),
    };

    private static Spawner SpawnerFor(bool? wanders) => new()
    {
        ZoneKey = "test.zone",
        TemplateKey = "rat",
        TemplateKind = TemplateKind.Mob,
        RoomKeys = [West.ToString()],
        TargetCount = 1,
        Wanders = wanders,
    };

    /// <summary>Sweeps once and answers whether the mob it made was marked as wandering.</summary>
    private static async Task<bool> SpawnedWanderingAsync(MobTemplate template, Spawner spawner)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var system = new SpawnerSystem(
            new FakeSpawnerRepository(spawner),
            new FakeMobTemplateRepository(template),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);

        var mob = Assert.Single(harness.World.MobsIn(West));

        // Read exactly the way MobAiSystem reads it, through JsonBag: absence is "stands still"
        // rather than "unset", so there is no third answer to distinguish here.
        return JsonBag.Boolean(mob.State, MobBehavior.WandersKey);
    }

    [Fact]
    public async Task A_template_that_says_nothing_stands_still()
    {
        // The safe direction. A mob nobody has said anything about is far more likely to be a
        // shopkeeper than a rat, and a shopkeeper that walks out of its shop is a bug a player
        // reports as "the shop is gone".
        Assert.False(await SpawnedWanderingAsync(Rat(null), SpawnerFor(null)));
    }

    [Fact]
    public async Task A_template_that_asks_to_wander_wanders()
    {
        Assert.True(await SpawnedWanderingAsync(Rat(true), SpawnerFor(null)));
    }

    [Fact]
    public async Task A_spawner_can_hold_a_wandering_template_in_place()
    {
        // The case the override exists for: one placement of a mob that normally roams - a rat in
        // a cage, a guard posted at a door - without editing the template every other spawner
        // shares.
        Assert.False(await SpawnedWanderingAsync(Rat(true), SpawnerFor(false)));
    }

    [Fact]
    public async Task A_spawner_can_send_a_still_template_wandering()
    {
        // The other direction, which matters because the template default is now "stands still":
        // without this, a builder wanting one wandering placement would have to flip the template
        // and then pin every other spawner of it.
        Assert.True(await SpawnedWanderingAsync(Rat(false), SpawnerFor(true)));
    }

    [Fact]
    public async Task A_spawner_with_no_opinion_does_not_override_the_template()
    {
        // Null is a third answer, not a missing one. Read as false it would have silenced every
        // wandering template in the game, since null is what every spawner has by default.
        Assert.True(await SpawnedWanderingAsync(Rat(true), SpawnerFor(null)));
        Assert.False(await SpawnedWanderingAsync(Rat(false), SpawnerFor(null)));
    }
}
