namespace DikuWeb.Engine.Abilities;

/// <summary>
/// In-memory cache of all abilities, populated at game loop startup.
/// Allows synchronous ability lookups from the command handler without async repo calls.
/// </summary>
public sealed class AbilityCache
{
    private readonly Dictionary<string, DikuWeb.Domain.Abilities.Ability> _abilities = [];

    public bool IsLoaded { get; private set; }

    /// <summary>Load all abilities from the repository into the cache.</summary>
    public async Task LoadAsync(IAbilityRepository repository, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _abilities.Clear();
        var all = await repository.GetAllAsync(ct);

        foreach (var ability in all)
        {
            _abilities[ability.Key] = ability;
        }

        IsLoaded = true;
    }

    /// <summary>
    /// Inserts or replaces one ability, so a reconcile or a test can populate the cache without
    /// a repository. Marks the cache loaded, for the reason given on <c>MobTemplateCache.Put</c>.
    /// </summary>
    public void Put(DikuWeb.Domain.Abilities.Ability ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        _abilities[ability.Key] = ability;
        IsLoaded = true;
    }

    /// <summary>Get an ability by its key.</summary>
    public DikuWeb.Domain.Abilities.Ability? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _abilities.TryGetValue(key, out var ability);
        return ability;
    }

    /// <summary>Check if an ability key exists.</summary>
    public bool Contains(string key) =>
        !string.IsNullOrEmpty(key) && _abilities.ContainsKey(key);

    /// <summary>Get all abilities (for reference).</summary>
    public IReadOnlyDictionary<string, DikuWeb.Domain.Abilities.Ability> All =>
        _abilities.AsReadOnly();
}
