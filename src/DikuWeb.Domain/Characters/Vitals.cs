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
