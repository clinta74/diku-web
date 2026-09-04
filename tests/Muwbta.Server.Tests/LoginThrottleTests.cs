using Muwbta.Server.Auth;

namespace Muwbta.Server.Tests;

/// <summary>
/// The arithmetic of the per-account backoff, against a clock the test controls.
/// </summary>
public sealed class LoginThrottleTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static (LoginThrottle Throttle, ManualClock Clock) Build(
        int failuresBefore = 3, int baseSeconds = 10, int maxSeconds = 40)
    {
        var clock = new ManualClock(Start);
        var options = new AuthOptions
        {
            LoginFailuresBeforeBackoff = failuresBefore,
            LoginBackoffSeconds = baseSeconds,
            LoginBackoffMaxSeconds = maxSeconds,
        };

        return (new LoginThrottle(options, clock), clock);
    }

    [Fact]
    public void Nothing_happens_below_the_threshold()
    {
        var (throttle, _) = Build();

        throttle.RecordFailure("kael");
        throttle.RecordFailure("kael");

        Assert.Null(throttle.RetryAfter("kael"));
    }

    [Fact]
    public void The_pause_starts_at_the_threshold_and_doubles_to_the_ceiling()
    {
        var (throttle, _) = Build(failuresBefore: 3, baseSeconds: 10, maxSeconds: 40);

        throttle.RecordFailure("kael");
        throttle.RecordFailure("kael");
        throttle.RecordFailure("kael");
        Assert.Equal(TimeSpan.FromSeconds(10), throttle.RetryAfter("kael"));

        throttle.RecordFailure("kael");
        Assert.Equal(TimeSpan.FromSeconds(20), throttle.RetryAfter("kael"));

        throttle.RecordFailure("kael");
        Assert.Equal(TimeSpan.FromSeconds(40), throttle.RetryAfter("kael"));

        // Capped. Without a ceiling, whoever hammers an account owns it: its real owner could
        // never get back in.
        throttle.RecordFailure("kael");
        Assert.Equal(TimeSpan.FromSeconds(40), throttle.RetryAfter("kael"));
    }

    [Fact]
    public void The_failure_that_starts_a_pause_says_so_and_later_ones_do_not()
    {
        // The dashboard counts pauses as events. Every failure past the threshold lengthens the
        // pause, but only the one that lit the fuse is a new pause.
        var (throttle, _) = Build(failuresBefore: 2);

        Assert.False(throttle.RecordFailure("kael"));
        Assert.True(throttle.RecordFailure("kael"));
        Assert.False(throttle.RecordFailure("kael"));
    }

    [Fact]
    public void The_pause_expires_with_the_clock()
    {
        var (throttle, clock) = Build(failuresBefore: 1, baseSeconds: 10);

        throttle.RecordFailure("kael");
        Assert.Equal(TimeSpan.FromSeconds(10), throttle.RetryAfter("kael"));

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(6), throttle.RetryAfter("kael"));

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(throttle.RetryAfter("kael"));
        Assert.Null(throttle.LockedUntil("kael"));
    }

    [Fact]
    public void A_success_forgives_everything()
    {
        var (throttle, _) = Build(failuresBefore: 1);

        throttle.RecordFailure("kael");
        throttle.RecordSuccess("kael");

        Assert.Null(throttle.RetryAfter("kael"));

        // And the count starts over: one failure is below a threshold of one plus... no, at it.
        // What matters is that it is not the fourth failure it would have been.
        throttle.RecordFailure("kael");
        Assert.Equal(TimeSpan.FromSeconds(10), throttle.RetryAfter("kael"));
    }

    [Fact]
    public void An_admin_can_lift_it()
    {
        var (throttle, _) = Build(failuresBefore: 1);

        throttle.RecordFailure("kael");
        Assert.True(throttle.Lift("kael"));
        Assert.Null(throttle.RetryAfter("kael"));

        // Nothing to lift is reported, so the panel can say so rather than pretend.
        Assert.False(throttle.Lift("kael"));
    }

    [Fact]
    public void Old_failures_do_not_shorten_a_new_fuse()
    {
        var (throttle, clock) = Build(failuresBefore: 3, maxSeconds: 40);

        throttle.RecordFailure("kael");
        throttle.RecordFailure("kael");

        // Longer than the ceiling since the last failure: the count restarts.
        clock.Advance(TimeSpan.FromSeconds(41));
        throttle.RecordFailure("kael");

        Assert.Null(throttle.RetryAfter("kael"));
    }

    [Fact]
    public void Names_are_compared_the_way_the_database_compares_them()
    {
        var (throttle, _) = Build(failuresBefore: 1);

        throttle.RecordFailure("Kael");

        Assert.NotNull(throttle.RetryAfter("kael"));
        Assert.NotNull(throttle.RetryAfter("KAEL"));
    }

    [Fact]
    public void An_unknown_name_is_slowed_like_a_known_one()
    {
        // Otherwise the throttle answers "does this account exist" by whether it ever fires.
        var (throttle, _) = Build(failuresBefore: 1);

        throttle.RecordFailure("nobody-at-all");

        Assert.NotNull(throttle.RetryAfter("nobody-at-all"));
    }

    [Fact]
    public void Zero_turns_it_off()
    {
        var (throttle, _) = Build(failuresBefore: 0);

        for (var i = 0; i < 20; i++)
        {
            throttle.RecordFailure("kael");
        }

        Assert.Null(throttle.RetryAfter("kael"));
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
