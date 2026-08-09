using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

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
    private DikuWebAppFactory? _strict;

    private DikuWebAppFactory Strict => _strict ??= new DikuWebAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["RateLimits:CommandBurst"] = "20",
            ["RateLimits:CommandsPerSecond"] = "5",
            ["RateLimits:AuthAttemptsPerMinute"] = "40",
        });

    public void Dispose() => _strict?.Dispose();

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
}
