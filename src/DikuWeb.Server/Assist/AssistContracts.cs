using DikuWeb.Server.Building;

namespace DikuWeb.Server.Assist;

/// <summary>
/// Anything the queue can be asked to draft.
/// </summary>
/// <remarks>
/// A base record rather than one request with a kind field, because the kinds genuinely differ: a
/// room has exits and a quest has a summary, and a single shape carrying every field for every kind
/// would be mostly nulls with nothing saying which ones are meaningful when. Each endpoint binds
/// its own concrete type, so nothing here needs polymorphic deserialisation.
/// </remarks>
public abstract record AssistRequest
{
    /// <summary>What is being drafted, for the log and for an error message.</summary>
    public abstract string Subject { get; }
}

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
/// and it lands at the very end of the prompt, after the canon and after the context, so nothing a
/// builder types can move the cached prefix.
/// </param>
/// <param name="Title">
/// What the editor currently holds, which may not be what the database holds.
/// </param>
/// <param name="Description">
/// The prose the builder has already written, if any.
/// <para>
/// <b>Sent by the client rather than read from the database, because the interesting case is the
/// unsaved one.</b> A builder asks for help with the half-paragraph in front of them - the one
/// they have just typed and are stuck on - and that text exists only in the browser. Reading the
/// saved row instead would seed the model with the version they are in the middle of replacing,
/// which is the one piece of context guaranteed to be stale.
/// </para>
/// </param>
public sealed record RoomDraftRequest(
    string ZoneKey,
    string RoomKey,
    string? Instruction,
    string? Title = null,
    string? Description = null) : AssistRequest
{
    public override string Subject => RoomKey;
}

/// <summary>
/// A mob, item, or quest, for which the assist writes prose and nothing else.
/// </summary>
/// <remarks>
/// <b>Prose and nothing else is the whole scope, and it is the <c>respawn: true</c> lesson applied
/// in advance.</b> A mob's level and loot, an item's weight and slots, a quest's giver and rewards
/// are all decided before anybody wants words for them - and a model handed those fields fills
/// them in plausibly, because a constrained sampler cannot decline. They go <em>in</em> as context
/// so the prose matches the thing; they do not come out. See <c>AssistSchema.MobNotGenerated</c>
/// and its siblings.
/// </remarks>
/// <param name="Kind">Which of the three.</param>
/// <param name="Key">The template or quest key. Must already exist - its numbers are the context.</param>
/// <param name="Name">What the editor currently holds, which may not be what the database holds.</param>
/// <param name="Summary">Quests only; ignored for the other two.</param>
public sealed record ProseDraftRequest(
    AssistSchema.ProseKind Kind,
    string Key,
    string? Instruction,
    string? Name = null,
    string? Summary = null,
    string? Description = null) : AssistRequest
{
    public override string Subject => Key;
}

/// <summary>One drafted mob, item, or quest.</summary>
/// <param name="Summary">Present for a quest, null for the other two.</param>
public sealed record ProseDraft(string Name, string Description, string? Summary);

/// <summary>One drafted room, as the model returned it and the validator accepted it.</summary>
public sealed record RoomDraft(string Title, string Description, IReadOnlyList<DraftExit> Exits);

/// <param name="Direction">One of the engine's six, lowercased.</param>
/// <param name="To">A room that already exists - the schema enumerates the legal set.</param>
public sealed record DraftExit(string Direction, string To);

/// <summary>Where a queued job has got to.</summary>
public enum AssistJobState
{
    Queued,

    /// <summary>
    /// Waiting for the model to finish caching the canon.
    /// </summary>
    /// <remarks>
    /// Its own state rather than <see cref="Queued"/>, because the wait is a different order of
    /// magnitude and the reason is worth telling somebody. On the deployment a cold canon is about
    /// half an hour; a queued job is three minutes.
    /// </remarks>
    Warming,

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
    ProseDraft? Prose,
    string? Error,
    IReadOnlyList<string> Warnings);
