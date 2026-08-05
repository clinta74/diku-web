using DikuWeb.Engine.Randomness;

namespace DikuWeb.Engine.Tests.Randomness;

public sealed class SeededRandomSourceTests
{
    [Fact]
    public void Same_seed_produces_the_same_sequence()
    {
        // The property the whole determinism story rests on: a failing balance test can be
        // re-run and will fail identically.
        var a = new SeededRandomSource(seed: 1234);
        var b = new SeededRandomSource(seed: 1234);

        var fromA = Enumerable.Range(0, 50).Select(_ => a.Roll(20)).ToArray();
        var fromB = Enumerable.Range(0, 50).Select(_ => b.Roll(20)).ToArray();

        Assert.Equal(fromA, fromB);
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var a = new SeededRandomSource(seed: 1);
        var b = new SeededRandomSource(seed: 2);

        var fromA = Enumerable.Range(0, 50).Select(_ => a.Roll(20)).ToArray();
        var fromB = Enumerable.Range(0, 50).Select(_ => b.Roll(20)).ToArray();

        Assert.NotEqual(fromA, fromB);
    }

    [Fact]
    public void Roll_stays_within_one_to_sides_inclusive()
    {
        var random = new SeededRandomSource(seed: 99);

        for (var i = 0; i < 5_000; i++)
        {
            var roll = random.Roll(20);
            Assert.InRange(roll, 1, 20);
        }
    }

    [Fact]
    public void Roll_can_produce_both_extremes()
    {
        // A d20 that never rolls 1 or 20 would silently disable critical hits and
        // fumbles, which is the kind of bug that hides for months.
        var random = new SeededRandomSource(seed: 7);
        var seen = new HashSet<int>();

        for (var i = 0; i < 5_000; i++)
        {
            seen.Add(random.Roll(20));
        }

        Assert.Contains(1, seen);
        Assert.Contains(20, seen);
    }

    [Fact]
    public void Multi_dice_rolls_sum_within_bounds()
    {
        var random = new SeededRandomSource(seed: 42);

        for (var i = 0; i < 2_000; i++)
        {
            Assert.InRange(random.Roll(3, 6), 3, 18);
        }
    }
}
