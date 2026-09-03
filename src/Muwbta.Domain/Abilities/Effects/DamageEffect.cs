using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Randomness;

namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// Physical or magical damage effect. Applies to a single target (character or mob).
/// Scaling factor and variance come from ability parameters; the base comes from the caster.
/// </summary>
public sealed class DamageEffect : IAbilityEffect
{
    /// <summary>What <c>scalingFactor</c> scales at level 1.</summary>
    /// <remarks>
    /// Every authored factor was written against this number, so it is unchanged and the whole
    /// content file still means exactly what it meant at the level each ability unlocks.
    /// </remarks>
    public const int UnscaledBaseDamage = 10;

    /// <summary>
    /// How much of the base each level past the first adds, as <see cref="PerLevelNumerator"/>
    /// over <see cref="PerLevelDenominator"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The base used to be a constant, and that was the single largest balance defect in the
    /// game.</b> Nothing here read the caster, so every ability dealt the same damage at level 50
    /// as at the level it was learned — while a typical target's health went from 22 to 747. One
    /// cast was half a level 1 mob and a twenty-fifth of a level 50 one, measured. The wound
    /// effects beside it held 34–48% of a target across the whole game, because a wound authors an
    /// absolute <c>tickDamage</c> its author could see against a health bar and this authored a
    /// ratio over a constant no builder ever laid eyes on.
    /// </para>
    /// <para>
    /// <b>Seven tenths, and the target it was chosen against is the wound line.</b> It puts the
    /// base at 44 by level 50, which lands a capstone at 15–20% of a level-appropriate target
    /// rather than 4% — the same band the wounds already occupy. It is deliberately *not* chosen to
    /// preserve a single cast's share of a target, which would need seven times as much: a level 50
    /// character has eighteen abilities where a level 1 has one, and holding each of them at half a
    /// health bar would make the whole kit absurd.
    /// </para>
    /// <para>
    /// <b>Level, not the casting attribute.</b> The obvious axis was Insight or Might, and it does
    /// not work for the reason <c>AbilityProgression.OffHandMasteryLevel</c> already records about
    /// Agility: attributes start at 10, cap at <c>AttributeSet.MaxValue</c>, and grow two a level
    /// for a Path's primary — so an Adept's Insight and a Temper's Might are both capped by level
    /// six. A modifier frozen for the last 88% of the game is not a scaling axis.
    /// </para>
    /// <para>
    /// <b>Linear, and integer.</b> A curve that compounds would have to be tuned against mob health,
    /// which is superlinear in level only because the world <c>strength</c> dials happen to step the
    /// way they do — chasing it would bake today's five realms into the damage formula.
    /// </para>
    /// </remarks>
    public const int PerLevelNumerator = 4;

    /// <inheritdoc cref="PerLevelNumerator"/>
    public const int PerLevelDenominator = 10;

    /// <summary>The share either side of the middle a blow can land, as a fraction.</summary>
    public const double VarianceShare = 0.2;

    public string EffectKey => "damage.physical";

    public bool IsHarmful => true;

    /// <summary>
    /// What <c>scalingFactor</c> scales, for a caster of this level.
    /// </summary>
    /// <remarks>
    /// Floored at level 1 rather than trusted: a caster whose level never got set reads as zero,
    /// and a negative base would turn a damage ability into a heal.
    /// </remarks>
    public static int BaseAtLevel(int casterLevel) =>
        UnscaledBaseDamage +
        ((Math.Max(1, casterLevel) - 1) * PerLevelNumerator / PerLevelDenominator);

    /// <summary>
    /// The damage this lands for, before variance - which is what the dials add up to.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="Describe"/> rather than repeated there, so the number a player is
    /// shown is arrived at by the code that deals it.
    /// </remarks>
    public static int Middle(Dictionary<string, string> parameters, int casterLevel)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Get scaling factor from parameters (default 1.0 = no scaling)
        var scalingFactor = parameters.TryGetValue("scalingFactor", out var scaleStr) && double.TryParse(scaleStr, out var scale)
            ? scale
            : 1.0;

        // Get minimum damage (default 1)
        var minDamage = parameters.TryGetValue("minDamage", out var minStr) && int.TryParse(minStr, out var min)
            ? min
            : 1;

        var scaledDamage = (int)(BaseAtLevel(casterLevel) * scalingFactor);

        return Math.Max(minDamage, scaledDamage);
    }

    /// <summary>How far either side of <see cref="Middle"/> a roll can fall.</summary>
    public static int Variance(int middle) => (int)(middle * VarianceShare);

    /// <summary>
    /// The level whoever is casting fights at, or 1 for anything that is neither a character nor
    /// a mob.
    /// </summary>
    /// <remarks>
    /// A mob reads its <em>effective</em> level, not its authored one — the same choice
    /// <c>DamageCalculator.FightingLevel</c> makes, and for the same reason: a mob scaled up by its
    /// zone hits harder with its dice, and an ability rider that ignored the scaling would be the
    /// one part of that mob the zone dial never reached.
    /// </remarks>
    public static int LevelOf(object? caster) => caster switch
    {
        Character character => character.Level,
        Mob mob => mob.EffectiveLevel > 0 ? mob.EffectiveLevel : mob.Level,
        _ => 1,
    };

    public string Describe(Dictionary<string, string> parameters, TargetingType targeting, int casterLevel)
    {
        var middle = Middle(parameters, casterLevel);
        var spread = Variance(middle);

        return $"deals {AbilityAudience.Amount(middle - spread, middle + spread)} damage to " +
               AbilityAudience.Whom(targeting, IsHarmful);
    }

    public void Apply(object caster, object? target, Dictionary<string, string> parameters, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        var finalDamage = Middle(parameters, LevelOf(caster));

        // Apply variance (±20%)
        var variance = Variance(finalDamage);
        var variance_amount = random.Next(-variance, variance + 1);
        var damage = finalDamage + variance_amount;

        // Apply damage to target
        if (target is Character targetChar)
        {
            targetChar.Vitals.Health = Math.Max(0, targetChar.Vitals.Health - damage);
        }
        else if (target is Mob targetMob)
        {
            targetMob.Vitals.Health = Math.Max(0, targetMob.Vitals.Health - damage);
        }
    }
}
