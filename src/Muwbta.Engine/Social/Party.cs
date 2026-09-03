namespace Muwbta.Engine.Social;

/// <summary>
/// A group of characters adventuring together (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// <b>Session-scoped.</b> A party lives in <see cref="World.WorldState"/> beside combat and
/// active effects, and there is no table behind it: it is a fact about who is in the world right
/// now, and persisting it would raise a question - what a party whose members are all offline
/// means - that nothing in §4.11 or the XP split needs answered.
///
/// Membership is by character id rather than by <see cref="World.PlayerActor"/>, so a link-dead
/// member is still in the group. The grace window (§3.6) leaves the character standing in the
/// room, and a party that dissolved because someone's wifi dropped for ten seconds would be worse
/// than one that waited.
/// </remarks>
public sealed class Party(Guid leaderId)
{
    /// <summary>
    /// Six, the size at which a room description still reads as a room rather than a roster.
    /// Not a balance number - nothing scales off party size yet - so it is a cap on nuisance,
    /// not on power.
    /// </summary>
    public const int MaxMembers = 6;

    private readonly List<Guid> _members = [leaderId];

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Who may invite, remove, and disband. Never outside <see cref="Members"/>.</summary>
    public Guid LeaderId { get; private set; } = leaderId;

    /// <summary>Leader first, then join order - which is the order the roster is shown in.</summary>
    public IReadOnlyList<Guid> Members => _members;

    public int Count => _members.Count;

    public bool IsFull => _members.Count >= MaxMembers;

    public bool Contains(Guid characterId) => _members.Contains(characterId);

    public bool IsLeader(Guid characterId) => LeaderId == characterId;

    public void Add(Guid characterId)
    {
        if (!_members.Contains(characterId))
        {
            _members.Add(characterId);
        }
    }

    /// <summary>
    /// Removes a member and returns whether the party is finished.
    /// </summary>
    /// <remarks>
    /// A party of one is not a party, so dropping to a single member ends it rather than leaving
    /// someone leading themselves - which would be a state you could not tell from being grouped,
    /// and which would quietly change how a helpful area effect gathers its targets.
    ///
    /// When the leader is the one who left, the longest-standing remaining member takes over.
    /// The alternative is a party nobody can invite to or disband, which is what "the leader
    /// logged out" would otherwise mean.
    /// </remarks>
    public bool Remove(Guid characterId)
    {
        _members.Remove(characterId);

        if (LeaderId == characterId && _members.Count > 0)
        {
            LeaderId = _members[0];
        }

        return _members.Count <= 1;
    }
}
