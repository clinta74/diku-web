using DikuWeb.Engine.Systems;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Getting somewhere without walking (PLAN.md §5.3).
/// </summary>
public static class TravelCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        // "rec": rest answers to "r" and remove to "re".
        commands.Add(new CommandDefinition(
            "recall", 3, "recall (rec) - return to where you bound your soul", Recall));
    }

    /// <summary>
    /// Sends the character back to their bind point.
    /// </summary>
    /// <remarks>
    /// The destination is the one <c>bind</c> already set and death already uses (§4.12), rather
    /// than a second configured room that could drift from it - which also means <c>bind</c> now
    /// matters while you are alive.
    ///
    /// It costs nothing and has no cooldown, which sounds generous until you notice what it is:
    /// dying on purpose without the experience loss. Death already returns you here for free. What
    /// stops it being an escape hatch is that it refuses in a fight, so the fight you would want
    /// to escape is exactly the one it will not take you out of.
    /// </remarks>
    private static void Recall(CommandContext ctx)
    {
        var character = ctx.Actor.Character;

        if (Travel.Refuse(ctx.World, character, ctx.Clock?.CurrentPulse ?? 0L) is { } refusal)
        {
            ctx.Reply(refusal, "bad");
            return;
        }

        // The configured starting room is the fallback for a character who has never bound, and
        // for one whose bind point a builder has since deleted (§7.4).
        var bound = character.RespawnRoomKey;
        var destination =
            (bound is { } key && ctx.World.TryGetRoom(key, out var boundRoom))
                ? boundRoom
                : ctx.Options is { } options && ctx.World.TryGetRoom(options.StartingRoom, out var start)
                    ? start
                    : null;

        if (destination is null)
        {
            ctx.Reply("Nothing answers. There is nowhere to return to.", "bad");
            return;
        }

        if (destination.Key == ctx.Actor.RoomKey)
        {
            ctx.Reply("You are already where your soul is bound.", "bad");
            return;
        }

        Travel.To(
            ctx.World,
            ctx.View,
            ctx.Actor,
            destination,
            $"{ctx.Actor.Name} is drawn away, and is gone.",
            $"{ctx.Actor.Name} appears, drawn out of the air.");

        ctx.Reply("The world folds, and lets you go.", "heading");
    }
}
