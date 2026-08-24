using DikuWeb.Domain.Items;

namespace DikuWeb.Engine;

/// <summary>
/// How long a mob's drop lies in the room before the world takes it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only mob loot decays.</b> An item a player puts down stays where they put it until the
/// server restarts or somebody picks it up — that is the rule, and it is not enforced here but by
/// where the stamp is written: <c>CombatSystem.RollLoot</c> stamps, and nothing else does. An item
/// with no stamp is not on any clock.
/// </para>
/// <para>
/// <b>Picking it up ends the countdown for good</b> (<see cref="Clear"/>). Once a drop has been in
/// someone's hands it is theirs, and putting it back down makes it an ordinary dropped item with
/// an ordinary fate. Without the clear, a stamp already in the past would delete the item the
/// instant it touched the floor again.
/// </para>
/// <para>
/// <b>Nothing respawns in its place.</b> Loot is spawned with no <c>SpawnerId</c>
/// (<see cref="Spawning.ItemSpawner.Spawn"/>'s optional parameter, which <c>RollLoot</c> does not
/// pass), and the spawn sweep counts population by spawner — so decaying a drop cannot be mistaken
/// for a gap the world owes an item.
/// </para>
/// <para>
/// <b>Twenty minutes rather than something shorter</b> because the failure this prevents is
/// cosmetic and the failure it could cause is not: a party that clears a room, rests, and comes
/// back for the pile should still find it. Nothing is racing this.
/// </para>
/// </remarks>
public static class GroundDecay
{
    /// <summary>How long an untouched drop lasts.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    /// <summary>Starts the countdown on a fresh drop.</summary>
    public static void Stamp(ItemInstance item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.State[ItemState.DecaysAtKey] = JsonBag.Stamp(now + Lifetime);
    }

    /// <summary>
    /// Whether this item's time is up.
    /// </summary>
    /// <remarks>
    /// False for anything unstamped, which is every item a player dropped and every item a builder
    /// or a spawner placed. The sweep asks this of the whole world, so the answer for "not loot"
    /// has to be no rather than an exemption somebody has to remember to write.
    /// </remarks>
    public static bool HasExpired(ItemInstance item, DateTimeOffset now) =>
        item is not null &&
        JsonBag.Timestamp(item.State, ItemState.DecaysAtKey) is { } decaysAt &&
        decaysAt <= now;

    /// <summary>Takes the item off the clock, permanently.</summary>
    public static void Clear(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.State.Remove(ItemState.DecaysAtKey);
    }
}
