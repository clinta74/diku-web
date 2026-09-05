#:project ../src/Muwbta.Server/Muwbta.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Writes the development starter world - Aldenmoor, as the seeder plants it - out as a
// WorldBundle, so it can be imported into a database that already exists.
//
//     dotnet run tools/export-seed.cs
//     dotnet run tools/export-seed.cs -- -o build/aldenmoor.json --connection "Host=localhost;..."
//
// The seeder runs only on an empty database (StarterWorldSeeder.SeedAsync), which is right for a
// fresh server and useless for the one you have been building in. So this runs the real seeder
// against a scratch database on the same Postgres - migrated, seeded, reconciled - exports the
// world through the same WorldExporter the API uses, and drops the scratch database again. What
// lands on disk is exactly what a fresh boot would have made, in the file the import endpoint
// takes. Import is a merge: rows the seed does not know about are left alone.
//
// The connection string names the *existing* database; the scratch one is created beside it as
// <database>_seed and removed afterwards, so the account needs CREATEDB, which the compose
// account has.

using Muwbta.Persistence;
using Muwbta.Persistence.Seeding;
using Muwbta.Server.Building;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var outPath = "build/aldenmoor.json";
string? connection = null;

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
        case "-h" or "--help":
            Console.WriteLine("""
                Writes the starter world (Aldenmoor) as a WorldBundle, by seeding a scratch database.

                  -o, --out <file>       Default: build/aldenmoor.json
                  --connection <string>  Npgsql connection string for the existing database. The
                                         scratch one is created beside it. Falls back to
                                         MUWBTA_CONNECTION, then to the compose defaults.
                """);
            return 0;
    }
}

connection ??= Environment.GetEnvironmentVariable("MUWBTA_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=muwbta;Username=muwbta;Password=muwbta_dev_only";

var existing = new NpgsqlConnectionStringBuilder(connection);
var scratchName = $"{existing.Database}_seed";
var admin = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" }.ConnectionString;
var scratch = new NpgsqlConnectionStringBuilder(connection) { Database = scratchName }.ConnectionString;

async Task AdminAsync(string sql)
{
    await using var conn = new NpgsqlConnection(admin);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    await cmd.ExecuteNonQueryAsync();
}

await AdminAsync($"DROP DATABASE IF EXISTS \"{scratchName}\" WITH (FORCE)");
await AdminAsync($"CREATE DATABASE \"{scratchName}\"");

try
{
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(scratch);
    dataSourceBuilder.EnableDynamicJson();

    await using (var dataSource = dataSourceBuilder.Build())
    {
        var options = new DbContextOptionsBuilder<MuwbtaDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        await using var db = new MuwbtaDbContext(options);

        await db.Database.MigrateAsync();

        // The three things a development boot does, in the order Program.cs does them.
        await StarterWorldSeeder.SeedAsync(db);
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);
        await StarterWorldSeeder.ReconcileStarterConfigurationAsync(db);

        var exporter = new WorldExporter(db, TimeProvider.System);
        var bundle = await exporter.ExportAsync(StarterWorldSeeder.WorldKey, null, CancellationToken.None)
            ?? throw new InvalidOperationException("The seeder planted nothing to export.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outPath, BundleFormat.Write(bundle));

        Console.WriteLine(
            $"Wrote {outPath}: {bundle.Rooms.Count} rooms, {bundle.MobTemplates.Count} mobs, "
            + $"{bundle.ItemTemplates.Count} items, {bundle.Spawners.Count} spawners, "
            + $"{bundle.Quests.Count} quests, {bundle.Configurations.Count} configuration(s).");
    }
}
finally
{
    await AdminAsync($"DROP DATABASE IF EXISTS \"{scratchName}\" WITH (FORCE)");
}

return 0;
