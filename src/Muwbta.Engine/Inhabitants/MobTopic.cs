using Muwbta.Domain.Characters;

namespace Muwbta.Engine.Inhabitants;

/// <summary>
/// One thing a mob can be asked about: the word that asks it, what they say, and what a
/// character must have done to hear it (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// <b>The second half of a conversation.</b> A <c>greeting</c> is what somebody says when spoken
/// to; a topic is what they say when asked. <c>talk adda stone</c> reaches the topic keyed
/// <c>stone</c>, exactly as it would reach a quest marked with that word — and quests are tried
/// first, so a topic can never steal a word an errand needs.
/// </para>
/// <para>
/// <b>Gated on a flag or a finished quest, never on both being absent.</b> This is how the same
/// person says more once the player has earned it, and how a quest giver keeps driving the story
/// between quests without a fourth quest to hand out: Adda's answer about Grask is closed until
/// <c>attuned.grask</c>, and nothing in the journal says so, which is the point. A topic with no
/// gate is open to everyone.
/// </para>
/// <para>
/// Content, not schema. It lives in the behavior bag beside <c>greeting</c> and <c>emotes</c>,
/// read through <see cref="MobBehavior.TopicsOf"/>, and a row missing its keyword or its text is
/// dropped rather than spoken blank, for the reason a textless emote is.
/// </para>
/// </remarks>
public sealed record MobTopic(
    string Keyword,
    string Text,
    string? RequiresFlag,
    string? RequiresQuest)
{
    /// <summary>
    /// Whether this character may hear it: every gate the topic names is satisfied.
    /// </summary>
    /// <param name="questCompleted">Whether the character has finished the quest with this key.</param>
    public bool IsOpenTo(Character character, Func<string, bool> questCompleted)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(questCompleted);

        return (RequiresFlag is null || character.HasFlag(RequiresFlag))
            && (RequiresQuest is null || questCompleted(RequiresQuest));
    }

    /// <summary>True when <paramref name="word"/> is this topic's keyword, however it was typed.</summary>
    public bool AnswersTo(string word) =>
        Keyword.Equals(word.Trim(), StringComparison.OrdinalIgnoreCase);
}
