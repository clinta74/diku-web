using System.Threading.Channels;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Protocol;

namespace DikuWeb.Engine.World;

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
