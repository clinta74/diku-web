using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Tests.Characters;

public sealed class CharacterProgressionTests
{
    [Fact]
    public void TryLevelUp_no_level_up_returns_null()
    {
        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 500,
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        Assert.Null(result);
    }

    [Fact]
    public void TryLevelUp_at_exact_threshold_levels_up()
    {
        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 1000, // Exactly level 2
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        Assert.NotNull(result);
        Assert.Equal(2, result.NewLevel);
        Assert.Equal(1, result.LeveledUp);
    }

    [Fact]
    public void TryLevelUp_applies_path_growth()
    {
        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 1000,
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        Assert.NotNull(result);
        // Warden gains +2 Might, +1 Agility, +2 Vitality, +0 Insight, +1 Resolve
        Assert.Equal(12, result.NewAttributes.Might);
        Assert.Equal(11, result.NewAttributes.Agility);
        Assert.Equal(12, result.NewAttributes.Vitality);
        Assert.Equal(10, result.NewAttributes.Insight);
        Assert.Equal(11, result.NewAttributes.Resolve);
    }

    [Fact]
    public void TryLevelUp_applies_different_growth_per_path()
    {
        var wardenResult = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 1000,
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        var adeptResult = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 1000,
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Adept,
            vitals: Vitals.StartingFor(CharacterPath.Adept));

        Assert.NotNull(wardenResult);
        Assert.NotNull(adeptResult);

        // Warden: +2 Might, Adept: +0 Might
        Assert.Equal(12, wardenResult.NewAttributes.Might);
        Assert.Equal(10, adeptResult.NewAttributes.Might);

        // Warden: +0 Insight, Adept: +2 Insight
        Assert.Equal(10, wardenResult.NewAttributes.Insight);
        Assert.Equal(12, adeptResult.NewAttributes.Insight);
    }

    [Fact]
    public void TryLevelUp_recalculates_vital_maxima()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var originalHealthMax = vitals.HealthMax;

        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 1000,
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: vitals);

        Assert.NotNull(result);
        // Level 2, Vitality becomes 12 (modifier +1)
        // Health = 60 + (2-1)*5 + 1*2 = 60 + 5 + 2 = 67
        Assert.Equal(67, result.NewVitals.HealthMax);
        Assert.True(result.NewVitals.HealthMax > originalHealthMax);
    }

    [Fact]
    public void TryLevelUp_multiple_levels_at_once()
    {
        // Skip to level 5 worth of XP
        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 10000,
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        Assert.NotNull(result);
        Assert.Equal(5, result.NewLevel);
        Assert.Equal(4, result.LeveledUp); // Levels 1->2, 2->3, 3->4, 4->5
    }

    [Fact]
    public void TryLevelUp_capped_at_max_level()
    {
        var result = CharacterProgression.TryLevelUp(
            currentLevel: 49,
            currentXp: 10000000, // Way more than level 50
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        Assert.NotNull(result);
        Assert.Equal(50, result.NewLevel);
    }

    [Fact]
    public void TryLevelUp_preserves_current_vitals()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        vitals.Health = 30;
        vitals.Focus = 10;
        vitals.Stamina = 50;

        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 1000,
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: vitals);

        Assert.NotNull(result);
        // Current vitals should not change (max does, but not current)
        Assert.Equal(30, result.NewVitals.Health);
        Assert.Equal(10, result.NewVitals.Focus);
        Assert.Equal(50, result.NewVitals.Stamina);
    }

    [Fact]
    public void AwardXp_negative_xp_throws()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Name = "Test",
            Path = CharacterPath.Warden,
            Level = 1,
            Xp = 0,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Warden),
            RoomKey = RoomKey.Create("test", "zone", "room"),
            CreatedAt = DateTimeOffset.Now,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterProgression.AwardXp(character, xpAwarded: -100));
    }

    [Fact]
    public void AwardXp_adds_to_total()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Name = "Test",
            Path = CharacterPath.Warden,
            Level = 1,
            Xp = 500,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Warden),
            RoomKey = RoomKey.Create("test", "zone", "room"),
            CreatedAt = DateTimeOffset.Now,
        };

        var result = CharacterProgression.AwardXp(character, xpAwarded: 600);

        Assert.NotNull(result);
        // 500 + 600 = 1100, which is level 2
        Assert.Equal(2, result.NewLevel);
    }

    [Fact]
    public void AwardXp_no_level_up_returns_null()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Name = "Test",
            Path = CharacterPath.Warden,
            Level = 1,
            Xp = 500,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Warden),
            RoomKey = RoomKey.Create("test", "zone", "room"),
            CreatedAt = DateTimeOffset.Now,
        };

        var result = CharacterProgression.AwardXp(character, xpAwarded: 100);

        Assert.Null(result);
    }

    [Fact]
    public void TryLevelUp_with_negative_attribute_modifier()
    {
        var lowAttr = new AttributeSet
        {
            Might = 8,     // modifier -1
            Agility = 6,   // modifier -2
            Vitality = 5,  // modifier -2 (round down from -2.5)
            Insight = 8,
            Resolve = 8,
        };

        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 1000,
            attributes: lowAttr,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        Assert.NotNull(result);
        // Health should be reduced by low Vitality modifier
        var expectedHealth = 60 + 5 + (-2 * 2); // 60 + level bonus - vitality penalty
        Assert.Equal(expectedHealth, result.NewVitals.HealthMax);
    }

    [Fact]
    public void TryLevelUp_multiple_levels_stacks_growth()
    {
        var result = CharacterProgression.TryLevelUp(
            currentLevel: 1,
            currentXp: 6000, // Level 4
            attributes: AttributeSet.Baseline,
            path: CharacterPath.Warden,
            vitals: Vitals.StartingFor(CharacterPath.Warden));

        Assert.NotNull(result);
        Assert.Equal(4, result.NewLevel);
        // Warden grows +2 Might per level, so 3 levels = +6 total
        Assert.Equal(16, result.NewAttributes.Might);  // 10 + 2 + 2 + 2
        // +1 Agility per level, so 3 levels = +3
        Assert.Equal(13, result.NewAttributes.Agility); // 10 + 1 + 1 + 1
    }

    [Fact]
    public void All_level_1_characters_have_starting_vitals()
    {
        var paths = new[] { CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Shade, CharacterPath.Hallow };

        foreach (var path in paths)
        {
            var vitals = Vitals.StartingFor(path);
            Assert.True(vitals.Health > 0);
            Assert.True(vitals.Focus >= 0);
            Assert.True(vitals.Stamina > 0);
        }
    }
}
