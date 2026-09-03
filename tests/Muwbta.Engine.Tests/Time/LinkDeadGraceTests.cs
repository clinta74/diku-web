using Muwbta.Engine;
using Muwbta.Engine.Time;

namespace Muwbta.Engine.Tests.Time;

/// <summary>
/// The link-dead window, expressed in the two units that have to agree: pulses, which the loop
/// counts, and seconds, which is what a deployment sets (PLAN.md §3.6, MOBILE.md §6).
/// </summary>
public sealed class LinkDeadGraceTests
{
    [Fact]
    public void The_default_is_the_ninety_seconds_the_plan_specifies()
    {
        var options = new EngineOptions();

        Assert.Equal(360, options.LinkDeadGracePulses);
        Assert.Equal(90, options.LinkDeadGraceSeconds);
    }

    [Theory]
    [InlineData(30, 120)]
    [InlineData(90, 360)]
    [InlineData(300, 1200)]
    public void Setting_seconds_sets_the_pulses_the_loop_actually_counts(int seconds, int pulses)
    {
        // The loop compares pulses and knows nothing about seconds, so a seconds-only setting
        // that did not reach this number would be the same silent no-op it replaced.
        var options = new EngineOptions { LinkDeadGraceSeconds = seconds };

        Assert.Equal(pulses, options.LinkDeadGracePulses);
    }

    [Fact]
    public void Reading_back_gives_what_was_set()
    {
        var options = new EngineOptions { LinkDeadGraceSeconds = 300 };

        Assert.Equal(300, options.LinkDeadGraceSeconds);
    }

    [Fact]
    public void Setting_pulses_directly_still_reads_as_seconds()
    {
        // Both directions are supported because both are legitimate: a test that wants an exact
        // pulse count should not have to convert, and a deployment should not have to know the
        // pulse rate.
        var options = new EngineOptions { LinkDeadGracePulses = 8 };

        Assert.Equal(2, options.LinkDeadGraceSeconds);
    }

    [Fact]
    public void A_negative_window_is_treated_as_none_rather_than_as_the_past()
    {
        // Guards the sweep's comparison: a negative pulse count would make every session
        // instantly overdue, evicting players the moment they connected.
        var options = new EngineOptions { LinkDeadGraceSeconds = -5 };

        Assert.Equal(0, options.LinkDeadGracePulses);
    }

    [Fact]
    public void The_conversion_uses_the_engines_own_pulse_interval()
    {
        // Rather than a hardcoded 4. If the pulse rate ever changes, a literal here would keep
        // converting against the old one and the window would quietly become the wrong length.
        var perSecond = 1 / GameTiming.PulseInterval.TotalSeconds;
        var options = new EngineOptions { LinkDeadGraceSeconds = 10 };

        Assert.Equal((int)(10 * perSecond), options.LinkDeadGracePulses);
    }
}
