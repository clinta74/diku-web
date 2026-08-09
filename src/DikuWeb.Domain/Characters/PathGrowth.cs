namespace DikuWeb.Domain.Characters;

/// <summary>
/// Defines base stat growth for each Path (PLAN.md §4.5). When a character levels up,
/// they gain these attribute increases automatically, in addition to any manual point-buy.
/// This is the framework-default growth; point-buy lets the player customize further.
/// </summary>
public sealed record StatGrowth(int Might, int Agility, int Vitality, int Insight, int Resolve)
{
    public void ApplyTo(ref AttributeSet attrs)
    {
        attrs = new AttributeSet
        {
            Might = Math.Min(AttributeSet.MaxValue, attrs.Might + Might),
            Agility = Math.Min(AttributeSet.MaxValue, attrs.Agility + Agility),
            Vitality = Math.Min(AttributeSet.MaxValue, attrs.Vitality + Vitality),
            Insight = Math.Min(AttributeSet.MaxValue, attrs.Insight + Insight),
            Resolve = Math.Min(AttributeSet.MaxValue, attrs.Resolve + Resolve),
        };
    }
}

/// <summary>
/// Base stat growth per level for each Path. These are suggested progressions
/// that favor a Path's theme; players can override with point-buy in Phase 5.
/// </summary>
public static class PathGrowth
{
    /// <summary>Warden: armored frontline — favors Might and Vitality.</summary>
    public static StatGrowth WardenGrowth => new(Might: 2, Agility: 1, Vitality: 2, Insight: 0, Resolve: 1);

    /// <summary>Adept: focus-caster — favors Insight and Resolve (willpower).</summary>
    public static StatGrowth AdeptGrowth => new(Might: 0, Agility: 1, Vitality: 1, Insight: 2, Resolve: 2);

    /// <summary>Shade: stealth and burst — favors Agility and Insight.</summary>
    public static StatGrowth ShadeGrowth => new(Might: 1, Agility: 2, Vitality: 0, Insight: 2, Resolve: 1);

    /// <summary>Hallow: support and control — balanced Insight/Resolve with Vitality.</summary>
    public static StatGrowth HallowGrowth => new(Might: 0, Agility: 1, Vitality: 1, Insight: 2, Resolve: 2);

    public static StatGrowth For(CharacterPath path) => path switch
    {
        CharacterPath.Warden => WardenGrowth,
        CharacterPath.Adept => AdeptGrowth,
        CharacterPath.Shade => ShadeGrowth,
        CharacterPath.Hallow => HallowGrowth,
        _ => new(Might: 1, Agility: 1, Vitality: 1, Insight: 1, Resolve: 1),
    };
}
