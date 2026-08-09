using System.Security.Cryptography;

namespace DikuWeb.Playtest.Session;

/// <summary>
/// Names the server will accept.
/// </summary>
/// <remarks>
/// Character names are 3–16 characters and <b>letters only</b> — see the pattern on
/// <c>CharacterEndpoints.CreateAsync</c> — so the obvious trick of appending a run number does not
/// work. Everything here generates letters.
/// </remarks>
public static class Names
{
    private const int MinLength = 3;
    private const int MaxLength = 16;
    private const int SuffixLength = 5;

    /// <summary>
    /// The names to try for an actor, best first.
    /// </summary>
    /// <remarks>
    /// The plan's own name comes first, because a transcript that says "Theron hits a rat" is worth
    /// a great deal more to a reviewer than one that says "Theronqxbfm hits a rat", and against a
    /// fresh database it is always free. The suffixed forms exist for the second run against a
    /// server that still remembers the first.
    /// </remarks>
    public static IEnumerable<string> Candidates(string preferred)
    {
        var stem = Letters(preferred);

        if (stem.Length >= MinLength)
        {
            yield return Truncate(stem, MaxLength);
        }

        // A stem short enough to leave room for the suffix, so the result stays inside the limit.
        var root = stem.Length >= MinLength
            ? Truncate(stem, MaxLength - SuffixLength)
            : "Actor";

        for (var attempt = 0; attempt < 8; attempt++)
        {
            yield return root + RandomLetters(SuffixLength);
        }
    }

    /// <summary>A name nothing else will have taken, for accounts rather than characters.</summary>
    public static string Unique(string prefix) => prefix + RandomLetters(8);

    private static string Letters(string raw) =>
        new([.. raw.Where(char.IsAsciiLetter)]);

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    /// <summary>
    /// Cryptographically random rather than <c>Random</c>, because several actors are created
    /// within the same millisecond and a time-seeded generator hands them the same name.
    /// </summary>
    private static string RandomLetters(int count)
    {
        var bytes = RandomNumberGenerator.GetBytes(count);
        return new string([.. bytes.Select(b => (char)('a' + (b % 26)))]);
    }
}
