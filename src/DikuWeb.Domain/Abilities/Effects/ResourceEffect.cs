using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// Puts a resource back: focus, stamina, or health.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the other half of a conversion, and the ability already carries the first half.</b>
/// An ability that turns stamina into focus needs no new vocabulary for the stamina: it is priced in
/// stamina, through <see cref="Ability.CostType"/> and <see cref="Ability.CostValue"/>, which the
/// cast path deducts before any effect runs. So a converter is an ordinary stamina ability whose
/// effect happens to hand back focus, and the exchange rate is the two authored numbers side by
/// side rather than a third concept that could disagree with them.
/// </para>
/// <para>
/// <b>Why it matters that a Path can reach its other bar at all.</b> Measured, a level 50 Adept
/// spends its whole focus pool in a fight and none of its 237 stamina — every Path drains one bar
/// and carries the other in untouched. That idle bar is the largest unused capacity in the game,
/// and it is unreachable not because it is small but because nothing is priced against it.
/// </para>
/// <para>
/// <b>Health is restorable here and it is not a heal.</b> <c>heal.restore</c> exists, is what every
/// healing ability in the game authors, and narrates as healing. This restores a *resource* and
/// says so; an ability wanting to mend somebody should use the heal. Health is accepted only so
/// that the three bars are one vocabulary rather than two-and-an-exception.
/// </para>
/// </remarks>
public sealed class ResourceEffect : IAbilityEffect
{
    /// <summary>Which bar this refills when the ability does not say.</summary>
    /// <remarks>
    /// Focus, because the case this was written for is a caster buying focus with stamina, and a
    /// silent parameter should land on the common one rather than on nothing.
    /// </remarks>
    public const CostType DefaultResource = CostType.Focus;

    public string EffectKey => "resource.restore";

    public bool IsHarmful => false;

    /// <summary>Which bar an authored <c>resource</c> names.</summary>
    /// <remarks>
    /// Anything unrecognised reads as <see cref="DefaultResource"/> rather than throwing, matching
    /// how every other executor treats a parameter it cannot parse — an ability that refuses to
    /// cast is worse in play than one that does the ordinary thing.
    /// </remarks>
    public static CostType ResourceOf(Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return parameters.TryGetValue("resource", out var raw) &&
               Enum.TryParse<CostType>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : DefaultResource;
    }

    /// <summary>
    /// The share of that bar's maximum this returns, or zero when it returns a flat amount.
    /// </summary>
    /// <remarks>
    /// Whole percentage points, as <c>healPercent</c> and <c>mitigation</c> are written. A
    /// proportional restore is the right default shape here for the reason a proportional heal was:
    /// the pools grow with level, so a flat number authored against a level 20 bar is a rounding
    /// error against a level 50 one.
    /// </remarks>
    public static double PercentOf(Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return parameters.TryGetValue("percent", out var raw) && double.TryParse(raw, out var value)
            ? Math.Clamp(value, 0, 100) / 100.0
            : 0;
    }

    /// <summary>How much this returns, for a bar of this size.</summary>
    public static int Amount(Dictionary<string, string> parameters, int maximum)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var percent = PercentOf(parameters);

        if (percent > 0 && maximum > 0)
        {
            return Math.Max(1, (int)Math.Round(maximum * percent, MidpointRounding.AwayFromZero));
        }

        return parameters.TryGetValue("amount", out var raw) && int.TryParse(raw, out var flat)
            ? Math.Max(0, flat)
            : 0;
    }

    public string Describe(
        Dictionary<string, string> parameters,
        TargetingType targeting,
        int casterLevel)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var resource = ResourceOf(parameters).ToString().ToLowerInvariant();
        var percent = PercentOf(parameters);
        var whom = AbilityAudience.Whom(targeting, IsHarmful);

        // The share rather than a number, for the reason a proportional heal describes itself that
        // way: the listing has no bar in front of it to take a maximum from.
        if (percent > 0)
        {
            return $"restores {percent:P0} of maximum {resource} to {whom}";
        }

        var flat = Amount(parameters, 0);

        return flat > 0
            ? $"restores {flat} {resource} to {whom}"
            : $"restores no {resource} — this ability sets neither 'percent' nor 'amount'";
    }

    public void Apply(object caster, object? target, Dictionary<string, string> parameters, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(parameters);

        // Characters only. A mob has no focus or stamina anybody reads - combat writes its health
        // and nothing else - so refilling one would be a bar with no readers, which is the
        // `itemPower` mistake in miniature (see RegenCalculator).
        if (target is not Character character)
        {
            return;
        }

        var vitals = character.Vitals;

        switch (ResourceOf(parameters))
        {
            case CostType.Focus:
                vitals.Focus = Math.Min(
                    vitals.FocusMax, vitals.Focus + Amount(parameters, vitals.FocusMax));
                break;

            case CostType.Stamina:
                vitals.Stamina = Math.Min(
                    vitals.StaminaMax, vitals.Stamina + Amount(parameters, vitals.StaminaMax));
                break;

            case CostType.Health:
                vitals.Health = Math.Min(
                    vitals.HealthMax, vitals.Health + Amount(parameters, vitals.HealthMax));
                break;
        }
    }
}
