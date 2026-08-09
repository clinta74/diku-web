using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;

namespace DikuWeb.Server.Tests;

/// <summary>
/// The §4.4 difficulty dial, through the real HTTP stack, the real loop, and a real PostgreSQL.
/// </summary>
/// <remarks>
/// This is the layer the bug lived in. The primitives carried no multipliers and `WorldWriter`
/// mentioned the word nowhere, so a save returned 200, the loop applied a change containing
/// nothing, and the numbers never reached a column. Only a round trip that *reads back* catches
/// that — asserting on the response of the write would have passed throughout.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class MultiplierApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    [Fact]
    public async Task A_zone_multiplier_survives_a_patch_and_a_reload()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);

        var patch = await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            multipliers = new { xp = 2.5, gold = 3.0, strength = 1.5 },
        });
        patch.EnsureSuccessStatusCode();

        var zone = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}", UriKind.Relative)));
        var multipliers = zone.GetProperty("multipliers");

        Assert.Equal(2.5m, multipliers.GetProperty("xp").GetDecimal());
        Assert.Equal(3.0m, multipliers.GetProperty("gold").GetDecimal());
        Assert.Equal(1.5m, multipliers.GetProperty("strength").GetDecimal());
    }

    [Fact]
    public async Task A_world_multiplier_survives_a_patch_and_a_reload()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (worldKey, _) = await BuilderClient.NewZoneAsync(client);

        (await client.PatchAsJsonAsync($"/api/builder/worlds/{worldKey}", new
        {
            multipliers = new { itemPower = 4.0 },
        })).EnsureSuccessStatusCode();

        var world = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/worlds/{worldKey}", UriKind.Relative)));

        Assert.Equal(4.0m, world.GetProperty("multipliers").GetProperty("itemPower").GetDecimal());
    }

    [Fact]
    public async Task A_new_zone_starts_neutral()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);

        var zone = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}", UriKind.Relative)));
        var multipliers = zone.GetProperty("multipliers");

        Assert.Equal(1m, multipliers.GetProperty("xp").GetDecimal());
        Assert.Equal(1m, multipliers.GetProperty("spawnDensity").GetDecimal());
    }

    /// <summary>
    /// A PATCH that does not mention multipliers must not reset them. They are stored as one
    /// jsonb object, so "unspecified" and "all defaults" look identical unless the endpoint
    /// distinguishes a null request field from an empty one.
    /// </summary>
    [Fact]
    public async Task Patching_an_unrelated_field_leaves_the_multipliers_alone()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);

        (await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            multipliers = new { xp = 7.0 },
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            name = "Renamed Zone",
        })).EnsureSuccessStatusCode();

        var zone = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}", UriKind.Relative)));

        Assert.Equal("Renamed Zone", zone.GetProperty("name").GetString());
        Assert.Equal(7.0m, zone.GetProperty("multipliers").GetProperty("xp").GetDecimal());
    }

    [Fact]
    public async Task The_preview_endpoint_reports_the_zones_current_multipliers()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);

        (await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            multipliers = new { xp = 2.0 },
        })).EnsureSuccessStatusCode();

        var preview = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/preview", UriKind.Relative)));

        // The preview's multiplier map is keyed in camelCase, matching the wire shape of the
        // typed Multipliers object the editor sends back.
        Assert.Equal(2.0m, preview.GetProperty("zoneMultipliers").GetProperty("xp").GetDecimal());
    }
}
