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

    /// <summary>
    /// Temper: fast, single-target damage — favors Might and Agility.
    /// </summary>
    /// <remarks>
    /// <b>Insight was chosen when this Path was Shade</b>, and it was reading the name rather than
    /// the kit: nothing a Temper does scales off Insight, and a striker is Might and Agility. The
    /// point moved from Insight to Might and the per-level total is unchanged at six, so this
    /// redistributes rather than buffs — the same discipline the epic weapon ranking used.
    ///
    /// <b>Existing characters keep the curve they levelled on.</b> Growth is applied at each
    /// level-up and stored, not derived from level, and a character's attributes are that plus
    /// whatever point-buy they spent — so there is no way to recompute the new curve without
    /// destroying choices a player made. New levels use the new numbers; old levels stay bought.
    /// </remarks>
    public static StatGrowth TemperGrowth => new(Might: 2, Agility: 2, Vitality: 0, Insight: 1, Resolve: 1);

    /// <summary>Hallow: support and control — balanced Insight/Resolve with Vitality.</summary>
    public static StatGrowth HallowGrowth => new(Might: 0, Agility: 1, Vitality: 1, Insight: 2, Resolve: 2);

    public static StatGrowth For(CharacterPath path) => path switch
    {
        CharacterPath.Warden => WardenGrowth,
        CharacterPath.Adept => AdeptGrowth,
        CharacterPath.Temper => TemperGrowth,
        CharacterPath.Hallow => HallowGrowth,
        _ => new(Might: 1, Agility: 1, Vitality: 1, Insight: 1, Resolve: 1),
    };
}
