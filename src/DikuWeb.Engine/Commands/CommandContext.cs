using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
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

    /// <summary>The verb as the player typed it, used in error messages.</summary>
    public required string Verb { get; init; }

    /// <summary>Everything after the verb, trimmed. Empty string when there were no arguments.</summary>
    public required string Argument { get; init; }

    /// <summary>Set by a handler to have the loop remove this player after the command.</summary>
    public LeaveReason? LeaveRequested { get; set; }

    public bool HasArgument => !string.IsNullOrWhiteSpace(Argument);

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
