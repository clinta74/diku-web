using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Abilities;

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
        Dictionary<string, string>? effectParams = null,
        List<AbilityEffectSpec>? effects = null) => new()
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
        Effects = effects ??
        [
            new(effectKey, effectParams
                ?? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["scalingFactor"] = "1.2",
                    ["minDamage"] = "3",
                }),
        ],
    };

    private static IReadOnlyList<AbilityProblem> Errors(Ability ability) =>
        [.. AbilityValidator.ValidateOne(ability, Effects)
            .Where(p => p.Severity == AbilityProblemSeverity.Error)];

    // -----------------------------------------------------------------------
    // The examples a fresh database is seeded with
    // -----------------------------------------------------------------------

    /// <summary>
    /// The four examples are each individually sound, which is all they claim to be.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>ValidateSet</c>.</b> The set-level rules ask whether a Path keeps unlocking to
    /// level 20 and whether the gaps are playable, and four level-1 abilities fail all of them by
    /// design — they are a floor for an empty database, not a progression. Those questions are
    /// asked of the shipped set, in <c>AbilityContentTests</c>, which is where the set lives.
    /// </remarks>
    [Fact]
    public void The_examples_a_fresh_database_is_seeded_with_are_sound()
    {
        var problems = AbilityCatalogue.AsAbilities
            .SelectMany(a => AbilityValidator.ValidateOne(a, Effects))
            .ToList();

        Assert.True(
            problems.Count == 0,
            "The examples must validate:\n" + string.Join("\n", problems.Select(p => $"  {p.Key}: {p.Message}")));
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
            Errors(Valid(key: "temper.kick", path: CharacterPath.Warden)),
            p => p.Message.Contains("must start with 'warden.'", StringComparison.Ordinal));

    // -----------------------------------------------------------------------
    // A list of effects
    // -----------------------------------------------------------------------

    [Fact]
    public void An_ability_with_several_effects_is_fine()
    {
        // The case the whole change exists for: Last Stand raising maximum health *and* hardening
        // defence, rather than being written as a heal because one slot was all there was.
        var ability = Valid(effects:
        [
            new("buff.damage-up", new(StringComparer.Ordinal)
            {
                ["outgoingMultiplier"] = "1.25",
                ["durationPulses"] = "80",
            }),
            new("heal.restore", new(StringComparer.Ordinal) { ["baseHeal"] = "40" }),
        ]);

        Assert.Empty(Errors(ability));
    }

    [Fact]
    public void An_ability_with_no_effects_is_refused()
    {
        Assert.Contains(
            Errors(Valid(effects: [])),
            p => p.Message.Contains("no effects", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_effect_twice_is_refused()
    {
        // Both would run and the second would refresh the first rather than stack with it, so the
        // ability is quietly weaker than the list makes it look.
        var ability = Valid(effects:
        [
            new("heal.restore", new(StringComparer.Ordinal) { ["baseHeal"] = "20" }),
            new("heal.restore", new(StringComparer.Ordinal) { ["baseHeal"] = "20" }),
        ]);

        Assert.Contains(Errors(ability), p => p.Message.Contains("listed twice", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ability_that_both_harms_and_helps_is_refused()
    {
        // There is one set of targets. A list mixing the two would either burn the people it meant
        // to mend or mend the people it meant to burn, and the cast path cannot choose - it treats
        // an ability as harmful if any part of it is.
        var ability = Valid(effects:
        [
            new("damage.physical", new(StringComparer.Ordinal) { ["scalingFactor"] = "1.2" }),
            new("heal.restore", new(StringComparer.Ordinal) { ["baseHeal"] = "20" }),
        ]);

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("both harmful and helpful", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_entry_in_an_otherwise_good_list_is_still_caught()
    {
        // The case worth catching: the ability half-works, which reads as a balance problem rather
        // than a mistake.
        var ability = Valid(effects:
        [
            new("heal.restore", new(StringComparer.Ordinal) { ["baseHeal"] = "20" }),
            new("heal.nonexistent", new(StringComparer.Ordinal)),
        ]);

        Assert.Contains(
            Errors(ability),
            p => p.Message.Contains("No effect executor", StringComparison.Ordinal));
    }

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

    // -----------------------------------------------------------------------
    // Two ways to take hold of a whole room
    // -----------------------------------------------------------------------

    /// <summary>A room-wide control ability, for the checks below.</summary>
    private static Ability RoomControl(
        string key,
        string effectKey = "control.taunt",
        int? group = null,
        int unlockLevel = 5,
        CharacterPath path = CharacterPath.Warden,
        TargetingType targeting = TargetingType.Aoe) => new()
    {
        Key = key,
        Path = path,
        UnlockLevel = unlockLevel,
        Name = key,
        Description = "For testing.",
        CostType = CostType.Stamina,
        CostValue = 10,
        CooldownPulses = 24,
        CooldownGroup = group,
        CastTimePulses = null,
        TargetingType = targeting,
        Effects = [new(effectKey, new Dictionary<string, string>(StringComparer.Ordinal))],
    };

    private static IReadOnlyList<AbilityProblem> Compounding(params Ability[] set) =>
        [.. AbilityValidator.ValidateSet(set, Effects)
            .Where(p => p.Message.Contains("compound", StringComparison.Ordinal))];

    /// <summary>
    /// The reported bug, stated: two room taunts with nothing stopping them being used together.
    /// </summary>
    /// <remarks>
    /// <b>Found in play, not by the validator.</b> Thunderclap and Mass Provocation both taunt the
    /// whole room and neither was on a timer, so both could be fired in the same beat. They do not
    /// overwrite each other — <c>Combat.ForceTopHater</c> sets the caster to the highest hate
    /// <em>plus</em> the lead — so the leads add: 35% of the health bar and then another 50% on top
    /// of that.
    /// </remarks>
    [Fact]
    public void Two_room_wide_controls_of_a_kind_on_no_shared_timer_are_reported()
    {
        var problems = Compounding(
            RoomControl("warden.thunderclap"),
            RoomControl("warden.mass-provocation"));

        // Named on both, because either one is where a builder might go to fix it.
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Key == "warden.thunderclap");
        Assert.Contains(problems, p => p.Key == "warden.mass-provocation");
        Assert.All(problems, p => Assert.Equal(AbilityProblemSeverity.Warning, p.Severity));
    }

    /// <summary>And putting them on one silences it, which is the fix the message asks for.</summary>
    [Fact]
    public void A_shared_timer_settles_it()
    {
        Assert.Empty(Compounding(
            RoomControl("warden.thunderclap", group: 2),
            RoomControl("warden.mass-provocation", group: 2)));
    }

    /// <summary>
    /// Only the ones off a timer are named, so a deliberate third is not nagged about.
    /// </summary>
    [Fact]
    public void An_ability_already_on_a_timer_is_left_alone()
    {
        var problems = Compounding(
            RoomControl("warden.thunderclap", group: 2),
            RoomControl("warden.mass-provocation", group: 2),
            RoomControl("warden.bellow"));

        Assert.Equal("warden.bellow", Assert.Single(problems).Key);
    }

    /// <summary>
    /// Different kinds of control do not collide, because they do not do the same thing.
    /// </summary>
    [Fact]
    public void A_room_taunt_and_a_room_root_are_two_different_questions()
    {
        Assert.Empty(Compounding(
            RoomControl("adept.gravity-well", effectKey: "control.root"),
            RoomControl("adept.the-word", effectKey: "control.stun")));
    }

    /// <summary>
    /// Damage, heals and buffs are meant to layer, so the check stays out of their way.
    /// </summary>
    /// <remarks>
    /// A Hallow with three room heals is the Path working as intended. Widening this rule past
    /// control would turn it into noise on the one Path that has most to say.
    /// </remarks>
    [Fact]
    public void Room_wide_healing_is_not_the_same_problem()
    {
        Assert.Empty(Compounding(
            RoomControl("hallow.benediction", effectKey: "heal.restore", path: CharacterPath.Hallow),
            RoomControl("hallow.communion", effectKey: "heal.restore", path: CharacterPath.Hallow),
            RoomControl("hallow.mending-tide", effectKey: "heal.restore", path: CharacterPath.Hallow)));
    }

    /// <summary>
    /// One creature's worth of consequence is not a room's, so single-target control is untouched.
    /// </summary>
    /// <remarks>
    /// The Warden really does have two single-target taunts — Taunt and Reprisal — and they are
    /// fine: Reprisal is a damage ability that also holds threat, which is a different tool from a
    /// shout, and using both costs two beats on one mob.
    /// </remarks>
    [Fact]
    public void Single_target_control_is_not_this_check()
    {
        Assert.Empty(Compounding(
            RoomControl("warden.taunt", targeting: TargetingType.SingleTarget),
            RoomControl("warden.reprisal", targeting: TargetingType.SingleTarget)));
    }

    /// <summary>
    /// Two Paths are two spellbooks. A character only ever knows one, so they can never meet.
    /// </summary>
    [Fact]
    public void Two_paths_with_a_room_control_each_are_not_compounding()
    {
        Assert.Empty(Compounding(
            RoomControl("warden.thunderclap"),
            RoomControl("adept.the-word", path: CharacterPath.Adept)));
    }
}
