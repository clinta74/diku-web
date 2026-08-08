namespace DikuWeb.Domain.Inhabitants;

/// <summary>
/// One of a mob's attacks: what it does, how often, and how hard relative to its own damage.
/// A template carries a list of these and each entry runs its own independent timer, so a wolf
/// can bite every second and rake every three.
/// </summary>
/// <remarks>
/// A settable class with a parameterless constructor rather than a positional record, because
/// this is persisted as jsonb through Npgsql's dynamic JSON mapping, which round-trips plain
/// property bags most predictably.
/// </remarks>
public sealed class MobAttack
{
    /// <summary>Base-form verb for narration: "bite" reads "A wolf bites you for 6 damage."</summary>
    public string Verb { get; set; } = Combat.AttackTiming.DefaultVerb;

    /// <summary>Pulses between swings of this attack. Floor of 4 (1 second).</summary>
    public int DelayPulses { get; set; } = Combat.AttackTiming.DefaultDelayPulses;

    /// <summary>
    /// Scales this attack against the mob's resolved damage, so one attack can hit harder than
    /// another. Null means 1.0. Deliberately a multiplier rather than its own dice: damage stays
    /// derived from <see cref="Mob.ResolvedStats"/>, so zone and world multipliers still apply
    /// and the same template remains dangerous in proportion to where it spawned (PLAN.md §4.4).
    /// </summary>
    public decimal? DamageMultiplier { get; set; }
}
