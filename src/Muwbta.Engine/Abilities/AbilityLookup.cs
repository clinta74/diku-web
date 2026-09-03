using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;

namespace Muwbta.Engine.Abilities;

/// <summary>
/// Works out which ability a player meant, and what they aimed it at.
/// </summary>
/// <remarks>
/// This used to be three lines inside <c>cast</c>: take the first word, and match it against the
/// ability <em>key</em> with <c>EndsWith</c> or <c>Contains</c>. Two things were wrong with that,
/// and both were found by walking the catalogue rather than by playing.
///
/// <b>Keys are not what a player sees.</b> The match never looked at the display name, so the only
/// reliable way to cast Shield Bash was to know it is stored as <c>warden.shield-bash</c> and type
/// the hyphen. The name is what <c>abilities</c> prints and what the builder shows.
///
/// <b>The first word is not the ability.</b> Splitting on the first space made
/// <c>cast shield bash rat</c> mean "cast shield at bash", so every multi-word ability in the
/// catalogue - eight of them - was unreachable by name. The ability is matched by taking the
/// <em>longest</em> name that fits the front of what was typed, which is the only way to tell the
/// end of a two-word ability from the start of a target.
/// </remarks>
public static class AbilityLookup
{
    /// <summary>
    /// The shortest abbreviation that may reach an ability by prefix.
    /// </summary>
    /// <remarks>
    /// <b>A single letter must not fire an ability.</b> A bare verb the command table misses is
    /// tried as an ability (<c>CommandRegistry.FindAbilityVerb</c>), and the fallback below ranks
    /// prefixes - so <c>m</c>, typed alone or as the tail of a fumbled command, reached Mass
    /// Provocation and spent the cooldown. Every verb in the table already carries a
    /// <c>MinLength</c> for exactly this reason; abilities arrive through a different door and had
    /// none.
    /// <para>
    /// Three, because the shortest ability in the catalogue is Sap and a rule that cannot spell the
    /// shortest name it governs is the wrong rule. It binds the <em>fuzzy</em> pass only: a name,
    /// key, or slug typed in full still resolves at any length, so a future two-letter ability is
    /// reachable by typing it.
    /// </para>
    /// </remarks>
    public const int MinimumAbbreviation = 3;

    /// <summary>What the player meant, or nulls when nothing they know answers to it.</summary>
    public readonly record struct Match(Ability? Ability, string? Target)
    {
        public bool Found => Ability is not null;
    }

    /// <summary>
    /// Resolves typed text against the abilities this character has actually learned.
    /// </summary>
    /// <remarks>
    /// Scoped to what they know rather than to the whole catalogue on purpose: a Warden typing
    /// <c>firestorm</c> should be told they do not know it, not that it does not exist. That
    /// distinction is the difference between a wrong Path and a typo.
    /// </remarks>
    public static Match Resolve(AbilityCache? cache, Character character, string typed)
    {
        ArgumentNullException.ThrowIfNull(character);

        var text = (typed ?? string.Empty).Trim();

        if (cache is null || text.Length == 0)
        {
            return default;
        }

        // Filtered out of the cache rather than looked up key by key: the cache is now the source
        // of the unlock table as well as of the abilities themselves, so there is no second list
        // to reconcile a key against.
        var known = cache.All.Values
            .Where(a => a.Path == character.Path && a.UnlockLevel <= character.Level)
            .ToList();

        if (known.Count == 0)
        {
            return default;
        }

        // Longest first, so "Shield Bash" wins over "Bash" when both would fit - otherwise a
        // Warden who knows both could never reach the longer one.
        Ability? best = null;
        var bestLength = 0;

        foreach (var ability in known)
        {
            foreach (var form in Forms(ability))
            {
                if (form.Length > bestLength && StartsWithWord(text, form))
                {
                    best = ability;
                    bestLength = form.Length;
                }
            }
        }

        if (best is not null)
        {
            var remainder = text[bestLength..].Trim();
            return new Match(best, remainder.Length == 0 ? null : remainder);
        }

        // Nothing matched in full. Fall back to the first word, matched the way every other
        // targeting command matches - so "cast fire rat" still reaches Firestorm, and a partial
        // name behaves the way a partial item or mob name does. Held to MinimumAbbreviation,
        // because a partial item name costs a look and a partial ability name costs a cooldown.
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var head = space < 0 ? text : text[..space];
        var tail = space < 0 ? null : text[(space + 1)..].Trim();

        if (head.Length < MinimumAbbreviation)
        {
            return default;
        }

        var fuzzy = NameMatch.Best(known, head, a => a.Name, a => a.Key);

        return fuzzy is null
            ? default
            : new Match(fuzzy, string.IsNullOrEmpty(tail) ? null : tail);
    }

    /// <summary>
    /// Every way of writing this ability that a player might reasonably type.
    /// </summary>
    /// <remarks>
    /// The display name, the key's own segment, and that segment with its hyphens opened out -
    /// so <c>shield bash</c>, <c>shield-bash</c>, and <c>warden.shield-bash</c> all arrive at the
    /// same place. Content authored with a name that differs from its key is covered by both.
    /// </remarks>
    private static IEnumerable<string> Forms(Ability ability)
    {
        if (!string.IsNullOrWhiteSpace(ability.Name))
        {
            yield return ability.Name.Trim().ToLowerInvariant();
        }

        var key = ability.Key.ToLowerInvariant();
        yield return key;

        var dot = key.LastIndexOf('.');
        var slug = dot < 0 ? key : key[(dot + 1)..];

        if (slug.Length > 0)
        {
            yield return slug;

            if (slug.Contains('-', StringComparison.Ordinal))
            {
                yield return slug.Replace('-', ' ');
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> begins with <paramref name="form"/> as whole words.
    /// </summary>
    /// <remarks>
    /// The word boundary is what stops "bash" matching the front of "bashful" and, more to the
    /// point, stops a one-word ability swallowing the first half of a two-word one.
    /// </remarks>
    private static bool StartsWithWord(string text, string form) =>
        text.StartsWith(form, StringComparison.OrdinalIgnoreCase) &&
        (text.Length == form.Length || text[form.Length] == ' ');
}
