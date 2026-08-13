namespace DikuWeb.Engine.Protocol;

/// <summary>
/// One server-to-client SSE event (PLAN.md §3.5). <see cref="Type"/> becomes the
/// "event:" line and <see cref="Payload"/> is serialised into "data:".
/// </summary>
public sealed record OutboundEvent(string Type, object Payload);

public static class EventTypes
{
    public const string Text = "text";
    public const string Room = "room";
    public const string Map = "map";
    public const string Contents = "contents";
    public const string Vitals = "vitals";
    public const string Sys = "sys";
}

/// <summary>
/// Styled markup rather than raw ANSI, so the client owns the colour theme (PLAN.md §3.5).
/// </summary>
/// <param name="T">The text.</param>
/// <param name="S">A style name the client resolves against its own theme.</param>
/// <param name="B">
/// A builder path this span opens, e.g. <c>/builder/items/rusty-dagger</c>. Null for ordinary
/// prose, which is nearly all of it.
/// </param>
/// <remarks>
/// The link is a *path*, not a URL: the client routes it internally rather than navigating, so
/// following one from the game keeps the session and the stream alive. Only builders are ever
/// sent one — the server decides, so a player cannot discover the builder by reading the wire.
/// </remarks>
public sealed record TextSpan(string T, string? S = null, string? B = null);

public sealed record TextPayload(IReadOnlyList<TextSpan> Spans)
{
    public static TextPayload Plain(string text) => new([new TextSpan(text)]);

    public static TextPayload Styled(string text, string style) =>
        new([new TextSpan(text, style)]);
}

public sealed record RoomPayload(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<string> Exits);

public sealed record MapEntity(string Id, string Icon, int X, int Y, string Label, string Type = "mob");

public sealed record MapPayload(
    int W,
    int H,
    IReadOnlyList<string> Terrain,
    IReadOnlyList<MapEntity> Entities);

public sealed record ContentEntry(string Icon, string Label, string Keyword);

public sealed record ContentsPayload(
    IReadOnlyList<ContentEntry> Occupants,
    IReadOnlyList<ContentEntry> Items,
    IReadOnlyDictionary<string, string>? Legend = null);

public sealed record VitalsPayload(
    int Health,
    int HealthMax,
    int Focus,
    int FocusMax,
    int Stamina,
    int StaminaMax,
    int Level,
    long Xp,
    string Path,
    long Gold);

/// <summary>Connection notices, link-dead warnings, forced logout.</summary>
public sealed record SysPayload(string Message, string Kind);

public static class SysKinds
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Disconnect = "disconnect";

    /// <summary>
    /// This character was opened somewhere else, and this connection is no longer the live one.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Disconnect"/> because the client must react differently: a
    /// disconnect is something to retry, and this is the one case where retrying is precisely
    /// wrong. Two devices that both keep reconnecting take it in turns to hold the stream, and
    /// each ends up with roughly half the game's output.
    /// </remarks>
    public const string Displaced = "displaced";
}
