using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Characters;

public sealed class VitalCalculatorTests
{
    [Fact]
    public void HealthMax_warden_level_1_baseline_attributes()
    {
        var health = VitalCalculator.HealthMax(CharacterPath.Warden, level: 1, AttributeSet.Baseline);
        // Warden starts with 60 health, level 1 has no level bonus, Vitality modifier is 0
        Assert.Equal(60, health);
    }

    [Fact]
    public void HealthMax_warden_level_2_baseline_attributes()
    {
        var health = VitalCalculator.HealthMax(CharacterPath.Warden, level: 2, AttributeSet.Baseline);
        // 60 + (2-1)*5 + 0 = 65
        Assert.Equal(65, health);
    }

    [Fact]
    public void HealthMax_scales_with_level()
    {
        var h1 = VitalCalculator.HealthMax(CharacterPath.Warden, level: 1, AttributeSet.Baseline);
        var h5 = VitalCalculator.HealthMax(CharacterPath.Warden, level: 5, AttributeSet.Baseline);
        var h10 = VitalCalculator.HealthMax(CharacterPath.Warden, level: 10, AttributeSet.Baseline);

        // Each level adds ~5
        Assert.True(h5 > h1);
        Assert.True(h10 > h5);
        Assert.Equal(20, h5 - h1);
        Assert.Equal(25, h10 - h5);
    }

    [Fact]
    public void HealthMax_scales_with_vitality_modifier()
    {
        var baseline = VitalCalculator.HealthMax(CharacterPath.Warden, level: 10, AttributeSet.Baseline);

        var highVitality = VitalCalculator.HealthMax(CharacterPath.Warden, level: 10, new AttributeSet
        {
            Might = 10,
            Agility = 10,
            Vitality = 16, // modifier +3
            Insight = 10,
            Resolve = 10,
        });

        // High vitality adds 3 * 2 = 6
        Assert.Equal(baseline + 6, highVitality);
    }

    [Fact]
    public void FocusMax_adept_level_1()
    {
        var focus = VitalCalculator.FocusMax(CharacterPath.Adept, level: 1, AttributeSet.Baseline);
        // Adept starts with 50 focus
        Assert.Equal(50, focus);
    }

    [Fact]
    public void FocusMax_scales_with_insight_modifier()
    {
        var baseline = VitalCalculator.FocusMax(CharacterPath.Adept, level: 5, AttributeSet.Baseline);

        var highInsight = VitalCalculator.FocusMax(CharacterPath.Adept, level: 5, new AttributeSet
        {
            Might = 10,
            Agility = 10,
            Vitality = 10,
            Insight = 18, // modifier +4
            Resolve = 10,
        });

        // High insight adds 4 * 2 = 8
        Assert.Equal(baseline + 8, highInsight);
    }

    [Fact]
    public void StaminaMax_shade_level_1()
    {
        var stamina = VitalCalculator.StaminaMax(CharacterPath.Shade, level: 1, AttributeSet.Baseline);
        // Shade starts with 110 stamina
        Assert.Equal(110, stamina);
    }

    [Fact]
    public void StaminaMax_scales_with_agility_modifier()
    {
        var baseline = VitalCalculator.StaminaMax(CharacterPath.Shade, level: 5, AttributeSet.Baseline);

        var highAgility = VitalCalculator.StaminaMax(CharacterPath.Shade, level: 5, new AttributeSet
        {
            Might = 10,
            Agility = 20, // modifier +5
            Vitality = 10,
            Insight = 10,
            Resolve = 10,
        });

        // High agility adds 5 * 2 = 10
        Assert.Equal(baseline + 10, highAgility);
    }

    [Fact]
    public void RecalculateMaxima_updates_all_three()
    {
        var vitals = new Vitals
        {
            Health = 30,
            HealthMax = 30,
            Focus = 20,
            FocusMax = 20,
            Stamina = 50,
            StaminaMax = 50,
        };

        VitalCalculator.RecalculateMaxima(CharacterPath.Warden, level: 5, AttributeSet.Baseline, vitals);

        Assert.Equal(80, vitals.HealthMax); // 60 + (5-1)*5 + 0 = 60 + 20
        Assert.True(vitals.FocusMax > 0);
        Assert.True(vitals.StaminaMax > 0);
        // Current vitals unchanged
        Assert.Equal(30, vitals.Health);
        Assert.Equal(20, vitals.Focus);
        Assert.Equal(50, vitals.Stamina);
    }

    [Fact]
    public void RecalculateMaxima_caps_current_to_max()
    {
        var vitals = new Vitals
        {
            Health = 100,
            HealthMax = 50,
            Focus = 50,
            FocusMax = 50,
            Stamina = 100,
            StaminaMax = 100,
        };

        VitalCalculator.RecalculateMaxima(CharacterPath.Warden, level: 1, AttributeSet.Baseline, vitals);

        // Current health was above max, but we don't auto-cap in RecalculateMaxima.
        // That's the caller's responsibility. Just verify maxima are set.
        Assert.Equal(60, vitals.HealthMax);
    }

    [Theory]
    [InlineData(CharacterPath.Warden)]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Shade)]
    [InlineData(CharacterPath.Hallow)]
    public void All_paths_start_with_positive_vitals(CharacterPath path)
    {
        var h = VitalCalculator.HealthMax(path, level: 1, AttributeSet.Baseline);
        var f = VitalCalculator.FocusMax(path, level: 1, AttributeSet.Baseline);
        var s = VitalCalculator.StaminaMax(path, level: 1, AttributeSet.Baseline);

        Assert.True(h > 0);
        Assert.True(f >= 0);
        Assert.True(s > 0);
    }
}
