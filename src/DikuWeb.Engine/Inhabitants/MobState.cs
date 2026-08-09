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
