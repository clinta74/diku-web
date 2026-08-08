using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Quests;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Mutations;

/// <summary>
/// Applies builder edits to the in-memory world, on the game loop thread (PLAN.md §7.3).
/// </summary>
/// <remarks>
/// Two jobs, in this order:
/// <list type="number">
/// <item><description>Validate against live world state and refuse cleanly - never throw. A
/// mutation that throws here would take down the loop, and a dead loop is a dead world for
/// every connected player.</description></item>
/// <item><description>Normalise the request into primitives, apply them, and return them so
/// persistence can replay the identical sequence.</description></item>
/// </list>
/// Occupant-facing side effects (relocating people out of a deleted room, pushing fresh room
/// and map events to anyone standing in an edited one) happen here too, because they must be
/// ordered with the edit itself.
/// </remarks>
public sealed class WorldMutationApplier(
    WorldState world,
    PlayerView view,
    EngineOptions options,
    QuestCache? questCache = null,
    MobTemplateCache? mobTemplateCache = null,
    ItemTemplateCache? itemTemplateCache = null)
{
    private const string UnfinishedTitle = "An Unfinished Room";

    private const string UnfinishedDescription =
        "Bare ground and unshaped air. Nobody has decided what this place is yet.";

    public MutationResult Apply(WorldChange request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request switch
        {
            UpsertWorld change => ApplyUpsertWorld(change),
            DeleteWorld change => ApplyDeleteWorld(change),
            UpsertZone change => ApplyUpsertZone(change),
            DeleteZone change => ApplyDeleteZone(change),
            UpsertRoom change => ApplyUpsertRoom(change),
            DeleteRoom change => ApplyDeleteRoom(change),
            SetExit change => ApplyLink(new LinkExit(change.From, change.Direction, change.To, Reciprocal: false)),
            RemoveExit change => ApplyUnlink(new UnlinkExit(change.From, change.Direction, Reciprocal: false)),
            LinkExit change => ApplyLink(change),
            UnlinkExit change => ApplyUnlink(change),
            DigRoom change => ApplyDig(change),
            RenameRoom change => ApplyRename(change),
            SetRoomFlag change => ApplySetFlag(change),
            UpsertMobTemplate change => ApplyUpsertMobTemplate(change),
            DeleteMobTemplate change => ApplyDeleteMobTemplate(change),
            UpsertItemTemplate change => ApplyUpsertItemTemplate(change),
            DeleteItemTemplate change => ApplyDeleteItemTemplate(change),
            UpsertSpawner change => ApplyUpsertSpawner(change),
            DeleteSpawner change => ApplyDeleteSpawner(change),
            UpsertQuest change => ApplyUpsertQuest(change),
            DeleteQuest change => ApplyDeleteQuest(change),
            _ => MutationResult.Fail(MutationError.Invalid, "Unsupported change."),
        };
    }

    // -----------------------------------------------------------------------
    // Worlds
    // -----------------------------------------------------------------------

    private MutationResult ApplyUpsertWorld(UpsertWorld change)
    {
        var existing = world.FindWorld(change.Key);

        if (existing is null)
        {
            world.PutWorld(new Domain.Worlds.World
            {
                Key = change.Key,
                Name = change.Name,
                Description = change.Description,
                SortOrder = change.SortOrder,
                Flags = change.Flags.Clone(),
            });
        }
        else
        {
            existing.Name = change.Name;
            existing.Description = change.Description;
            existing.SortOrder = change.SortOrder;
            existing.Flags = change.Flags.Clone();

            // World flags sit at the top of the inheritance chain, so a change here can flip
            // pvp for thousands of rooms at once. Everyone currently standing anywhere in it
            // needs a fresh room event.
            RefreshWorld(change.Key);
        }

        return MutationResult.Ok([change]);
    }

    private MutationResult ApplyDeleteWorld(DeleteWorld change)
    {
        if (world.FindWorld(change.Key) is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No world '{change.Key}'.");
        }

        var occupied = world.AllPlayers
            .Count(p => p.RoomKey.World == change.Key);

        if (occupied > 0)
        {
            return MutationResult.Fail(
                MutationError.Occupied,
                $"{occupied} character(s) are still in '{change.Key}'.");
        }

        foreach (var zone in world.ZonesIn(change.Key).ToList())
        {
            foreach (var room in world.RoomsIn(zone.Key).ToList())
            {
                world.RemoveRoom(room.Key);
            }

            world.RemoveZone(zone.Key);
        }

        world.RemoveWorld(change.Key);
        return MutationResult.Ok([change]);
    }

    // -----------------------------------------------------------------------
    // Zones
    // -----------------------------------------------------------------------

    private MutationResult ApplyUpsertZone(UpsertZone change)
    {
        if (world.FindWorld(change.WorldKey) is null)
        {
            return MutationResult.Fail(
                MutationError.NotFound,
                $"No world '{change.WorldKey}' to put this zone in.");
        }

        if (!change.Key.StartsWith(change.WorldKey + ".", StringComparison.Ordinal))
        {
            return MutationResult.Fail(
                MutationError.Invalid,
                $"Zone key '{change.Key}' must start with '{change.WorldKey}.'.");
        }

        var existing = world.FindZone(change.Key);

        if (existing is null)
        {
            world.PutZone(new Zone
            {
                Key = change.Key,
                WorldKey = change.WorldKey,
                Name = change.Name,
                Description = change.Description,
                MinLevel = change.MinLevel,
                MaxLevel = change.MaxLevel,
                Flags = change.Flags.Clone(),
            });
        }
        else
        {
            existing.Name = change.Name;
            existing.Description = change.Description;
            existing.MinLevel = change.MinLevel;
            existing.MaxLevel = change.MaxLevel;
            existing.Flags = change.Flags.Clone();

            RefreshZone(change.Key);
        }

        return MutationResult.Ok([change]);
    }

    /// <summary>
    /// The one destructive edit gated on being empty (PLAN.md §7.4). Everything else degrades
    /// gracefully; deleting a zone out from under people would have nowhere sensible to put
    /// them, since the zone entrance they would be moved to is what is being deleted.
    /// </summary>
    private MutationResult ApplyDeleteZone(DeleteZone change)
    {
        if (world.FindZone(change.Key) is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No zone '{change.Key}'.");
        }

        var occupants = world.AllPlayers
            .Where(p => p.RoomKey.ZoneKey == change.Key)
            .Select(p => p.Name)
            .ToList();

        if (occupants.Count > 0)
        {
            return MutationResult.Fail(
                MutationError.Occupied,
                $"Still occupied by {string.Join(", ", occupants)}.");
        }

        foreach (var room in world.RoomsIn(change.Key).ToList())
        {
            world.RemoveRoom(room.Key);
        }

        world.RemoveZone(change.Key);
        return MutationResult.Ok([change]);
    }

    // -----------------------------------------------------------------------
    // Rooms
    // -----------------------------------------------------------------------

    private MutationResult ApplyUpsertRoom(UpsertRoom change)
    {
        if (world.FindZone(change.ZoneKey) is null)
        {
            return MutationResult.Fail(
                MutationError.NotFound,
                $"No zone '{change.ZoneKey}' to put this room in.");
        }

        if (change.Key.ZoneKey != change.ZoneKey)
        {
            return MutationResult.Fail(
                MutationError.Invalid,
                $"Room key '{change.Key}' does not belong to zone '{change.ZoneKey}'.");
        }

        var existing = world.FindRoom(change.Key);

        if (existing is null)
        {
            world.PutRoom(ToRoom(change, exits: []));
        }
        else
        {
            existing.Title = change.Title;
            existing.Description = change.Description;
            existing.Flags = change.Flags.Clone();
            existing.Grid = [.. change.Grid];
            existing.Legend = new Dictionary<string, string>(change.Legend, StringComparer.Ordinal);
            existing.EditorX = change.EditorX;
            existing.EditorY = change.EditorY;
        }

        // Live edits reach anyone standing here without them relogging (PLAN.md §3.5).
        RefreshOccupants(change.Key);
        return MutationResult.Ok([change], change.Key);
    }

    private MutationResult ApplyDeleteRoom(DeleteRoom change)
    {
        var room = world.FindRoom(change.Key);
        if (room is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No room '{change.Key}'.");
        }

        // §7.4: occupants are moved to the zone entrance, never orphaned. Resolved before the
        // removal, because the fallbacks have to be rooms that still exist afterwards.
        var refuge = FindRefuge(change.Key);
        var stranded = world.OccupantsOf(change.Key).ToList();

        // Moved out first, then the room goes. The other order would leave them momentarily
        // standing in a room that no longer exists, and every read of their location between
        // those two lines would be of something that is not there.
        foreach (var actor in stranded)
        {
            actor.SendSys("The ground goes out from under you. Someone unmade this place.", SysKinds.Warning);
            world.Move(actor, refuge);
        }

        world.RemoveRoom(change.Key);

        foreach (var actor in stranded)
        {
            view.SendRoom(world, actor, verbose: true);
        }

        if (stranded.Count > 0)
        {
            view.RefreshRoom(world, refuge);
        }

        return MutationResult.Ok([change]);
    }

    /// <summary>
    /// Where to put people standing in a room that just disappeared: the zone entrance, then
    /// any other room in the zone, then the world's starting room. Each step falls through
    /// because live editing means the obvious answer may also have been deleted.
    /// </summary>
    private RoomKey FindRefuge(RoomKey deleted)
    {
        var sibling = world.RoomsIn(deleted.ZoneKey).FirstOrDefault(r => r.Key != deleted);
        if (sibling is not null)
        {
            return sibling.Key;
        }

        return world.FindRoom(options.StartingRoom) is not null
            ? options.StartingRoom
            : world.AllRooms.FirstOrDefault()?.Key ?? options.StartingRoom;
    }

    private MutationResult ApplyRename(RenameRoom change)
    {
        var room = world.FindRoom(change.From);
        if (room is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No room '{change.From}'.");
        }

        if (change.From == change.To)
        {
            return MutationResult.Fail(MutationError.Invalid, "The room already has that key.");
        }

        if (world.FindRoom(change.To) is not null)
        {
            return MutationResult.Fail(MutationError.Conflict, $"'{change.To}' already exists.");
        }

        if (world.FindZone(change.To.ZoneKey) is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No zone '{change.To.ZoneKey}'.");
        }

        var applied = new List<WorldChange>
        {
            new UpsertRoom(
                change.To,
                change.To.ZoneKey,
                room.Title,
                room.Description,
                room.Flags.Clone(),
                [.. room.Grid],
                new Dictionary<string, string>(room.Legend, StringComparer.Ordinal),
                room.EditorX,
                room.EditorY),
        };

        // Carry the room's own exits across to the new key.
        foreach (var exit in room.Exits)
        {
            applied.Add(new SetExit(change.To, exit.Direction, exit.ToRoomKey));
        }

        // And repoint everything that pointed at the old key, in the same mutation - otherwise
        // renaming a dug room silently orphans its neighbours (PLAN.md §7.6).
        var inbound = world.ExitsPointingAt(change.From).ToList();
        foreach (var exit in inbound)
        {
            applied.Add(new SetExit(exit.FromRoomKey, exit.Direction, change.To));
        }

        applied.Add(new DeleteRoom(change.From));

        var occupants = world.OccupantsOf(change.From).ToList();

        world.PutRoom(ToRoom((UpsertRoom)applied[0], room.Exits.Select(e => (e.Direction, e.ToRoomKey))));
        foreach (var exit in inbound)
        {
            exit.ToRoomKey = change.To;
        }

        // Occupants follow the room rather than being evicted: from their point of view
        // nothing happened, which is the correct experience for a key change. Moved before
        // the old key is dropped so nobody is ever in a room that does not exist.
        foreach (var actor in occupants)
        {
            world.Move(actor, change.To);
        }

        world.RemoveRoom(change.From);

        foreach (var actor in occupants)
        {
            view.SendRoom(world, actor, verbose: false);
        }

        return MutationResult.Ok(applied, change.To);
    }

    private MutationResult ApplySetFlag(SetRoomFlag change)
    {
        var room = world.FindRoom(change.Key);
        if (room is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No room '{change.Key}'.");
        }

        if (!RoomFlags.IsKnown(change.Flag))
        {
            // Refused rather than stored. Unknown keys are preserved when they arrive from the
            // database (§4.10), but there is no reason to let a builder type a new one into
            // existence - it would be a flag nothing ever reads.
            return MutationResult.Fail(
                MutationError.Invalid,
                $"'{change.Flag}' is not a known room flag.");
        }

        var flags = room.Flags.Clone();

        if (change.Value is { } value)
        {
            flags.Set(change.Flag, value);
        }
        else
        {
            flags.Clear(change.Flag);
        }

        room.Flags = flags;
        RefreshOccupants(change.Key);

        return MutationResult.Ok([ToUpsert(room)], change.Key);
    }

    // -----------------------------------------------------------------------
    // Exits
    // -----------------------------------------------------------------------

    private MutationResult ApplyLink(LinkExit change)
    {
        var from = world.FindRoom(change.From);
        if (from is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No room '{change.From}'.");
        }

        // The destination is deliberately not required to exist. Live editing has no publish
        // gate to defer the link to, so an exit may be authored before its target (§7.4).
        var applied = new List<WorldChange> { new SetExit(change.From, change.Direction, change.To) };
        PutExit(from, change.Direction, change.To);

        if (change.Reciprocal && world.FindRoom(change.To) is { } to)
        {
            var back = change.Direction.Opposite();
            applied.Add(new SetExit(change.To, back, change.From));
            PutExit(to, back, change.From);
            RefreshOccupants(change.To);
        }

        RefreshOccupants(change.From);
        return MutationResult.Ok(applied, change.From);
    }

    private MutationResult ApplyUnlink(UnlinkExit change)
    {
        var from = world.FindRoom(change.From);
        if (from is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No room '{change.From}'.");
        }

        var exit = from.ExitTo(change.Direction);
        if (exit is null)
        {
            return MutationResult.Fail(
                MutationError.NotFound,
                $"There is no {change.Direction.ToLowerName()} exit here.");
        }

        var applied = new List<WorldChange> { new RemoveExit(change.From, change.Direction) };
        from.Exits.Remove(exit);

        if (change.Reciprocal && world.FindRoom(exit.ToRoomKey) is { } to)
        {
            var back = to.ExitTo(change.Direction.Opposite());

            // Only remove the far side if it actually points back here. A neighbour whose exit
            // leads somewhere else is a separate passage and is none of this edit's business.
            if (back is not null && back.ToRoomKey == change.From)
            {
                applied.Add(new RemoveExit(to.Key, back.Direction));
                to.Exits.Remove(back);
                RefreshOccupants(to.Key);
            }
        }

        RefreshOccupants(change.From);
        return MutationResult.Ok(applied, change.From);
    }

    // -----------------------------------------------------------------------
    // Dig (PLAN.md §7.6)
    // -----------------------------------------------------------------------

    private MutationResult ApplyDig(DigRoom change)
    {
        var from = world.FindRoom(change.From);
        if (from is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No room '{change.From}'.");
        }

        var existingExit = from.ExitTo(change.Direction);

        if (existingExit is not null && world.FindRoom(existingExit.ToRoomKey) is not null)
        {
            return MutationResult.Fail(
                MutationError.Conflict,
                $"There is already a room {change.Direction.ToLowerName()} of here.");
        }

        // Materialize: an exit already names a room that does not exist, so reuse its key and
        // the dangling link resolves itself. Otherwise generate one.
        var materializing = existingExit is not null;
        var zoneKey = change.ZoneKey ?? (materializing ? existingExit!.ToRoomKey.ZoneKey : from.ZoneKey);

        if (world.FindZone(zoneKey) is null)
        {
            return MutationResult.Fail(MutationError.NotFound, $"No zone '{zoneKey}'.");
        }

        RoomKey newKey;
        if (materializing)
        {
            newKey = existingExit!.ToRoomKey;
        }
        else if (change.NewRoomKey is { } requested)
        {
            if (world.FindRoom(requested) is not null)
            {
                return MutationResult.Fail(MutationError.Conflict, $"'{requested}' already exists.");
            }

            newKey = requested;
        }
        else if (!TryGenerateKey(zoneKey, out newKey))
        {
            return MutationResult.Fail(
                MutationError.Conflict,
                $"Could not find a free room key in '{zoneKey}'.");
        }

        var (x, y) = OffsetFor(from, change.Direction);

        // Born unfinished: placeholder text, no grid art so §7.4's default rectangle renders,
        // and the flag that puts it on the zone's build to-do list.
        var flags = new FlagSet();
        flags.Set(RoomFlags.Unfinished.Key, true);

        var upsert = new UpsertRoom(
            newKey,
            zoneKey,
            UnfinishedTitle,
            UnfinishedDescription,
            flags,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            x,
            y);

        var applied = new List<WorldChange> { upsert };
        world.PutRoom(ToRoom(upsert, exits: []));

        if (!materializing)
        {
            applied.Add(new SetExit(change.From, change.Direction, newKey));
            PutExit(from, change.Direction, newKey);
        }

        if (change.Reciprocal)
        {
            var back = change.Direction.Opposite();
            applied.Add(new SetExit(newKey, back, change.From));
            PutExit(world.FindRoom(newKey)!, back, change.From);
        }

        RefreshOccupants(change.From);
        return MutationResult.Ok(applied, newKey);
    }

    /// <summary>
    /// Provisional keys are <c>room-N</c> with the lowest free N (PLAN.md §7.6). Nobody is
    /// prompted for a slug mid-walk; rename later, which rewrites inbound exits for you.
    /// </summary>
    private bool TryGenerateKey(string zoneKey, out RoomKey key)
    {
        for (var n = 1; n <= 9999; n++)
        {
            if (RoomKey.TryParse($"{zoneKey}.room-{n}", out var candidate)
                && world.FindRoom(candidate) is null)
            {
                key = candidate;
                return true;
            }
        }

        key = default;
        return false;
    }

    /// <summary>
    /// Canvas placement is automatic, one step from the source in the dug direction, so
    /// walk-building produces a zone canvas that already reads like a map (PLAN.md §7.6).
    /// Up and down reuse the source cell - the canvas is 2D and levels are marked, not moved.
    /// </summary>
    private static (int? X, int? Y) OffsetFor(Room from, Direction direction)
    {
        if (from.EditorX is not { } x || from.EditorY is not { } y)
        {
            return (null, null);
        }

        return direction switch
        {
            Direction.North => (x, y - 1),
            Direction.South => (x, y + 1),
            Direction.East => (x + 1, y),
            Direction.West => (x - 1, y),
            _ => (x, y),
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PutExit(Room room, Direction direction, RoomKey to)
    {
        var existing = room.ExitTo(direction);

        if (existing is not null)
        {
            existing.ToRoomKey = to;
            return;
        }

        room.Exits.Add(new RoomExit
        {
            FromRoomKey = room.Key,
            Direction = direction,
            ToRoomKey = to,
        });
    }

    private static Room ToRoom(UpsertRoom change, IEnumerable<(Direction Direction, RoomKey To)> exits)
    {
        var room = new Room
        {
            Key = change.Key,
            ZoneKey = change.ZoneKey,
            Title = change.Title,
            Description = change.Description,
            Flags = change.Flags.Clone(),
            Grid = [.. change.Grid],
            Legend = new Dictionary<string, string>(change.Legend, StringComparer.Ordinal),
            EditorX = change.EditorX,
            EditorY = change.EditorY,
        };

        foreach (var (direction, to) in exits)
        {
            room.Exits.Add(new RoomExit
            {
                FromRoomKey = change.Key,
                Direction = direction,
                ToRoomKey = to,
            });
        }

        return room;
    }

    public static UpsertRoom ToUpsert(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        return new UpsertRoom(
            room.Key,
            room.ZoneKey,
            room.Title,
            room.Description,
            room.Flags.Clone(),
            [.. room.Grid],
            new Dictionary<string, string>(room.Legend, StringComparer.Ordinal),
            room.EditorX,
            room.EditorY);
    }

    private void RefreshOccupants(RoomKey key)
    {
        foreach (var actor in world.OccupantsOf(key).ToList())
        {
            view.SendRoom(world, actor, verbose: true);
        }
    }

    private void RefreshZone(string zoneKey)
    {
        foreach (var actor in world.AllPlayers.Where(p => p.RoomKey.ZoneKey == zoneKey).ToList())
        {
            view.SendRoom(world, actor, verbose: false);
        }
    }

    private void RefreshWorld(string worldKey)
    {
        foreach (var actor in world.AllPlayers.Where(p => p.RoomKey.World == worldKey).ToList())
        {
            view.SendRoom(world, actor, verbose: false);
        }
    }

    // -----------------------------------------------------------------------
    // Templates and Spawners (Phase 3)
    // -----------------------------------------------------------------------

    // Templates and quests live in side caches rather than in WorldState, but they are still
    // in-memory world data, so the applier maintains them exactly as it maintains rooms - on the
    // loop thread, under the single-writer rule. Without this a builder's save reached the
    // spawner sweep (which reads repositories) but not shops or quest dialogue (which read these
    // caches) until the next restart, contradicting the "live immediate" decision in PLAN.md §1.

    private MutationResult ApplyUpsertMobTemplate(UpsertMobTemplate change)
    {
        mobTemplateCache?.Put(new MobTemplate
        {
            Key = change.Key,
            Name = change.Name,
            Description = change.Description,
            Icon = change.Icon,
            Level = change.Level,
            WanderIntervalPulses = change.WanderIntervalPulses,
            BaseStats = new Dictionary<string, object>(change.BaseStats, StringComparer.Ordinal),
            BaseXp = change.BaseXp,
            BaseGold = change.BaseGold,
            Behavior = new Dictionary<string, object>(change.Behavior, StringComparer.Ordinal),
            Loot = [.. change.Loot],
            Attacks = [.. change.Attacks],
        });

        return MutationResult.Ok([change]);
    }

    private MutationResult ApplyDeleteMobTemplate(DeleteMobTemplate change)
    {
        mobTemplateCache?.Remove(change.Key);
        return MutationResult.Ok([change]);
    }

    private MutationResult ApplyUpsertItemTemplate(UpsertItemTemplate change)
    {
        itemTemplateCache?.Put(new ItemTemplate
        {
            Key = change.Key,
            Name = change.Name,
            Description = change.Description,
            Icon = change.Icon,
            Slot = change.Slot,
            Weight = change.Weight,
            BaseValue = change.BaseValue,
            BaseStats = new Dictionary<string, object>(change.BaseStats, StringComparer.Ordinal),
            AttackDelayPulses = change.AttackDelayPulses,
            AttackVerb = change.AttackVerb,
        });

        return MutationResult.Ok([change]);
    }

    private MutationResult ApplyDeleteItemTemplate(DeleteItemTemplate change)
    {
        itemTemplateCache?.Remove(change.Key);
        return MutationResult.Ok([change]);
    }

    private MutationResult ApplyUpsertSpawner(UpsertSpawner change) =>
        MutationResult.Ok([change]);

    private MutationResult ApplyDeleteSpawner(DeleteSpawner change) =>
        MutationResult.Ok([change]);

    // -----------------------------------------------------------------------
    // Quests (Phase 5.2b)
    // -----------------------------------------------------------------------

    private MutationResult ApplyUpsertQuest(UpsertQuest change)
    {
        questCache?.Put(new Quest
        {
            Key = change.Key,
            ZoneKey = change.ZoneKey ?? string.Empty,
            Name = change.Name,
            Summary = change.Summary,
            Description = change.Description,
            GiverMobKey = change.GiverMobKey,
            TurninMobKey = change.TurninMobKey,
            RequiredItemKey = change.RequiredItemKey,
            RequiredCount = change.RequiredCount,
            RewardXp = change.RewardXp,
            RewardGold = change.RewardGold,
            RewardItemKey = change.RewardItemKey,
            RewardItemCount = change.RewardItemCount,
            PrerequisiteQuestKeys = [.. change.PrerequisiteQuestKeys],
            IsRepeatable = change.IsRepeatable,
            Dialogue = new Dictionary<string, string>(change.Dialogue, StringComparer.Ordinal),
            SortOrder = change.SortOrder,
        });

        return MutationResult.Ok([change]);
    }

    private MutationResult ApplyDeleteQuest(DeleteQuest change)
    {
        questCache?.Remove(change.Key);
        return MutationResult.Ok([change]);
    }
}
