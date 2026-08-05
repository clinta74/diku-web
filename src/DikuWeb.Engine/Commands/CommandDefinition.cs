using DikuWeb.Domain.Accounts;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// One verb in the command table.
/// </summary>
/// <param name="Name">Full verb, lowercase.</param>
/// <param name="MinLength">
/// Shortest prefix a player may type. "look" is happy at 1 character; "quit" demands all
/// four, because losing a character to a stray keypress is not a good experience.
/// </param>
/// <param name="Help">One line shown by the help command.</param>
/// <param name="Handler">Executes the command.</param>
/// <param name="Requires">
/// The role needed to see this verb in <c>help</c>. Handlers still check for themselves - this
/// only keeps the verb out of sight, so a player never learns the world can be edited from here
/// and nobody below Admin learns that roles can be granted from here.
/// </param>
public sealed record CommandDefinition(
    string Name,
    int MinLength,
    string Help,
    Action<CommandContext> Handler,
    AccountRole Requires = AccountRole.Player)
{
    /// <summary>
    /// True when the typed verb is an acceptable abbreviation. Order in the table breaks
    /// ties, so "n" reaches north rather than any later verb starting with n.
    /// </summary>
    public bool Matches(string verb) =>
        verb.Length >= MinLength
        && verb.Length <= Name.Length
        && Name.StartsWith(verb, StringComparison.Ordinal);

    public bool VisibleTo(AccountRole role) => role.Satisfies(Requires);
}
