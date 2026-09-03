using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;

namespace Muwbta.Server.Tests;

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
            multipliers = new { itemValue = 4.0 },
        })).EnsureSuccessStatusCode();

        var world = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/worlds/{worldKey}", UriKind.Relative)));

        Assert.Equal(4.0m, world.GetProperty("multipliers").GetProperty("itemValue").GetDecimal());
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
        Assert.Equal(1m, multipliers.GetProperty("itemValue").GetDecimal());

        // The two dials that used to be asserted here, `itemPower` and `spawnDensity`, are gone —
        // authored, editable, previewed, exported, and applied by nothing (BUGS.md #17).
        Assert.False(multipliers.TryGetProperty("itemPower", out _));
        Assert.False(multipliers.TryGetProperty("spawnDensity", out _));
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

    /// <summary>
    /// The preview resolves a template's *own* health, not a default.
    /// </summary>
    /// <remarks>
    /// `BaseStats` is jsonb, so its values come back as <c>JsonElement</c>. The resolver read
    /// health with <c>value is int</c>, which is false for every template that had ever been
    /// saved - so the panel reported 40 health for everything in the zone, and a builder tuning
    /// the Strength dial watched a number that was not their mob's.
    /// </remarks>
    [Fact]
    public async Task The_preview_resolves_a_templates_own_health()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "den");

        (await client.PostAsJsonAsync("/api/builder/mob-templates/ogre", new
        {
            name = "ogre",
            baseStats = new { health = 250 },
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey = "ogre",
            templateKind = "Mob",
            roomKeys = new[] { roomKey },
            targetCount = 1,
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            multipliers = new { strength = 2.0 },
        })).EnsureSuccessStatusCode();

        var preview = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/preview", UriKind.Relative)));

        var ogre = preview.GetProperty("templates").EnumerateArray()
            .Single(t => t.GetProperty("templateKey").GetString() == "ogre");

        // 250 doubled, not the 40 fallback doubled.
        Assert.Equal(500, ogre.GetProperty("resolvedStats").GetProperty("health").GetInt32());
    }

    /// <summary>
    /// The preview reports the level a mob will fight at, not only the level it was authored as.
    /// </summary>
    /// <remarks>
    /// The number that decides whether killing it teaches anyone anything (§4.7), and there was
    /// nowhere to see it before a player felt it. Also pins that the damage dial reaches the
    /// preview at all: it resolved health, xp and gold and nothing else, so the one dial that did
    /// nothing in the engine also showed nothing in the panel that exists to tune it.
    /// </remarks>
    [Fact]
    public async Task The_preview_reports_the_level_a_mob_will_fight_at()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "hollow");

        (await client.PostAsJsonAsync("/api/builder/mob-templates/dire-rat", new
        {
            name = "dire rat",
            level = 5,
            baseStats = new { health = 40, damage = "4-7" },
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey = "dire-rat",
            templateKind = "Mob",
            roomKeys = new[] { roomKey },
            targetCount = 1,
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            multipliers = new { strength = 4.0 },
        })).EnsureSuccessStatusCode();

        var preview = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/preview", UriKind.Relative)));

        var rat = preview.GetProperty("templates").EnumerateArray()
            .Single(t => t.GetProperty("templateKey").GetString() == "dire-rat");

        Assert.Equal(5, rat.GetProperty("templateLevel").GetInt32());
        Assert.Equal(20, rat.GetProperty("fightsAtLevel").GetInt32());

        // Damage scaled, and reported as a pair because the resolved values are integers and the
        // template wrote its dice as a range.
        Assert.Equal(16, rat.GetProperty("resolvedStats").GetProperty("damageMin").GetInt32());
        Assert.Equal(28, rat.GetProperty("resolvedStats").GetProperty("damageMax").GetInt32());

        // And the unscaled counterpart, so the panel's Base column is not "—" for the one dial the
        // panel exists to tune.
        Assert.Equal(4, rat.GetProperty("baseValues").GetProperty("damageMin").GetInt32());
        Assert.Equal(7, rat.GetProperty("baseValues").GetProperty("damageMax").GetInt32());
    }

    /// <summary>
    /// A room's spawner list says what each placement will actually produce.
    /// </summary>
    [Fact]
    public async Task A_spawner_reports_the_level_its_mobs_will_fight_at()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "warren");

        (await client.PostAsJsonAsync("/api/builder/mob-templates/mole", new
        {
            name = "mole",
            level = 6,
            baseStats = new { health = 30 },
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            multipliers = new { strength = 3.0 },
        })).EnsureSuccessStatusCode();

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/builder/spawners",
            new
            {
                zoneKey,
                templateKey = "mole",
                templateKind = "Mob",
                roomKeys = new[] { roomKey },
                targetCount = 1,
            }));

        var id = created.GetProperty("id").GetString();

        var spawner = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/spawners/{id}", UriKind.Relative)));

        Assert.Equal(18, spawner.GetProperty("fightsAtLevel").GetInt32());
    }

    /// <summary>An item has no level, and the field says so rather than guessing.</summary>
    [Fact]
    public async Task An_item_spawner_reports_no_level()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "cache");

        (await client.PostAsJsonAsync("/api/builder/item-templates/lantern", new
        {
            name = "lantern",
            baseValue = 12,
        })).EnsureSuccessStatusCode();

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/builder/spawners",
            new
            {
                zoneKey,
                templateKey = "lantern",
                templateKind = "Item",
                roomKeys = new[] { roomKey },
                targetCount = 1,
            }));

        Assert.Equal(0, created.GetProperty("fightsAtLevel").GetInt32());
    }
}
