using DikuWeb.Domain.Quests;

namespace DikuWeb.Engine.Quests;

/// <summary>
/// In-memory cache of all quests, populated at game loop startup.
/// Provides O(1) lookups by key and by giver/turnin mob template keys.
/// </summary>
public sealed class QuestCache
{
    private readonly Dictionary<string, Quest> _questsByKey = [];
    private readonly Dictionary<string, List<Quest>> _questsByGiverMobKey = [];
    private readonly Dictionary<string, List<Quest>> _questsByTurninMobKey = [];

    public bool IsLoaded { get; private set; }

    /// <summary>Load all quests from the repository into the cache and build indexes.</summary>
    public async Task LoadAsync(IQuestRepository repository, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _questsByKey.Clear();
        _questsByGiverMobKey.Clear();
        _questsByTurninMobKey.Clear();

        var all = await repository.GetAllAsync(ct);

        foreach (var quest in all)
        {
            _questsByKey[quest.Key] = quest;

            if (!_questsByGiverMobKey.TryGetValue(quest.GiverMobKey, out var giverList))
            {
                giverList = [];
                _questsByGiverMobKey[quest.GiverMobKey] = giverList;
            }
            giverList.Add(quest);

            if (!_questsByTurninMobKey.TryGetValue(quest.TurninMobKey, out var turninList))
            {
                turninList = [];
                _questsByTurninMobKey[quest.TurninMobKey] = turninList;
            }
            turninList.Add(quest);
        }

        IsLoaded = true;
    }

    /// <summary>Get a quest by its key.</summary>
    public Quest? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _questsByKey.TryGetValue(key, out var quest);
        return quest;
    }

    /// <summary>Get all quests offered by a specific mob template.</summary>
    public IReadOnlyList<Quest> GetByGiverMobKey(string giverMobKey)
    {
        ArgumentNullException.ThrowIfNull(giverMobKey);
        return _questsByGiverMobKey.TryGetValue(giverMobKey, out var quests)
            ? quests.AsReadOnly()
            : [];
    }

    /// <summary>Get all quests that can be turned in to a specific mob template.</summary>
    public IReadOnlyList<Quest> GetByTurninMobKey(string turninMobKey)
    {
        ArgumentNullException.ThrowIfNull(turninMobKey);
        return _questsByTurninMobKey.TryGetValue(turninMobKey, out var quests)
            ? quests.AsReadOnly()
            : [];
    }

    /// <summary>Check if a quest key exists.</summary>
    public bool Contains(string key) =>
        !string.IsNullOrEmpty(key) && _questsByKey.ContainsKey(key);

    /// <summary>Get all quests (for reference).</summary>
    public IReadOnlyDictionary<string, Quest> All =>
        _questsByKey.AsReadOnly();
}
