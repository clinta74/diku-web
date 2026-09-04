#:project ../src/Muwbta.Server/Muwbta.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Writes one account and its characters out as a re-runnable SQL script, for moving them to
// another server.
//
//     dotnet run tools/export-players.cs --account clint
//     dotnet run tools/export-players.cs --account clint -o backups/clint.sql
//     dotnet run tools/export-players.cs --account clint --relocate ossara.gatetown.the-gate-yard
//
// The complement of `tools/export-bundle.cs`, which carries content and deliberately leaves player
// data behind. This carries the player data and deliberately leaves the world behind: run the world
// in first, with `POST /api/builder/import`, and then this.
//
// **THE COLUMN LISTS ARE DERIVED, NOT TRANSCRIBED.** This replaced a psql script whose every column
// list was hand-typed. That is not a style objection - the content export had the identical shape
// and drifted from the schema five times, every drift silent and every one in the same direction: a
// column that stops being emitted restores as its column default, so the data comes back looking
// right. It cleared every quest's auto-start, and later every attunement gate.
//
// So nothing here names a column. `IRelationalModel` is asked what the table has, and the INSERT is
// built from the answer - add or rename a column on any of the four tables and this export carries
// it on the next run with no edit here at all.
//
// **Read from the relational model, NOT from `IEntityType.GetProperties()`.** That was tried and it
// is wrong in the quietest available way: `Character.Attributes` and `Character.Vitals` are owned
// types mapped to jsonb columns, so `GetProperties()` returns seventeen of the nineteen columns and
// no error. A port that used it would have reintroduced, on its first run, the exact bug it was
// written to remove. `GetRelationalModel()` describes the table rather than the CLR type, which is
// the thing an INSERT actually has to agree with.
//
// The old script's `-q` hazard is gone with it, rather than fixed: psql echoed `Output format is
// unaligned.` into its own output, that line was not SQL, and a restore taken the documented way
// died on it. Nothing echoes into a file that is written rather than captured.
//
// WHAT IT CARRIES
//   accounts           the one row named by --account
//   characters         every character on it, deleted ones included (see below)
//   item_instances     everything those characters own, walked recursively through containers
//   character_quests   their journals, progress and repeat counts
//
// WHAT IT DOES NOT
//   Content of any kind. Rooms, templates, quests-as-authored, spawners and abilities are the
//   bundle's job. **Run the world in first.** A character whose room_key names a room the target
//   does not have is relocated to the configured starting room on entry (GameLoop logs
//   RelocatedFromMissingRoom), which is graceful but is not what you wanted; an item whose
//   template_key is missing keeps its resolved stats and loses its rules. `--relocate` is the
//   answer when you know the target does not have the room.
//
//   Items lying on the ground. Those belong to the world's population, not to a player, and the
//   spawners on the target will make their own.
//
// DELETED CHARACTERS TRAVEL. `deleted_at` is a soft delete and the row is the record of it; a move
// that dropped them would let a retired name be taken again on the target and would lose the
// history. They stay invisible in the character list exactly as they were here.
//
// THE THREE THINGS THIS REFUSES TO DO, all enforced in the generated SQL rather than here, so they
// still hold when somebody applies the file by hand:
//
//   1. Overwrite a character belonging to somebody else. `characters.name` is unique, so a name
//      already taken on the target under a *different* id is a hard stop.
//   2. Overwrite an account that is not this one. Same argument for `email` and `username`.
//   3. Half-apply. Everything is inside one transaction.
//
// IT IS A MOVE, NOT A MERGE, AND THAT IS DESTRUCTIVE ON RE-RUN. Applying the output replaces the
// moved characters' items and quests with the ones in the file - scoped strictly to the character
// ids being moved, and to nothing else on the target. That is what makes a second run produce the
// same state rather than a pile of ghosts. If those characters have been *played* on the target
// since, their progress since is what you are throwing away.
//
// This writes a file. **It does not touch the target** - applying it is a separate command you run
// yourself, printed at the end.

using System.Text;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

string? account = null;
string? outPath = null;
string? relocate = null;
string? connection = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--account":
            account = args[++i];
            break;
        case "-o" or "--out":
            outPath = args[++i];
            break;
        case "--relocate":
            relocate = args[++i];
            break;
        case "--connection":
            connection = args[++i];
            break;
        case "-h" or "--help":
            Console.WriteLine("""
                Writes one account and its characters out as a re-runnable SQL script.

                  --account <name>       Username or email. Required: a default here would be
                                         "everybody".
                  -o, --out <file>       Default: backups/players-<account>-<date>.sql
                  --relocate <room key>  Rewrite every character's room on the way out, for a
                                         target that does not have the room they are standing in.
                  --connection <string>  Npgsql connection string. Falls back to
                                         MUWBTA_CONNECTION, then to the compose defaults.
                """);
            return 0;
    }
}

if (string.IsNullOrWhiteSpace(account))
{
    Console.Error.WriteLine("export-players: --account <username or email> is required.");
    return 1;
}

// Three dot-separated segments, the same shape the room keys are authored in. Checked here rather
// than discovered on the target, where the symptom is every moved character standing in the
// starting room with nothing saying why.
if (relocate is not null
    && !System.Text.RegularExpressions.Regex.IsMatch(relocate, @"^[a-z0-9-]+\.[a-z0-9-]+\.[a-z0-9-]+$"))
{
    Console.Error.WriteLine(
        $"export-players: --relocate wants a room key of three dot-separated segments, got '{relocate}'.");
    return 1;
}

outPath ??= Path.Combine(
    "backups",
    $"players-{string.Concat(account.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_'))}"
        + $"-{DateTime.UtcNow:yyyy-MM-dd}.sql");

// The compose defaults are the fallback, because that is what a developer following the README is
// running. Anything else is passed in or set in the environment.
connection ??= Environment.GetEnvironmentVariable("MUWBTA_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=muwbta;Username=muwbta;Password=password";

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connection);
dataSourceBuilder.EnableDynamicJson();
await using var dataSource = dataSourceBuilder.Build();

var options = new DbContextOptionsBuilder<MuwbtaDbContext>().UseNpgsql(dataSource).Options;
await using var db = new MuwbtaDbContext(options);

var relational = db.Model.GetRelationalModel();

var accounts = Describe("accounts");
var characters = Describe("characters");
var items = Describe("item_instances");
var quests = Describe("character_quests");

// Resolve the account exactly once. Everything below is scoped by the ids this produces, so the
// guards, the deletes and the inserts cannot drift apart the way a repeated WHERE clause does.
var accountRows = await ReadAsync(
    accounts,
    "WHERE username = $1 OR email = $1",
    ["ORDER BY id"],
    account);

if (accountRows.Count != 1)
{
    // An export that "succeeded" and carries no rows is the one failure that looks like success,
    // right up until somebody applies it.
    Console.Error.WriteLine(
        accountRows.Count == 0
            ? $"export-players: no account matched '{account}'."
            : $"export-players: '{account}' matched {accountRows.Count} accounts.");
    return 1;
}

var accountId = Value(accounts, accountRows[0], "id")!;
var email = Value(accounts, accountRows[0], "email")!;
var username = Value(accounts, accountRows[0], "username")!;

var characterRows = await ReadAsync(
    characters, "WHERE account_id = $1::uuid", ["ORDER BY created_at"], accountId);

if (characterRows.Count == 0)
{
    Console.Error.WriteLine($"export-players: '{account}' has no characters.");
    return 1;
}

var characterIds = characterRows.Select(r => Value(characters, r, "id")!).ToArray();

// Rewritten at the source rather than by a regex over the finished SQL, which is how the script
// this replaced did it.
if (relocate is not null)
{
    var roomKey = characters.Index("room_key");

    foreach (var row in characterRows)
    {
        row[roomKey] = relocate;
    }
}

// Walked recursively, so an item inside a bag inside a chest comes too. A contained item has a null
// owner_character_id - the check constraint allows exactly one of the three locations - so a flat
// join on ownership would carry the bag and leave everything in it behind.
var itemRows = await ReadRecursiveItemsAsync(characterIds);

// Boxed deliberately: `string[]` is covariant to `object[]`, so passing it bare to a
// `params object[]` spreads it into one parameter per id and $1 becomes a single uuid that
// Postgres then fails to read as an array.
var questRows = await ReadAsync(
    quests,
    "WHERE character_id = ANY($1::uuid[])",
    ["ORDER BY character_id, quest_key"],
    (object)characterIds);

var sql = new StringBuilder();

sql.AppendLine($"-- muwbta player export, {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ssK}");
sql.AppendLine($"-- account: {username} <{email}>");
sql.AppendLine(
    $"-- {characterRows.Count} characters, {itemRows.Count} items, {questRows.Count} quest rows");

if (relocate is not null)
{
    sql.AppendLine($"-- every character relocated to {relocate}");
}

sql.AppendLine("-- Column lists derived from the EF relational model by tools/export-players.cs.");
sql.AppendLine("-- Re-runnable. Replaces the moved characters' items and quests; touches nothing else.");
sql.AppendLine();
sql.AppendLine("BEGIN;");

// Guards first, because the point of them is that nothing has happened yet.
sql.AppendLine();
sql.AppendLine("-- Refuse if a name on this server belongs to somebody else.");
sql.AppendLine("DO $guard$");
sql.AppendLine("DECLARE clash text;");
sql.AppendLine("BEGIN");
sql.AppendLine("  SELECT string_agg(name, ', ' ORDER BY name) INTO clash FROM characters");
sql.AppendLine(
    "   WHERE name IN ("
    + string.Join(", ", characterRows.Select(r => Lit(Value(characters, r, "name"))).Order(StringComparer.Ordinal))
    + ") AND id NOT IN ("
    + string.Join(", ", characterIds.Order(StringComparer.Ordinal).Select(Lit))
    + ");");
sql.AppendLine("  IF clash IS NOT NULL THEN");
sql.AppendLine(
    "    RAISE EXCEPTION 'refusing: character name(s) already held by another account here: %', clash;");
sql.AppendLine("  END IF;");
sql.AppendLine("END $guard$;");

sql.AppendLine();
sql.AppendLine("-- Refuse if this account's email or username belongs to a different account here.");
sql.AppendLine("DO $guard$");
sql.AppendLine("BEGIN");
sql.AppendLine("  IF EXISTS (SELECT 1 FROM accounts");
sql.AppendLine($"              WHERE (email = {Lit(email)} OR username = {Lit(username)})");
sql.AppendLine($"                AND id <> {Lit(accountId)}) THEN");
// The values go in as RAISE arguments rather than being interpolated into the message. Written the
// other way, a quoted literal lands inside an already-quoted string and closes it early.
sql.AppendLine(
    "    RAISE EXCEPTION 'refusing: % or % already belongs to a different account here', "
    + $"{Lit(email)}, {Lit(username)};");
sql.AppendLine("  END IF;");
sql.AppendLine("END $guard$;");

sql.AppendLine();
sql.AppendLine("-- account (DO NOTHING on conflict: the target's own password and role win)");
sql.AppendLine(Insert(accounts, accountRows[0], update: false));

sql.AppendLine();
sql.AppendLine("-- characters");

foreach (var row in characterRows)
{
    sql.AppendLine(Insert(characters, row, update: true));
}

sql.AppendLine();
sql.AppendLine("-- items: cleared and rewritten, scoped to these characters only");
sql.AppendLine(
    "DELETE FROM item_instances WHERE owner_character_id IN ("
    + string.Join(", ", characterIds.Order(StringComparer.Ordinal).Select(Lit)) + ");");

// Containers before their contents on the way in. There is no foreign key on container_item_id to
// enforce it - the ordering is for a reader following what happened, and for the day somebody adds
// the constraint.
foreach (var row in itemRows)
{
    sql.AppendLine(Insert(items, row, update: true));
}

sql.AppendLine();
sql.AppendLine("-- quest journals: cleared and rewritten, scoped to these characters only");
sql.AppendLine(
    "DELETE FROM character_quests WHERE character_id IN ("
    + string.Join(", ", characterIds.Order(StringComparer.Ordinal).Select(Lit)) + ");");

foreach (var row in questRows)
{
    sql.AppendLine(Insert(quests, row, update: true));
}

// What the target should say afterwards, so applying it reports rather than being silent.
sql.AppendLine();
sql.AppendLine("DO $report$");
sql.AppendLine("BEGIN");
sql.AppendLine("  RAISE NOTICE 'moved %: % characters, % items, % quest rows',");
sql.AppendLine($"    (SELECT username FROM accounts WHERE id = {Lit(accountId)}),");
sql.AppendLine($"    (SELECT count(*) FROM characters WHERE account_id = {Lit(accountId)}),");
sql.AppendLine("    (SELECT count(*) FROM item_instances i JOIN characters c ON c.id = i.owner_character_id");
sql.AppendLine($"      WHERE c.account_id = {Lit(accountId)}),");
sql.AppendLine("    (SELECT count(*) FROM character_quests q JOIN characters c ON c.id = q.character_id");
sql.AppendLine($"      WHERE c.account_id = {Lit(accountId)});");
sql.AppendLine("END $report$;");

sql.AppendLine();
sql.AppendLine("COMMIT;");

var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));

if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

await File.WriteAllTextAsync(outPath, sql.ToString());

Console.WriteLine(
    $"Wrote {outPath}: {characterRows.Count} characters, {itemRows.Count} items, "
    + $"{questRows.Count} quest rows.");
Console.WriteLine();
Console.WriteLine("The target needs these rooms to exist, or the characters in them are relocated on login:");

foreach (var room in characterRows
    .Select(r => Value(characters, r, "room_key"))
    .Where(r => r is not null)
    .Distinct(StringComparer.Ordinal)
    .Order(StringComparer.Ordinal))
{
    Console.WriteLine($"  {room}");
}

Console.WriteLine();
Console.WriteLine("Apply it yourself - this touched nothing:");
Console.WriteLine($"  docker exec -i muwbta-postgres psql -U muwbta -d muwbta < {outPath}");

return 0;

// ---------------------------------------------------------------------------------------------

Table Describe(string table)
{
    var relationalTable = relational.Tables.FirstOrDefault(t => t.Name == table)
        ?? throw new InvalidOperationException($"No table '{table}' in the model.");

    // Ordered, so two exports of the same rows are the same bytes and can be diffed. The model's
    // own order is stable but arbitrary; alphabetical is stable and says so.
    var columns = relationalTable.Columns.Select(c => c.Name).Order(StringComparer.Ordinal).ToArray();
    var key = relationalTable.PrimaryKey?.Columns.Select(c => c.Name).ToArray() ?? [];

    return new Table(table, columns, key);
}

// Every column read as text, which is what makes the formatting generic: a jsonb, a timestamptz, a
// uuid and an enum-as-int all arrive as the string Postgres would print, and all four coerce back
// from a quoted literal on the way in. It is what psql's %L did, without psql.
async Task<List<string?[]>> ReadAsync(Table table, string where, string[] tail, params object[] parameters)
{
    var select = $"SELECT {string.Join(", ", table.Columns.Select(c => $"\"{c}\"::text"))} "
        + $"FROM {table.Name} {where} {string.Join(" ", tail)}";

    await using var command = dataSource.CreateCommand(select);

    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter);
    }

    return await ReadRowsAsync(command, table.Columns.Length);
}

async Task<List<string?[]>> ReadRecursiveItemsAsync(string[] ids)
{
    var columns = string.Join(", ", items.Columns.Select(c => $"\"{c}\"::text"));

    await using var command = dataSource.CreateCommand($"""
        WITH RECURSIVE owned AS (
            SELECT * FROM item_instances WHERE owner_character_id = ANY($1::uuid[])
            UNION ALL
            SELECT i.* FROM item_instances i JOIN owned o ON i.container_item_id = o.id
        )
        SELECT {columns} FROM owned ORDER BY (container_item_id IS NOT NULL), id
        """);

    command.Parameters.AddWithValue(ids);

    return await ReadRowsAsync(command, items.Columns.Length);
}

static async Task<List<string?[]>> ReadRowsAsync(NpgsqlCommand command, int width)
{
    var rows = new List<string?[]>();
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        var row = new string?[width];

        for (var i = 0; i < width; i++)
        {
            row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetString(i);
        }

        rows.Add(row);
    }

    return rows;
}

static string Lit(string? value) =>
    value is null ? "NULL" : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

static string? Value(Table table, string?[] row, string column) => row[table.Index(column)];

// The conflict target and the SET list both come off the primary key, so a table that gains a
// composite key or a column needs no edit here.
static string Insert(Table table, string?[] row, bool update)
{
    var columns = string.Join(", ", table.Columns);
    var values = string.Join(", ", row.Select(Lit));
    var conflict = string.Join(", ", table.Key);

    if (!update)
    {
        return $"INSERT INTO {table.Name} ({columns}) VALUES ({values}) "
            + $"ON CONFLICT ({conflict}) DO NOTHING;";
    }

    var assignments = string.Join(
        ", ",
        table.Columns.Where(c => !table.Key.Contains(c, StringComparer.Ordinal))
            .Select(c => $"{c} = EXCLUDED.{c}"));

    return $"INSERT INTO {table.Name} ({columns}) VALUES ({values}) "
        + $"ON CONFLICT ({conflict}) DO UPDATE SET {assignments};";
}

internal sealed record Table(string Name, string[] Columns, string[] Key)
{
    public int Index(string column)
    {
        var index = Array.IndexOf(Columns, column);

        return index >= 0
            ? index
            : throw new InvalidOperationException($"{Name} has no column '{column}'.");
    }
}
