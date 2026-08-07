namespace DikuWeb.Domain.Narration;

/// <summary>
/// Shared narration utilities for formatting combat and world event messages.
/// Handles articles (a/an), capitalization, and prose tokenization consistently.
/// </summary>
public static class NarrationHelper
{
    /// <summary>
    /// Whether a name is a proper name, and so takes no article and keeps its capital
    /// wherever it appears: "Grimble hits you", never "a grimble hits you".
    /// </summary>
    /// <remarks>
    /// The signal is the builder's own capitalization. Templates are authored in the case
    /// they should read in ("large rat", "long sword", "Grimble"), so no extra flag is
    /// needed - and a builder who wants the proper-name treatment gets it by typing a
    /// capital, which is what they would do anyway.
    /// </remarks>
    public static bool IsProperName(string name) =>
        !string.IsNullOrEmpty(name) && char.IsUpper(name[0]);

    /// <summary>
    /// Names a thing as a noun phrase with its indefinite article: "a rat", "an orc".
    /// Proper names are returned untouched: "Grimble".
    /// </summary>
    /// <param name="name">The name to add an article to (e.g., "rat", "orc").</param>
    /// <param name="capitalize">
    /// Only when the phrase opens a sentence. This defaults to false because most call sites
    /// embed the phrase mid-sentence ("You see a rat."), and a capital there reads as a
    /// stray proper noun. Callers that start a sentence must say so explicitly.
    /// </param>
    /// <returns>The name with a proper article prefix.</returns>
    public static string WithArticle(string name, bool capitalize = false)
    {
        if (string.IsNullOrEmpty(name) || IsProperName(name))
            return name;

        var result = $"{GetArticle(name)} {name}";
        return capitalize ? Capitalize(result) : result;
    }

    /// <summary>
    /// Names a specific, already-established thing: "the long sword". Proper names are
    /// returned untouched, because "you drop the Grimble" is not English.
    /// </summary>
    public static string WithDefiniteArticle(string name, bool capitalize = false)
    {
        if (string.IsNullOrEmpty(name) || IsProperName(name))
            return name;

        return capitalize ? $"The {name}" : $"the {name}";
    }

    /// <summary>Upper-cases the first character, leaving the rest of the text alone.</summary>
    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

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

        // Process tokens in order of appearance, handling {type:key} syntax
        var tokenPattern = new System.Text.RegularExpressions.Regex(@"\{(\w+):(\w+)\}|\{(\w+)\}");
        var result = tokenPattern.Replace(template, match =>
        {
            string tokenKey;
            var tokenType = "default";

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

            return tokenType switch
            {
                "entity" => WithArticle(value),                     // Add article; the whole line is capitalized below
                "player" => value,                                   // Player names: no article, kept as-is
                "direction" => value.ToLowerInvariant(),            // Directions: lowercase
                _ => value,                                          // Default: pass through
            };
        });

        return Capitalize(result);
    }

    /// <summary>
    /// Builds a whole sentence about one entity: article, capital, and a full stop.
    /// Example: BuildSentence("rat", "is here") → "A rat is here."
    /// Only for text that stands alone - embedding the result inside another sentence
    /// re-introduces the capital. Use <see cref="WithArticle"/> for that.
    /// </summary>
    public static string BuildSentence(string entityName, string predicate)
    {
        var sentence = Capitalize($"{WithArticle(entityName)} {predicate}".Trim());

        // Callers pass predicates both ways ("is here." and "leaves north"), so terminate
        // here rather than leaving half the world's narration without a full stop.
        return EndsSentence(sentence) ? sentence : sentence + ".";
    }

    /// <summary>Gets the appropriate article for a name (a or an).</summary>
    public static string GetArticle(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "a";
        var firstChar = char.ToLowerInvariant(name[0]);
        return "aeiou".Contains(firstChar) ? "an" : "a";
    }

    private static bool EndsSentence(string text) =>
        text.Length > 0 && text[^1] is '.' or '!' or '?';
}
