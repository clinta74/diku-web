using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.World;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Engine.Spawning;

/// <summary>
/// Runs every 15 seconds to maintain population targets set by Spawners.
/// Queries database for active spawners, checks current population, and calls
/// MobSpawner/ItemSpawner to create new instances as needed.
/// </summary>
public sealed class SpawnerSystem(
    ISpawnerRepository spawners,
    IMobTemplateRepository mobTemplates,
    IItemTemplateRepository itemTemplates,
    MobSpawner mobSpawner,
    ItemSpawner itemSpawner,
    ILogger<SpawnerSystem> logger)
{
    public async Task RunAsync(WorldState world, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(world);

        var allSpawners = await spawners.GetAllAsync(ct);

        foreach (var spawner in allSpawners)
        {
            await ProcessSpawnerAsync(world, spawner, ct);
        }
    }

    private async Task ProcessSpawnerAsync(WorldState world, Spawner spawner, CancellationToken ct)
    {
        if (spawner.TemplateKind == TemplateKind.Mob)
        {
            await ProcessMobSpawnerAsync(world, spawner, ct);
        }
        else if (spawner.TemplateKind == TemplateKind.Item)
        {
            await ProcessItemSpawnerAsync(world, spawner, ct);
        }
    }

    private async Task ProcessMobSpawnerAsync(WorldState world, Spawner spawner, CancellationToken ct)
    {
        var template = await mobTemplates.GetByKeyAsync(spawner.TemplateKey, ct);
        if (template is null)
        {
            EngineLog.SpawnerTemplateNotFound(logger, spawner.Id, spawner.TemplateKey);
            return;
        }

        var zone = world.FindZone(spawner.ZoneKey);
        if (zone is null)
        {
            return;
        }

        var worldEnt = world.FindWorld(zone.WorldKey);
        if (worldEnt is null)
        {
            return;
        }

        var roomKeys = spawner.RoomKeys.Select(RoomKey.Parse).ToList();
        var currentCount = roomKeys.Sum(room => world.MobsIn(room).Count);

        for (int i = currentCount; i < spawner.TargetCount; i++)
        {
            var room = roomKeys[Random.Shared.Next(roomKeys.Count)];
            var mob = await mobSpawner.SpawnAsync(template, zone, worldEnt, room, ct);
            world.AddMob(mob);
        }
    }

    private async Task ProcessItemSpawnerAsync(WorldState world, Spawner spawner, CancellationToken ct)
    {
        var template = await itemTemplates.GetByKeyAsync(spawner.TemplateKey, ct);
        if (template is null)
        {
            EngineLog.SpawnerTemplateNotFound(logger, spawner.Id, spawner.TemplateKey);
            return;
        }

        var zone = world.FindZone(spawner.ZoneKey);
        if (zone is null)
        {
            return;
        }

        var worldEnt = world.FindWorld(zone.WorldKey);
        if (worldEnt is null)
        {
            return;
        }

        var roomKeys = spawner.RoomKeys.Select(RoomKey.Parse).ToList();
        var currentCount = roomKeys.Sum(room => world.ItemsIn(room).Count);

        for (int i = currentCount; i < spawner.TargetCount; i++)
        {
            var room = roomKeys[Random.Shared.Next(roomKeys.Count)];
            var item = await itemSpawner.SpawnAsync(template, zone, worldEnt, room, ct);
            world.AddItem(item);
        }
    }
}
