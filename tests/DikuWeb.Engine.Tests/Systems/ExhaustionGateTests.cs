using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Systems;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// What a character owes before they can move again, and the one case where they owe nothing.
/// </summary>
public sealed class ExhaustionGateTests
{
    private static Character Spent(int stamina, CombatState state = CombatState.Idle) => new()
    {
        AccountId = Guid.Empty,
        Name = "Kael",
        Path = CharacterPath.Warden,
        Level = 10,
        Attributes = AttributeSet.Baseline,
        Vitals = new Vitals { Health = 50, HealthMax = 50, Stamina = stamina, StaminaMax = 100 },
        RoomKey = RoomKey.Create("t", "t", "t"),
        CreatedAt = DateTimeOffset.UnixEpoch,
        CombatState = state,
    };

    /// <summary>Carrying nothing still owes something — everyone catches their breath.</summary>
    [Fact]
    public void Everyone_owes_at_least_the_minimum()
    {
        Assert.Equal(ExhaustionGate.MinimumOwed, ExhaustionGate.StaminaOwed(0));
    }

    /// <summary>And nobody owes more than the ceiling, however absurd the load.</summary>
    [Theory]
    [InlineData(1_000_000)]
    [InlineData(int.MaxValue)]
    public void Nobody_owes_more_than_the_ceiling(int grams)
    {
        Assert.Equal(ExhaustionGate.MaximumOwed, ExhaustionGate.StaminaOwed(grams));
    }

    /// <summary>Negative weight is nonsense and lands on the floor rather than throwing.</summary>
    [Fact]
    public void Negative_weight_is_the_minimum()
    {
        Assert.Equal(ExhaustionGate.MinimumOwed, ExhaustionGate.StaminaOwed(-5000));
    }

    /// <summary>
    /// A heavier character owes more. This is the whole reason item weight exists.
    /// </summary>
    [Fact]
    public void Carrying_more_costs_more()
    {
        var light = ExhaustionGate.StaminaOwed(5_000);
        var heavy = ExhaustionGate.StaminaOwed(40_000);

        Assert.True(heavy > light, $"heavy {heavy} should owe more than light {light}");
    }

    /// <summary>A character with stamina to spare is never refused.</summary>
    [Fact]
    public void Having_stamina_is_not_exhaustion()
    {
        Assert.Null(ExhaustionGate.Refuse(Spent(stamina: 50), carriedGrams: 40_000));
    }

    /// <summary>An empty character is.</summary>
    [Fact]
    public void An_empty_character_is_refused()
    {
        Assert.NotNull(ExhaustionGate.Refuse(Spent(stamina: 0), carriedGrams: 0));
    }

    /// <summary>
    /// Never in a fight, however empty.
    /// </summary>
    /// <remarks>
    /// The rule that keeps this from being a death sentence. A character who hits zero beside
    /// something aggressive has to keep the moves that get them out — swinging and running — or the
    /// cost of running yourself dry is not a cost, it is an execution.
    /// </remarks>
    [Fact]
    public void Fighting_is_always_allowed()
    {
        Assert.Null(ExhaustionGate.Refuse(
            Spent(stamina: 0, CombatState.Fighting), carriedGrams: 50_000));
    }

    /// <summary>
    /// The weight counted is what is owned, worn included.
    /// </summary>
    /// <remarks>
    /// A harness is on your back whether or not it is in your pack, and <c>InventoryOf</c> returns
    /// owned items rather than unequipped ones — so this is the behaviour that falls out, and it is
    /// the one that is right.
    /// </remarks>
    [Fact]
    public void Worn_gear_counts_toward_the_load()
    {
        var items = new[]
        {
            new ItemInstance { TemplateKey = "harness", EquippedSlot = ItemSlot.Chest },
            new ItemInstance { TemplateKey = "loaf" },
        };

        var carried = ExhaustionGate.CarriedGrams(
            items,
            key => key == "harness" ? 9_000 : 400);

        Assert.Equal(9_400, carried);
    }

    /// <summary>An item whose template nobody loaded weighs nothing rather than throwing.</summary>
    [Fact]
    public void An_unknown_template_weighs_nothing()
    {
        var carried = ExhaustionGate.CarriedGrams(
            [new ItemInstance { TemplateKey = "ghost" }],
            _ => 0);

        Assert.Equal(0, carried);
    }
}
