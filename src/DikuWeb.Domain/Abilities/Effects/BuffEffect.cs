using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// A buff effect that increases outgoing damage for the target.
/// Implements IBuffEffect to create ongoing ActiveEffect state.
/// </summary>
public sealed class BuffEffect : IBuffEffect
{
    public string EffectKey => "buff.damage-up";

    public bool IsHarmful => false;

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

        var outgoingMultiplier = 1.0m;
        var durationPulses = 240L;
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
