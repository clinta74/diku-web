namespace Muwbta.Domain.Abilities;

/// <summary>What is stopping an ability being used, and for how long.</summary>
/// <param name="Source">
/// The ability whose cooldown is running. Usually the one being asked about; a different one when
/// they share a timer, which is the whole reason this carries an ability rather than a number.
/// </param>
public readonly record struct CooldownBlock(Ability Source, long RemainingPulses);

/// <summary>
/// When an ability may be used, counting its own cooldown and any timer it shares.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is stored.</b> The world already records the pulse each ability was last cast on,
/// and a shared timer is just the longest of those among the abilities that share it. Deriving it
/// means there is no second piece of state to clear on logout, none to persist, and none that can
/// drift from what a player actually cast — and it is correct across reconnects, retunes and
/// level-ups without any of those being thought about.
/// </para>
/// <para>
/// It also collapses to exactly the old behaviour when nothing is grouped: an ungrouped ability has
/// no group-mates, so the maximum is taken over itself alone. The ungrouped path is unchanged by
/// construction rather than by inspection.
/// </para>
/// <para>
/// The last-cast pulse arrives as a delegate rather than as a world, because the world lives in the
/// Engine and this rule is worth testing without one.
/// </para>
/// </remarks>
public static class AbilityCooldowns
{
    /// <summary>
    /// Pulses left of this ability's own cooldown. Zero when it is ready.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="lastCastPulse"/> is an ability this character has never cast, which is
    /// not the same as having cast it on pulse 0 — see <c>WorldState.GetAbilityCooldown</c>, where
    /// returning 0 for "never" once made the whole spellbook unusable for its own cooldown after
    /// every restart.
    /// </remarks>
    public static long OwnRemaining(Ability ability, long? lastCastPulse, long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(ability);

        if (lastCastPulse is not { } startedAt)
        {
            return 0;
        }

        var remaining = (startedAt + ability.CooldownPulses) - currentPulse;
        return remaining > 0 ? remaining : 0;
    }

    /// <summary>
    /// The other abilities on this one's timer, or nothing when it shares no timer.
    /// </summary>
    /// <remarks>
    /// Filtered on Path as well as on the number, because the number is only half the identity — see
    /// <see cref="Ability.CooldownGroup"/>. In play the Path filter changes nothing, since a
    /// character only knows one Path's abilities; it matters to the builder, which is looking at all
    /// four at once.
    /// </remarks>
    public static IEnumerable<Ability> GroupMates(Ability ability, IEnumerable<Ability> all)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(all);

        if (ability.CooldownGroup is not { } group)
        {
            return [];
        }

        return all.Where(a =>
            a is not null &&
            a.CooldownGroup == group &&
            a.Path == ability.Path &&
            !string.Equals(a.Key, ability.Key, StringComparison.Ordinal));
    }

    /// <summary>
    /// Why this ability cannot be used yet, or null when it can be.
    /// </summary>
    /// <remarks>
    /// <b>The longest cooldown on the timer wins, and the ability that owns it is named.</b> Naming
    /// it is most of the value: a player refused for a cooldown they can see on screen understands
    /// it, and a player refused for one they cannot see needs telling which ability is responsible.
    /// </remarks>
    public static CooldownBlock? Blocking(
        Ability ability,
        IEnumerable<Ability> all,
        Func<Ability, long?> lastCastPulse,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(lastCastPulse);

        CooldownBlock? longest = null;

        // The ability itself first, so that when it and a group-mate are both cooling for the same
        // number of pulses the refusal is about the thing the player actually typed.
        foreach (var candidate in new[] { ability }.Concat(GroupMates(ability, all)))
        {
            var remaining = OwnRemaining(candidate, lastCastPulse(candidate), currentPulse);

            if (remaining > 0 && remaining > (longest?.RemainingPulses ?? 0))
            {
                longest = new CooldownBlock(candidate, remaining);
            }
        }

        return longest;
    }
}
