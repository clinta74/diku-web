using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Abilities;

/// <summary>
/// <see cref="AbilityValidator"/>, which is what replaced the compile-time guardrails when
/// abilities became rows a builder can edit.
/// </summary>
/// <remarks>
/// Two halves, and both matter. The shipped catalogue must pass clean — a validator that complains
/// about the game's own content is one whose output nobody reads within a week. And each check must
/// actually catch the thing it was written for, driven by a deliberately broken ability rather than
/// asserted in the abstract, because a check that silently matches nothing is exactly the failure
/// this class exists to prevent.
/// </remarks>
public sealed class AbilityValidatorTests
{
    private static readonly EffectRegistry Effects = new();

    private static Ability Valid(
        string key = "warden.test",
        CharacterPath path = CharacterPath.Warden,
        int unlockLevel = 5,
        int cost = 10,
        string effectKey = "damage.physical",
        Dictionary<string, string>? effectParams = null) => new()
    {
        Key = key,
        Path = path,
        UnlockLevel = unlockLevel,
        Name = "Test Ability",
        Description = "For testing.",
        CostType = CostType.Stamina,
        CostValue = cost,
        CooldownPulses = 24,
        CastTimePulses = null,
        TargetingType = TargetingType.SingleTarget,
        EffectKey = effectKey,
        EffectParams = effectParams
            ?? new(StringComparer.Ordinal) { ["scalingFactor"] = "1.2", ["minDamage"] = "3" },
    };

    private static IReadOnlyList<AbilityProblem> Errors(Ability ability) =>
        [.. AbilityValidator.ValidateOne(ability, Effects)
            .Where(p => p.Severity == AbilityProblemSeverity.Error)];

    // -----------------------------------------------------------------------
    // The shipped set
    // -----------------------------------------------------------------------

    [Fact]
    public void The_catalogue_the_game_ships_has_no_errors()
    {
        var problems = AbilityValidator
            .ValidateSet(AbilityCatalogue.AsAbilities, Effects)
            .Where(p => p.Severity == AbilityProblemSeverity.Error)
            .ToList();

        Assert.True(
            problems.Count == 0,
            "The starter set must validate:\n" + string.Join("\n", problems.Select(p => $"  {p.Key}: {p.Message}")));
    }

    [Fact]
    public void The_catalogue_the_game_ships_has_no_warnings_either()
    {
        // Held to the higher bar deliberately. The warnings are about progression shape - a Path
        // with nothing at level 1, a four-level gap - and the shipped set is the one thing that
        // has been designed against those rules rather than merely permitted by them.
        var problems = AbilityValidator
            .ValidateSet(AbilityCatalogue.AsAbilities, Effects)
            .ToList();

        Assert.True(
            problems.Count == 0,
            "The starter set should be exemplary:\n" + string.Join("\n", problems.Select(p => $"  {p.Key}: {p.Message}")));
    }

    // -----------------------------------------------------------------------
    // The silent failures, each driven by a broken ability
    // -----------------------------------------------------------------------

    [Fact]
    public void An_unknown_effect_key_is_refused()
    {
        // Costs its resource, starts its cooldown, does nothing, reports nothing.
        Assert.Contains(
            Errors(Valid(effectKey: "damage.nonexistent")),
            p => p.Message.Contains("No effect executor", StringComparison.Ordinal));
    }

    [Fact]
    public void An_effect_missing_the_parameter_it_reads_is_refused()
    {
        // Effects skip parameters they do not recognise, so "magnitude" instead of "scalingFactor"
        // is an ability that runs and does nothing at all.
        var ability = Valid(effectParams: new(StringComparer.Ordinal) { ["magnitude"] = "1.2" });

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("scalingFactor", StringComparison.Ordinal));
    }

    [Fact]
    public void A_weaken_written_the_wrong_way_round_is_refused()
    {
        // The bug this class is shaped around, and the one that shipped: incomingMultiplier scales
        // what the target *takes*, so below 1.0 it makes them harder to kill. Every debuff in the
        // game was written this way and nothing failed.
        var ability = Valid(
            effectKey: "debuff.weaken",
            effectParams: new(StringComparer.Ordinal)
            {
                ["incomingMultiplier"] = "0.75",
                ["durationPulses"] = "80",
            });

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("protects the target", StringComparison.Ordinal));
    }

    [Fact]
    public void A_weaken_that_moves_no_multiplier_at_all_is_refused()
    {
        var ability = Valid(
            effectKey: "debuff.weaken",
            effectParams: new(StringComparer.Ordinal) { ["durationPulses"] = "80" });

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("does nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_damage_buff_below_one_is_refused()
    {
        var ability = Valid(
            effectKey: "buff.damage-up",
            effectParams: new(StringComparer.Ordinal)
            {
                ["outgoingMultiplier"] = "0.8",
                ["durationPulses"] = "80",
            });

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("makes the caster weaker", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wound_that_expires_before_it_ticks_is_refused()
    {
        var ability = Valid(
            effectKey: "damage.overtime",
            effectParams: new(StringComparer.Ordinal)
            {
                ["tickDamage"] = "6",
                ["tickIntervalPulses"] = "16",
                ["durationPulses"] = "8",
            });

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("expires before it ever ticks", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stacking_control_effect_is_refused()
    {
        var ability = Valid(
            effectKey: "control.stun",
            effectParams: new(StringComparer.Ordinal)
            {
                ["durationPulses"] = "16",
                ["maxStacks"] = "3",
            });

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("permanent lock", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stun_past_the_executors_ceiling_is_a_warning_not_a_refusal()
    {
        // The executor clamps it, so the ability works — the number in the editor has simply
        // stopped describing the game, which is worth saying and not worth blocking a save over.
        var ability = Valid(
            effectKey: "control.stun",
            effectParams: new(StringComparer.Ordinal)
            {
                ["durationPulses"] = (StunEffect.MaxDurationPulses + 10).ToString(),
            });

        var problems = AbilityValidator.ValidateOne(ability, Effects);

        Assert.DoesNotContain(problems, p => p.Severity == AbilityProblemSeverity.Error);
        Assert.Contains(problems, p => p.Message.Contains("clamps it", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ability_that_costs_nothing_is_refused() =>
        Assert.Contains(Errors(Valid(cost: 0)), p => p.Message.Contains("costs nothing", StringComparison.Ordinal));

    [Fact]
    public void An_unlock_level_of_zero_is_refused() =>
        // What the scaffolded migration would have produced: known by everyone from level 1.
        Assert.Contains(
            Errors(Valid(unlockLevel: 0)),
            p => p.Message.Contains("Unlock level", StringComparison.Ordinal));

    [Fact]
    public void A_key_naming_the_wrong_path_is_refused() =>
        // AbilityLookup resolves the full key, so this is a name that reaches another Path's
        // ability rather than merely an untidy string.
        Assert.Contains(
            Errors(Valid(key: "shade.kick", path: CharacterPath.Warden)),
            p => p.Message.Contains("must start with 'warden.'", StringComparison.Ordinal));

    // -----------------------------------------------------------------------
    // Set-wide shape
    // -----------------------------------------------------------------------

    [Fact]
    public void A_path_with_nothing_at_level_one_is_reported()
    {
        var problems = AbilityValidator.ValidateSet([Valid(unlockLevel: 3)], Effects);

        Assert.Contains(problems, p => p.Message.Contains("nothing castable at level 1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gap_longer_than_four_levels_is_reported()
    {
        IReadOnlyCollection<Ability> set =
        [
            Valid(key: "warden.one", unlockLevel: 1),
            Valid(key: "warden.two", unlockLevel: 9),
        ];

        Assert.Contains(
            AbilityValidator.ValidateSet(set, Effects),
            p => p.Message.Contains("from level 1 to 9 with nothing new", StringComparison.Ordinal));
    }

    [Fact]
    public void Set_validation_still_reports_each_ability_own_problems()
    {
        // The set pass must not replace the per-ability pass — an import that lands a broken row
        // is exactly when nobody is running the save-time check.
        Assert.Contains(
            AbilityValidator.ValidateSet([Valid(effectKey: "damage.nonexistent")], Effects),
            p => p.Severity == AbilityProblemSeverity.Error);
    }
}
