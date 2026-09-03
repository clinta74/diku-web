using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Randomness;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// Ability damage answers to the caster's level (PLAN.md §4.6, docs/ABILITY_SCALING.md).
/// </summary>
/// <remarks>
/// <b>The thing these guard is that the authored content did not have to change.</b> Every
/// <c>scalingFactor</c> in <c>abilities.json</c> was written against a base of 10, so the level
/// term had to leave level 1 exactly where it was and grow from there — anything else would have
/// silently retuned sixty abilities at once. The first test below is the one that says so.
/// </remarks>
public sealed class AbilityLevelScalingTests
{
    private static Dictionary<string, string> Damage(string factor) =>
        new(StringComparer.Ordinal) { ["scalingFactor"] = factor };

    /// <summary>
    /// A level 1 caster deals exactly what the flat base used to deal, so no authored factor
    /// changed meaning when the level term arrived.
    /// </summary>
    [Fact]
    public void Level_one_is_unchanged_by_the_level_term()
    {
        Assert.Equal(DamageEffect.UnscaledBaseDamage, DamageEffect.BaseAtLevel(1));

        // The three factors at the ends and middle of the authored range.
        Assert.Equal(11, DamageEffect.Middle(Damage("1.1"), 1));
        Assert.Equal(20, DamageEffect.Middle(Damage("2.0"), 1));
        Assert.Equal(35, DamageEffect.Middle(Damage("3.5"), 1));
    }

    /// <summary>
    /// The base grows with the caster, which is the whole point: it used to be a constant, so an
    /// ability learned at level 16 dealt the same damage against a level 50 mob with thirty times
    /// the health.
    /// </summary>
    [Fact]
    public void The_base_grows_with_the_caster()
    {
        var first = DamageEffect.BaseAtLevel(1);
        var last = DamageEffect.BaseAtLevel(50);

        Assert.True(last > first, "a level 50 caster must hit harder than a level 1 one");

        // Monotone the whole way, so no level is a step backwards.
        for (var level = 2; level <= 50; level++)
        {
            Assert.True(
                DamageEffect.BaseAtLevel(level) >= DamageEffect.BaseAtLevel(level - 1),
                $"level {level} deals less than level {level - 1}");
        }
    }

    /// <summary>
    /// A level that was never set reads as level 1 rather than as zero or negative — a negative
    /// base would turn a damage ability into a heal.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void An_absent_level_floors_at_one(int level)
    {
        Assert.Equal(DamageEffect.UnscaledBaseDamage, DamageEffect.BaseAtLevel(level));
        Assert.True(DamageEffect.Middle(Damage("2.0"), level) > 0);
    }

    /// <summary>
    /// <c>minDamage</c> still wins where the scaled value falls under it, so the floor an author
    /// wrote is honoured at every level rather than only at the bottom.
    /// </summary>
    [Fact]
    public void An_authored_floor_still_wins_when_it_is_higher()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["scalingFactor"] = "0.01",
            ["minDamage"] = "7",
        };

        Assert.Equal(7, DamageEffect.Middle(parameters, 1));
        Assert.Equal(7, DamageEffect.Middle(parameters, 50));
    }

    /// <summary>
    /// What the executor deals and what it says it deals move together.
    /// </summary>
    /// <remarks>
    /// The reason <c>Describe</c> takes a level at all. A listing generated without one would quote
    /// the level 1 number to every character in the game — a screen disagreeing with the game,
    /// which is the failure this codebase keeps rediscovering.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    public void What_it_says_is_what_it_deals(int level)
    {
        var effect = new DamageEffect();
        var parameters = Damage("2.0");

        var middle = DamageEffect.Middle(parameters, level);
        var spread = DamageEffect.Variance(middle);

        var phrase = effect.Describe(parameters, TargetingType.SingleTarget, level);

        Assert.Contains((middle - spread).ToString(), phrase, StringComparison.Ordinal);
        Assert.Contains((middle + spread).ToString(), phrase, StringComparison.Ordinal);
    }

    /// <summary>
    /// The level comes off the caster when the ability actually lands, not off a default.
    /// </summary>
    [Fact]
    public void Apply_reads_the_caster()
    {
        static Character Caster(int level) => new()
        {
            AccountId = Guid.Empty,
            Name = "Kaeda",
            Path = CharacterPath.Adept,
            Level = level,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Adept),
            RoomKey = Muwbta.Domain.Worlds.RoomKey.Create("t", "t", "t"),
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        static Mob Target() => new()
        {
            TemplateKey = "dummy",
            RoomKey = "t.t.t",
            Vitals = new Vitals { Health = 100_000, HealthMax = 100_000 },
        };

        var effect = new DamageEffect();
        var parameters = Damage("2.0");

        var low = Target();
        var high = Target();

        // A fixed source, so the only thing differing between the two is the caster's level.
        effect.Apply(Caster(1), low, parameters, new SeededRandomSource(1));
        effect.Apply(Caster(50), high, parameters, new SeededRandomSource(1));

        Assert.True(
            100_000 - high.Vitals.Health > 100_000 - low.Vitals.Health,
            "a level 50 caster must wound harder than a level 1 one");
    }

    /// <summary>
    /// A mob casting reads the level its zone scaled it to, not the one its template was authored
    /// at — the same choice <c>DamageCalculator.FightingLevel</c> makes.
    /// </summary>
    [Fact]
    public void A_scaled_mob_casts_at_the_level_it_fights_at()
    {
        var mob = new Mob
        {
            TemplateKey = "dummy",
            RoomKey = "t.t.t",
            Level = 4,
            EffectiveLevel = 40,
        };

        Assert.Equal(40, DamageEffect.LevelOf(mob));

        // Never through the spawner: the authored level is the only level there is.
        var unscaled = new Mob { TemplateKey = "dummy", RoomKey = "t.t.t", Level = 4 };

        Assert.Equal(4, DamageEffect.LevelOf(unscaled));
    }
}
