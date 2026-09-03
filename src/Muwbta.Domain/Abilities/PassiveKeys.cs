namespace Muwbta.Domain.Abilities;

/// <summary>
/// Passives a Path grants by reaching a level. Unlike abilities they are never cast, have no
/// row in the abilities table, and cost nothing - the rules simply ask whether a character has
/// one. Keys are namespaced away from ability keys so nothing can cast a passive by accident.
/// </summary>
public static class PassiveKeys
{
    /// <summary>Lets an off-hand weapon strike at all. Without it the hand only carries.</summary>
    public const string DualWield = "passive.dual-wield";

    /// <summary>Frees the off-hand from the main hand's rhythm, giving it its own timer.</summary>
    public const string Ambidextrous = "passive.ambidextrous";

    /// <summary>
    /// A chance to turn aside a blow that would otherwise have landed.
    /// </summary>
    /// <remarks>
    /// A passive rather than a castable ability: parrying is what a trained fighter does
    /// continuously, not something they stop to do. As an ability it was also strictly worse
    /// than it looked - a self-heal on a 32-pulse cooldown that had to be spent *before* the
    /// blow you wanted to stop.
    /// </remarks>
    public const string Parry = "passive.parry";

    /// <summary>Display name for a passive key, for the abilities screen.</summary>
    public static string NameOf(string key) => key switch
    {
        DualWield => "Dual Wield",
        Ambidextrous => "Ambidextrous",
        Parry => "Parry",
        _ => key,
    };

    /// <summary>One-line description of what a passive does, for the abilities screen.</summary>
    public static string DescriptionOf(string key) => key switch
    {
        DualWield => "You can strike with a weapon in your off hand.",
        Ambidextrous => "Your off hand strikes on its own timing, not the main hand's.",
        Parry => "You sometimes turn aside a blow that would have landed.",
        _ => string.Empty,
    };
}
