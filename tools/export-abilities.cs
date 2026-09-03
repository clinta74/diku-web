#:project ../src/Muwbta.Domain/Muwbta.Domain.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Writes named abilities out of AbilityCatalogue as re-runnable upsert SQL.
//
//   dotnet run tools/export-abilities.cs warden.last-stand warden.shield-wall
//   dotnet run tools/export-abilities.cs warden.last-stand -o backups/ability-fix.sql
//   dotnet run tools/export-abilities.cs --all -o backups/abilities.sql
//
// WHY THIS EXISTS. `AbilityCatalogue` is the set a *fresh* database is seeded with, and the
// startup reconcile only plants rows that are missing - it never updates and never deletes, which
// is deliberate and is what stops a restart from reverting a builder's work. So editing the
// catalogue retunes new installs and reaches a running server not at all. This is the bridge:
// the same numbers, as SQL somebody can read before they run it.
//
// NAMED KEYS BY DEFAULT, AND THAT IS THE POINT. An upsert overwrites, so exporting everything
// would push the catalogue over the top of every retune a builder has made through the editor -
// silently, and in one statement. `--all` is there for a database being rebuilt from nothing;
// anything else should name the rows it means to change.
//
// The column list is a hand-written copy of the schema: **add or rename a column on `abilities`
// and you must edit this file in the same commit.** Nothing compiles this string.
//
// It is the last such list in the repository, and it is guarded rather than derived. The content
// export had the identical flaw and drifted five times before it was retired for
// `tools/export-bundle.cs`; the player export went the same way, to `tools/export-players.cs`.
// Both of those read a database, so both could take their columns off the EF model. This one
// reads `AbilityCatalogue` and deliberately depends on Muwbta.Domain alone - it never opens a
// connection, which is what lets it run against a database that does not exist yet. So the diff
// lives in `AbilityExportCompletenessTests` instead, which fails if this list drifts from the
// model in either direction, or if a column is inserted but not updated on conflict.
//
// NOTE: `dotnet run` on a file-based app caches its build against *this file's* content, so a
// change in Muwbta.Domain alone may not be picked up. Touch this file if output looks stale.

using System.Text;
using System.Text.Json;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;

var keys = new List<string>();
string? outFile = null;
var all = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--all":
            all = true;
            break;

        case "-o" or "--out":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("-o needs a path.");
                return 2;
            }

            outFile = args[++i];
            break;

        default:
            if (args[i].StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                return 2;
            }

            keys.Add(args[i]);
            break;
    }
}

if (!all && keys.Count == 0)
{
    Console.Error.WriteLine("Name the ability keys to export, or pass --all.");
    Console.Error.WriteLine("  dotnet run tools/export-abilities.cs warden.last-stand");
    return 2;
}

var catalogue = AbilityCatalogue.AsAbilities.ToDictionary(a => a.Key, StringComparer.Ordinal);

// Refused rather than skipped: a mistyped key that produced an empty script would look like a
// successful export of nothing, and be run as one.
var missing = keys.Where(k => !catalogue.ContainsKey(k)).ToList();

if (missing.Count > 0)
{
    Console.Error.WriteLine($"No such ability: {string.Join(", ", missing)}");
    return 1;
}

var chosen = (all ? catalogue.Values.AsEnumerable() : keys.Select(k => catalogue[k]))
    .OrderBy(a => a.Path)
    .ThenBy(a => a.UnlockLevel)
    .ThenBy(a => a.Key, StringComparer.Ordinal)
    .ToList();

var effects = new EffectRegistry();
var sql = new StringBuilder();

sql.AppendLine("-- diku-web ability export, from AbilityCatalogue.");
sql.AppendLine($"-- {chosen.Count} {(chosen.Count == 1 ? "ability" : "abilities")}, "
    + $"generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC.");
sql.AppendLine("--");
sql.AppendLine("-- Every statement is an upsert and OVERWRITES the row it names, including any");
sql.AppendLine("-- retune made through the builder. Read the comment above each one first.");
sql.AppendLine("--");
sql.AppendLine("--   docker exec -i dikuweb-postgres psql -U dikuweb -d dikuweb < this-file.sql");
sql.AppendLine();
sql.AppendLine("BEGIN;");

foreach (var ability in chosen)
{
    // What the ability will do once this is applied, in the same words `abilities` will use.
    // The point of putting it here is that the effect of a jsonb blob is otherwise unreadable at
    // the moment it matters most - when somebody is deciding whether to run the file.
    sql.AppendLine();
    sql.AppendLine($"-- {ability.Name} ({ability.Path} {ability.UnlockLevel}) — "
        + AbilityDescriber.Describe(ability, effects));

    sql.AppendLine(
        "INSERT INTO abilities (key, path, unlock_level, name, description, cost_type, cost_value, "
        + "cooldown_pulses, cooldown_group, cast_time_pulses, targeting_type, effects) VALUES ("
        + $"{Text(ability.Key)}, {(int)ability.Path}, {ability.UnlockLevel}, {Text(ability.Name)}, "
        + $"{Text(ability.Description)}, {(int)ability.CostType}, {ability.CostValue}, "
        + $"{ability.CooldownPulses}, {Number((long?)ability.CooldownGroup)}, "
        + $"{Number(ability.CastTimePulses)}, {(int)ability.TargetingType}, "
        + $"{Text(JsonSerializer.Serialize(ability.Effects))}::jsonb)");

    sql.AppendLine(
        "ON CONFLICT (key) DO UPDATE SET path = EXCLUDED.path, "
        + "unlock_level = EXCLUDED.unlock_level, name = EXCLUDED.name, "
        + "description = EXCLUDED.description, cost_type = EXCLUDED.cost_type, "
        + "cost_value = EXCLUDED.cost_value, cooldown_pulses = EXCLUDED.cooldown_pulses, "
        + "cooldown_group = EXCLUDED.cooldown_group, "
        + "cast_time_pulses = EXCLUDED.cast_time_pulses, targeting_type = EXCLUDED.targeting_type, "
        + "effects = EXCLUDED.effects;");
}

sql.AppendLine();
sql.AppendLine("COMMIT;");

if (outFile is null)
{
    Console.Write(sql.ToString());
}
else
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(outFile));

    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllText(outFile, sql.ToString());
    Console.Error.WriteLine($"Wrote {chosen.Count} upserts to {outFile}");
}

foreach (var ability in chosen)
{
    // Validated on the way out, because the row is about to be written straight past the builder
    // API - which is the one place that normally refuses a broken ability.
    foreach (var problem in AbilityValidator.ValidateOne(ability, effects))
    {
        Console.Error.WriteLine($"  {problem.Severity}: {ability.Key} — {problem.Message}");
    }
}

return 0;

// Postgres string literal. standard_conforming_strings is on by default, so a backslash is a
// backslash and doubling the quote is the whole of the escaping.
static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

static string Number(long? value) =>
    value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
