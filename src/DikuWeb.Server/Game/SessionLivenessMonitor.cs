using DikuWeb.Engine;
using DikuWeb.Engine.Protocol;

namespace DikuWeb.Server.Game;

/// <summary>
/// Notices clients that have gone quiet, and tells the loop they are link-dead.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because the transport cannot be trusted to say so.</b> A stream ends, and the
/// character goes link-dead, only when a write to the socket fails — and a write into a kernel send
/// buffer succeeds for a very long time after the peer stops acknowledging it. Measured with the
/// load apparatus: a container killed outright was noticed in seven seconds, because tearing down
/// its network sends a reset; a container whose network was severed while it kept running was
/// noticed after <b>sixteen and a half minutes</b>, and putting nginx in front changed that by
/// twenty-one seconds (PLAN.md §11). Both were waiting on the kernel giving up its
/// retransmissions, which is the only clock in that story.
/// </para>
/// <para>
/// Seventeen minutes of a character standing in a room, broadcast to, regenerated and ticked, and
/// looking to everyone else like somebody idle rather than somebody dropped — because §3.6's grace
/// window had not started, since nothing had noticed there was anything to grace.
/// </para>
/// <para>
/// So the client says it is there instead, and this reaps the sessions that stop saying it. What
/// it submits is an ordinary <see cref="LeaveReason.LinkDead"/>, so everything downstream is
/// unchanged: the character stays put for the grace window and can still be attacked, exactly as
/// if the socket had failed honestly.
/// </para>
/// </remarks>
public sealed class SessionLivenessMonitor(
    SessionRegistry sessions,
    SessionRegistryOptions options,
    GameGateway gateway,
    TimeProvider clock,
    ILogger<SessionLivenessMonitor> logger) : BackgroundService
{
    /// <summary>
    /// How often to sweep.
    /// </summary>
    /// <remarks>
    /// A third of the shortest sensible timeout, so the delay this adds is small beside the
    /// timeout itself and the work is nothing: a walk over a few hundred sessions comparing two
    /// timestamps, ten times a minute.
    /// </remarks>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.HeartbeatTimeoutSeconds <= 0)
        {
            // The escape hatch, and it is worth a line in the log: a deployment that has turned
            // this off should be able to find out that it did.
            ServerLog.LivenessSweepDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                Sweep();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A sweep that throws must not take the sweeper down with it, or one bad session
                // would silently restore the seventeen-minute behaviour for every other player.
                ServerLog.LivenessSweepFailed(logger, ex);
            }
        }
    }

    /// <summary>
    /// One pass over the sessions. Internal so a test can drive it against a stopped clock rather
    /// than waiting out real minutes.
    /// </summary>
    internal void Sweep()
    {
        var timeout = TimeSpan.FromSeconds(options.HeartbeatTimeoutSeconds);

        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        var now = clock.GetUtcNow();

        foreach (var session in sessions.All)
        {
            // Only sessions that have proved they know how to say they are alive. A client from
            // before heartbeats existed sends none, and reaping it for that would throw a healthy
            // player out of the world for running a cached build. Those keep today's behaviour.
            if (!session.SendsHeartbeats)
            {
                continue;
            }

            if (now - session.LastSeenAt < timeout)
            {
                continue;
            }

            // Submitted once per session, because the loop leaves the session in the registry for
            // the grace window and this would otherwise re-send on every sweep for ninety seconds.
            // The loop ignores a repeat, but the log would be a lie about how often this happens.
            if (!session.MarkReaped())
            {
                continue;
            }

            ServerLog.SessionWentQuiet(
                logger, session.CharacterName, (now - session.LastSeenAt).TotalSeconds);

            gateway.TrySubmit(new LeaveWorld
            {
                SessionId = session.Id,
                Reason = LeaveReason.LinkDead,
            });
        }
    }
}
