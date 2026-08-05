using System.Threading.Channels;
using DikuWeb.Domain.Characters;

namespace DikuWeb.Engine.Protocol;

/// <summary>
/// Everything that reaches the game loop arrives as one of these, on a single bounded
/// channel. HTTP handlers never touch world state directly (PLAN.md §2.1).
/// </summary>
public abstract record InboundMessage
{
    public required Guid SessionId { get; init; }
}

/// <summary>
/// Puts a character into the world. The Server creates the channel and keeps the reader for
/// its SSE writer, handing the writer over here - so no acknowledgement round trip is needed
/// just to obtain a stream.
/// </summary>
/// <remarks>
/// Also handles reconnection. If the character is already present the loop rebinds it to the
/// new session rather than creating a second actor, which is what makes the link-dead grace
/// window work and what stops two browser tabs from cloning a character.
/// </remarks>
public sealed record EnterWorld : InboundMessage
{
    public required Character Character { get; init; }

    public required ChannelWriter<OutboundEvent> Output { get; init; }
}

public sealed record PlayerCommand : InboundMessage
{
    public required string Input { get; init; }
}

public sealed record LeaveWorld : InboundMessage
{
    public required LeaveReason Reason { get; init; }
}

public enum LeaveReason
{
    /// <summary>The player typed "quit". Saves and removes immediately.</summary>
    Quit = 0,

    /// <summary>
    /// The SSE stream dropped. The character stays in the world for the grace window and
    /// can still be attacked - classic MUD risk (PLAN.md §3.6).
    /// </summary>
    LinkDead = 1,

    /// <summary>The grace window expired without a reconnect.</summary>
    LinkDeadExpired = 2,

    /// <summary>Server shutting down.</summary>
    Shutdown = 3,
}
