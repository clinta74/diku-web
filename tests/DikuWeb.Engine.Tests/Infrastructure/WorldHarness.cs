using System.Threading.Channels;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Domain.Items;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Mutations;
using DikuWeb.Domain.Randomness;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Quests;
using DikuWeb.Domain.Quests;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;
using EngineCombatSystem = DikuWeb.Engine.Systems.CombatSystem;

namespace DikuWeb.Engine.Tests.Infrastructure;

/// <summary>
/// Captures what would have been persisted, so a test can assert the primitives the loop
/// produced without a database anywhere near it.
/// </summary>
internal sealed class RecordingWriteQueue : IWorldWriteQueue
{
    public List<WorldWriteJob> Jobs { get; } = [];

    public IEnumerable<WorldChange> AllChanges => Jobs.SelectMany(j => j.Changes);

    public void Enqueue(WorldWriteJob job) => Jobs.Add(job);
}

/// <summary>
/// Captures admin requests instead of touching an account store, which the loop could not do
/// anyway (PLAN.md §7.7). What the command produced is the whole of what these tests assert.
/// </summary>
internal sealed class RecordingAdminQueue : IAccountAdminQueue
{
    public List<AccountAdminRequest> Requests { get; } = [];

    public void Enqueue(AccountAdminRequest request) => Requests.Add(request);
}

/// <summary>
/// Captures item persistence instead of touching a database. Removing an item has to reach
/// storage as well as the in-memory world, and only this records whether it did.
/// </summary>
internal sealed class RecordingItemSaveQueue : IItemSaveQueue
{
    public List<ItemInstance> Saved { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public void Enqueue(ItemInstance item) => Saved.Add(item);

    public void EnqueueDelete(Guid itemId) => Deleted.Add(itemId);

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Captures quest progress instead of writing it, so a test can assert what was saved.</summary>
internal sealed class RecordingQuestSaveQueue : ICharacterQuestSaveQueue
{
    public List<CharacterQuestSnapshot> Saved { get; } = [];

    public void Enqueue(CharacterQuestSnapshot snapshot) => Saved.Add(snapshot);

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// A random source whose probability rolls are decided by the test.
/// </summary>
/// <remarks>
/// Dice still come from a seeded source, so damage stays realistic; only
/// <see cref="IRandomSource.NextDouble"/> is pinned. That is what <c>Chance</c> reads, which is
/// how a test says "this parry fires" or "this one does not" without inferring it from a seed.
/// </remarks>
internal sealed class ScriptedChanceSource(double nextDouble, int seed = 42) : IRandomSource
{
    private readonly SeededRandomSource _dice = new(seed);

    /// <summary>Never rolls a chance: any probability below 1.0 fails.</summary>
    public static ScriptedChanceSource Never => new(0.999);

    /// <summary>Always rolls a chance: any probability above 0 succeeds.</summary>
    public static ScriptedChanceSource Always => new(0.0);

    public int Next(int minInclusive, int maxExclusive) => _dice.Next(minInclusive, maxExclusive);

    public double NextDouble() => nextDouble;
}

/// <summary>
/// Wires the real WorldState, CommandRegistry, and PlayerView without the hosted service, so
/// command behaviour can be asserted synchronously. No timers, no sleeping, no database.
/// </summary>
internal sealed class WorldHarness
{
    private readonly Dictionary<Guid, Channel<OutboundEvent>> _channels = [];

    /// <param name="random">
    /// Overrides the seeded default, for tests that need a specific probability roll rather than
    /// whatever seed 42 happens to produce.
    /// </param>
    public WorldHarness(IRandomSource? random = null)
    {
        random ??= new SeededRandomSource(42);
        World = new WorldState(random);
        // AbilityCache was left null here, so every `cast` answered "not configured" and the
        // whole command path went untested - the buff tests reach past it and apply effects to
        // the world directly. Populated from the catalogue by DefineAbility.
        Commands = new CommandRegistry(abilityCache: AbilityCache, clock: Clock);
        View = new PlayerView(new RoomLayoutService());
        Options = new EngineOptions { StartingRoom = RoomKey.Parse("test.zone.west") };
        Applier = new WorldMutationApplier(World, View, Options);
        Writes = new RecordingWriteQueue();
        Editor = new LoopWorldEditor(Applier, Writes);
        Admin = new RecordingAdminQueue();
        // With the real effect registry, so a mob attack that carries an effect actually applies
        // it rather than swinging for damage alone.
        Combat = new EngineCombatSystem(
            Options,
            View,
            ItemTemplates,
            MobTemplates,
            itemSpawner: null,
            logger: null,
            effects: new Domain.Abilities.Effects.EffectRegistry());

        // With a cache and the real effect registry, so a cast resolves into an actual effect
        // rather than falling out of Tick with nothing to apply. The mob templates are what tell
        // an area effect which mobs are non-combatants; without them a Firestorm would kill the
        // shopkeeper and the test would pass.
        Abilities = new AbilitySystem(
            Clock,
            AbilityCache,
            new Domain.Abilities.Effects.EffectRegistry(),
            mobTemplates: MobTemplates);
    }

    public WorldState World { get; }

    /// <summary>Time under the test's control, so per-attack timing is exact rather than raced.</summary>
    public ManualGameClock Clock { get; } = new();

    /// <summary>Mob templates combat reads attack lists from.</summary>
    public MobTemplateCache MobTemplates { get; } = new();

    public EngineCombatSystem Combat { get; }

    public AbilitySystem Abilities { get; }

    /// <summary>The abilities `cast` can find. Populated by <see cref="DefineAbility"/>.</summary>
    public AbilityCache AbilityCache { get; } = new();

    /// <summary>
    /// Puts a real catalogue ability into the cache, so a test casts the same thing the game
    /// does rather than a hand-built stand-in with convenient numbers.
    /// </summary>
    public Domain.Abilities.Ability DefineAbility(string key)
    {
        var entry = Domain.Abilities.AbilityCatalogue.All.Single(e => e.Key == key);
        var ability = Domain.Abilities.AbilityCatalogue.ToAbility(entry);
        AbilityCache.Put(ability);
        return ability;
    }

    /// <summary>
    /// Advances time the way <c>GameLoop.Pulse</c> does: one pulse at a time, abilities before
    /// combat. Stepping in single pulses matters - batching would quantise attack timing in the
    /// harness rather than in the game, and hide exactly the bugs these tests exist to catch.
    /// </summary>
    public void Pump(int pulses = 1)
    {
        for (var i = 0; i < pulses; i++)
        {
            Abilities.Tick(World);
            Combat.Tick(World, Clock.CurrentPulse);
            Clock.AdvancePulses(1);
        }
    }

    /// <summary>Item templates the registry resolves slots and descriptions from.</summary>
    public ItemTemplateCache ItemTemplates { get; } = new();

    /// <summary>
    /// Quests the command layer reads. Populated by <see cref="DefineQuest"/>.
    /// </summary>
    /// <remarks>
    /// This used to be left null, so <c>Talk</c> and <c>TryTurnInQuest</c> returned on their
    /// first line and every quest test passed without reaching the code it named (PLAN §12).
    /// </remarks>
    public QuestCache Quests { get; } = new();

    /// <summary>Quest progress the commands handed off to be persisted.</summary>
    public RecordingQuestSaveQueue QuestSaves { get; } = new();

    private readonly FakeItemTemplateRepository _itemTemplateRepo = new();

    public CommandRegistry Commands { get; }

    public PlayerView View { get; }

    public EngineOptions Options { get; }

    public WorldMutationApplier Applier { get; }

    public RecordingWriteQueue Writes { get; }

    public LoopWorldEditor Editor { get; }

    public RecordingAdminQueue Admin { get; }

    /// <summary>What the commands under test handed off to be persisted or deleted.</summary>
    public RecordingItemSaveQueue ItemSaves { get; } = new();

    /// <summary>Applies a builder edit exactly as the loop would, minus persistence.</summary>
    public MutationResult Mutate(WorldChange change) => Applier.Apply(change);

    /// <summary>
    /// Three rooms west-to-east, plus a fourth exit off the east room that points at a room
    /// which does not exist - the dangling-exit case live editing makes routine.
    /// </summary>
    public static (Room West, Room Middle, Room East) BuildTestRooms()
    {
        var west = NewRoom(
            "west",
            grid: ["#######", "#.....#", "#.....#", "#######"],
            legend: new() { ["#"] = "wall", ["."] = "floor" });
        var middle = NewRoom("middle");
        var east = NewRoom("east");

        Link(west, Direction.East, middle);
        Link(middle, Direction.East, east);

        east.Exits.Add(new RoomExit
        {
            FromRoomKey = east.Key,
            Direction = Direction.North,
            ToRoomKey = RoomKey.Parse("test.zone.nowhere"),
        });

        return (west, middle, east);
    }

    /// <summary>
    /// The world plus its containing zone and world rows, which the flag chain needs: a room
    /// whose zone is not loaded can only ever resolve to the registry default.
    /// </summary>
    public void LoadTestWorld()
    {
        var (west, middle, east) = BuildTestRooms();

        World.Load(
            [new Domain.Worlds.World { Key = "test", Name = "Test" }],
            [new Zone { Key = "test.zone", WorldKey = "test", Name = "Test Zone" }],
            [west, middle, east]);
    }

    public Zone Zone => World.FindZone("test.zone")!;

    public Domain.Worlds.World World_ => World.FindWorld("test")!;

    public static Room NewRoom(
        string slug,
        string[]? grid = null,
        Dictionary<string, string>? legend = null) =>
        new()
        {
            Key = RoomKey.Create("test", "zone", slug),
            ZoneKey = "test.zone",
            Title = $"The {slug} room",
            Description = $"A featureless {slug} room used for testing.",
            Grid = [.. grid ?? []],
            Legend = legend ?? [],
        };

    public static void Link(Room from, Direction direction, Room to)
    {
        from.Exits.Add(new RoomExit
        {
            FromRoomKey = from.Key,
            Direction = direction,
            ToRoomKey = to.Key,
        });

        to.Exits.Add(new RoomExit
        {
            FromRoomKey = to.Key,
            Direction = direction.Opposite(),
            ToRoomKey = from.Key,
        });
    }

    public PlayerActor AddPlayer(
        string name,
        RoomKey at,
        AccountRole role = AccountRole.Player,
        CharacterPath path = CharacterPath.Warden,
        int level = 1)
    {
        var channel = Channel.CreateUnbounded<OutboundEvent>();

        var character = NewCharacter(name, at, path);
        character.Level = level;

        var actor = new PlayerActor
        {
            Character = character,
            Role = role,
            SessionId = Guid.CreateVersion7(),
            Output = channel.Writer,
        };

        _channels[actor.CharacterId] = channel;
        World.Add(actor);
        return actor;
    }

    public static Character NewCharacter(
        string name,
        RoomKey at,
        CharacterPath path = CharacterPath.Warden) => new()
    {
        AccountId = Guid.CreateVersion7(),
        Name = name,
        Path = path,
        Attributes = AttributeSet.Baseline,
        Vitals = Vitals.StartingFor(path),
        RoomKey = at,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>Runs a command exactly as the game loop would, minus the loop.</summary>
    public CommandContext Execute(PlayerActor actor, string input)
    {
        var (verb, argument) = CommandRegistry.Split(input);
        var definition = Commands.Find(verb)
            ?? throw new InvalidOperationException($"No command matched '{verb}'.");

        var context = new CommandContext
        {
            Actor = actor,
            World = World,
            View = View,
            Editor = Editor,
            AdminQueue = Admin,
            ItemSaveQueue = ItemSaves,
            ItemTemplates = ItemTemplates,
            MobTemplates = MobTemplates,
            Options = Options,
            Clock = Clock,
            Quests = Quests,
            QuestSaveQueue = QuestSaves,
            Verb = verb,
            Argument = argument,
        };

        definition.Handler(context);
        return context;
    }

    /// <summary>
    /// Puts a free-form bag through the same JSON round trip Postgres and the builder API put it
    /// through, so its values arrive as <c>JsonElement</c> rather than as the C# types they were
    /// written as.
    /// </summary>
    /// <remarks>
    /// A bag built inline in a test is the one shape the running game never sees. Shops and mob
    /// emotes were both dead in production while their hand-built test doubles passed, because
    /// the code pattern-matched <c>is bool</c> and <c>is List&lt;object&gt;</c>. Any test that
    /// asserts on behavior or item state should author it through here.
    /// </remarks>
    public static Dictionary<string, object> AsPersisted(Dictionary<string, object> bag)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(bag);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
    }

    /// <summary>
    /// Registers a quest in the cache the command layer reads, and returns it.
    /// </summary>
    /// <remarks>
    /// Defaults to the harness's own zone, because a quest whose zone is not loaded cannot
    /// resolve its rewards through any multiplier and would quietly pay its authored numbers.
    /// </remarks>
    public Quest DefineQuest(
        string key,
        string giverMobKey,
        string? turninMobKey = null,
        string? requiredItemKey = null,
        int requiredCount = 1,
        int rewardXp = 0,
        int rewardGold = 0,
        string? rewardItemKey = null,
        int rewardItemCount = 1,
        bool repeatable = false,
        string zoneKey = "test.zone",
        Dictionary<string, string>? dialogue = null)
    {
        var quest = new Quest
        {
            Key = key,
            Name = key,
            ZoneKey = zoneKey,
            GiverMobKey = giverMobKey,
            TurninMobKey = turninMobKey ?? giverMobKey,
            RequiredItemKey = requiredItemKey,
            RequiredCount = requiredCount,
            RewardXp = rewardXp,
            RewardGold = rewardGold,
            RewardItemKey = rewardItemKey,
            RewardItemCount = rewardItemCount,
            IsRepeatable = repeatable,
            Dialogue = dialogue ?? [],
        };

        Quests.Put(quest);
        return quest;
    }

    /// <summary>Sets this zone's difficulty dial, so reward and spawn resolution have something to scale by.</summary>
    public void SetZoneMultipliers(Action<Multipliers> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Zone.Multipliers);
    }

    /// <summary>Registers an item template (so its slot and description resolve) and returns it.</summary>
    public ItemTemplate DefineItem(
        string key,
        string name,
        ItemSlot? slot,
        string description = "",
        int value = 0)
    {
        var template = new ItemTemplate
        {
            Key = key,
            Name = name,
            Icon = "$",
            Slot = slot,
            Description = description,
            BaseValue = value,
        };

        _itemTemplateRepo.Add(template);
        ItemTemplates.LoadAsync(_itemTemplateRepo, CancellationToken.None).GetAwaiter().GetResult();
        return template;
    }

    /// <summary>
    /// Registers a weapon template with a speed and a verb. A null delay is the "declares no
    /// speed" case: a default-speed main hand, and an off-hand item that never strikes.
    /// </summary>
    /// <param name="attackBonus">
    /// Defaults absurdly high so a timing test never has to care whether a swing connected -
    /// the question under test is which pulse it happened on.
    /// </param>
    public ItemTemplate DefineWeapon(
        string key,
        string name,
        ItemSlot slot,
        int? delayPulses,
        string? verb = null,
        int damageMin = 1,
        int damageMax = 1,
        int attackBonus = 100)
    {
        var template = new ItemTemplate
        {
            Key = key,
            Name = name,
            Icon = "/",
            Slot = slot,
            AttackDelayPulses = delayPulses,
            AttackVerb = verb,
            BaseStats = new Dictionary<string, object>
            {
                ["damageMin"] = damageMin,
                ["damageMax"] = damageMax,
                ["bonus"] = attackBonus,
            },
        };

        _itemTemplateRepo.Add(template);
        ItemTemplates.LoadAsync(_itemTemplateRepo, CancellationToken.None).GetAwaiter().GetResult();
        return template;
    }

    /// <summary>Puts an instance of a template into a character's inventory and returns it.</summary>
    public ItemInstance GiveItem(
        PlayerActor actor,
        ItemTemplate template,
        Dictionary<string, object>? state = null)
    {
        var item = new ItemInstance
        {
            TemplateKey = template.Key,
            TemplateName = template.Name,
            OwnerCharacterId = actor.CharacterId,
            ResolvedStats = new Dictionary<string, object>(template.BaseStats),
            Value = template.BaseValue,
            State = state ?? [],
        };

        World.LoadCharacterItems(actor.CharacterId, [.. World.InventoryOf(actor.CharacterId), item]);
        return item;
    }

    /// <summary>Gives the character an instance of the template already equipped in that slot.</summary>
    public ItemInstance Equip(PlayerActor actor, ItemTemplate template, ItemSlot slot)
    {
        var item = GiveItem(actor, template);
        item.EquippedSlot = slot;
        return item;
    }

    /// <summary>
    /// Registers a mob template and puts one instance of it in a room, ready to fight.
    /// </summary>
    public Mob AddMob(
        string templateKey,
        RoomKey at,
        IEnumerable<MobAttack>? attacks = null,
        int health = 100,
        string name = "rat",
        int damageMin = 1,
        int damageMax = 1,
        Dictionary<string, object>? behavior = null)
    {
        MobTemplates.Put(new MobTemplate
        {
            Key = templateKey,
            Name = name,
            Icon = "r",
            Attacks = [.. attacks ?? []],
            Behavior = behavior ?? [],
        });

        var mob = new Mob
        {
            TemplateKey = templateKey,
            TemplateName = name,
            RoomKey = at.ToString(),
            Level = 1,
            Vitals = new Vitals
            {
                Health = health,
                HealthMax = health,
                Focus = 0,
                FocusMax = 0,
                Stamina = 0,
                StaminaMax = 0,
            },
            ResolvedStats = new Dictionary<string, object>
            {
                ["damageMin"] = damageMin,
                ["damageMax"] = damageMax,
                // As with DefineWeapon: connect every time, so the assertion is about timing.
                ["attackRating"] = 100,
            },
        };

        World.AddMob(mob);
        return mob;
    }

    private sealed class FakeItemTemplateRepository : IItemTemplateRepository
    {
        private readonly Dictionary<string, ItemTemplate> _templates = [];

        public void Add(ItemTemplate template) => _templates[template.Key] = template;

        public Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(_templates.GetValueOrDefault(key));

        public Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ItemTemplate>>([.. _templates.Values]);
    }

    /// <summary>Everything queued for this player since the last drain.</summary>
    public List<OutboundEvent> Drain(PlayerActor actor)
    {
        var events = new List<OutboundEvent>();
        var reader = _channels[actor.CharacterId].Reader;

        while (reader.TryRead(out var gameEvent))
        {
            events.Add(gameEvent);
        }

        return events;
    }

    /// <summary>All text produced for this player, flattened into one string.</summary>
    public string DrainText(PlayerActor actor) =>
        string.Concat(Drain(actor)
            .Where(e => e.Type == EventTypes.Text)
            .Cast<OutboundEvent>()
            .Select(e => string.Concat(((TextPayload)e.Payload).Spans.Select(s => s.T))));
}
