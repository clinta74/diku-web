using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Tests.Inhabitants;

/// <summary>
/// The level a mob actually fights at, once its zone has scaled it (PLAN.md §4.4, §4.7).
/// </summary>
public sealed class MobLevelTests
{
    private static Multipliers None => new();

    private static Multipliers With(decimal strength = 1m, decimal health = 1m, decimal damage = 1m) =>
        new() { Strength = strength, Health = health, Damage = damage };

    private static int Level(int templateLevel, Multipliers zone, int minLevel = 1) =>
        MobLevel.Effective(templateLevel, None, zone, minLevel);

    [Fact]
    public void An_untouched_zone_is_exactly_the_identity()
    {
        // The one that has to hold before any of the rest matters: every multiplier at its default
        // must leave the authored level alone, or the derivation quietly retunes every zone nobody
        // has edited.
        for (var level = 1; level <= 50; level++)
        {
            Assert.Equal(level, Level(level, None));
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 4)]
    [InlineData(10, 3)]
    public void Strength_moves_the_level_in_proportion(int templateLevel, int strength)
    {
        // Strength scales health and damage together, so it is already the whole power ratio's
        // square root: doubling it doubles the level.
        Assert.Equal(templateLevel * strength, Level(templateLevel, With(strength: strength)));
    }

    [Fact]
    public void Four_times_the_power_is_twice_the_level()
    {
        // The anchor. XpForLevel is quadratic, so power tracks the square of level throughout the
        // game - and the derivation has to use the same exponent or a "level" would mean one thing
        // in the progression curve and another here.
        //
        // Health x4 and damage x4 is sixteen times the power, which is four times the level.
        Assert.Equal(20, Level(5, With(health: 4m, damage: 4m)));

        // And one-sided scaling takes the root: four times the health alone is twice the level.
        Assert.Equal(10, Level(5, With(health: 4m)));
    }

    [Fact]
    public void The_zones_band_is_a_floor_underneath_the_scaling()
    {
        // Two different statements by the author, and both are theirs: the multipliers are how hard
        // they made it, the band is who they made it for. A flavour critter in a level 40 zone is
        // not prey even if nobody touched a dial for it.
        Assert.Equal(40, Level(1, None, minLevel: 40));

        // Scaling that already clears the band wins on its own.
        Assert.Equal(50, Level(10, With(strength: 5m), minLevel: 40));
    }

    [Fact]
    public void A_boss_above_its_band_keeps_its_level()
    {
        // Floored, never clamped: MaxLevel is not consulted, because an author who levelled a mob
        // above the band meant it.
        Assert.Equal(45, Level(45, None, minLevel: 10));
    }

    [Fact]
    public void Nothing_ever_lands_below_level_one()
    {
        // A zone can multiply downward, and level 0 is not a level - a mob at 0 would be beneath a
        // level 1 character, who is supposed to have nothing beneath them.
        Assert.Equal(1, Level(1, With(strength: 0.1m)));
        Assert.Equal(1, Level(2, With(health: 0.01m, damage: 0.01m)));
    }

    [Fact]
    public void Nonsense_scaling_falls_back_to_the_authored_level()
    {
        // A zone multiplying combat power by zero or below is authored nonsense rather than an
        // invincible mob, and a square root of a negative is worse than either. The label is the
        // honest answer.
        Assert.Equal(7, Level(7, With(strength: 0m)));
        Assert.Equal(7, Level(7, With(damage: -2m)));
    }

    [Fact]
    public void World_and_zone_multipliers_compose()
    {
        // §4.4's rule is base x world x zone, and a level has to compose the same way or a world
        // dial would be silently ignored.
        var world = new Multipliers { Strength = 2m };
        var zone = new Multipliers { Strength = 3m };

        Assert.Equal(30, MobLevel.Effective(5, world, zone, 1));
    }
}
