using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Walking behind somebody without typing the direction (PLAN.md §4.17).
/// </summary>
/// <remarks>
/// <b>Group only.</b> Following is a standing licence to be dragged around the world, and the group
/// is where that consent already lives — <c>group invite</c> / <c>group accept</c> is two people
/// agreeing to travel together, which is exactly what this needs and what a bare name in a room
/// does not establish.
///
/// <b>It follows walking, and nothing else.</b> A recall, a portal, a <c>goto</c> or a respawn is
/// not a move anyone could have walked behind, so instead of being silently skipped it
/// <em>ends</em> every follow pointed at that character. A follower left standing while the leader
/// crosses the world is the failure worth making loud, and the alternative — a link that survives —
/// means the next ordinary step teleports somebody. The break itself lives in
/// <see cref="WorldState.Move"/> so that a relocation added later cannot forget it.
///
/// <b>A failed step ends the follow.</b> Locked door, mid-fight, asleep, rooted: the follower stops
/// and is told why, and has to type the verb again. Retrying silently is how somebody ends up
/// several rooms behind and unaware, which is the state this verb exists to prevent.
/// </remarks>
public static class FollowSystem
{
    /// <summary>
    /// Moves everyone following <paramref name="leader"/> out of <paramref name="origin"/> the same
    /// way the leader just went, and everyone following <em>them</em> after that.
    /// </summary>
    /// <remarks>
    /// Only followers standing in the room the leader left. Following is a standing intent rather
    /// than a leash, so somebody who is elsewhere simply does not move — and picks the leader up
    /// again the next time they are in the same room when a step happens.
    ///
    /// The visited set is what makes the recursion provably finite. Cycles are refused when the
    /// verb is typed, but the chain can be re-pointed between one step and the next, and a
    /// termination guarantee that depends on a command handler having been careful is not one.
    /// </remarks>
    public static void Step(
        WorldState world,
        PlayerView view,
        PlayerActor leader,
        RoomKey origin,
        Direction direction,
        long pulse)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(leader);

        var followers = world.FollowersOf(leader.CharacterId);
        if (followers.Count == 0)
        {
            return;
        }

        Propagate(world, view, leader, origin, direction, pulse, [leader.CharacterId]);
    }

    private static void Propagate(
        WorldState world,
        PlayerView view,
        PlayerActor leader,
        RoomKey origin,
        Direction direction,
        long pulse,
        HashSet<Guid> visited)
    {
        foreach (var follower in world.FollowersOf(leader.CharacterId))
        {
            if (!visited.Add(follower.CharacterId) || follower.RoomKey != origin)
            {
                continue;
            }

            if (!TryStep(world, view, follower, leader, origin, direction, pulse))
            {
                continue;
            }

            // Chains: whoever was following the follower comes along too, and they were standing in
            // the same room, so the same origin still applies.
            Propagate(world, view, follower, origin, direction, pulse, visited);
        }
    }

    private static bool TryStep(
        WorldState world,
        PlayerView view,
        PlayerActor follower,
        PlayerActor leader,
        RoomKey origin,
        Direction direction,
        long pulse)
    {
        if (Refuse(world, follower, origin, direction, pulse) is { } refusal)
        {
            world.StopFollowing(follower.CharacterId);
            follower.SendText($"You lose sight of {leader.Name}. {refusal}", "bad");
            return false;
        }

        var destination = world.FindRoom(origin)!.ExitTo(direction)!.ToRoomKey;

        foreach (var other in world.OthersIn(origin, follower))
        {
            other.SendText($"{follower.Name} leaves {direction.ToLowerName()}.", "movement");
        }

        world.Move(follower, destination, walked: true);

        foreach (var other in world.OthersIn(destination, follower))
        {
            other.SendText(
                $"{follower.Name} arrives from the {direction.Opposite().ToLowerName()}.", "movement");
        }

        follower.SendText($"You follow {leader.Name} {direction.ToLowerName()}.", "movement");
        view.SendRoom(world, follower, verbose: false);
        view.RefreshRoom(world, origin);
        view.RefreshRoom(world, destination);

        return true;
    }

    /// <summary>
    /// Why this follower cannot take the step, or null when they can.
    /// </summary>
    /// <remarks>
    /// The same questions <c>Move</c> asks of the person typing a direction, in the same order.
    /// Deliberately re-asked rather than assumed from the leader having passed: the gate that let a
    /// Warden with the key through is exactly the gate that must stop the Temper without it (§4.15),
    /// and a follow that skipped it would be a way to walk anybody past any lock.
    /// </remarks>
    private static string? Refuse(
        WorldState world,
        PlayerActor follower,
        RoomKey origin,
        Direction direction,
        long pulse)
    {
        var character = follower.Character;

        if (character.CombatState == CombatState.Fighting)
        {
            return "You are still fighting.";
        }

        if (RestGate.Refuse(character) is { } resting)
        {
            return resting;
        }

        if (world.IsRooted(character.Id, pulse))
        {
            return $"You are {world.RootName(character.Id, pulse) ?? "held fast"}.";
        }

        // Re-read rather than passed in: the leader's exit and the follower's are the same object
        // today, but the applier can edit a room between two steps of one propagation.
        if (world.FindRoom(origin)?.ExitTo(direction) is not { } exit)
        {
            return "The way is gone.";
        }

        if (ExitGate.Refuse(world, character, exit) is { } barred)
        {
            return barred;
        }

        return world.FindRoom(exit.ToRoomKey) is null ? "The way is blocked." : null;
    }
}
