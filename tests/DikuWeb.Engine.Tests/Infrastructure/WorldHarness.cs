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
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
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
/// Wires the real WorldState, CommandRegistry, and PlayerView without the hosted service, so
/// command behaviour can be asserted synchronously. No timers, no sleeping, no database.
/// </summary>
internal sealed class WorldHarness
{
    private readonly Dictionary<Guid, Channel<OutboundEvent>> _channels = [];

    public WorldHarness()
    {
        var random = new DikuWeb.Domain.Randomness.SeededRandomSource(42);
        World = new WorldState(random);
        Commands = new CommandRegistry(itemTemplateCache: ItemTemplates, clock: Clock);
        View = new PlayerView(new RoomLayoutService());
        Options = new EngineOptions { StartingRoom = RoomKey.Parse("test.zone.west") };
        Applier = new WorldMutationApplier(World, View, Options);
        Writes = new RecordingWriteQueue();
        Editor = new LoopWorldEditor(Applier, Writes);
        Admin = new RecordingAdminQueue();
        Combat = new EngineCombatSystem(Options, View, ItemTemplates, MobTemplates);
        Abilities = new AbilitySystem(Clock);
    }

    public WorldState World { get; }

    /// <summary>Time under the test's control, so per-attack timing is exact rather than raced.</summary>
    public ManualGameClock Clock { get; } = new();

    /// <summary>Mob templates combat reads attack lists from.</summary>
    public MobTemplateCache MobTemplates { get; } = new();

    public EngineCombatSystem Combat { get; }

    public AbilitySystem Abilities { get; }

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

    private readonly FakeItemTemplateRepository _itemTemplateRepo = new();

    public CommandRegistry Commands { get; }

    public PlayerView View { get; }

    public EngineOptions Options { get; }

    public WorldMutationApplier Applier { get; }

    public RecordingWriteQueue Writes { get; }

    public LoopWorldEditor Editor { get; }

    public RecordingAdminQueue Admin { get; }

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
            ItemTemplates = ItemTemplates,
            Verb = verb,
            Argument = argument,
        };

        definition.Handler(context);
        return context;
    }

    /// <summary>Registers an item template (so its slot and description resolve) and returns it.</summary>
    public ItemTemplate DefineItem(string key, string name, ItemSlot? slot, string description = "")
    {
        var template = new ItemTemplate
        {
            Key = key,
            Name = name,
            Icon = "$",
            Slot = slot,
            Description = description,
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
    public ItemInstance GiveItem(PlayerActor actor, ItemTemplate template)
    {
        var item = new ItemInstance
        {
            TemplateKey = template.Key,
            TemplateName = template.Name,
            OwnerCharacterId = actor.CharacterId,
            ResolvedStats = new Dictionary<string, object>(template.BaseStats),
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
        int damageMax = 1)
    {
        MobTemplates.Put(new MobTemplate
        {
            Key = templateKey,
            Name = name,
            Icon = "r",
            Attacks = [.. attacks ?? []],
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
