using Muwbta.Domain.Randomness;

namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// Shared machinery for the two effects that move a target's guard.
/// </summary>
/// <remarks>
/// <b>Two dials, because they are two different things.</b> <c>defenseRating</c> is added to the
/// number an attack roll has to beat, so it changes how *often* a blow lands; <c>mitigation</c> is
/// the extra share of each blow that does land which the bearer shrugs off, so it changes what one
/// *costs*. A braced stance wants the first, plate wants the second, and an ability can reasonably
/// want either without the other.
///
/// <c>mitigation</c> is authored in whole percentage points — <c>"8"</c> is eight points — because
/// a builder typing <c>0.08</c> into a box that also takes <c>defenseRating</c> would be one
/// mistyped decimal away from a permanent 8x. It replaces <c>armorFlat</c>, which was subtracted
/// per blow and so meant something different at every level.
///
/// Distinct from <c>debuff.weaken</c>, which moves damage multipliers: weakening a target reduces
/// what it *deals*, and this changes what it *takes*.
/// </remarks>
public abstract class GuardEffect : IBuffEffect
{
    public abstract string EffectKey { get; }

    public abstract bool IsHarmful { get; }

    /// <summary>+1 for an effect that hardens, -1 for one that strips.</summary>
    protected abstract int Sign { get; }

    /// <summary>What to call it when the author did not.</summary>
    protected abstract string DefaultName { get; }

    /// <summary>How long the stance holds when the ability does not say - twenty seconds.</summary>
    public const long DefaultDurationPulses = 80L;

    /// <summary>
    /// Both dials and the clock, read once for the effect and for the phrase.
    /// </summary>
    /// <remarks>
    /// The sign is not applied here: it belongs to which effect this is, and the description says
    /// the direction in words instead - "guards" against "strips".
    /// </remarks>
    private static (int DefenseRating, int Mitigation, long Duration) Dials(
        Dictionary<string, string> parameters)
    {
        var duration = parameters.TryGetValue("durationPulses", out var raw)
            && long.TryParse(raw, out var value)
            ? value
            : DefaultDurationPulses;

        return (Math.Abs(Read(parameters, "defenseRating")), Math.Abs(Read(parameters, "mitigation")), duration);
    }

    /// <summary>
    /// The two dials said separately, because they are two different things: one changes how often
    /// a blow lands and the other what it costs.
    /// </summary>
    public string Describe(
        Dictionary<string, string> parameters,
        TargetingType targeting,
        int casterLevel)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var (defenseRating, mitigation, duration) = Dials(parameters);
        var hardens = Sign > 0;
        var whom = AbilityAudience.Whom(targeting, IsHarmful);
        var whose = AbilityAudience.Whose(targeting, IsHarmful);

        var parts = new List<string>();

        if (defenseRating != 0)
        {
            parts.Add(hardens
                ? $"makes {whom} {defenseRating} harder to hit"
                : $"makes {whom} {defenseRating} easier to hit");
        }

        if (mitigation != 0)
        {
            parts.Add(hardens
                ? $"turns aside {mitigation}% of each blow"
                : $"lets through {mitigation}% more of each blow");
        }

        if (parts.Count == 0)
        {
            // Both dials at zero is an ability that costs its resource and changes nothing, which
            // no validator currently refuses. Saying so is cheaper than finding it in a fight.
            return $"leaves {whose} guard exactly as it was";
        }

        return $"{string.Join(" and ", parts)} for {AbilityAudience.Seconds(duration)}";
    }

    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        // Nothing lands at the moment of the cast. The whole of this effect is the state it leaves
        // behind, which CombatSystem reads when it next builds the defender.
    }

    public ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Authored as a positive amount either way - "how much guard" or "how much to strip" - and
        // the sign comes from which effect this is. Letting a builder type a negative into a
        // hardening effect is how you get a shield that makes you easier to hit.
        var defenseRating = Sign * Math.Abs(Read(parameters, "defenseRating"));
        var mitigation = Sign * Math.Abs(Read(parameters, "mitigation")) / 100m;

        var duration = Dials(parameters).Duration;

        var name = parameters.TryGetValue("name", out var authored) && !string.IsNullOrWhiteSpace(authored)
            ? authored
            : DefaultName;

        return new ActiveEffect
        {
            EffectKey = EffectKey,
            Name = name,
            SourceEntityId = EffectSource.Of(caster),
            DefenseRatingDelta = defenseRating,
            MitigationDelta = mitigation,
            ExpiresAtPulse = currentPulse + duration,

            // Never stacks. Two castings of a guard would pile into an unhittable target, which is
            // the argument that already keeps control effects to one stack.
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };
    }

    private static int Read(Dictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) ? value : 0;
}

/// <summary>
/// Hardens the bearer: harder to hit, and blows that land cost less.
/// </summary>
/// <remarks>
/// <b>Separate from <see cref="ExposeEffect"/> rather than one executor taking a signed number.</b>
/// That was the first shape, and the validator refused the first ability written with it: Last
/// Stand pairs this with a maximum-health buff, and an executor that had to declare itself harmful
/// to cover the stripping case made the whole ability mixed-direction. The split is also the
/// convention already in the codebase — <c>buff.damage-up</c> and <c>debuff.weaken</c> are two
/// executors over the same two multipliers, for exactly this reason.
/// </remarks>
public sealed class DefenseEffect : GuardEffect
{
    public override string EffectKey => "buff.defense";

    public override bool IsHarmful => false;

    protected override int Sign => 1;

    protected override string DefaultName => "guarded";
}

/// <summary>
/// Strips a target's guard: easier to hit, and blows land harder.
/// </summary>
/// <remarks>
/// Harmful, so it answers to the §4.11 gate like any other attack — stripping somebody's defence
/// in a peaceful room is not a thing that should be castable. Authored with positive amounts
/// meaning "how much to take away".
/// </remarks>
public sealed class ExposeEffect : GuardEffect
{
    public override string EffectKey => "debuff.expose";

    public override bool IsHarmful => true;

    protected override int Sign => -1;

    protected override string DefaultName => "exposed";
}
