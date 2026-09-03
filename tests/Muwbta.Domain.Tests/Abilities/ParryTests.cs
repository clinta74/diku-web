using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// Parry as a passive.
/// </summary>
/// <remarks>
/// It was a castable self-heal on a 32-pulse cooldown, which is not what parrying is: a fighter
/// turns a blow aside continuously, and an ability version has to be spent *before* the blow it
/// was meant to stop. As a passive it needs no button and cannot be mistimed.
/// </remarks>
public sealed class ParryTests
{
    [Theory]
    [InlineData(CharacterPath.Warden, 4)]
    [InlineData(CharacterPath.Temper, 8)]
    public void The_martial_paths_learn_to_parry(CharacterPath path, int level)
    {
        Assert.True(AbilityProgression.KnowsPassive(path, level, PassiveKeys.Parry));
        Assert.True(AbilityProgression.ParryChance(path, level) > 0);
    }

    [Theory]
    [InlineData(CharacterPath.Warden, 3)]
    [InlineData(CharacterPath.Temper, 7)]
    public void Nobody_parries_before_they_have_learned_how(CharacterPath path, int level)
    {
        Assert.False(AbilityProgression.KnowsPassive(path, level, PassiveKeys.Parry));
        Assert.Equal(0.0, AbilityProgression.ParryChance(path, level));
    }

    [Theory]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Hallow)]
    public void The_casting_paths_never_parry(CharacterPath path)
    {
        // Even at the level cap. Standing in the way of a blade is not what either Path does.
        Assert.False(AbilityProgression.KnowsPassive(path, 20, PassiveKeys.Parry));
        Assert.Equal(0.0, AbilityProgression.ParryChance(path, 20));
    }

    [Fact]
    public void A_warden_parries_more_reliably_than_a_blade()
    {
        // A shield and a braced stance against footwork. If these ever equalise, the Warden has
        // lost the thing that distinguishes it from the other martial Path.
        var warden = AbilityProgression.ParryChance(CharacterPath.Warden, 20);
        var temper = AbilityProgression.ParryChance(CharacterPath.Temper, 20);

        Assert.True(warden > temper, $"Warden {warden} should exceed Temper {temper}.");
    }

    [Fact]
    public void The_parry_chance_stays_a_chance()
    {
        // A parry that always fires makes the Path unkillable by anything that swings.
        foreach (var path in new[]
        {
            CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Temper, CharacterPath.Hallow,
        })
        {
            var chance = AbilityProgression.ParryChance(path, 20);
            Assert.InRange(chance, 0.0, 0.5);
        }
    }

    [Fact]
    public void Parry_is_not_castable()
    {
        // A passive has no ability row, so letting one into the ability list would put a key in
        // front of `cast` that can never resolve - which is exactly what the old warden.parry
        // entry did at level 6.
        var castable = AbilityProgression.GetAbilitiesForPath(AbilityCatalogue.AsAbilities, CharacterPath.Warden)
            .Select(x => x.AbilityKey)
            .ToList();

        Assert.DoesNotContain(PassiveKeys.Parry, castable);
        Assert.DoesNotContain("warden.parry", castable);
    }

    [Fact]
    public void Parry_has_a_name_and_a_description_for_the_abilities_screen()
    {
        Assert.Equal("Parry", PassiveKeys.NameOf(PassiveKeys.Parry));
        Assert.NotEqual(string.Empty, PassiveKeys.DescriptionOf(PassiveKeys.Parry));
    }
}
