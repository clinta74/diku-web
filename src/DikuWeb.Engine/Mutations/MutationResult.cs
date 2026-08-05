using DikuWeb.Domain.Worlds;

namespace DikuWeb.Engine.Mutations;

/// <summary>
/// Why a mutation was refused. Mapped to HTTP status by the Server, which is the only layer
/// that should know what a 409 is.
/// </summary>
public enum MutationError
{
    None = 0,

    /// <summary>Malformed input the Server did not catch - bad key, unknown direction. → 400</summary>
    Invalid = 1,

    /// <summary>The thing being edited is not there. → 404</summary>
    NotFound = 2,

    /// <summary>Creating something whose key is taken. → 409</summary>
    Conflict = 3,

    /// <summary>
    /// Refused because of live world state - the one case being a zone with players standing
    /// in it. → 409
    /// </summary>
    Occupied = 4,
}

/// <summary>
/// What the loop did. <see cref="Applied"/> is the ordered list of primitives that persistence
/// must replay; it is empty on failure, so a refused mutation can never reach the database.
/// </summary>
public sealed record MutationResult(
    bool Success,
    MutationError Error,
    string? Message,
    IReadOnlyList<WorldChange> Applied,
    RoomKey? AffectedRoom = null)
{
    public static MutationResult Ok(IReadOnlyList<WorldChange> applied, RoomKey? room = null) =>
        new(true, MutationError.None, null, applied, room);

    public static MutationResult Fail(MutationError error, string message) =>
        new(false, error, message, []);
}
