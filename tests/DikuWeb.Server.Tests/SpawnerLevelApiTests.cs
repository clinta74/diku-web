using System.Net;
using System.Net.Http.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Pinning the level a spawner's mobs fight at, over the wire (PLAN.md §4.7).
/// </summary>
/// <remarks>
/// The wire carries a <em>word</em> — <c>"zone"</c> or a number as text — for the reason
/// <c>wander</c> does: on a PATCH, null already spells "leave this alone", so a nullable number
/// could not also spell "clear the pin". Most of what is asserted here is that distinction holding
/// under the three requests that can be confused with each other.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class SpawnerLevelApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<(HttpClient Client, string Id)> SpawnerAsync(
        string? level = null,
        int templateLevel = 10)
    {
        var factory = postgres.App;
        var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "pit");
        var templateKey = BuilderClient.UniqueName("brute").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{templateKey}", new
        {
            name = "brute",
            level = templateLevel,
            baseStats = new { health = 40 },
        })).EnsureSuccessStatusCode();

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/builder/spawners",
            new
            {
                zoneKey,
                templateKey,
                templateKind = "Mob",
                roomKeys = new[] { roomKey },
                targetCount = 1,
                respawnSeconds = 60,
                level,
            }));

        return (client, created.GetProperty("id").GetString()!);
    }

    private static async Task<System.Text.Json.JsonElement> ReadAsync(HttpClient client, string id) =>
        await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/spawners/{id}", UriKind.Relative)));

    [Fact]
    public async Task A_fresh_spawner_lets_the_zone_decide()
    {
        var (client, id) = await SpawnerAsync();
        using var _ = client;

        var spawner = await ReadAsync(client, id);

        Assert.Equal("zone", spawner.GetProperty("level").GetString());
        Assert.Equal(10, spawner.GetProperty("fightsAtLevel").GetInt32());
    }

    [Fact]
    public async Task A_pin_survives_a_save_and_a_reload()
    {
        var (client, id) = await SpawnerAsync(level: "27");
        using var _ = client;

        var spawner = await ReadAsync(client, id);

        Assert.Equal("27", spawner.GetProperty("level").GetString());
        Assert.Equal(27, spawner.GetProperty("fightsAtLevel").GetInt32());
    }

    [Fact]
    public async Task A_patch_that_omits_the_level_leaves_it_alone()
    {
        // The lesson WanderMode was created for, re-pinned for this field. If `level` were a
        // nullable number, this request would clear the pin - and a builder would lose a setting by
        // editing something else on the same form.
        var (client, id) = await SpawnerAsync(level: "27");
        using var _ = client;

        (await client.PatchAsJsonAsync($"/api/builder/spawners/{id}", new { targetCount = 3 }))
            .EnsureSuccessStatusCode();

        var spawner = await ReadAsync(client, id);

        Assert.Equal("27", spawner.GetProperty("level").GetString());
        Assert.Equal(3, spawner.GetProperty("targetCount").GetInt32());
    }

    [Fact]
    public async Task Sending_zone_clears_the_pin()
    {
        // The other half: there has to be a way back to the default, and it is a word rather than
        // an absence.
        var (client, id) = await SpawnerAsync(level: "27");
        using var _ = client;

        (await client.PatchAsJsonAsync($"/api/builder/spawners/{id}", new { level = "zone" }))
            .EnsureSuccessStatusCode();

        var spawner = await ReadAsync(client, id);

        Assert.Equal("zone", spawner.GetProperty("level").GetString());
        Assert.Equal(10, spawner.GetProperty("fightsAtLevel").GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("27.0")]
    [InlineData("+27")]
    [InlineData(" 27")]
    [InlineData("1e2")]
    public async Task A_level_that_is_not_one_is_refused(string level)
    {
        // Parsed with NumberStyles.None and the invariant culture, so none of these are quietly
        // coerced into something nearby. A level is typed by a person; a typo should bounce rather
        // than be interpreted - unlike a derived level, which MobLevel floors because there is
        // nobody to tell.
        var (client, id) = await SpawnerAsync();
        using var _ = client;

        var response = await client.PatchAsJsonAsync(
            $"/api/builder/spawners/{id}", new { level });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And the stored value is untouched by the refusal.
        Assert.Equal("zone", (await ReadAsync(client, id)).GetProperty("level").GetString());
    }

    [Fact]
    public async Task An_item_spawner_cannot_pin_a_level()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "shelf");

        (await client.PostAsJsonAsync("/api/builder/item-templates/urn", new { name = "urn" }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey = "urn",
            templateKind = "Item",
            roomKeys = new[] { roomKey },
            targetCount = 1,
            respawnSeconds = 60,
            level = "12",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_pin_far_above_the_template_warns_that_the_reward_did_not_follow()
    {
        // The honest cost of the pin saying nothing about experience (§4.7). Lifting a level 10 rat
        // to 27 leaves it paying a level 10 rat's reward, which is a deliberate trade - and a trade
        // nobody is told about is a trap. Advisory per §7.4: it never blocks the save.
        var (client, id) = await SpawnerAsync(level: "27", templateLevel: 10);
        using var _ = client;

        var spawner = await ReadAsync(client, id);
        var zoneKey = spawner.GetProperty("zoneKey").GetString();

        var validation = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/zones/{zoneKey}/validate", UriKind.Relative)));

        var kinds = validation.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("kind").GetString())
            .ToList();

        Assert.Contains("reward-lags-level", kinds);
    }

    [Fact]
    public async Task A_level_inside_the_band_with_a_matching_reward_warns_about_neither()
    {
        var (client, id) = await SpawnerAsync(level: null, templateLevel: 3);
        using var _ = client;

        var spawner = await ReadAsync(client, id);
        var zoneKey = spawner.GetProperty("zoneKey").GetString();

        var validation = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/zones/{zoneKey}/validate", UriKind.Relative)));

        var kinds = validation.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("kind").GetString())
            .ToList();

        Assert.DoesNotContain("reward-lags-level", kinds);
        Assert.DoesNotContain("level-above-cap", kinds);
    }

    [Fact]
    public async Task Turning_a_pinned_mob_spawner_into_an_item_one_is_refused()
    {
        // Checked against the *resulting* kind rather than the stored one. Otherwise the pin
        // survives as a value that means nothing - and comes back to life the day somebody flips
        // the kind back to Mob, at a level nobody remembers choosing.
        var (client, id) = await SpawnerAsync(level: "27");
        using var _ = client;

        (await client.PostAsJsonAsync("/api/builder/item-templates/censer", new { name = "censer" }))
            .EnsureSuccessStatusCode();

        var response = await client.PatchAsJsonAsync(
            $"/api/builder/spawners/{id}",
            new { templateKind = "Item", templateKey = "censer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
