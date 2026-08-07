using DikuWeb.Domain.Items;

namespace DikuWeb.Engine.Spawning;

/// <summary>
/// In-memory cache of all item templates, populated at game loop startup.
/// Allows synchronous item template lookups from reward systems without async repo calls.
/// </summary>
public sealed class ItemTemplateCache
{
    private readonly Dictionary<string, ItemTemplate> _itemTemplates = [];

    public bool IsLoaded { get; private set; }

    /// <summary>Load all item templates from the repository into the cache.</summary>
    public async Task LoadAsync(IItemTemplateRepository repository, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _itemTemplates.Clear();
        var all = await repository.GetAllAsync(ct);

        foreach (var template in all)
        {
            _itemTemplates[template.Key] = template;
        }

        IsLoaded = true;
    }

    /// <summary>Get an item template by its key.</summary>
    public ItemTemplate? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _itemTemplates.TryGetValue(key, out var template);
        return template;
    }

    /// <summary>Check if an item template key exists.</summary>
    public bool Contains(string key) =>
        !string.IsNullOrEmpty(key) && _itemTemplates.ContainsKey(key);

    /// <summary>Get all item templates (for reference).</summary>
    public IReadOnlyDictionary<string, ItemTemplate> All =>
        _itemTemplates.AsReadOnly();
}
