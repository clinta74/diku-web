#:project ../src/DikuWeb.Server/DikuWeb.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Writes the live world out as a WorldBundle JSON file, with no server running.
//
//     dotnet run tools/export-bundle.cs -o build/live.json
//     dotnet run tools/export-bundle.cs --connection "Host=localhost;..." -o build/live.json
//
// The same door `GET /api/builder/export` goes through - WorldExporter, against the same
// DbContext - so what lands on disk is the file the endpoint would have sent.
//
// **It exists so that reading the world does not require starting the server.** Booting
// DikuWeb.Server migrates on startup in every environment, and in Development it also seeds
// starter content and reconciles the ability table against the catalogue. All three are writes,
// and none of them is what somebody who wants to *read* the world asked for - a balance run that
// silently rewrote the abilities it was about to measure would be the worst possible outcome.
//
// This opens a connection, reads, and closes it. Nothing here writes.

using DikuWeb.Domain.Spawning;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Persistence;
using DikuWeb.Server.Building;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var outPath = "build/live.json";
string? connection = null;
string? worldKey = null;
string? zoneKey = null;
var abilitiesOnly = false;
var unplacedOnly = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o" or "--out":
            outPath = args[++i];
            break;
        case "--connection":
            connection = args[++i];
            break;
        case "--world":
            worldKey = args[++i];
            break;
        case "--zone":
            zoneKey = args[++i];
            break;
        case "--abilities":
            abilitiesOnly = true;
            break;
        case "--unplaced":
            unplacedOnly = true;
            break;
        case "-h" or "--help":
            Console.WriteLine("""
                Writes the live world out as a WorldBundle, without starting the server.

                  -o, --out <file>       Default: build/live.json
                  --connection <string>  Npgsql connection string. Falls back to
                                         DIKUWEB_CONNECTION, then to the compose defaults.
                  --world <key>          Export one world instead of everything
                  --zone <key>           Export one zone instead of everything
                  --abilities            Export only the abilities, as content/abilities.json is
                  --unplaced             Export only the templates nothing places. A scoped export
                                         cannot reach those, so this is what keeps content/ complete
                """);
            return 0;
    }
}

// The compose defaults (docker-compose.yml) are the fallback, because that is what a developer
// following the README is running. Anything else is passed in or set in the environment.
connection ??= Environment.GetEnvironmentVariable("DIKUWEB_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=dikuweb;Username=dikuweb;Password=password";

// EnableDynamicJson, exactly as PersistenceServiceCollectionExtensions does it. Every stat bag in
// the world is a jsonb Dictionary<string, object>, and without this the first template read throws
// - so the opt-in is not optional here, it is the difference between reading the world and not.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connection);
dataSourceBuilder.EnableDynamicJson();

await using var dataSource = dataSourceBuilder.Build();

var options = new DbContextOptionsBuilder<DikuWebDbContext>()
    .UseNpgsql(dataSource)
    .Options;

await using var db = new DikuWebDbContext(options);

var exporter = new WorldExporter(db, TimeProvider.System);

// Abilities carry no zone and belong to a Path rather than to a place, so they have their own
// scope rather than being a filter over the world export - see WorldExporter.ExportAbilitiesAsync.
var bundle = abilitiesOnly
    ? await exporter.ExportAbilitiesAsync(CancellationToken.None)
    : unplacedOnly
        ? await exporter.ExportUnplacedAsync(CancellationToken.None)
        : await exporter.ExportAsync(worldKey, zoneKey, CancellationToken.None);

if (bundle is null)
{
    await Console.Error.WriteLineAsync(
        $"Nothing to export for world '{worldKey}' zone '{zoneKey}'.");
    return 1;
}

var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));

if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

// BundleFormat.Write rather than a bare Serialize: this is a file-based app, which the SDK
// compiles as trimmable and AOT-ready, so a Serialize call here fails the build with IL2026 and
// IL3050. That helper exists for exactly this - see its own remarks.
await File.WriteAllTextAsync(outPath, BundleFormat.Write(bundle));

Console.WriteLine(
    $"Wrote {outPath}: format v{bundle.FormatVersion}, " +
    $"{bundle.Worlds.Count} world(s), {bundle.Zones.Count} zone(s), " +
    $"{bundle.ItemTemplates.Count} item(s), {bundle.MobTemplates.Count} mob(s), " +
    $"{bundle.Abilities.Count} abilit(ies), {bundle.Spawners.Count} spawner(s)");

// Templates no scoped export anywhere would carry, said out loud.
//
// A scoped bundle closes over references - the templates its spawners place, its quests name, and
// its mobs drop - because a mob template has no zone and a zone bundle has to stand up on its own
// (see WorldExporter's remarks). That is right for the bundle and wrong for a *snapshot*: a
// template nothing references yet is still real content. `spawn <item|mob> <key>` is a builder
// command, so an unplaced mob is one somebody can put down on purpose - a test fixture, or
// something held back for later.
//
// The interesting set is not "what this scope missed" - that is nearly everything, and nearly all
// of it is carried by another zone's file. It is what NO scope reaches: referenced by no spawner,
// named by no quest, dropped by no mob. Those are the ones a world rebuilt from scoped files loses
// silently, and the symptom is `spawn` refusing a key that worked yesterday.
//
// Printed rather than fixed, because the fix is a judgement - place it, drop it, or keep a full
// export beside the scoped ones - and only the author knows which.
if (!abilitiesOnly && !unplacedOnly)
{
    var placedMobs = await db.Spawners.AsNoTracking()
        .Where(s => s.TemplateKind == TemplateKind.Mob)
        .Select(s => s.TemplateKey)
        .ToListAsync();

    var placedItems = await db.Spawners.AsNoTracking()
        .Where(s => s.TemplateKind == TemplateKind.Item)
        .Select(s => s.TemplateKey)
        .ToListAsync();

    var quests = await db.Quests.AsNoTracking().ToListAsync();
    var allMobs = await db.MobTemplates.AsNoTracking().ToListAsync();

    var reachedMobs = new HashSet<string>(placedMobs, StringComparer.OrdinalIgnoreCase);
    var reachedItems = new HashSet<string>(placedItems, StringComparer.OrdinalIgnoreCase);

    foreach (var quest in quests)
    {
        Reach(reachedMobs, quest.GiverMobKey);
        Reach(reachedMobs, quest.TurninMobKey);
        Reach(reachedItems, quest.RequiredItemKey);
        Reach(reachedItems, quest.RewardItemKey);
    }

    // Loot, the same hop the exporter makes: a quest item usually arrives through a drop.
    foreach (var mob in allMobs)
    {
        foreach (var entry in mob.Loot)
        {
            if (entry.TryGetValue("itemTemplateKey", out var value))
            {
                Reach(reachedItems, value?.ToString());
            }
        }

        // The same hop the exporter makes, and it has to stay the same: this warning is only
        // useful if "unreachable here" means "unreachable there".
        foreach (var sold in MobBehavior.SellsOf(mob.Behavior))
        {
            Reach(reachedItems, sold);
        }
    }

    var looseMobs = allMobs.Select(m => m.Key)
        .Where(k => !reachedMobs.Contains(k)).Order(StringComparer.Ordinal).ToList();

    var looseItems = (await db.ItemTemplates.AsNoTracking().Select(i => i.Key).ToListAsync())
        .Where(k => !reachedItems.Contains(k)).Order(StringComparer.Ordinal).ToList();

    Report("mob", looseMobs);
    Report("item", looseItems);

    static void Reach(HashSet<string> into, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            into.Add(key);
        }
    }

    static void Report(string kind, List<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        Console.WriteLine(
            $"  note: {keys.Count} {kind} template(s) are reached by no spawner, quest or loot "
            + "table, so no scoped export carries them: " + string.Join(", ", keys));
        Console.WriteLine(
            "        Run again with --unplaced to write them out; without that a world rebuilt from "
            + "content/ loses them and `spawn` starts refusing those keys.");
    }
}

return 0;
