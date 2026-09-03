namespace Muwbta.Domain.Items;

/// <summary>
/// What a shopkeeper charges for what it stocks (PLAN.md §4.13).
/// </summary>
/// <remarks>
/// In Domain rather than in the shop command because it is arithmetic over two numbers with no
/// world in it - the same reason <c>Multipliers.Resolve</c> lives here. The value of that is the
/// rounding rule, which is the whole of this and wants a unit test rather than a shopkeeper, a
/// room, and a player standing in it.
/// </remarks>
public static class ShopPricing
{
    /// <summary>
    /// The asking price for an item of <paramref name="baseValue"/> at a shop set to
    /// <paramref name="markup"/>, where <c>0.1</c> means 1.1x.
    /// </summary>
    /// <remarks>
    /// <b>Rounds up, and never adds less than a gold.</b> A shop rounds in its own favour, and a
    /// markup that disappears on cheap stock is a dial that does nothing where most of a village
    /// shop's inventory sits: at 1.1x a 1-gold loaf costs 2. The <c>base + 1</c> floor is implied
    /// by the ceiling for every base of 1 or more, so it only does work at a base value of zero -
    /// where it is still the answer wanted, because a trader charging nothing is not a trader.
    ///
    /// <b>A markup at or below zero is no markup.</b> Absence must price at base, as an untouched
    /// shopkeeper should, and a negative reads the same way rather than as a discount: §4.13 keeps
    /// discounting out because the minimum-increase rule contradicts it, so a shop that pays the
    /// player to shop there would need a rule of its own rather than a sign flip on this one.
    /// </remarks>
    public static long Price(int baseValue, decimal markup)
    {
        // A negative base is not a price. Guarded rather than trusted: BaseValue is an int a
        // builder types, and a shop quoting -5 gold would be a purchase that pays out.
        var floorValue = Math.Max(0, baseValue);

        if (markup <= 0m)
        {
            return floorValue;
        }

        var raised = (long)Math.Ceiling(floorValue * (1m + markup));

        return Math.Max(raised, floorValue + 1);
    }
}
