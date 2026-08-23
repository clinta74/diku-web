namespace DikuWeb.Server.Assist;

/// <summary>What a builder asked for. Names an entity; does not carry a prompt.</summary>
/// <remarks>
/// PLAN.md §13: "the request should name an entity, not carry a prompt", with the server assembling
/// context. That keeps the prompt one thing in one place to tune, and it keeps the client thin -
/// but the reason it matters more than tidiness is the cache. A client that sent its own prompt
/// would decide the prefix, and a prefix the client decides is a prefix that differs between two
/// builders using two versions of the page.
/// </remarks>
/// <param name="ZoneKey">The zone the room belongs to. Its rooms are the exemplars and the exits.</param>
/// <param name="RoomKey">The room being drafted. May not exist yet.</param>
/// <param name="Instruction">
/// An optional steer from the builder - "make it colder", "there should be a shrine". Free text,
/// and the only free text in the request; it lands at the very end of the prompt, after the canon
/// and after the context, so nothing a builder types can move the cached prefix.
/// </param>
public sealed record RoomDraftRequest(string ZoneKey, string RoomKey, string? Instruction);

/// <summary>One drafted room, as the model returned it and the validator accepted it.</summary>
public sealed record RoomDraft(string Title, string Description, IReadOnlyList<DraftExit> Exits);

/// <param name="Direction">One of the engine's six, lowercased.</param>
/// <param name="To">A room that already exists - the schema enumerates the legal set.</param>
public sealed record DraftExit(string Direction, string To);

/// <summary>Where a queued job has got to.</summary>
public enum AssistJobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// A job and whatever is known about it so far.
/// </summary>
/// <remarks>
/// A job rather than a response because generation is measured at 1.3-1.8 tok/s and a room is
/// about three minutes. There is no version of this that is a request the browser waits on.
/// </remarks>
public sealed record AssistJob(
    Guid Id,
    AssistJobState State,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    RoomDraft? Draft,
    string? Error,
    IReadOnlyList<string> Warnings);
