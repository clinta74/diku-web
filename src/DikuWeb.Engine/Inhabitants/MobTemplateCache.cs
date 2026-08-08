using DikuWeb.Domain.Inhabitants;

namespace DikuWeb.Engine.Inhabitants;

/// <summary>
/// Cached mob templates for O(1) lookup during gameplay.
/// Loaded at startup and kept current by builder edits, indexed by template key.
/// </summary>
public sealed class MobTemplateCache
{
    private Dictionary<string, MobTemplate> _templates = [];

    public bool IsLoaded { get; private set; }

    public MobTemplate? Get(string key) =>
        _templates.TryGetValue(key, out var template) ? template : null;

    public async Task LoadAsync(IMobTemplateRepository repo, CancellationToken ct)
    {
        var templates = await repo.GetAllAsync(ct);
        _templates = templates.ToDictionary(t => t.Key);
        IsLoaded = true;
    }

    /// <summary>
    /// Inserts or replaces one template, so a builder's save is live without a restart.
    /// </summary>
    /// <remarks>
    /// Shops read this cache while the spawner sweep reads the repository directly, so without
    /// this a saved edit changed what spawned but not what a shopkeeper charged for it.
    /// </remarks>
    public void Put(MobTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        _templates[template.Key] = template;
    }

    public void Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _templates.Remove(key);
    }
}
