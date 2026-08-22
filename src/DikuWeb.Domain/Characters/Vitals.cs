namespace DikuWeb.Domain.Characters;

/// <summary>
/// PLAN.md §4.5: Health (damage pool, zero is death), Focus (powers abilities),
/// Stamina (movement and heavy attacks). Persisted as jsonb.
/// </summary>
public sealed class Vitals
{
    public int Health { get; set; }
    public int HealthMax { get; set; }
    public int Focus { get; set; }
    public int FocusMax { get; set; }
    public int Stamina { get; set; }
    public int StaminaMax { get; set; }

    /// <summary>
    /// How empty the belly is, from <c>0</c> (fed) to <see cref="Needs.Worst"/> (starving).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Emptiness rather than fullness, and the direction is load-bearing.</b> This class is
    /// persisted as a single jsonb column (<c>CharacterConfiguration</c>), so a new field costs no
    /// schema change — but every row written before today has no key for it and deserialises to
    /// <c>0</c>. Counting <em>emptiness</em> makes that silence mean "well fed"; counting fullness
    /// would have logged every existing character in starving and required a backfill to say
    /// something the default could have said for free.
    /// </para>
    /// <para>
    /// Nothing reads a mob's. Mobs get a <see cref="Vitals"/> from <c>MobSpawner</c> and never eat.
    /// </para>
    /// </remarks>
    public int Hunger { get; set; }

    /// <inheritdoc cref="Hunger"/>
    public int Thirst { get; set; }

    public bool IsDead => Health <= 0;

    public static Vitals StartingFor(CharacterPath path) => path switch
    {
        CharacterPath.Warden => Full(health: 60, focus: 20, stamina: 100),
        CharacterPath.Adept => Full(health: 36, focus: 50, stamina: 80),
        CharacterPath.Temper => Full(health: 44, focus: 30, stamina: 110),
        CharacterPath.Hallow => Full(health: 40, focus: 45, stamina: 85),
        _ => Full(health: 40, focus: 30, stamina: 100),
    };

    private static Vitals Full(int health, int focus, int stamina) => new()
    {
        Health = health,
        HealthMax = health,
        Focus = focus,
        FocusMax = focus,
        Stamina = stamina,
        StaminaMax = stamina,
    };
}
