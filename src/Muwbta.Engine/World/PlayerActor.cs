using System.Threading.Channels;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Protocol;

namespace Muwbta.Engine.World;

/// <summary>
/// A character present in the world, plus the runtime state that is not worth persisting.
/// Owned exclusively by the game loop thread (PLAN.md §2.1) and therefore not thread-safe.
/// </summary>
public sealed class PlayerActor
{
    public required Character Character { get; init; }

    /// <summary>
    /// The account's role, carried in so builder commands can be gated without the Engine
    /// reaching for an account store. A player hitting a dangling exit gets "The way is
    /// blocked."; a builder standing in the same spot is offered <c>dig</c> (PLAN.md §7.6).
    /// </summary>
    /// <remarks>
    /// Settable because a promotion or demotion must reach a character already in the world
    /// (PLAN.md §7.7). Only the loop writes it, on a <see cref="Protocol.SetActorRole"/>.
    /// </remarks>
    public AccountRole Role { get; set; } = AccountRole.Player;

    public bool IsBuilder => Role is AccountRole.Builder or AccountRole.Admin;

    /// <summary>
    /// The name as other players see it on anything this character says: staff wear their role.
    /// </summary>
    /// <remarks>
    /// The point is not decoration. Without it a real admin and a level-one Warden named to look
    /// like one were indistinguishable on a tell, and "Admin tells you, 'send me your password'"
    /// had nothing genuine to be compared against. The tag is something only the server can put
    /// on a line — a name is letters only (see the creation regex), so nobody can type the
    /// brackets into one — and the words inside it are reserved as names besides
    /// (<see cref="ReservedNames"/>). Builders are staff for this purpose: what they write
    /// arrives styled as the world, which is a claim of authority too.
    ///
    /// Used where the character speaks or is listed to others - who, tell, say, chat, emote -
    /// and not where they are merely narrated moving about, which is not a claim of anything.
    /// </remarks>
    public string TaggedName => Role switch
    {
        AccountRole.Admin => $"[Admin] {Name}",
        AccountRole.Moderator => $"[Moderator] {Name}",
        AccountRole.Builder => $"[Builder] {Name}",
        _ => Name,
    };

    /// <summary>
    /// When this account's mute expires, or null when it is not muted (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// Settable for the same reason <see cref="Role"/> is: a mute has to reach a character already
    /// playing, which is the only time it matters. Only the loop writes it, on a
    /// <see cref="Protocol.SetActorMute"/>.
    /// </remarks>
    public DateTimeOffset? MutedUntil { get; set; }

    /// <summary>
    /// Whether this player may speak to anyone right now.
    /// </summary>
    /// <remarks>
    /// Compared against the clock rather than cleared on expiry, so a mute lifts itself without a
    /// sweep whose only job is tidiness.
    /// </remarks>
    public bool IsMuted(DateTimeOffset now) => MutedUntil is { } until && until > now;

    /// <summary>Mutable: a reconnect inside the grace window rebinds a new session.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Null while link-dead. Output is dropped rather than buffered indefinitely.</summary>
    public ChannelWriter<OutboundEvent>? Output { get; set; }

    public bool IsLinkDead => Output is null;

    /// <summary>Pulse at which the link dropped, used to expire the grace window.</summary>
    public long LinkDeadSincePulse { get; set; }

    /// <summary>
    /// The last vitals frame this player actually received, so the loop can skip sending an
    /// identical one. Cleared on rebind: a reconnecting client has no state to compare against
    /// and must be told everything.
    /// </summary>
    public VitalsPayload? LastSentVitals { get; set; }

    /// <summary>
    /// The level this player's ability roster was built for, or null before one was ever sent.
    /// </summary>
    /// <remarks>
    /// Levelling is the only thing that changes which abilities a character has, and it happens in
    /// three places - a kill, a quest turn-in, and an admin `set`. Comparing here rather than
    /// pushing from each of them is the argument <see cref="LastSentVitals"/> already makes: there
    /// is no mutation site that can forget to announce itself.
    /// </remarks>
    public int? LastSentAbilityLevel { get; set; }

    /// <summary>
    /// The last group roster this player received, so an unchanged one is not resent.
    /// </summary>
    /// <remarks>
    /// A list rather than the payload record, because a record's generated equality compares a
    /// list by reference and would therefore report every frame as different - which is the whole
    /// of what this exists to avoid. Compared element by element instead; the entries themselves
    /// are records and do have value equality.
    /// </remarks>
    public IReadOnlyList<PartyMemberEntry>? LastSentParty { get; set; }

    /// <summary>
    /// Who last sent this player a tell, so <c>reply</c> has something to answer (PLAN.md §5.3).
    /// Runtime only: a conversation does not outlive the session it happened in.
    /// </summary>
    public Guid? LastTellFrom { get; set; }

    /// <summary>
    /// Whether this player has turned the world channel off. Off means both directions - you do
    /// not read it and you do not post to it - because a channel you can shout into while ignoring
    /// the replies is not one anybody else wants to share.
    /// </summary>
    public bool ChatOff { get; set; }

    public Guid CharacterId => Character.Id;

    public string Name => Character.Name;

    public RoomKey RoomKey => Character.RoomKey;

    /// <summary>Stable id used in map payloads.</summary>
    public string EntityId => $"c_{Character.Id:N}";

    /// <summary>Players are drawn as a person; the viewer sees themselves as '@'.</summary>
    public string Icon => "p";

    /// <summary>
    /// Fire-and-forget. A full or closed channel means the client is gone or hopelessly
    /// behind, and blocking the game loop to wait for it would stall the whole world.
    /// </summary>
    public void Send(OutboundEvent gameEvent) => Output?.TryWrite(gameEvent);

    public void SendText(string text) =>
        Send(new OutboundEvent(EventTypes.Text, TextPayload.Plain(text)));

    public void SendText(string text, string style) =>
        Send(new OutboundEvent(EventTypes.Text, TextPayload.Styled(text, style)));

    public void SendSys(string message, string kind) =>
        Send(new OutboundEvent(EventTypes.Sys, new SysPayload(message, kind)));
}
