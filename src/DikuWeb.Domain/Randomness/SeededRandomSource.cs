namespace DikuWeb.Domain.Randomness;

/// <summary>
/// Default implementation. Seed it explicitly in tests; in production it is seeded once at
/// startup and the seed is logged, so a reported balance oddity can be reproduced.
/// </summary>
/// <remarks>
/// Not thread-safe by design - only the single game-loop thread may touch it (PLAN.md §2.1).
/// Note also that the sequence produced for a given seed is stable within a .NET major
/// version but is not guaranteed across them, so seeds are for reproducing a run now,
/// not for reproducing one years later.
/// </remarks>
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    public int Seed { get; } = seed;

    public int Next(int minInclusive, int maxExclusive) =>
        _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();
}
