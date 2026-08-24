using DikuWeb.Domain.Items;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Takes untaken mob loot back out of the world, twenty minutes after it fell.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only sweep of the floor there is.</b> Before this, an item put in a room stayed there for
/// as long as the process lived — mob loot nobody wanted piled up in every farming spot until a
/// restart cleared it, because a restart is the only thing that ever cleared it.
/// </para>
/// <para>
/// <b>It asks the item, not the room.</b> <see cref="GroundDecay.HasExpired"/> is false for
/// anything unstamped, so player drops, builder placements and spawner population are all
/// untouched by construction rather than by a list of exemptions that would go stale. The one
/// place a stamp is written is <c>CombatSystem.RollLoot</c>.
/// </para>
/// <para>
/// <b>It says so.</b> An item disappearing from the room listing with no line to explain it reads
/// as the client losing track, and the fix people reach for is a reload. One sentence to the room
/// is what makes it weather rather than a bug.
/// </para>
/// <para>
/// <b>Once a minute.</b> A twenty-minute deadline does not need finer than that, and this walks
/// every item in the world — including the ones in packs, which is the price of asking the item
/// rather than keeping a second index that could disagree with the first.
/// </para>
/// </remarks>
public static class GroundDecaySystem
{
    /// <summary>Sixty seconds, in pulses.</summary>
    public const int IntervalPulses = 240;

    /// <summary>
    /// Removes every drop whose time is up.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="now">Wall time, because the deadline is stamped in it.</param>
    /// <param name="view">Redraws the rooms that changed. Null in tests that only count items.</param>
    /// <param name="saves">
    /// Told to delete, for the case that does not arise today and would be silent if it did. Loot
    /// is never written to the database, so there is normally no row — but an id that was never
    /// persisted simply matches no rows, and the alternative is a rule that quietly stops holding
    /// the day somebody makes ground items durable.
    /// </param>
    public static void Tick(
        WorldState world, DateTimeOffset now, PlayerView? view = null, IItemSaveQueue? saves = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Gathered before anything is removed: RemoveItem writes to the dictionary this walks.
        List<ItemInstance>? expired = null;

        foreach (var item in world.AllItems)
        {
            if (item.RoomKey is not null && GroundDecay.HasExpired(item, now))
            {
                (expired ??= []).Add(item);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (var item in expired)
        {
            // Read before the removal, which is what clears RoomKey's usefulness to us.
            var roomKey = RoomKey.Parse(item.RoomKey!);

            world.RemoveItem(item);
            saves?.EnqueueDelete(item.Id);

            var prose = NarrationHelper.BuildSentence(item.DisplayName, "crumbles away.");
            foreach (var occupant in world.AwakeIn(roomKey))
            {
                occupant.SendText(prose, "movement");
            }

            view?.RefreshRoom(world, roomKey);
        }
    }
}
