using DikuWeb.Domain.Spawning;

namespace DikuWeb.Engine.Spawning;

/// <summary>
/// In-memory copy of every spawner rule, loaded at startup and kept current by builder edits.
/// </summary>
/// <remarks>
/// Spawner rows are read whole, every sweep, forever - the sweep has no key to look up, it needs
/// the entire set. That made it the last per-tick database read left after the template caches
/// landed: one <c>SELECT * FROM spawners</c> every 15 seconds for the life of the process, in a
/// world where the answer only changes when a builder saves.
/// </remarks>
public sealed class SpawnerCache
{
    private readonly Dictionary<Guid, Spawner> _spawners = [];

    public bool IsLoaded { get; private set; }

    /// <summary>Every spawner, in no particular order. The sweep enumerates this.</summary>
    public IReadOnlyCollection<Spawner> All => _spawners.Values;

    public Spawner? Get(Guid id) =>
        _spawners.TryGetValue(id, out var spawner) ? spawner : null;

    public async Task LoadAsync(ISpawnerRepository repository, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _spawners.Clear();
        foreach (var spawner in await repository.GetAllAsync(ct))
        {
            _spawners[spawner.Id] = spawner;
        }

        IsLoaded = true;
    }

    /// <summary>
    /// Inserts or replaces one spawner, so a builder's save is live without a restart.
    /// </summary>
    public void Put(Spawner spawner)
    {
        ArgumentNullException.ThrowIfNull(spawner);
        _spawners[spawner.Id] = spawner;

        // A cache holding spawners is loaded, whoever put them there. See MobTemplateCache.Put:
        // without this, a world whose first spawner is created by a builder rather than read at
        // startup would keep reading through to the database on every sweep.
        IsLoaded = true;
    }

    public void Remove(Guid id) => _spawners.Remove(id);
}
