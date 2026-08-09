using DikuWeb.Domain.Entities;
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

    /// <summary>Round counter. Increments once per combat tick.</summary>
    public int RoundNumber { get; set; }

    /// <summary>
    /// When each combatant joined the fight. A combatant's first swing comes one full delay
    /// after this, never on the pulse they engage - otherwise fleeing and re-attacking would be
    /// a free opening blow.
    /// </summary>
    public Dictionary<string, long> EngagedAtPulse { get; init; } = [];

    /// <summary>
    /// When each of a combatant's attacks last landed a swing. Absent means it has not swung in
    /// this fight, and readiness falls back to <see cref="EngagedAtPulse"/>.
    /// </summary>
    /// <remarks>
    /// Schedules live on the fight rather than beside the cast queue because their lifetime is
    /// exactly the fight, and <see cref="RemoveCombatant"/> is already the one door every exit
    /// walks through - death, leaving the room, a refused target, fleeing. A world-level map
    /// would need the same cleanup mirrored at each of those plus despawn, logout, and reload,
    /// and a leaked stamp is a silently slowed weapon that no test would notice.
    /// </remarks>
    public Dictionary<(string CombatantId, AttackSlot Slot), long> LastSwingPulse { get; init; } = [];

    /// <summary>Records when a combatant joined, if this is the first time it has been seen.</summary>
    public void MarkEngaged(string entityId, long pulse) =>
        EngagedAtPulse.TryAdd(entityId, pulse);

    /// <summary>When this combatant joined, or 0 if it somehow never was recorded.</summary>
    public long EngagedAt(string entityId) =>
        EngagedAtPulse.TryGetValue(entityId, out var pulse) ? pulse : 0;

    /// <summary>When this attack last swung, or null if it has not swung in this fight.</summary>
    public long? LastSwing(string entityId, AttackSlot slot) =>
        LastSwingPulse.TryGetValue((entityId, slot), out var pulse) ? pulse : null;

    /// <summary>Stamps a swing so the next one is a delay away.</summary>
    public void RecordSwing(string entityId, AttackSlot slot, long pulse) =>
        LastSwingPulse[(entityId, slot)] = pulse;

    /// <summary>Add a combatant to this fight.</summary>
    public void AddCombatant(string entityId)
    {
        if (!Combatants.Contains(entityId))
        {
            Combatants.Add(entityId);

            // Initialize hate list for mobs
            if (EntityId.IsMob(entityId))
            {
                HateLists[entityId] = [];
            }
        }
    }

    /// <summary>Remove a combatant (dead or fled).</summary>
    public void RemoveCombatant(string entityId)
    {
        Combatants.Remove(entityId);
        HateLists.Remove(entityId);

        // Also forget this entity inside everyone else's hate list. Without this a player who
        // fled stayed the mob's top hater: GetTopHater kept naming them, so the mob kept
        // swinging at someone who had just been told they escaped.
        foreach (var hateList in HateLists.Values)
        {
            hateList.Remove(entityId);
        }

        // Remove any player targeting this entity, and this entity's own target if it is a player
        var targetsToRemove = PlayerTargets
            .Where(kvp => kvp.Value == entityId || EntityId.ForCharacter(kvp.Key) == entityId)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var playerId in targetsToRemove)
        {
            PlayerTargets.Remove(playerId);
        }

        // Drop the schedules with the fighter. Re-engaging then costs a fresh delay rather than
        // inheriting a stale stamp that would let an old fight's timing bleed into a new one.
        EngagedAtPulse.Remove(entityId);
        foreach (var key in LastSwingPulse.Keys.Where(k => k.CombatantId == entityId).ToList())
        {
            LastSwingPulse.Remove(key);
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
            if (hateList.Count == 0)
            {
                return null;
            }

            return hateList.OrderByDescending(kvp => kvp.Value).First().Key;
        }
        return null;
    }

    /// <summary>How much this mob hates one attacker. Zero when it has no reason to.</summary>
    public int HateOf(string mobId, string attackerId) =>
        HateLists.TryGetValue(mobId, out var hateList) &&
        hateList.TryGetValue(attackerId, out var hate)
            ? hate
            : 0;

    /// <summary>The highest threat anyone holds against this mob, or zero if nobody does.</summary>
    public int HighestHate(string mobId) =>
        HateLists.TryGetValue(mobId, out var hateList) && hateList.Count > 0
            ? hateList.Values.Max()
            : 0;

    /// <summary>
    /// Puts one attacker at the top of a mob's hate list, ahead of the current leader by
    /// <paramref name="lead"/>, and returns the threat they now hold.
    /// </summary>
    /// <remarks>
    /// The only thing in the game that writes threat rather than adding it, which is what makes a
    /// taunt a taunt: everything else earns its place by dealing damage.
    /// </remarks>
    /// <para>
    /// <b>A lead, not a lock.</b> The list stays a damage meter, so whoever the taunt displaced
    /// climbs back by dealing <paramref name="lead"/> more damage than the taunter does in the
    /// meantime. That is deliberate - a taunt that pinned a mob permanently would delete the
    /// decision it exists to create, and holding threat would stop being something a Warden has
    /// to keep doing.
    /// </para>
    /// <para>
    /// Never lowers anyone. A taunter who is already miles ahead keeps what they had rather than
    /// being dropped to a computed number, so taunting twice cannot cost you the lead.
    /// </para>
    public int ForceTopHater(string mobId, string attackerId, int lead)
    {
        if (!HateLists.TryGetValue(mobId, out var hateList))
        {
            return 0;
        }

        var target = HighestHate(mobId) + Math.Max(1, lead);
        var current = hateList.TryGetValue(attackerId, out var held) ? held : 0;

        hateList[attackerId] = Math.Max(current, target);
        return hateList[attackerId];
    }
}
