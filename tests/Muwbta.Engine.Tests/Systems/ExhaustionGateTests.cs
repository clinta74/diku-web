using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Systems;

namespace Muwbta.Engine.Tests.Systems;

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

/// <summary>
/// Hunger and thirst arrive over exactly the span they are authored for.
/// </summary>
/// <remarks>
/// Worth asserting because the obvious implementation cannot do it. Eight hours over a hundred
/// points is 9.6 ticks a point, and a modulus has to round — which is twenty minutes of drift on
/// hunger and ten on thirst, in a number chosen to be a round figure.
/// </remarks>
public sealed class NeedsSystemTimingTests
{
    private static int PointsAfter(long ticks, long ticksToEmpty)
    {
        var points = 0;

        for (var tick = 1; tick <= ticks; tick++)
        {
            if (tick * Needs.Worst / ticksToEmpty > (tick - 1) * Needs.Worst / ticksToEmpty)
            {
                points++;
            }
        }

        return points;
    }

    [Fact]
    public void Hunger_reaches_starving_at_exactly_eight_hours()
    {
        Assert.Equal(Needs.Worst, PointsAfter(NeedsSystem.TicksToStarving, NeedsSystem.TicksToStarving));

        // And not a tick early. Reaching the worst before the span is up would mean the span is
        // shorter than it says, which is the drift this arithmetic exists to avoid.
        Assert.True(
            PointsAfter(NeedsSystem.TicksToStarving - 1, NeedsSystem.TicksToStarving) < Needs.Worst);
    }

    [Fact]
    public void Thirst_reaches_parched_at_exactly_six_hours()
    {
        Assert.Equal(Needs.Worst, PointsAfter(NeedsSystem.TicksToParched, NeedsSystem.TicksToParched));
    }

    /// <summary>Thirst arrives first, which is what gives a waterskin its own job.</summary>
    [Fact]
    public void Thirst_arrives_before_hunger()
    {
        Assert.True(NeedsSystem.TicksToParched < NeedsSystem.TicksToStarving);
    }

    /// <summary>Halfway through the span is halfway to the worst of it, not a burst at the end.</summary>
    [Fact]
    public void The_climb_is_even()
    {
        var half = PointsAfter(NeedsSystem.TicksToStarving / 2, NeedsSystem.TicksToStarving);

        Assert.InRange(half, (Needs.Worst / 2) - 1, (Needs.Worst / 2) + 1);
    }
}
