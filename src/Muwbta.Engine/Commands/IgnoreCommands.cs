using System.Text.RegularExpressions;
using Muwbta.Domain.Accounts;

namespace Muwbta.Engine.Commands;

/// <summary>
/// A player's own answer to being bothered, without waiting for a moderator.
/// </summary>
/// <remarks>
/// <b>Why.</b> Harassment relief used to be a moderator's mute and nothing else, so a player being
/// followed around by somebody unpleasant while no admin was online had one option, which was to
/// log out. This is the other option. It is per character and it persists, because an ignore
/// that reset on logout would be a promise the game broke every evening.
///
/// <b>What it covers.</b> Tells (the sender is told they are not being listened to, since a tell
/// that vanishes silently reads as a bug), room speech and emotes, the world channel, and party
/// chat. Not movement or combat narration: those are things happening in the room, not things
/// being said to you, and hiding them would leave a player fighting somebody they cannot see.
///
/// <b>What it cannot cover.</b> Staff. A moderator telling you to stop is not a conversation you
/// get to opt out of, so a staff account cannot be ignored at all and a staff sender is delivered
/// whatever the list says. Which is one more reason the staff tag exists (PLAN.md, the impersonation
/// findings): a player has to be able to see who that rule applies to.
///
/// <b>Names, not accounts.</b> An ignore names a character, checked case-insensitively the way
/// names compare everywhere else. It does not have to be online, or even exist - "I do not want
/// to hear from anyone called that" is a reasonable thing to mean, and the loop has no way to
/// ask the database whether it does.
/// </remarks>
internal static partial class IgnoreCommands
{
    /// <summary>
    /// More than this is not a list of people, it is a policy; the moderators should hear about it.
    /// </summary>
    internal const int MaxIgnored = 50;

    public static void Register(List<CommandDefinition> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        // "ig": inventory answers to "i".
        commands.Add(new CommandDefinition(
            "ignore", 2, "ignore [name] - stop hearing from someone, or list who you are not hearing", Ignore));

        // "unig": unequip takes "une", unlock "unl".
        commands.Add(new CommandDefinition(
            "unignore", 4, "unignore <name> - hear from them again", Unignore));
    }

    private static void Ignore(CommandContext ctx)
    {
        var list = ctx.Actor.Character.IgnoredNames;

        if (string.IsNullOrWhiteSpace(ctx.Argument))
        {
            ctx.Reply(
                list.Count == 0
                    ? "You are listening to everyone."
                    : $"Not listening to: {string.Join(", ", list.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}.",
                "sys");
            return;
        }

        var name = ctx.Argument.Trim();

        if (!NamePattern().IsMatch(name))
        {
            ctx.Reply("Ignore whom? A character's name is letters only.", "bad");
            return;
        }

        if (string.Equals(name, ctx.Actor.Name, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Reply("You cannot ignore yourself, though some days it must be tempting.", "bad");
            return;
        }

        // Somebody online with that name and a staff role: refuse, and say why, because the
        // silence would otherwise look like the ignore working.
        if (ctx.World.FindPlayerByName(name) is { } online && online.Role != AccountRole.Player)
        {
            ctx.Reply($"{online.TaggedName} is staff. You cannot ignore staff.", "bad");
            return;
        }

        if (list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ctx.Reply($"You are already not listening to {name}.", "bad");
            return;
        }

        if (list.Count >= MaxIgnored)
        {
            ctx.Reply(
                $"You are not listening to {MaxIgnored} people already. If it is that bad, tell a moderator.",
                "bad");
            return;
        }

        list.Add(name);
        ctx.Reply($"You stop listening to {name}.", "sys");
    }

    private static void Unignore(CommandContext ctx)
    {
        var list = ctx.Actor.Character.IgnoredNames;
        var name = ctx.Argument.Trim();

        if (name.Length == 0)
        {
            ctx.Reply("Unignore whom?", "bad");
            return;
        }

        var index = list.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            ctx.Reply($"You were listening to {name} already.", "bad");
            return;
        }

        list.RemoveAt(index);
        ctx.Reply($"You listen to {name} again.", "sys");
    }

    /// <summary>The same shape a character name has to have to be created.</summary>
    [GeneratedRegex("^[A-Za-z]{3,16}$")]
    private static partial Regex NamePattern();
}
