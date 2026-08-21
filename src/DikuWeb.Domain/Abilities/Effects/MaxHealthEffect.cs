using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// Raises the bearer's maximum health while it lasts, and grants that much health when it lands.
/// </summary>
/// <remarks>
/// <b>It grants the health, and it is still not a heal.</b> Raising the ceiling without filling
/// the space under it would be a buff that does nothing at the moment you need it — a Warden at
/// 40/100 who gains 50 maximum health is at 40/150, which is further from safety than before. So
/// the grant comes with it.
///
/// <b>Only on first application, never on a refresh.</b> That is the whole difference between this
/// and a heal. Re-casting an expiring guard tops the ceiling back up but adds no health, so the
/// ability cannot be milked as a repeatable top-up on a short cooldown. Enforced by
/// <c>WorldState.ApplyEffect</c>, which knows whether it is adding an effect or refreshing one -
/// the executor cannot tell from here.
///
/// <b>When it expires the ceiling comes down and health clamps to it.</b> A character at 150/150
/// who loses 50 maximum health is at 100/100, not 150/100. The clamp is in the expiry sweep for
/// the same reason the flag lives on the effect: it cannot be forgotten by a code path that
/// removed the effect some other way.
/// </remarks>
public sealed class MaxHealthEffect : IBuffEffect
{
    public string EffectKey => "buff.max-health";

    public bool IsHarmful => false;

    /// <summary>How long the ceiling stays raised when the ability does not say.</summary>
    public const long DefaultDurationPulses = 80L;

    private static (int Bonus, long Duration) Dials(Dictionary<string, string> parameters)
    {
        var bonus = parameters.TryGetValue("maxHealth", out var raw) && int.TryParse(raw, out var value)
            ? value
            : 0;

        var duration = parameters.TryGetValue("durationPulses", out var durRaw)
            && long.TryParse(durRaw, out var dur)
            ? dur
            : DefaultDurationPulses;

        return (bonus, duration);
    }

    /// <summary>
    /// Says the grant as well as the ceiling, because the grant is the half that keeps somebody
    /// alive and the half a player would otherwise have to infer.
    /// </summary>
    public string Describe(
        Dictionary<string, string> parameters,
        TargetingType targeting,
        int casterLevel)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var (bonus, duration) = Dials(parameters);
        var whose = AbilityAudience.Whose(targeting, IsHarmful);

        return bonus == 0
            ? $"leaves {whose} maximum health where it is"
            : $"raises {whose} maximum health by {bonus} and grants that much health, " +
              $"for {AbilityAudience.Seconds(duration)}";
    }

    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        // The health that comes with the ceiling is granted by ApplyEffect, not here. Doing it in
        // Apply would grant it on every cast including a refresh, which is the heal this effect
        // exists to not be.
    }

    public ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var (bonus, duration) = Dials(parameters);

        var name = parameters.TryGetValue("name", out var authored) && !string.IsNullOrWhiteSpace(authored)
            ? authored
            : "steeled";

        return new ActiveEffect
        {
            EffectKey = EffectKey,
            Name = name,
            SourceEntityId = EffectSource.Of(caster),
            MaxHealthDelta = bonus,
            ExpiresAtPulse = currentPulse + duration,

            // Never stacks: two castings would raise the ceiling twice and grant twice, which is
            // the repeatable heal this is shaped to avoid.
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };
    }
}
