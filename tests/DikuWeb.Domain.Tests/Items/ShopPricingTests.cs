using DikuWeb.Domain.Items;

namespace DikuWeb.Domain.Tests.Items;

/// <summary>
/// What a shopkeeper charges (PLAN.md §4.13).
/// </summary>
/// <remarks>
/// Unit tests rather than shop tests, which is why the arithmetic is in Domain: the rounding rule
/// is the whole of this feature, and pinning it needs two numbers rather than a shopkeeper, a
/// room, and a player standing in it. <c>ShopCommandTests</c> covers the part that needs a shop -
/// that <c>list</c> and <c>buy</c> both ask this.
/// </remarks>
public sealed class ShopPricingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(-1)]
    public void No_markup_prices_at_base_value(decimal markup)
    {
        // A negative reads as absent rather than as a discount: §4.13 keeps discounting out
        // because the minimum-increase rule contradicts it, so a negative is a typo.
        Assert.Equal(40, ShopPricing.Price(40, markup));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 6)]
    [InlineData(10, 11)]
    [InlineData(12, 14)]
    [InlineData(100, 110)]
    public void A_tenth_over_rounds_up_to_the_next_whole_gold(int baseValue, long expected)
    {
        // The case from play: 1 gold at 1.1x is 1.1, and a shop rounds in its own favour. Rounding
        // to nearest would list it at 1, which is a dial that does nothing on cheap stock.
        Assert.Equal(expected, ShopPricing.Price(baseValue, 0.1m));
    }

    [Fact]
    public void A_markup_never_adds_less_than_a_gold()
    {
        // Implied by the ceiling everywhere except a base of zero, where it still holds: a trader
        // charging nothing is not a trader.
        Assert.Equal(1, ShopPricing.Price(0, 0.1m));
        Assert.Equal(2, ShopPricing.Price(1, 0.001m));
    }

    [Fact]
    public void An_exact_multiple_is_not_rounded_up_by_a_fraction_that_is_not_there()
    {
        // decimal is why: in binary floating point 100 x 1.1 is 110.00000000000001, and the
        // ceiling would charge a gold of representation error. The builder's preview reimplements
        // this in TypeScript, where it has to correct for exactly that - these are the cases it
        // is held to.
        Assert.Equal(110, ShopPricing.Price(100, 0.1m));
        Assert.Equal(23, ShopPricing.Price(20, 0.15m));
        Assert.Equal(119, ShopPricing.Price(70, 0.7m));
    }

    [Fact]
    public void A_larger_markup_scales_the_whole_price()
    {
        Assert.Equal(125, ShopPricing.Price(100, 0.25m));
        Assert.Equal(200, ShopPricing.Price(100, 1.0m));
        Assert.Equal(13, ShopPricing.Price(10, 0.25m));
    }

    [Fact]
    public void A_price_is_never_negative()
    {
        // BaseValue is an int a builder types. A shop quoting -5 gold would be a purchase that
        // pays out, which is the one direction this must not fail in.
        Assert.Equal(0, ShopPricing.Price(-5, 0m));
        Assert.Equal(1, ShopPricing.Price(-5, 0.1m));
    }

    [Fact]
    public void Price_rises_monotonically_with_the_markup()
    {
        // The property behind the builder's preview: turning the dial up must never lower a price,
        // whatever the rounding does at the boundaries.
        var previous = ShopPricing.Price(37, 0m);

        for (var markup = 0.05m; markup <= 2m; markup += 0.05m)
        {
            var price = ShopPricing.Price(37, markup);
            Assert.True(price >= previous, $"markup {markup} priced {price}, below {previous}");
            previous = price;
        }
    }
}
