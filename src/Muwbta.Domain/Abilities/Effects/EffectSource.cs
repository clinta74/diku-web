using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;

namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// Who an <see cref="ActiveEffect"/> came from, as the id string the effect carries.
/// </summary>
/// <remarks>
/// Five executors spelled this out for themselves before it was extracted, which is four chances
/// for a prefix to drift. It is load-bearing rather than cosmetic: a damage-over-time tick credits
/// its threat to <c>SourceEntityId</c>, so a mismatched prefix is a wound that hits somebody's hate
/// list under a name nothing can resolve — and a malformed one has already taken the game loop down
/// once (HISTORY.md, 5.2f).
/// </remarks>
public static class EffectSource
{
    /// <summary>The id string for whoever cast this, or "unknown" for anything else.</summary>
    public static string Of(object? caster) => caster switch
    {
        Character c => $"c_{c.Id:N}",
        Mob m => $"m_{m.Id:N}",
        _ => "unknown",
    };
}
