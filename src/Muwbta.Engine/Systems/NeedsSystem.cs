using Muwbta.Domain.Characters;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Systems;

/// <summary>
/// Hunger and thirst arriving, one point at a time, and being said out loud when they matter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only for characters who are logged in.</b> This walks <c>world.AllPlayers</c>, which is the
/// connected set — so a character left alone for a week comes back exactly as fed as they left. That
/// falls out of the existing shape rather than needing a rule of its own, and it is the property
/// that keeps an upkeep from becoming a punishment for having a job.
/// </para>
/// <para>
/// <b>The announcement is half the feature.</b> The only other thing hunger does is slow recovery
/// (<c>Needs.RegenShare</c>), and a regen rate is not something a player can see. Without a line
/// when a threshold is crossed, the whole system reads as "the game got slower and nobody said
/// why" — which is indistinguishable from a bug.
/// </para>
/// <para>
/// Ticks faster than either need grows, and each need carries its own accumulator, so the two can
/// run at different rates without the interval having to be a common factor of both. Thirst arrives
/// first; see <see cref="Needs.MinutesPerThirst"/> for why.
/// </para>
/// </remarks>
public static class NeedsSystem
{
    /// <summary>Thirty seconds, in pulses.</summary>
    /// <remarks>
    /// Short relative to both needs, so the accumulators below decide the pace rather than this
    /// does. It exists to be a cheap heartbeat, not a rate.
    /// </remarks>
    public const long IntervalPulses = 120;

    /// <summary>How many of these ticks make an hour.</summary>
    private const long TicksPerHour = 240L * 60 / IntervalPulses;

    /// <summary>Ticks from a full belly to starving.</summary>
    public const long TicksToStarving = Needs.HoursToStarving * TicksPerHour;

    /// <summary>Ticks from watered to parched.</summary>
    public const long TicksToParched = Needs.HoursToParched * TicksPerHour;

    /// <summary>
    /// Advances hunger and thirst for everyone connected, and tells them when it starts to show.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="tick">
    /// Which tick this is. Counted rather than read from the pulse so the arithmetic below is about
    /// elapsed ticks and not about where the server happened to be when it started.
    /// </param>
    public static void Tick(WorldState world, long tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        foreach (var actor in world.AllPlayers)
        {
            var character = actor.Character;
            var vitals = character.Vitals;

            var hungerBefore = Needs.DescribeHunger(vitals.Hunger);
            var thirstBefore = Needs.DescribeThirst(vitals.Thirst);

            if (Due(tick, TicksToStarving))
            {
                vitals.Hunger = Needs.Increased(vitals.Hunger, 1);
            }

            if (Due(tick, TicksToParched))
            {
                vitals.Thirst = Needs.Increased(vitals.Thirst, 1);
            }

            // Said only on the way past a threshold, not every time one is held. A line every
            // thirty seconds saying you are still hungry is the emote stream DreamSystem exists to
            // avoid, wearing a different hat.
            if (Needs.DescribeHunger(vitals.Hunger) is { } hunger && hunger != hungerBefore)
            {
                actor.SendText($"You are {hunger}.", "bad");
            }

            if (Needs.DescribeThirst(vitals.Thirst) is { } thirst && thirst != thirstBefore)
            {
                actor.SendText($"You are {thirst}.", "bad");
            }
        }
    }

    /// <summary>
    /// Whether a need that empties over <paramref name="ticksToEmpty"/> should advance on this tick.
    /// </summary>
    /// <remarks>
    /// <b>Exact over the whole span rather than a period rounded to a tick.</b> Eight hours over a
    /// hundred points is 9.6 ticks a point, and a modulus has to round that — to 10, which is eight
    /// hours and twenty minutes, and to 7 for thirst, which is five hours fifty. Comparing how many
    /// points <em>should</em> have arrived by this tick against the previous one advances exactly
    /// <see cref="Needs.Worst"/> times across exactly the span asked for, with the unevenness spread
    /// through it instead of accumulating at the end.
    /// </remarks>
    private static bool Due(long tick, long ticksToEmpty)
    {
        if (tick <= 0 || ticksToEmpty <= 0)
        {
            return false;
        }

        return tick * Needs.Worst / ticksToEmpty > (tick - 1) * Needs.Worst / ticksToEmpty;
    }
}
