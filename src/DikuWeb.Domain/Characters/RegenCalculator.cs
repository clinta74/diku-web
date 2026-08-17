namespace DikuWeb.Domain.Characters;

/// <summary>
/// Pure function: calculates vital regeneration based on rest state and attributes.
/// Scales with Vitality modifier to reward defensive gearing.
/// </summary>
public static class RegenCalculator
{
    /// <summary>
    /// Base regen percentage per vital per state, per tick. All vitals regen at the same rate
    /// for their state; different vitals' max values cause the absolute amounts to differ.
    /// </summary>
    private static readonly Dictionary<CharacterRestState, double> BaseRegenPercent = new()
    {
        { CharacterRestState.Sleep, 0.15 },   // 15% of max per tick
        { CharacterRestState.Rest, 0.08 },    // 8% of max per tick
        { CharacterRestState.Stand, 0.02 },   // 2% of max per tick
    };

    /// <summary>
    /// How much faster a Path recovers focus than it recovers anything else.
    /// </summary>
    /// <remarks>
    /// Doubled for the two Paths that spend focus to do their job (PLAN.md §4.5). A Warden's focus
    /// bar is a small reserve behind a stamina bar that does the work; an Adept's <em>is</em> the
    /// work, and at the shared rate an empty one took the better part of an hour standing, which
    /// made the recovery verbs the game's most-typed ones for exactly two of the four Paths.
    ///
    /// Applied to the whole rate rather than only the base, so gearing Vitality still helps a caster
    /// recover twice as much as it helps a martial Path - the multiplier is about what the vital is
    /// <em>for</em>, not about which half of the sum it lands on.
    /// </remarks>
    public const double CasterFocusRate = 2.0;

    /// <summary>Whether this Path pays for its abilities in focus.</summary>
    public static double FocusRateFor(CharacterPath path) =>
        path is CharacterPath.Adept or CharacterPath.Hallow ? CasterFocusRate : 1.0;

    /// <summary>
    /// The health arm of <see cref="Calculate"/> on its own, for an entity that has no Path.
    /// </summary>
    /// <remarks>
    /// <b>Split out for mobs, and split rather than defaulted on purpose.</b> Mobs regenerate now
    /// (PLAN.md §4.6) and a mob has no <see cref="CharacterPath"/>, so the alternative was passing
    /// one that is not true — which <see cref="Calculate"/>'s own doc argues against, because the
    /// only symptom of the wrong Path is a rate nothing on screen reports. Health does not consult
    /// the Path at all, so there is nothing to invent: this is the whole of what a mob needs, and
    /// <see cref="Calculate"/> calls it for its own health term so the rate table stays one table.
    ///
    /// <b>Health only, for mobs.</b> Nothing reads a mob's focus or stamina — ability costs come off
    /// <c>character.Vitals</c> and combat only ever writes <c>mob.Vitals.Health</c> — and
    /// regenerating a bar with no readers is <c>itemPower</c> in miniature.
    /// </remarks>
    public static int HealthFor(CharacterRestState state, Vitals vitals, int vitalityModifier)
    {
        ArgumentNullException.ThrowIfNull(vitals);

        return Math.Max(1, (int)Math.Floor(vitals.HealthMax * EffectivePercent(state, vitalityModifier)));
    }

    /// <summary>The share of a maximum that comes back this tick, before any per-vital rate.</summary>
    private static double EffectivePercent(CharacterRestState state, int vitalityModifier) =>
        // Each modifier point adds one percentage point.
        BaseRegenPercent[state] + (vitalityModifier * 0.01);

    /// <summary>
    /// Calculate how much of each vital regenerates in a single 60-second tick.
    /// Amount is always at least 1 per vital, floored after applying modifiers.
    /// </summary>
    /// <remarks>
    /// Vitality modifier adds percentage points to the base regen rate. This ties recovery
    /// speed directly to character gearing and creates a tangible reward for stacking
    /// defensive attributes. A character with +3 Vitality modifier effectively gets 3% bonus
    /// regen across all states.
    ///
    /// The Path is taken rather than defaulted because getting it wrong is silent: a caster handed
    /// the martial rate recovers at half speed and nothing on screen says so.
    /// </remarks>
    public static (int health, int focus, int stamina) Calculate(
        CharacterRestState state,
        Vitals vitals,
        int vitalityModifier,
        CharacterPath path)
    {
        var effectivePercent = EffectivePercent(state, vitalityModifier);

        var health = HealthFor(state, vitals, vitalityModifier);
        var focus = Math.Max(1, (int)Math.Floor(vitals.FocusMax * effectivePercent * FocusRateFor(path)));
        var stamina = Math.Max(1, (int)Math.Floor(vitals.StaminaMax * effectivePercent));

        return (health, focus, stamina);
    }

    /// <summary>
    /// Apply regen to a character's vitals in place, capping to max.
    /// Returns true if any vital changed, false if already at max.
    /// </summary>
    public static bool ApplyRegen(
        CharacterRestState state,
        Vitals vitals,
        int vitalityModifier,
        CharacterPath path)
    {
        var (healthRegen, focusRegen, staminaRegen) = Calculate(state, vitals, vitalityModifier, path);

        var beforeHealth = vitals.Health;
        var beforeFocus = vitals.Focus;
        var beforeStamina = vitals.Stamina;

        vitals.Health = Math.Min(vitals.Health + healthRegen, vitals.HealthMax);
        vitals.Focus = Math.Min(vitals.Focus + focusRegen, vitals.FocusMax);
        vitals.Stamina = Math.Min(vitals.Stamina + staminaRegen, vitals.StaminaMax);

        return vitals.Health != beforeHealth || vitals.Focus != beforeFocus || vitals.Stamina != beforeStamina;
    }
}
