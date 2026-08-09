using System.Threading.Channels;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Engine.Mutations;

namespace DikuWeb.Engine.Protocol;

/// <summary>
/// Everything that reaches the game loop arrives as one of these, on a single bounded
/// channel. HTTP handlers never touch world state directly (PLAN.md §2.1).
/// </summary>
public abstract record InboundMessage;

/// <summary>
/// Anything originating from a player's session. Builder mutations deliberately sit outside
/// this: a world edit arrives from an HTTP request that has an account behind it but no
/// character in the world, so a session id would be a lie.
/// </summary>
public abstract record SessionMessage : InboundMessage
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
public sealed record EnterWorld : SessionMessage
{
    public required Character Character { get; init; }

    public required ChannelWriter<OutboundEvent> Output { get; init; }

    /// <summary>
    /// Carried in from the account rather than looked up: the Engine has no account store, and
    /// a database read on the loop thread is forbidden anyway (PLAN.md §2.1).
    /// </summary>
    public AccountRole Role { get; init; } = AccountRole.Player;

    /// <summary>
    /// Character's active and completed quests, loaded from the database by the Server.
    /// Empty by default; the Server populates this if quest support is enabled.
    /// </summary>
    public IReadOnlyList<CharacterQuest> Quests { get; init; } = [];

    /// <summary>
    /// Character's inventory and equipped items, loaded from the database by the Server.
    /// Empty by default; the Server populates this on entering the game.
    /// </summary>
    public IReadOnlyList<ItemInstance> Items { get; init; } = [];
}

public sealed record PlayerCommand : SessionMessage
{
    public required string Input { get; init; }
}

public sealed record LeaveWorld : SessionMessage
{
    public required LeaveReason Reason { get; init; }
}

/// <summary>
/// A builder edit, and the one inbound message that answers back (PLAN.md §7.3).
/// </summary>
/// <remarks>
/// Player commands are fire-and-forget because all their output arrives on the SSE stream.
/// A builder saving a room needs to know whether the save succeeded, so this carries a
/// completion the loop resolves after applying. Builder traffic is rare enough that awaiting
/// one pulse (≤250 ms) costs nothing.
/// </remarks>
public sealed record WorldMutation : InboundMessage
{
    public required WorldChange Change { get; init; }

    public required TaskCompletionSource<MutationResult> Completion { get; init; }
}

/// <summary>
/// Delivers a message to one session from outside its own command flow (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// The first outbound path that does not originate from something the player typed. It exists
/// because the admin commands are answered asynchronously - the work happens on a Server worker,
/// and the admin who typed <c>promote</c> still deserves to be told whether it worked. Phase 6's
/// kick and ban need exactly the same thing.
///
/// Addressed by session rather than by character on purpose: the reply belongs to the connection
/// that asked, not to every tab that account has open.
/// </remarks>
public sealed record Notify : SessionMessage
{
    public required string Message { get; init; }

    public string Kind { get; init; } = SysKinds.Info;
}

/// <summary>
/// Updates the cached role on every character an account has in the world (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// <see cref="World.PlayerActor.Role"/> is a copy taken at <see cref="EnterWorld"/>, so without
/// this a promotion would not reach a character already playing until they logged out and back
/// in. Pushing it live matches how a content edit reaches an occupied room (§3.5) - and it
/// matters more in the revoking direction than the granting one.
/// </remarks>
public sealed record SetActorRole : InboundMessage
{
    public required Guid AccountId { get; init; }

    public required AccountRole Role { get; init; }
}

/// <summary>
/// Swaps the entire loaded world for a freshly-read one.
/// </summary>
/// <remarks>
/// Exists for one job: if a mutation applied to memory but then failed to persist, memory is
/// ahead of the database and would stay that way until a restart quietly discarded the edit.
/// The Server reloads from Postgres off-thread and hands the result here, which puts memory
/// back in agreement with durable state. Players are kept - only content is replaced.
/// </remarks>
public sealed record ReplaceWorld : InboundMessage
{
    public required WorldData Data { get; init; }

    public required TaskCompletionSource<bool> Completion { get; init; }
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

    /// <summary>
    /// An admin disconnected them (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Quit"/> because the two are not the same event to anyone reading
    /// the log afterwards, and because a kick is worth saying out loud in the room they were
    /// standing in — a character vanishing with no explanation reads as a bug.
    /// </remarks>
    Kicked = 4,
}
