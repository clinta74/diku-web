using Muwbta.Domain.Worlds;
using Muwbta.Engine.Protocol;

namespace Muwbta.Engine.World;

/// <summary>
/// Telling a whole room one thing, once.
/// </summary>
/// <remarks>
/// <para>
/// <b>The event is built before the loop and the same instance goes to everybody.</b>
/// <see cref="OutboundEvent"/> and every payload beneath it are immutable records, and a session
/// does nothing with what it is handed except serialise it, so one instance can sit in sixty
/// channels at once.
/// </para>
/// <para>
/// This exists because the shape it replaces was written six times and was wrong in the same way
/// each time: an interpolated string and a <c>SendText</c> <em>inside</em> the loop, so a sentence
/// identical for every recipient cost a string, an event, a payload, a span and the array holding
/// it — five objects apiece. Movement is the hot one, because it says something to two rooms and
/// then redraws both, and at two hundred sessions crowded into three rooms the allocation rate
/// showed up in the pulse histogram as a long tail (PLAN.md §11).
/// </para>
/// <para>
/// Extension methods rather than methods on <see cref="WorldState"/>: what a room is told is a
/// presentation concern, and world state should not learn about <see cref="OutboundEvent"/> to
/// serve it. <c>CommandContext</c> keeps its own pair for the common case of "the room this actor
/// is standing in", which is most call sites.
/// </para>
/// </remarks>
public static class RoomBroadcast
{
    /// <summary>
    /// Tells everyone else in a room something they can only have <em>seen</em>, skipping sleepers.
    /// </summary>
    public static void TellOthersWhoCanSee(
        this WorldState world,
        RoomKey room,
        PlayerActor except,
        string text,
        string? style = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        var message = Line(text, style);

        foreach (var other in world.OthersAwakeIn(room, except))
        {
            other.Send(message);
        }
    }

    /// <summary>
    /// Tells everyone else in a room something, sleepers included.
    /// </summary>
    /// <remarks>
    /// For speech, which is the one thing in a room that reaches somebody with their eyes shut.
    /// </remarks>
    public static void TellOthers(
        this WorldState world,
        RoomKey room,
        PlayerActor except,
        string text,
        string? style = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        var message = Line(text, style);

        foreach (var other in world.OthersIn(room, except))
        {
            other.Send(message);
        }
    }

    private static OutboundEvent Line(string text, string? style) =>
        new(EventTypes.Text, style is null ? TextPayload.Plain(text) : TextPayload.Styled(text, style));
}
