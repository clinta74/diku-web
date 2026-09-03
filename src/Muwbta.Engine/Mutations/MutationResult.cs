using Muwbta.Domain.Worlds;

namespace Muwbta.Engine.Mutations;

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
/// What a <see cref="RespawnZone"/> moved: mobs taken out, mobs put back (PLAN.md §7.5).
/// </summary>
/// <remarks>
/// The two numbers differ whenever the zone was not at its population target - a spawner still
/// waiting out its <c>respawnSeconds</c> refills the whole way here, so "3 removed, 5 placed" is
/// the honest report and a single count would be a lie in one direction or the other.
/// </remarks>
public sealed record RespawnTally(int Despawned, int Spawned);

/// <summary>
/// What the loop did. <see cref="Applied"/> is the ordered list of primitives that persistence
/// must replay; it is empty on failure, so a refused mutation can never reach the database.
/// </summary>
public sealed record MutationResult(
    bool Success,
    MutationError Error,
    string? Message,
    IReadOnlyList<WorldChange> Applied,
    RoomKey? AffectedRoom = null,
    RespawnTally? Respawned = null)
{
    public static MutationResult Ok(IReadOnlyList<WorldChange> applied, RoomKey? room = null) =>
        new(true, MutationError.None, null, applied, room);

    /// <summary>
    /// A mutation that succeeded and authored nothing, so there is no primitive to replay - the
    /// count is the whole of what happened. See <see cref="RespawnZone"/>.
    /// </summary>
    public static MutationResult Ok(RespawnTally tally) =>
        new(true, MutationError.None, null, [], null, tally);

    public static MutationResult Fail(MutationError error, string message) =>
        new(false, error, message, []);
}
