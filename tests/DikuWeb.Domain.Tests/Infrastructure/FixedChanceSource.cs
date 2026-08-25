using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Tests.Infrastructure;

/// <summary>
/// A randomness source whose <see cref="IRandomSource.NextDouble"/> is pinned, so a test can say
/// "this swing lands" or "this one does not" outright.
/// </summary>
/// <remarks>
/// Landing a blow is a probability rather than a die face (PLAN.md §4.6), and
/// <c>RandomSourceExtensions.Chance</c> reads <see cref="IRandomSource.NextDouble"/> — so pinning
/// that is how a test names an outcome. The old suite searched for a seed whose first d20 came up
/// on a chosen face, which has no equivalent here and was always a slow way to say something
/// simple.
///
/// Dice still come from a seeded source, so damage stays realistic and varies run to run. The same
/// shape as <c>WorldHarness.ScriptedChanceSource</c> in the Engine tests, which exists for the
/// parry roll.
/// </remarks>
internal sealed class FixedChanceSource(double nextDouble, int seed = 42) : IRandomSource
{
    private readonly SeededRandomSource _dice = new(seed);

    /// <summary>Every chance succeeds: the swing lands, and it is a critical.</summary>
    public static FixedChanceSource Always => new(0.0);

    /// <summary>No chance succeeds: the swing misses.</summary>
    public static FixedChanceSource Never => new(0.999);

    /// <summary>
    /// Lands an ordinary blow: above <c>CriticalChance</c> so the critical roll fails, below any
    /// hit chance worth testing so the swing itself still connects.
    /// </summary>
    public static FixedChanceSource OrdinaryHit(int seed = 42) => new(0.5, seed);

    public int Next(int minInclusive, int maxExclusive) => _dice.Next(minInclusive, maxExclusive);

    public double NextDouble() => nextDouble;
}
