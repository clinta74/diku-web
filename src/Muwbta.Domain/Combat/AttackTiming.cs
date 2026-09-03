namespace Muwbta.Domain.Combat;

/// <summary>
/// The vocabulary of attack speed. Every combatant swings on its own clock (PLAN.md §4.6), and
/// this is where "how fast is allowed" and "what happens when nobody said" are decided.
/// </summary>
/// <remarks>
/// Delays are whole pulses because the loop has no finer quantum: one pulse is 250 ms and every
/// system's cadence is an integer multiple of it. The engine could honour a delay of 1; the floor
/// of 4 is a balance rule, enforced when a builder saves and clamped again here so a row written
/// before the rule existed - or straight into the database - can never outrun it.
/// </remarks>
public static class AttackTiming
{
    /// <summary>Fastest a weapon or mob attack may swing: 4 pulses = 1.0 second.</summary>
    public const int MinDelayPulses = 4;

    /// <summary>
    /// What silence means: 8 pulses = 2 seconds, the single shared round every fight used before
    /// combatants had their own clocks. An unauthored weapon must feel exactly as it did.
    /// </summary>
    public const int DefaultDelayPulses = 8;

    /// <summary>What silence means for prose. "You hit a rat" is the pre-verb narration verbatim.</summary>
    public const string DefaultVerb = "hit";

    /// <summary>
    /// Resolves an authored delay to one the engine will honour: absent becomes the default,
    /// and anything below the floor is raised to it rather than refused, because a fight in
    /// progress is the wrong place to discover a bad number.
    /// </summary>
    public static int Clamp(int? authoredPulses) =>
        Math.Max(MinDelayPulses, authoredPulses ?? DefaultDelayPulses);

    /// <summary>Trims an authored verb, falling back to <see cref="DefaultVerb"/> when blank.</summary>
    public static string VerbOr(string? authored) =>
        string.IsNullOrWhiteSpace(authored) ? DefaultVerb : authored.Trim();
}
