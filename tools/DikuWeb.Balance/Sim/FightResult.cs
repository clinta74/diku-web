namespace DikuWeb.Balance.Sim;

/// <summary>What one simulated fight cost and where the damage came from.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Seconds">Wall-clock seconds of fight, at four pulses to the second.</param>
/// <param name="WeaponDamage">Dealt by main- and off-hand swings.</param>
/// <param name="AbilityDamage">Dealt by an ability at the moment it landed.</param>
/// <param name="WoundDamage">Dealt by wounds an ability left behind, ticking.</param>
/// <param name="DamageTaken">Dealt to the player, by anything.</param>
/// <param name="HealthRemaining">The player's health at the end, out of <paramref name="HealthMax"/>.</param>
/// <param name="Swings">Weapon attacks attempted, hit or miss.</param>
/// <param name="Casts">Abilities successfully cast.</param>
/// <param name="StarvedPulses">
/// Pulses on which an ability was ready and unaffordable. The measure of whether a Path is
/// limited by its resource bar or by its cooldowns, which are different balance problems with
/// different fixes.
/// </param>
public sealed record FightResult(
    FightOutcome Outcome,
    double Seconds,
    int WeaponDamage,
    int AbilityDamage,
    int WoundDamage,
    int DamageTaken,
    int HealthRemaining,
    int HealthMax,
    int Swings,
    int Casts,
    int StarvedPulses)
{
    /// <summary>Everything the player dealt.</summary>
    public int TotalDamage => WeaponDamage + AbilityDamage + WoundDamage;

    /// <summary>Everything that came from an ability, landed or ticking.</summary>
    public int AbilityTotal => AbilityDamage + WoundDamage;

    /// <summary>The share of the player's output that came from abilities, 0-1.</summary>
    public double AbilityShare => TotalDamage == 0 ? 0 : (double)AbilityTotal / TotalDamage;

    /// <summary>The share of the player's health pool left standing, 0-1.</summary>
    public double HealthShare => HealthMax == 0 ? 0 : (double)HealthRemaining / HealthMax;

    public double DamagePerSecond => Seconds <= 0 ? 0 : TotalDamage / Seconds;
}

public enum FightOutcome
{
    /// <summary>The mob died.</summary>
    Won,

    /// <summary>The player died.</summary>
    Lost,

    /// <summary>
    /// Neither died before the cap.
    /// </summary>
    /// <remarks>
    /// A finding in its own right rather than a failed run: a fight nobody can end is what a
    /// mitigation cap plus a 1-damage floor plus enough regeneration produces, and it is invisible
    /// in any measure that only reports the fights that finished.
    /// </remarks>
    Stalemate,
}
