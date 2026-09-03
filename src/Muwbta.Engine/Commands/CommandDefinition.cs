using Muwbta.Domain.Accounts;

namespace Muwbta.Engine.Commands;

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
/// <param name="Hidden">
/// Keeps the verb out of <c>help</c> while leaving it entirely usable. For an <em>alias</em>: a
/// second spelling of a verb that is already listed under its real name, where listing both would
/// only be noise.
/// <para>
/// It exists for <c>kill</c>, which <c>attack</c> replaced. The rename is about the word the game
/// puts in front of people, and unregistering the old one would have taken <c>k</c> - the most
/// reflexive keystroke in the genre - away with it. Distinct from <see cref="Requires"/>, which
/// hides a verb from people who may not use it; this hides one from everybody and works for
/// everybody.
/// </para>
/// </param>
public sealed record CommandDefinition(
    string Name,
    int MinLength,
    string Help,
    Action<CommandContext> Handler,
    AccountRole Requires = AccountRole.Player,
    bool Hidden = false)
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
