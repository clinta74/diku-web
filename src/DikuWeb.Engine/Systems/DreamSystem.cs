using DikuWeb.Domain.Characters;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// What a sleeping character gets instead of the room's flavour: one line every five minutes.
/// </summary>
/// <remarks>
/// <para>
/// Sleep silences idle emotes — a sleeper is not watching the room, and a screen filling with
/// things a rat did while they were unconscious is noise nobody reads. Silence on its own is
/// worse than the noise, though: a player who types <c>sleep</c> and then sees nothing at all for
/// ten minutes has no way to tell rest from a dropped connection. The dream is the heartbeat that
/// says the game still has them.
/// </para>
/// <para>
/// Five minutes is deliberately long. This is the opposite of the emote stream it replaces, and a
/// dream every thirty seconds would be the same problem wearing a hat.
/// </para>
/// <para>
/// The line is chosen from the character's id and the count of dreams they have had, rather than at
/// random: one player gets a different dream each time, two players are keyed apart so a shared room
/// does not read as a broadcast, and neither needs a random source threaded into the loop.
/// </para>
/// <para>
/// <b>Two sleepers are not <em>guaranteed</em> to differ</b>, and the comment here used to say they
/// were. Ten lines and two arbitrary ids collide about one time in ten, so a test asserting it
/// failed roughly that often. Keying the line to the id is what stops the room reading as one
/// announcement; making the collision impossible would need the sleepers to coordinate, which is a
/// great deal of machinery for a line of flavour.
/// </para>
/// </remarks>
public static class DreamSystem
{
    /// <summary>Five minutes, in pulses.</summary>
    public const long IntervalPulses = 1200;

    /// <summary>The dream table. Public so a test can assert on it without sleeping for an hour.</summary>
    public static IReadOnlyList<string> Lines => Dreams;

    private static readonly string[] Dreams =
    [
        "You dream of electric sheep. There are not many and they are not very electric.",
        "You dream that you are counting something important, and you have lost your place.",
        "You dream of a door you are certain you have opened before, onto the room you are in.",
        "You dream you are carrying something heavy and cannot look down to see what.",
        "You dream of the sea, which you have never seen, and it is a disappointing colour.",
        "You dream that somebody has been waiting to speak to you, very patiently, for years.",
        "You dream you are explaining yourself, at length, to a goat. The goat is unconvinced.",
        "You dream of a road with markers on it, and you are the one setting them.",
        "You dream that you have forgotten how to stand up. It is not distressing, in the dream.",
        "You dream of bread. Just bread. It is the best dream you have had in a while.",
    ];

    /// <summary>
    /// Sends a dream to anyone who has been asleep long enough, and forgets anyone who woke up.
    /// </summary>
    public static void Tick(WorldState world, long pulse)
    {
        ArgumentNullException.ThrowIfNull(world);

        foreach (var actor in world.AllPlayers)
        {
            var character = actor.Character;

            if (character.RestState != CharacterRestState.Sleep)
            {
                // Cleared on waking, so the next sleep starts a fresh five minutes rather than
                // firing immediately on a stale due-time from an hour ago.
                world.ClearDreams(character.Id);
                continue;
            }

            if (world.NextDreamPulse(character.Id) is not { } due)
            {
                // Just fell asleep. The first dream is a full interval away - dropping off and
                // immediately dreaming reads as a bug rather than as sleep.
                world.SetNextDreamPulse(character.Id, pulse + IntervalPulses);
                continue;
            }

            if (pulse < due)
            {
                continue;
            }

            actor.SendText(Dreams[LineFor(character.Id, pulse)], "emote");
            world.SetNextDreamPulse(character.Id, pulse + IntervalPulses);
        }
    }

    /// <summary>
    /// Which line this character dreams at this pulse.
    /// </summary>
    /// <remarks>
    /// Exposed so the choice can be tested as the function it is. Asserting it by putting two
    /// players in a room and comparing what they were sent is sampling a distribution, not checking
    /// a rule — and with ten lines that sample is wrong one time in ten.
    /// </remarks>
    public static int LineFor(Guid characterId, long pulse) =>
        (int)(((uint)characterId.GetHashCode() + (ulong)(pulse / IntervalPulses))
            % (ulong)Dreams.Length);
}
