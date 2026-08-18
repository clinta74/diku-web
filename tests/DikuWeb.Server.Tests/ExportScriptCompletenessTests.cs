using System.Text.RegularExpressions;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DikuWeb.Server.Tests;

/// <summary>
/// <c>tools/export-content.sql</c> names every column of every table it exports.
/// </summary>
/// <remarks>
/// <para>
/// That file is a hand-written copy of the schema, nothing compiles it, and it has drifted
/// <b>five</b> times — each one silent, each one only found by somebody who had come here to do
/// something else:
/// </para>
/// <list type="number">
/// <item><c>spawners.sentinel</c> renamed to <c>wanders</c>: the export went on emitting the old
/// name, so it failed to load and every existing backup stopped applying.</item>
/// <item><c>quests.auto_start</c> unlisted: a restore reset every quest's auto-start.</item>
/// <item><c>quests.reward_flag_key</c> unlisted: a restore silently cleared the four attunement
/// gates, which are the game's only progression lock (BUGS.md #6, #7).</item>
/// <item><c>item_templates</c> missing <c>is_lore</c>, <c>is_no_drop</c>, <c>is_light_source</c>
/// and <c>paths</c>: a restore un-bound every epic reward and put out every lamp.</item>
/// <item><c>room_exits</c> missing <c>required_flag_key</c>, <c>required_item_key</c> and
/// <c>refusal_message</c>: a restore opened every locked door and portal in the game (§4.15).</item>
/// </list>
/// <para>
/// <b>The failure is always the same shape and always in the same direction.</b> A missing column
/// does not break the restore — it applies cleanly and writes the column default, so the world
/// comes back looking right and quietly unlocked. Nothing downstream can tell that from a world
/// that was authored that way.
/// </para>
/// <para>
/// So the diff that found all five is this test. It builds the EF model rather than reading a
/// database, which is what lets it run with no Postgres in the picture, and it reads the SQL as
/// text because the SQL is text — there is nothing else to ask.
/// </para>
/// </remarks>
public sealed class ExportScriptCompletenessTests
{
    /// <summary>
    /// Columns the script may omit, each with the reason.
    /// </summary>
    /// <remarks>
    /// Empty, and worth keeping that way: every entry would be a hole in the guard, and this whole
    /// class of defect looks exactly like a missing entry until somebody decides it is fine.
    /// </remarks>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal);

    /// <summary>Model building needs a provider, not a reachable server.</summary>
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<DikuWebDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var db = new DikuWebDbContext(options);
        return db.Model;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DikuWeb.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tools", "export-content.sql"));

    /// <summary>
    /// The columns one table's <c>INSERT INTO</c> lists, or null if the script does not export it.
    /// </summary>
    /// <remarks>
    /// The column list is spread across several concatenated SQL string literals, so the quotes and
    /// the <c>||</c> joins come out before the commas are split on. Matching the whole
    /// <c>INSERT INTO x (...)</c> through to the closing paren is what makes that safe: anything
    /// this fails to parse produces a missing column and a failure, never a silent pass.
    /// </remarks>
    private static IReadOnlyList<string>? ListedColumns(string script, string table)
    {
        var match = Regex.Match(
            script,
            @"INSERT INTO " + Regex.Escape(table) + @" \((?<cols>.*?)\)\s*'",
            RegexOptions.Singleline);

        if (!match.Success)
        {
            return null;
        }

        return [.. match.Groups["cols"].Value
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("||", string.Empty, StringComparison.Ordinal)
            .Split(',')
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)];
    }

    /// <summary>The tables the script exports, read from the script rather than listed here.</summary>
    private static IEnumerable<string> ExportedTables(string script) =>
        Regex.Matches(script, @"INSERT INTO (?<table>\w+) \(")
            .Select(m => m.Groups["table"].Value)
            .Distinct(StringComparer.Ordinal);

    [Fact]
    public void Every_exported_table_names_every_one_of_its_columns()
    {
        var script = Script();
        var model = BuildModel();
        var missing = new List<string>();

        foreach (var table in ExportedTables(script))
        {
            var entity = model.GetEntityTypes().FirstOrDefault(
                e => string.Equals(e.GetTableName(), table, StringComparison.Ordinal));

            Assert.True(entity is not null, $"{table} is exported but is not a table in the model");

            var store = StoreObjectIdentifier.Create(entity!, StoreObjectType.Table);
            Assert.True(store is not null, $"{table} has no store object");

            var listed = ListedColumns(script, table);
            Assert.True(listed is not null, $"could not read the column list for {table}");

            missing.AddRange(entity!.GetProperties()
                .Select(p => p.GetColumnName(store!.Value))
                .Where(c => c is not null
                    && !listed!.Contains(c, StringComparer.Ordinal)
                    && !Exempt.ContainsKey($"{table}.{c}"))
                .Select(c => $"{table}.{c}"));
        }

        Assert.True(
            missing.Count == 0,
            "tools/export-content.sql does not export these columns, so a restore would silently "
            + "write their defaults: " + string.Join(", ", missing.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// And nothing it names has since been renamed away. The <c>sentinel</c> case: the column list
    /// was not short, it was <em>wrong</em>, and that one does not restore quietly — it fails
    /// outright, which is better, but only once somebody runs it.
    /// </summary>
    [Fact]
    public void Every_column_it_names_still_exists()
    {
        var script = Script();
        var model = BuildModel();
        var unknown = new List<string>();

        foreach (var table in ExportedTables(script))
        {
            var entity = model.GetEntityTypes().FirstOrDefault(
                e => string.Equals(e.GetTableName(), table, StringComparison.Ordinal));

            if (entity is null
                || StoreObjectIdentifier.Create(entity, StoreObjectType.Table) is not { } store
                || ListedColumns(script, table) is not { } listed)
            {
                continue;
            }

            var real = entity.GetProperties()
                .Select(p => p.GetColumnName(store))
                .Where(c => c is not null)
                .ToHashSet(StringComparer.Ordinal);

            unknown.AddRange(listed.Where(c => !real.Contains(c)).Select(c => $"{table}.{c}"));
        }

        Assert.True(
            unknown.Count == 0,
            "tools/export-content.sql names columns that do not exist, so the export itself would "
            + "fail: " + string.Join(", ", unknown.Order(StringComparer.Ordinal)));
    }
}
