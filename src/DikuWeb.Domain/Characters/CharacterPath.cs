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

    /// <summary>Stealth and burst.</summary>
    Shade = 2,

    /// <summary>Support and control.</summary>
    Hallow = 3,
}
