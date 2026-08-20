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
    [InlineData(CharacterPath.Temper, 2, false)]
    [InlineData(CharacterPath.Temper, 3, true)]
    [InlineData(CharacterPath.Warden, 4, false)]
    [InlineData(CharacterPath.Warden, 5, true)]
    [InlineData(CharacterPath.Adept, 50, false)]
    [InlineData(CharacterPath.Hallow, 50, false)]
    public void Dual_wield_unlocks_by_path_and_level(CharacterPath path, int level, bool expected) =>
        Assert.Equal(expected, AbilityProgression.KnowsPassive(path, level, PassiveKeys.DualWield));

    [Theory]
    [InlineData(CharacterPath.Temper, 9, false)]
    [InlineData(CharacterPath.Temper, 10, true)]
    [InlineData(CharacterPath.Warden, 14, false)]
    [InlineData(CharacterPath.Warden, 15, true)]
    [InlineData(CharacterPath.Adept, 50, false)]
    [InlineData(CharacterPath.Hallow, 50, false)]
    public void Ambidextrous_unlocks_by_path_and_level(CharacterPath path, int level, bool expected) =>
        Assert.Equal(expected, AbilityProgression.KnowsPassive(path, level, PassiveKeys.Ambidextrous));

    [Theory]
    [InlineData(CharacterPath.Temper)]
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
    [InlineData(CharacterPath.Temper)]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Hallow)]
    public void Passives_are_not_castable_abilities(CharacterPath path)
    {
        var abilities = AbilityProgression.GetKnownAbilitiesForLevel(AbilityCatalogue.AsAbilities, path, 50);

        Assert.DoesNotContain(PassiveKeys.DualWield, abilities);
        Assert.DoesNotContain(PassiveKeys.Ambidextrous, abilities);
    }

    /// <summary>
    /// The passive keys and the ability keys are disjoint, and cannot be made to overlap.
    /// </summary>
    /// <remarks>
    /// <b>Two sources of truth now answer "what does this Path get at level N".</b> Abilities are
    /// rows in a table a builder edits; passives are these three constants, because a passive has
    /// no row, no cost, and nothing to target. That split is deliberate and was confirmed as the
    /// right call — but it is the same shape as the seeder-versus-progression drift 5.1e removed,
    /// so it is held by this test rather than by everyone remembering.
    ///
    /// It holds structurally rather than by vigilance, which is the part worth pinning: an ability
    /// key must begin with the name of the Path that learns it, no Path is called "passive", and so
    /// no ability can ever be given a key in the passive namespace. The check below is that those
    /// two rules — living in <c>PassiveKeys</c> and in <c>AbilityValidator</c> — still agree.
    /// </remarks>
    [Fact]
    public void No_passive_key_can_ever_collide_with_an_ability_key()
    {
        string[] passives = [PassiveKeys.DualWield, PassiveKeys.Ambidextrous, PassiveKeys.Parry];
        var abilityKeys = AbilityCatalogue.AsAbilities.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var passive in passives)
        {
            Assert.DoesNotContain(passive, abilityKeys);
        }

        // And the reason it cannot happen by accident later: every Path prefix is something other
        // than "passive.", so the validator's key rule forbids the whole namespace.
        foreach (var path in Enum.GetValues<CharacterPath>())
        {
            var prefix = path.ToString().ToLowerInvariant() + ".";

            Assert.All(passives, p =>
                Assert.False(
                    p.StartsWith(prefix, StringComparison.Ordinal),
                    $"Passive '{p}' sits in {path}'s ability namespace."));
        }
    }
}
