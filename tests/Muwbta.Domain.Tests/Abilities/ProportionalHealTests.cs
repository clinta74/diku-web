using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Randomness;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// A heal may be authored as a share of the target's maximum rather than as a flat amount.
/// </summary>
/// <remarks>
/// <b>For a heal whose intent is proportional.</b> Second Wind is worth <em>getting back on your
/// feet</em>, and that is 52 health at level 13 and 145 at level 50 — one authored number cannot be
/// both, which is why it had drifted to being worth 6% of an endgame Temper's bar.
/// </remarks>
public sealed class ProportionalHealTests
{
    private static Character Character(CharacterPath path, int level)
    {
        var attributes = AttributeSet.Baseline;
        var growth = PathGrowth.For(path);

        for (var i = 1; i < level; i++)
        {
            growth.ApplyTo(ref attributes);
        }

        var vitals = Vitals.StartingFor(path);
        VitalCalculator.RecalculateMaxima(path, level, attributes, vitals);

        return new Character
        {
            AccountId = Guid.Empty,
            Name = "Kaeda",
            Path = path,
            Level = level,
            Attributes = attributes,
            Vitals = vitals,
            RoomKey = Muwbta.Domain.Worlds.RoomKey.Create("t", "t", "t"),
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static Dictionary<string, string> Percent(string value) =>
        new(StringComparer.Ordinal) { ["healPercent"] = value };

    /// <summary>
    /// Half a bar is half a bar at every level, which is the whole point of authoring it this way.
    /// </summary>
    [Theory]
    [InlineData(13)]
    [InlineData(30)]
    [InlineData(50)]
    public void A_percentage_heal_tracks_the_health_pool(int level)
    {
        var character = Character(CharacterPath.Temper, level);
        var max = character.Vitals.HealthMax;

        character.Vitals.Health = 1;

        new HealEffect().Apply(character, character, Percent("50"), new SeededRandomSource(7));

        var restored = character.Vitals.Health - 1;

        // Within the executor's own ±10% variance.
        Assert.InRange(restored, max * 0.45 - 1, (max * 0.55) + 1);
    }

    /// <summary>
    /// The target's maximum decides it, not the caster's — a Hallow mending a Warden restores a
    /// share of what the Warden can hold.
    /// </summary>
    [Fact]
    public void The_targets_pool_decides_it()
    {
        var healer = Character(CharacterPath.Hallow, 50);
        var warden = Character(CharacterPath.Warden, 50);

        Assert.True(warden.Vitals.HealthMax > healer.Vitals.HealthMax);

        warden.Vitals.Health = 1;

        new HealEffect().Apply(healer, warden, Percent("50"), new SeededRandomSource(7));

        var restored = warden.Vitals.Health - 1;

        Assert.InRange(
            restored,
            warden.Vitals.HealthMax * 0.45 - 1,
            (warden.Vitals.HealthMax * 0.55) + 1);
    }

    /// <summary>A flat heal is untouched: every other heal in the game still authors one.</summary>
    [Fact]
    public void A_flat_heal_is_unchanged()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["baseHeal"] = "70" };

        Assert.Equal(70, HealEffect.Middle(parameters));
        Assert.Equal(70, HealEffect.Middle(parameters, targetHealthMax: 900));
    }

    /// <summary>A percentage wins over a flat amount when both are somehow authored.</summary>
    [Fact]
    public void A_percentage_wins_over_a_flat_amount()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseHeal"] = "20",
            ["healPercent"] = "50",
        };

        Assert.Equal(150, HealEffect.Middle(parameters, targetHealthMax: 300));
    }

    /// <summary>
    /// Absurd shares are clamped rather than honoured: a negative one would be a heal that wounds.
    /// </summary>
    [Theory]
    [InlineData("-50")]
    [InlineData("0")]
    public void A_share_at_or_below_zero_falls_back_to_the_flat_amount(string value)
    {
        Assert.Equal(HealEffect.DefaultBaseHeal, HealEffect.Middle(Percent(value), targetHealthMax: 300));
    }

    [Fact]
    public void A_share_over_a_whole_bar_is_capped_at_one()
    {
        Assert.Equal(300, HealEffect.Middle(Percent("400"), targetHealthMax: 300));
    }

    /// <summary>
    /// It says what it does. A proportional heal describes itself as a share, because the listing
    /// has no target to take a maximum from and the caster's own is right only when self-healing.
    /// </summary>
    [Fact]
    public void What_it_says_is_what_it_does()
    {
        var phrase = new HealEffect().Describe(Percent("50"), TargetingType.Self, casterLevel: 50);

        Assert.Contains("50", phrase, StringComparison.Ordinal);
        Assert.Contains("maximum health", phrase, StringComparison.Ordinal);
    }

    /// <summary>
    /// The validator accepts a heal authored either way, and still refuses one authored neither.
    /// </summary>
    [Fact]
    public void The_validator_takes_either_parameter()
    {
        var effects = new EffectRegistry();

        static Ability With(Dictionary<string, string> parameters) => new()
        {
            Key = "temper.second-wind",
            Path = CharacterPath.Temper,
            UnlockLevel = 13,
            Name = "Second Wind",
            Description = "An inner peace that brings healing.",
            CostType = CostType.Focus,
            CostValue = 18,
            CooldownPulses = 240,
            TargetingType = TargetingType.Self,
            Effects = [new AbilityEffectSpec("heal.restore", parameters)],
        };

        Assert.Empty(AbilityValidator.ValidateOne(With(Percent("50")), effects));

        Assert.Empty(AbilityValidator.ValidateOne(
            With(new Dictionary<string, string>(StringComparer.Ordinal) { ["baseHeal"] = "45" }),
            effects));

        Assert.NotEmpty(AbilityValidator.ValidateOne(
            With(new Dictionary<string, string>(StringComparer.Ordinal)),
            effects));
    }
}
