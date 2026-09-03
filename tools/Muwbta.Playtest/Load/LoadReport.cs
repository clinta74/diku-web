using System.Globalization;
using System.Text;

namespace Muwbta.Playtest.Load;

/// <summary>
/// Turns a load run into the four or five sentences somebody actually needs.
/// </summary>
/// <remarks>
/// Written against <c>PLAN.md</c> §11, which is the only reason this apparatus grew a load mode:
/// "200 concurrent sessions on one process" and "pulse duration p99 under 25 ms" were the two
/// targets with nothing behind them, the first measurements having been taken single-player
/// against an idle world.
/// </remarks>
public static class LoadReport
{
    /// <summary>The §11 pulse budget, in milliseconds, and an exact bucket boundary.</summary>
    public const double PulseBudgetMs = 25;

    /// <summary>
    /// The loop's pulse interval, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Duplicated from <c>GameTiming.PulseInterval</c> rather than referenced, because the
    /// apparatus is a client and does not see Engine. It is used only to derive how many pulses
    /// <em>should</em> have happened in the window — which is the sharpest signal in the whole
    /// report, and degrades honestly if the number ever drifts: a mismatch shows up as an
    /// apparent shortfall, which is exactly what a reader is being asked to look at.
    /// </remarks>
    public const double PulseIntervalMs = 250;

    public static string Build(LoadOutcome run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var pulse = run.After.Pulse.Since(run.Before.Pulse);
        var command = run.After.CommandLatency.Since(run.Before.CommandLatency);
        var report = new StringBuilder();

        var window = run.Window;
        var owed = window.TotalMilliseconds / PulseIntervalMs;
        var kept = owed <= 0 ? 0 : pulse.Count / owed;
        var held = run.Replicas * run.CastSize;
        var failed = run.Sessions.Count(s => !s.Arrived);

        report.AppendLine("── Load");
        report.AppendLine();

        Line(report, $"window            {window.TotalSeconds:0.0}s of steady state, {run.Before.SessionsActive} → {run.After.SessionsActive} sessions active");
        Line(report, $"sessions          {run.Arrived} of {held} arrived{(failed > 0 ? $", {failed} failed to" : string.Empty)}");
        report.AppendLine();

        // The single most informative line here, and the one needing no interpolation at all. A
        // 250 ms loop owes four pulses a second; if it delivered three it is behind, and no
        // percentile can show that, because the pulses it missed were never recorded. A histogram
        // alone cannot say this - a loop running half as often reports the same healthy p99 as one
        // keeping up.
        var behind = kept < ScheduleFloor ? "  ← THE LOOP IS BEHIND" : string.Empty;
        Line(report, $"pulses            {pulse.Count:N0} in the window, {owed:N0} owed ({kept:P1} of schedule){behind}");
        Line(report, $"mean              {pulse.Mean:0.###} ms");

        if (pulse.At(0.5) is { } p50)
        {
            Line(report, $"p50               {p50}");
        }

        if (pulse.At(0.99) is { } p99)
        {
            Line(report, $"p99               {p99}");
        }

        // Exact, because 25 is a bucket boundary the exporter puts there on purpose. The
        // percentiles above are interpolated inside a bucket; this one is a count.
        if (pulse.ShareAbove(PulseBudgetMs) is { } over)
        {
            Line(report, $"over 25 ms        {pulse.CountAbove(PulseBudgetMs) ?? 0:N0} pulses, {over:P2} — exact, 25 is a bucket edge");
        }

        // Two independent paths to one number: the histogram's boundary, and the counter the loop
        // increments when its own watchdog fires. They should agree. Where they do not, one of the
        // two is measuring something other than what its name says, which is worth more than
        // either number on its own.
        var counted = run.After.PulsesOverBudget - run.Before.PulsesOverBudget;

        if (pulse.CountAbove(PulseBudgetMs) is { } bucketed && bucketed != counted)
        {
            Line(report, $"!                 the over-budget counter says {counted:N0} where the buckets say {bucketed:N0}");
        }

        report.AppendLine();

        var commands = run.After.CommandsHandled - run.Before.CommandsHandled;
        var rate = window.TotalSeconds <= 0 ? 0 : commands / window.TotalSeconds;

        Line(report, $"commands          {commands:N0} handled, {rate:N1}/s");

        if (command.Count > 0 && command.At(0.99) is { } commandP99)
        {
            Line(report, $"command p99       {commandP99} in-loop");
        }

        report.AppendLine();
        report.AppendLine(Verdict(run, pulse, kept));

        return report.ToString();
    }

    /// <summary>
    /// §11 as a sentence, refusing to answer where the run could not support one.
    /// </summary>
    /// <remarks>
    /// A run that could not put the sessions in the world measured a smaller world, and "p99 under
    /// budget" about that world would be true and useless. The session count is therefore checked
    /// before any timing is reported as a verdict.
    /// </remarks>
    private static string Verdict(LoadOutcome run, Histogram pulse, double kept)
    {
        // The lower of the two scrapes, not the higher: the claim is that the world held this many
        // for the whole window, and a count taken only at the start would credit a run whose
        // sessions were dropping out while it was being measured.
        var held = Math.Min(run.Before.SessionsActive, run.After.SessionsActive);

        if (held < run.SessionsAsked)
        {
            return Culture($"  VERDICT: NOT PROVEN at {run.SessionsAsked}. The world held {held} session(s) across the whole window, so the §11 session target was not under test — whatever the pulse numbers say, they describe a smaller world than the one asked for.");
        }

        if (kept < ScheduleFloor)
        {
            return Culture($"  VERDICT: FAILED at {held} sessions. The loop ran {kept:P1} of the pulses it owed, so it is not keeping its own schedule — pulse duration is beside the point when pulses are being missed.");
        }

        var over = pulse.ShareAbove(PulseBudgetMs) ?? 0;

        if (over > 0.01)
        {
            return Culture($"  VERDICT: FAILED at {held} sessions. {over:P2} of pulses exceeded the 25 ms budget, which is more than the 1% a p99 target allows.");
        }

        var p99 = pulse.At(0.99)?.Estimate ?? 0;

        return Culture($"  VERDICT: MET at {held} sessions. p99 {p99:0.###} ms against a 25 ms budget, {over:P2} of pulses over it, and the loop kept {kept:P1} of its schedule.");
    }

    /// <summary>
    /// How much of its schedule the loop must keep before its timings mean anything.
    /// </summary>
    /// <remarks>
    /// Two percent of slack rather than none, because the window is bracketed by two HTTP scrapes
    /// rather than by the loop itself: a pulse in flight when either scrape lands is counted on one
    /// side and not the other. At the two-minute windows this runs, that rounding is a handful of
    /// pulses out of a few thousand, well inside the margin — and a loop genuinely falling behind
    /// misses far more than two percent.
    /// </remarks>
    private const double ScheduleFloor = 0.98;

    private static void Line(StringBuilder report, FormattableString formatted) =>
        report.AppendLine("  " + formatted.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Formats invariantly, so a run on a machine whose decimal separator is a comma produces the
    /// same report as one that does not — and so the numbers can be pasted between them.
    /// </summary>
    private static string Culture(FormattableString formatted) =>
        formatted.ToString(CultureInfo.InvariantCulture);

    /// <summary>The machine-readable form, for trending one run against the next.</summary>
    public static string Json(LoadOutcome run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var pulse = run.After.Pulse.Since(run.Before.Pulse);
        var expected = run.Window.TotalMilliseconds / PulseIntervalMs;

        return System.Text.Json.JsonSerializer.Serialize(
            new
            {
                startedAt = run.Before.At,
                windowSeconds = run.Window.TotalSeconds,
                sessionsAsked = run.SessionsAsked,
                sessionsActiveBefore = run.Before.SessionsActive,
                sessionsActiveAfter = run.After.SessionsActive,
                sessionsFailed = run.Sessions.Count(s => !s.Arrived),
                pulses = pulse.Count,
                pulsesOwed = (long)expected,
                pulseMeanMs = pulse.Mean,
                pulseP50Ms = pulse.At(0.5)?.Estimate,
                pulseP99Ms = pulse.At(0.99)?.Estimate,
                pulsesOverBudget = pulse.CountAbove(PulseBudgetMs),
                pulsesOverBudgetCounter = run.After.PulsesOverBudget - run.Before.PulsesOverBudget,
                commandsHandled = run.After.CommandsHandled - run.Before.CommandsHandled,
                commandP99Ms = run.After.CommandLatency.Since(run.Before.CommandLatency).At(0.99)?.Estimate,
                roomsLoaded = run.After.RoomsLoaded,
                failures = run.Sessions
                    .Where(s => !s.Arrived)
                    .Select(s => new { s.Replica, s.Failure })
                    .ToList(),
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
