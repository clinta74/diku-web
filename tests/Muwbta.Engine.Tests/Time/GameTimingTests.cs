using Muwbta.Engine.Time;

namespace Muwbta.Engine.Tests.Time;

public sealed class GameTimingTests
{
    [Fact]
    public void Documented_cadences_match_their_real_world_durations()
    {
        // PLAN.md §2.3 states these in seconds; the constants are in pulses. If someone
        // changes PulseInterval, this catches the drift rather than letting combat
        // silently speed up.
        Assert.Equal(TimeSpan.FromSeconds(2), GameTiming.PulseInterval * GameTiming.CombatRoundPulses);
        Assert.Equal(TimeSpan.FromSeconds(4), GameTiming.PulseInterval * GameTiming.MobAiPulses);
        Assert.Equal(TimeSpan.FromSeconds(15), GameTiming.PulseInterval * GameTiming.SpawnSweepPulses);
        Assert.Equal(TimeSpan.FromSeconds(60), GameTiming.PulseInterval * GameTiming.RegenPulses);
        Assert.Equal(TimeSpan.FromMinutes(5), GameTiming.PulseInterval * GameTiming.AutosavePulses);
    }

    [Fact]
    public void Pulse_budget_is_ten_percent_of_the_interval()
    {
        Assert.Equal(GameTiming.PulseInterval * 0.1, GameTiming.PulseBudget);
    }

    [Theory]
    [InlineData(0, 8, true)]
    [InlineData(8, 8, true)]
    [InlineData(7, 8, false)]
    [InlineData(16, 8, true)]
    public void RunsOn_fires_only_on_pulse_multiples(long pulse, int every, bool expected) =>
        Assert.Equal(expected, GameTiming.RunsOn(pulse, every));
}

public sealed class ManualGameClockTests
{
    [Fact]
    public void Advancing_pulses_moves_wall_time_in_step()
    {
        var clock = new ManualGameClock(DateTimeOffset.UnixEpoch);

        clock.AdvancePulses(GameTiming.CombatRoundPulses);

        Assert.Equal(GameTiming.CombatRoundPulses, clock.CurrentPulse);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), clock.UtcNow);
    }

    [Fact]
    public void Ten_minutes_of_game_time_costs_no_real_time()
    {
        // The point of the abstraction: a long simulation is instant in a test.
        var clock = new ManualGameClock();

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(2400, clock.CurrentPulse);
    }
}
