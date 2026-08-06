using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Spawning;
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

/// <summary>Where the game loop hands off characters to be saved.</summary>
public interface ICharacterSaveQueue
{
    void Enqueue(CharacterSnapshot snapshot);
}

/// <summary>Where the game loop hands off items to be saved.</summary>
public interface IItemSaveQueue
{
    void Enqueue(ItemInstance item);
}

/// <summary>Where in-game builder commands hand off their writes.</summary>
public interface IWorldWriteQueue
{
    void Enqueue(WorldWriteJob job);
}

public sealed record WorldWriteJob(
    IReadOnlyList<Mutations.WorldChange> Changes,
    Guid? AccountId);

/// <summary>Where the in-game admin commands hand off work that touches the account store.</summary>
public interface IAccountAdminQueue
{
    void Enqueue(AccountAdminRequest request);
}

public abstract record AccountAdminRequest
{
    public required Guid ActorAccountId { get; init; }
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

/// <summary>Read-only access to template data for spawning systems.</summary>
public interface IMobTemplateRepository
{
    Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct);
}

public interface IItemTemplateRepository
{
    Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct);
}

public interface ISpawnerRepository
{
    Task<IReadOnlyList<Spawner>> GetAllAsync(CancellationToken ct);
}

public interface IAbilityRepository
{
    Task<Ability?> GetByKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<Ability>> GetAllAsync(CancellationToken ct);
}

public interface IQuestRepository
{
    Task<Quest?> GetByKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<Quest>> GetAllAsync(CancellationToken ct);
}

public interface ICharacterQuestRepository
{
    Task<IReadOnlyList<CharacterQuest>> GetForCharacterAsync(Guid characterId, CancellationToken ct);
    Task<CharacterQuest?> GetByKeyAsync(Guid characterId, string questKey, CancellationToken ct);
}

public interface ICharacterQuestSaveQueue
{
    void Enqueue(CharacterQuestSnapshot snapshot);
}

public sealed record CharacterQuestSnapshot(
    Guid CharacterId,
    string QuestKey,
    DikuWeb.Domain.Quests.QuestStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int TimesCompleted);

/// <summary>An immutable copy of the persistable state of a character, taken on the game loop thread.</summary>
public sealed record CharacterSnapshot(
    Guid Id,
    RoomKey RoomKey,
    int Level,
    long Xp,
    AttributeSet Attributes,
    Vitals Vitals,
    RoomKey? RespawnRoomKey,
    long Gold,
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
            character.RespawnRoomKey,
            character.Gold,
            now,
            character.PlaytimeSeconds);
    }
}

/// <summary>Engine configuration supplied by the host.</summary>
public sealed class EngineOptions
{
    /// <summary>
    /// Where new characters start, and where anyone whose saved room no longer exists is
    /// placed on login (PLAN.md §7.4).
    /// </summary>
    public RoomKey StartingRoom { get; set; } = RoomKey.Parse("aldenmoor.millbrook.north-gate");

    /// <summary>PLAN.md §3.6: 90 seconds, expressed in pulses.</summary>
    public int LinkDeadGracePulses { get; set; } = 360;

    /// <summary>Bound on the inbound queue; a full queue produces backpressure, not growth.</summary>
    public int InboundCapacity { get; set; } = 4096;

    /// <summary>Death system configuration (PLAN.md §4.12).</summary>
    public double XpLossPercent { get; set; } = 0.10;
    public int XpLossMinLevel { get; set; } = 5;
    public double RespawnHealthPercent { get; set; } = 0.25;
    public bool PvpCostsXp { get; set; } = false;
}
