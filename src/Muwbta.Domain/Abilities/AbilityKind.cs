namespace Muwbta.Domain.Abilities;

/// <summary>
/// Whether an ability is something you cast or something you do (PLAN.md §4.7).
/// </summary>
public enum AbilityKind
{
    /// <summary>
    /// A spell. Worked out of Focus, and the thing <c>cast</c> is for.
    /// </summary>
    Spell = 0,

    /// <summary>
    /// A skill. Paid for out of the body, and used by naming it: <c>kick rat</c>.
    /// </summary>
    Skill = 1,
}

/// <summary>
/// Reads the spell/skill split off what an ability already declares.
/// </summary>
/// <remarks>
/// <b>Derived, not authored.</b> The catalogue already draws this line exactly: the two caster
/// Paths pay Focus for all eighteen of their abilities, and the two martial Paths pay Stamina —
/// or, once, Health. Adding a field for it would be adding a second source of truth for something
/// the cost type already says, and the two would eventually disagree.
///
/// It matters because <c>cast kick</c> reads wrong, and reads wrong for a real reason: a boot to
/// the knee is not a spell. Splitting the vocabulary means the verb a player types matches what
/// their character is doing.
/// </remarks>
public static class AbilityKinds
{
    /// <summary>Which of the two this ability is.</summary>
    public static AbilityKind Of(CostType cost) =>
        cost == CostType.Focus ? AbilityKind.Spell : AbilityKind.Skill;

    /// <inheritdoc cref="Of(CostType)"/>
    public static AbilityKind Of(Ability ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        return Of(ability.CostType);
    }

    /// <summary>The word for it, for prose that has to name the category.</summary>
    public static string NameOf(AbilityKind kind) =>
        kind == AbilityKind.Spell ? "spell" : "skill";

    /// <summary>What a player actually types to use this — <c>cast ember</c>, or <c>kick</c>.</summary>
    /// <remarks>
    /// Here rather than at each place that needs it, because two of them now exist: the ability
    /// roster the client draws, and the level-up message that names a new ability as it is earned.
    /// Those two disagreeing would teach a verb that <c>cast</c> then refuses.
    /// </remarks>
    public static string VerbFor(Ability ability)
    {
        ArgumentNullException.ThrowIfNull(ability);

        var name = ability.Name.ToLowerInvariant();
        return Of(ability) == AbilityKind.Spell ? $"cast {name}" : name;
    }
}
