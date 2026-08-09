namespace DikuWeb.Engine.Social;

/// <summary>Why a join did not happen, or that it did.</summary>
public enum PartyJoinResult
{
    Joined = 0,

    /// <summary>Nobody asked, or the ask has expired.</summary>
    NoInvitation = 1,

    /// <summary>Already in a party. Leave that one first, so leaving is always deliberate.</summary>
    AlreadyGrouped = 2,

    /// <summary>Whoever asked has left the world, or left the party they asked you into.</summary>
    InviterGone = 3,

    /// <summary>The party filled up while the invitation sat unanswered.</summary>
    Full = 4,
}

/// <summary>A standing invitation, and when it stops standing.</summary>
public sealed record PartyInvitation(Guid InviterId, Guid InviteeId, long ExpiresAtPulse);

/// <summary>
/// Every party in the world, and who is in which (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// Owned by <see cref="World.WorldState"/> and therefore by the game-loop thread, like everything
/// else it holds. Indexed by character rather than by party, because every question actually asked
/// of it - <em>are these two on the same side?</em>, <em>who shares this kill?</em> - starts from
/// a character and would otherwise be a scan.
/// </remarks>
public sealed class PartyRegistry
{
    /// <summary>
    /// One minute. Long enough to finish the fight you were in when the offer arrived, short
    /// enough that an invitation you forgot about cannot pull you into a group later.
    /// </summary>
    public const int InvitationLifetimePulses = 240;

    private readonly Dictionary<Guid, Party> _byCharacter = [];
    private readonly Dictionary<Guid, PartyInvitation> _invitations = [];

    /// <summary>This character's party, or null when they are adventuring alone.</summary>
    public Party? Of(Guid characterId) => _byCharacter.GetValueOrDefault(characterId);

    public bool IsGrouped(Guid characterId) => _byCharacter.ContainsKey(characterId);

    /// <summary>
    /// Whether these two are on the same side.
    /// </summary>
    /// <remarks>
    /// The question §4.11 asks before every hostile action between players, and the reason
    /// <em>"party members are never valid targets"</em> stopped being a comment describing nothing.
    /// A character is not their own party member here: naming yourself is answered elsewhere, and
    /// returning true would make an ungrouped player un-targetable by their own area effects for
    /// a different reason than the one intended.
    /// </remarks>
    public bool SameParty(Guid a, Guid b) =>
        a != b && _byCharacter.TryGetValue(a, out var party) && party.Contains(b);

    /// <summary>
    /// Everyone in this character's party, themselves included. A single-element list when they
    /// are ungrouped - so a caller splitting a reward or gathering allies needs no special case
    /// for the solo player, which is the common one.
    /// </summary>
    public IReadOnlyList<Guid> MembersOf(Guid characterId) =>
        _byCharacter.TryGetValue(characterId, out var party)
            ? party.Members
            : [characterId];

    /// <summary>Records an offer. A second offer to the same character replaces the first.</summary>
    public void Invite(Guid inviterId, Guid inviteeId, long currentPulse) =>
        _invitations[inviteeId] = new PartyInvitation(
            inviterId,
            inviteeId,
            currentPulse + InvitationLifetimePulses);

    /// <summary>The offer this character may still accept, or null.</summary>
    public PartyInvitation? InvitationFor(Guid inviteeId, long currentPulse) =>
        _invitations.TryGetValue(inviteeId, out var invitation) &&
        invitation.ExpiresAtPulse > currentPulse
            ? invitation
            : null;

    public bool Decline(Guid inviteeId) => _invitations.Remove(inviteeId);

    /// <summary>
    /// Accepts a standing invitation, forming the party if the inviter was alone.
    /// </summary>
    /// <remarks>
    /// The party is resolved from the inviter at <em>accept</em> time rather than recorded when
    /// the offer was made. An invitation is a message from a person, not a ticket to a group: if
    /// they have since joined someone else, that is the party you are being let into.
    /// </remarks>
    public PartyJoinResult Accept(Guid inviteeId, Func<Guid, bool> isPresent, long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(isPresent);

        if (InvitationFor(inviteeId, currentPulse) is not { } invitation)
        {
            return PartyJoinResult.NoInvitation;
        }

        if (_byCharacter.ContainsKey(inviteeId))
        {
            return PartyJoinResult.AlreadyGrouped;
        }

        if (!isPresent(invitation.InviterId))
        {
            _invitations.Remove(inviteeId);
            return PartyJoinResult.InviterGone;
        }

        var party = _byCharacter.GetValueOrDefault(invitation.InviterId);

        if (party is null)
        {
            party = new Party(invitation.InviterId);
            _byCharacter[invitation.InviterId] = party;
        }
        else if (party.IsFull)
        {
            return PartyJoinResult.Full;
        }

        party.Add(inviteeId);
        _byCharacter[inviteeId] = party;
        _invitations.Remove(inviteeId);

        return PartyJoinResult.Joined;
    }

    /// <summary>
    /// Takes a character out of their party, dissolving it if too few are left.
    /// </summary>
    /// <returns>The party they were in, already updated, or null if they were not in one.</returns>
    public Party? Leave(Guid characterId)
    {
        if (!_byCharacter.TryGetValue(characterId, out var party))
        {
            return null;
        }

        _byCharacter.Remove(characterId);

        if (party.Remove(characterId))
        {
            Disband(party);
        }

        return party;
    }

    /// <summary>Ends a party outright, releasing everyone still in it.</summary>
    public IReadOnlyList<Guid> Disband(Party party)
    {
        ArgumentNullException.ThrowIfNull(party);

        var released = party.Members.ToList();

        foreach (var member in released)
        {
            if (_byCharacter.TryGetValue(member, out var theirs) && theirs.Id == party.Id)
            {
                _byCharacter.Remove(member);
            }
        }

        return released;
    }

    /// <summary>
    /// Forgets a character entirely - their party and any invitation waiting for them.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="World.WorldState.Remove"/>, which is the one door out of the world,
    /// so there is no exit - quit, shutdown, or an expired link-dead window - that leaves a party
    /// holding a character who is not in it. Going link-dead deliberately does not come through
    /// here: that character is still standing in the room and still in the group.
    /// </remarks>
    public Party? Forget(Guid characterId)
    {
        _invitations.Remove(characterId);

        // Any offer this character made is dropped too - accepting it would name a party whose
        // inviter is gone, which Accept would refuse anyway, less clearly and one step later.
        foreach (var (invitee, invitation) in _invitations.ToList())
        {
            if (invitation.InviterId == characterId)
            {
                _invitations.Remove(invitee);
            }
        }

        return Leave(characterId);
    }
}
