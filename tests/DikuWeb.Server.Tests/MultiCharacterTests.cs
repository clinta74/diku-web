using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// One account driving several characters at once. Sessions are keyed by character rather
/// than by account, so each gets its own stream, scrollback, and link-dead window.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class MultiCharacterTests(PostgresFixture postgres)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private DikuWebAppFactory NewFactory() => new(postgres.ConnectionString);

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return prefix + new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
    }

    private static async Task RegisterAsync(HttpClient client)
    {
        var username = UniqueName("acct").ToLowerInvariant();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateCharacterAsync(HttpClient client, string prefix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/characters",
            new { name = UniqueName(prefix), path = "Warden" });
        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<SseStream> OpenStreamAsync(HttpClient client, Guid characterId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return new SseStream(await response.Content.ReadAsStreamAsync());
    }

    [Fact]
    public async Task One_account_can_hold_two_characters_in_the_world_at_once()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await RegisterAsync(client);

        var first = await CreateCharacterAsync(client, "Alpha");
        var second = await CreateCharacterAsync(client, "Beta");

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/game/{first}/enter", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/game/{second}/enter", new { })).StatusCode);

        var sessions = await client.GetFromJsonAsync<JsonElement>("/api/game/sessions");
        Assert.Equal(2, sessions.GetArrayLength());
    }

    [Fact]
    public async Task Each_character_gets_its_own_stream()
    {
        // The regression this whole change exists to prevent: entering a second character
        // used to evict the first, because sessions were keyed by account.
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await RegisterAsync(client);

        var first = await CreateCharacterAsync(client, "Alpha");
        var second = await CreateCharacterAsync(client, "Beta");

        await client.PostAsJsonAsync($"/api/game/{first}/enter", new { });
        await using var firstStream = await OpenStreamAsync(client, first);
        await firstStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await client.PostAsJsonAsync($"/api/game/{second}/enter", new { });
        await using var secondStream = await OpenStreamAsync(client, second);
        await secondStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        // The first stream must still be live: send it a command and expect its own output.
        await client.PostAsJsonAsync($"/api/game/{first}/command", new { input = "east" });

        var frames = await firstStream.ReadUntilAsync(f => f.HasText("You walk east"), EventTimeout);
        Assert.True(frames.HasText("You walk east"), "The first character's stream was evicted.");
    }

    [Fact]
    public async Task Two_characters_on_one_account_can_see_and_hear_each_other()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await RegisterAsync(client);

        var first = await CreateCharacterAsync(client, "Alpha");
        var second = await CreateCharacterAsync(client, "Beta");

        await client.PostAsJsonAsync($"/api/game/{first}/enter", new { });
        await using var firstStream = await OpenStreamAsync(client, first);
        await firstStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await client.PostAsJsonAsync($"/api/game/{second}/enter", new { });
        await using var secondStream = await OpenStreamAsync(client, second);
        await secondStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await client.PostAsJsonAsync($"/api/game/{second}/command", new { input = "say talking to myself" });

        var heard = await firstStream.ReadUntilAsync(f => f.HasText("talking to myself"), EventTimeout);
        Assert.True(heard.HasText("talking to myself"), "Same-account characters could not hear each other.");
    }

    [Fact]
    public async Task Commands_are_routed_to_the_named_character_only()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await RegisterAsync(client);

        var walker = await CreateCharacterAsync(client, "Walk");
        var stayer = await CreateCharacterAsync(client, "Stay");

        await client.PostAsJsonAsync($"/api/game/{walker}/enter", new { });
        await client.PostAsJsonAsync($"/api/game/{stayer}/enter", new { });

        await using var walkerStream = await OpenStreamAsync(client, walker);
        await using var stayerStream = await OpenStreamAsync(client, stayer);
        await walkerStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);
        await stayerStream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await client.PostAsJsonAsync($"/api/game/{walker}/command", new { input = "east" });

        var walkerFrames = await walkerStream.ReadUntilAsync(f => f.HasText("You walk east"), EventTimeout);
        Assert.True(walkerFrames.HasText("You walk east"));

        // The stayer should have seen the departure, but never "You walk east" - that is the
        // walker's own second-person echo and must not leak across characters.
        var stayerFrames = await stayerStream.ReadUntilAsync(f => f.HasText("leaves east"), EventTimeout);
        Assert.True(stayerFrames.HasText("leaves east"), "The other character never saw the departure.");
        Assert.False(stayerFrames.HasText("You walk east"), "Second-person output leaked to another character.");
    }

    [Fact]
    public async Task A_character_on_another_account_cannot_be_streamed()
    {
        using var factory = NewFactory();
        using var owner = NewClient(factory);
        using var intruder = NewClient(factory);

        await RegisterAsync(owner);
        await RegisterAsync(intruder);

        var characterId = await CreateCharacterAsync(owner, "Own");
        await owner.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        // The session exists and the id is correct, so this is exactly the case where a
        // registry keyed only by character id would leak someone else's stream.
        var stream = await intruder.GetAsync(new Uri($"/api/game/{characterId}/stream", UriKind.Relative));
        var command = await intruder.PostAsJsonAsync(
            $"/api/game/{characterId}/command",
            new { input = "look" });

        Assert.Equal(HttpStatusCode.Conflict, stream.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, command.StatusCode);
    }

    [Fact]
    public async Task Sessions_listing_shows_only_this_accounts_characters()
    {
        using var factory = NewFactory();
        using var mine = NewClient(factory);
        using var theirs = NewClient(factory);

        await RegisterAsync(mine);
        await RegisterAsync(theirs);

        var myCharacter = await CreateCharacterAsync(mine, "Mine");
        var theirCharacter = await CreateCharacterAsync(theirs, "Thei");

        await mine.PostAsJsonAsync($"/api/game/{myCharacter}/enter", new { });
        await theirs.PostAsJsonAsync($"/api/game/{theirCharacter}/enter", new { });

        var sessions = await mine.GetFromJsonAsync<JsonElement>("/api/game/sessions");

        Assert.Equal(1, sessions.GetArrayLength());
        Assert.Equal(myCharacter, sessions[0].GetProperty("characterId").GetGuid());
    }

    [Fact]
    public async Task The_per_account_cap_is_enforced()
    {
        // Each character holds an open SSE connection and a ring buffer, so an uncapped
        // account could exhaust server resources by looping over its character list.
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await RegisterAsync(client);

        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add(await CreateCharacterAsync(client, $"Ch{(char)('a' + i)}"));
        }

        var statuses = new List<HttpStatusCode>();
        foreach (var id in ids)
        {
            var response = await client.PostAsJsonAsync($"/api/game/{id}/enter", new { });
            statuses.Add(response.StatusCode);
        }

        // Default cap is 3.
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.Conflict, statuses[^1]);
    }

    [Fact]
    public async Task Leaving_frees_a_slot_against_the_cap()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await RegisterAsync(client);

        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add(await CreateCharacterAsync(client, $"Fr{(char)('a' + i)}"));
        }

        foreach (var id in ids.Take(3))
        {
            await client.PostAsJsonAsync($"/api/game/{id}/enter", new { });
        }

        var blocked = await client.PostAsJsonAsync($"/api/game/{ids[3]}/enter", new { });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        await client.PostAsync(new Uri($"/api/game/{ids[0]}/leave", UriKind.Relative), null);

        var allowed = await client.PostAsJsonAsync($"/api/game/{ids[3]}/enter", new { });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Re_entering_the_same_character_does_not_consume_another_slot()
    {
        // A reconnect is not a new presence. Counting it against the cap would lock a player
        // out of their own character after a few flaky connections.
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await RegisterAsync(client);

        var characterId = await CreateCharacterAsync(client, "Redo");

        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var sessions = await client.GetFromJsonAsync<JsonElement>("/api/game/sessions");
        Assert.Equal(1, sessions.GetArrayLength());
    }
}
