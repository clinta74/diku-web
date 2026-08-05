using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Tests.Worlds;

public sealed class MultiplierTests
{
    [Fact]
    public void Default_multipliers_are_identity()
    {
        var mults = new Multipliers();

        Assert.Equal(1.0m, mults.Strength);
        Assert.Equal(1.0m, mults.Health);
        Assert.Equal(1.0m, mults.Damage);
        Assert.Equal(1.0m, mults.Xp);
        Assert.Equal(1.0m, mults.Gold);
        Assert.Equal(1.0m, mults.ItemValue);
        Assert.Equal(1.0m, mults.ItemPower);
        Assert.Equal(1.0m, mults.SpawnDensity);
    }

    [Theory]
    [InlineData(40, 1.0, 1.0, 40)]                  // baseline
    [InlineData(40, 2.5, 1.0, 100)]                 // zone multiplier only
    [InlineData(40, 1.0, 2.0, 80)]                  // world multiplier only
    [InlineData(40, 2.5, 2.0, 200)]                 // zone × world composed
    public void Strength_multiplies_health(int baseHealth, decimal worldMult, decimal zoneMult, int expected)
    {
        var world = new Multipliers { Strength = worldMult };
        var zone = new Multipliers { Strength = zoneMult };

        var result = Multipliers.Resolve(baseHealth, world, zone, MultiplierType.Strength);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(40, 0.0, 1.0)]   // 0 multiplier clamps to 1 (health never scales to 0)
    [InlineData(40, 0.5, 1.0)]   // fractional still yields >= 1
    [InlineData(1, 1.0, 1.0)]    // baseline 1 stays 1
    public void Health_never_scales_below_one(int baseHealth, decimal worldMult, decimal zoneMult)
    {
        var world = new Multipliers { Health = worldMult };
        var zone = new Multipliers { Health = zoneMult };

        var result = Multipliers.Resolve(baseHealth, world, zone, MultiplierType.Health);

        Assert.True(result >= 1, $"Health scaled to {result}, expected >= 1");
    }

    [Theory]
    [InlineData(4, 1.0, 1.0, 4)]     // baseline
    [InlineData(4, 2.0, 1.5, 12)]    // 4 × 2 × 1.5 = 12
    [InlineData(4, 0.5, 0.5, 1)]     // 4 × 0.5 × 0.5 = 1 (clamped to 1)
    public void Damage_never_scales_below_one(int baseDamage, decimal worldMult, decimal zoneMult, int expected)
    {
        var world = new Multipliers { Damage = worldMult };
        var zone = new Multipliers { Damage = zoneMult };

        var result = Multipliers.Resolve(baseDamage, world, zone, MultiplierType.Damage);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(120, 1.0, 1.0, 120)]  // baseline
    [InlineData(120, 1.0, 2.5, 300)]  // 120 × 2.5 = 300
    [InlineData(100, 0.0, 1.0, 0)]    // can scale to 0
    [InlineData(100, 1.0, 0.0, 0)]    // zone 0 yields 0
    public void Xp_can_scale_to_zero(int baseXp, decimal worldMult, decimal zoneMult, int expected)
    {
        var world = new Multipliers { Xp = worldMult };
        var zone = new Multipliers { Xp = zoneMult };

        var result = Multipliers.Resolve(baseXp, world, zone, MultiplierType.Xp);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(25, 1.0, 1.0, 25)]   // baseline
    [InlineData(25, 3.0, 1.0, 75)]   // 25 × 3.0 = 75
    [InlineData(50, 2.0, 0.0, 0)]    // 50 × 2.0 × 0.0 = 0 (stingy zone)
    public void Gold_can_scale_to_zero(int baseGold, decimal worldMult, decimal zoneMult, int expected)
    {
        var world = new Multipliers { Gold = worldMult };
        var zone = new Multipliers { Gold = zoneMult };

        var result = Multipliers.Resolve(baseGold, world, zone, MultiplierType.Gold);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Rounding_is_half_away_from_zero()
    {
        // half-away-from-zero: 0.5 rounds to 1, 1.5 rounds to 2, -0.5 rounds to -1
        var world = new Multipliers();

        // 3 × 1.5 = 4.5 → rounds to 5
        var zone1 = new Multipliers { Strength = 1.5m };
        var result1 = Multipliers.Resolve(3, world, zone1, MultiplierType.Strength);
        Assert.Equal(5, result1);

        // 5 × 0.3 = 1.5 → rounds to 2
        var zone2 = new Multipliers { Strength = 0.3m };
        var result2 = Multipliers.Resolve(5, world, zone2, MultiplierType.Strength);
        Assert.Equal(2, result2);

        // 1 × 0.4 = 0.4 → rounds to 0, but clamped to 1
        var zone3 = new Multipliers { Strength = 0.4m };
        var result3 = Multipliers.Resolve(1, world, zone3, MultiplierType.Strength);
        Assert.Equal(1, result3);
    }

    [Fact]
    public void Multipliers_compose_world_and_zone()
    {
        // Worked example from PLAN.md §4.4:
        // kobold-sentry template: base 40 hp
        // sunken-crypt zone: strength 2.5, gold 3.0, xp 1.0
        // no world multipliers (all 1.0)

        var worldMults = new Multipliers();  // all 1.0
        var zoneMults = new Multipliers { Strength = 2.5m, Gold = 3.0m, Xp = 1.0m };

        var hp = Multipliers.Resolve(40, worldMults, zoneMults, MultiplierType.Strength);
        Assert.Equal(100, hp);

        var gold = Multipliers.Resolve(25, worldMults, zoneMults, MultiplierType.Gold);
        Assert.Equal(75, gold);

        var xp = Multipliers.Resolve(120, worldMults, zoneMults, MultiplierType.Xp);
        Assert.Equal(120, xp);
    }

    [Fact]
    public void World_and_zone_multipliers_compound()
    {
        // World-level and zone-level both scale: 40 × world(2.0) × zone(2.5) = 200
        var world = new Multipliers { Strength = 2.0m };
        var zone = new Multipliers { Strength = 2.5m };

        var result = Multipliers.Resolve(40, world, zone, MultiplierType.Strength);

        Assert.Equal(200, result);
    }

    [Fact]
    public void All_multiplier_types_resolve_independently()
    {
        var world = new Multipliers { Strength = 2.0m, Xp = 0.5m, ItemPower = 1.5m };
        var zone = new Multipliers { Strength = 1.5m, Xp = 2.0m, ItemPower = 1.0m };

        var strength = Multipliers.Resolve(40, world, zone, MultiplierType.Strength);
        var xp = Multipliers.Resolve(100, world, zone, MultiplierType.Xp);
        var itemPower = Multipliers.Resolve(5, world, zone, MultiplierType.ItemPower);

        // 40 × 2.0 × 1.5 = 120
        Assert.Equal(120, strength);

        // 100 × 0.5 × 2.0 = 100
        Assert.Equal(100, xp);

        // 5 × 1.5 × 1.0 = 7.5 → rounds to 8
        Assert.Equal(8, itemPower);
    }
}
