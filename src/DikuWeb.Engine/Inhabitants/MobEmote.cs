namespace DikuWeb.Engine.Inhabitants;

/// <summary>
/// One line of idle prose and how often it should be heard (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// A cadence per line rather than per mob, because the two things a mob says are rarely worth
/// hearing equally often: a shopkeeper calling the catch of the day every few minutes is
/// atmosphere, and the same line every four seconds is a reason to leave the room. A rat squeaking
/// often is fine, because "squeaks" is two syllables and carries no information to get tired of.
///
/// Ranges rather than fixed intervals so a room of three rats does not fall into step. Two mobs
/// spawned in the same sweep with the same fixed interval emote in lockstep forever, which reads
/// as clockwork rather than as life — the thing this feature exists to fix.
/// </remarks>
public sealed record MobEmote(string Text, int MinPulses, int MaxPulses)
{
    /// <summary>Four pulses to the second (PLAN.md §2.3).</summary>
    private const int PulsesPerSecond = 4;

    /// <summary>
    /// What a line with no timing of its own gets: somewhere between twenty seconds and a minute.
    /// </summary>
    /// <remarks>
    /// This replaces a hardcoded four-second interval that every mob shared. Four seconds was
    /// never an authored choice — it was the only number there was, and it made anything with an
    /// emote list chatter roughly fifteen times a minute. Nothing seeded relies on it.
    /// </remarks>
    public const int DefaultMinSeconds = 20;

    /// <inheritdoc cref="DefaultMinSeconds"/>
    public const int DefaultMaxSeconds = 60;

    /// <summary>
    /// Builds one from authored seconds, clamped into a range that can actually be scheduled.
    /// </summary>
    /// <remarks>
    /// A minimum of one second, because zero would mean every pulse and a negative would mean
    /// every pulse forever. A maximum below the minimum is read as "exactly the minimum" rather
    /// than refused: the builder writes two numbers and inverting them is an easy slip, and there
    /// is an obvious reading that is not a crash.
    /// </remarks>
    public static MobEmote FromSeconds(string text, int minSeconds, int maxSeconds)
    {
        var min = Math.Max(1, minSeconds);
        var max = Math.Max(min, maxSeconds);

        return new MobEmote(text, min * PulsesPerSecond, max * PulsesPerSecond);
    }

    /// <summary>When this line should next be heard, given the pulse it was last scheduled from.</summary>
    public long NextPulseAfter(long currentPulse, Domain.Randomness.IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        // Exclusive upper bound, hence the +1: a range authored as 30 to 30 must be able to
        // produce 30 rather than throwing on an empty interval.
        return currentPulse + random.Next(MinPulses, MaxPulses + 1);
    }
}
