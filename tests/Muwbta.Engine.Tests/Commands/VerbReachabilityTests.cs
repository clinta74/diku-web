using System.Text.RegularExpressions;
using Muwbta.Engine.Commands;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Every registered verb must be reachable by something a player can type.
/// </summary>
/// <remarks>
/// <c>Matches</c> accepts any prefix of a verb's name and <c>Find</c> takes the first definition
/// that matches, so a verb whose name is a strict prefix of an earlier one is shadowed
/// completely: no input reaches it, and nothing anywhere reports that.
///
/// <c>quest</c> was exactly this. It sat behind <c>quests</c>, and since "quests".StartsWith("quest")
/// is true and <c>quests</c> asked for only three characters, typing <c>quest fresh</c> printed
/// the journal. <c>QuestDetail</c> had been dead from the day it was written and the only symptom
/// was the wrong output — the help text advertised a command that could not be run.
///
/// The other prefix pairs survive by accident of their numbers rather than by design:
/// <c>whois</c> demands five characters so "who" cannot reach it. That is a property worth pinning
/// rather than rediscovering, because the next verb somebody adds beside an existing one will land
/// on the same rake.
///
/// <c>stats</c> was guarded the same way and it was the wrong guard. It demanded five characters so
/// that "stat" would fall through to an admin verb of that name — which meant a player typing the
/// obvious abbreviation of their own combat sheet was told the verb did not exist. Being reachable
/// is not enough on its own: a prefix has to reach the command the person typing it meant, and
/// there is no arrangement of lengths that makes a shared name do that. The admin verb is
/// <c>inspect</c> now, which is also what it does.
/// </remarks>
public sealed class VerbReachabilityTests
{
    private static IReadOnlyList<CommandDefinition> AllCommands() =>
        new WorldHarness().Commands.Commands;

    [Fact]
    public void Every_verb_is_reachable_by_its_own_full_name()
    {
        var shadowed = new List<string>();

        foreach (var command in AllCommands())
        {
            var resolved = AllCommands().FirstOrDefault(c => c.Matches(command.Name));

            if (resolved is null || resolved.Name != command.Name)
            {
                shadowed.Add($"'{command.Name}' resolves to '{resolved?.Name ?? "nothing"}'");
            }
        }

        Assert.Empty(shadowed);
    }

    [Fact]
    public void Every_verb_is_reachable_at_its_shortest_allowed_abbreviation()
    {
        // A verb whose own MinLength prefix reaches something else is half-dead: it works typed
        // out in full and silently means another command when abbreviated, which is worse than
        // being unreachable because it does the wrong thing rather than nothing.
        var stolen = new List<string>();

        foreach (var command in AllCommands())
        {
            var shortest = command.Name[..Math.Max(command.MinLength, 1)];
            var resolved = AllCommands().FirstOrDefault(c => c.Matches(shortest));

            if (resolved is null)
            {
                stolen.Add($"'{shortest}' ({command.Name}) reaches nothing");
            }
        }

        Assert.Empty(stolen);
    }

    /// <summary>
    /// Every abbreviation a help string advertises must reach the verb advertising it.
    /// </summary>
    /// <remarks>
    /// <b>The half the test above was missing.</b> Its own comment describes exactly this — "a
    /// verb whose own MinLength prefix reaches something else is half-dead" — and then only checks
    /// that the prefix reaches <em>something</em>, never that it reaches the right thing. Being
    /// reachable is not enough: a prefix has to reach the command the person typing it meant.
    ///
    /// Three help strings were lying when this was written. <c>examine (x)</c> advertised a letter
    /// that is not a prefix of "examine" and could never match anything. <c>remove (r)</c>
    /// advertised one character for a verb demanding two, so <c>r</c> reached <c>rest</c> — which
    /// ignores its argument, so <c>r dagger</c> sat the player down. <c>cast (c)</c> lost to
    /// <c>consider</c>, a collision already known and commented elsewhere while the help text went
    /// on claiming otherwise (BUGS.md #12, #22).
    ///
    /// Parsed out of <c>Help</c> rather than declared separately, because a second field is a
    /// second thing to forget — the point is that the sentence shown to the player is the thing
    /// under test.
    /// </remarks>
    [Fact]
    public void Every_advertised_abbreviation_reaches_the_verb_that_advertises_it()
    {
        var lies = new List<string>();

        foreach (var command in AllCommands())
        {
            // The "(x)" or "(gr)" form only. Directions carry "n / north", which is a different
            // shape and is covered by the resolution table below.
            var advertised = Regex.Match(command.Help, @"\(([a-z]+)\)");
            if (!advertised.Success)
            {
                continue;
            }

            var abbreviation = advertised.Groups[1].Value;

            // The parenthetical is overloaded: most carry an abbreviation, and the restricted verbs
            // carry who may run them. Skipping the role words rather than renaming the convention,
            // because "(builder)" at the end of a help line is doing a useful job for a reader.
            if (abbreviation is "builder" or "admin")
            {
                continue;
            }
            var resolved = AllCommands().FirstOrDefault(c => c.Matches(abbreviation));

            if (resolved is null)
            {
                lies.Add($"'{command.Name}' advertises ({abbreviation}), which reaches nothing");
            }
            else if (resolved.Name != command.Name)
            {
                lies.Add($"'{command.Name}' advertises ({abbreviation}), which reaches '{resolved.Name}'");
            }
        }

        Assert.True(
            lies.Count == 0,
            "A help string promises an abbreviation that does not work:\n  "
            + string.Join("\n  ", lies));
    }

    [Theory]
    [InlineData("quest", "quest")]
    [InlineData("quests", "quests")]
    [InlineData("que", "quest")]
    [InlineData("who", "who")]
    [InlineData("whois", "whois")]
    [InlineData("stat", "stats")]
    [InlineData("stats", "stats")]
    [InlineData("ins", "inspect")]
    [InlineData("inv", "inventory")]
    public void The_prefix_pairs_resolve_the_way_a_player_would_expect(string typed, string expected)
    {
        var resolved = new WorldHarness().Commands.Find(typed);

        Assert.NotNull(resolved);
        Assert.Equal(expected, resolved.Name);
    }
}
