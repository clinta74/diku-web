using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Time;

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
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>Where the game loop hands off items to be saved or destroyed.</summary>
public interface IItemSaveQueue
{
    void Enqueue(ItemInstance item);

    /// <summary>
    /// Removes an item from storage. Takes an id rather than the instance because callers
    /// reach this point after the item has already left the world.
    /// </summary>
    void EnqueueDelete(Guid itemId);

    Task FlushAsync(CancellationToken cancellationToken);
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

/// <summary>
/// Retires a character by name (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// By <em>character</em> name rather than account name, which is the opposite of every other
/// request here — and deliberately, because a character is the thing an administrator can see.
/// They are dealing with a name standing in a room, and making them look up which account owns it
/// first is asking them to do a join by hand.
///
/// A soft delete: the row keeps its <c>DeletedAt</c> and everything hanging off it stays
/// referentially intact. Hard deletion would cascade through items, quest progress and audit rows,
/// and "we deleted the wrong Kael" is not a recoverable sentence.
/// </remarks>
public sealed record DeleteCharacterRequest : AccountAdminRequest
{
    public required string CharacterName { get; init; }
}

/// <summary>
/// Bans or unbans an account (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// One request for both directions rather than two, because they are the same edit to the same
/// column with the same audit row — and a separate unban path is how one of the two ends up
/// forgetting to write the audit.
/// </remarks>
public sealed record SetAccountBanRequest : AccountAdminRequest
{
    public required string TargetUsername { get; init; }

    public required bool Banned { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Silences an account on the player-to-player channels for a while (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// A duration rather than a flag, because the moderation action people actually want is "cool off
/// for an hour" and an indefinite mute someone has to remember to lift is one that never gets
/// lifted. <see cref="Until"/> null means lift it now.
/// </remarks>
public sealed record SetAccountMuteRequest : AccountAdminRequest
{
    public required string TargetUsername { get; init; }

    public required DateTimeOffset? Until { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Read-only access to template data for spawning systems.</summary>
public interface IMobTemplateRepository
{
    Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct);
}

public interface IItemTemplateRepository
{
    Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct);
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

    /// <summary>
    /// Forgets a character's row for one quest. Abandoning returns a quest to "never started",
    /// which §6 spells as the absence of a row - so it has to reach storage as a delete. Doing it
    /// in memory only would have the quest reappear Active at the next restart, which is the same
    /// bug the turn-in path already carries a comment about.
    /// </summary>
    void EnqueueDelete(Guid characterId, string questKey);
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
    /// <summary>
    /// Capabilities earned (PLAN.md §4.15). Copied rather than shared: the list on the character
    /// is mutated in place when a quest grants a flag, and a snapshot that aliased it would not be
    /// a snapshot.
    /// </summary>
    IReadOnlyList<string> Flags,
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

                // Copied here or never saved at all. This method is the whole of what reaches the
                // database, and three fields have already been dropped by being added to Vitals and
                // not to this list - see the comments in CharacterSaveQueue.
                Hunger = character.Vitals.Hunger,
                Thirst = character.Vitals.Thirst,
            },
            character.RespawnRoomKey,
            character.Gold,
            [.. character.Flags],
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
    /// <remarks>
    /// <b>Seeded from the active <c>game_configurations</c> row at boot, and editable in the
    /// builder while the server runs</b> (§4.16). The value here is the fallback for a database
    /// with no active configuration — a first boot, or a test harness — and is the last thing
    /// consulted rather than the first. It still names Millbrook because that is what a development
    /// seed creates.
    /// </remarks>
    public RoomKey StartingRoom { get; set; } = RoomKey.Parse("aldenmoor.millbrook.north-gate");

    /// <summary>
    /// What a character is told on entering the game, with <c>{name}</c> replaced by theirs.
    /// </summary>
    /// <remarks>
    /// This was a literal in <c>GameLoop</c> that named a world by hand, so it greeted every
    /// player in every world with the name of one of them and went stale the moment the world
    /// changed. Same provenance as <see cref="StartingRoom"/>: authored in the builder, stored in
    /// the database, and defaulted here only for an environment with no active configuration.
    /// </remarks>
    public string WelcomeMessage { get; set; } = GameConfiguration.DefaultWelcomeMessage;

    /// <summary>PLAN.md §3.6: 90 seconds, expressed in pulses.</summary>
    public int LinkDeadGracePulses { get; set; } = 360;

    /// <summary>
    /// The same window in seconds, which is the unit a deployment sets it in.
    /// </summary>
    /// <remarks>
    /// <c>docker-compose.prod.yml</c> has set <c>Engine__LinkDeadGraceSeconds</c> since it was
    /// written, and nothing read it: the option was pulses-only, and the <c>Engine</c>
    /// configuration section was never bound at all (fixed in <c>Program.cs</c>). Rather than
    /// correct the compose file to speak pulses, the option now speaks the unit the decision is
    /// actually made in — nobody chooses "360 quarter-seconds", they choose a minute and a half.
    ///
    /// Pulses remain the stored form because the loop counts in them; this is a view over that,
    /// so setting either one is coherent and reading back reflects whichever was set last.
    ///
    /// Mobile is what made this matter (MOBILE.md §6). Ninety seconds is generous on a desktop
    /// and short on a phone, where switching apps for two minutes is an ordinary interruption
    /// rather than a disconnection.
    /// </remarks>
    public int LinkDeadGraceSeconds
    {
        get => (int)Math.Round(LinkDeadGracePulses * GameTiming.PulseInterval.TotalSeconds);
        set => LinkDeadGracePulses =
            (int)Math.Round(Math.Max(0, value) / GameTiming.PulseInterval.TotalSeconds);
    }

    /// <summary>Bound on the inbound queue; a full queue produces backpressure, not growth.</summary>
    public int InboundCapacity { get; set; } = 4096;

    /// <summary>Death system configuration (PLAN.md §4.12).</summary>
    public double XpLossPercent { get; set; } = 0.10;
    public int XpLossMinLevel { get; set; } = 5;
    public double RespawnHealthPercent { get; set; } = 0.25;
    public bool PvpCostsXp { get; set; } = false;

    /// <summary>Shop sellback percentage (PLAN.md §5.2c).</summary>
    public decimal? ShopSellbackPercent { get; set; } = 0.5m;
}
