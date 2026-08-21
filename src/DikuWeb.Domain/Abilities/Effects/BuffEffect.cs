using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// A buff effect that increases outgoing damage for the target.
/// Implements IBuffEffect to create ongoing ActiveEffect state.
/// </summary>
public sealed class BuffEffect : IBuffEffect
{
    /// <summary>What the buff is worth when the ability does not say: nothing.</summary>
    public const decimal DefaultOutgoingMultiplier = 1.0m;

    /// <summary>How long it lasts when the ability does not say - a minute.</summary>
    public const long DefaultDurationPulses = 240L;

    public string EffectKey => "buff.damage-up";

    public bool IsHarmful => false;

    /// <summary>How much more damage the bearer deals, and for how long.</summary>
    /// <remarks>
    /// Read here rather than in <see cref="Describe"/> as well, so the phrase a player is shown
    /// comes from the same defaults the buff is built with.
    /// </remarks>
    private static (decimal Outgoing, long Duration) Dials(Dictionary<string, string> parameters)
    {
        var outgoing = parameters.TryGetValue("outgoingMultiplier", out var outStr) &&
                       decimal.TryParse(outStr, out var outMult)
            ? outMult
            : DefaultOutgoingMultiplier;

        var duration = parameters.TryGetValue("durationPulses", out var durStr) &&
                       long.TryParse(durStr, out var dur)
            ? dur
            : DefaultDurationPulses;

        return (outgoing, duration);
    }

    public string Describe(
        Dictionary<string, string> parameters,
        TargetingType targeting,
        int casterLevel)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var (outgoing, duration) = Dials(parameters);
        var whose = AbilityAudience.Whose(targeting, IsHarmful);

        // A buff below 1.0 is refused by AbilityValidator, so this only reads oddly for content
        // that was never saved through the builder - and reading oddly is the correct answer there.
        return outgoing <= 1m
            ? $"changes nothing about {whose} damage for {AbilityAudience.Seconds(duration)}"
            : $"raises {whose} damage by {AbilityAudience.Percent(outgoing, above: true)}% " +
              $"for {AbilityAudience.Seconds(duration)}";
    }

    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        // Buff application is a no-op; all real work happens in CreateActiveEffect
    }

    public ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var outgoingMultiplier = DefaultOutgoingMultiplier;
        var durationPulses = DefaultDurationPulses;
        var maxStacks = 1;
        var stackingRule = EffectStackingRule.Refresh;
        // A participle, not a noun. This name is shown in the status panel *and* dropped into
        // "You are …!", and "You are damage boost!" is what a noun produces there.
        var name = "emboldened";

        // Parse parameters
        if (parameters.TryGetValue("outgoingMultiplier", out var outStr) &&
            decimal.TryParse(outStr, out var outMult))
        {
            outgoingMultiplier = outMult;
        }

        if (parameters.TryGetValue("durationPulses", out var durStr) &&
            long.TryParse(durStr, out var dur))
        {
            durationPulses = dur;
        }

        if (parameters.TryGetValue("maxStacks", out var stackStr) &&
            int.TryParse(stackStr, out var stacks))
        {
            maxStacks = stacks;
        }

        if (parameters.TryGetValue("stackingRule", out var ruleStr) &&
            Enum.TryParse<EffectStackingRule>(ruleStr, out var rule))
        {
            stackingRule = rule;
        }

        if (parameters.TryGetValue("name", out var nameStr) && !string.IsNullOrEmpty(nameStr))
        {
            name = nameStr;
        }

        var sourceId = EffectSource.Of(caster);

        return new ActiveEffect
        {
            EffectKey = EffectKey,
            Name = name,
            SourceEntityId = sourceId,
            OutgoingDamageMultiplier = outgoingMultiplier,
            IncomingDamageMultiplier = 1.0m,
            ExpiresAtPulse = currentPulse + durationPulses,
            Stacks = 1,
            MaxStacks = maxStacks,
            StackingRule = stackingRule,
        };
    }
}
