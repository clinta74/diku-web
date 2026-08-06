using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Characters;

public sealed class RegenCalculatorTests
{
    [Fact]
    public void Calculate_sleep_baseline_attributes()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden); // Health 60, Focus 20, Stamina 100
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Sleep,
            vitals,
            vitalityModifier: 0);

        // 15% of max per vital
        Assert.Equal(9, health);   // floor(60 * 0.15)
        Assert.Equal(3, focus);    // floor(20 * 0.15)
        Assert.Equal(15, stamina); // floor(100 * 0.15)
    }

    [Fact]
    public void Calculate_rest_baseline_attributes()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Rest,
            vitals,
            vitalityModifier: 0);

        // 8% of max per vital
        Assert.Equal(4, health);   // floor(60 * 0.08)
        Assert.Equal(1, focus);    // floor(20 * 0.08) = 1
        Assert.Equal(8, stamina);  // floor(100 * 0.08)
    }

    [Fact]
    public void Calculate_stand_baseline_attributes()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Stand,
            vitals,
            vitalityModifier: 0);

        // 2% of max per vital, minimum 1
        Assert.Equal(1, health);   // floor(60 * 0.02) = 1
        Assert.Equal(1, focus);    // floor(40 * 0.02) = 1 (minimum)
        Assert.Equal(2, stamina);  // floor(100 * 0.02) = 2
    }

    [Fact]
    public void Calculate_positive_vitality_modifier_increases_regen()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var baseRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0);
        var boostedRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 3);

        // +3 modifier adds 3% to base 8% = 11% total
        var expectedHealth = (int)Math.Floor(60 * 0.11);
        Assert.Equal(expectedHealth, boostedRegen.health);
        Assert.True(boostedRegen.health > baseRegen.health);
    }

    [Fact]
    public void Calculate_negative_vitality_modifier_decreases_regen()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var baseRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0);
        var penalizedRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: -2);

        // -2 modifier reduces base 8% by 2% = 6% total
        var expectedHealth = (int)Math.Floor(60 * 0.06);
        Assert.Equal(expectedHealth, penalizedRegen.health);
        Assert.True(penalizedRegen.health < baseRegen.health);
    }

    [Fact]
    public void Calculate_minimum_is_one_per_vital()
    {
        var vitals = new Vitals
        {
            Health = 1,
            HealthMax = 1,
            Focus = 1,
            FocusMax = 1,
            Stamina = 1,
            StaminaMax = 1,
        };

        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Stand,
            vitals,
            vitalityModifier: 0);

        // Even with 1 HP, regen is at least 1
        Assert.Equal(1, health);
        Assert.Equal(1, focus);
        Assert.Equal(1, stamina);
    }

    [Fact]
    public void Calculate_high_modifier_can_enable_regen_at_stand()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        // With +10 modifier, Stand is 2% + 10% = 12%
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Stand,
            vitals,
            vitalityModifier: 10);

        var expectedHealth = (int)Math.Floor(60 * 0.12);
        Assert.Equal(expectedHealth, health);
        Assert.True(health > 2); // Significantly more than default 1
    }

    [Fact]
    public void ApplyRegen_increases_vitals_to_max()
    {
        var vitals = new Vitals
        {
            Health = 30,
            HealthMax = 60,
            Focus = 10,
            FocusMax = 20,
            Stamina = 40,
            StaminaMax = 100,
        };

        var changed = RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0);

        Assert.True(changed);
        Assert.Equal(39, vitals.Health);    // 30 + 9 (15% of 60)
        Assert.Equal(13, vitals.Focus);     // 10 + 3 (15% of 20)
        Assert.Equal(55, vitals.Stamina);   // 40 + 15 (15% of 100)
    }

    [Fact]
    public void ApplyRegen_caps_to_max()
    {
        var vitals = new Vitals
        {
            Health = 55,
            HealthMax = 60,
            Focus = 18,
            FocusMax = 20,
            Stamina = 90,
            StaminaMax = 100,
        };

        RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0);

        Assert.Equal(60, vitals.Health);   // 55 + 9 = 64, capped to 60
        Assert.Equal(20, vitals.Focus);    // 18 + 3 = 21, capped to 20
        Assert.Equal(100, vitals.Stamina); // 90 + 15 = 105, capped to 100
    }

    [Fact]
    public void ApplyRegen_returns_false_when_already_at_max()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        // Don't change anything; it's at max
        var changed = RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0);

        Assert.False(changed);
    }

    [Fact]
    public void ApplyRegen_returns_true_even_if_one_vital_changes()
    {
        var vitals = new Vitals
        {
            Health = 59,       // Not at max
            HealthMax = 60,
            Focus = 40,        // At max
            FocusMax = 40,
            Stamina = 100,     // At max
            StaminaMax = 100,
        };

        var changed = RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0);

        Assert.True(changed);
        Assert.Equal(60, vitals.Health);
    }

    [Fact]
    public void All_paths_regen_at_sleep()
    {
        var paths = new[] { CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Shade, CharacterPath.Channeler };

        foreach (var path in paths)
        {
            var vitals = Vitals.StartingFor(path);
            var (health, focus, stamina) = RegenCalculator.Calculate(
                CharacterRestState.Sleep,
                vitals,
                vitalityModifier: 0);

            Assert.True(health > 0);
            Assert.True(focus > 0);
            Assert.True(stamina > 0);
        }
    }

    [Fact]
    public void Sleep_regens_more_than_rest()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var sleep = RegenCalculator.Calculate(CharacterRestState.Sleep, vitals, vitalityModifier: 0);
        var rest = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0);

        Assert.True(sleep.health > rest.health);
        Assert.True(sleep.focus > rest.focus);
        Assert.True(sleep.stamina > rest.stamina);
    }

    [Fact]
    public void Rest_regens_more_than_stand()
    {
        // Use Adept which has higher Focus max (50 vs 20), ensuring Rest > Stand for all vitals
        var vitals = Vitals.StartingFor(CharacterPath.Adept);
        var rest = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0);
        var stand = RegenCalculator.Calculate(CharacterRestState.Stand, vitals, vitalityModifier: 0);

        Assert.True(rest.health > stand.health);
        Assert.True(rest.focus > stand.focus);
        Assert.True(rest.stamina > stand.stamina);
    }
}
