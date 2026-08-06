using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Combat;

/// <summary>
/// An active combat instance in a room. Tracks all participants and hate lists.
/// One combat per room; multiple combatants can fight simultaneously.
/// </summary>
public sealed class Combat
{
    /// <summary>The room where this combat is occurring.</summary>
    public required RoomKey RoomKey { get; init; }

    /// <summary>All participants: character IDs and mob IDs together.</summary>
    public List<string> Combatants { get; init; } = [];

    /// <summary>
    /// Hate list per mob. Key is mob entity ID (m_<guid>), value is damage dealt.
    /// Players don't have hate lists; they attack their chosen target.
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> HateLists { get; init; } = [];

    /// <summary>
    /// Target chosen by each player. Key is character ID, value is target entity ID.
    /// </summary>
    public Dictionary<Guid, string> PlayerTargets { get; init; } = [];

    /// <summary>Round counter. Increments every 8 pulses (2 seconds).</summary>
    public int RoundNumber { get; set; }

    /// <summary>Track pulses until next round fires (8 pulse cycle).</summary>
    public int PulsesUntilNextRound { get; set; } = 8;

    /// <summary>Add a combatant to this fight.</summary>
    public void AddCombatant(string entityId)
    {
        if (!Combatants.Contains(entityId))
        {
            Combatants.Add(entityId);

            // Initialize hate list for mobs
            if (entityId.StartsWith("m_"))
            {
                HateLists[entityId] = [];
            }
        }
    }

    /// <summary>Remove a combatant (dead or fled).</summary>
    public void RemoveCombatant(string entityId)
    {
        Combatants.Remove(entityId);
        if (HateLists.Remove(entityId))
        {
            // Removed a mob's hate list
        }
        // Remove any player targeting this entity
        var targetsToRemove = PlayerTargets.Where(kvp => kvp.Value == entityId).Select(kvp => kvp.Key).ToList();
        foreach (var playerId in targetsToRemove)
        {
            PlayerTargets.Remove(playerId);
        }
    }

    /// <summary>Add damage to a mob's hate list from an attacker.</summary>
    public void AddToHateList(string mobId, string attackerId, int damage)
    {
        if (HateLists.TryGetValue(mobId, out var hateList))
        {
            if (!hateList.ContainsKey(attackerId))
            {
                hateList[attackerId] = 0;
            }
            hateList[attackerId] += damage;
        }
    }

    /// <summary>Get the top hater for a mob (highest damage dealt).</summary>
    public string? GetTopHater(string mobId)
    {
        if (HateLists.TryGetValue(mobId, out var hateList))
        {
            if (hateList.Count == 0) return null;
            return hateList.OrderByDescending(kvp => kvp.Value).First().Key;
        }
        return null;
    }
}
