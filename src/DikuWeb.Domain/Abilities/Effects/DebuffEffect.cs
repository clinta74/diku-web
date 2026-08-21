using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// A debuff on the target: it can make them take more damage, deal less, or both.
/// Implements IBuffEffect to create ongoing ActiveEffect state.
/// </summary>
/// <remarks>
/// <c>outgoingMultiplier</c> was hardcoded to 1.0, so the only debuff this could express was
/// vulnerability - raising incoming damage. That left "weaken" unable to actually weaken
/// anything, and an ability author reaching for it would set <c>incomingMultiplier</c> below 1.0
/// and *protect* the target instead. Both directions are readable now, and each ability says
/// which one it means.
/// </remarks>
public sealed class DebuffEffect : IBuffEffect
{
    /// <summary>Neither dial moves anything when the ability does not set it.</summary>
    public const decimal NoChange = 1.0m;

    /// <summary>How long it lasts when the ability does not say - a minute.</summary>
    public const long DefaultDurationPulses = 240L;

    public string EffectKey => "debuff.weaken";

    public bool IsHarmful => true;

    /// <summary>The two directions and the clock, read once for both the effect and the phrase.</summary>
    private static (decimal Incoming, decimal Outgoing, long Duration) Dials(
        Dictionary<string, string> parameters)
    {
        var incoming = parameters.TryGetValue("incomingMultiplier", out var inStr) &&
                       decimal.TryParse(inStr, out var inMult)
            ? inMult
            : NoChange;

        var outgoing = parameters.TryGetValue("outgoingMultiplier", out var outStr) &&
                       decimal.TryParse(outStr, out var outMult)
            ? outMult
            : NoChange;

        var duration = parameters.TryGetValue("durationPulses", out var durStr) &&
                       long.TryParse(durStr, out var dur)
            ? dur
            : DefaultDurationPulses;

        return (incoming, outgoing, duration);
    }

    /// <summary>
    /// Says which of the two things it is doing, because they are two things.
    /// </summary>
    /// <remarks>
    /// The direction is the whole danger with this effect - a "weaken" written as
    /// <c>incomingMultiplier</c> below 1.0 protects its target, which shipped once to every debuff
    /// in the game. Naming the direction out loud in play is a second pair of eyes on it: a debuff
    /// that reads "raises your target's damage" is wrong in a way nobody has to read code to see.
    /// </remarks>
    public string Describe(
        Dictionary<string, string> parameters,
        TargetingType targeting,
        int casterLevel)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var (incoming, outgoing, duration) = Dials(parameters);
        var whom = AbilityAudience.Whom(targeting, IsHarmful);
        var whose = AbilityAudience.Whose(targeting, IsHarmful);

        var parts = new List<string>();

        if (outgoing != NoChange)
        {
            parts.Add(outgoing < NoChange
                ? $"cuts {whose} damage by {AbilityAudience.Percent(outgoing, above: false)}%"
                : $"raises {whose} damage by {AbilityAudience.Percent(outgoing, above: true)}%");
        }

        if (incoming != NoChange)
        {
            parts.Add(incoming > NoChange
                ? $"raises the damage {whom} takes by {AbilityAudience.Percent(incoming, above: true)}%"
                : $"cuts the damage {whom} takes by {AbilityAudience.Percent(incoming, above: false)}%");
        }

        return parts.Count == 0
            ? $"does nothing to {whom}"
            : $"{string.Join(" and ", parts)} for {AbilityAudience.Seconds(duration)}";
    }

    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        // Debuff application is a no-op; all real work happens in CreateActiveEffect
    }

    public ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var incomingMultiplier = NoChange;
        var outgoingMultiplier = NoChange;
        var durationPulses = DefaultDurationPulses;
        var maxStacks = 1;
        var stackingRule = EffectStackingRule.Refresh;
        // A participle, not a noun - see BuffEffect. This one is reachable in play: weaken is in
        // the rider set, so an authored mob attack with no name said "You are weakness!".
        var name = "weakened";

        // Parse parameters. Above 1.0 on incoming means the target takes more; below 1.0 on
        // outgoing means it deals less. Either alone is a debuff; both together is a strong one.
        if (parameters.TryGetValue("incomingMultiplier", out var inStr) &&
            decimal.TryParse(inStr, out var inMult))
        {
            incomingMultiplier = inMult;
        }

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
            IncomingDamageMultiplier = incomingMultiplier,
            ExpiresAtPulse = currentPulse + durationPulses,
            Stacks = 1,
            MaxStacks = maxStacks,
            StackingRule = stackingRule,
        };
    }
}
