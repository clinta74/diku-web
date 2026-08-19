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

    /// <summary>The whole ability list, with whatever is still cooling down.</summary>
    public const string Abilities = "abilities";

    /// <summary>One ability just went on cooldown.</summary>
    public const string Cooldown = "cooldown";

    /// <summary>The group's roster and how everyone in it is holding up.</summary>
    public const string Party = "party";
}

/// <param name="RemainingPulses">
/// What is left of this ability's cooldown right now, or 0 when it is ready.
/// </param>
public sealed record AbilityEntry(
    string Key,
    string Name,
    string Verb,
    string CostType,
    int CostValue,
    long CooldownPulses,
    long RemainingPulses,
    bool IsSpell);

/// <summary>
/// Everything the character can use, sent whole (PLAN.md §3.5).
/// </summary>
/// <remarks>
/// <b>The roster is the half without which a cooldown event says nothing.</b> The client had never
/// been told what abilities a character has, so an event naming <c>warden.kick</c> would have
/// arrived somewhere with nothing to grey out.
///
/// <b>It carries remaining cooldown, and that is what makes a reconnect correct.</b> The client
/// counts down locally between casts, so a reconnecting one has missed every
/// <see cref="EventTypes.Cooldown"/> event that fired while it was away - or, worse, replays stale
/// ones out of the ring buffer (§3.4) and shows a cooldown that expired minutes ago. Sending the
/// remaining time with the roster, and sending the roster on entry, makes resynchronising a
/// property of reconnecting rather than a thing to remember.
///
/// Sent whole rather than as a delta because it is small - thirty-seven abilities at most, of
/// which one Path sees ten - and because a delta would need the client to already hold a correct
/// list, which is the thing being established.
/// </remarks>
public sealed record AbilitiesPayload(IReadOnlyList<AbilityEntry> Abilities);

/// <summary>
/// One ability has just been used and is now cooling down.
/// </summary>
/// <remarks>
/// <b>One event per cast, with the client doing the counting.</b> The alternative - a frame per
/// pulse while anything is cooling - is four a second per player times however many abilities are
/// down, which is exactly the traffic §11's pulse budget is careful about. This is the floor: one
/// event when the state actually changes.
///
/// Emitted where the cooldown is *recorded* rather than where the command is parsed, because cost
/// and cooldown are spent before the target resolves on some paths - a refusal must not grey out a
/// button for an ability that was never used.
/// </remarks>
public sealed record CooldownPayload(string Key, long Pulses);

/// <summary>
/// Styled markup rather than raw ANSI, so the client owns the colour theme (PLAN.md §3.5).
/// </summary>
/// <param name="T">The text.</param>
/// <param name="S">A style name the client resolves against its own theme.</param>
/// <param name="B">
/// A builder path this span opens, e.g. <c>/builder/items/rusty-dagger</c>. Null for ordinary
/// prose, which is nearly all of it.
/// </param>
/// <param name="C">
/// A command this span runs when clicked, e.g. <c>talk vane cogs</c>. Null for ordinary prose.
/// </param>
/// <remarks>
/// <para>
/// The link is a *path*, not a URL: the client routes it internally rather than navigating, so
/// following one from the game keeps the session and the stream alive. Only builders are ever
/// sent one — the server decides, so a player cannot discover the builder by reading the wire.
/// </para>
/// <para>
/// <b>A command span must carry the same text it runs.</b> The client echoes what it sends, so a
/// span reading "talk vane cogs" that fired <c>talk vane a3-1-blanks-and-cogs</c> would put a key
/// in the player's transcript that they never typed. Keeping them identical also means the label
/// and the action cannot drift apart, which is the failure mode of authoring the link as markup
/// inside the prose.
/// </para>
/// <para>
/// It <em>runs</em> rather than inserting, unlike the contents panel, whose clicks type a keyword
/// and let the player finish the sentence. The difference is ambiguity: clicking "a rat" could
/// mean look, attack or get, while a command span is already a resolved verb and object.
/// </para>
/// </remarks>
public sealed record TextSpan(string T, string? S = null, string? B = null, string? C = null);

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

/// <summary>
/// One member of the group, as the rest of the group sees them.
/// </summary>
/// <param name="Here">
/// Whether they are standing in the same room as the viewer. A group is allowed to split up, and a
/// bar that does not say so reads as "nobody is helping" rather than "they are two rooms away".
/// </param>
public sealed record PartyMemberEntry(
    string Name,
    int Level,
    string Path,
    int Health,
    int HealthMax,
    int Focus,
    int FocusMax,
    int Stamina,
    int StaminaMax,
    bool IsLeader,
    bool Here,
    bool LinkDead);

/// <summary>
/// The whole group, sent to each of its members (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// <b>An empty list means "not in a group"</b> rather than being a state the client has to
/// distinguish from never having been told. Leaving a party is the case that matters: it has to
/// clear the panel, and a payload that only ever described a party would leave the last roster on
/// screen forever.
///
/// A single frame naming everyone rather than a per-member one, for the reason
/// <see cref="AbilitiesPayload"/> gives: it is at most six entries, and a delta would require the
/// client to already hold a correct roster. The viewer is included in it - they are in their own
/// group, and dropping them would make the panel disagree with <c>group</c>.
///
/// The viewer's own copy of this is what makes it comparable per player: <c>Here</c> is answered
/// from the viewer's room, so two members standing apart are correctly sent different frames.
/// </remarks>
public sealed record PartyPayload(IReadOnlyList<PartyMemberEntry> Members);

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
