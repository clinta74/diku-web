using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Inhabitants;

/// <summary>
/// What level a mob actually fights at, once its zone has finished scaling it (PLAN.md §4.4, §4.7).
/// </summary>
/// <remarks>
/// <b>A template's level is a label; the multipliers are the fight.</b> Reusing one template across
/// zones of different difficulty is the entire purpose of §4.4 — the same "rat" is a nuisance in
/// Millbrook and a real problem in a zone that doubles its health and damage. Its authored level
/// never changed, so anything that reads <c>Level</c> alone is reading the label rather than the
/// creature, and will happily tell a level 40 that the thing which nearly killed them was beneath
/// their notice.
///
/// <b>Resolved once, at spawn.</b> It sits beside <c>ResolvedXp</c> and <c>ResolvedStats</c> on the
/// mob, for the reason §4.4 gives for all of them: multipliers are snapshotted when the mob comes
/// into the world, so a builder retuning a zone changes what spawns next rather than silently
/// re-levelling everything already standing in it.
///
/// <h4>The curve, and why this one</h4>
/// <para>
/// Combat power is roughly "how long it survives" times "how hard it hits", so a zone scaling both
/// by <c>s</c> makes a mob <c>s²</c> times the problem it was. Level has to move by the square root
/// of that, or the number stops meaning what every other level in the game means.
/// </para>
/// <para>
/// The anchor is the game's own experience curve, which is <em>already</em> quadratic:
/// <c>XpForLevel</c> is <c>1000·L·(L−1)/2</c>, so power ∝ L² is the relationship progression
/// assumes throughout. Deriving levels with the same exponent keeps one idea of what a level is
/// worth rather than introducing a second. It also means the arithmetic composes: four times the
/// power is twice the level, at every level.
/// </para>
/// <para>
/// So <c>effective = level × strength × √(health × damage)</c> — <c>Strength</c> appears
/// un-rooted because it is documented as scaling health <em>and</em> damage together, so it is
/// already the <c>s</c> in <c>s²</c>; <c>Health</c> and <c>Damage</c> are one-sided fine-tuning
/// and take the root.
/// </para>
/// </remarks>
public static class MobLevel
{
    /// <summary>
    /// The level a mob spawned in this zone fights at.
    /// </summary>
    /// <remarks>
    /// Floored at <see cref="Zone.MinLevel"/> as well as scaled. The two say different things and
    /// both are the author's: the multipliers are how hard they made it, and the band is who they
    /// made it for. A zone can declare itself level 40 content without touching a dial, and a
    /// flavour critter dropped into it should not read as prey.
    ///
    /// Not clamped at <see cref="Zone.MaxLevel"/>. A boss authored or scaled above its band is
    /// deliberate, and pulling it down would overrule the author in the one direction they were
    /// explicit about.
    /// </remarks>
    public static int Effective(int templateLevel, Multipliers world, Multipliers zone, int zoneMinLevel)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(zone);

        var strength = (double)(world.Strength * zone.Strength);
        var health = (double)(world.Health * zone.Health);
        var damage = (double)(world.Damage * zone.Damage);

        // A zone that multiplies something by zero or below is authored nonsense rather than an
        // invincible mob, and the square root of a negative is worse than either. Treat the whole
        // scaling as absent and fall back to the label.
        var scale = strength <= 0 || health <= 0 || damage <= 0
            ? 1.0
            : strength * Math.Sqrt(health * damage);

        var scaled = (int)Math.Round(templateLevel * scale, MidpointRounding.AwayFromZero);

        // Never below 1: level 0 is not a level, and a mob at 0 would be beneath a level 1.
        return Math.Max(1, Math.Max(scaled, zoneMinLevel));
    }
}
