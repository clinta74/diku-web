using System.Reflection;

namespace Muwbta.Server.Assist;

/// <summary>
/// The world canon, as one byte-stable block of text that goes in front of every request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Byte-stable is the requirement, not a nicety.</b> Ollama reuses the KV cache for a prompt
/// that shares a prefix with the last one, and measured, that is the difference between 4.4 s and
/// 187 s (tools/ollama/README.md). One stray character - a reordered section, a trailing newline
/// from a different reader - costs three minutes and does so invisibly, because the answer is
/// still correct.
/// </para>
/// <para>
/// <b>Embedded rather than read from disk.</b> A path in configuration is a path that can be
/// missing in a container, different between two servers, or edited under a running process - and
/// each of those is a cache miss or an inconsistency that nothing reports. Embedding makes the
/// prefix a property of the build: it is present, it is identical everywhere that build runs, and
/// changing it is a deploy rather than a surprise.
/// </para>
/// <para>
/// <b>Cut at a marker the document declares itself.</b> §10 is authoring process - how content
/// lands, what was retired, notes to builders - which is true, useful, and no part of what the
/// world <em>is</em>. It is also 3,000 tokens, and the budget does not have 3,000 tokens spare.
/// Slicing on a marker rather than a line number or a heading means renumbering the sections does
/// not silently change what the model is told.
/// </para>
/// </remarks>
public static class Canon
{
    /// <summary>The line in <c>docs/WORLD.md</c> that ends the canon and begins the process notes.</summary>
    public const string EndMarker = "<!-- canon:end -->";

    private const string ResourceName = "Muwbta.Server.Canon.WORLD.md";

    private static readonly Lazy<string> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Everything before <see cref="EndMarker"/>, with line endings normalised.
    /// </summary>
    /// <remarks>
    /// Normalised because git may check the file out with either ending depending on the machine,
    /// and a prefix that differs between a developer's server and the deployed one is a prefix
    /// that shares no cache with itself.
    /// </remarks>
    public static string Prefix => Loaded.Value;

    private static string Load()
    {
        using var stream = typeof(Canon).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"'{ResourceName}' is not embedded. The assist cannot run without the canon, and a "
                + "server that started anyway would answer every request from a blank world.");

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);

        var end = text.IndexOf(EndMarker, StringComparison.Ordinal);

        if (end < 0)
        {
            throw new InvalidOperationException(
                $"docs/WORLD.md has no '{EndMarker}'. Without it the whole document goes to the "
                + "model, including the authoring notes, and the prefix no longer fits the window.");
        }

        return text[..end].TrimEnd() + "\n";
    }
}
