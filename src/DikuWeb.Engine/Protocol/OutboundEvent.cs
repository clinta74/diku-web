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
public sealed record TextSpan(string T, string? S = null);

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

public sealed record MapEntity(string Id, string Icon, int X, int Y, string Label);

public sealed record MapPayload(
    int W,
    int H,
    IReadOnlyList<string> Terrain,
    IReadOnlyDictionary<string, string> Legend,
    IReadOnlyList<MapEntity> Entities);

public sealed record ContentEntry(string Icon, string Label, string Keyword);

public sealed record ContentsPayload(
    IReadOnlyList<ContentEntry> Occupants,
    IReadOnlyList<ContentEntry> Items);

public sealed record VitalsPayload(
    int Health,
    int HealthMax,
    int Focus,
    int FocusMax,
    int Stamina,
    int StaminaMax,
    int Level,
    long Xp,
    string Path);

/// <summary>Connection notices, link-dead warnings, forced logout.</summary>
public sealed record SysPayload(string Message, string Kind);

public static class SysKinds
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Disconnect = "disconnect";
}
