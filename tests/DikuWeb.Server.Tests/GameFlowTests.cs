using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// End-to-end through the real HTTP stack, the real game loop, and a real PostgreSQL.
/// Nothing is stubbed, so these exercise the pulse cadence and the SSE framing as shipped.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GameFlowTests(PostgresFixture postgres)
{
    /// <summary>Generous: the loop drains commands on a 250 ms pulse and CI is slower.</summary>
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private DikuWebAppFactory NewFactory() => new(postgres.ConnectionString);

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>
    /// Letters only, because character names are validated against ^[A-Za-z]{3,16}$ - a
    /// numeric suffix would be rejected with a 400.
    /// </summary>
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

    private static async Task<SseStream> OpenStreamAsync(HttpClient client, Guid characterId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        return new SseStream(await response.Content.ReadAsStreamAsync());
    }

    [Fact]
    public async Task Entering_the_world_streams_the_opening_panels()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Kael"));
        var enter = await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });
        Assert.Equal(HttpStatusCode.OK, enter.StatusCode);

        await using var stream = await OpenStreamAsync(client, characterId);
        var frames = await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room") && f.Any(x => x.EventType == "map"),
            EventTimeout);

        Assert.Contains(frames, f => f.EventType == "sys");
        Assert.Contains(frames, f => f.EventType == "vitals");

        var room = Assert.Single(frames, f => f.EventType == "room");
        Assert.Equal("aldenmoor.millbrook.north-gate", room.Json.GetProperty("key").GetString());

        var map = frames.First(f => f.EventType == "map");
        Assert.True(map.Json.GetProperty("w").GetInt32() > 0);
        Assert.Equal(1, map.Json.GetProperty("entities").GetArrayLength());
    }

    [Fact]
    public async Task Event_ids_are_monotonic()
    {
        // Last-Event-ID replay depends on this ordering being reliable (PLAN.md §3.4).
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Mono"));
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        await using var stream = await OpenStreamAsync(client, characterId);
        var frames = await stream.ReadUntilAsync(f => f.Count >= 4, EventTimeout);

        var ids = frames.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.OrderBy(i => i), ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task A_command_produces_output_on_the_stream_not_in_its_own_response()
    {
        // PLAN.md §3.3: the POST answers 202 with an empty body so there is exactly one
        // ordered output channel and the scrollback can never interleave wrongly.
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Walk"));
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        await using var stream = await OpenStreamAsync(client, characterId);

        // Drain the entry burst first so the assertion below is about the command.
        await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        var command = await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = "east" });

        Assert.Equal(HttpStatusCode.Accepted, command.StatusCode);
        Assert.Empty(await command.Content.ReadAsStringAsync());

        // Stop on the destination room rather than the movement text: the text is emitted
        // first, so stopping there would end the read before the room event arrived.
        var frames = await stream.ReadUntilAsync(
            f => f.Any(x =>
                x.EventType == "room"
                && x.Json.GetProperty("key").GetString() == "aldenmoor.millbrook.market-row"),
            EventTimeout);

        Assert.True(frames.HasText("You walk east"), "Movement text never arrived on the stream.");
        Assert.Contains(frames, f =>
            f.EventType == "room"
            && f.Json.GetProperty("key").GetString() == "aldenmoor.millbrook.market-row");
    }

    [Fact]
    public async Task A_dangling_or_missing_exit_fails_closed()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Wall"));
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        await using var stream = await OpenStreamAsync(client, characterId);
        await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        // The north gate has no "down" exit.
        await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = "down" });

        var frames = await stream.ReadUntilAsync(f => f.HasText("cannot go down"),
            EventTimeout);

        Assert.True(frames.HasText("cannot go down"), "Expected a refusal, and the loop to survive it.");
    }

    [Fact]
    public async Task Two_players_in_a_room_see_each_other()
    {
        // The Phase 1 acceptance criterion in miniature.
        using var factory = NewFactory();
        using var kael = NewClient(factory);
        using var mira = NewClient(factory);

        var kaelId = await CreatePlayerAsync(kael, UniqueName("Kae"));
        var miraId = await CreatePlayerAsync(mira, UniqueName("Mir"));

        await kael.PostAsJsonAsync($"/api/game/{kaelId}/enter", new { });
        await using var kaelStream = await OpenStreamAsync(kael, kaelId);
        await kaelStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await mira.PostAsJsonAsync($"/api/game/{miraId}/enter", new { });
        await using var miraStream = await OpenStreamAsync(mira, miraId);
        await miraStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await mira.PostAsJsonAsync($"/api/game/{miraId}/command", new { input = "say hello there" });

        var heard = await kaelStream.ReadUntilAsync(f => f.HasText("hello there"), EventTimeout);
        Assert.True(heard.HasText("hello there"), "Kael never heard Mira speak.");

        // And both should appear on the map.
        var maps = heard.Where(f => f.EventType == "map").ToList();
        Assert.Contains(maps, m => m.Json.GetProperty("entities").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Commands_are_rejected_before_entering_the_world()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Idle"));

        var response = await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = "look" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Opening_a_stream_before_entering_is_a_conflict()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var characterId = await CreatePlayerAsync(client, UniqueName("Prem"));

        var response = await client.GetAsync(new Uri($"/api/game/{characterId}/stream", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_character_belonging_to_another_account_cannot_be_entered()
    {
        using var factory = NewFactory();
        using var owner = NewClient(factory);
        using var intruder = NewClient(factory);

        var characterId = await CreatePlayerAsync(owner, UniqueName("Own"));
        await CreatePlayerAsync(intruder, UniqueName("Int"));

        var response = await intruder.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
