using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.World;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Engine.Spawning;

/// <summary>
/// Runs every 15 seconds to maintain population targets set by Spawners.
/// Reads the spawner rules and templates out of memory, checks current population, and calls
/// MobSpawner/ItemSpawner to create new instances as needed.
/// </summary>
public sealed class SpawnerSystem(
    ISpawnerRepository spawners,
    IMobTemplateRepository mobTemplates,
    IItemTemplateRepository itemTemplates,
    MobSpawner mobSpawner,
    ItemSpawner itemSpawner,
    ILogger<SpawnerSystem> logger,
    PlayerView? view = null,
    MobTemplateCache? mobTemplateCache = null,
    ItemTemplateCache? itemTemplateCache = null,
    SpawnerCache? spawnerCache = null)
{
    /// <summary>
    /// The templates a spawner fills from, out of memory where possible.
    /// </summary>
    /// <remarks>
    /// The sweep used to call <c>GetByKeyAsync</c> per spawner, per kind, every 60 pulses, each
    /// opening a fresh <c>DbContext</c> for a single-row read that nothing memoized between
    /// calls. The caches hold every template, load at boot, and are kept live by the applier -
    /// this system predates them.
    ///
    /// Gated on <c>IsLoaded</c> rather than falling back per miss, for the reason spelled out in
    /// <c>MobAiSystem.TemplateForAsync</c>: a spawner pointing at a template a builder deleted
    /// misses on every sweep forever, so a per-miss fallback would keep issuing the doomed query
    /// in precisely the case where it can never succeed.
    /// </remarks>
    private async Task<MobTemplate?> MobTemplateAsync(string key, CancellationToken ct) =>
        mobTemplateCache is { IsLoaded: true }
            ? mobTemplateCache.Get(key)
            : await mobTemplates.GetByKeyAsync(key, ct);

    /// <inheritdoc cref="MobTemplateAsync"/>
    private async Task<ItemTemplate?> ItemTemplateAsync(string key, CancellationToken ct) =>
        itemTemplateCache is { IsLoaded: true }
            ? itemTemplateCache.Get(key)
            : await itemTemplates.GetByKeyAsync(key, ct);

    /// <summary>
    /// The spawner rules to enforce this sweep, out of memory where possible.
    /// </summary>
    /// <remarks>
    /// Unlike the templates there is no key to miss on - the sweep wants the whole set - so the
    /// only reason to touch the database is a cache that was never loaded at all.
    ///
    /// Copied rather than enumerated live: the sweep is fire-and-forget, so a template read that
    /// does fall through to the database parks it on the thread pool, and a builder saving a
    /// spawner on the loop thread meanwhile would otherwise invalidate the enumerator underneath
    /// it. One list of a few dozen references every 15 seconds is not the cost being avoided here.
    /// </remarks>
    private async Task<IReadOnlyList<Spawner>> SpawnersAsync(CancellationToken ct) =>
        spawnerCache is { IsLoaded: true }
            ? [.. spawnerCache.All]
            : await spawners.GetAllAsync(ct);

    public async Task RunAsync(WorldState world, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(world);

        try
        {
            var allSpawners = await SpawnersAsync(ct);
            EngineLog.SpawnerSweepStarting(logger, allSpawners.Count);

            foreach (var spawner in allSpawners)
            {
                await ProcessSpawnerAsync(world, spawner, ct);
            }
        }
        catch (Exception ex)
        {
            EngineLog.SpawnerSweepFailed(logger, ex);
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
        try
        {
            var template = await MobTemplateAsync(spawner.TemplateKey, ct);
            if (template is null)
            {
                EngineLog.SpawnerTemplateNotFound(logger, spawner.Id, spawner.TemplateKey);
                return;
            }

            var zone = world.FindZone(spawner.ZoneKey);
            if (zone is null)
            {
                EngineLog.SpawnerZoneNotFound(logger, spawner.Id, spawner.ZoneKey);
                return;
            }

            var worldEnt = world.FindWorld(zone.WorldKey);
            if (worldEnt is null)
            {
                EngineLog.SpawnerWorldNotFound(logger, spawner.Id, zone.WorldKey);
                return;
            }

            var roomKeys = spawner.RoomKeys.Select(RoomKey.Parse).ToList();

            // Counted across the world rather than across this spawner's own rooms. A mob that
            // wandered one room east used to stop counting, so the spawner replaced it - and the
            // replacement wandered off as well. A spawner set to three could fill a zone.
            var currentCount = world.MobsFromSpawner(spawner.Id);

            EngineLog.SpawnerPopulation(
                logger, spawner.Id, spawner.TemplateKey, currentCount, spawner.TargetCount, roomKeys.Count);

            var touched = new HashSet<RoomKey>();

            // The template says what this mob normally does; the spawner overrides it for these
            // placements and has the last word (PLAN.md §4.8). Both are phrased as "wanders", so
            // the resolution is a coalesce rather than a polarity flip.
            var wanders = spawner.Wanders ?? MobBehavior.Wanders(template.Behavior);

            for (var i = currentCount; i < spawner.TargetCount; i++)
            {
                var room = roomKeys[Random.Shared.Next(roomKeys.Count)];
                var mob = mobSpawner.Spawn(
                    template, zone, worldEnt, room, wanders, spawner.Id, spawner.FightsAtLevel);
                world.AddMob(mob);
                touched.Add(room);

                // Narrate spawn to room occupants. `mob.DisplayName`, not a hand-written copy of
                // the same fallback - which is the duplication Mob.DisplayName exists to end, and
                // this was one of three surviving copies (BUGS.md #14).
                var prose = NarrationHelper.BuildSentence(mob.DisplayName, "appears.");
                foreach (var occupant in world.OccupantsOf(room))
                {
                    occupant.SendText(prose, "arrival");
                }

                EngineLog.SpawnerSpawned(logger, spawner.Id, spawner.TemplateKey, room);
            }

            // The prose alone leaves the map and contents panels stale - the rat is announced
            // but invisible until the player moves. Refresh once per room rather than once per
            // mob, so a spawner filling three slots in one room sends one update, not three.
            foreach (var room in touched)
            {
                view?.RefreshRoom(world, room);
            }
        }
        catch (Exception ex)
        {
            EngineLog.MobSpawnerFailed(logger, spawner.Id, ex);
        }
    }

    private async Task ProcessItemSpawnerAsync(WorldState world, Spawner spawner, CancellationToken ct)
    {
        var template = await ItemTemplateAsync(spawner.TemplateKey, ct);
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

        // Deliberately NOT the world-wide count mobs now use. An item spawner is a resupply point
        // rather than a population cap: take the herb and another grows, which only works if what
        // is in your pack has stopped counting. A mob spawner asks "how many of these exist" and
        // an item spawner asks "is the shelf stocked" - the same field, two questions.
        var roomKeys = spawner.RoomKeys.Select(RoomKey.Parse).ToList();
        var currentCount = roomKeys.Sum(room => world.ItemsIn(room).Count(i => i.SpawnerId == spawner.Id));

        var touched = new HashSet<RoomKey>();

        for (var i = currentCount; i < spawner.TargetCount; i++)
        {
            var room = roomKeys[Random.Shared.Next(roomKeys.Count)];
            var item = itemSpawner.Spawn(template, zone, worldEnt, room, spawner.Id);
            world.AddItem(item);
            touched.Add(room);

            // Narrate spawn to room occupants. See the mob branch above: ItemInstance carries the
            // same DisplayName property for the same reason.
            var prose = NarrationHelper.BuildSentence(item.DisplayName, "appears.");
            foreach (var occupant in world.OccupantsOf(room))
            {
                occupant.SendText(prose, "arrival");
            }
        }

        // Same as mobs: without this the item is announced but missing from the map and the
        // room contents until something else forces a redraw.
        foreach (var room in touched)
        {
            view?.RefreshRoom(world, room);
        }
    }
}
