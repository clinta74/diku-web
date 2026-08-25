using DikuWeb.Domain.Combat;

namespace DikuWeb.Domain.Tests.Combat;

/// <summary>
/// An item's numbers, in words a player reads.
/// </summary>
/// <remarks>
/// The <c>stats</c> screen printed the stat bag the way a builder reads one — <c>bonus=4,
/// damageMax=13, damageMin=7</c>, alphabetical, so the maximum came before the minimum and the one
/// term that is accuracy rather than damage was named only by its authored key.
/// </remarks>
public sealed class ItemStatLineTests
{
    private static Dictionary<string, object> Bag(params (string Key, object Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    // -----------------------------------------------------------------------
    // Reading one item
    // -----------------------------------------------------------------------

    [Fact]
    public void A_weapon_reads_as_damage_then_accuracy()
    {
        // The oathmaul from the playtest transcript, which printed as
        // "bonus=4, damageMax=13, damageMin=7".
        var line = ItemStatLine.For(Bag(("bonus", 4), ("damageMax", 13), ("damageMin", 7)));

        Assert.Equal("Damage 7-13, +4 to hit", line);
    }

    /// <summary>
    /// <c>bonus</c> is attack rating, and the wording has to say so. A player reading "+4" beside
    /// a damage range will reasonably assume it is more damage; it is not, and
    /// <see cref="EquipmentResolver"/> is where that is settled.
    /// </summary>
    [Fact]
    public void The_accuracy_term_says_what_it_affects()
    {
        Assert.Contains("to hit", ItemStatLine.For(Bag(("bonus", 5)))!, StringComparison.Ordinal);
        Assert.DoesNotContain("bonus", ItemStatLine.For(Bag(("bonus", 5)))!, StringComparison.Ordinal);
    }

    [Fact]
    public void Armour_reads_as_armour_and_defence_as_defence()
    {
        var line = ItemStatLine.For(Bag(("armor", 10), ("defense", 1)));

        Assert.Equal("Armour 10, +1 defence", line);
    }

    /// <summary>
    /// Added after the roll rather than to the dice, so it is its own term. A range that absorbed
    /// it would claim dice the weapon does not have.
    /// </summary>
    [Fact]
    public void A_flat_damage_add_is_named_separately_from_the_dice()
    {
        var line = ItemStatLine.For(Bag(("damageMin", 2), ("damageMax", 6), ("baseDamage", 3)));

        Assert.Equal("Damage 2-6, +3 damage", line);
    }

    /// <summary>A cloak with no numbers is a cloak, not "Armour 0".</summary>
    [Fact]
    public void An_item_with_no_numbers_has_no_line()
    {
        Assert.Null(ItemStatLine.For(null));
        Assert.Null(ItemStatLine.For(Bag()));
        Assert.Null(ItemStatLine.For(Bag(("armor", 0))));
    }

    /// <summary>
    /// The bags are schemaless and a value can arrive as a string from jsonb, so the reader has to
    /// be the same one the engine uses (<see cref="StatReader"/>) rather than a cast.
    /// </summary>
    [Fact]
    public void A_number_that_arrived_as_a_string_still_reads()
    {
        Assert.Equal("Armour 26", ItemStatLine.For(Bag(("armor", "26"))));
    }

    // -----------------------------------------------------------------------
    // Comparing two
    // -----------------------------------------------------------------------

    /// <summary>
    /// The question the playtest note asked: is this better than the one I have. Answered as what
    /// moves and in which direction, rather than as a verdict — a trade is not an upgrade.
    /// </summary>
    [Fact]
    public void A_trade_reports_both_directions()
    {
        var oathmaul = Bag(("damageMin", 7), ("damageMax", 13), ("bonus", 4));
        var keening = Bag(("damageMin", 4), ("damageMax", 8), ("bonus", 5));

        Assert.Equal("+4 damage, -1 to hit", ItemStatLine.Delta(oathmaul, keening));
    }

    [Fact]
    public void Armour_compares_on_its_own_terms()
    {
        Assert.Equal(
            "+8 armour",
            ItemStatLine.Delta(Bag(("armor", 26)), Bag(("armor", 18))));
    }

    /// <summary>
    /// Two weapons whose ranges differ on one side only differ by half a point on average, and
    /// that is real — rounding it away would report two different weapons as identical.
    /// </summary>
    [Fact]
    public void Half_a_point_of_average_damage_is_not_lost()
    {
        Assert.Equal(
            "+0.5 damage",
            ItemStatLine.Delta(Bag(("damageMin", 2), ("damageMax", 7)), Bag(("damageMin", 2), ("damageMax", 6))));
    }

    /// <summary>
    /// The dice and the flat add are one number to a player: both land as damage. Reporting them
    /// as two terms would be reporting the implementation.
    /// </summary>
    [Fact]
    public void The_flat_add_and_the_dice_are_one_damage_figure()
    {
        var withFlat = Bag(("damageMin", 4), ("damageMax", 8), ("baseDamage", 2));
        var without = Bag(("damageMin", 4), ("damageMax", 8));

        Assert.Equal("+2 damage", ItemStatLine.Delta(withFlat, without));
    }

    [Fact]
    public void A_like_for_like_swap_reports_nothing()
    {
        var same = Bag(("armor", 10), ("defense", 1));

        Assert.Null(ItemStatLine.Delta(same, Bag(("armor", 10), ("defense", 1))));
    }

    /// <summary>An empty slot compares against nothing, so everything the item has is a gain.</summary>
    [Fact]
    public void Against_an_empty_slot_the_whole_item_is_the_difference()
    {
        Assert.Equal("+26 armour", ItemStatLine.Delta(Bag(("armor", 26)), null));
    }

    // -----------------------------------------------------------------------
    // Staying in step with the engine
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>Every key the game reads is a key this describes.</b> One the engine acts on and no
    /// screen mentions is a number changing a fight that the player cannot see — which is how
    /// <c>armor</c>, <c>bonus</c> and <c>defense</c> came to be hidden behind a heading promising
    /// bonuses (BUGS.md #11).
    /// </summary>
    [Fact]
    public void Every_stat_the_engine_reads_produces_a_word()
    {
        foreach (var key in EquipmentResolver.KnownStatKeys)
        {
            if (ItemStatLine.Unknown.Contains(key))
            {
                continue;
            }

            var line = ItemStatLine.For(Bag((key, 7)));

            Assert.True(
                !string.IsNullOrEmpty(line),
                $"'{key}' is read by EquipmentResolver and says nothing on any player screen.");
        }
    }

    /// <summary>
    /// And a movement in any of them is a movement a comparison reports, or the shop would call
    /// two different items the same.
    /// </summary>
    [Fact]
    public void Every_stat_the_engine_reads_moves_a_comparison()
    {
        foreach (var key in EquipmentResolver.KnownStatKeys)
        {
            if (ItemStatLine.Unknown.Contains(key))
            {
                continue;
            }

            Assert.NotNull(ItemStatLine.Delta(Bag((key, 9)), Bag((key, 3))));
        }
    }
}
