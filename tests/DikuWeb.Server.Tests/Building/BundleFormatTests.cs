using System.Text.Json;
using System.Text.RegularExpressions;
using DikuWeb.Server.Building;

namespace DikuWeb.Server.Tests.Building;

/// <summary>
/// The authored content agrees with the build that has to read it (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// <b>The format version is the import path's one hard refusal, and it has been wrong twice.</b>
/// Not in the code — every C# reference already goes to <see cref="WorldBundle.CurrentFormatVersion"/>
/// — but at the edges, where the number has to be restated as data or as prose: the six content
/// files declare it, <c>content/README.md</c> names it, and <c>tools/check-bundle.py</c> keeps a
/// copy. The README said <b>9</b> for two bumps, underneath a sentence warning the reader that it
/// had been wrong before. A comment asking to be kept in step is not a mechanism; this is.
/// </para>
/// <para>
/// <b>Reading through the real record is the half no external checker can do.</b> A script reading
/// raw JSON can confirm a key is present and cannot confirm the server would bind it — and the
/// converters are exactly where that goes wrong, since <c>"paths": ["Warden"]</c> needs
/// <c>JsonStringEnumConverter</c> and throws without it. So these deserialize through
/// <see cref="BundleFormat.SerializerOptions"/>, which is the same set the request pipeline
/// installs.
/// </para>
/// </remarks>
public sealed class BundleFormatTests
{
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

    /// <summary>
    /// Every authored bundle. Fails loudly when there are none, because a glob that silently
    /// matches nothing turns this whole file into a test that always passes.
    /// </summary>
    public static TheoryData<string> ContentFiles()
    {
        var root = Path.Combine(RepoRoot(), "content");
        var data = new TheoryData<string>();

        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(RepoRoot(), path));
        }

        Assert.NotEmpty(data);
        return data;
    }

    [Theory]
    [MemberData(nameof(ContentFiles))]
    public void An_authored_bundle_declares_the_version_this_build_reads(string relativePath)
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        Assert.True(
            BundleFormat.TryRead(json, out var bundle, out var error),
            $"{relativePath} is not a bundle this build can read: {error}");

        Assert.True(
            BundleFormat.IsCurrent(bundle!),
            $"{relativePath}: {BundleFormat.VersionRefusal(bundle!.FormatVersion)} "
            + "Re-export it, or bump it deliberately with the changelog entry that explains why.");
    }

    /// <summary>
    /// And binds — not merely parses. Every one of these counts was zero or threw at some point
    /// while this was being written, each for a different converter.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContentFiles))]
    public void An_authored_bundle_binds_through_the_converters_the_endpoint_uses(string relativePath)
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        Assert.True(BundleFormat.TryRead(json, out var bundle, out var error), error);

        // A spawner id is content: re-minting one doubles a zone's population, so Guid.Empty here
        // would mean the field did not bind rather than that the author left it out.
        Assert.DoesNotContain(bundle!.Spawners, spawner => spawner.Id == Guid.Empty);

        // Enum-valued fields the general converter is responsible for. Named individually because
        // "it deserialised" is true of a record whose every enum silently defaulted.
        Assert.All(bundle.Spawners, spawner => Assert.True(Enum.IsDefined(spawner.TemplateKind)));
        Assert.All(
            bundle.ItemTemplates.Where(item => item.Paths is not null).SelectMany(item => item.Paths!),
            path => Assert.True(Enum.IsDefined(path)));
        Assert.All(
            bundle.ItemTemplates.Where(item => item.Slot is not null),
            item => Assert.True(Enum.IsDefined(item.Slot!.Value)));
    }

    /// <summary>
    /// A bundle from the future is refused rather than half-applied, and says so in the one place
    /// the wording lives.
    /// </summary>
    [Fact]
    public void A_bundle_of_another_version_is_not_current_and_the_refusal_names_both_numbers()
    {
        var stale = new WorldBundle(
            BundleFormat.CurrentVersion - 1,
            DateTimeOffset.UtcNow,
            new BundleScope("all", null),
            [], [], [], [], [], [], [], [], []);

        Assert.False(BundleFormat.IsCurrent(stale));

        var refusal = BundleFormat.VersionRefusal(stale.FormatVersion);
        Assert.Contains((BundleFormat.CurrentVersion - 1).ToString(), refusal, StringComparison.Ordinal);
        Assert.Contains(BundleFormat.CurrentVersion.ToString(), refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading a bundle and judging what it claims to be are separate questions, and a malformed
    /// document must not surface as a version complaint.
    /// </summary>
    [Fact]
    public void Something_that_is_not_a_bundle_reports_where_it_went_wrong()
    {
        Assert.False(BundleFormat.TryRead("{\"formatVersion\": \"eleven\"}", out var bundle, out var error));
        Assert.Null(bundle);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// The pipeline and the file reader install the same converters, which is the whole claim
    /// <see cref="BundleFormat"/> exists to make.
    /// </summary>
    /// <remarks>
    /// Asserted by type rather than by reading <c>Program.cs</c>, because the endpoint's set is
    /// built from <see cref="BundleFormat.Converters"/> now — this is what fails if somebody adds
    /// a converter to one of the two by hand later.
    /// </remarks>
    [Fact]
    public void The_file_reader_carries_every_converter_the_pipeline_installs()
    {
        var expected = BundleFormat.Converters().Select(c => c.GetType()).ToList();
        var actual = BundleFormat.SerializerOptions.Converters.Select(c => c.GetType()).ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
        Assert.Equal(JsonNamingPolicy.CamelCase, BundleFormat.SerializerOptions.PropertyNamingPolicy);
    }

    // -----------------------------------------------------------------------
    // The copies that live outside C#
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>content/README.md</c> tells an author which version to write. It said 9 through two bumps.
    /// </summary>
    [Fact]
    public void The_content_readme_names_the_version_this_build_reads()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "content", "README.md"));

        var stated = Regex.Match(readme, @"these files are at \*\*(\d+)\*\*");

        Assert.True(
            stated.Success,
            "content/README.md no longer states the format version in the shape this reads. "
            + "Restore the sentence or delete this test deliberately - silently losing the check "
            + "is how it was wrong for two bumps.");

        Assert.Equal(BundleFormat.CurrentVersion.ToString(), stated.Groups[1].Value);
    }

    /// <summary>
    /// <c>tools/check-bundle.py</c> keeps its own copy of the number, since it runs with no .NET.
    /// </summary>
    /// <remarks>
    /// <b>Interim.</b> This guards a copy that only exists because the checker is in another
    /// language, and it goes away with the copy when that tool is ported to C# and can reference
    /// <see cref="BundleFormat.CurrentVersion"/> outright. Skipped rather than failed if the file
    /// is gone, so the port does not have to land as one commit with this deletion.
    /// </remarks>
    [Fact]
    public void The_python_checker_agrees_about_the_version_while_it_still_exists()
    {
        var path = Path.Combine(RepoRoot(), "tools", "check-bundle.py");

        if (!File.Exists(path))
        {
            return;
        }

        var declared = Regex.Match(File.ReadAllText(path), @"^FORMAT_VERSION = (\d+)", RegexOptions.Multiline);

        Assert.True(declared.Success, "tools/check-bundle.py no longer declares FORMAT_VERSION where this looks.");
        Assert.Equal(BundleFormat.CurrentVersion.ToString(), declared.Groups[1].Value);
    }
}
