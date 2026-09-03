using Muwbta.Domain.Quests;

namespace Muwbta.Engine.Quests;

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

    /// <summary>
    /// Inserts or replaces one quest, keeping the giver and turn-in indexes consistent.
    /// </summary>
    /// <remarks>
    /// Called when a builder saves a quest, so the edit is live without a restart (PLAN.md §1,
    /// "live immediate"). Removing first is what makes a *changed* giver or turn-in correct: the
    /// old key's index list must not keep pointing at this quest.
    /// </remarks>
    public void Put(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        Remove(quest.Key);

        _questsByKey[quest.Key] = quest;
        Index(_questsByGiverMobKey, quest.GiverMobKey, quest);
        Index(_questsByTurninMobKey, quest.TurninMobKey, quest);

        // A cache holding quests is loaded, whoever put them there. Without this, a world whose
        // first quest is authored by a builder rather than read at startup keeps reporting itself
        // unloaded, and `talk` answers "Quests are not available" for good.
        IsLoaded = true;
    }

    /// <summary>Removes one quest and drops it from both indexes.</summary>
    public void Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_questsByKey.Remove(key, out var existing))
        {
            return;
        }

        Deindex(_questsByGiverMobKey, existing.GiverMobKey, key);
        Deindex(_questsByTurninMobKey, existing.TurninMobKey, key);
    }

    private static void Index(Dictionary<string, List<Quest>> index, string mobKey, Quest quest)
    {
        if (!index.TryGetValue(mobKey, out var list))
        {
            list = [];
            index[mobKey] = list;
        }

        list.Add(quest);
    }

    private static void Deindex(Dictionary<string, List<Quest>> index, string mobKey, string questKey)
    {
        if (!index.TryGetValue(mobKey, out var list))
        {
            return;
        }

        list.RemoveAll(q => q.Key == questKey);

        // Drop the empty bucket so a renamed giver does not leave a growing tail of empty lists.
        if (list.Count == 0)
        {
            index.Remove(mobKey);
        }
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
