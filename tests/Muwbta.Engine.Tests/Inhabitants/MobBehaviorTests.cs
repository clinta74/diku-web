using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Inhabitants;

/// <summary>
/// Reading the free-form behavior bag in both the shape it is authored in and the shape it comes
/// back in. The second is the one the running game sees, and the one that used to be unreadable.
/// </summary>
public sealed class MobBehaviorTests
{
    private static Dictionary<string, object> Persisted(Dictionary<string, object> bag) =>
        WorldHarness.AsPersisted(bag);

    [Theory]
    [InlineData("aggressive", MobDisposition.Aggressive)]
    [InlineData("npc", MobDisposition.Npc)]
    [InlineData("passive", MobDisposition.Passive)]
    public void A_disposition_survives_the_round_trip(string authored, MobDisposition expected)
    {
        var bag = Persisted(new Dictionary<string, object> { ["type"] = authored });

        Assert.Equal(expected, MobBehavior.DispositionOf(bag));
    }

    [Fact]
    public void An_absent_type_is_passive()
    {
        Assert.Equal(MobDisposition.Passive, MobBehavior.DispositionOf(new Dictionary<string, object>()));
        Assert.Equal(MobDisposition.Passive, MobBehavior.DispositionOf(null));
    }

    /// <summary>
    /// An unrecognised word must not make a mob hostile, and must not make it invulnerable.
    /// Both would be content bugs a builder could introduce with a typo.
    /// </summary>
    [Fact]
    public void An_unrecognised_type_is_passive_rather_than_hostile_or_untouchable()
    {
        var bag = Persisted(new Dictionary<string, object> { ["type"] = "aggresive" });

        Assert.Equal(MobDisposition.Passive, MobBehavior.DispositionOf(bag));
        Assert.False(MobBehavior.IsAggressive(bag));
        Assert.False(MobBehavior.IsNonCombatant(bag));
    }

    [Fact]
    public void Disposition_matching_ignores_case()
    {
        var bag = Persisted(new Dictionary<string, object> { ["type"] = "Aggressive" });

        Assert.True(MobBehavior.IsAggressive(bag));
    }

    [Fact]
    public void A_persisted_shopkeeper_flag_reads_as_true()
    {
        var bag = Persisted(new Dictionary<string, object> { ["shopkeeper"] = true });

        Assert.True(MobBehavior.IsShopkeeper(bag));
    }

    [Fact]
    public void A_native_shopkeeper_flag_still_reads_as_true()
    {
        // Seed data and the mutation applier build the bag in C#, so both shapes must work.
        var bag = new Dictionary<string, object> { ["shopkeeper"] = true };

        Assert.True(MobBehavior.IsShopkeeper(bag));
    }

    [Fact]
    public void A_shopkeeper_flag_set_to_false_is_not_a_shop()
    {
        var bag = Persisted(new Dictionary<string, object> { ["shopkeeper"] = false });

        Assert.False(MobBehavior.IsShopkeeper(bag));
    }

    [Fact]
    public void A_persisted_string_list_reads_back_as_its_entries()
    {
        var bag = Persisted(new Dictionary<string, object>
        {
            ["sells"] = new List<object> { "bread", "torch" },
        });

        Assert.Equal(["bread", "torch"], MobBehavior.SellsOf(bag));
    }

    [Fact]
    public void A_native_string_list_reads_back_as_its_entries()
    {
        var bag = new Dictionary<string, object>
        {
            ["emotes"] = new List<object> { "snarls", "growls" },
        };

        Assert.Equal(["snarls", "growls"], MobBehavior.EmotesOf(bag));
    }

    /// <summary>
    /// A builder who types one emote into a field meant for a list should get that emote, not
    /// a bag of single characters - which is what treating the string as a sequence would give.
    /// </summary>
    [Fact]
    public void A_bare_string_where_a_list_was_expected_reads_as_one_entry()
    {
        var bag = Persisted(new Dictionary<string, object> { ["emotes"] = "snarls" });

        Assert.Equal(["snarls"], MobBehavior.EmotesOf(bag));
    }

    [Fact]
    public void Blank_entries_are_dropped_so_a_half_filled_row_is_not_stock()
    {
        var bag = Persisted(new Dictionary<string, object>
        {
            ["sells"] = new List<object> { "bread", "", "   ", "torch" },
        });

        Assert.Equal(["bread", "torch"], MobBehavior.SellsOf(bag));
    }

    [Fact]
    public void An_absent_list_is_empty_rather_than_null()
    {
        Assert.Empty(MobBehavior.SellsOf(new Dictionary<string, object>()));
        Assert.Empty(MobBehavior.EmotesOf(null));
    }

    [Fact]
    public void A_list_key_holding_a_number_does_not_throw()
    {
        // The bag is schemaless, so nothing stops a bad write landing here. Reading it must
        // degrade to "no stock", not take the game loop down.
        var bag = Persisted(new Dictionary<string, object> { ["sells"] = 7 });

        Assert.Empty(MobBehavior.SellsOf(bag));
    }
}
