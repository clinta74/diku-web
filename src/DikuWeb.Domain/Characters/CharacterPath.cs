namespace DikuWeb.Domain.Characters;

/// <summary>
/// PLAN.md §4.5. A Path grants an ability list and shapes stat growth;
/// it does not hard-gate equipment. Fixed at creation and never changed (Q3).
/// </summary>
/// <remarks>
/// <b>These names are persisted, not the ordinals.</b> <c>characters.path</c> is a text column
/// via <c>HasConversion&lt;string&gt;()</c>, which is worth it - the column is readable in a psql
/// session and survives someone reordering this enum - but it makes a rename here a data
/// migration rather than a refactor. Renaming a member without one leaves every existing
/// character of that Path unable to materialise, which takes its account's whole character list
/// down with it. <c>RenameChannelerPathToHallow</c> is the worked example.
/// </remarks>
public enum CharacterPath
{
    /// <summary>Armored frontline.</summary>
    Warden = 0,

    /// <summary>Focus-caster.</summary>
    Adept = 1,

    /// <summary>
    /// Two weapons, one target. Fast, cheap strikes and wounds that keep working.
    /// </summary>
    /// <remarks>
    /// <b>Called Shade until it was read carefully.</b> The name promised stealth and the game has
    /// none — no sneak, no hide, nothing to be unseen by — and four of its ability names promised
    /// mechanics that were not there either: Evasion and Vanish were self-heals, Death Mark was a
    /// flat hit with no mark, Shadowstep did not move you. What the Path is, and always was, is
    /// the one that duals to a full off hand, stacks bleeds, and kills one thing properly.
    /// </remarks>
    Temper = 2,

    /// <summary>Support and control.</summary>
    Hallow = 3,
}
