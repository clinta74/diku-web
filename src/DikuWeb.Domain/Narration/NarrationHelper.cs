namespace DikuWeb.Domain.Narration;

/// <summary>
/// Shared narration utilities for formatting combat and world event messages.
/// Handles articles (a/an), capitalization, and prose tokenization consistently.
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

    /// <summary>
    /// Formats a prose message with proper articles, capitalization, and entity handling.
    /// Tokens: {entity:name} adds article, {name} does not, {player:name} formats as player name.
    /// Examples: "entity:rat is here" → "A rat is here."
    ///           "player:Alice gives item:sword" → "Alice gives a sword."
    /// </summary>
    public static string FormatProse(string template, Dictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        var result = template;
        var processed = new HashSet<string>();

        // Process tokens in order of appearance, handling {type:key} syntax
        var tokenPattern = new System.Text.RegularExpressions.Regex(@"\{(\w+):(\w+)\}|\{(\w+)\}");
        result = tokenPattern.Replace(result, match =>
        {
            string tokenKey;
            string tokenType = "default";

            if (match.Groups[1].Success)
            {
                // {type:key} format
                tokenType = match.Groups[1].Value;
                tokenKey = match.Groups[2].Value;
            }
            else
            {
                // {key} format
                tokenKey = match.Groups[3].Value;
            }

            if (!tokens.TryGetValue(tokenKey, out var value))
                return match.Value;

            processed.Add(tokenKey);

            return tokenType switch
            {
                "entity" => WithArticle(value, capitalize: false), // Add article, no cap (will capitalize at sentence start)
                "player" => value,                                   // Player names: no article, kept as-is
                "direction" => value.ToLowerInvariant(),            // Directions: lowercase
                _ => value,                                          // Default: pass through
            };
        });

        // Capitalize first letter
        if (!string.IsNullOrEmpty(result))
        {
            result = char.ToUpper(result[0]) + result.Substring(1);
        }

        return result;
    }

    /// <summary>
    /// Builds a properly formatted prose sentence from a template and entity.
    /// Convenience method for single-entity narration.
    /// Example: BuildSentence("rat", "is here") → "A rat is here."
    /// </summary>
    public static string BuildSentence(string entityName, string predicate)
    {
        var article = GetArticle(entityName);
        var result = $"{article} {entityName} {predicate}";
        if (!string.IsNullOrEmpty(result))
        {
            result = char.ToUpper(result[0]) + result.Substring(1);
        }
        return result;
    }

    /// <summary>Gets the appropriate article for a name (a or an).</summary>
    public static string GetArticle(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "a";
        var firstChar = char.ToLowerInvariant(name[0]);
        return "aeiou".Contains(firstChar) ? "an" : "a";
    }
}
