namespace Muwbta.Domain.Narration;

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
        {
            return name;
        }

        // A name that is already a whole noun phrase keeps its own wording. Templates are
        // authored by hand and "a rat" is at least as natural a thing to type as "rat", so
        // without this a builder's own wording decides whether the game says "an a rat" - and it
        // would say it in combat, in the room listing, and in every ability that names a target.
        if (StandsAlone(name))
        {
            return capitalize ? Capitalize(name) : name;
        }

        var result = $"{GetArticle(name)} {name}";
        return capitalize ? Capitalize(result) : result;
    }

    /// <summary>Whether this name already opens with an article, which can be swapped for another.</summary>
    private static bool HasArticle(string name) => Opens(name, "a", "an", "the");

    /// <summary>
    /// Whether this name is already a complete noun phrase, and so takes no article at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two predicates rather than one, and the difference is load-bearing.</b> A name opening
    /// with an article can have that article <em>replaced</em> — "a rat" becomes "the rat" by
    /// dropping the first word. A name opening with a pronoun cannot: dropping the first word of
    /// "one of the owed" leaves "of the owed". So the pronouns live here and nowhere else.
    /// </para>
    /// <para>
    /// <b>Six mobs and two items were reading wrong.</b> The Reaches names its unquiet dead
    /// "one of the owed", "one of the long held", "someone who has been waiting" — noun phrases
    /// that head themselves. Prefixing an article produced <em>"an one of the owed"</em>, which
    /// appeared about forty times in a single fight: once per swing, once per miss, and again
    /// when it fell.
    /// </para>
    /// <para>
    /// Plurals are deliberately not detected. A name is plural far too often for a string to tell
    /// — "harness", "grass" and "keepsakes" all end the same way — so a plural item is authored
    /// with a phrase that carries its own article instead: "a pair of quiet wraps".
    /// </para>
    /// </remarks>
    private static bool StandsAlone(string name) =>
        HasArticle(name) || Opens(name, "one", "someone", "somebody", "something");

    /// <summary>Whether the name's first word is one of these, possessive or not.</summary>
    /// <remarks>
    /// Matched as a whole word rather than a prefix, or "oneiric" would answer to "one" and a
    /// one-word name would match itself. The trailing <c>'s</c> comes off first, so
    /// "somebody's keepsake" is recognised as the phrase it is — a possessive pronoun heads a
    /// noun phrase every bit as completely as a bare one does.
    /// </remarks>
    private static bool Opens(string name, params string[] words)
    {
        var space = name.IndexOf(' ', StringComparison.Ordinal);

        if (space <= 0)
        {
            return false;
        }

        var first = name[..space];

        if (first.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
        {
            first = first[..^2];
        }

        return words.Any(w => first.Equals(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Names a specific, already-established thing: "the long sword". Proper names are
    /// returned untouched, because "you drop the Grimble" is not English.
    /// </summary>
    public static string WithDefiniteArticle(string name, bool capitalize = false)
    {
        if (string.IsNullOrEmpty(name) || IsProperName(name))
        {
            return name;
        }

        // Same reasoning as WithArticle: a name authored as "a rat" becomes "the rat" rather than
        // "the a rat", because what the builder wrote was a noun phrase, not a bare noun.
        if (HasArticle(name))
        {
            var noun = name[(name.IndexOf(' ', StringComparison.Ordinal) + 1)..];
            return capitalize ? $"The {noun}" : $"the {noun}";
        }

        // A pronoun-led phrase keeps every word. Swapping its first word out, the way an article
        // is swapped above, would turn "one of the owed" into "the of the owed".
        if (StandsAlone(name))
        {
            return capitalize ? Capitalize(name) : name;
        }

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
        {
            return template;
        }

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
            {
                return match.Value;
            }

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

    /// <summary>
    /// Conjugates an authored attack verb for third person: "slash" → "slashes", "parry" →
    /// "parries", "bite" → "bites". A blank verb becomes "hits", so a weapon that declares
    /// nothing narrates exactly as the game did before weapons had verbs.
    /// </summary>
    /// <remarks>
    /// Verbs are authored in the base form, and only the first word is conjugated so a phrase
    /// like "chops at" reads "chops at" rather than "chops ats". There is no way to detect a
    /// verb someone typed already-conjugated - "hits" becomes "hitses" and the engine cannot
    /// know better - which is why the builder field says base form.
    /// </remarks>
    public static string ThirdPerson(string verb)
    {
        if (string.IsNullOrWhiteSpace(verb))
        {
            return "hits";
        }

        var trimmed = verb.Trim();

        // Only the head verb inflects; anything after the first space is a particle or object.
        var split = trimmed.IndexOf(' ');
        var head = split < 0 ? trimmed : trimmed[..split];
        var tail = split < 0 ? string.Empty : trimmed[split..];

        return Inflect(head) + tail;
    }

    private static string Inflect(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        var lower = word.ToLowerInvariant();
        var last = lower[^1];
        var previous = word.Length > 1 ? lower[^2] : 'a';
        var previousIsVowel = "aeiou".Contains(previous);

        // parry → parries, but slay → slays: only a consonant before the y takes the -ies form.
        if (last == 'y' && !previousIsVowel)
        {
            return word[..^1] + "ies";
        }

        // Sibilants and a consonant-final o need the extra syllable: slashes, crushes, boxes, goes.
        var needsEs =
            last is 's' or 'x' or 'z' ||
            lower.EndsWith("ch", StringComparison.Ordinal) ||
            lower.EndsWith("sh", StringComparison.Ordinal) ||
            (last == 'o' && !previousIsVowel);

        return needsEs ? word + "es" : word + "s";
    }

    /// <summary>
    /// Joins names into an English list: "a rat", "a rat and a crow", "a rat, a crow and a dog".
    /// </summary>
    /// <remarks>
    /// A joiner and nothing more - articles are the caller's business, because the same list is
    /// wanted with them ("drops a fang and a token") and without ("Bram, Wen and Kaeda").
    ///
    /// No Oxford comma, to match the rest of the game's prose.
    /// </remarks>
    public static string List(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
        };
    }

    /// <summary>
    /// The article a name takes: "a" or "an", chosen by how the name <b>sounds</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Spelling alone gets this wrong in both directions</b>, and English keeps the exceptions
    /// in a short list: a leading <c>u</c> that says "you" takes "a" (a unit, a user), and a
    /// silent <c>h</c> takes "an" (an hour, an heir). The one that turned up in play was
    /// <c>one</c> — a vowel on the page and a consonant in the mouth.
    /// </para>
    /// <para>
    /// Prefix-matched rather than word-matched, because the sound belongs to the start of the
    /// word: "unicorn", "united" and "usable" all begin the same way and all take "a". That is
    /// also why the list holds stems rather than whole words.
    /// </para>
    /// </remarks>
    public static string GetArticle(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "a";
        }

        var word = name.Split(' ', '-')[0];

        if (WholeWordConsonant.Any(w => word.Equals(w, StringComparison.OrdinalIgnoreCase))
            || StemConsonant.Any(w => word.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
        {
            return "a";
        }

        if (StemVowel.Any(w => word.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
        {
            return "an";
        }

        return "aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an" : "a";
    }

    /// <summary>
    /// Vowel on the page, consonant in the mouth, and only as the whole word: "a one-eyed dog".
    /// </summary>
    /// <remarks>
    /// Whole-word rather than stem, because "oneiric" opens with the same three letters and is
    /// said "oh-", so it takes "an". The first word is split on the hyphen as well as the space,
    /// which is what lets "one" here cover "one-eyed".
    /// </remarks>
    private static readonly string[] WholeWordConsonant = ["one", "once", "ewe"];

    /// <summary>
    /// The same, as a stem, where every word built on it sounds alike: "a unicorn", "a eulogy".
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. <c>un</c> would be wrong — "an unclaimed thing" — so the "you" sound
    /// is spelled out as far as it takes to be sure of it.
    /// </remarks>
    private static readonly string[] StemConsonant = ["uni", "use", "usu", "utili", "eu"];

    /// <summary>Consonant on the page, vowel in the mouth: "an hour", "an honest mistake".</summary>
    private static readonly string[] StemVowel = ["hour", "honest", "honour", "honor", "heir"];

    private static bool EndsSentence(string text) =>
        text.Length > 0 && text[^1] is '.' or '!' or '?';
}
