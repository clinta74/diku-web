using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Engine;

/// <summary>
/// How the Engine obtains the world without referencing the persistence layer. Implemented
/// in the Server, which is the only project that knows about EF Core (PLAN.md §2.2).
/// </summary>
public interface IWorldSource
{
    Task<WorldData> LoadAsync(CancellationToken cancellationToken);
}

public sealed record WorldData(
    IReadOnlyList<Domain.Worlds.World> Worlds,
    IReadOnlyList<Zone> Zones,
    IReadOnlyList<Room> Rooms);

/// <summary>
/// Where the game loop hands off characters to be saved.
/// </summary>
/// <remarks>
/// Deliberately fire-and-forget and non-blocking. PLAN.md §2.1 forbids any database call on
/// the loop thread - a save that waited on Postgres would stall every player in the world
/// for the duration of the round trip.
/// </remarks>
public interface ICharacterSaveQueue
{
    void Enqueue(CharacterSnapshot snapshot);
}

/// <summary>
/// Where in-game builder commands hand off their writes.
/// </summary>
/// <remarks>
/// Builder edits made over HTTP await persistence before answering, because the caller is a
/// request that can wait (PLAN.md §7.3). A <c>dig</c> typed at the command line cannot: it runs
/// on the loop thread, and the loop is forbidden from touching the database at all (§2.1). So
/// the in-game path is fire-and-forget through this queue, exactly like a character save.
///
/// The cost of that is real and worth stating: a failed write from an in-game command is logged
/// rather than reported to the builder, and the edit stays live until a restart drops it. The
/// builder panel, which is where anyone authoring seriously works, does not have this problem.
/// </remarks>
public interface IWorldWriteQueue
{
    void Enqueue(WorldWriteJob job);
}

public sealed record WorldWriteJob(
    IReadOnlyList<Mutations.WorldChange> Changes,
    Guid? AccountId);

/// <summary>
/// Where the in-game admin commands hand off work that touches the account store (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// Roles do not live in the world, they live in the database, and the loop is forbidden from
/// reading it (§2.1) - the Engine has no account repository at all, which is precisely why a
/// character's role is carried in on <see cref="Protocol.EnterWorld"/> rather than looked up.
///
/// So <c>promote</c> and <c>whois</c> are not command handlers that do the work. They validate
/// their arguments, enqueue here, and the answer comes back later as a
/// <see cref="Protocol.Notify"/> addressed at the session that asked.
/// </remarks>
public interface IAccountAdminQueue
{
    void Enqueue(AccountAdminRequest request);
}

public abstract record AccountAdminRequest
{
    /// <summary>Who asked. Used for the audit row and for the self-demotion guard.</summary>
    public required Guid ActorAccountId { get; init; }

    /// <summary>Where the answer goes. The command is fire-and-forget; the reply is not.</summary>
    public required Guid ReplyToSessionId { get; init; }
}

public sealed record SetAccountRoleRequest : AccountAdminRequest
{
    public required string TargetUsername { get; init; }

    public required AccountRole Role { get; init; }
}

public sealed record LookupAccountRequest : AccountAdminRequest
{
    public required string TargetUsername { get; init; }
}

/// <summary>
/// An immutable copy of the persistable state of a character, taken on the game loop thread.
/// </summary>
/// <remarks>
/// A snapshot rather than the live <see cref="Character"/> for a specific reason: the loop
/// keeps mutating that object while the save worker runs on another thread. Handing over the
/// entity itself would be a data race, and the symptom would be a character saved with a
/// room from one moment and vitals from another - rare, unreproducible, and awful to chase.
/// </remarks>
public sealed record CharacterSnapshot(
    Guid Id,
    RoomKey RoomKey,
    int Level,
    long Xp,
    AttributeSet Attributes,
    Vitals Vitals,
    DateTimeOffset LastPlayedAt,
    long PlaytimeSeconds)
{
    public static CharacterSnapshot From(Character character, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(character);

        return new CharacterSnapshot(
            character.Id,
            character.RoomKey,
            character.Level,
            character.Xp,
            // AttributeSet is init-only, so sharing the reference is safe.
            character.Attributes,
            // Vitals is mutable and changes every regen tick, so it must be copied.
            new Vitals
            {
                Health = character.Vitals.Health,
                HealthMax = character.Vitals.HealthMax,
                Focus = character.Vitals.Focus,
                FocusMax = character.Vitals.FocusMax,
                Stamina = character.Vitals.Stamina,
                StaminaMax = character.Vitals.StaminaMax,
            },
            now,
            character.PlaytimeSeconds);
    }
}

/// <summary>Engine configuration supplied by the host.</summary>
public sealed class EngineOptions
{
    /// <summary>
    /// Where new characters start, and where anyone whose saved room no longer exists is
    /// placed on login (PLAN.md §7.4 - live editing can delete a room out from under them).
    /// </summary>
    public RoomKey StartingRoom { get; set; } = RoomKey.Parse("aldenmoor.millbrook.north-gate");

    /// <summary>PLAN.md §3.6: 90 seconds, expressed in pulses.</summary>
    public int LinkDeadGracePulses { get; set; } = 360;

    /// <summary>Bound on the inbound queue; a full queue produces backpressure, not growth.</summary>
    public int InboundCapacity { get; set; } = 4096;
}
