using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// A wound that keeps working: a bleed, a burn, a poison.
/// </summary>
/// <remarks>
/// The fifth effect executor, and the first that differs from the other four in *kind* rather
/// than in magnitude. The originals all scale a number at the moment of a swing - damage, healing,
/// or a multiplier on someone else's swing - so every Path could only be distinguished by how much
/// it paid and how often. This one puts damage on a clock of its own, which is a different thing
/// to build an ability around: it rewards applying early and punishes a target that stands still.
///
/// The application itself deals nothing. All of it lands through the <see cref="ActiveEffect"/>,
/// which <c>CombatSystem</c> ticks - there is no instant component, so a bleed that is immediately
/// cured does no damage at all.
/// </remarks>
public sealed class DamageOverTimeEffect : IBuffEffect
{
    public string EffectKey => "damage.overtime";

    public bool IsHarmful => true;

    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        // Nothing on application: the whole effect is the ticking, and doing damage here as well
        // would make a one-tick bleed hit twice.
    }

    public ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var tickDamage = Read(parameters, "tickDamage", 4);
        var tickInterval = Read(parameters, "tickIntervalPulses", 8);
        var durationPulses = Read(parameters, "durationPulses", 48);
        var maxStacks = (int)Read(parameters, "maxStacks", 1);

        var stackingRule = parameters.TryGetValue("stackingRule", out var ruleStr) &&
                           Enum.TryParse<EffectStackingRule>(ruleStr, out var rule)
            ? rule
            : EffectStackingRule.Refresh;

        var name = parameters.TryGetValue("name", out var nameStr) && !string.IsNullOrEmpty(nameStr)
            ? nameStr
            : "bleeding";

        var sourceId = caster is Characters.Character c
            ? $"c_{c.Id:N}"
            : caster is Inhabitants.Mob m
                ? $"m_{m.Id:N}"
                : "unknown";

        return new ActiveEffect
        {
            EffectKey = EffectKey,
            Name = name,
            SourceEntityId = sourceId,
            TickDamage = (int)tickDamage,
            TickIntervalPulses = tickInterval,

            // The first tick lands one interval from now, not immediately. Otherwise a bleed
            // deals its opening damage on the same pulse as the blow that caused it, and reads
            // to a player as the strike hitting twice.
            NextTickPulse = currentPulse + tickInterval,
            ExpiresAtPulse = currentPulse + durationPulses,
            Stacks = 1,
            MaxStacks = maxStacks,
            StackingRule = stackingRule,
        };
    }

    private static long Read(Dictionary<string, string> parameters, string key, long fallback) =>
        parameters.TryGetValue(key, out var raw) && long.TryParse(raw, out var value)
            ? value
            : fallback;
}
