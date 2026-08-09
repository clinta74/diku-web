using DikuWeb.Domain.Worlds;

namespace DikuWeb.Engine;

/// <summary>
/// Paths into the builder, for the deep links <c>examine</c> and <c>stats</c> hand a builder.
/// </summary>
/// <remarks>
/// Built here rather than on the client so the game can decide *whether* to offer a link at all.
/// A player never receives one, which keeps the builder's existence off the wire for anyone who
/// cannot use it. These must stay in step with <c>client/src/builder/routes.ts</c>; a stale path
/// lands on an empty tab rather than failing, so the client's route tests are the other half of
/// this contract.
/// </remarks>
public static class BuilderLinks
{
    public static string ToItem(string templateKey) => $"/builder/items/{templateKey}";

    public static string ToMob(string templateKey) => $"/builder/mobs/{templateKey}";

    /// <summary>
    /// The room editor, on its details tab.
    /// </summary>
    /// <remarks>
    /// Each segment is the *last* part of its key, not the qualified key: the client's
    /// <c>toWorldPath</c> slugs "aldenmoor.millbrook" down to "millbrook" and recomposes the
    /// full keys from the path. Passing qualified keys here produces a path that routes to
    /// nothing in particular.
    /// </remarks>
    public static string ToRoom(RoomKey key) =>
        $"/builder/world/{key.World}/{key.Zone}/{key.Room}/details";
}
