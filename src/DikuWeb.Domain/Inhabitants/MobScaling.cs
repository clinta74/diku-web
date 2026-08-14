using System.Globalization;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Inhabitants;

/// <summary>
/// How hard a particular placement of a template actually is (PLAN.md §4.4, §4.7): the factor on
/// its health, the factor on its damage, and the level those two add up to.
/// </summary>
/// <remarks>
/// <b>One type so that exactly one place knows how a placement is scaled.</b> There are two ways a
/// mob's difficulty is decided — the zone's dials, and a spawner pinning a level outright — and the
/// second one <em>replaces</em> the first rather than composing with it. Expressing that as two
/// factories means <see cref="MobSpawner"/> picks one and never branches again, and it means a mob
/// scaled either way travels the same arithmetic afterwards.
///
/// <b>The resolved stats of a mob at effective level N should look like a mob authored at level
/// N.</b> That is the invariant <see cref="ResolveStats"/> exists to hold. Without it a template
/// that declares its own combat stats scales while one that stays silent does not — the silent one
/// falls back to level-derived defaults in <see cref="DamageCalculator"/>, which do move with the
/// level. Two templates in one zone would then disagree about what the zone did to them, entirely
/// according to which fields their author happened to fill in.
/// </remarks>
public sealed record MobScaling(decimal Health, decimal Damage, int Level)
{
    /// <summary>The zone decides — today's behaviour, and the default for every spawner.</summary>
    public static MobScaling FromZone(
        int templateLevel,
        Multipliers world,
        Multipliers zone,
        int zoneMinLevel)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(zone);

        return new MobScaling(
            // Strength is the master dial and Health fine-tunes on top of it (§4.4). Health used to
            // resolve through Strength alone, which left the Health dial editable and inert.
            Health: world.Strength * zone.Strength * world.Health * zone.Health,
            Damage: world.Strength * zone.Strength * world.Damage * zone.Damage,
            Level: MobLevel.Effective(templateLevel, world, zone, zoneMinLevel));
    }

    /// <summary>
    /// A spawner pinning the level its mobs fight at, whatever the zone says.
    /// </summary>
    /// <remarks>
    /// <b>The exponent is forced, not chosen.</b> Level is linear in the strength dial — a zone at
    /// <c>strength 2</c> produces a mob of twice the level with twice the health and twice the
    /// damage. Inverting that to reach a target level therefore gives exactly one factor,
    /// <c>N / L</c>, applied wherever <c>Strength</c> is applied. Any other exponent would leave two
    /// ways to say "twice as hard" that produce differently-powered mobs wearing the same level,
    /// which is the divergence <see cref="MobLevel"/> exists to close.
    ///
    /// A template level of zero is a real case — the builder API accepts it with no floor — so the
    /// divisor is floored rather than trusted.
    /// </remarks>
    public static MobScaling FromTarget(int templateLevel, int targetLevel)
    {
        var scale = (decimal)targetLevel / Math.Max(1, templateLevel);

        return new MobScaling(Health: scale, Damage: scale, Level: targetLevel);
    }

    /// <summary>
    /// Applies the factors to a template's stat bag, producing the bag the mob actually fights
    /// with.
    /// </summary>
    /// <remarks>
    /// <b>Read through <see cref="StatReader"/>, never by C# type pattern.</b> These values arrive
    /// from jsonb as <c>JsonElement</c>, and code that matched the C# shape was false for every
    /// value that had round-tripped (§12). Anything unreadable is copied across untouched rather
    /// than dropped — a stat this does not understand still belongs to the mob.
    ///
    /// <b>Ratios are not scaled.</b> <c>damageMultiplier</c>, <c>armorMultiplier</c> and
    /// <c>armorPercent</c> are already factors; multiplying a factor by a factor applies the zone
    /// twice. <c>damageMultiplier</c> in particular is applied again by <see cref="DamageCalculator"/>
    /// after this, to whichever dice are in play.
    ///
    /// Defence scales on the health side and to-hit on the damage side, because that is what those
    /// numbers are for — a higher-level mob is harder to hit as well as tougher, which is exactly
    /// what the level-derived fallbacks (<c>level/2</c>, <c>level/3</c>, <c>level/4</c>) already
    /// say.
    /// </remarks>
    public Dictionary<string, object> ResolveStats(IReadOnlyDictionary<string, object>? baseStats)
    {
        var resolved = baseStats is null
            ? []
            : new Dictionary<string, object>(baseStats, StringComparer.Ordinal);

        if (baseStats is null || baseStats.Count == 0)
        {
            return resolved;
        }

        foreach (var key in HealthScaled)
        {
            ScaleInt(baseStats, resolved, key, Health);
        }

        foreach (var key in DamageScaled)
        {
            ScaleInt(baseStats, resolved, key, Damage);
        }

        ScaleRange(baseStats, resolved, "damage", Damage);

        return resolved;
    }

    /// <summary>How tough it is. <c>armorPercent</c> is deliberately absent — it is a ratio.</summary>
    private static readonly string[] HealthScaled = ["health", "defense", "armorFlat"];

    /// <summary>How hard it hits. <c>damageMultiplier</c> is deliberately absent — it is a ratio.</summary>
    private static readonly string[] DamageScaled = ["attackRating", "baseDamage", "damageMin", "damageMax"];

    private static void ScaleInt(
        IReadOnlyDictionary<string, object> source,
        Dictionary<string, object> target,
        string key,
        decimal factor)
    {
        if (StatReader.TryReadInt(source, key, out var value))
        {
            target[key] = Round(value, factor);
        }
    }

    /// <summary>
    /// The <c>"damage": "4-7"</c> form MobTemplate documents, rewritten as a range so it stays the
    /// shape every reader expects.
    /// </summary>
    private static void ScaleRange(
        IReadOnlyDictionary<string, object> source,
        Dictionary<string, object> target,
        string key,
        decimal factor)
    {
        if (!StatReader.TryReadRange(source, key, out var min, out var max))
        {
            return;
        }

        var scaledMin = Round(min, factor);
        var scaledMax = Math.Max(scaledMin, Round(max, factor));

        target[key] = string.Create(CultureInfo.InvariantCulture, $"{scaledMin}-{scaledMax}");
    }

    /// <summary>
    /// Half away from zero, per §4.4, and never below 1 — the floor <c>Multipliers.Resolve</c>
    /// already applies to health and damage, for the same reason: a mob scaled to nothing is not a
    /// mob, it is a crash waiting for a damage roll.
    /// </summary>
    private static int Round(int value, decimal factor) =>
        Math.Max(1, (int)Math.Round(value * factor, MidpointRounding.AwayFromZero));
}
