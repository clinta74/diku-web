namespace DikuWeb.Domain.Combat;

/// <summary>
/// Result of an attack roll: hit or miss, with damage if hit.
/// </summary>
public sealed class AttackResult
{
    /// <summary>The d20 roll (1-20).</summary>
    public int AttackRoll { get; init; }

    /// <summary>Attacker's attack rating (level/2 + might modifier + bonuses).</summary>
    public int AttackRating { get; init; }

    /// <summary>Defender's defense value (10 + agility modifier + armor bonuses).</summary>
    public int DefenseValue { get; init; }

    /// <summary>Whether the attack connected.</summary>
    public bool IsHit => AttackRoll + AttackRating >= DefenseValue;

    /// <summary>Whether this is a critical hit (natural 20 or beats defense by 10+).</summary>
    public bool IsCritical => AttackRoll == 20 || (IsHit && AttackRoll + AttackRating >= DefenseValue + 10);

    /// <summary>
    /// Damage dealt if hit, or 0 if miss.
    /// For crits, damage dice are rolled twice and summed, then modifier added once.
    /// </summary>
    public int DamageDealt { get; init; }
}
