using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.World;

namespace Muwbta.Engine;

/// <summary>
/// What to call a mob when the room holds more than one of it.
/// </summary>
/// <remarks>
/// <b>Two identical mobs in a room were indistinguishable in every line the game printed.</b>
/// Reported from play as a question nobody could answer: one player attacks a crow, a second crow
/// wanders in, the other player attacks — did they hit the same bird? They did, but the transcript
/// said "a terrace crow" four times and there was no way to tell. An answer that cannot be observed
/// is not much better than a wrong one, and it makes every group-targeting feature untestable by
/// hand, <c>assist</c> included.
///
/// <b>Numbered only when there is something to disambiguate.</b> A lone crow is "a terrace crow",
/// not "a terrace crow (1)" — an ordinal on a thing with no twin is noise on every line in the game
/// for the sake of the rare room where it matters.
///
/// <b>Arrival order, which is the same order <see cref="NameMatch"/> resolves in.</b> That is what
/// makes the label typeable: <c>attack crow 2</c> reaches the mob shown as "(2)" because both read
/// the room's list the same way. Keyed on the display name rather than the template, because two
/// things a player cannot tell apart are what needs numbering — a room holding a "cave rat" and a
/// "barn rat" needs no ordinals at all even though both are rats.
///
/// The number is <em>positional, not an identity</em>. When (1) dies, (2) becomes (1). That is
/// unavoidable without giving mobs permanent per-room names, and it is why <c>assist</c> exists:
/// the ordinal is for reading, and following a person is for aiming.
/// </remarks>
public static class MobLabel
{
    /// <summary>
    /// The label for <paramref name="mob"/> among <paramref name="roomMobs"/>, with an ordinal
    /// only when another mob in the room shows the same name.
    /// </summary>
    public static string For(IReadOnlyList<Mob> roomMobs, Mob mob)
    {
        ArgumentNullException.ThrowIfNull(roomMobs);
        ArgumentNullException.ThrowIfNull(mob);

        var name = mob.DisplayName;

        var ordinal = 0;
        var total = 0;

        foreach (var other in roomMobs)
        {
            if (!string.Equals(other.DisplayName, name, StringComparison.Ordinal))
            {
                continue;
            }

            total++;

            if (other.Id == mob.Id)
            {
                ordinal = total;
            }
        }

        // Also covers a mob that is not in the list it was asked about - a corpse being narrated
        // after removal, say. Naming it plainly beats naming it "(0)".
        return total > 1 && ordinal > 0 ? $"{name} ({ordinal})" : name;
    }

    /// <summary>The label for a mob in whatever room it is standing in.</summary>
    public static string For(WorldState world, Mob mob)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mob);

        return RoomKey.TryParse(mob.RoomKey, out var roomKey)
            ? For(world.MobsIn(roomKey), mob)
            : mob.DisplayName;
    }
}
