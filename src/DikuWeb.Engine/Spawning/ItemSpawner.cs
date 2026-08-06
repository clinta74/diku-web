using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Engine.Spawning;

/// <summary>
/// Creates an ItemInstance from an ItemTemplate, resolving multipliers and capturing spawn state.
/// PLAN.md §4.4: Resolves using round(base × world × zone) with type-specific clamping.
/// </summary>
public sealed class ItemSpawner
{
    /// <summary>
    /// Spawns a new item with multiplier-resolved stats. Called during spawner sweep
    /// to fill population targets.
    /// </summary>
    public Task<ItemInstance> SpawnAsync(
        ItemTemplate template,
        Zone zone,
        global::DikuWeb.Domain.Worlds.World worldEntity,
        RoomKey roomKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(worldEntity);

        // Snapshot multipliers at spawn time (PLAN.md §4.4)
        var worldMults = worldEntity.Multipliers;
        var zoneMults = zone.Multipliers;

        // Resolve item value via ItemValue multiplier
        var resolvedValue = Multipliers.Resolve(
            template.BaseValue,
            worldMults,
            zoneMults,
            MultiplierType.ItemValue);

        var item = new ItemInstance
        {
            Id = Guid.NewGuid(),
            TemplateKey = template.Key,
            TemplateName = string.IsNullOrEmpty(template.Name) ? template.Key : template.Name,
            Icon = template.Icon,
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
            Value = resolvedValue,
            State = [],
        };

        return Task.FromResult(item);
    }
}
