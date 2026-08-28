using System.Reflection;
using System.Text.RegularExpressions;
using DikuWeb.Server.Game;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Every <c>Sessions__*</c> key the example deployments set must name a real setting.
/// </summary>
/// <remarks>
/// <para>
/// <b>Configuration that misses its target does not fail. It does nothing.</b> Two keys sat in the
/// shipped compose files for months — <c>Sessions__MaxCharactersPerAccount</c>, which is the right
/// setting under a name missing one word, and <c>Sessions__MaxConcurrentSessions</c>, which was
/// never a setting at all. Both bound to nothing. The files read as though a five-character limit
/// and a hundred-session cap were in force; the engine default of three was, and nothing anywhere
/// said so.
/// </para>
/// <para>
/// A misspelt key is worse than a missing one, because a missing one prompts somebody to add it.
/// This is the cheapest way to make that class of mistake loud: the binder will not complain, so
/// the test does.
/// </para>
/// <para>
/// Scoped to the <c>Sessions</c> section deliberately rather than generalised over every prefix.
/// <c>Engine</c> is read key by key rather than bound (see
/// <see cref="EngineConfigurationBindingTests"/>), and <c>Logging</c> and <c>ConnectionStrings</c>
/// are the framework's own with shapes this has no business asserting. A test that tried to cover
/// all of them would either be wrong or be a second copy of the binder.
/// </para>
/// </remarks>
public sealed class ComposeConfigurationKeysTests
{
    /// <summary>The double underscore is how an environment variable spells a config section.</summary>
    private static readonly Regex SessionKey = new(
        @"^\s*Sessions__(?<key>[A-Za-z0-9_]+)\s*:", RegexOptions.Multiline);

    public static TheoryData<string> ExampleDeployments =>
        [.. Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "example"), "docker-compose*.yml")
            .Select(Path.GetFileName)
            .OfType<string>()];

    [Theory]
    [MemberData(nameof(ExampleDeployments))]
    public void Every_session_key_names_a_real_setting(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "example", file));

        var settable = typeof(SessionRegistryOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Only the uncommented ones. A key named inside a comment is documentation - the two that
        // were removed are still described in those files, and describing them must stay legal.
        var declared = SessionKey.Matches(text)
            .Where(m => !text[..m.Index].Split('\n')[^1].TrimStart().StartsWith('#'))
            .Select(m => m.Groups["key"].Value)
            .ToList();

        var unknown = declared.Where(k => !settable.Contains(k)).ToList();

        Assert.True(
            unknown.Count == 0,
            $"{file} sets {string.Join(", ", unknown)} under Sessions:, which "
            + $"{nameof(SessionRegistryOptions)} has no property for. The binder ignores keys it "
            + "does not recognise, so this would take effect nowhere and say nothing. Valid: "
            + string.Join(", ", settable.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void The_section_holds_exactly_the_settings_the_deployments_declare()
    {
        // Pins the shape the test above depends on. When a setting is added this fails, and
        // whoever added it decides whether the example deployments should declare it - which is
        // the conversation that never happened for the two keys that bound to nothing. It has
        // already earned its keep once: HeartbeatTimeoutSeconds arrived and this caught it.
        //
        // The two are easy to confuse and were confused: one bounds how many characters an
        // account may HAVE, the other how many may be in the world AT ONCE. The shipped compose
        // files set the first for months against a server that only implemented the second.
        var settable = typeof(SessionRegistryOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                nameof(SessionRegistryOptions.HeartbeatTimeoutSeconds),
                nameof(SessionRegistryOptions.MaxCharactersPerAccount),
                nameof(SessionRegistryOptions.MaxConcurrentCharactersPerAccount),
            ],
            settable);
    }

    [Fact]
    public void The_example_deployments_declare_every_session_setting()
    {
        // Not merely "no unknown keys": a deployment that silently dropped one of these would be
        // running an undeclared default, which is the sort of thing nobody notices until it
        // matters. Every one is named, so every one is a deliberate choice.
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "example"), "docker-compose*.yml"))
        {
            var text = File.ReadAllText(file);

            // The GPU variant layers onto the base file rather than repeating its environment,
            // so it legitimately declares nothing here.
            if (!text.Contains("Sessions__", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains("Sessions__MaxCharactersPerAccount", text, StringComparison.Ordinal);
            Assert.Contains(
                "Sessions__MaxConcurrentCharactersPerAccount", text, StringComparison.Ordinal);
            Assert.Contains(
                "Sessions__HeartbeatTimeoutSeconds", text, StringComparison.Ordinal);
        }
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
