using System.Text.RegularExpressions;

namespace Muwbta.Server.Tests;

/// <summary>
/// A migration that cannot run against a database with rows in it is a migration that fails on
/// every deployment but a fresh one.
/// </summary>
/// <remarks>
/// <b>The failure this guards against, which happened.</b> <c>CharacterIgnoreList</c> added
/// <c>ignored_names text[] NOT NULL</c> with no default. Postgres refuses to add such a column to
/// a table that has any rows — and the beta had characters, so the startup migrator threw and
/// the server did not come up. The suite had passed, because the test database is migrated
/// before anything is inserted into it: every migration in this project has only ever been
/// tested against empty tables.
///
/// <b>Why a static check rather than a migration against a populated database.</b> Populating
/// the previous schema means inserting rows by hand into tables whose shape is exactly what the
/// migrations keep changing, which is a test that breaks for reasons that are not the bug. This
/// reads the migration source instead: every <c>AddColumn</c> that is <c>nullable: false</c> must
/// say what the existing rows get. It would have caught the case above, and it costs nothing.
/// </remarks>
public sealed partial class MigrationSafetyTests
{
    [Fact]
    public void Every_required_column_added_to_an_existing_table_says_what_the_rows_already_there_get()
    {
        var migrations = Directory
            .EnumerateFiles(RepoPath.Combine("src", "Muwbta.Persistence", "Migrations"), "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .ToList();

        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in migrations)
        {
            var source = File.ReadAllText(file);

            foreach (Match call in AddColumn().Matches(source))
            {
                inspected++;
                var body = call.Groups["body"].Value;

                if (body.Contains("nullable: false", StringComparison.Ordinal)
                    && !body.Contains("defaultValue", StringComparison.Ordinal))
                {
                    var table = Named().Match(body).Groups["table"].Value;
                    var column = Named().Match(body).Groups["name"].Value;
                    offenders.Add($"{Path.GetFileName(file)}: {table}.{column}");
                }
            }
        }

        // Not vacuous: a pattern that matched nothing would pass by inspecting nothing. The
        // first version of the scan did exactly that, because AddColumn<List<string>> has a
        // generic argument with its own closing bracket.
        Assert.True(inspected > 0, "No AddColumn calls were found in any migration; the pattern is broken.");

        Assert.True(
            offenders.Count == 0,
            "These migrations add a NOT NULL column with no default, which Postgres refuses on a "
            + "table that already has rows - every deployment but a fresh one. Give the column a "
            + "defaultValue or defaultValueSql so the existing rows have something to hold: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// One AddColumn call. The generic argument is matched non-greedily up to the first bracket
    /// that is followed by the opening parenthesis, so a nested generic does not end it early.
    /// </summary>
    [GeneratedRegex(@"migrationBuilder\.AddColumn<.*?>\s*\((?<body>.*?)\);", RegexOptions.Singleline)]
    private static partial Regex AddColumn();

    [GeneratedRegex(@"name:\s*""(?<name>[^""]+)"".*?table:\s*""(?<table>[^""]+)""", RegexOptions.Singleline)]
    private static partial Regex Named();
}
