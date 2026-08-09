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

    /// <summary>
    /// Brings the ability rows in line with <see cref="AbilityCatalogue"/>.
    /// </summary>
    /// <remarks>
    /// Run on every startup and independent of the world check, because abilities are not
    /// starter *content* - they are what the level table promises. Seeding them once meant an
    /// existing database never received an ability added later, so a character levelling into it
    /// was granted a key with no row behind it and could not cast what the game said they knew.
    ///
    /// Upserts by key: a builder's balance edit to a row survives, but the row cannot go missing.
    /// </remarks>
    public static async Task<int> ReconcileAbilitiesAsync(
        DikuWebDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Abilities
            .ToDictionaryAsync(a => a.Key, StringComparer.Ordinal, cancellationToken);

        var added = 0;

        foreach (var entry in AbilityCatalogue.All)
        {
            if (existing.ContainsKey(entry.Key))
            {
                continue;
            }

            db.Abilities.Add(AbilityCatalogue.ToAbility(entry));
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return added;
    }
}
