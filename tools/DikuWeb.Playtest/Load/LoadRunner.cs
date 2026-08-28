using DikuWeb.Playtest.Plans;
using DikuWeb.Playtest.Recording;
using DikuWeb.Playtest.Running;
using DikuWeb.Playtest.Session;
using DikuWeb.Playtest.Targets;

namespace DikuWeb.Playtest.Load;

/// <summary>How one replica of the cast got on.</summary>
public sealed record SessionOutcome(int Replica, bool Arrived, PlanOutcome? Outcome, string? Failure);

/// <summary>Everything a load run produced.</summary>
public sealed record LoadOutcome(
    int SessionsAsked,
    int Replicas,
    int CastSize,
    TimeSpan Ramp,
    TimeSpan Hold,
    MetricsSnapshot Before,
    MetricsSnapshot After,
    IReadOnlyList<SessionOutcome> Sessions,
    Transcript Observed,
    PlanOutcome? ObservedOutcome)
{
    /// <summary>Sessions that got into the world and stayed long enough to be measured.</summary>
    public int Arrived => Sessions.Count(s => s.Arrived) * CastSize;

    /// <summary>The measured window — the two scrapes, not the wall clock of the whole run.</summary>
    public TimeSpan Window => After.At - Before.At;
}

/// <summary>
/// Holds a stated number of sessions in the world and reads what it did to the game loop.
/// </summary>
/// <remarks>
/// <para>
/// <b>Load here means world state, not request rate.</b> Two hundred characters standing in
/// separate rooms typing <c>look</c> is nearly free; the loop's expensive work is combat, threat,
/// regen and room fan-out, most of which lands on pulses where no request arrived at all. So this
/// drives real plans rather than firing synthetic traffic: whatever the plan has a player do is
/// what two hundred of them do at once.
/// </para>
/// <para>
/// <b>The run has three parts and only the middle one is measured.</b> Arrivals are spread over a
/// ramp, because two hundred registrations landing together measures signing up rather than
/// playing. The hold that follows is the steady state, bracketed by two scrapes of
/// <c>/metrics</c> — and since every instrument there is a total since boot, subtracting one from
/// the other gives exactly the pulses that happened while the world was full, with the idle
/// minutes before the run and the ramp itself excluded rather than averaged in.
/// </para>
/// </remarks>
public sealed class LoadRunner(IGameTarget target, Uri metricsAddress, LoadSettings settings)
{
    /// <summary>
    /// How much scrollback a load session keeps, in bytes.
    /// </summary>
    /// <remarks>
    /// Half a megabyte each, so two hundred sessions hold about a hundred megabytes between them
    /// and the apparatus stays a long way from the limit its container gives it. Bytes rather than
    /// lines because the lines are not a fixed size: a <c>who</c> reply naming two hundred people,
    /// or a room listing two hundred occupants, is kilobytes on its own, and a run bounded at two
    /// thousand entries died of an OutOfMemoryException at seventy sessions with the measurement
    /// half-taken. Generous against what a wait can actually reach - the longest looks back ten
    /// seconds, which even in the busiest room of this plan is a small fraction of this.
    /// </remarks>
    private const int LoadScrollback = 512 * 1024;

    /// <summary>
    /// How much scrollback the one observed session keeps, in bytes.
    /// </summary>
    /// <remarks>
    /// Sixty-four times a load session's budget, because this transcript is the artefact somebody
    /// opens and a few thousand lines of it are worth reading. Still finite, for the reason above:
    /// it is the busiest session in the run by construction.
    /// </remarks>
    private const int ObservedScrollback = 32 * 1024 * 1024;

    public async Task<LoadOutcome> RunAsync(PlanDocument plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var castSize = Math.Max(1, plan.Cast.Count);
        var replicas = (int)Math.Ceiling((double)settings.Sessions / castSize);

        // The plan's cast is the unit that gets replicated, because a plan whose two actors fight
        // each other is only meaningful in pairs. So the sessions actually held is a multiple of
        // the cast size, and is reported as what it is rather than as the number asked for.
        var actual = replicas * castSize;

        Console.WriteLine(
            $"Load: {actual} session(s) — {replicas} × a cast of {castSize}"
            + (actual == settings.Sessions ? string.Empty : $" (asked for {settings.Sessions})"));
        Console.WriteLine(
            $"      ramp {settings.Ramp.TotalSeconds:0}s, then a measured hold of "
            + $"{settings.Hold.TotalSeconds:0}s");
        Console.WriteLine();

        using var probe = new MetricsProbe(metricsAddress);

        // Proves the endpoint answers and the names match before two hundred people arrive. A run
        // that discovers at the end that it cannot read the histogram has wasted the whole hold.
        var opening = await probe.ReadAsync(cancellationToken);
        Console.WriteLine(
            $"      /metrics reachable: {opening.SessionsActive} session(s) already active, "
            + $"{opening.RoomsLoaded} rooms loaded");
        Console.WriteLine();

        var startedAt = DateTimeOffset.UtcNow;
        var rampEnds = startedAt + settings.Ramp;

        // The outside edge of the run, and what every session is told to keep playing until. It
        // has to cover the arrival grace as well as the hold, or the last stragglers would be
        // walking out of the world exactly as the measured window opened.
        var playUntil = rampEnds + ArrivalGrace + settings.Hold;

        // The observed replica keeps a far larger transcript than the rest and is the one written
        // out. The question it answers is not "how fast" but "did an ordinary session still play
        // correctly while this was going on", which no histogram can report.
        //
        // Larger, but still bounded. Left unbounded it was the one thing in the process that grew
        // without limit: at two hundred sessions the observed session stands in the busiest room
        // in the run and is told about every arrival and departure, and the record of that passed
        // a gigabyte over a ten-minute run while every other session sat inside its budget. A
        // reader wants the last few minutes of it, not the first.
        var observed = new Transcript(ObservedScrollback);
        var sessions = new SessionOutcome[replicas];
        var arrivals = new Task[replicas];

        for (var replica = 0; replica < replicas; replica++)
        {
            var index = replica;

            // Spread across the ramp rather than fired together. Arriving is the most expensive
            // thing a session ever does - a registration, a character, an enter, and a stream
            // opening - and doing all of it at once measures the front door instead of the game.
            var stagger = replicas <= 1
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(settings.Ramp.Ticks * index / (replicas - 1));

            arrivals[index] = Task.Run(
                async () =>
                {
                    sessions[index] = await PlaySessionAsync(
                        plan, index, stagger, playUntil, observed, cancellationToken);
                },
                CancellationToken.None);
        }

        await WaitUntilAsync(rampEnds, cancellationToken);

        // The ramp ending is not the same as everybody having arrived, and the difference is not
        // cosmetic. The last session's stagger lands on the ramp deadline, and arriving takes four
        // round trips after that — so a window opened on the deadline itself measures a world that
        // is still filling, and the verdict reads the low session count and correctly refuses to
        // answer. Waiting for the server's own gauge to reach the target is the fix, and it uses
        // exactly the number the verdict will later be judged against.
        var before = await SettleAsync(probe, actual, cancellationToken);

        Console.WriteLine(
            $"      ramp over — {before.SessionsActive} session(s) in the world. Measuring for "
            + $"{settings.Hold.TotalSeconds:0}s.");

        // Measured from the moment the world was full, not from the ramp deadline: settling can
        // take a while at two hundred sessions, and a hold that started its clock before everyone
        // had arrived would be a shorter window than the one asked for.
        await WaitUntilAsync(before.At + settings.Hold, cancellationToken);

        // Before the sessions leave, or the last scrape would describe a world emptying out.
        var after = await probe.ReadAsync(cancellationToken);
        Console.WriteLine($"      hold over — {after.SessionsActive} session(s) still in the world.");
        Console.WriteLine();

        await Task.WhenAll(arrivals);

        var observedOutcome = sessions.FirstOrDefault(s => s?.Replica == 0)?.Outcome;

        return new LoadOutcome(
            settings.Sessions,
            replicas,
            castSize,
            settings.Ramp,
            settings.Hold,
            before,
            after,
            [.. sessions.Where(s => s is not null)],
            observed,
            observedOutcome);
    }

    /// <summary>One replica: waits its turn, arrives, and keeps playing until the hold is over.</summary>
    private async Task<SessionOutcome> PlaySessionAsync(
        PlanDocument plan,
        int replica,
        TimeSpan stagger,
        DateTimeOffset until,
        Transcript observed,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(stagger, cancellationToken);

            var transcript = replica == 0 ? observed : new Transcript(LoadScrollback);

            var runner = new PlanRunner(
                target,
                transcript,
                new RunSettings
                {
                    PlayUntil = until,

                    // Shorter than a playtest's ten seconds. A load session's waits are not
                    // evidence - the observed replica's are - and a wait that hangs on for ten
                    // seconds is a session doing nothing, which is a session not generating the
                    // load it was created to generate.
                    WaitTimeout = replica == 0 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(4),
                });

            var outcome = await runner.RunAsync(plan, cancellationToken);

            // Arrival is read off the names the world handed out, not off RunAsync returning. The
            // runner swallows a PlaytestException by design - the transcript after a failure is
            // usually what explains it - so a session whose registration was refused still comes
            // back as a completed plan. Counting those as arrived would inflate the one number the
            // verdict is not allowed to get wrong.
            var arrived = outcome.CharacterNames.Count == plan.Cast.Count;

            return new SessionOutcome(
                replica,
                arrived,
                outcome,
                arrived ? null : outcome.Problems.FirstOrDefault() ?? "never entered the world");
        }
        catch (OperationCanceledException)
        {
            return new SessionOutcome(replica, Arrived: false, null, "interrupted");
        }
        catch (Exception ex) when (ex is PlaytestException or HttpRequestException or IOException)
        {
            // Never rethrown. One session that could not get in is a finding — very often *the*
            // finding, when it is the hundred and eightieth — and taking the run down with it
            // would destroy the measurement that explains why.
            return new SessionOutcome(replica, Arrived: false, null, ex.Message);
        }
    }

    /// <summary>
    /// Waits for the world to actually hold the sessions that were asked for.
    /// </summary>
    /// <remarks>
    /// Gives up after <see cref="ArrivalGrace"/> and returns what it found rather than throwing: a
    /// run that could only get a hundred and eighty sessions in has discovered something worth
    /// reporting, and the honest thing is to measure that world and say which world it was. The
    /// verdict handles the rest.
    /// </remarks>
    private static async Task<MetricsSnapshot> SettleAsync(
        MetricsProbe probe,
        int wanted,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ArrivalGrace;
        var snapshot = await probe.ReadAsync(cancellationToken);

        while (snapshot.SessionsActive < wanted && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            snapshot = await probe.ReadAsync(cancellationToken);
        }

        return snapshot;
    }

    /// <summary>
    /// How long past the ramp the last arrivals are given before the window opens anyway.
    /// </summary>
    /// <remarks>
    /// Generous, because this time is not wasted - the sessions already in the world are playing
    /// throughout it, so a run that needs all of it has simply had a longer warm-up. What it must
    /// not do is wait forever: a session that cannot get in at all is the finding, and a run that
    /// blocked on it would never report anything.
    /// </remarks>
    private static readonly TimeSpan ArrivalGrace = TimeSpan.FromSeconds(90);

    private static async Task WaitUntilAsync(DateTimeOffset moment, CancellationToken cancellationToken)
    {
        var remaining = moment - DateTimeOffset.UtcNow;

        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }
}

/// <summary>What a load run was asked for.</summary>
public sealed record LoadSettings
{
    /// <summary>Concurrent character sessions to hold in the world.</summary>
    public int Sessions { get; init; }

    /// <summary>How long arrivals are spread over.</summary>
    public TimeSpan Ramp { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long the full complement plays while the loop is measured.</summary>
    public TimeSpan Hold { get; init; } = TimeSpan.FromSeconds(120);
}
