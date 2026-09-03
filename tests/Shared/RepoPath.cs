namespace Muwbta.Tests;

/// <summary>
/// Where the repository root is, for the tests that read files out of the working tree.
/// </summary>
/// <remarks>
/// <b>Why a shared file rather than a base class.</b> The two test projects reference different
/// halves of the solution and nothing in common, so there is no type either could inherit from.
/// This file is linked into both, which is the cheapest thing that leaves exactly one definition.
///
/// <b>Why it is worth having at all.</b> This walk was copy-pasted into eight test files, each
/// naming the solution file as a string. Renaming the solution meant finding all eight, and
/// missing one produced a directory walk that runs off the top of the filesystem and fails with
/// a null rather than saying what it was looking for. One definition means one line to change,
/// and it is a compile error if the name is wrong rather than a runtime surprise.
/// </remarks>
public static class RepoPath
{
    /// <summary>
    /// The file whose presence marks the repository root.
    /// </summary>
    /// <remarks>
    /// The solution file, because it is the one thing guaranteed to sit at the root and nowhere
    /// else. <c>content/</c> and <c>docs/</c> are also root-only today, but that is a convention
    /// rather than a rule, and a nested copy of either would silently move the root.
    /// </remarks>
    private const string Sentinel = "Muwbta.slnx";

    /// <summary>
    /// Walks up from the test binary until it finds the repository root.
    /// </summary>
    /// <remarks>
    /// From <see cref="AppContext.BaseDirectory"/> rather than the current directory, because the
    /// test host's working directory is not something a test should depend on — it differs
    /// between <c>dotnet test</c>, the IDE runner and a bare <c>vstest</c> invocation, while the
    /// binary's own location does not.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The walk reached the top of the filesystem without finding the sentinel — which happens
    /// when the binary is run from outside the working tree, such as a temporary output path.
    /// Said out loud, because the previous version returned a null that surfaced far from here.
    /// </exception>
    public static string Root()
    {
        var start = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(start);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, Sentinel)))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException(
            $"Could not find the repository root: no '{Sentinel}' in any directory above '{start}'.");
    }

    /// <summary>A path inside the repository, from segments.</summary>
    public static string Combine(params string[] parts) => Path.Combine([Root(), .. parts]);
}
