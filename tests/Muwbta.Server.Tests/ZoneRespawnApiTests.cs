using System.Net;
using System.Net.Http.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// The <em>Respawn zone</em> button, over the wire (PLAN.md §7.5).
/// </summary>
/// <remarks>
/// <para>
/// The endpoint was listed in §7.3 from Phase 3 and never mapped, which is a shape this codebase
/// has been caught by before: a designed route nothing routes to reads as built to everyone except
/// the person who tries it. So the first thing asserted is that a POST to it is not a 404.
/// </para>
/// <para>
/// It is also the only builder write with nothing to persist - mobs live in memory alone - so it
/// is the only one whose result cannot be read back from a row. The counts in the response are
/// therefore the assertion, and they come from the loop that did the work.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ZoneRespawnApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>A zone with one room and one mob spawner in it, filled to <paramref name="target"/>.</summary>
    private async Task<(HttpClient Client, string ZoneKey)> PopulatedZoneAsync(int target = 2)
    {
        var factory = postgres.App;
        var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "burrow");
        var templateKey = BuilderClient.UniqueName("rat").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{templateKey}", new
        {
            name = "a rat",
            level = 3,
            baseStats = new { health = 40 },
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey,
            templateKind = "Mob",
            roomKeys = new[] { roomKey },
            targetCount = target,

            // Long enough that the 15-second sweep can never be what refilled the zone, so the
            // counts below are this endpoint's work and nothing else's.
            respawnSeconds = 3600,
        })).EnsureSuccessStatusCode();

        return (client, zoneKey);
    }

    private static async Task<HttpResponseMessage> RespawnAsync(HttpClient client, string zoneKey) =>
        await client.PostAsync(new Uri($"/api/builder/zones/{zoneKey}/respawn", UriKind.Relative), null);

    [Fact]
    public async Task A_respawn_fills_the_zone_to_its_spawners_targets()
    {
        var (client, zoneKey) = await PopulatedZoneAsync(target: 2);
        using var _ = client;

        var body = await BuilderClient.JsonAsync(await RespawnAsync(client, zoneKey));

        Assert.Equal(zoneKey, body.GetProperty("zoneKey").GetString());
        Assert.Equal(2, body.GetProperty("spawned").GetInt32());
    }

    /// <summary>
    /// The second call is the one that proves the "despawn" half: the zone is known to be at its
    /// target, so everything standing in it came out and the same number went back.
    /// </summary>
    [Fact]
    public async Task A_second_respawn_replaces_what_the_first_one_placed()
    {
        var (client, zoneKey) = await PopulatedZoneAsync(target: 3);
        using var _ = client;

        (await RespawnAsync(client, zoneKey)).EnsureSuccessStatusCode();
        var body = await BuilderClient.JsonAsync(await RespawnAsync(client, zoneKey));

        Assert.Equal(3, body.GetProperty("despawned").GetInt32());
        Assert.Equal(3, body.GetProperty("spawned").GetInt32());
    }

    [Fact]
    public async Task A_zone_that_does_not_exist_is_a_404()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await RespawnAsync(client, "test.nowhere-at-all");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
