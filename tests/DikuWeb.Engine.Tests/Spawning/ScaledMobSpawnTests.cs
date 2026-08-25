using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Spawning;

/// <summary>
/// A zone's dials reaching a spawned mob, through the spawner and out the other side into combat
/// (PLAN.md §4.4).
/// </summary>
/// <remarks>
/// <c>MobScalingTests</c> covers the arithmetic on a bag built in C#. This covers the two things it
/// cannot: that the numbers survive the <b>jsonb round trip</b> — every stat comes back a
/// <c>JsonElement</c> and code that pattern-matched the C# shape was false for all of them
/// (§12) — and that <see cref="DamageCalculator"/> then reads what the spawner wrote.
/// </remarks>
public sealed class ScaledMobSpawnTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static Mob Spawn(
        WorldHarness harness,
        Dictionary<string, object> baseStats,
        int templateLevel = 8)
    {
        // Through jsonb, which is the only shape the running game ever sees.
        var template = new MobTemplate
        {
            Key = "kobold-sentry",
            Name = "kobold sentry",
            Icon = "k",
            Level = templateLevel,
            BaseStats = WorldHarness.AsPersisted(baseStats),
        };

        return new MobSpawner().Spawn(template, harness.Zone, harness.World_, West);
    }

    private static WorldHarness Scaled(decimal strength = 1m, decimal damage = 1m)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.SetZoneMultipliers(m =>
        {
            m.Strength = strength;
            m.Damage = damage;
        });
        return harness;
    }

    [Fact]
    public void A_scaled_zone_makes_a_mob_hit_harder_and_not_only_survive_longer()
    {
        // The defect this slice exists for. ResolvedStats was a verbatim copy of the template, so
        // the master difficulty dial doubled a mob's health pool and left its punch untouched -
        // a x6 zone was six times the fight only in the sense that it took six times as long.
        var mob = Spawn(Scaled(strength: 2.5m), new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damage"] = "4-7",
        });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(100, mob.Vitals.HealthMax);
        Assert.Equal(10, stats.MinDamage);
        Assert.Equal(18, stats.MaxDamage);
    }

    [Fact]
    public void The_damage_dial_alone_reaches_combat()
    {
        // It reached nothing at all before: MultiplierType.Damage was never passed to Resolve
        // anywhere in the codebase.
        var mob = Spawn(Scaled(damage: 3m), new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damageMin"] = 5,
            ["damageMax"] = 9,
        });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(15, stats.MinDamage);
        Assert.Equal(27, stats.MaxDamage);
        Assert.Equal(40, mob.Vitals.HealthMax);
    }

    [Fact]
    public void A_template_that_declares_no_combat_stats_still_scales()
    {
        // The common case, and the one that made EffectiveLevel a promise the fight did not keep.
        // DamageCalculator falls back to level-derived values for a silent template, and those read
        // Mob.Level - so a rat in a x4 zone had four times the health and swung like the level 8 it
        // was authored as.
        var scaled = Spawn(Scaled(strength: 4m), new Dictionary<string, object> { ["health"] = 40 });
        var plain = Spawn(Scaled(), new Dictionary<string, object> { ["health"] = 40 });

        Assert.Equal(32, scaled.EffectiveLevel);
        Assert.True(
            DamageCalculator.StatsFrom(scaled).AttackRating > DamageCalculator.StatsFrom(plain).AttackRating,
            "A mob the zone lifted to level 32 should be harder to evade than the level 8 it was authored as.");

        // Defence carries the lifted level rather than a rating derived from it: the number to beat
        // is 10 + level/2 + rating (§4.6), so the level is what makes the lifted mob harder to hit.
        // A silent template's rating is zero on both, and asserting on that compared nothing.
        Assert.Equal(32, DamageCalculator.DefenderStatsFrom(scaled).Level);
        Assert.Equal(8, DamageCalculator.DefenderStatsFrom(plain).Level);
    }

    [Fact]
    public void An_unscaled_zone_spawns_exactly_what_was_authored()
    {
        // Every live zone is at 1.0, so this is the case that must not have moved. It is also the
        // reason the whole suite passed while the resolve was wrong.
        var mob = Spawn(Scaled(), new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damage"] = "4-7",
            ["attackRating"] = 9,
        });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(40, mob.Vitals.HealthMax);
        Assert.Equal(8, mob.EffectiveLevel);
        Assert.Equal(4, stats.MinDamage);
        Assert.Equal(7, stats.MaxDamage);

        // The authored rating survives the unscaled zone untouched and then takes the skill factor
        // every mob takes, which is the one thing between a template's number and its accuracy.
        Assert.Equal(11, stats.AttackRating);   // round(1.25 x 9)
    }

    [Fact]
    public void A_damage_multiplier_is_applied_once_not_twice()
    {
        // damageMultiplier is a ratio the template carries and DamageCalculator applies to
        // whichever dice are in play. Scaling it at spawn as well would square the zone.
        var mob = Spawn(Scaled(damage: 2m), new Dictionary<string, object>
        {
            ["damageMin"] = 5,
            ["damageMax"] = 5,
            ["damageMultiplier"] = 1.5m,
        });

        var stats = DamageCalculator.StatsFrom(mob);

        // 5 doubled by the zone, then x1.5 by the template's own ratio.
        Assert.Equal(15, stats.MinDamage);
    }
}
