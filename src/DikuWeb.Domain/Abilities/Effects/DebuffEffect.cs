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
    public string EffectKey => "debuff.weaken";

    public bool IsHarmful => true;

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
        var outgoingMultiplier = 1.0m;
        var durationPulses = 240L;
        var maxStacks = 1;
        var stackingRule = EffectStackingRule.Refresh;
        var name = "weakness";

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
            OutgoingDamageMultiplier = outgoingMultiplier,
            IncomingDamageMultiplier = incomingMultiplier,
            ExpiresAtPulse = currentPulse + durationPulses,
            Stacks = 1,
            MaxStacks = maxStacks,
            StackingRule = stackingRule,
        };
    }
}
