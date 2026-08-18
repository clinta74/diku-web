using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// A blow that takes the target off its feet: for a short while it does not act.
/// </summary>
/// <remarks>
/// The sixth executor, and the first that takes a *turn* away rather than changing a number. That
/// is what makes it the strongest identity a martial Path can have: a Warden who can open a window
/// is doing something no amount of damage scaling expresses.
///
/// Deliberately short. A stun is measured in a couple of swings, not in seconds of standing
/// still - anything longer stops being a tempo tool and starts being a way to remove an opponent
/// from the game, which is the thing that makes stuns miserable to play against.
///
/// It also breaks a cast in progress. A caster who cannot act cannot be halfway through a spell,
/// and an interrupt is most of why a stun is worth pressing.
/// </remarks>
public sealed class StunEffect : IBuffEffect
{
    public string EffectKey => "control.stun";

    public bool IsHarmful => true;

    /// <summary>
    /// The longest a stun may last, in pulses. Six seconds of not acting is already at the edge
    /// of tolerable; this is a hard ceiling so no authored ability can exceed it by accident.
    /// </summary>
    public const long MaxDurationPulses = 24;

    /// <summary>
    /// The clamped duration, not the authored one - the executor clamps, so the clamp is what a player is told.
    /// </summary>
    /// <remarks>
    /// <c>AbilityValidator</c> warns when an authored duration exceeds the ceiling, and this is the
    /// same fact said in the other direction: whatever the editor holds, the description reports
    /// the {0} pulses the effect will actually run for.
    /// </remarks>
    public static long DurationOf(Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var duration = parameters.TryGetValue("durationPulses", out var raw) &&
                       long.TryParse(raw, out var parsed)
            ? parsed
            : 8;

        return Math.Clamp(duration, 1, MaxDurationPulses);
    }

    public string Describe(Dictionary<string, string> parameters, TargetingType targeting) =>
        $"stops {AbilityAudience.Whom(targeting, IsHarmful)} acting " +
        $"for {AbilityAudience.Seconds(DurationOf(parameters))}";

    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        // Nothing instant: the whole effect is the window it opens.
    }

    public ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Clamped rather than trusted. These parameters are authored content, and a typo that
        // added a zero would be a target that never acts again.
        var duration = DurationOf(parameters);

        var name = parameters.TryGetValue("name", out var nameStr) && !string.IsNullOrEmpty(nameStr)
            ? nameStr
            : "stunned";

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
            PreventsActing = true,
            ExpiresAtPulse = currentPulse + duration,
            Stacks = 1,
            MaxStacks = 1,

            // Never stacks. Chaining stuns into a permanent lock is the failure mode, and
            // refreshing is generous enough for the tempo it is meant to buy.
            StackingRule = EffectStackingRule.Refresh,
        };
    }
}
