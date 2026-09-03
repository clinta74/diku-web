using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// Where a template actually exists in the world (PLAN.md §7.9).
/// </summary>
/// <remarks>
/// Every relationship read here is stored on the side that does the naming — a spawner names its
/// template, a loot row names its item, a quest names its reward — so all of them are invisible
/// from the template's own editor and none of them can be asserted from one table. That is the
/// point of the endpoint and the reason these are round trips rather than unit tests: the answer
/// is a join across four of them.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class TemplatePlacementApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> BuilderAsync()
    {
        var factory = postgres.App;
        var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        return client;
    }

    private static async Task<JsonElement> PlacementAsync(HttpClient client, string kind, string key) =>
        await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/{kind}-templates/{key}/placement", UriKind.Relative)));

    private static async Task<string> NewMobAsync(
        HttpClient client,
        string name = "rat",
        object? loot = null,
        object? behavior = null)
    {
        var key = BuilderClient.UniqueName(name).ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name,
            level = 3,
            baseStats = new { health = 40 },
            loot,
            behavior,
        })).EnsureSuccessStatusCode();

        return key;
    }

    private static async Task<string> NewItemAsync(HttpClient client, string name = "torch")
    {
        var key = BuilderClient.UniqueName(name).ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name,
            description = "",
            icon = "i",
        })).EnsureSuccessStatusCode();

        return key;
    }

    private static async Task SpawnerAsync(
        HttpClient client,
        string zoneKey,
        string templateKey,
        string kind,
        params string[] roomKeys)
    {
        (await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey,
            templateKind = kind,
            roomKeys,
            targetCount = 2,
            respawnSeconds = 60,
        })).EnsureSuccessStatusCode();
    }

    private static JsonElement[] Items(JsonElement array) => [.. array.EnumerateArray()];

    // -----------------------------------------------------------------------
    // Mobs
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_mobs_placement_names_its_spawners_and_the_rooms_they_fill()
    {
        using var client = await BuilderAsync();
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var burrow = await BuilderClient.NewRoomAsync(client, zoneKey, "burrow");
        var ditch = await BuilderClient.NewRoomAsync(client, zoneKey, "ditch");
        var mobKey = await NewMobAsync(client);

        await SpawnerAsync(client, zoneKey, mobKey, "Mob", burrow, ditch);

        var placement = await PlacementAsync(client, "mob", mobKey);

        Assert.Equal("mob", placement.GetProperty("kind").GetString());
        var spawner = Assert.Single(Items(placement.GetProperty("spawners")));
        Assert.Equal(zoneKey, spawner.GetProperty("zoneKey").GetString());
        Assert.Equal(2, spawner.GetProperty("targetCount").GetInt32());

        // The rooms are the half a template's own editor cannot show, and a key is not what a
        // builder reads - the title is why this is an endpoint rather than a filter over spawners.
        var rooms = Items(spawner.GetProperty("rooms"));
        Assert.Equal(2, rooms.Length);
        Assert.Contains(rooms, r => r.GetProperty("key").GetString() == burrow);
        Assert.All(rooms, r => Assert.False(string.IsNullOrEmpty(r.GetProperty("title").GetString())));
    }

    [Fact]
    public async Task A_mob_nothing_places_reports_no_spawners_rather_than_failing()
    {
        using var client = await BuilderAsync();
        var mobKey = await NewMobAsync(client);

        var placement = await PlacementAsync(client, "mob", mobKey);

        Assert.Empty(Items(placement.GetProperty("spawners")));
    }

    /// <summary>
    /// A spawner pointing at a deleted room is allowed (§7.4) and is exactly the kind of thing
    /// this panel exists to make visible, so the row survives with no title rather than being
    /// filtered out into looking like it was never there.
    /// </summary>
    [Fact]
    public async Task A_room_that_was_deleted_under_a_spawner_still_shows_with_no_title()
    {
        using var client = await BuilderAsync();
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var doomed = await BuilderClient.NewRoomAsync(client, zoneKey, "doomed");
        var mobKey = await NewMobAsync(client);

        await SpawnerAsync(client, zoneKey, mobKey, "Mob", doomed);
        (await client.DeleteAsync(new Uri($"/api/builder/rooms/{doomed}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var placement = await PlacementAsync(client, "mob", mobKey);
        var spawner = Assert.Single(Items(placement.GetProperty("spawners")));
        var room = Assert.Single(Items(spawner.GetProperty("rooms")));

        Assert.Equal(doomed, room.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, room.GetProperty("title").ValueKind);
    }

    [Fact]
    public async Task A_template_that_does_not_exist_is_a_404()
    {
        using var client = await BuilderAsync();

        var response = await client.GetAsync(
            new Uri("/api/builder/mob-templates/no-such-thing/placement", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Items
    // -----------------------------------------------------------------------

    /// <summary>
    /// Most items have no ground spawner at all — they drop. An item placement that reported only
    /// spawners would answer "nowhere" for nearly every item in the game.
    /// </summary>
    [Fact]
    public async Task An_items_placement_names_the_mobs_that_drop_it()
    {
        using var client = await BuilderAsync();
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var lair = await BuilderClient.NewRoomAsync(client, zoneKey, "lair");
        var itemKey = await NewItemAsync(client);

        var dropper = await NewMobAsync(client, "brute", loot: new[]
        {
            new { itemTemplateKey = itemKey, chance = 0.25 },
        });

        await SpawnerAsync(client, zoneKey, dropper, "Mob", lair);

        var placement = await PlacementAsync(client, "item", itemKey);
        var drop = Assert.Single(Items(placement.GetProperty("droppedBy")));

        Assert.Equal(dropper, drop.GetProperty("key").GetString());
        Assert.Equal(0.25, drop.GetProperty("chance").GetDouble());
        Assert.True(drop.GetProperty("placed").GetBoolean());
    }

    /// <summary>
    /// Loot on a mob no spawner places is loot nobody can reach — the finding
    /// <c>/reachability</c> already reports for a quest item, said here for every item.
    /// </summary>
    [Fact]
    public async Task A_drop_from_a_mob_nothing_places_says_so()
    {
        using var client = await BuilderAsync();
        var itemKey = await NewItemAsync(client);

        await NewMobAsync(client, "ghost", loot: new[]
        {
            new { itemTemplateKey = itemKey, chance = 1.0 },
        });

        var placement = await PlacementAsync(client, "item", itemKey);
        var drop = Assert.Single(Items(placement.GetProperty("droppedBy")));

        Assert.False(drop.GetProperty("placed").GetBoolean());
    }

    /// <summary>A zero chance is a row that can never fire, so it is not a source.</summary>
    [Fact]
    public async Task A_loot_row_at_zero_chance_is_not_a_source()
    {
        using var client = await BuilderAsync();
        var itemKey = await NewItemAsync(client);

        await NewMobAsync(client, "tease", loot: new[]
        {
            new { itemTemplateKey = itemKey, chance = 0.0 },
        });

        var placement = await PlacementAsync(client, "item", itemKey);

        Assert.Empty(Items(placement.GetProperty("droppedBy")));
    }

    [Fact]
    public async Task An_items_placement_names_the_shopkeepers_that_sell_it()
    {
        using var client = await BuilderAsync();
        var itemKey = await NewItemAsync(client);

        var shopkeeper = await NewMobAsync(client, "trader", behavior: new
        {
            shopkeeper = true,
            sells = new[] { itemKey },
        });

        var placement = await PlacementAsync(client, "item", itemKey);
        var shop = Assert.Single(Items(placement.GetProperty("soldBy")));

        Assert.Equal(shopkeeper, shop.GetProperty("key").GetString());
    }

    [Fact]
    public async Task An_items_placement_names_the_quest_that_hands_it_over()
    {
        using var client = await BuilderAsync();
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var itemKey = await NewItemAsync(client);
        var questKey = BuilderClient.UniqueName("q").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/quests/{questKey}", new
        {
            zoneKey,
            name = "The Lost Ledger",
            giverMobKey = "kaelen",
            turninMobKey = "kaelen",
            rewardItemKey = itemKey,
            rewardItemCount = 1,
        })).EnsureSuccessStatusCode();

        var placement = await PlacementAsync(client, "item", itemKey);
        var quest = Assert.Single(Items(placement.GetProperty("quests")));

        Assert.Equal(questKey, quest.GetProperty("key").GetString());
        Assert.Equal("reward", quest.GetProperty("role").GetString());
    }

    /// <summary>
    /// A fetch quest that hands back what it asked for is two facts about one key, so it is two
    /// rows - a single compound role would have to be read rather than displayed.
    /// </summary>
    [Fact]
    public async Task A_quest_that_both_asks_for_an_item_and_rewards_it_is_listed_twice()
    {
        using var client = await BuilderAsync();
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var itemKey = await NewItemAsync(client);
        var questKey = BuilderClient.UniqueName("q").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/quests/{questKey}", new
        {
            zoneKey,
            name = "Hand It Back",
            giverMobKey = "kaelen",
            turninMobKey = "kaelen",
            requiredItemKey = itemKey,
            requiredCount = 1,
            rewardItemKey = itemKey,
            rewardItemCount = 1,
        })).EnsureSuccessStatusCode();

        var placement = await PlacementAsync(client, "item", itemKey);
        var roles = Items(placement.GetProperty("quests"))
            .Select(q => q.GetProperty("role").GetString())
            .ToList();

        Assert.Equal(2, roles.Count);
        Assert.Contains("reward", roles);
        Assert.Contains("required", roles);
    }

    /// <summary>
    /// A mob key and an item key are different namespaces and routinely collide, which is why
    /// there are two routes rather than one that takes a kind.
    /// </summary>
    [Fact]
    public async Task An_item_and_a_mob_sharing_a_key_do_not_report_each_others_spawners()
    {
        using var client = await BuilderAsync();
        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var shelf = await BuilderClient.NewRoomAsync(client, zoneKey, "shelf");
        var key = BuilderClient.UniqueName("torch").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "a torch",
            icon = "i",
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "a living torch",
            level = 2,
        })).EnsureSuccessStatusCode();

        await SpawnerAsync(client, zoneKey, key, "Item", shelf);

        Assert.Single(Items((await PlacementAsync(client, "item", key)).GetProperty("spawners")));
        Assert.Empty(Items((await PlacementAsync(client, "mob", key)).GetProperty("spawners")));
    }
}
