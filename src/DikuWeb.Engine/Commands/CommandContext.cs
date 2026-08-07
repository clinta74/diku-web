using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Everything a command handler is allowed to touch. Note the absence of
/// RoomLayoutService: handlers reach presentation only through <see cref="View"/>, so no
/// rule can ever branch on where something is drawn (PLAN.md §4.2).
/// </summary>
public sealed class CommandContext
{
    public required PlayerActor Actor { get; init; }

    public required WorldState World { get; init; }

    public required PlayerView View { get; init; }

    /// <summary>
    /// The world-editing path for builder commands (PLAN.md §7.6). Null in contexts that do
    /// not permit editing at all, which is why <see cref="Edit"/> refuses rather than throws.
    /// </summary>
    public LoopWorldEditor? Editor { get; init; }

    /// <summary>
    /// Where account administration is handed off (PLAN.md §7.7). Null in contexts that do not
    /// permit it, which is why the admin commands check rather than assume.
    /// </summary>
    public IAccountAdminQueue? AdminQueue { get; init; }

    /// <summary>Where items are handed off to be persisted. Null if item saving is not available.</summary>
    public IItemSaveQueue? ItemSaveQueue { get; init; }

    /// <summary>
    /// Item templates, for handlers that need an item's declared slot or description - an
    /// <see cref="Domain.Items.ItemInstance"/> caches only its key. Null if unavailable.
    /// </summary>
    public ItemTemplateCache? ItemTemplates { get; init; }

    /// <summary>Applies a content edit and queues it for persistence.</summary>
    public MutationResult Edit(WorldChange change) =>
        Editor is null
            ? MutationResult.Fail(MutationError.Invalid, "World editing is not available here.")
            : Editor.Apply(change, Actor.Character.AccountId);

    /// <summary>The verb as the player typed it, used in error messages.</summary>
    public required string Verb { get; init; }

    /// <summary>Everything after the verb, trimmed. Empty string when there were no arguments.</summary>
    public required string Argument { get; init; }

    /// <summary>Set by a handler to have the loop remove this player after the command.</summary>
    public LeaveReason? LeaveRequested { get; set; }

    /// <summary>Rooms marked for refresh after command completes.</summary>
    internal HashSet<RoomKey> RoomsToRefresh { get; } = [];

    public bool HasArgument => !string.IsNullOrWhiteSpace(Argument);

    /// <summary>Mark a room to be refreshed for all occupants after this command completes.</summary>
    public void MarkRoomForRefresh(RoomKey roomKey) => RoomsToRefresh.Add(roomKey);

    public void Reply(string text) => Actor.SendText(text);

    public void Reply(string text, string style) => Actor.SendText(text, style);

    /// <summary>Sends to everyone else in the actor's room.</summary>
    public void Broadcast(string text, string? style = null)
    {
        foreach (var other in World.OthersIn(Actor.RoomKey, Actor))
        {
            if (style is null)
            {
                other.SendText(text);
            }
            else
            {
                other.SendText(text, style);
            }
        }
    }
}
