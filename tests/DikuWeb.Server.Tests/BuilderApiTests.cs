using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Domain.Accounts;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// The builder API end to end (PLAN.md §7.3), through the real HTTP stack, the real game loop,
/// and a real PostgreSQL. Every write here goes enqueue → loop applies → persist → notify.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class BuilderApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    // -----------------------------------------------------------------------
    // Authorization
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_builder_api_is_closed_to_anonymous_callers()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var response = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_player_is_forbidden_not_merely_unauthorized()
    {
        // A logged-in player must get 403, not 401: 401 would tell the client to try logging
        // in again, which cannot possibly help.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterAsync(client);

        var read = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));
        var write = await client.PostAsJsonAsync("/api/builder/worlds/nope", new { name = "Nope" });

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task A_moderator_is_not_a_builder()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, AccountRole.Moderator);
        await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });

        var response = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_do_everything_a_builder_can()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, AccountRole.Admin);
        await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });

        var response = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // CRUD
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_zone_can_be_built_end_to_end_with_no_sql()
    {
        // The Phase 2 acceptance criterion in miniature.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (worldKey, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.Equal(roomKey, room.GetProperty("key").GetString());
        Assert.Equal(zoneKey, room.GetProperty("zoneKey").GetString());

        var zones = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones?world={worldKey}", UriKind.Relative)));

        Assert.Equal(1, zones.GetArrayLength());
        Assert.Equal(1, zones[0].GetProperty("roomCount").GetInt32());
    }

    [Fact]
    public async Task Creating_something_that_already_exists_is_a_conflict()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var again = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{zoneKey}.gate", new { zoneKey, title = "Again" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_patch_leaves_unmentioned_fields_alone()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new { title = "The Iron Gate" });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.Equal("The Iron Gate", room.GetProperty("title").GetString());
        Assert.Equal("A room made by a test.", room.GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_room_key_that_does_not_match_its_zone_is_rejected()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/builder/rooms/elsewhere.other.room", new { zoneKey, title = "Wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_room_key_is_rejected_before_it_reaches_the_loop()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.PostAsJsonAsync(
            "/api/builder/rooms/NotAKey", new { title = "Wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Exits and dangling links (PLAN.md §7.4)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_exit_may_be_linked_before_its_destination_exists()
    {
        // Live editing has no publish gate to defer the link to, so this must be allowed -
        // and the response must say plainly that the target is not there yet.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var response = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/exits/north",
            new { to = $"{zoneKey}.not-yet" });

        var room = await BuilderClient.JsonAsync(response);
        var exit = room.GetProperty("exits")[0];

        Assert.Equal("north", exit.GetProperty("direction").GetString());
        Assert.False(exit.GetProperty("targetExists").GetBoolean());
    }

    [Fact]
    public async Task Linking_two_existing_rooms_creates_the_reciprocal_by_default()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var first = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");
        var second = await BuilderClient.NewRoomAsync(client, zoneKey, "road");

        await client.PutAsJsonAsync($"/api/builder/rooms/{first}/exits/north", new { to = second });

        var back = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{second}", UriKind.Relative)));

        var exit = back.GetProperty("exits")[0];
        Assert.Equal("south", exit.GetProperty("direction").GetString());
        Assert.Equal(first, exit.GetProperty("to").GetString());
    }

    // -----------------------------------------------------------------------
    // Walk-and-build (PLAN.md §7.6)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Dig_creates_a_room_and_returns_it()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var dug = await BuilderClient.JsonAsync(
            await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" }));

        Assert.Equal($"{zoneKey}.room-1", dug.GetProperty("key").GetString());
        Assert.True(dug.GetProperty("flags").GetProperty("unfinished").GetBoolean());

        // And the link back exists, so walking returns you where you came from.
        Assert.Contains(
            dug.GetProperty("exits").EnumerateArray(),
            e => e.GetProperty("direction").GetString() == "south"
                && e.GetProperty("to").GetString() == roomKey);
    }

    [Fact]
    public async Task Dig_materializes_a_dangling_exit_rather_than_creating_a_second_room()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var promised = $"{zoneKey}.the-hill";
        await client.PutAsJsonAsync($"/api/builder/rooms/{roomKey}/exits/north", new { to = promised });

        var dug = await BuilderClient.JsonAsync(
            await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" }));

        Assert.Equal(promised, dug.GetProperty("key").GetString());

        // The originally dangling link now resolves.
        var source = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.True(source.GetProperty("exits")[0].GetProperty("targetExists").GetBoolean());
    }

    [Fact]
    public async Task Digging_where_a_room_already_is_conflicts()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" });

        // Wait out the per-account dig throttle before the second attempt, so this asserts the
        // conflict rather than accidentally asserting the rate limit.
        await Task.Delay(TimeSpan.FromSeconds(2.2));

        var again = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/dig", new { direction = "north" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Dig_is_rate_limited()
    {
        // A held-down key would otherwise carve forty rooms (PLAN.md §7.6).
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var first = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/dig", new { direction = "north" });
        var second = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/dig", new { direction = "east" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Unfinished_rooms_are_the_zone_build_list_and_editing_clears_them()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var dug = await BuilderClient.JsonAsync(
            await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" }));
        var dugKey = dug.GetProperty("key").GetString()!;

        var before = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/unfinished", UriKind.Relative)));

        Assert.Equal(1, before.GetArrayLength());

        await client.PatchAsJsonAsync($"/api/builder/rooms/{dugKey}", new
        {
            title = "The Hill Road",
            description = "Cart ruts run north into the hills.",
        });

        var after = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/unfinished", UriKind.Relative)));

        Assert.Equal(0, after.GetArrayLength());
    }

    [Fact]
    public async Task Renaming_a_room_rewrites_the_exits_that_pointed_at_it()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var gate = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");
        var road = await BuilderClient.NewRoomAsync(client, zoneKey, "road");

        await client.PutAsJsonAsync($"/api/builder/rooms/{gate}/exits/north", new { to = road });

        var renamed = $"{zoneKey}.hill-road";
        var response = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{road}/rename", new { newKey = renamed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var source = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{gate}", UriKind.Relative)));

        var exit = source.GetProperty("exits")[0];
        Assert.Equal(renamed, exit.GetProperty("to").GetString());
        Assert.True(exit.GetProperty("targetExists").GetBoolean());
    }

    // -----------------------------------------------------------------------
    // Flags (PLAN.md §4.10)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_flag_registry_is_served_so_the_editor_can_render_itself()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var flags = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/room-flags", UriKind.Relative)));

        Assert.Contains(flags.EnumerateArray(), f => f.GetProperty("key").GetString() == "pvp");
        Assert.All(
            flags.EnumerateArray(),
            f => Assert.False(f.GetProperty("default").GetBoolean()));
    }

    [Fact]
    public async Task A_zone_flag_is_inherited_by_its_rooms_and_says_so()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "arena");

        await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = true },
        });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        var pvp = room.GetProperty("resolved").EnumerateArray()
            .First(f => f.GetProperty("key").GetString() == "pvp");

        Assert.True(pvp.GetProperty("value").GetBoolean());
        Assert.Equal("zone", pvp.GetProperty("source").GetString());

        // The room itself declares nothing, which is what makes it inherited.
        Assert.False(room.GetProperty("flags").TryGetProperty("pvp", out _));
    }

    [Fact]
    public async Task A_room_can_override_its_zones_flag()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "chapel");

        await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = true },
        });

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = false },
        });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        var pvp = room.GetProperty("resolved").EnumerateArray()
            .First(f => f.GetProperty("key").GetString() == "pvp");

        Assert.False(pvp.GetProperty("value").GetBoolean());
        Assert.Equal("room", pvp.GetProperty("source").GetString());
    }

    [Fact]
    public async Task An_unrecognised_flag_key_is_dropped_rather_than_stored()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new
        {
            flags = new Dictionary<string, bool> { ["dark"] = true, ["notARealFlag"] = true },
        });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.True(room.GetProperty("flags").GetProperty("dark").GetBoolean());
        Assert.False(room.GetProperty("flags").TryGetProperty("notARealFlag", out _));
    }

    [Fact]
    public async Task Setting_one_flag_leaves_its_siblings_alone()
    {
        // The whole point of the per-flag endpoint: two builders editing one zone must not
        // erase each other. Patching the room replaces the entire set; this must not.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var first = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/flags/dark", new { value = true });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/flags/indoors", new { value = true });

        var room = await BuilderClient.JsonAsync(second);

        // Both survive - the second write did not clobber the first.
        Assert.True(room.GetProperty("flags").GetProperty("dark").GetBoolean());
        Assert.True(room.GetProperty("flags").GetProperty("indoors").GetBoolean());
    }

    [Fact]
    public async Task Clearing_a_flag_removes_the_key_so_inheritance_resumes()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PutAsJsonAsync($"/api/builder/rooms/{roomKey}/flags/dark", new { value = false });

        // A null value clears the key entirely rather than storing "off".
        var room = await BuilderClient.JsonAsync(
            await client.PutAsJsonAsync(
                $"/api/builder/rooms/{roomKey}/flags/dark", new { value = (bool?)null }));

        Assert.False(room.GetProperty("flags").TryGetProperty("dark", out _));
    }

    [Fact]
    public async Task An_unknown_flag_name_is_refused_by_the_per_flag_endpoint()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var response = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/flags/notARealFlag", new { value = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Templates - the round-trip defects (no coverage existed before)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_item_templates_slot_survives_as_a_name_not_a_number()
    {
        // The response record serialised Slot as an int while the client reads a string, so a
        // slot could not survive a load-edit-save round trip - and Head, being 0, read as unset.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "A plumed helm",
            slot = "Head",
        })).EnsureSuccessStatusCode();

        var template = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/item-templates/{key}", UriKind.Relative)));

        var slot = template.GetProperty("slot");
        Assert.Equal(JsonValueKind.String, slot.ValueKind);
        Assert.Equal("Head", slot.GetString());
    }

    [Fact]
    public async Task Patching_a_mob_templates_name_leaves_its_loot_and_behavior_intact()
    {
        // The editor sent a fresh baseStats object and never sent loot/behavior, so a name-only
        // save from the panel wiped content authored elsewhere. A field-scoped PATCH must not.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("m").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "A warden",
            level = 5,
            baseStats = new Dictionary<string, object> { ["health"] = 40, ["strength"] = 12 },
            loot = new[]
            {
                new Dictionary<string, object> { ["itemKey"] = "rusted-blade", ["chance"] = 0.15 },
            },
            behavior = new Dictionary<string, object> { ["aggressive"] = true },
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "A grizzled warden",
        })).EnsureSuccessStatusCode();

        var template = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/mob-templates/{key}", UriKind.Relative)));

        Assert.Equal("A grizzled warden", template.GetProperty("name").GetString());
        Assert.Equal(12, template.GetProperty("baseStats").GetProperty("strength").GetInt32());
        Assert.True(template.GetProperty("behavior").GetProperty("aggressive").GetBoolean());
        Assert.Equal(
            "rusted-blade",
            template.GetProperty("loot")[0].GetProperty("itemKey").GetString());
    }

    // -----------------------------------------------------------------------
    // Validation (PLAN.md §7.4) - advisory, never blocking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Validation_reports_dangling_exits_without_having_blocked_the_save()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var saved = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/exits/north", new { to = $"{zoneKey}.not-yet" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var report = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/validate", UriKind.Relative)));

        Assert.Contains(
            report.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("kind").GetString() == "dangling-exit");
    }

    [Fact]
    public async Task Validation_names_the_rooms_that_became_pvp_by_inheritance()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "square");

        await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = true },
        });

        var report = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/validate", UriKind.Relative)));

        Assert.Contains(
            report.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("kind").GetString() == "inherited-pvp"
                && w.GetProperty("entityKey").GetString() == roomKey);
    }

    // -----------------------------------------------------------------------
    // Audit (PLAN.md §6, §7.3)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Every_write_leaves_an_audit_row_with_before_and_after()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new { title = "The Iron Gate" });

        var audit = await BuilderClient.JsonAsync(
            await client.GetAsync(
                new Uri($"/api/builder/audit?kind=room&key={roomKey}", UriKind.Relative)));

        var actions = audit.EnumerateArray()
            .Select(a => a.GetProperty("action").GetString())
            .ToList();

        Assert.Contains("Create", actions);
        Assert.Contains("Update", actions);

        // And it records who, which is the question actually asked after a bad live edit.
        Assert.All(
            audit.EnumerateArray(),
            a => Assert.False(string.IsNullOrEmpty(a.GetProperty("username").GetString())));
    }

    [Fact]
    public async Task Deleting_leaves_the_audit_row_behind()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "doomed");

        var deleted = await client.DeleteAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var audit = await BuilderClient.JsonAsync(
            await client.GetAsync(
                new Uri($"/api/builder/audit?kind=room&key={roomKey}", UriKind.Relative)));

        Assert.Contains(
            audit.EnumerateArray(),
            a => a.GetProperty("action").GetString() == "Delete");
    }
}
