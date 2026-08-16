using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

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
