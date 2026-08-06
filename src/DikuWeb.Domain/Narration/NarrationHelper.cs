namespace DikuWeb.Domain.Narration;

/// <summary>
/// Shared narration utilities for formatting combat and world event messages.
/// </summary>
public static class NarrationHelper
{
    /// <summary>
    /// Adds proper article (A/An) to a name for third-person narration.
    /// Used for mob combat and death messages.
    /// </summary>
    /// <param name="name">The name to add an article to (e.g., "rat", "orc").</param>
    /// <param name="capitalize">If true, capitalize the article (e.g., "A rat" vs "a rat").</param>
    /// <returns>The name with a proper article prefix.</returns>
    public static string WithArticle(string name, bool capitalize = true)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var firstChar = char.ToLowerInvariant(name[0]);
        var article = "aeiou".Contains(firstChar) ? "an" : "a";
        var result = $"{article} {name}";
        return capitalize ? char.ToUpper(result[0]) + result.Substring(1) : result;
    }
}
