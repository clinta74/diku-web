namespace DikuWeb.Domain.Characters;

/// <summary>
/// PLAN.md §4.5. A Path grants an ability list and shapes stat growth;
/// it does not hard-gate equipment.
/// </summary>
public enum CharacterPath
{
    /// <summary>Armored frontline.</summary>
    Warden = 0,

    /// <summary>Focus-caster.</summary>
    Adept = 1,

    /// <summary>Stealth and burst.</summary>
    Shade = 2,

    /// <summary>Support and control.</summary>
    Channeler = 3,
}
