using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Server.Building;

namespace DikuWeb.Server.Tests.Building;

/// <summary>
/// Every mob the shipped world spawns can be hit, and can hit back (PLAN.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Written after a level 45 Warden asked what their DPS was.</b> The answer was fine; the fight
/// that produced it was against a mob 23 levels below them, and checking the zone they were about
/// to walk into found that they could not have fought it at all. <c>the-unlit</c> is authored at
/// <c>strength 4.7</c>, and defence used to scale with that dial — so four mobs authored 2 / 3 / 4
/// / 6 apart arrived 10 / 14 / 20 / 30 apart, and the top of the endgame zone needed a natural 20
/// from a fully equipped level 48.
/// </para>
/// <para>
/// <b>The die is the budget, and nothing enforced it.</b> <c>DamageCalculator</c> clamps the needed
/// roll to 2..20, so the failure is silent by construction: the numbers go on making sense and the
/// roll simply stops being consulted. Every other guard on scaling asks whether a stat came out at
/// the right multiple. This one asks the only question a player cares about — can the two sides
/// still reach each other — and it reads the shipped content rather than a fixture, because a
/// fixture would agree with itself.
/// </para>
/// </remarks>
public sealed class MobReachTests
{
    /// <summary>
    /// The worst roll a level-appropriate player should ever need. Sixteen leaves a 25% chance.
    /// </summary>
    /// <remarks>
    /// Deliberately loose. The point is to catch a wall, not to tune evasiveness — a mob that wants
    /// to be hard to hit is entitled to be, and the authored spread today tops out at 6, which
    /// lands on 11. Anything approaching 20 is the die falling out of the fight.
    /// </remarks>
    private const int WorstNeededRoll = 16;

    /// <summary>
    /// The most a blow should ever be absorbed. <see cref="ArmorCurve.Cap"/> allows 75%.
    /// </summary>
    /// <remarks>
    /// Separate from the roll, and looser, because armour is a fraction with a ceiling rather than
    /// a comparison — it can make a fight long, and it cannot make one impossible. This is here so
    /// that a dial pushed far enough to approach the cap is reported rather than discovered.
    /// </remarks>
    private const decimal WorstAbsorbed = 0.65m;

    /// <summary>
    /// A player who has reached this mob's level and is carrying nothing special: the Might
    /// modifier caps at +5 (<c>AttributeSet.MaxValue</c> is 20, reached in the teens) and no
    /// weapon bonus at all.
    /// </summary>
    /// <remarks>
    /// The floor rather than the average, on purpose. A guard modelling a well-equipped player
    /// would pass while the zone was unplayable for anybody who arrived without the right weapon —
    /// and arriving without it is what happens on the way in.
    /// </remarks>
    private static int AttackRatingAt(int level) => (level / 2) + 5;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DikuWeb.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static WorldBundle World()
    {
        var sources = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "content"), "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(path =>
            {
                Assert.True(BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var error), error);
                return new BundleSource(path, bundle!);
            })
            .ToList();

        var merged = BundleMerge.Merge(sources);
        Assert.True(merged.Ok, string.Join("\n", merged.Errors));
        return merged.Bundle!;
    }

    private static Multipliers From(IReadOnlyDictionary<string, decimal>? values)
    {
        var m = new Multipliers();

        if (values is null)
        {
            return m;
        }

        decimal Get(string key, decimal fallback) =>
            values.TryGetValue(key, out var v) ? v : fallback;

        m.Strength = Get("strength", 1m);
        m.Health = Get("health", 1m);
        m.Damage = Get("damage", 1m);
        m.Xp = Get("xp", 1m);
        m.Gold = Get("gold", 1m);
        m.ItemValue = Get("itemValue", 1m);

        return m;
    }

    /// <summary>Every mob a spawner actually places, as it fights after its zone has scaled it.</summary>
    private static IEnumerable<(string Zone, string Mob, int Level, int Defence, int Armor)> Spawned()
    {
        var bundle = World();

        var worlds = bundle.Worlds.ToDictionary(w => w.Key, w => From(w.Multipliers), StringComparer.Ordinal);
        var zones = bundle.Zones.ToDictionary(z => z.Key, StringComparer.Ordinal);
        var mobs = bundle.MobTemplates.ToDictionary(m => m.Key, StringComparer.Ordinal);

        foreach (var spawner in bundle.Spawners.Where(s => s.TemplateKind == TemplateKind.Mob))
        {
            if (!zones.TryGetValue(spawner.ZoneKey, out var zone)
                || !mobs.TryGetValue(spawner.TemplateKey, out var mob))
            {
                continue;
            }

            var worldKey = zone.Key.Split('.')[0];
            var world = worlds.TryGetValue(worldKey, out var w) ? w : new Multipliers();

            var scaling = spawner.FightsAtLevel is { } target
                ? MobScaling.FromTarget(mob.Level, target)
                : MobScaling.FromZone(mob.Level, world, From(zone.Multipliers), zone.MinLevel);

            var resolved = scaling.ResolveStats(mob.BaseStats);

            var defence = StatReader.TryReadInt(resolved, "defense", out var d) ? d : 0;
            var armor = StatReader.TryReadInt(resolved, "armor", out var a) ? a : 0;

            yield return (zone.Key, mob.Key, scaling.Level, defence, armor);
        }
    }

    /// <summary>
    /// A player who has reached a mob's level can land a blow on better than a natural 20.
    /// </summary>
    [Fact]
    public void Every_spawned_mob_can_be_hit_by_someone_of_its_own_level()
    {
        var walls = new List<string>();

        foreach (var (zone, mob, level, defence, _) in Spawned())
        {
            // DamageCalculator: defenceVal = 10 + level/2 + defenceRating, and both sides carry
            // the level/2 so it cancels at equal levels.
            var defenceValue = 10 + (level / 2) + defence;
            var needed = Math.Clamp(defenceValue - AttackRatingAt(level), 2, 20);

            if (needed > WorstNeededRoll)
            {
                walls.Add(
                    $"{zone} / {mob} at level {level}: defence {defence} needs a {needed} "
                    + $"({(21 - needed) * 5}% to hit)");
            }
        }

        Assert.True(
            walls.Count == 0,
            "Out of reach of the die:" + Environment.NewLine + string.Join(Environment.NewLine, walls));
    }

    /// <summary>
    /// And armour never climbs so far that the blow, once landed, stops mattering.
    /// </summary>
    [Fact]
    public void No_spawned_mob_absorbs_almost_everything()
    {
        var sponges = new List<string>();

        foreach (var (zone, mob, level, _, armor) in Spawned())
        {
            var absorbed = ArmorCurve.Mitigation(armor);

            if (absorbed > WorstAbsorbed)
            {
                sponges.Add($"{zone} / {mob} at level {level}: armour {armor} absorbs {absorbed:P0}");
            }
        }

        Assert.True(
            sponges.Count == 0,
            "Absorbing nearly everything:" + Environment.NewLine + string.Join(Environment.NewLine, sponges));
    }
}
