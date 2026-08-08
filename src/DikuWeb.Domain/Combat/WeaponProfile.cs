using DikuWeb.Domain.Items;

namespace DikuWeb.Domain.Combat;

/// <summary>How fast one hand swings and what the prose calls it.</summary>
/// <param name="DelayPulses">Pulses between swings, already clamped to the floor.</param>
/// <param name="Verb">Base-form verb for narration.</param>
/// <param name="CanSwing">False when this hand does not attack at all.</param>
public readonly record struct WeaponProfile(int DelayPulses, string Verb, bool CanSwing)
{
    /// <summary>A hand that holds nothing it can strike with.</summary>
    public static WeaponProfile Idle { get; } =
        new(AttackTiming.DefaultDelayPulses, AttackTiming.DefaultVerb, CanSwing: false);
}

/// <summary>
/// Reads a hand's speed and verb from what is equipped in it.
/// </summary>
/// <remarks>
/// Speed and verb live on the <see cref="ItemTemplate"/>, not on the spawned instance, so this
/// takes a lookup delegate rather than a cache: the Domain stays ignorant of how the Engine
/// stores templates, and because the lookup runs at every readiness check, a builder retuning a
/// sword changes a fight already under way.
/// </remarks>
public static class WeaponResolver
{
    /// <summary>
    /// Resolves the profile for one hand.
    /// </summary>
    /// <param name="equipped">What is in that hand, or null.</param>
    /// <param name="lookup">Resolves a template key to its authored delay and verb.</param>
    /// <param name="isMainHand">
    /// The two hands treat silence differently, and the asymmetry is deliberate. An empty or
    /// speechless main hand still swings - a fist is always a weapon - so it falls back to the
    /// 8-pulse default. An off hand only swings when its item declares a delay, because the
    /// alternative is every shield in the game throwing punches.
    /// </param>
    public static WeaponProfile ForHand(
        ItemInstance? equipped,
        Func<string, (int? DelayPulses, string? Verb)> lookup,
        bool isMainHand)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var (delay, verb) = equipped is null
            ? (null, null)
            : lookup(equipped.TemplateKey);

        if (!isMainHand && (equipped is null || delay is null))
        {
            return WeaponProfile.Idle;
        }

        return new WeaponProfile(
            AttackTiming.Clamp(delay),
            AttackTiming.VerbOr(verb),
            CanSwing: true);
    }
}
