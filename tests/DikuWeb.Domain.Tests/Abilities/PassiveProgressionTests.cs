using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Abilities;

/// <summary>
/// Fighting with a second weapon is trained, not innate, and only the two martial Paths learn
/// it. These are derived from Path and level, so there is nothing to spend and nothing to store.
/// </summary>
public sealed class PassiveProgressionTests
{
    [Theory]
    [InlineData(CharacterPath.Shade, 2, false)]
    [InlineData(CharacterPath.Shade, 3, true)]
    [InlineData(CharacterPath.Warden, 4, false)]
    [InlineData(CharacterPath.Warden, 5, true)]
    [InlineData(CharacterPath.Adept, 50, false)]
    [InlineData(CharacterPath.Hallow, 50, false)]
    public void Dual_wield_unlocks_by_path_and_level(CharacterPath path, int level, bool expected) =>
        Assert.Equal(expected, AbilityProgression.KnowsPassive(path, level, PassiveKeys.DualWield));

    [Theory]
    [InlineData(CharacterPath.Shade, 9, false)]
    [InlineData(CharacterPath.Shade, 10, true)]
    [InlineData(CharacterPath.Warden, 14, false)]
    [InlineData(CharacterPath.Warden, 15, true)]
    [InlineData(CharacterPath.Adept, 50, false)]
    [InlineData(CharacterPath.Hallow, 50, false)]
    public void Ambidextrous_unlocks_by_path_and_level(CharacterPath path, int level, bool expected) =>
        Assert.Equal(expected, AbilityProgression.KnowsPassive(path, level, PassiveKeys.Ambidextrous));

    [Theory]
    [InlineData(CharacterPath.Shade)]
    [InlineData(CharacterPath.Warden)]
    public void Ambidexterity_never_arrives_before_the_hand_that_uses_it(CharacterPath path)
    {
        var passives = AbilityProgression.GetPassivesForPath(path);
        var dualWield = passives.Single(p => p.PassiveKey == PassiveKeys.DualWield).UnlockLevel;
        var ambidextrous = passives.Single(p => p.PassiveKey == PassiveKeys.Ambidextrous).UnlockLevel;

        Assert.True(dualWield < ambidextrous);
    }

    /// <summary>
    /// Passives are kept out of the castable list on purpose: they have no ability row, so a
    /// key that leaked into it would be offered to <c>cast</c> and never resolve.
    /// </summary>
    [Theory]
    [InlineData(CharacterPath.Warden)]
    [InlineData(CharacterPath.Shade)]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Hallow)]
    public void Passives_are_not_castable_abilities(CharacterPath path)
    {
        var abilities = AbilityProgression.GetKnownAbilitiesForLevel(AbilityCatalogue.AsAbilities, path, 50);

        Assert.DoesNotContain(PassiveKeys.DualWield, abilities);
        Assert.DoesNotContain(PassiveKeys.Ambidextrous, abilities);
    }
}
