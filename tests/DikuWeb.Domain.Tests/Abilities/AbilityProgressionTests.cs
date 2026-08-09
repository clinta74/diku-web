using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Abilities;

public sealed class AbilityProgressionTests
{
    [Fact]
    public void GetAbilitiesForPath_Warden_ReturnsWardenAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(CharacterPath.Warden);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("warden.", a.AbilityKey));
        Assert.Contains((1, "warden.kick"), abilities);
    }

    [Fact]
    public void GetAbilitiesForPath_Adept_ReturnsAdeptAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(CharacterPath.Adept);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("adept.", a.AbilityKey));
        Assert.Contains((1, "adept.bolt"), abilities);
    }

    [Fact]
    public void GetAbilitiesForPath_Shade_ReturnsShadeAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(CharacterPath.Shade);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("shade.", a.AbilityKey));
    }

    [Fact]
    public void GetAbilitiesForPath_Channeler_ReturnsChannelerAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(CharacterPath.Channeler);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("channeler.", a.AbilityKey));
        Assert.Contains((1, "channeler.mend"), abilities);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_Level1_ReturnsLevel1Only()
    {
        // Act
        var known = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 1);

        // Assert
        Assert.Single(known);
        Assert.Contains("warden.kick", known);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_Level3_ReturnsLevel1And3()
    {
        // Act
        var known = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 3);

        // Assert
        Assert.Equal(2, known.Count);
        Assert.Contains("warden.kick", known);
        Assert.Contains("warden.bash", known);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_Level6_ReturnsEverythingUnlockedSoFar()
    {
        // Act
        var known = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 6);

        // Assert
        Assert.Equal(3, known.Count);
        Assert.Contains("warden.kick", known);
        Assert.Contains("warden.bash", known);

        // Battle Fury, not Parry. This used to expect parry at 6 and call the result "all",
        // which was doubly wrong: progression carried on past 6 in neither direction, and parry
        // had no ability row behind it, so the level-up granted something uncastable.
        Assert.Contains("warden.battle-fury", known);
        Assert.DoesNotContain("warden.sunder", known);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_KeepsGrantingPastLevelSix()
    {
        var atSix = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 6);
        var atTwenty = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 20);

        Assert.True(atTwenty.Count > atSix.Count);
    }

    [Fact]
    public void Knows_WithKnownAbility_ReturnsTrue()
    {
        // Act
        var knows = AbilityProgression.Knows(CharacterPath.Channeler, 1, "channeler.mend");

        // Assert
        Assert.True(knows);
    }

    [Fact]
    public void Knows_BelowUnlockLevel_ReturnsFalse()
    {
        // Act - Warden only gets bash at level 3
        var knows = AbilityProgression.Knows(CharacterPath.Warden, 2, "warden.bash");

        // Assert
        Assert.False(knows);
    }

    [Fact]
    public void Knows_WithUnknownAbility_ReturnsFalse()
    {
        // Act
        var knows = AbilityProgression.Knows(CharacterPath.Warden, 10, "nonexistent.ability");

        // Assert
        Assert.False(knows);
    }
}
