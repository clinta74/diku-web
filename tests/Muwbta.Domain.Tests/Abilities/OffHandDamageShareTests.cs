using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Items;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// A second weapon is grown into rather than granted whole (PLAN.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// Dual wielding used to arrive complete: the day a Temper learned the passive at level 3, their off
/// hand hit for everything the main hand did, and Ambidextrous later doubled the rate on top. The
/// doubling at the top is intended and is untouched — a Temper at 40 and beyond is exactly where it
/// was. What this ramps is everything before it.
/// </para>
/// <para>
/// <b>Level rather than Agility, and that was the second answer.</b> Agility caps at
/// <see cref="AttributeSet.MaxValue"/>, which a Temper reaches at level 6 and a Warden at 11 — so an
/// Agility ramp would have finished before the passive had been held for long, which is the opposite
/// of spreading it out.
/// </para>
/// </remarks>
public sealed class OffHandDamageShareTests
{
    private const int Mastery = AbilityProgression.OffHandMasteryLevel;

    // -----------------------------------------------------------------------
    // Who gets one at all
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Hallow)]
    public void A_path_that_never_learns_to_dual_wield_has_no_share(CharacterPath path)
    {
        // They may still hold a blade there. It simply never swings.
        Assert.Equal(0m, AbilityProgression.OffHandDamageShare(path, 1));
        Assert.Equal(0m, AbilityProgression.OffHandDamageShare(path, 50));
    }

    [Theory]
    [InlineData(CharacterPath.Warden, 4)]
    [InlineData(CharacterPath.Temper, 2)]
    public void Below_the_unlock_there_is_no_share(CharacterPath path, int level)
    {
        Assert.Equal(0m, AbilityProgression.OffHandDamageShare(path, level));
    }

    // -----------------------------------------------------------------------
    // The endpoints
    // -----------------------------------------------------------------------

    /// <summary>
    /// Half at the unlock, so the passive is worth having on the day it is granted rather than
    /// being a promise about level 40.
    /// </summary>
    [Theory]
    [InlineData(CharacterPath.Warden, 5, 0.40)]
    [InlineData(CharacterPath.Temper, 3, 0.50)]
    public void It_starts_at_half_on_the_level_it_unlocks(CharacterPath path, int level, double expected)
    {
        Assert.Equal((decimal)expected, AbilityProgression.OffHandDamageShare(path, level));
    }

    /// <summary>
    /// The Path's identity, at the top: a Temper ends at all of it because two blades is what the
    /// Path is, a Warden at four fifths because the hand is also holding a shield.
    /// </summary>
    [Theory]
    [InlineData(CharacterPath.Warden, 0.80)]
    [InlineData(CharacterPath.Temper, 1.00)]
    public void It_reaches_the_paths_full_share_at_mastery_and_stays_there(CharacterPath path, double expected)
    {
        Assert.Equal((decimal)expected, AbilityProgression.OffHandDamageShare(path, Mastery));
        Assert.Equal((decimal)expected, AbilityProgression.OffHandDamageShare(path, 50));
    }

    // -----------------------------------------------------------------------
    // The line between them
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(CharacterPath.Warden)]
    [InlineData(CharacterPath.Temper)]
    public void It_only_ever_grows(CharacterPath path)
    {
        var previous = -1m;

        for (var level = 1; level <= 50; level++)
        {
            var share = AbilityProgression.OffHandDamageShare(path, level);
            Assert.True(share >= previous, $"level {level} went backwards: {share} after {previous}");
            previous = share;
        }
    }

    /// <summary>
    /// A straight line, not a curve: every level of the climb is worth the same.
    /// </summary>
    /// <remarks>
    /// Asserted as equal steps rather than as one sampled midpoint, because the spans are odd — 35
    /// levels for a Warden, 37 for a Temper — so there is no level that sits exactly halfway and a
    /// midpoint assertion would be testing integer division rather than the ramp.
    /// </remarks>
    [Theory]
    [InlineData(CharacterPath.Warden, 5)]
    [InlineData(CharacterPath.Temper, 3)]
    public void It_climbs_in_equal_steps(CharacterPath path, int unlock)
    {
        var first = AbilityProgression.OffHandDamageShare(path, unlock + 1)
            - AbilityProgression.OffHandDamageShare(path, unlock);

        Assert.True(first > 0m);

        for (var level = unlock + 1; level < Mastery; level++)
        {
            var step = AbilityProgression.OffHandDamageShare(path, level + 1)
                - AbilityProgression.OffHandDamageShare(path, level);

            Assert.Equal(Math.Round(first, 6), Math.Round(step, 6));
        }
    }

    /// <summary>Half the climb spends half the levels, which is what "straight line" buys a player.</summary>
    [Theory]
    [InlineData(CharacterPath.Warden, 5, 0.80)]
    [InlineData(CharacterPath.Temper, 3, 1.00)]
    public void Half_the_remaining_climb_is_done_halfway_through_it(CharacterPath path, int unlock, double full)
    {
        var span = Mastery - unlock;
        var halfway = AbilityProgression.OffHandDamageShare(path, unlock + (span / 2));

        // Between the two levels that straddle the true midpoint of an odd span.
        var low = (decimal)full * 0.73m;
        var high = (decimal)full * 0.77m;

        Assert.InRange(halfway, low, high);
    }

    /// <summary>
    /// The Temper is ahead of the Warden at every level where both have the passive, which is what
    /// makes it the Path that fights with two weapons.
    /// </summary>
    [Fact]
    public void A_blade_is_always_ahead_of_a_warden()
    {
        for (var level = 5; level <= 50; level++)
        {
            Assert.True(
                AbilityProgression.OffHandDamageShare(CharacterPath.Temper, level)
                    > AbilityProgression.OffHandDamageShare(CharacterPath.Warden, level),
                $"at level {level} the Warden is not behind the Temper");
        }
    }

    // -----------------------------------------------------------------------
    // What it does to a swing
    // -----------------------------------------------------------------------

    private static ItemInstance Weapon(ItemSlot slot, int min, int max) => new()
    {
        TemplateKey = "temper",
        EquippedSlot = slot,
        ResolvedStats = new Dictionary<string, object> { { "damageMin", min }, { "damageMax", max } },
    };

    /// <summary>
    /// <b>The whole swing, dice and flat together.</b> <c>BaseDamage</c> is the Might modifier and is
    /// added per swing, so scaling only the dice would leave the ramp barely biting at the levels it
    /// is steepest — a Warden's +4 Might dwarfs two fifths of a starter weapon's dice.
    /// </summary>
    [Fact]
    public void A_half_share_halves_the_dice_and_the_flat_damage_together()
    {
        var stats = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 10,
            mightModifier: 4,
            equipped: new[] { Weapon(ItemSlot.OffHand, 6, 12) },
            hand: ItemSlot.OffHand,
            offHandShare: 0.5m);

        Assert.Equal(3, stats.MinDamage);
        Assert.Equal(6, stats.MaxDamage);
        Assert.Equal(2, stats.BaseDamage);
    }

    /// <summary>Accuracy is not what the ramp limits — a softer hit, not a wilder one.</summary>
    [Fact]
    public void The_share_leaves_attack_rating_alone()
    {
        var equipped = new[] { Weapon(ItemSlot.OffHand, 6, 12) };

        var whole = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 10, mightModifier: 4, equipped: equipped, hand: ItemSlot.OffHand, offHandShare: 1m);

        var partial = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 10, mightModifier: 4, equipped: equipped, hand: ItemSlot.OffHand, offHandShare: 0.4m);

        Assert.Equal(whole.AttackRating, partial.AttackRating);
        Assert.True(partial.MaxDamage < whole.MaxDamage);
    }

    /// <summary>A swing that lands does something, however early the character is.</summary>
    [Fact]
    public void A_small_share_of_a_small_weapon_still_lands_for_one()
    {
        var stats = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 3,
            mightModifier: 0,
            equipped: new[] { Weapon(ItemSlot.OffHand, 2, 3) },
            hand: ItemSlot.OffHand,
            offHandShare: 0.1m);

        Assert.Equal(1, stats.MinDamage);
        Assert.True(stats.MaxDamage >= stats.MinDamage);
    }

    /// <summary>The main hand is never scaled — the share is an off-hand concept.</summary>
    [Fact]
    public void The_main_hand_swings_whole_whatever_share_is_passed()
    {
        var stats = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 10,
            mightModifier: 4,
            equipped: new[] { Weapon(ItemSlot.MainHand, 6, 12) },
            hand: ItemSlot.MainHand,
            offHandShare: 0.1m);

        Assert.Equal(6, stats.MinDamage);
        Assert.Equal(12, stats.MaxDamage);
        Assert.Equal(4, stats.BaseDamage);
    }
}
