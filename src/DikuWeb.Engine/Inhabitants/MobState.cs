using DikuWeb.Domain.Inhabitants;

namespace DikuWeb.Engine.Inhabitants;

/// <summary>
/// The per-mob state bag's vocabulary — what one instance knows about itself.
/// </summary>
/// <remarks>
/// Distinct from <see cref="MobBehavior"/>, which reads the <em>template</em>: that says what
/// every rat does, this says what <em>this</em> rat has done. Named here for the same reason the
/// behavior keys are, and read through <see cref="JsonBag"/> for the same reason too — the bag is
/// jsonb, so a value written as a string comes back as a <c>JsonElement</c>.
/// </remarks>
public static class MobState
{
    /// <summary>The zone this mob was spawned into, which bounds where it may wander.</summary>
    public const string HomeZoneKey = "homeZone";

    /// <summary>The pulse each of this mob's emotes is next due on, keyed by the line itself.</summary>
    /// <remarks>
    /// Keyed by text rather than by position in the template's list, so adding, removing, or
    /// reordering emotes in the builder does not shift every schedule onto the wrong line.
    /// </remarks>
    public const string EmoteScheduleKey = "emoteNext";

    /// <summary>The pulse this mob may next try to wander on.</summary>
    /// <remarks>
    /// A deadline, rather than the <c>lastWanderPulse</c> stamp it replaces. The gap between two
    /// moves is now drawn per move, so there is no longer one interval to add a stamp to. Mobs are
    /// respawned from scratch at boot, so the old key needs no migration - it simply stops being
    /// written.
    /// </remarks>
    public const string WanderNextKey = "wanderNext";

    /// <summary>
    /// When this mob may next try to wander, or null when it has never been scheduled.
    /// </summary>
    /// <remarks>
    /// Null is not zero. A mob with no schedule is one the AI has not seen yet, and it has to be
    /// scheduled rather than moved - reading absence as "due now" is exactly what had every mob in
    /// a room leaving together on the first sweep after it spawned, and re-arming from that same
    /// pulse forever after.
    /// </remarks>
    public static long? WanderNextOf(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        return mob.State.ContainsKey(WanderNextKey)
            ? JsonBag.Int64(mob.State, WanderNextKey)
            : null;
    }

    /// <inheritdoc cref="WanderNextOf"/>
    public static void SetWanderNext(Mob mob, long pulse)
    {
        ArgumentNullException.ThrowIfNull(mob);
        mob.State[WanderNextKey] = pulse;
    }

    /// <summary>
    /// This mob's emote schedule, as a map the caller may edit in place.
    /// </summary>
    /// <remarks>
    /// A copy rather than a live view: the stored value may have arrived from jsonb as a
    /// <c>JsonElement</c>, which is not writable. <see cref="SetEmoteSchedule"/> puts it back.
    /// </remarks>
    public static Dictionary<string, long> EmoteScheduleIn(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var schedule = new Dictionary<string, long>(StringComparer.Ordinal);

        if (JsonBag.AsBag(mob.State.GetValueOrDefault(EmoteScheduleKey)) is not { } stored)
        {
            return schedule;
        }

        foreach (var (text, _) in stored)
        {
            schedule[text] = JsonBag.Int64(stored, text);
        }

        return schedule;
    }

    /// <summary>
    /// Stores the schedule, keeping only the lines the template still carries.
    /// </summary>
    /// <remarks>
    /// Pruning matters because the key is the emote text: editing a typo in the builder makes a
    /// new key, and without this every correction would leave its predecessor behind in the state
    /// of every mob ever spawned from that template.
    /// </remarks>
    public static void SetEmoteSchedule(
        Mob mob,
        IReadOnlyDictionary<string, long> schedule,
        IEnumerable<string> keep)
    {
        ArgumentNullException.ThrowIfNull(mob);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(keep);

        var live = keep.ToHashSet(StringComparer.Ordinal);
        var kept = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var (text, pulse) in schedule)
        {
            if (live.Contains(text))
            {
                kept[text] = pulse;
            }
        }

        if (kept.Count == 0)
        {
            mob.State.Remove(EmoteScheduleKey);
            return;
        }

        mob.State[EmoteScheduleKey] = kept;
    }

    /// <summary>
    /// The zone this mob calls home, or null when it has never been told.
    /// </summary>
    /// <remarks>
    /// Null for mobs that were already in the database before the key existed. Callers must
    /// resolve that to the zone the mob is standing in rather than to "anywhere" — absence has to
    /// mean the confining value, the same way an absent room flag resolves to the safe one
    /// (§4.10). Reading it as "no restriction" would set every existing mob loose on the next
    /// restart, which is exactly the behaviour this key was added to stop.
    /// </remarks>
    public static string? HomeZoneOf(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);
        return JsonBag.Text(mob.State, HomeZoneKey);
    }
}
