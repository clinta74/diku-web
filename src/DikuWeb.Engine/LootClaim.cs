using System.Globalization;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;

namespace DikuWeb.Engine;

/// <summary>
/// A short window in which a fresh drop belongs to whoever earned it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem is that loot lands on the floor, not in a corpse.</b> <c>RollLoot</c> spawns
/// straight into the room, and <c>get</c> asked nothing about entitlement — so a bystander who
/// watched the fight and a party that fought it were equally entitled, and being faster to type was
/// the whole of the rule. A corpse container would fix that too, and at ten times the cost: it
/// would have to persist, be looked into, answer <c>get from</c>, and decay. The claim is the part
/// that was actually missing.
/// </para>
/// <para>
/// <b>Who earned it is not a new question.</b> <c>CombatSystem.KillCredit</c> already answers it —
/// the killer plus any party member standing where the mob died — and already hands out the
/// experience and the gold on that basis. Its own remarks warn that the danger here is the same
/// question answered two different ways, so the claim is stamped from that same list rather than
/// recomputed from the party roster. A member who was elsewhere is out of the split and out of the
/// claim together.
/// </para>
/// <para>
/// <b>It has to expire, and that is not a balance choice.</b> Nothing in this codebase sweeps
/// dropped items; they lie in the room until somebody takes them. A claim that never lapsed would
/// turn every drop a party walked away from into furniture that no one could ever pick up — a
/// worse and far quieter bug than the one being fixed. <see cref="Window"/> is the answer to
/// "how long is the fight still yours", and after it the item is loot on the floor like any other.
/// </para>
/// <para>
/// <b>Stamped with a wall clock rather than a pulse.</b> Pulses restart at zero with the server and
/// the instance's <c>state</c> bag does not, so a pulse deadline would come back from a restart
/// either already lapsed or lapsing hours late. A round-trip timestamp survives correctly, and an
/// item that outlived the process still frees up on schedule.
/// </para>
/// </remarks>
public static class LootClaim
{
    /// <summary>How long a drop stays with whoever earned it.</summary>
    /// <remarks>
    /// Long enough to finish the fight, catch your breath and loot without racing; short enough
    /// that a contested spawn is not blocked for the next person by a party that has moved on.
    /// </remarks>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Marks a fresh drop as belonging to the people who earned it.
    /// </summary>
    /// <remarks>
    /// The killer's name is written down beside the ids rather than looked up when the refusal is
    /// worded. Two minutes is long enough for them to quit, and "That is the 's for another
    /// minute" is worse than the rule not existing.
    /// </remarks>
    public static void Stamp(ItemInstance item, IReadOnlyList<Character> earners, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(earners);

        if (earners.Count == 0)
        {
            return;
        }

        item.State[ItemState.LootClaimKey] =
            earners.Select(e => e.Id.ToString()).ToList();
        item.State[ItemState.LootClaimByKey] = earners[0].Name;
        item.State[ItemState.LootClaimUntilKey] =
            (now + Window).ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Why this character may not take this item yet, or null when they may.
    /// </summary>
    /// <remarks>
    /// <b>Named, and with the wait spelled out.</b> A bare "you cannot take that" is
    /// indistinguishable from a bug, and leaves the player with no way to tell whether waiting
    /// helps. Saying whose it is and for how long turns a refusal into something they can act on —
    /// wait, or ask.
    /// </remarks>
    public static string? Refuse(
        ItemInstance item, Guid characterId, DateTimeOffset now, string article)
    {
        if (item is null || ExpiryOf(item) is not { } until || until <= now)
        {
            return null;
        }

        var claimants = JsonBag.Strings(item.State, ItemState.LootClaimKey);
        if (claimants.Count == 0 || claimants.Contains(characterId.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var owner = JsonBag.Text(item.State, ItemState.LootClaimByKey);
        var remaining = Remaining(until - now);

        return owner is null
            ? $"{article} is not yours to take — not for another {remaining}."
            : $"{article} belongs to {owner} for another {remaining}.";
    }

    /// <summary>
    /// Drops the claim once the item has changed hands.
    /// </summary>
    /// <remarks>
    /// Not merely tidiness. Left on the instance, a claim would come back the moment the item was
    /// dropped again — so a player could bank a drop for good by picking it up and putting it down,
    /// and a party member's dropped share would refuse the person it was dropped for.
    /// </remarks>
    public static void Clear(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.State.Remove(ItemState.LootClaimKey);
        item.State.Remove(ItemState.LootClaimByKey);
        item.State.Remove(ItemState.LootClaimUntilKey);
    }

    /// <summary>When the claim lapses, or null when there is not one to read.</summary>
    private static DateTimeOffset? ExpiryOf(ItemInstance item) =>
        JsonBag.Text(item.State, ItemState.LootClaimUntilKey) is { } text &&
        DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until)
            ? until
            : null;

    /// <summary>The wait, in words a player would use.</summary>
    /// <remarks>
    /// Rounded up and never "0 seconds": a refusal that says the wait is over is a refusal that
    /// reads as broken, however briefly it is true.
    /// </remarks>
    private static string Remaining(TimeSpan left)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(left.TotalSeconds));

        if (seconds < 60)
        {
            return Plural(seconds, "second");
        }

        return Plural((int)Math.Ceiling(seconds / 60.0), "minute");
    }

    private static string Plural(int count, string unit) =>
        count == 1 ? $"{count} {unit}" : $"{count} {unit}s";
}
