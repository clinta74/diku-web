using Muwbta.Domain.Abilities;
using Muwbta.Domain.Worlds;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Persistence.Seeding;

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
        MuwbtaDbContext db,
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
        // So is the starter configuration - see ReconcileStarterConfigurationAsync.
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The starter configuration's key (PLAN.md §4.16).</summary>
    public const string ConfigurationKey = "aldenmoor-starter";

    /// <summary>
    /// Plants a <see cref="GameConfiguration"/> matching the engine's compiled fallback, so the
    /// value the server is obeying is visible and editable rather than implicit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="SeedAsync"/>, because that one skips a database that already has
    /// a world.</b> Every development database created before configurations existed has a world
    /// and no configuration, and would never have got one - the Setup tab would show an empty list
    /// beside a server quietly starting people in Millbrook, which is precisely the implicit state
    /// §4.16 exists to end.
    /// </para>
    /// <para>
    /// <b>It activates only when nothing else is.</b> Creating the row is safe to repeat; stealing
    /// the active flag would not be, because a restart must never undo an operator's choice of
    /// which world their server opens in. Zero-active is reachable only on a fresh database - the
    /// API refuses to delete the live one - so this claims the slot exactly once.
    /// </para>
    /// <para>
    /// Development-only, like the world it describes. A production server has no Aldenmoor, so a
    /// configuration pointing into it would be a row naming a room that does not exist.
    /// </para>
    /// </remarks>
    /// <returns>True when a configuration was planted.</returns>
    public static async Task<bool> ReconcileStarterConfigurationAsync(
        MuwbtaDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.GameConfigurations.AnyAsync(c => c.Key == ConfigurationKey, cancellationToken))
        {
            return false;
        }

        var anyActive = await db.GameConfigurations.AnyAsync(c => c.IsActive, cancellationToken);

        db.GameConfigurations.Add(new GameConfiguration
        {
            Key = ConfigurationKey,
            Name = "Aldenmoor",
            Description =
                "The original starter world, kept as a sandbox and as the fixture the playtest "
                + "plans run against. Matches the engine's built-in fallback.",
            StartingRoomKey = StartingRoom.ToString(),

            // The line GameLoop used to hold as a literal, now where it can be changed.
            WelcomeMessage = "Welcome to Aldenmoor, {name}.",
            IsActive = !anyActive,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

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
    /// Plants any ability from <see cref="AbilityCatalogue"/> that the database does not have.
    /// Never updates and never deletes.
    /// </summary>
    /// <remarks>
    /// <b>This used to make the table match the catalogue exactly, and that is precisely what it
    /// must no longer do.</b> The old version's own note said why: *"If abilities ever become
    /// builder-editable, this has to become a merge instead — at that point a row differing from
    /// the catalogue stops meaning stale."* Abilities are builder-editable now, so a row that
    /// differs is somebody's retune, and updating or deleting it would throw their work away on
    /// the next restart — silently, and on every restart after that.
    ///
    /// What survives from the old behaviour is the half that fixed a real bug: **a missing row is
    /// still planted**. Seeding once meant a database that already existed never received an
    /// ability added later, so a character levelling into it was granted a key with nothing behind
    /// it and could not cast what the game said they knew. Insert-if-absent keeps that fixed
    /// without claiming authority over anything already there.
    ///
    /// The two directions deliberately given up, and what replaces them:
    ///
    /// - **Retuning** no longer arrives this way. Editing the catalogue changes what a *fresh*
    ///   database is born with and nothing else. An existing deployment gets a retune the way it
    ///   gets any other content change — an import, or a migration when the change has to be
    ///   automatic (PLAN.md §6.1).
    /// - **Orphan cleanup** is now a deliberate delete through the builder. A key the catalogue
    ///   drops stays in the table, because this code can no longer tell "the catalogue moved on"
    ///   from "somebody authored an ability we have never heard of" — and guessing wrong deletes
    ///   content.
    /// </remarks>
    public static async Task<AbilityReconciliation> ReconcileAbilitiesAsync(
        MuwbtaDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Abilities
            .ToDictionaryAsync(a => a.Key, StringComparer.Ordinal, cancellationToken);

        var catalogue = AbilityCatalogue.All.ToDictionary(e => e.Key, StringComparer.Ordinal);

        var toAdd = catalogue.Values
            .Where(entry => !existing.ContainsKey(entry.Key))
            .ToList();

        if (toAdd.Count == 0)
        {
            return new AbilityReconciliation(0, 0, 0);
        }

        db.Abilities.AddRange(toAdd.Select(AbilityCatalogue.ToAbility));
        await db.SaveChangesAsync(cancellationToken);

        // Updated and Removed stay on the result and stay zero. The shape is kept because the
        // startup log line and its test read it, and because a future merge that *can* tell a
        // stale row from an authored one would fill them in - reporting "0 updated" is a truthful
        // statement about what this pass does, where dropping the fields would erase the question.
        return new AbilityReconciliation(toAdd.Count, 0, 0);
    }
}
