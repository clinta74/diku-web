using System.Text.Json;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;

namespace DikuWeb.Engine.Spawning;

/// <summary>
/// Creates a Mob instance from a MobTemplate, resolving multipliers and capturing spawn state.
/// PLAN.md §4.4: Resolves using round(base × world × zone) with type-specific clamping.
/// </summary>
public sealed class MobSpawner
{
    /// <summary>
    /// Spawns a new mob with multiplier-resolved stats. Called during spawner sweep
    /// to fill population targets.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose, for the same reason as <see cref="ItemSpawner.Spawn"/>:
    /// nothing here awaits, so a Task only obscured that.
    /// </remarks>
    public Mob Spawn(
        MobTemplate template,
        Zone zone,
        global::DikuWeb.Domain.Worlds.World worldEntity,
        RoomKey roomKey,
        bool wanders = false,
        Guid? spawnerId = null,
        int? fightsAtLevel = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(worldEntity);

        // Snapshot multipliers at spawn time (PLAN.md §4.4)
        var worldMults = worldEntity.Multipliers;
        var zoneMults = zone.Multipliers;

        // How hard this placement is, in one object: the factor on health, the factor on damage,
        // and the level those add up to. Everything combat-shaped below goes through it, so a mob
        // scaled by a zone and one pinned by its spawner travel the same arithmetic.
        //
        // A pin *replaces* the zone's combat dials rather than composing with them (§4.7). The
        // whole reason a spawner states an outcome - "fights at 27" - is that 27 is then what
        // happens; multiplying the zone on top would make it 54 in a doubled zone and the number
        // the builder typed would be a lie. Xp and Gold below are untouched by this: they are not
        // level statements, and a zone that is hard but stingy is a deliberate shape (§4.4).
        var scaling = fightsAtLevel is { } pinned
            ? MobScaling.FromTarget(template.Level, pinned)
            : MobScaling.FromZone(template.Level, worldMults, zoneMults, zone.MinLevel);

        // Health has its own key because Vitals is a column, not part of the stat bag. The default
        // of 40 is for a template that declares no health at all.
        var baseHealth = GetIntFromStats(template.BaseStats, "health", 40);
        var resolvedHealth = Math.Max(
            1,
            (int)Math.Round(baseHealth * scaling.Health, MidpointRounding.AwayFromZero));

        // Resolve Xp
        var resolvedXp = Multipliers.Resolve(
            template.BaseXp,
            worldMults,
            zoneMults,
            MultiplierType.Xp);

        // Resolve Gold
        var resolvedGold = Multipliers.Resolve(
            template.BaseGold,
            worldMults,
            zoneMults,
            MultiplierType.Gold);

        var mob = new Mob
        {
            Id = Guid.NewGuid(),
            TemplateKey = template.Key,
            SpawnerId = spawnerId,
            TemplateName = template.Name,
            Icon = template.Icon,
            Level = template.Level,
            EffectiveLevel = scaling.Level,
            RoomKey = roomKey.ToString(),

            // Actually resolved, which the name has claimed since it was written. It was a verbatim
            // copy of the template, so a zone's damage dial reached nothing at all and its master
            // strength dial made mobs tankier without ever making them hit harder.
            ResolvedStats = scaling.ResolveStats(template.BaseStats),
            SpawnMultipliers = new()
            {
                ["Strength"] = zoneMults.Strength,
                ["Health"] = zoneMults.Health,
                ["Damage"] = zoneMults.Damage,
                ["Xp"] = zoneMults.Xp,
                ["Gold"] = zoneMults.Gold,
                ["ItemValue"] = zoneMults.ItemValue,

                // What was actually applied, when it was not the dials above. Without it the
                // snapshot that exists to answer "why does this kobold have 137 hp?" reports the
                // zone's numbers for a mob the zone did not scale.
                ["FightsAt"] = fightsAtLevel ?? 0,
            },
            ResolvedXp = resolvedXp,
            ResolvedGold = resolvedGold,
            Vitals = new()
            {
                Health = resolvedHealth,
                HealthMax = resolvedHealth,
                Focus = 0,
                FocusMax = 0,
                Stamina = 100,
                StaminaMax = 100,
            },
            State = NewState(wanders, roomKey),
        };

        return mob;
    }

    /// <summary>
    /// The per-mob state a fresh spawn starts with.
    /// </summary>
    /// <remarks>
    /// The home zone is recorded here, in the state bag beside <c>wanders</c>, rather than as a
    /// column on <c>mobs</c> - it is per-instance runtime state, which is what the bag is for, and
    /// it needs no migration. It has to be captured at spawn because the mob's own room moves:
    /// asking "which zone is it in" after it has wandered answers the wrong question.
    ///
    /// <b>Only a wandering mob is marked.</b> The key used to be <c>sentinel</c> and was written
    /// when the mob should stay put, which put the default on the wrong side: a mob whose state
    /// failed to round-trip, or that something built without going through here, came back
    /// wandering. Absence now means standing still, so the failure is a dull room rather than a
    /// quest giver that walked off (<see cref="MobBehavior.Wanders"/>).
    /// </remarks>
    private static Dictionary<string, object> NewState(bool wanders, RoomKey roomKey)
    {
        var state = new Dictionary<string, object> { [MobState.HomeZoneKey] = roomKey.ZoneKey };

        if (wanders)
        {
            state[MobBehavior.WandersKey] = true;
        }

        return state;
    }

    private static int GetIntFromStats(Dictionary<string, object> stats, string key, int defaultValue)
    {
        if (!stats.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            decimal d => (int)d,
            double d => (int)d,
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.Number => je.GetInt32(),
                _ => defaultValue
            },
            _ => defaultValue
        };
    }
}
