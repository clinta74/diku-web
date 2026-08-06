using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// A debuff effect that increases incoming damage for the target.
/// Implements IBuffEffect to create ongoing ActiveEffect state.
/// </summary>
public sealed class DebuffEffect : IBuffEffect
{
    public string EffectKey => "debuff.weaken";

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

        var incomingMultiplier = 1.0m;
        var durationPulses = 240L;
        var maxStacks = 1;
        var stackingRule = EffectStackingRule.Refresh;
        var name = "weakness";

        // Parse parameters
        if (parameters.TryGetValue("incomingMultiplier", out var inStr) &&
            decimal.TryParse(inStr, out var inMult))
        {
            incomingMultiplier = inMult;
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

        var sourceId = caster is DikuWeb.Domain.Characters.Character c
            ? $"c_{c.Id:N}"
            : caster is DikuWeb.Domain.Inhabitants.Mob m
                ? $"m_{m.Id:N}"
                : "unknown";

        return new ActiveEffect
        {
            EffectKey = EffectKey,
            Name = name,
            SourceEntityId = sourceId,
            OutgoingDamageMultiplier = 1.0m,
            IncomingDamageMultiplier = incomingMultiplier,
            ExpiresAtPulse = currentPulse + durationPulses,
            Stacks = 1,
            MaxStacks = maxStacks,
            StackingRule = stackingRule,
        };
    }
}
