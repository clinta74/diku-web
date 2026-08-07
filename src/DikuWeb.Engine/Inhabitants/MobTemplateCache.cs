using DikuWeb.Domain.Inhabitants;

namespace DikuWeb.Engine.Inhabitants;

/// <summary>
/// Cached mob templates for O(1) lookup during gameplay.
/// Loaded once at startup, indexed by template key.
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
}
