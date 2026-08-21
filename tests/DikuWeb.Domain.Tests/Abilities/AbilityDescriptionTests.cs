using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Tests.Abilities;

/// <summary>
/// An ability says what it does, worked out from its own dials.
/// </summary>
/// <remarks>
/// <para>
/// <c>abilities</c> used to print a name and a cost and stop there, so the only way to find out
/// what Mend restored was to cast it at something and watch. <see cref="Ability.Description"/>
/// existed the whole time and reached nobody but the builder — and it is authored prose, free to
/// go on describing a heal that was halved last week.
/// </para>
/// <para>
/// <b>So the line is derived, and these are the tests that keep it honest.</b> The value of
/// deriving it is that it cannot disagree with the game; the risk is that it disagrees anyway,
/// because the phrase was written from the ability's parameters rather than from the code that
/// reads them. Most of what is below is aimed at that: where an executor clamps, floors, or drops
/// a value, the description must report what happens rather than what was typed.
/// </para>
/// </remarks>
public sealed class AbilityDescriptionTests
{
    private static readonly EffectRegistry Effects = new();

    private static Dictionary<string, string> Params(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static Ability Ability(
        TargetingType targeting,
        params AbilityEffectSpec[] effects) => new()
        {
            Key = "adept.test",
            Path = CharacterPath.Adept,
            UnlockLevel = 1,
            Name = "Test",
            Description = "Authored flavour that nothing reads.",
            CostType = CostType.Focus,
            CostValue = 10,
            CooldownPulses = 24,
            TargetingType = targeting,
            Effects = [.. effects],
        };

    // -----------------------------------------------------------------------
    // Every executor answers, which is the point of putting it on the interface
    // -----------------------------------------------------------------------

    /// <summary>
    /// The reason <c>Describe</c> is an interface member rather than a lookup table: a table can
    /// be missing a row and nothing notices until a player types <c>abilities</c>.
    /// </summary>
    [Fact]
    public void Every_registered_effect_describes_itself()
    {
        foreach (var key in EffectKeys())
        {
            var effect = Effects.Get(key)!;

            // Empty parameters on purpose: an executor falls back to its own defaults, and a
            // description that only works for authored content is one that breaks on the case
            // where the numbers are hardest for a reader to guess.
            var phrase = effect.Describe([], TargetingType.SingleTarget, 1);

            Assert.False(string.IsNullOrWhiteSpace(phrase), $"{key} describes itself as nothing");
            Assert.DoesNotContain(".", phrase, StringComparison.Ordinal);
            Assert.Equal(phrase.Trim(), phrase);
        }
    }

    /// <summary>Lower case, so several can be joined onto one line after an em dash.</summary>
    [Fact]
    public void Every_phrase_starts_lower_case()
    {
        foreach (var key in EffectKeys())
        {
            var phrase = Effects.Get(key)!.Describe([], TargetingType.SingleTarget, 1);
            Assert.False(char.IsUpper(phrase[0]), $"{key} starts with a capital: {phrase}");
        }
    }

    private static IEnumerable<string> EffectKeys() =>
        AbilityCatalogue.AllEffects.Select(x => x.Effect.Key).Distinct(StringComparer.Ordinal);

    // -----------------------------------------------------------------------
    // The number shown is the number rolled
    // -----------------------------------------------------------------------

    /// <summary>The heal this whole change was asked for: "mend — heals your target 20 hp".</summary>
    [Fact]
    public void A_heal_says_what_it_restores()
    {
        var ability = Ability(
            TargetingType.SingleTarget,
            new AbilityEffectSpec("heal.restore", Params(("baseHeal", "20"))));

        Assert.Equal(
            "restores 18-22 health to your target",
            AbilityDescriber.Describe(ability, Effects, 1));
    }

    /// <summary>
    /// A heal small enough to have no variance is one number, not a range from a number to itself.
    /// </summary>
    [Fact]
    public void A_heal_too_small_to_vary_is_a_single_number()
    {
        var ability = Ability(
            TargetingType.Self,
            new AbilityEffectSpec("heal.restore", Params(("baseHeal", "5"))));

        Assert.Equal("restores 5 health to you", AbilityDescriber.Describe(ability, Effects, 1));
    }

    /// <summary>
    /// <b>The drift guard.</b> Measured against what <c>Apply</c> actually rolls rather than
    /// against a second copy of the arithmetic — a test that recomputed the range would agree with
    /// itself while the screen disagreed with the game, which is the failure it exists to catch.
    /// </summary>
    [Theory]
    [InlineData("heal.restore", "baseHeal", "40")]
    [InlineData("heal.restore", "baseHeal", "7")]
    [InlineData("damage.physical", "scalingFactor", "2.2")]
    [InlineData("damage.physical", "scalingFactor", "1.0")]
    public void Every_roll_lands_inside_the_range_the_description_promises(
        string effectKey, string dial, string value)
    {
        var parameters = Params((dial, value), ("minDamage", "1"));
        var effect = Effects.Get(effectKey)!;
        var (low, high) = RangeIn(effect.Describe(parameters, TargetingType.SingleTarget, 1));

        var random = new SeededRandomSource(7);
        var lowestSeen = int.MaxValue;
        var highestSeen = int.MinValue;

        // A body with room to take the worst roll and room to take the best, so neither end is
        // clipped by the health floor or by the maximum-health ceiling.
        for (var i = 0; i < 400; i++)
        {
            var subject = Subject();
            effect.Apply(subject, subject, parameters, random);

            var moved = Math.Abs(subject.Vitals.Health - StartingHealth);
            lowestSeen = Math.Min(lowestSeen, moved);
            highestSeen = Math.Max(highestSeen, moved);
        }

        // Both ends reachable, and neither reached past: a range merely wide enough to be true
        // would pass the first assertion and fail these.
        Assert.Equal(low, lowestSeen);
        Assert.Equal(high, highestSeen);
    }

    private const int StartingHealth = 5000;

    private static Character Subject() => new()
    {
        AccountId = Guid.NewGuid(),
        Name = "Subject",
        Path = CharacterPath.Adept,
        Attributes = new AttributeSet(),
        Vitals = new Vitals { Health = StartingHealth, HealthMax = 10_000 },
        RoomKey = RoomKey.Parse("test.zone.west"),
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>The "18-22" or the "5" out of a phrase, as a pair.</summary>
    private static (int Low, int High) RangeIn(string phrase)
    {
        var token = phrase.Split(' ').First(t => t.Length > 0 && char.IsDigit(t[0]));
        var parts = token.Split('-');

        return parts.Length == 2
            ? (int.Parse(parts[0]), int.Parse(parts[1]))
            : (int.Parse(parts[0]), int.Parse(parts[0]));
    }

    // -----------------------------------------------------------------------
    // What happens, not what was authored
    // -----------------------------------------------------------------------

    /// <summary>
    /// A stun authored past its ceiling is described at the ceiling. <c>AbilityValidator</c> warns
    /// about the same fact at the point of authoring; this is the half a player sees.
    /// </summary>
    [Fact]
    public void A_clamped_stun_is_described_at_the_clamp()
    {
        var parameters = Params(("durationPulses", "400"));

        Assert.Equal(
            "stops your target acting for 6s",
            Effects.Get("control.stun")!.Describe(parameters, TargetingType.SingleTarget, 1));

        Assert.Equal(StunEffect.MaxDurationPulses, StunEffect.DurationOf(parameters));
    }

    [Fact]
    public void A_clamped_root_is_described_at_the_clamp()
    {
        var parameters = Params(("durationPulses", "4000"));

        Assert.Equal(
            "stops your target fleeing for 10s",
            Effects.Get("control.root")!.Describe(parameters, TargetingType.SingleTarget, 1));

        Assert.Equal(RootEffect.MaxDurationPulses, RootEffect.DurationOf(parameters));
    }

    /// <summary>The described duration is the one the effect is built with, whatever was typed.</summary>
    [Theory]
    [InlineData("control.stun", "2")]
    [InlineData("control.stun", "400")]
    [InlineData("control.root", "12")]
    [InlineData("control.root", "400")]
    public void A_control_effect_is_described_with_the_duration_it_is_built_with(
        string key, string authored)
    {
        var parameters = Params(("durationPulses", authored));
        var effect = (IBuffEffect)Effects.Get(key)!;
        var built = effect.CreateActiveEffect(Subject(), Subject(), parameters, currentPulse: 0);

        Assert.Contains(
            AbilityAudience.Seconds(built.ExpiresAtPulse),
            effect.Describe(parameters, TargetingType.SingleTarget, 1),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A wound ticks one fewer time than its two numbers read as.</b> The tick loop skips an
    /// effect whose expiry has arrived, and the first tick lands a whole interval after the cast,
    /// so 72 pulses ticking every 12 lands five times rather than six. The total shown is the total
    /// dealt.
    /// </summary>
    [Fact]
    public void A_wound_counts_the_ticks_that_actually_land()
    {
        Assert.Equal(5, DamageOverTimeEffect.TickCount(durationPulses: 72, tickIntervalPulses: 12));
        Assert.Equal(5, DamageOverTimeEffect.TickCount(durationPulses: 48, tickIntervalPulses: 8));

        var phrase = Effects.Get("damage.overtime")!.Describe(
            Params(("tickDamage", "9"), ("tickIntervalPulses", "12"), ("durationPulses", "72")),
            TargetingType.SingleTarget, 1);

        Assert.Equal("deals 9 damage to your target every 3s for 18s (45 in all)", phrase);
    }

    /// <summary>A wound that expires before its first tick says so rather than promising damage.</summary>
    [Fact]
    public void A_wound_that_never_ticks_says_so()
    {
        var phrase = Effects.Get("damage.overtime")!.Describe(
            Params(("tickDamage", "9"), ("tickIntervalPulses", "40"), ("durationPulses", "20")),
            TargetingType.SingleTarget, 1);

        Assert.Contains("expires before it ticks", phrase, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Direction, which is the thing that has actually gone wrong before
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every weaken in the game was once authored as <c>incomingMultiplier</c> below 1.0, which
    /// made its target harder to kill. The phrase names the direction, so that shape of mistake is
    /// visible without reading code.
    /// </summary>
    [Fact]
    public void A_weaken_says_which_way_it_points()
    {
        Assert.Equal(
            "cuts your target's damage by 25% for 20s",
            Effects.Get("debuff.weaken")!.Describe(
                Params(("outgoingMultiplier", "0.75"), ("durationPulses", "80")),
                TargetingType.SingleTarget, 1));

        Assert.Equal(
            "raises the damage your target takes by 30% for 20s",
            Effects.Get("debuff.weaken")!.Describe(
                Params(("incomingMultiplier", "1.3"), ("durationPulses", "80")),
                TargetingType.SingleTarget, 1));
    }

    /// <summary>
    /// The backwards case reads backwards, which is the whole point of printing it: a debuff whose
    /// line says it protects its target needs no code review to spot.
    /// </summary>
    [Fact]
    public void A_debuff_written_the_wrong_way_round_reads_as_a_gift()
    {
        Assert.Equal(
            "cuts the damage your target takes by 30% for 20s",
            Effects.Get("debuff.weaken")!.Describe(
                Params(("incomingMultiplier", "0.7"), ("durationPulses", "80")),
                TargetingType.SingleTarget, 1));
    }

    /// <summary>Both dials at once are both said, because they are two different things.</summary>
    [Fact]
    public void A_debuff_that_does_both_says_both()
    {
        Assert.Equal(
            "cuts your target's damage by 20% and raises the damage your target takes by 20% for 10s",
            Effects.Get("debuff.weaken")!.Describe(
                Params(("outgoingMultiplier", "0.8"), ("incomingMultiplier", "1.2"), ("durationPulses", "40")),
                TargetingType.SingleTarget, 1));
    }

    /// <summary>A guard's two dials are separate clauses because they buy separate things.</summary>
    [Fact]
    public void A_guard_separates_being_missed_from_being_spared()
    {
        Assert.Equal(
            "makes you 8 harder to hit and turns aside 8% of each blow for 20s",
            Effects.Get("buff.defense")!.Describe(
                Params(("defenseRating", "8"), ("mitigation", "8"), ("durationPulses", "80")),
                TargetingType.Self, 1));

        Assert.Equal(
            "makes your target 5 easier to hit and lets through 5% more of each blow for 6s",
            Effects.Get("debuff.expose")!.Describe(
                Params(("defenseRating", "5"), ("mitigation", "5"), ("durationPulses", "24")),
                TargetingType.SingleTarget, 1));
    }

    /// <summary>One dial set alone is one clause, not a clause about zero.</summary>
    [Fact]
    public void A_guard_with_one_dial_says_one_thing()
    {
        Assert.Equal(
            "makes you 6 harder to hit for 20s",
            Effects.Get("buff.defense")!.Describe(
                Params(("defenseRating", "6"), ("durationPulses", "80")),
                TargetingType.Self, 1));
    }

    /// <summary>
    /// A taunt is measured as a share of the target's health, which is the only unit that means the
    /// same thing against a rat and against a dragon — so that is the unit it is said in.
    /// </summary>
    [Fact]
    public void A_taunt_says_the_lead_in_the_units_it_is_measured_in()
    {
        Assert.Equal(
            "makes your target fight you, by a lead worth 25% of its health",
            Effects.Get("control.taunt")!.Describe([], TargetingType.SingleTarget, 1));
    }

    // -----------------------------------------------------------------------
    // Who it lands on
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(TargetingType.Self, "restores 18-22 health to you")]
    [InlineData(TargetingType.SingleTarget, "restores 18-22 health to your target")]
    [InlineData(TargetingType.Aoe, "restores 18-22 health to everyone with you")]
    public void The_phrase_names_who_it_reaches(TargetingType targeting, string expected)
    {
        var ability = Ability(
            targeting,
            new AbilityEffectSpec("heal.restore", Params(("baseHeal", "20"))));

        Assert.Equal(expected, AbilityDescriber.Describe(ability, Effects, 1));
    }

    /// <summary>
    /// An area effect gathers a different set depending on which way it points — every mob that may
    /// be fought, or the caster's party — so the two are not described with the same word.
    /// </summary>
    [Fact]
    public void An_area_effect_names_the_side_it_gathers()
    {
        var harmful = Ability(
            TargetingType.Aoe,
            new AbilityEffectSpec("damage.physical", Params(("scalingFactor", "1.3"), ("minDamage", "6"))));

        var helpful = Ability(
            TargetingType.Aoe,
            new AbilityEffectSpec("heal.restore", Params(("baseHeal", "60"))));

        Assert.Contains("every enemy here", AbilityDescriber.Describe(harmful, Effects, 1), StringComparison.Ordinal);
        Assert.Contains("everyone with you", AbilityDescriber.Describe(helpful, Effects, 1), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Abilities that do several things
    // -----------------------------------------------------------------------

    [Fact]
    public void An_ability_with_several_effects_says_all_of_them()
    {
        var ability = Ability(
            TargetingType.SingleTarget,
            new AbilityEffectSpec("damage.physical", Params(("scalingFactor", "1.5"), ("minDamage", "10"))),
            new AbilityEffectSpec("control.stun", Params(("durationPulses", "16"))));

        Assert.Equal(
            "deals 12-18 damage to your target, and stops your target acting for 4s",
            AbilityDescriber.Describe(ability, Effects, 1));
    }

    /// <summary>
    /// An unregistered effect key is the most expensive mistake an ability can carry: the cast
    /// succeeds, the cost is spent, the cooldown starts, and nothing happens. Saying so in the
    /// listing puts it in front of somebody who can report it.
    /// </summary>
    [Fact]
    public void An_effect_with_no_executor_is_named_rather_than_skipped()
    {
        var ability = Ability(
            TargetingType.SingleTarget,
            new AbilityEffectSpec("damage.chaos", Params(("scalingFactor", "9"))));

        Assert.Contains(
            "'damage.chaos' is not a known effect",
            AbilityDescriber.Describe(ability, Effects, 1),
            StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // The shipped set
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every starter ability describes itself as doing something. None of them should reach the
    /// phrasings that exist for content which went wrong — those are the ones that say an ability
    /// spends its cost and changes nothing.
    /// </summary>
    [Fact]
    public void Every_starter_ability_says_something_it_does()
    {
        foreach (var ability in AbilityCatalogue.AsAbilities)
        {
            var phrase = AbilityDescriber.Describe(ability, Effects, 1);

            Assert.False(string.IsNullOrWhiteSpace(phrase), $"{ability.Key} says nothing");

            foreach (var complaint in new[]
                {
                    "not a known effect",
                    "does nothing",
                    "expires before it ticks",
                    "changes nothing",
                    "exactly as it was",
                    "where it is",
                })
            {
                Assert.DoesNotContain(complaint, phrase, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// And every one of them carries a number, since a description with no quantity in it is the
    /// flavour text this replaced.
    /// </summary>
    [Fact]
    public void Every_starter_ability_says_a_number()
    {
        foreach (var ability in AbilityCatalogue.AsAbilities)
        {
            Assert.Contains(AbilityDescriber.Describe(ability, Effects, 1), char.IsDigit);
        }
    }
}
