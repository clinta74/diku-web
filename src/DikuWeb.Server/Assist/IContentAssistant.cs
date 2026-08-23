namespace DikuWeb.Server.Assist;

/// <summary>
/// What a zone can teach the model about itself.
/// </summary>
/// <remarks>
/// <b>Exemplars are extracted rooms, not files.</b> A whole zone bundle is ~36,000 tokens against a
/// 16,384 window, so "show it the zone" is not available at any price. A handful of the zone's own
/// rooms is - and PLAN.md §13 argues it is also the better teacher, because a zone with fifteen
/// rooms teaches its own voice with no schema change and no <c>tone</c> field to maintain.
/// </remarks>
/// <param name="ZoneName">What the zone is called, for the model to say it correctly.</param>
/// <param name="ZoneDescription">The zone's own description, which is the closest thing to a brief.</param>
/// <param name="RoomKeys">Every room in the zone - the legal set an exit may lead to.</param>
/// <param name="Exemplars">A few existing rooms, title and description, to set the voice.</param>
public sealed record ZoneContext(
    string ZoneName,
    string ZoneDescription,
    IReadOnlyList<string> RoomKeys,
    IReadOnlyList<RoomExemplar> Exemplars);

/// <param name="Title">The room's look line.</param>
/// <param name="Description">Its prose.</param>
public sealed record RoomExemplar(string Title, string Description);

/// <summary>
/// Drafts content. One implementation talks to Ollama; tests use their own.
/// </summary>
/// <remarks>
/// An interface because this is <b>the first outbound HTTP call anywhere in <c>src/</c></b>
/// (PLAN.md §13) - everything else this server talks to is Postgres, in process. That is worth a
/// seam rather than discovering later that nothing can be tested without a model on the machine.
/// </remarks>
public interface IContentAssistant
{
    /// <summary>
    /// Drafts one room, or throws.
    /// </summary>
    /// <remarks>
    /// Throwing rather than returning a failure result is deliberate: the only caller is the queue
    /// worker, whose whole job is to turn an exception into a failed job with a message on it. A
    /// result type here would be a second way to say the same thing, and the one that callers
    /// forget to check.
    /// </remarks>
    Task<RoomDraft> DraftRoomAsync(
        RoomDraftRequest request,
        ZoneContext context,
        CancellationToken cancellationToken);

    /// <summary>Drafts the prose for a mob, item, or quest, or throws.</summary>
    Task<ProseDraft> DraftProseAsync(
        ProseDraftRequest request,
        ProseContext context,
        CancellationToken cancellationToken);
}
