using System.Text.RegularExpressions;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DikuWeb.Server.Tests;

/// <summary>
/// <c>tools/export-abilities.cs</c> names every column of the <c>abilities</c> table.
/// </summary>
/// <remarks>
/// <para>
/// It is the last hand-written column list in the repository. Its predecessor —
/// <c>tools/export-content.sql</c> — had the same shape and drifted from the schema <b>five</b>
/// times, every drift silent and every one in the same direction: a column that stops being
/// emitted is not an error, it restores as the column default. That is how every quest's
/// auto-start was reset, and later how every attunement gate was cleared. The data comes back
/// looking entirely reasonable.
/// </para>
/// <para>
/// The content export was retired for <c>tools/export-bundle.cs</c>, and the player export for
/// <c>tools/export-players.cs</c>; both derive their columns from the model, so neither can drift
/// and neither needs a test. <b>This one cannot follow them.</b> Its input is
/// <c>AbilityCatalogue</c> rather than a database — it deliberately depends on
/// <c>DikuWeb.Domain</c> alone and never opens a connection, which is what lets it run against a
/// database that does not exist yet. Deriving the column list would mean pulling EF Core into it
/// for metadata it otherwise has no use for.
/// </para>
/// <para>
/// So the diff is here instead. Same guarantee, different place: add or rename a column on
/// <c>abilities</c> and this fails, rather than a patch quietly writing a default over a retune.
/// </para>
/// <para>
/// <b>Read the columns off <see cref="IRelationalModel"/>, not
/// <c>IEntityType.GetProperties()</c>.</b> The test this replaces used the latter and inherited its
/// blind spot: a property mapped to a jsonb column through an owned type is simply absent from
/// <c>GetProperties()</c>, with no error — on <c>characters</c> that is two of nineteen columns.
/// The relational model describes the table, which is what an INSERT has to agree with.
/// </para>
/// </remarks>
public sealed class AbilityExportCompletenessTests
{
    private static IRelationalModel Model()
    {
        // No connection is opened - building the model needs a provider, not a database.
        var options = new DbContextOptionsBuilder<DikuWebDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var db = new DikuWebDbContext(options);
        return db.Model.GetRelationalModel();
    }

    private static string Tool() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tools", "export-abilities.cs"));

    /// <summary>
    /// The columns the tool's <c>INSERT INTO abilities (...)</c> lists.
    /// </summary>
    /// <remarks>
    /// The list is spread over concatenated C# string literals, so the quotes and the <c>+</c>
    /// joins come out before the commas are split on. Matching through to the closing paren is
    /// what makes that safe: anything this fails to parse yields a missing column and a failure,
    /// never a silent pass.
    /// </remarks>
    private static IReadOnlyList<string> ListedColumns(string tool)
    {
        var match = Regex.Match(
            tool, @"INSERT INTO abilities \((?<cols>.*?)\) VALUES", RegexOptions.Singleline);

        Assert.True(match.Success, "could not find the INSERT INTO abilities column list");

        return [.. match.Groups["cols"].Value
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("+", string.Empty, StringComparison.Ordinal)
            .Split(',')
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)];
    }

    [Fact]
    public void The_export_names_every_column_of_the_abilities_table()
    {
        var listed = ListedColumns(Tool());

        var actual = Model().Tables
            .First(t => string.Equals(t.Name, "abilities", StringComparison.Ordinal))
            .Columns.Select(c => c.Name);

        var missing = actual.Where(c => !listed.Contains(c, StringComparer.Ordinal)).ToList();

        Assert.True(
            missing.Count == 0,
            "tools/export-abilities.cs does not export these columns, so applying its output would "
            + "silently overwrite them with their defaults: "
            + string.Join(", ", missing.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void The_export_names_no_column_that_does_not_exist()
    {
        // The other direction, and it fails louder rather than quieter: a column that has been
        // renamed leaves the export naming the old one, and the whole patch dies on the first
        // statement. Better than the silent case, still worth catching here rather than at 2am
        // against a live database.
        var listed = ListedColumns(Tool());

        var actual = Model().Tables
            .First(t => string.Equals(t.Name, "abilities", StringComparison.Ordinal))
            .Columns.Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = listed.Where(c => !actual.Contains(c)).ToList();

        Assert.True(
            unknown.Count == 0,
            "tools/export-abilities.cs names columns the abilities table does not have, so its "
            + "output would fail on the first statement: "
            + string.Join(", ", unknown.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void The_upsert_updates_every_column_it_inserts()
    {
        // A column present in the INSERT but missing from the ON CONFLICT ... DO UPDATE SET is the
        // subtlest version of the same bug: the row is correct when it is new and stale when it is
        // not, which is precisely the case this tool exists for - it patches abilities that are
        // already there.
        var tool = Tool();
        var listed = ListedColumns(tool);

        var update = Regex.Match(
            tool, @"ON CONFLICT \(key\) DO UPDATE SET(?<set>.*?);", RegexOptions.Singleline);

        Assert.True(update.Success, "could not find the ON CONFLICT ... DO UPDATE SET clause");

        var assigned = Regex.Matches(update.Groups["set"].Value, @"(?<col>\w+) = EXCLUDED\.")
            .Select(m => m.Groups["col"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // `key` is the conflict target, so it is the one column that must not be assigned.
        var unassigned = listed
            .Where(c => !string.Equals(c, "key", StringComparison.Ordinal) && !assigned.Contains(c))
            .ToList();

        Assert.True(
            unassigned.Count == 0,
            "tools/export-abilities.cs inserts these columns but does not update them on conflict, "
            + "so re-applying it would leave them stale: "
            + string.Join(", ", unassigned.Order(StringComparer.Ordinal)));
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
}
