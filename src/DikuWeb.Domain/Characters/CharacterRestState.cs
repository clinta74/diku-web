namespace DikuWeb.Domain.Characters;

public enum CharacterRestState
{
    /// <summary>Deepest rest: character is asleep. Highest regen rates.</summary>
    Sleep = 0,

    /// <summary>Active recovery: sitting/meditating. Moderate regen rates.</summary>
    Rest = 1,

    /// <summary>Awake and active (or in combat). Minimal regen rates.</summary>
    Stand = 2,
}
