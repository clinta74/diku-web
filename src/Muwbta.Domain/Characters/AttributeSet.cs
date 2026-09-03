namespace Muwbta.Domain.Characters;

/// <summary>
/// PLAN.md §4.5: five attributes replacing the classic six. Range 1-20.
/// Persisted as jsonb, so adding a sixth attribute is not a migration.
/// </summary>
public sealed class AttributeSet
{
    public const int MinValue = 1;
    public const int MaxValue = 20;

    public int Might { get; init; }
    public int Agility { get; init; }
    public int Vitality { get; init; }
    public int Insight { get; init; }
    public int Resolve { get; init; }

    /// <summary>Every attribute at 10, i.e. a modifier of zero across the board.</summary>
    public static AttributeSet Baseline { get; } = new()
    {
        Might = 10,
        Agility = 10,
        Vitality = 10,
        Insight = 10,
        Resolve = 10,
    };

    /// <summary>
    /// PLAN.md §4.5: modifier = (value - 10) / 2, rounded down.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Math.Floor(double)"/> rather than integer division on purpose.
    /// C# integer division truncates toward zero, so (7 - 10) / 2 would give -1 where
    /// rounding down requires -2. Every combat roll reads this, so the sign handling
    /// has to be right for below-average attributes, not just above-average ones.
    /// </remarks>
    public static int ModifierFor(int value) => (int)Math.Floor((value - 10) / 2.0);

    public int MightModifier => ModifierFor(Might);
    public int AgilityModifier => ModifierFor(Agility);
    public int VitalityModifier => ModifierFor(Vitality);
    public int InsightModifier => ModifierFor(Insight);
    public int ResolveModifier => ModifierFor(Resolve);
}
