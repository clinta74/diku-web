using System.Globalization;

namespace Muwbta.Domain.Abilities;

/// <summary>
/// The words an effect uses for whoever it lands on, and for the clock it runs on.
/// </summary>
/// <remarks>
/// <b>Shared so that eleven executors describe the same targeting the same way.</b> Each one knows
/// what it does but none of them knows who it is being aimed at, and "your target" written out
/// eleven times is eleven chances to say "the target" in one of them.
///
/// Who an area effect gathers depends on which way it points, which is why these take
/// <c>harmful</c> as well: <c>AbilitySystem.AreaTargets</c> gathers every mob that may be fought
/// for a harmful cast, and the caster's party - or the room, ungrouped - for a helpful one.
/// </remarks>
public static class AbilityAudience
{
    /// <summary>
    /// Seconds in one pulse.
    /// </summary>
    /// <remarks>
    /// The same quarter second as <c>GameTiming.PulseInterval</c>, which lives in the Engine and so
    /// cannot be referenced from here. <c>AbilityDescriptionTests</c> asserts the two agree, because
    /// a description measured in the wrong seconds is worse than no description.
    /// </remarks>
    public const decimal SecondsPerPulse = 0.25m;

    /// <summary>Whoever this lands on, as the object of a sentence: "stuns <i>your target</i>".</summary>
    public static string Whom(TargetingType targeting, bool harmful) => targeting switch
    {
        TargetingType.Self => "you",
        TargetingType.Aoe => harmful ? "every enemy here" : "everyone with you",
        _ => "your target",
    };

    /// <summary>The same, possessive: "cuts <i>your target's</i> damage".</summary>
    public static string Whose(TargetingType targeting, bool harmful) => targeting switch
    {
        TargetingType.Self => "your",
        TargetingType.Aoe => harmful ? "every enemy's" : "everyone's",
        _ => "your target's",
    };

    /// <summary>A duration in pulses, said in seconds: 80 pulses is "20s", 6 is "1.5s".</summary>
    public static string Seconds(long pulses) =>
        (pulses * SecondsPerPulse).ToString("0.##", CultureInfo.InvariantCulture) + "s";

    /// <summary>
    /// A quantity that varies, said as a range - or as one number when it does not vary.
    /// </summary>
    /// <remarks>
    /// Small numbers have no variance at all, because the executors take a whole-number share of
    /// them: 10% of a 5 point heal truncates to zero. "5-5" would read as a range that is not one.
    /// </remarks>
    public static string Amount(int low, int high) =>
        low == high
            ? low.ToString(CultureInfo.InvariantCulture)
            : $"{low}-{high}";

    /// <summary>A fraction as a percentage: 0.25 is "25".</summary>
    public static string Share(decimal fraction) =>
        Math.Round(fraction * 100m, 0).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A multiplier as the percentage it moves something by: 1.35 is "35".</summary>
    public static string Percent(decimal multiplier, bool above) =>
        Math.Round(Math.Abs((above ? multiplier - 1m : 1m - multiplier) * 100m), 0)
            .ToString("0.##", CultureInfo.InvariantCulture);
}
