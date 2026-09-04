using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// What one authenticated caller can cost the process (PLAN.md §8, Phase 6, and the §10 risk row
/// for command flooding).
/// </summary>
/// <remarks>
/// The game loop is a single thread, so a player who can enqueue faster than it drains is not
/// only hurting themselves. These assert the limits fire, that they fire per caller rather than
/// globally, and that the surfaces which must never be limited are not.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class RateLimitTests(PostgresFixture postgres) : IDisposable
{
    /// <summary>
    /// Its own host, because the shared one has the limits lifted out of the way.
    /// </summary>
    /// <remarks>
    /// One host serves the whole collection from a single loopback address, so leaving the
    /// shipped limits in place would have every other test competing for the same auth partition
    /// — the suite would fail on registration during setup, in tests that are not about rate
    /// limiting at all. Asserting against a host with real numbers is the point of this file, so
    /// this is where they live.
    ///
    /// Sign-ins are given room the command limit is not: each test here registers an account
    /// before it can flood anything, and a limiter that refused the setup would be untestable.
    /// </remarks>
    private MuwbtaAppFactory? _strict;

    private MuwbtaAppFactory Strict => _strict ??= new MuwbtaAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["RateLimits:CommandBurst"] = "20",
            ["RateLimits:CommandsPerSecond"] = "5",
            ["RateLimits:AuthAttemptsPerMinute"] = "40",
        });

    public void Dispose()
    {
        _strict?.Dispose();
        _assist?.Dispose();
        _behindProxy?.Dispose();
        _notBehindProxy?.Dispose();
    }

    /// <summary>
    /// A host that trusts loopback as a proxy, which is what the test server's connection reads as.
    /// </summary>
    /// <remarks>
    /// Five sign-in attempts a minute, so exhausting one forwarded address takes six requests
    /// rather than forty-one. Behind a trusted proxy the auth limiter must partition by the
    /// address in <c>X-Forwarded-For</c>, because the connection's own address is the proxy's for
    /// every caller — and a limit keyed on that is a site-wide cap that one stranger can spend
    /// for everybody.
    /// </remarks>
    private MuwbtaAppFactory? _behindProxy;

    private MuwbtaAppFactory BehindProxy => _behindProxy ??= new MuwbtaAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["RateLimits:AuthAttemptsPerMinute"] = "5",
            ["Proxy:KnownProxies"] = "127.0.0.1",
        });

    /// <summary>The same limits with nothing trusted, so the header must be ignored.</summary>
    private MuwbtaAppFactory? _notBehindProxy;

    private MuwbtaAppFactory NotBehindProxy => _notBehindProxy ??= new MuwbtaAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["RateLimits:AuthAttemptsPerMinute"] = "5",
        });

    [Fact]
    public async Task Behind_a_trusted_proxy_failed_logins_are_limited_per_forwarded_address()
    {
        var factory = BehindProxy;
        using var client = NewClient(factory);

        // Six guesses from one address: past the five-a-minute budget, so the last is refused.
        var fromFirst = await FailedLoginsAsync(client, forwardedFor: "203.0.113.10", count: 6);
        Assert.Equal(HttpStatusCode.TooManyRequests, fromFirst[^1]);

        // A different caller, through the same proxy, has a budget of their own. Refusing them
        // here is exactly the site-wide lockout the trust list exists to prevent.
        var fromSecond = await FailedLoginsAsync(client, forwardedFor: "203.0.113.11", count: 1);
        Assert.Equal(HttpStatusCode.Unauthorized, fromSecond[0]);
    }

    [Fact]
    public async Task With_nothing_trusted_the_forwarded_address_is_ignored()
    {
        // The dangerous configuration is the opposite one: believing the header from anyone. A
        // caller who could reach the port directly would then set a fresh address on every
        // request and never be limited at all. With no proxy trusted, two callers claiming
        // different addresses land in the same partition - the connection's - and the second
        // inherits the first's exhaustion.
        var factory = NotBehindProxy;
        using var client = NewClient(factory);

        var fromFirst = await FailedLoginsAsync(client, forwardedFor: "203.0.113.10", count: 6);
        Assert.Equal(HttpStatusCode.TooManyRequests, fromFirst[^1]);

        var fromSecond = await FailedLoginsAsync(client, forwardedFor: "203.0.113.11", count: 1);
        Assert.Equal(HttpStatusCode.TooManyRequests, fromSecond[0]);
    }

    private static async Task<List<HttpStatusCode>> FailedLoginsAsync(
        HttpClient client,
        string forwardedFor,
        int count)
    {
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < count; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { username = "nobody-at-all", password = "guess" }),
            };
            request.Headers.Add("X-Forwarded-For", forwardedFor);

            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var suffix = new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
        return prefix + suffix;
    }

    private static async Task<Guid> CreatePlayerAsync(HttpClient client, string name)
    {
        var username = name.ToLowerInvariant();

        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });
        registration.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/characters", new { name, path = "Warden" });
        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_command_flood_is_refused_once_the_bucket_empties()
    {
        var factory = Strict;
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Flood"));
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        var statuses = new List<HttpStatusCode>();

        // Well past the twenty-token burst, sent as fast as the socket allows. A human typing
        // cannot reach this; a held-down key or a script trivially can.
        for (var i = 0; i < 60; i++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/game/{characterId}/command", new { input = "look" });

            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // The early ones still worked: a limit that refused from the first request would be a
        // broken game rather than a protected one.
        Assert.Equal(HttpStatusCode.Accepted, statuses[0]);
    }

    [Fact]
    public async Task A_refusal_says_how_long_to_wait()
    {
        // A client that retries immediately on a 429 turns one breach into a tight loop, which is
        // the behaviour being limited in the first place.
        var factory = Strict;
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Retry"));
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        HttpResponseMessage? refused = null;

        for (var i = 0; i < 60 && refused is null; i++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/game/{characterId}/command", new { input = "look" });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                refused = response;
            }
        }

        Assert.NotNull(refused);
        Assert.True(refused.Headers.Contains("Retry-After"), "A 429 must say when to try again.");
    }

    [Fact]
    public async Task One_players_flood_does_not_refuse_another_player()
    {
        // The load-bearing property. A limiter partitioned globally would let any one player
        // switch the game off for everybody, which is worse than the flood it prevents.
        var factory = Strict;

        using var noisy = NewClient(factory);
        using var quiet = NewClient(factory);

        var noisyId = await CreatePlayerAsync(noisy, UniqueName("Noisy"));
        var quietId = await CreatePlayerAsync(quiet, UniqueName("Quiet"));

        await noisy.PostAsJsonAsync($"/api/game/{noisyId}/enter", new { });
        await quiet.PostAsJsonAsync($"/api/game/{quietId}/enter", new { });

        for (var i = 0; i < 60; i++)
        {
            await noisy.PostAsJsonAsync($"/api/game/{noisyId}/command", new { input = "look" });
        }

        var innocent = await quiet.PostAsJsonAsync(
            $"/api/game/{quietId}/command", new { input = "look" });

        Assert.Equal(HttpStatusCode.Accepted, innocent.StatusCode);
    }

    [Fact]
    public async Task The_event_stream_is_never_limited()
    {
        // One long-lived request per character. A limiter here would never fire on honest use and
        // would be a way to break a session that had reconnected a few times in quick succession.
        var factory = Strict;
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Stream"));
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        for (var i = 0; i < 15; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Repeated_failed_logins_are_refused()
    {
        // The only surface a stranger can reach, and the only one where the limit is tight.
        var factory = Strict;
        using var client = NewClient(factory);

        var statuses = new List<HttpStatusCode>();

        // Past the forty-a-minute budget this host is configured with. A password guesser wants
        // orders of magnitude more than this; a person who has forgotten their password wants
        // three or four.
        for (var i = 0; i < 60; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "nobody-at-all",
                password = "guess",
            });

            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
    /// <summary>
    /// Its own host again, with the assist on and its budget tiny.
    /// </summary>
    /// <remarks>
    /// The assist is off unless configured, which is the right default and makes it invisible to
    /// every other test. Turning it on here needs no model behind it: these assert who is charged
    /// what, and a job that then fails for want of an Ollama is a job that was still accepted.
    /// </remarks>
    private MuwbtaAppFactory? _assist;

    private MuwbtaAppFactory Assist => _assist ??= new MuwbtaAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["Assist:Enabled"] = "true",
            ["RateLimits:AuthAttemptsPerMinute"] = "40",
            // Two, so the third submission is refused and the test does not have to make six.
            ["RateLimits:AssistRequestsPerMinute"] = "2",
        });

    /// <summary>
    /// Reading a job back is not charged the price of asking for one.
    /// </summary>
    /// <remarks>
    /// <b>This is a regression test for a bug that reached a running server.</b> The assist policy
    /// was applied to the whole endpoint group, so the client's own progress checks were spending
    /// the submission budget: one POST and five GETs, and the sixth request came back 429 while the
    /// draft it was asking about was still running — and went on to finish. The builder was shown a
    /// failure for a job that had succeeded.
    /// <para>
    /// The two calls cost wildly different things. Submitting occupies the only model this server
    /// has for minutes; reading a job back is a dictionary lookup. Charging them the same made the
    /// cheap one impossible long before the expensive one was.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Reading_a_job_is_not_charged_the_submission_budget()
    {
        using var client = NewClient(Assist);
        await BuilderClient.RegisterBuilderAsync(Assist, client);

        // Never queued, so this is purely about who the limiter charges. A 404 is the honest
        // answer and, crucially, is an answer rather than a refusal.
        var id = Guid.NewGuid();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 12; i++)
        {
            using var response = await client.GetAsync(
                new Uri($"/api/builder/assist/rooms/{id}", UriKind.Relative));

            statuses.Add(response.StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.NotFound, s));
    }

    /// <summary>And the expensive call still is charged, at the number configured.</summary>
    /// <remarks>
    /// The other half, because a fix that stopped limiting the reads by removing the limit
    /// altogether would pass the test above and leave one builder able to fill the queue.
    /// </remarks>
    [Fact]
    public async Task Asking_for_a_draft_is_still_limited()
    {
        using var client = NewClient(Assist);
        await BuilderClient.RegisterBuilderAsync(Assist, client);

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/builder/assist/rooms",
                new { zoneKey = "nowhere.at-all", roomKey = "nowhere.at-all.a-room" });

            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
