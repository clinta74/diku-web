using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Worlds;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Persistence.Seeding;

/// <summary>
/// PLAN.md Phase 1: a hand-built starter zone, enough to have somewhere to walk before the
/// world builder exists in Phase 2. Once the builder ships this is only used to give a fresh
/// database something in it.
/// </summary>
public static class StarterWorldSeeder
{
    public const string WorldKey = "aldenmoor";
    public const string ZoneKey = "aldenmoor.millbrook";

    /// <summary>Where new characters begin, and where anyone in a deleted room is sent.</summary>
    public static RoomKey StartingRoom { get; } = RoomKey.Parse("aldenmoor.millbrook.north-gate");

    /// <summary>
    /// Authored one way only. Reciprocal exits are generated, because hand-writing both
    /// halves is exactly how one-way passages get created by accident.
    /// </summary>
    private static readonly (string From, Direction Direction, string To)[] Links =
    [
        ("old-mill", Direction.South, "millpond"),
        ("millpond", Direction.East, "north-gate"),
        ("north-gate", Direction.North, "hill-road"),
        ("north-gate", Direction.East, "market-row"),
        ("north-gate", Direction.South, "village-green"),
        ("market-row", Direction.South, "tavern-door"),
        ("village-green", Direction.West, "smithy"),
        ("village-green", Direction.East, "tavern-door"),
        ("village-green", Direction.South, "well-yard"),
        ("tavern-door", Direction.South, "tavern-common"),
        ("well-yard", Direction.West, "chapel-steps"),
        ("chapel-steps", Direction.Down, "chapel-nave"),
    ];

    /// <summary>
    /// The starter zone's rooms.
    /// </summary>
    /// <remarks>
    /// Grids are 21x9 - roughly double the old 11x5 - and drawn with box-drawing borders instead
    /// of hashes. The extra room is what makes furniture legible: a tavern with four tables and a
    /// bar reads as a tavern, where at 11x5 it could only afford a suggestion of one. Six of these
    /// rooms had no art at all and rendered as a blank rectangle.
    ///
    /// Every glyph must be a single BMP character. <c>RoomLayoutService</c> indexes terrain rows
    /// per <c>char</c>, so anything outside the basic plane would be split across two cells and
    /// draw as mojibake - which rules out emoji. Box-drawing, block, and geometric shapes are all
    /// safe, and are what a monospace font renders at exactly one cell wide.
    /// </remarks>
    private static readonly RoomSeed[] Rooms =
    [
        new("old-mill", "The Old Mill", 0, 0,
            "The mill wheel has not turned in years. Its paddles hang black with rot, and "
            + "the axle beam sags where the water no longer reaches it. Somewhere inside, "
            + "something small scurries along a rafter.",
            [
                "┌───────────────────┐",
                "│░░...............░░│",
                "│..┌───────────┐....│",
                "│..│.....◎.....│....│",
                "│..└───────────┘....│",
                "│░.................░│",
                "├───────────────────┤",
                "│≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈│",
                "└───────────────────┘",
            ],
            new() { ["."] = "floor", ["≈"] = "water", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["├"] = "wall", ["┤"] = "wall", ["░"] = "rubble", ["◎"] = "well" }),

        new("millpond", "The Millpond", 0, 1,
            "Still green water, thick with duckweed, holds the sky upside down. Reeds crowd "
            + "the near bank. A heron stands motionless in the shallows and does not "
            + "acknowledge you.",
            [
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"≈≈≈≈≈≈≈≈≈≈≈\"\"\"\"\"\"",
                "\"\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"",
                "\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"\"\"\"≈≈≈≈≈≈≈≈≈≈≈\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["≈"] = "water" }),

        new("hill-road", "The Hill Road", 2, 0,
            "Cart ruts climb north into brown hills and lose themselves in the gorse. The "
            + "wind up here carries no smell of the village at all.",
            [
                "\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"·\"\"\"\"\"\"♣\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"·\"·\"\"\"\"\"\"\"\"\"\"",
                "\"♣\"\"\"\"\"\"·\"\"·\"\"\"♣\"\"\"\"\"",
                "\"\"\"\"\"\"\"·\"\"\"\"·\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"·\"\"\"\"\"\"·\"\"\"♣\"\"\"",
                "\"\"\"\"\"·\"\"\"\"\"\"\"\"·\"\"\"\"\"\"",
                "\"\"\"\"·\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["·"] = "path", ["♣"] = "tree" }),

        new("north-gate", "The North Gate", 2, 1,
            "A weathered portcullis stands half-raised above the road, its iron teeth furred "
            + "with rust. Nobody has lowered it in living memory, and the winch house has "
            + "been given over to swallows.",
            [
                "┌────────╥─╥────────┐",
                "│........│.│........│",
                "│........│.│........│",
                "│░.......│.│.......░│",
                "│........···........│",
                "│...................│",
                "│░.................░│",
                "│........···........│",
                "└────────╨─╨────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["╥"] = "gate", ["╨"] = "gate", ["░"] = "rubble" }),

        new("market-row", "Market Row", 4, 1,
            "Empty trestle tables lean against the shopfronts, stacked for a market day that "
            + "is not today. Straw and broken crate slats drift against the kerb.",
            [
                "┌───────────────────┐",
                "│▬▬...▬▬...▬▬...▬▬..│",
                "│...................│",
                "│░.................░│",
                "│...................│",
                "│..▬▬...▬▬...▬▬...▬▬│",
                "│...................│",
                "│░░...............░░│",
                "└─────────··────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["░"] = "rubble", ["▬"] = "table" }),

        new("village-green", "The Village Green", 2, 2,
            "Cropped grass, a lightning-split oak, and a stone bench worn smooth by "
            + "generations of sitting. Paths run off in every direction, all of them shorter "
            + "than they look.",
            [
                "\"\"\"\"\"\"\"\"\"··\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"··\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "·········\"♠\"·········",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"═══\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"·\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"··\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["·"] = "path", ["═"] = "bench", ["♠"] = "oak", ["♣"] = "tree" }),

        new("smithy", "The Smithy", 0, 2,
            "Heat rolls out of the open front like a held breath. Horseshoes hang in graded "
            + "rows along the wall, and the anvil bears a bright crescent where the hammer "
            + "always lands.",
            [
                "┌───────────────────┐",
                "│▲▲.......░.........│",
                "│▲▲.................│",
                "│.........▄.........│",
                "│...................│",
                "│░...............═══│",
                "│...................│",
                "│...................│",
                "└─────────··────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["═"] = "bench", ["▄"] = "anvil", ["░"] = "rubble", ["▲"] = "forge" }),

        new("tavern-door", "Outside the Drowned Rat", 4, 2,
            "A painted sign shows a rodent floating cheerfully in a tankard. The door beneath "
            + "it stands open, and warm noise spills out into the street.",
            [
                "┌────────┐░░░┌──────┐",
                "│........│...│......│",
                "│........└───┘......│",
                "│..................·│",
                "└───────╥╥─────────·│",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"·\"",
                "\"\"♣\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"·\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"·\"\"\"",
                "················\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["╥"] = "gate", ["░"] = "rubble", ["♣"] = "tree" }),

        new("tavern-common", "The Drowned Rat", 4, 3,
            "Low beams, lower conversation, and a fire that has been burning so long the "
            + "stones behind it have gone the colour of old tea. The floor is sticky in a way "
            + "nobody wants explained.",
            [
                "┌───────────────────┐",
                "│..▬▬.....▬▬........│",
                "│..▬▬.....▬▬........│",
                "│................▲▲.│",
                "│...................│",
                "│..▬▬.....▬▬........│",
                "│..▬▬.....▬▬........│",
                "│═══════════════....│",
                "└─────────··────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["═"] = "bench", ["▬"] = "table", ["▲"] = "forge" }),

        new("well-yard", "The Well Yard", 2, 3,
            "A round stone well with a rope that disappears into dark. Somebody has left a "
            + "chipped cup on the rim for whoever comes thirsty next.",
            [
                "\"\"\"\"\"\"\"\"\"··\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"┌───┐\"\"·\"\"\"\"\"\"\"\"\"\"",
                "···│.◎.│·············",
                "\"\"\"└───┘\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"·\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["◎"] = "well", ["♣"] = "tree" }),

        new("chapel-steps", "The Chapel Steps", 0, 3,
            "Shallow steps, hollowed in the middle by centuries of feet, run down to a door "
            + "of grey wood. Moss has taken the north side of every one of them.",
            [
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"\"\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"┌─────────┐\"\"\"\"\"",
                "·····│≡≡≡≡≡≡≡≡≡│\"\"\"\"\"",
                "\"\"\"\"\"│≡≡≡≡≡≡≡≡≡│\"\"\"\"\"",
                "\"\"\"\"\"│≡≡≡≡≡≡≡≡≡│\"\"\"\"\"",
                "\"\"\"\"\"└────╥╥───┘\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["·"] = "path", ["≡"] = "stairs", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["╥"] = "gate", ["♣"] = "tree" }),

        new("chapel-nave", "The Chapel Nave", 0, 4,
            "Cold, quiet, and smelling of stone dust. Six rows of benches face an altar with "
            + "nothing on it. The light from the high windows arrives already tired.",
            [
                "┌─────────··────────┐",
                "│═══════..═════════.│",
                "│═══════..═════════.│",
                "│═══════..═════════.│",
                "│........†..........│",
                "│═══════..═════════.│",
                "│═══════..═════════.│",
                "│═══════..═════════.│",
                "└───────────────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["†"] = "altar", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["═"] = "bench" })
    ];

    public static async Task<bool> SeedAsync(
        DikuWebDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.Worlds.AnyAsync(cancellationToken))
        {
            return false;
        }

        db.Worlds.Add(new World
        {
            Key = WorldKey,
            Name = "Aldenmoor",
            Description = "A damp northern kingdom of failing villages and older stone.",
            SortOrder = 0,
        });

        db.Zones.Add(new Zone
        {
            Key = ZoneKey,
            WorldKey = WorldKey,
            Name = "Millbrook",
            Description = "A village that has been slowly emptying for two generations.",
            MinLevel = 1,
            MaxLevel = 5,
        });

        foreach (var seed in Rooms)
        {
            db.Rooms.Add(new Room
            {
                Key = RoomKey.Create(WorldKey, "millbrook", seed.Slug),
                ZoneKey = ZoneKey,
                Title = seed.Title,
                Description = seed.Description,
                Grid = [.. seed.Grid],
                Legend = new Dictionary<string, string>(seed.Legend, StringComparer.Ordinal),
                EditorX = seed.EditorX,
                EditorY = seed.EditorY,
            });
        }

        foreach (var (from, direction, to) in Links)
        {
            var fromKey = RoomKey.Create(WorldKey, "millbrook", from);
            var toKey = RoomKey.Create(WorldKey, "millbrook", to);

            db.RoomExits.Add(new RoomExit
            {
                FromRoomKey = fromKey,
                Direction = direction,
                ToRoomKey = toKey,
            });

            db.RoomExits.Add(new RoomExit
            {
                FromRoomKey = toKey,
                Direction = direction.Opposite(),
                ToRoomKey = fromKey,
            });
        }

        // Abilities are reconciled separately, on every startup - see ReconcileAbilitiesAsync.
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private sealed record RoomSeed(
        string Slug,
        string Title,
        int EditorX,
        int EditorY,
        string Description,
        string[] Grid,
        Dictionary<string, string> Legend);

    /// <summary>What a reconcile did, for the startup log.</summary>
    public readonly record struct AbilityReconciliation(int Added, int Updated, int Removed)
    {
        public bool ChangedAnything => Added > 0 || Updated > 0 || Removed > 0;
    }

    /// <summary>
    /// Makes the ability rows match <see cref="AbilityCatalogue"/> exactly: adds what is missing,
    /// updates what has drifted, and deletes what the catalogue no longer defines.
    /// </summary>
    /// <remarks>
    /// Run on every startup and independent of the world check, because abilities are not starter
    /// *content* - they are what the level table promises. Seeding them once meant an existing
    /// database never received an ability added later, so a character levelling into it was
    /// granted a key with no row behind it and could not cast what the game said they knew.
    ///
    /// The catalogue is authoritative in all three directions, which is safe precisely because
    /// abilities are the one kind of content with no builder UI and no foreign keys pointing at
    /// them. Nothing else can author a row, so any difference is the catalogue having moved:
    ///
    /// - **Missing** rows are the original bug - a promised ability that cannot be cast.
    /// - **Drifted** rows are the same staleness one level down: retuning a cost or a cooldown
    ///   here would otherwise never reach a database that already had the row.
    /// - **Orphaned** rows are keys the catalogue dropped. <c>warden.slash</c> and
    ///   <c>warden.parry</c> became <c>warden.kick</c> and a passive, and without this the old
    ///   two would sit in the table forever, castable by nobody and confusing to read.
    ///
    /// If abilities ever become builder-editable, this has to become a merge instead - at that
    /// point a row differing from the catalogue stops meaning "stale".
    /// </remarks>
    public static async Task<AbilityReconciliation> ReconcileAbilitiesAsync(
        DikuWebDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Abilities
            .ToDictionaryAsync(a => a.Key, StringComparer.Ordinal, cancellationToken);

        var catalogue = AbilityCatalogue.All.ToDictionary(e => e.Key, StringComparer.Ordinal);

        var toAdd = new List<AbilityCatalogue.Entry>();
        var toRemove = new List<Ability>();
        var updated = 0;

        foreach (var (key, entry) in catalogue)
        {
            if (!existing.TryGetValue(key, out var row))
            {
                toAdd.Add(entry);
                continue;
            }

            if (Matches(entry, row))
            {
                continue;
            }

            // Replaced rather than edited: Ability is init-only because content rows are meant
            // to be immutable. Rewriting one in place would mean opening every property up for
            // anything to mutate, to save a delete and an insert on a table of thirty rows.
            toRemove.Add(row);
            toAdd.Add(entry);
            updated++;
        }

        foreach (var (key, row) in existing)
        {
            if (!catalogue.ContainsKey(key))
            {
                toRemove.Add(row);
            }
        }

        var removed = toRemove.Count - updated;

        if (toRemove.Count == 0 && toAdd.Count == 0)
        {
            return new AbilityReconciliation(0, 0, 0);
        }

        // Two passes, because a replaced row is deleted and re-inserted under the same primary
        // key. In one SaveChanges the insert can be ordered before the delete and collide.
        if (toRemove.Count > 0)
        {
            db.Abilities.RemoveRange(toRemove);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (toAdd.Count > 0)
        {
            db.Abilities.AddRange(toAdd.Select(AbilityCatalogue.ToAbility));
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AbilityReconciliation(toAdd.Count - updated, updated, removed);
    }

    /// <summary>
    /// Whether a stored row already says exactly what the catalogue says. A startup that finds
    /// everything in agreement writes nothing and logs nothing.
    /// </summary>
    private static bool Matches(AbilityCatalogue.Entry entry, Ability row) =>
        row.Name == entry.Name &&
        row.Description == entry.Description &&
        row.CostType == entry.CostType &&
        row.CostValue == entry.CostValue &&
        row.CooldownPulses == entry.CooldownPulses &&
        row.CastTimePulses == entry.CastTimePulses &&
        row.TargetingType == entry.TargetingType &&
        row.EffectKey == entry.EffectKey &&
        SameParams(row.EffectParams, entry.EffectParams);

    private static bool SameParams(
        IReadOnlyDictionary<string, string> a,
        IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count &&
        a.All(kv => b.TryGetValue(kv.Key, out var other) && other == kv.Value);
}
