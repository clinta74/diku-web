namespace DikuWeb.Domain.Randomness;

/// <summary>
/// PLAN.md §9: all randomness comes from an injected source seeded per test.
/// Random.Shared must never appear in Domain or Engine, or combat stops being reproducible
/// and a failing balance test cannot be re-run.
/// </summary>
/// <remarks>
/// Kept to the two irreducible primitives. Dice live in <see cref="RandomSourceExtensions"/>
/// rather than as default interface members, because default members are only callable
/// through the interface - implementations would not expose them, which makes both test
/// code and call sites awkward for no benefit.
/// </remarks>
public interface IRandomSource
{
    /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Uniform in [0.0, 1.0).</summary>
    double NextDouble();
}

public static class RandomSourceExtensions
{
    /// <summary>Rolls one die, returning 1..sides inclusive.</summary>
    public static int Roll(this IRandomSource random, int sides)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sides);
        return random.Next(1, sides + 1);
    }

    /// <summary>Rolls <paramref name="count"/> dice of <paramref name="sides"/> and sums them.</summary>
    public static int Roll(this IRandomSource random, int count, int sides)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var total = 0;
        for (var i = 0; i < count; i++)
        {
            total += random.Roll(sides);
        }

        return total;
    }

    /// <summary>True with the given probability, expressed as 0.0-1.0.</summary>
    public static bool Chance(this IRandomSource random, double probability)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.NextDouble() < probability;
    }
}
