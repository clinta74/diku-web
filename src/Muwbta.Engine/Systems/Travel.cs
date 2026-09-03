using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Presentation;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Systems;

/// <summary>
/// Moving a character somewhere no exit leads (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// A destination is a <c>world.zone.room</c> like any other, so travelling is a parameter rather
/// than a new kind of link - nothing here needs to know whether the target is in the same world,
/// and an ordinary exit already crosses worlds without help.
///
/// This exists so <c>noRecall</c> has exactly one reader. The flag has been registered since
/// Phase 4 with nothing behind it (§4.10 calls a flag with no reader dead weight), and the way it
/// would have become a lie is a second travel verb landing later that forgot to ask. Any future
/// spell that sends someone somewhere goes through here for the same reason.
///
/// Deliberately not used by the builder's <c>goto</c>, which is a tool for looking at the world
/// from outside it and is documented as ignoring exits. A builder who could be held in place by
/// the content they are editing would have to walk out to fix it.
/// </remarks>
public static class Travel
{
    /// <summary>
    /// Why this character cannot travel out of where they are standing, or null if they can.
    /// </summary>
    public static string? Refuse(WorldState world, Character character, long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(character);

        // Refused rather than offered as an escape, so `flee` stays the one way out of a fight
        // and keeps its cost. Travel is what you do between fights.
        if (character.CombatState == CombatState.Fighting)
        {
            return "You cannot slip away mid-fight. Try 'flee' first.";
        }

        if (RestGate.Refuse(character) is { } resting)
        {
            return resting;
        }

        if (world.IsRooted(character.Id, currentPulse))
        {
            var holding = world.RootName(character.Id, currentPulse) ?? "held fast";
            return $"You cannot go anywhere — you are {holding}.";
        }

        if (world.IsFlagSet(character.RoomKey, RoomFlags.NoRecall))
        {
            return "The way out of this place is not through the world. You must walk.";
        }

        return null;
    }

    /// <summary>
    /// Moves a character to a room nothing connects to, narrating both ends.
    /// </summary>
    /// <remarks>
    /// Checks nothing: the caller decides whether travel is allowed (<see cref="Refuse"/>) and
    /// whether the destination exists. Splitting it that way is what lets one verb refuse with its
    /// own wording while the movement itself - the two room refreshes that are easy to forget -
    /// happens the same way every time.
    /// </remarks>
    public static void To(
        WorldState world,
        PlayerView view,
        PlayerActor actor,
        Room destination,
        string departure,
        string arrival)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(destination);

        var origin = actor.RoomKey;

        foreach (var other in world.OthersAwakeIn(origin, actor))
        {
            other.SendText(departure, "movement");
        }

        // Not walked, so this ends every follow pointed at them (§4.17) — a recall crosses the
        // world and nobody walks behind that. Telling them is the whole reason the list comes back.
        foreach (var dropped in world.Move(actor, destination.Key))
        {
            dropped.SendText($"{actor.Name} vanishes, and you stop following.", "bad");
        }

        foreach (var other in world.OthersAwakeIn(destination.Key, actor))
        {
            other.SendText(arrival, "movement");
        }

        view.SendRoom(world, actor, verbose: true);
        view.RefreshRoom(world, origin);
        view.RefreshRoom(world, destination.Key);
    }
}
