using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace DikuWeb.Engine.Tests.Spawning;

/// <summary>
/// A spawner pinning the level its mobs fight at, whatever the zone's dials say (PLAN.md §4.7).
/// </summary>
/// <remarks>
/// <b>Why the pin exists.</b> A zone dial is zone-wide, so scaling a 25–30 zone by two turns a
/// level-10 template into the level-20 content that was wanted <em>and</em> a level-25 template
/// already written for that zone into a level-50 monster. Without a per-placement say, an author
/// must either write every template at its final level — losing the reuse §4.4 exists for — or keep
/// the dials at 1.0 and hand-write one template per tier.
///
/// <b>Asserted through a real sweep</b>, as <see cref="SpawnerWanderResolutionTests"/> argues: the
/// resolution is one ternary, and testing the ternary would pass whether or not anything called it.
/// </remarks>
public sealed class SpawnerLevelOverrideTests
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

    /// <summary>Through storage, so the stats are <c>JsonElement</c> as they are in play (§12).</summary>
    private static MobTemplate Rat(int level) => new()
    {
        Key = "rat",
        Name = "a rat",
        Icon = "r",
        Level = level,
        BaseStats = WorldHarness.AsPersisted(new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damage"] = "4-7",
        }),
    };

    private static async Task<Mob> SweptAsync(
        int templateLevel,
        int? fightsAt,
        decimal zoneStrength = 1m,
        int zoneMinLevel = 1,
        TemplateKind kind = TemplateKind.Mob)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Zone.MinLevel = zoneMinLevel;
        harness.SetZoneMultipliers(m => m.Strength = zoneStrength);

        var spawner = new Spawner
        {
            ZoneKey = "test.zone",
            TemplateKey = "rat",
            TemplateKind = kind,
            RoomKeys = [West.ToString()],
            TargetCount = 1,
            FightsAtLevel = fightsAt,
        };

        var system = new SpawnerSystem(
            new FakeSpawnerRepository(spawner),
            new FakeMobTemplateRepository(Rat(templateLevel)),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        await system.RunAsync(harness.World, CancellationToken.None);

        return Assert.Single(harness.World.MobsIn(West));
    }

    [Fact]
    public async Task No_pin_leaves_the_zone_in_charge()
    {
        // The default, and the shape every existing spawner has. Strength 3 on a level 5 template.
        var mob = await SweptAsync(templateLevel: 5, fightsAt: null, zoneStrength: 3m);

        Assert.Equal(15, mob.EffectiveLevel);
        Assert.Equal(120, mob.Vitals.HealthMax);
    }

    [Fact]
    public async Task A_pin_beats_the_zone_rather_than_composing_with_it()
    {
        // The decision this whole feature rests on. Composing would make this 27 x 2 = 54, and the
        // number the builder typed would be a lie - which discards the reason for stating an
        // outcome instead of a factor.
        var mob = await SweptAsync(templateLevel: 10, fightsAt: 27, zoneStrength: 2m);

        Assert.Equal(27, mob.EffectiveLevel);
    }

    [Fact]
    public async Task A_pin_scales_health_and_damage_to_match()
    {
        // A level that arrived without the stats to back it would be a promise the fight does not
        // keep - the same defect that made effective level meaningless before slice 1.
        var mob = await SweptAsync(templateLevel: 10, fightsAt: 20);

        Assert.Equal(20, mob.EffectiveLevel);
        Assert.Equal(80, mob.Vitals.HealthMax);

        var stats = DamageCalculator.StatsFrom(mob);
        Assert.Equal(8, stats.MinDamage);
        Assert.Equal(14, stats.MaxDamage);
    }

    [Fact]
    public async Task Pinning_a_template_to_its_own_level_cancels_the_zone()
    {
        // The mixed-zone case, which is the point. A level 25 template already written for a scaled
        // zone stands as authored beside templates the zone is lifting.
        var mob = await SweptAsync(templateLevel: 25, fightsAt: 25, zoneStrength: 2m);

        Assert.Equal(25, mob.EffectiveLevel);
        Assert.Equal(40, mob.Vitals.HealthMax);
    }

    [Fact]
    public async Task A_pin_is_not_floored_by_the_zones_band()
    {
        // MobLevel floors a *derived* level at the band because the band catches mobs nobody said
        // anything about. A builder who types 3 in a level 40 zone meant a flavour critter, and the
        // most explicit statement in the system must not be overruled by the least.
        var mob = await SweptAsync(templateLevel: 1, fightsAt: 3, zoneMinLevel: 40);

        Assert.Equal(3, mob.EffectiveLevel);
    }

    [Fact]
    public async Task The_snapshot_records_that_a_pin_was_applied()
    {
        // SpawnMultipliers exists to answer "why does this kobold have 137 hp?" from the instance.
        // With a pin the zone's dials are not what was applied, so recording only those would make
        // the snapshot confidently wrong.
        var pinned = await SweptAsync(templateLevel: 10, fightsAt: 27, zoneStrength: 2m);
        var unpinned = await SweptAsync(templateLevel: 10, fightsAt: null, zoneStrength: 2m);

        Assert.Equal(27m, pinned.SpawnMultipliers["FightsAt"]);
        Assert.Equal(0m, unpinned.SpawnMultipliers["FightsAt"]);
    }

    [Fact]
    public async Task An_item_spawner_carrying_a_stored_pin_is_unaffected()
    {
        // The API refuses to set one, but an import is deliberately more permissive than the
        // builder - so a stored value has to be inert rather than merely unreachable.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var spawner = new Spawner
        {
            ZoneKey = "test.zone",
            TemplateKey = "lamp",
            TemplateKind = TemplateKind.Item,
            RoomKeys = [West.ToString()],
            TargetCount = 1,
            FightsAtLevel = 30,
        };

        var system = new SpawnerSystem(
            new FakeSpawnerRepository(spawner),
            new FakeMobTemplateRepository(Rat(1)),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        // The item template is unknown, so this exercises the dormant-spawner path (§7.4) as well:
        // whatever happens, it must not be a throw.
        await system.RunAsync(harness.World, CancellationToken.None);

        Assert.Empty(harness.World.MobsIn(West));
    }
}
