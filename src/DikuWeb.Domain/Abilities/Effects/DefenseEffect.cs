using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// Shared machinery for the two effects that move a target's guard.
/// </summary>
/// <remarks>
/// <b>Two dials, because they are two different things.</b> <c>defenseRating</c> is added to the
/// number an attack roll has to beat, so it changes how *often* a blow lands; <c>armorFlat</c> is
/// subtracted from each blow that does land, so it changes what one *costs*. A braced stance wants
/// the first, plate wants the second, and an ability can reasonably want either without the other.
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
        var armorFlat = Sign * Math.Abs(Read(parameters, "armorFlat"));

        var duration = parameters.TryGetValue("durationPulses", out var raw)
            && long.TryParse(raw, out var value)
            ? value
            : 80L;

        var name = parameters.TryGetValue("name", out var authored) && !string.IsNullOrWhiteSpace(authored)
            ? authored
            : DefaultName;

        return new ActiveEffect
        {
            EffectKey = EffectKey,
            Name = name,
            SourceEntityId = EffectSource.Of(caster),
            DefenseRatingDelta = defenseRating,
            ArmorFlatDelta = armorFlat,
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
