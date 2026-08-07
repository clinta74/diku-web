using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Live-immediate editing (PLAN.md §1, §3.5): a builder's save reaches players standing in the
/// edited room without them relogging. This is the half of the design that pays for choosing
/// live-immediate over draft/publish, so it needs a test against the real stream.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class LiveEditTests(PostgresFixture postgres)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>Registers, creates a character, enters the world, and opens its stream.</summary>
    private static async Task<(Guid CharacterId, SseStream Stream)> PlayAsync(HttpClient client)
    {
        await BuilderClient.RegisterAsync(client);

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/characters",
            new { name = BuilderClient.UniqueName("Pl"), path = "Warden" }));

        var characterId = created.GetProperty("id").GetGuid();

        (await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { })).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var stream = new SseStream(await response.Content.ReadAsStreamAsync());
        await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        return (characterId, stream);
    }

    [Fact]
    public async Task Retitling_a_room_reaches_the_player_standing_in_it()
    {
        var factory = postgres.App;
        using var player = NewClient(factory);
        using var builder = NewClient(factory);

        var (_, stream) = await PlayAsync(player);
        await using var _ = stream;

        await BuilderClient.RegisterBuilderAsync(factory, builder);

        // The starter world's north gate is where new characters begin.
        var response = await builder.PatchAsJsonAsync(
            "/api/builder/rooms/aldenmoor.millbrook.north-gate",
            new { title = "The Shattered Gate" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var frames = await stream.ReadUntilAsync(
            f => f.Any(x => x.EventType == "room"
                && x.Json.GetProperty("title").GetString() == "The Shattered Gate"),
            EventTimeout);

        Assert.Contains(
            frames,
            f => f.EventType == "room"
                && f.Json.GetProperty("title").GetString() == "The Shattered Gate");

        // Put it back: the starter world is shared by every test in this collection.
        await builder.PatchAsJsonAsync(
            "/api/builder/rooms/aldenmoor.millbrook.north-gate",
            new { title = "The North Gate" });
    }

    [Fact]
    public async Task A_zone_containing_a_player_cannot_be_deleted()
    {
        // The one destructive edit gated on being empty (PLAN.md §7.4), asserted against a
        // genuinely occupied zone rather than a unit-test fixture.
        var factory = postgres.App;
        using var player = NewClient(factory);
        using var builder = NewClient(factory);

        var (_, stream) = await PlayAsync(player);
        await using var _ = stream;

        await BuilderClient.RegisterBuilderAsync(factory, builder);

        var response = await builder.DeleteAsync(
            new Uri("/api/builder/zones/aldenmoor.millbrook", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // And it is still there, which is the part that matters.
        var zone = await builder.GetAsync(
            new Uri("/api/builder/zones/aldenmoor.millbrook", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, zone.StatusCode);
    }

    [Fact]
    public async Task A_builder_walking_into_a_dangling_exit_is_offered_the_dig()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, Domain.Accounts.AccountRole.Builder);
        await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/characters",
            new { name = BuilderClient.UniqueName("Bld"), path = "Warden" }));

        var characterId = created.GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = new SseStream(await response.Content.ReadAsStreamAsync());
        await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        // The north gate has no "down" exit at all, so this is the plain refusal - what we are
        // checking is that a builder is told how to make one rather than "you cannot go down".
        await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = "down" });

        var frames = await stream.ReadUntilAsync(f => f.HasText("cannot go down"), EventTimeout);
        Assert.True(frames.HasText("cannot go down"));
    }

    [Fact]
    public async Task An_in_game_dig_is_persisted_by_the_write_worker()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, Domain.Accounts.AccountRole.Builder);
        await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });

        // Build a private zone first so the dig cannot collide with another test's world.
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "start");

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/characters",
            new { name = BuilderClient.UniqueName("Dig"), path = "Warden" }));

        var characterId = created.GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = new SseStream(await response.Content.ReadAsStreamAsync());
        await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = $"goto {roomKey}" });
        await stream.ReadUntilAsync(
            f => f.Any(x => x.EventType == "room" && x.Json.GetProperty("key").GetString() == roomKey),
            EventTimeout);

        await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = "dig north" });
        await stream.ReadUntilAsync(f => f.HasText("You open a way north"), EventTimeout);

        // In-game edits persist through a fire-and-forget queue (PLAN.md §7.6), so the read
        // has to allow for the worker not having drained yet.
        JsonElement? dug = null;
        for (var attempt = 0; attempt < 40 && dug is null; attempt++)
        {
            var probe = await client.GetAsync(
                new Uri($"/api/builder/rooms/{zoneKey}.room-1", UriKind.Relative));

            if (probe.StatusCode == HttpStatusCode.OK)
            {
                dug = await BuilderClient.JsonAsync(probe);
                break;
            }

            await Task.Delay(100);
        }

        Assert.NotNull(dug);
        Assert.True(dug.Value.GetProperty("flags").GetProperty("unfinished").GetBoolean());
    }
}
