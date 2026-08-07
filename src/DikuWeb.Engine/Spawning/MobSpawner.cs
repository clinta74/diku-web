using System.Text.Json;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;

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
    public Task<Mob> SpawnAsync(
        MobTemplate template,
        Zone zone,
        global::DikuWeb.Domain.Worlds.World worldEntity,
        RoomKey roomKey,
        bool sentinel = false,
        CancellationToken ct = default,
        Guid? spawnerId = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(worldEntity);

        // Snapshot multipliers at spawn time (PLAN.md §4.4)
        var worldMults = worldEntity.Multipliers;
        var zoneMults = zone.Multipliers;

        // Resolve Health via Strength multiplier
        var baseHealth = GetIntFromStats(template.BaseStats, "health", 40);
        var resolvedHealth = Multipliers.Resolve(
            baseHealth,
            worldMults,
            zoneMults,
            MultiplierType.Strength);

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
            Level = template.Level,
            RoomKey = roomKey.ToString(),
            ResolvedStats = new(template.BaseStats),
            SpawnMultipliers = new()
            {
                ["Strength"] = zoneMults.Strength,
                ["Health"] = zoneMults.Health,
                ["Damage"] = zoneMults.Damage,
                ["Xp"] = zoneMults.Xp,
                ["Gold"] = zoneMults.Gold,
                ["ItemValue"] = zoneMults.ItemValue,
                ["ItemPower"] = zoneMults.ItemPower,
                ["SpawnDensity"] = zoneMults.SpawnDensity,
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
            State = sentinel ? new() { ["sentinel"] = true } : [],
        };

        return Task.FromResult(mob);
    }

    private static int GetIntFromStats(Dictionary<string, object> stats, string key, int defaultValue)
    {
        if (!stats.TryGetValue(key, out var value))
            return defaultValue;

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
