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
