using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// A snare: the target can still fight, but it cannot get away.
/// </summary>
/// <remarks>
/// The seventh executor, and the counterpart to the stun. Where a stun takes the turn and leaves
/// the exit open, this leaves the turn and closes the exit - so the two are not degrees of the
/// same thing and an ability can want one without wanting the other.
///
/// What it actually denies is <c>flee</c>. Ordinary movement is already refused while fighting,
/// so a root that only blocked walking would do nothing at all in the one situation it is cast
/// in. Fleeing is the escape; denying it is the effect.
///
/// That makes it strong, so it is clamped like the stun and kept shorter than most buffs. A root
/// long enough to guarantee a kill stops being a way to catch someone and becomes a way to
/// remove them, which is the same failure the stun ceiling guards against.
/// </remarks>
public sealed class RootEffect : IBuffEffect
{
    public string EffectKey => "control.root";

    /// <summary>The longest a snare may hold, in pulses - ten seconds.</summary>
    public const long MaxDurationPulses = 40;

    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        // Nothing instant: the effect is entirely the exit it closes.
    }

    public ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var duration = parameters.TryGetValue("durationPulses", out var raw) &&
                       long.TryParse(raw, out var parsed)
            ? parsed
            : 24;

        duration = Math.Clamp(duration, 1, MaxDurationPulses);

        var name = parameters.TryGetValue("name", out var nameStr) && !string.IsNullOrEmpty(nameStr)
            ? nameStr
            : "held fast";

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
            PreventsEscape = true,
            ExpiresAtPulse = currentPulse + duration,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };
    }
}
