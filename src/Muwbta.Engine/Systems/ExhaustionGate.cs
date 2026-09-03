using Muwbta.Domain.Characters;
using Muwbta.Domain.Items;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Systems;

/// <summary>
/// Why a character who has run themselves empty cannot act, or null when they have enough left.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what makes <c>Weight</c> mean something.</b> Item weight has had a column, a change
/// record, an applier, a bundle field and an exporter since the beginning, and exactly one reader —
/// the builder's <c>examine</c> block, which printed it. Nothing summed it and nothing gated on it.
/// The recovery a character owes at zero stamina is the first rule that reads what they are
/// carrying.
/// </para>
/// <para>
/// <b>It refuses; it does not sit you down.</b> <see cref="RestGate"/> makes the argument and it
/// holds here too: a rule that changes your posture for you is a rule that costs you regen without
/// saying so. There is a second reason as well — <c>Character.RestState</c> is not carried by
/// <c>CharacterSnapshot</c>, so a forced posture would quietly fail to survive a restart. Deriving
/// the refusal from stamina, which <em>is</em> persisted, means there is no new state to keep and
/// nothing to get out of step.
/// </para>
/// <para>
/// <b>It never applies in combat, and that is deliberate.</b> An exhausted character can always
/// swing and can always run. The alternative is a player who hits zero beside something aggressive
/// and is held still until it kills them, which is not a cost — it is a death with no move left to
/// make. Everywhere else it bites: movement, abilities, travel, following.
/// </para>
/// </remarks>
public static class ExhaustionGate
{
    /// <summary>Grams of carried weight per point of stamina owed.</summary>
    /// <remarks>
    /// <b>Taken from the content rather than invented.</b> Equippable items run to a median of
    /// 2.6 kg and a heavy eight-slot kit comes to about 49 kg, so five kilograms a point puts a
    /// lightly-equipped character at one or two and a fully-loaded one at the ceiling. Anything
    /// smaller made a starting character owe as much as an endgame one; anything larger meant only
    /// the very heaviest kit ever noticed.
    /// </remarks>
    public const int GramsPerStaminaOwed = 5000;

    /// <summary>The least any character owes, however light.</summary>
    /// <remarks>
    /// One rather than zero, so the rule exists for everybody. A naked character still has to catch
    /// their breath; what the weight decides is how long for.
    /// </remarks>
    public const int MinimumOwed = 1;

    /// <summary>The most any character owes, however loaded.</summary>
    public const int MaximumOwed = 10;

    /// <summary>
    /// How much stamina this much carried weight obliges a character to recover before acting again.
    /// </summary>
    public static int StaminaOwed(int carriedGrams) => Math.Clamp(
        (int)Math.Round(Math.Max(0, carriedGrams) / (double)GramsPerStaminaOwed, MidpointRounding.AwayFromZero),
        MinimumOwed,
        MaximumOwed);

    /// <summary>
    /// Everything a character is carrying, in grams.
    /// </summary>
    /// <remarks>
    /// <b>Worn gear counts.</b> The weight of a harness is on your back whether or not it is in your
    /// pack, and <c>WorldState.InventoryOf</c> returns owned items rather than unequipped ones, so
    /// this falls out of using it as-is.
    ///
    /// <b>Summed on demand, never on a tick.</b> <c>InventoryOf</c> scans every item in the world
    /// and allocates a fresh list, which is nothing at a command and would be the first thing to
    /// show up in a profile if it ran every pulse for every player.
    /// </remarks>
    public static int CarriedGrams(
        IEnumerable<ItemInstance> inventory,
        Func<string, int> weightOfTemplate)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(weightOfTemplate);

        var total = 0;

        foreach (var item in inventory)
        {
            total += Math.Max(0, weightOfTemplate(item.TemplateKey));
        }

        return total;
    }

    /// <summary>Why <paramref name="character"/> cannot act, or null if they can.</summary>
    /// <param name="character">The one acting.</param>
    /// <param name="carriedGrams">What they are carrying, from <see cref="CarriedGrams"/>.</param>
    /// <remarks>
    /// Names the way out, the way <see cref="RestGate"/> and <c>flee</c> both do: say the state,
    /// then the verb that ends it.
    /// </remarks>
    public static string? Refuse(Character character, int carriedGrams)
    {
        ArgumentNullException.ThrowIfNull(character);

        // Fighting is always allowed. See the class remarks: the alternative is a death with no
        // move left to make.
        if (character.CombatState == Domain.Combat.CombatState.Fighting)
        {
            return null;
        }

        var owed = StaminaOwed(carriedGrams);

        if (character.Vitals.Stamina >= owed)
        {
            return null;
        }

        return carriedGrams >= GramsPerStaminaOwed * 2
            ? "You are spent, and what you are carrying is not helping. Try 'rest' - or put something down."
            : "You are spent. Try 'rest' until you have your breath back.";
    }

    /// <summary>
    /// The same refusal, weighing what the character is actually carrying.
    /// </summary>
    /// <remarks>
    /// The overload every call site uses, so none of them has to remember to sum the pack. Templates
    /// may be absent in a test that never loaded any; an unknown template weighs nothing, which
    /// makes the gate lenient rather than making it throw.
    /// </remarks>
    public static string? Refuse(Character character, WorldState world, ItemTemplateCache? templates)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(world);

        // Cheap exit before the inventory scan. Anyone with stamina to spare is not exhausted
        // however much they are carrying, and that is nearly every call - InventoryOf walks every
        // item in the world, so the common path should not pay for it.
        if (character.Vitals.Stamina >= MaximumOwed ||
            character.CombatState == Domain.Combat.CombatState.Fighting)
        {
            return null;
        }

        var carried = CarriedGrams(
            world.InventoryOf(character.Id),
            key => templates?.Get(key)?.Weight ?? 0);

        return Refuse(character, carried);
    }
}
