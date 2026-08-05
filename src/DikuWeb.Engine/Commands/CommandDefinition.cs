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
public sealed record CommandDefinition(
    string Name,
    int MinLength,
    string Help,
    Action<CommandContext> Handler)
{
    /// <summary>
    /// True when the typed verb is an acceptable abbreviation. Order in the table breaks
    /// ties, so "n" reaches north rather than any later verb starting with n.
    /// </summary>
    public bool Matches(string verb) =>
        verb.Length >= MinLength
        && verb.Length <= Name.Length
        && Name.StartsWith(verb, StringComparison.Ordinal);
}
