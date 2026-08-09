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

    /// <summary>
    /// An effect this attack applies on a landed hit, keyed as <c>EffectRegistry</c> knows it -
    /// <c>control.stun</c>, <c>damage.overtime</c>, <c>debuff.weaken</c>. Null for a plain attack,
    /// which is nearly all of them.
    /// </summary>
    /// <remarks>
    /// This is how a mob gets a vocabulary, and it is deliberately <em>not</em> a spellbook
    /// (PLAN.md §12). A mob has no cast bar, no focus pool, and no ability list to work through -
    /// it has attacks, and an attack can do something other than damage. The asymmetry it closes
    /// was live: a Warden's Shield Bash takes a boss off its feet for three seconds, and until
    /// this existed the boss had no answer of any kind.
    ///
    /// Riding on the swing rather than on its own clock is most of why this is cheap. The attack
    /// already has a timer, already rolls to hit, and already resolves damage; the effect lands
    /// where that damage lands, so it inherits the miss chance, the parry, and the death check
    /// for free. It also reads correctly: a stun you dodged should not stun you.
    /// </remarks>
    public string? EffectKey { get; set; }

    /// <summary>
    /// Parameters for <see cref="EffectKey"/>, exactly as an ability's <c>EffectParams</c> carries
    /// them - <c>durationPulses</c>, <c>tickDamage</c>, and so on.
    /// </summary>
    /// <remarks>
    /// Strings, not numbers, because the executors parse strings and this bag round-trips through
    /// jsonb where a number would come back as a <c>JsonElement</c> and quietly stop matching.
    /// Every executor skips a parameter it does not recognise, so a misspelled key is an effect
    /// that runs with defaults rather than one that throws - which is why the builder offers the
    /// fields rather than a free-text bag.
    /// </remarks>
    public Dictionary<string, string>? EffectParams { get; set; }
}
