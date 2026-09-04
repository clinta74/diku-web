using System.Net;
using System.Net.Http.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// A spawner's name modifier over the wire (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// The wire carries text, and on a PATCH null means "leave it" while an empty string means "clear
/// it" — the same distinction <c>level</c> draws with a word, drawn here with the value an empty
/// text field naturally produces. Most of what is asserted is that distinction, plus the two
/// refusals: an item spawner cannot carry one, and a named character cannot take one.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class SpawnerNameApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<(HttpClient Client, string ZoneKey, string RoomKey, string TemplateKey)> WorldAsync(
        string templateName = "a brigand")
    {
        var factory = postgres.App;
        var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "fen");
        var templateKey = BuilderClient.UniqueName("brigand").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{templateKey}", new
        {
            name = templateName,
            level = 7,
            baseStats = new { health = 40 },
        })).EnsureSuccessStatusCode();

        return (client, zoneKey, roomKey, templateKey);
    }

    private static async Task<HttpResponseMessage> CreateAsync(
        HttpClient client, string zoneKey, string roomKey, string templateKey,
        string? nameModifier, string templateKind = "Mob") =>
        await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey,
            templateKind,
            roomKeys = new[] { roomKey },
            targetCount = 1,
            nameModifier,
        });

    private static async Task<System.Text.Json.JsonElement> ReadAsync(HttpClient client, string id) =>
        await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/spawners/{id}", UriKind.Relative)));

    [Fact]
    public async Task Creating_with_a_modifier_reports_the_composed_name()
    {
        var (client, zoneKey, roomKey, templateKey) = await WorldAsync();

        var created = await BuilderClient.JsonAsync(
            await CreateAsync(client, zoneKey, roomKey, templateKey, "marsh"));

        Assert.Equal("marsh", created.GetProperty("nameModifier").GetString());
        Assert.Equal("a marsh brigand", created.GetProperty("spawnsAs").GetString());
    }

    [Fact]
    public async Task Creating_without_one_composes_nothing()
    {
        var (client, zoneKey, roomKey, templateKey) = await WorldAsync();

        var created = await BuilderClient.JsonAsync(
            await CreateAsync(client, zoneKey, roomKey, templateKey, nameModifier: null));

        Assert.Equal(System.Text.Json.JsonValueKind.Null, created.GetProperty("nameModifier").ValueKind);
        Assert.Equal("a brigand", created.GetProperty("spawnsAs").GetString());
    }

    [Fact]
    public async Task A_patch_that_omits_the_modifier_leaves_it_and_an_empty_one_clears_it()
    {
        var (client, zoneKey, roomKey, templateKey) = await WorldAsync();
        var created = await BuilderClient.JsonAsync(
            await CreateAsync(client, zoneKey, roomKey, templateKey, "marsh"));
        var id = created.GetProperty("id").GetString()!;

        (await client.PatchAsJsonAsync($"/api/builder/spawners/{id}", new { targetCount = 2 }))
            .EnsureSuccessStatusCode();
        Assert.Equal("marsh", (await ReadAsync(client, id)).GetProperty("nameModifier").GetString());

        (await client.PatchAsJsonAsync($"/api/builder/spawners/{id}", new { nameModifier = "" }))
            .EnsureSuccessStatusCode();

        var cleared = await ReadAsync(client, id);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cleared.GetProperty("nameModifier").ValueKind);
        Assert.Equal("a brigand", cleared.GetProperty("spawnsAs").GetString());
    }

    [Fact]
    public async Task The_stored_word_is_trimmed()
    {
        var (client, zoneKey, roomKey, templateKey) = await WorldAsync();

        var created = await BuilderClient.JsonAsync(
            await CreateAsync(client, zoneKey, roomKey, templateKey, "  marsh "));

        Assert.Equal("marsh", created.GetProperty("nameModifier").GetString());
    }

    [Theory]
    [InlineData("a marsh")]
    [InlineData("Marsh")]
    [InlineData("marsh2")]
    public async Task A_bad_word_is_refused_with_the_reason(string modifier)
    {
        var (client, zoneKey, roomKey, templateKey) = await WorldAsync();

        var response = await CreateAsync(client, zoneKey, roomKey, templateKey, modifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(modifier, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_named_character_cannot_take_one()
    {
        var (client, zoneKey, roomKey, templateKey) = await WorldAsync("Tessa Roke, armourer");

        var response = await CreateAsync(client, zoneKey, roomKey, templateKey, "marsh");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("named character", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_spawner_cannot_carry_one()
    {
        var (client, zoneKey, roomKey, _) = await WorldAsync();
        var itemKey = BuilderClient.UniqueName("lamp").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{itemKey}", new
        {
            name = "a lamp",
        })).EnsureSuccessStatusCode();

        var response = await CreateAsync(client, zoneKey, roomKey, itemKey, "hooded", templateKind: "Item");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Flipping_a_modified_spawner_to_item_is_refused_unless_the_word_is_cleared()
    {
        var (client, zoneKey, roomKey, templateKey) = await WorldAsync();
        var created = await BuilderClient.JsonAsync(
            await CreateAsync(client, zoneKey, roomKey, templateKey, "marsh"));
        var id = created.GetProperty("id").GetString()!;

        var itemKey = BuilderClient.UniqueName("lamp").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{itemKey}", new
        {
            name = "a lamp",
        })).EnsureSuccessStatusCode();

        var refused = await client.PatchAsJsonAsync(
            $"/api/builder/spawners/{id}", new { templateKind = "Item", templateKey = itemKey });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var allowed = await client.PatchAsJsonAsync(
            $"/api/builder/spawners/{id}", new { templateKind = "Item", templateKey = itemKey, nameModifier = "" });
        allowed.EnsureSuccessStatusCode();
    }
}
