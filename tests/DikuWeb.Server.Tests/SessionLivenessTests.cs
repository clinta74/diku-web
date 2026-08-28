using System.Net;
using System.Net.Http.Json;
using DikuWeb.Engine;
using DikuWeb.Engine.Protocol;
using DikuWeb.Server.Game;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DikuWeb.Server.Tests;

/// <summary>
/// A client that stops saying it is there is treated as gone.
/// </summary>
/// <remarks>
/// <para>
/// The transport cannot answer this. A write into a kernel send buffer succeeds long after the
/// peer has stopped acknowledging it, so a stream only ends when the kernel gives up
/// retransmitting — measured at sixteen and a half minutes for a client whose network vanished
/// silently, and putting nginx in front changed it by twenty-one seconds (PLAN.md §11).
/// </para>
/// <para>
/// These use a fake clock rather than waiting, so the sweep is exercised in milliseconds. The
/// end-to-end behaviour has its own coverage in the load apparatus, which is where the sixteen
/// minutes was measured in the first place.
/// </para>
/// </remarks>
public sealed class SessionLivenessTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A clock that only moves when a test says so.
    /// </summary>
    /// <remarks>
    /// Six lines rather than a package. The alternative is Microsoft.Extensions.TimeProvider.Testing
    /// for one <c>Advance</c>, and this repo's dependency list is short on purpose.
    /// </remarks>
    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static (SessionRegistry Sessions, GameGateway Gateway, StoppedClock Clock, SessionLivenessMonitor Monitor)
        Build(int timeoutSeconds = 60)
    {
        var options = new SessionRegistryOptions { HeartbeatTimeoutSeconds = timeoutSeconds };
        var sessions = new SessionRegistry(options);
        var gateway = new GameGateway(new EngineOptions());
        var clock = new StoppedClock(Start);

        return (
            sessions,
            gateway,
            clock,
            new SessionLivenessMonitor(
                sessions, options, gateway, clock, NullLogger<SessionLivenessMonitor>.Instance));
    }

    private static GameSession Enter(SessionRegistry sessions, string name = "Kael") =>
        sessions.Open(Guid.NewGuid(), Guid.NewGuid(), name).Session!;


    [Fact]
    public void A_client_that_stops_beating_is_reaped()
    {
        var (sessions, _, clock, monitor) = Build();
        var session = Enter(sessions);

        session.Seen(clock.GetUtcNow(), heartbeat: true);
        clock.Advance(TimeSpan.FromSeconds(61));

        monitor.Sweep();

        // What the monitor submits is an ordinary LeaveWorld carrying LeaveReason.LinkDead, so
        // §3.6's grace window still applies and a player whose phone died comes back to their
        // character standing where they left it. What is asserted here is who was given up on and
        // when - the loop's handling of LinkDead has its own coverage in the Engine suite.
        Assert.True(session.IsReaped);
    }

    [Fact]
    public void A_client_that_keeps_beating_is_left_alone()
    {
        var (sessions, _, clock, monitor) = Build();
        var session = Enter(sessions);

        // Three intervals of a well-behaved client.
        for (var i = 0; i < 3; i++)
        {
            session.Seen(clock.GetUtcNow(), heartbeat: true);
            clock.Advance(TimeSpan.FromSeconds(20));
            monitor.Sweep();
        }

        Assert.False(session.IsReaped);
    }

    [Fact]
    public void A_client_that_has_never_beaten_is_never_reaped()
    {
        // The migration hinge. A browser running a cached build from before heartbeats existed
        // sends none, and throwing that player out of the world for it would be a regression
        // introduced by a fix. Those sessions keep exactly today's behaviour.
        var (sessions, _, clock, monitor) = Build();
        var session = Enter(sessions);

        clock.Advance(TimeSpan.FromHours(1));
        monitor.Sweep();

        Assert.False(session.IsReaped);
    }

    [Fact]
    public void A_reaped_session_is_only_reported_once()
    {
        // The loop keeps the session in the registry for the ninety-second grace window, so
        // without the claim the sweep would re-submit every six seconds and the log would report
        // fifteen disconnections where one happened.
        var (sessions, _, clock, monitor) = Build();
        var session = Enter(sessions);

        session.Seen(clock.GetUtcNow(), heartbeat: true);
        clock.Advance(TimeSpan.FromSeconds(61));

        monitor.Sweep();
        monitor.Sweep();
        monitor.Sweep();

        Assert.True(session.IsReaped);

        // The claim is what stops the re-submission: having already been taken, it refuses to be
        // taken again, so the second and third sweeps found nothing to report.
        Assert.False(session.MarkReaped());
    }

    [Fact]
    public void One_quiet_client_does_not_take_the_others_with_it()
    {
        var (sessions, _, clock, monitor) = Build();
        var quiet = Enter(sessions, "Quiet");
        var talkative = Enter(sessions, "Talkative");

        quiet.Seen(clock.GetUtcNow(), heartbeat: true);
        talkative.Seen(clock.GetUtcNow(), heartbeat: true);

        clock.Advance(TimeSpan.FromSeconds(61));
        talkative.Seen(clock.GetUtcNow(), heartbeat: true);

        monitor.Sweep();

        Assert.True(quiet.IsReaped);
        Assert.False(talkative.IsReaped);
    }

    [Fact]
    public void A_command_counts_as_being_there_but_is_not_a_promise_to_keep_typing()
    {
        // Typing proves somebody is present, so it postpones the deadline. It must not *create*
        // one: a client that has never sent a heartbeat has not agreed to meet a deadline, and
        // holding it to one because it happened to send a command would reap old clients the
        // moment their player stopped typing.
        var (sessions, _, clock, monitor) = Build();
        var session = Enter(sessions);

        session.Seen(clock.GetUtcNow(), heartbeat: false);
        Assert.False(session.SendsHeartbeats);

        clock.Advance(TimeSpan.FromSeconds(61));
        monitor.Sweep();

        Assert.False(session.IsReaped);
    }

    [Fact]
    public void A_timeout_of_zero_turns_the_sweep_off()
    {
        // The escape hatch, for a deployment where this ever misbehaves.
        var (sessions, _, clock, monitor) = Build(timeoutSeconds: 0);
        var session = Enter(sessions);

        session.Seen(clock.GetUtcNow(), heartbeat: true);
        clock.Advance(TimeSpan.FromHours(1));

        monitor.Sweep();

        Assert.False(session.IsReaped);
    }
}

/// <summary>
/// The heartbeat endpoint, through the real HTTP stack.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class HeartbeatEndpointTests(PostgresFixture postgres)
{
    private static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return prefix + new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
    }

    [Fact]
    public async Task Beating_marks_the_session_seen()
    {
        var factory = postgres.App;
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        var name = UniqueName("Beat");
        var username = name.ToLowerInvariant();

        (await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        })).EnsureSuccessStatusCode();

        var created = await client.PostAsJsonAsync("/api/characters", new { name, path = "Warden" });
        created.EnsureSuccessStatusCode();

        var body = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var characterId = body.RootElement.GetProperty("id").GetGuid();

        // Not in the world yet: beating is a conflict rather than a quiet success, so a client
        // that has lost its session is told to re-enter instead of talking to nothing forever.
        var early = await client.PostAsync($"/api/game/{characterId}/heartbeat", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        (await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { })).EnsureSuccessStatusCode();

        var beat = await client.PostAsync($"/api/game/{characterId}/heartbeat", null);
        Assert.Equal(HttpStatusCode.NoContent, beat.StatusCode);

        var sessions = factory.Services.GetRequiredService<SessionRegistry>();
        var session = Assert.Single(sessions.All, s => s.CharacterId == characterId);

        Assert.True(session.SendsHeartbeats);
    }
}
