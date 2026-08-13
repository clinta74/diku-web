using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DikuWeb.Domain.Worlds;
using DikuWeb.Persistence;
using DikuWeb.Server.Building;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DikuWeb.Server.Tests;

/// <summary>
/// World export and import (PLAN.md §6, Phase 6) - the JSON that moves authored content between
/// environments, through the real HTTP stack, the real game loop, and a real PostgreSQL.
/// </summary>
/// <remarks>
/// Every test scopes its export to its own zone. The suite shares one database, so an "all"
/// export would pick up whatever else the run had authored - and a test whose subject is
/// "everything" cannot assert anything about the size of what it got back.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class WorldTransferTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    // -----------------------------------------------------------------------
    // Authorization
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Transferring_the_world_is_closed_to_ordinary_players()
    {
        // Asserted on these two routes specifically rather than trusted to the group: a route
        // mapped a line outside the MapGroup would be an unauthenticated dump of the whole world,
        // and nothing else in the suite would notice.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterAsync(client);

        var export = await client.GetAsync(new Uri("/api/builder/export", UriKind.Relative));
        var import = await client.PostAsJsonAsync("/api/builder/import", new { formatVersion = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, import.StatusCode);
    }

    [Fact]
    public async Task Exporting_a_zone_that_does_not_exist_is_not_an_empty_bundle()
    {
        // An empty bundle would be indistinguishable from a correct export of an empty zone,
        // which is exactly the answer somebody restores from and then wonders where it went.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.GetAsync(
            new Uri("/api/builder/export?zone=nosuch.zone", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // What a scoped bundle carries
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_zone_bundle_carries_the_templates_its_content_needs()
    {
        // The load-bearing property of a scoped export. Rooms, spawners, and quests belong to a
        // zone, so scoping those is a filter; templates are global, so a bundle that filtered
        // them the same way would import cleanly and then spawn nothing.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var bundle = await ExportZoneAsync(client, content.ZoneKey);

        Assert.Contains(content.MobKey, KeysOf(bundle, "mobTemplates"));

        // Reached through the spawner, which places the mob.
        Assert.Contains(content.QuestItemKey, KeysOf(bundle, "itemTemplates"));

        // Reached only through the mob's loot table - no spawner places it and no quest names it
        // directly, so a closure that stopped at spawners and quests would leave the quest
        // unfinishable in the target environment (§10).
        Assert.Contains(content.LootKey, KeysOf(bundle, "itemTemplates"));
    }

    [Fact]
    public async Task A_zone_bundle_carries_the_world_above_it()
    {
        // Multipliers resolve through the world (§4.4), so a zone imported without its world
        // would produce wrong numbers rather than merely missing ones.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var bundle = await ExportZoneAsync(client, content.ZoneKey);

        Assert.Equal([content.WorldKey], KeysOf(bundle, "worlds"));
        Assert.Equal([content.ZoneKey], KeysOf(bundle, "zones"));
        Assert.Equal("zone", bundle.GetProperty("scope").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_zone_bundle_does_not_carry_another_zones_rooms()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var mine = await AuthorZoneAsync(client);
        var theirs = await AuthorZoneAsync(client);

        var bundle = await ExportZoneAsync(client, mine.ZoneKey);
        var rooms = KeysOf(bundle, "rooms");

        Assert.Contains(mine.RoomKey, rooms);
        Assert.DoesNotContain(theirs.RoomKey, rooms);
    }

    // -----------------------------------------------------------------------
    // The round trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_import_restores_what_was_edited_after_the_export()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.PatchAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}",
            new { title = "Edited after the export" })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json);
        Assert.True(report.GetProperty("failures").GetArrayLength() == 0, Raw(report));

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        Assert.Equal(content.RoomTitle, room.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_import_restores_the_exits_that_were_removed_after_the_export()
    {
        // Exits travel inside their room rather than as their own list, so this is the assertion
        // that they travel at all - a bundle that carried rooms and dropped the graph between
        // them would look complete and import a zone nobody can walk through.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.DeleteAsync(
            new Uri($"/api/builder/rooms/{content.RoomKey}/exits/north", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        await ImportAsync(client, json);

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        var directions = room.GetProperty("exits").EnumerateArray()
            .Select(e => e.GetProperty("direction").GetString())
            .ToList();

        Assert.Contains("north", directions);
    }

    [Fact]
    public async Task Importing_the_same_bundle_twice_does_not_double_the_population()
    {
        // A spawner has no content key to collide on, so it travels with its id. Minting a fresh
        // one per import would double every zone's population on the second run - and a doubled
        // spawner is invisible in the editor and obvious only in play.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        await ImportAsync(client, json);
        await ImportAsync(client, json);

        var spawners = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/spawners?zone={content.ZoneKey}", UriKind.Relative)));

        Assert.Equal(1, spawners.GetArrayLength());
    }

    [Fact]
    public async Task A_second_import_of_an_unchanged_bundle_updates_and_creates_nothing()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        var report = await ImportAsync(client, json);

        foreach (var count in report.GetProperty("counts").EnumerateArray())
        {
            Assert.Equal(0, count.GetProperty("created").GetInt32());
        }
    }

    [Fact]
    public async Task A_flag_this_build_does_not_recognise_survives_the_round_trip()
    {
        // §4.10 in transit. The builder API drops an unknown flag on the way in, deliberately -
        // but an export/import is transport, not authoring, and silently rewriting content on its
        // way between two environments running different builds is the failure this guards.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        const string Unknown = "experimental-fog";

        await SetFlagDirectlyAsync(factory, content.RoomKey, flags =>
        {
            var next = flags.Clone();
            next.Set(Unknown, true);
            return next;
        });

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);
        Assert.Contains(Unknown, json, StringComparison.Ordinal);

        await SetFlagDirectlyAsync(factory, content.RoomKey, flags =>
        {
            var next = flags.Clone();
            next.Clear(Unknown);
            return next;
        });

        await ImportAsync(client, json);

        Assert.True(await HasFlagDirectlyAsync(factory, content.RoomKey, Unknown));
    }

    // -----------------------------------------------------------------------
    // Rehearsing, and refusing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_dry_run_reports_what_it_would_do_and_changes_nothing()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.PatchAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}",
            new { title = "Edited after the export" })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json, dryRun: true);

        Assert.True(report.GetProperty("dryRun").GetBoolean());

        // Both of the zone's rooms are already here, so both count as updates and neither as a
        // creation - which is the distinction a builder reads the rehearsal for.
        var rooms = report.GetProperty("counts").EnumerateArray()
            .Single(c => c.GetProperty("kind").GetString() == "room");

        Assert.Equal(2, rooms.GetProperty("updated").GetInt32());
        Assert.Equal(0, rooms.GetProperty("created").GetInt32());

        // The rehearsal is worth nothing if it also performs.
        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        Assert.Equal("Edited after the export", room.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_bundle_from_another_format_version_is_refused_outright()
    {
        // The one hard refusal in the import path. A bundle this build cannot read would
        // otherwise apply the fields that happened to match and silently drop the rest.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        // Rewritten off the current version rather than off the literal 1. Written as a literal
        // this silently stopped testing anything the day the format went to 2: the replace matched
        // nothing, the bundle stayed valid, and the assertion failed rather than passing wrongly -
        // which is only luck, since a test asserting a refusal would have gone green if the import
        // had happened to reject it for some other reason.
        var json = (await ExportZoneJsonAsync(client, content.ZoneKey))
            .Replace(
                $"\"formatVersion\":{WorldBundle.CurrentFormatVersion}",
                "\"formatVersion\":99",
                StringComparison.Ordinal);

        Assert.Contains("\"formatVersion\":99", json, StringComparison.Ordinal);

        var response = await client.PostAsync(
            new Uri("/api/builder/import", UriKind.Relative),
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_bundle_from_the_previous_format_version_is_refused()
    {
        // Version 1 carried `sentinel: bool` on a spawner, where false was the value every row had
        // by default and meant "these mobs wander". Version 2 carries `wanders: bool?`, where
        // absent means "follow the template". Read as v2 a v1 bundle deserialises the missing key
        // to null, so every spawner in it changes behaviour without a word - the silent partial
        // apply the version number exists to refuse, arriving through a rename rather than
        // through a new field.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = (await ExportZoneJsonAsync(client, content.ZoneKey))
            .Replace(
                $"\"formatVersion\":{WorldBundle.CurrentFormatVersion}",
                "\"formatVersion\":1",
                StringComparison.Ordinal);

        var response = await client.PostAsync(
            new Uri("/api/builder/import", UriKind.Relative),
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_reference_the_bundle_does_not_carry_is_a_warning_not_a_refusal()
    {
        // Advisory, exactly as /validate is (§7.4). Importing one zone of several legitimately
        // leaves exits pointing at rooms that are not here yet, and refusing that would make the
        // zone-at-a-time workflow the scoped export exists for impossible.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}/exits/south",
            new { to = $"{content.ZoneKey}.elsewhere", reciprocal = false }))
            .EnsureSuccessStatusCode();

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);
        var report = await ImportAsync(client, json);

        var warnings = report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("kind").GetString())
            .ToList();

        Assert.Contains("dangling-exit", warnings);
        Assert.Equal(0, report.GetProperty("failures").GetArrayLength());
    }

    [Fact]
    public async Task An_import_is_answerable_afterwards()
    {
        // Every entity goes through WorldEditor, so an import writes a content_audit row per
        // entity the same way a hand edit does. For a change this size that is the whole point -
        // "who replaced the crypt" has to be one query (§10).
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        await ImportAsync(client, json);

        var audit = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/audit?kind=room&key={content.RoomKey}", UriKind.Relative)));

        Assert.True(audit.GetArrayLength() >= 2, "The import should have left an audit row of its own.");
    }

    // -----------------------------------------------------------------------
    // Authoring a zone with one of everything
    // -----------------------------------------------------------------------

    private sealed record AuthoredZone(
        string WorldKey,
        string ZoneKey,
        string RoomKey,
        string RoomTitle,
        string MobKey,
        string QuestItemKey,
        string LootKey,
        string QuestKey);

    /// <summary>
    /// A zone with one of every content type, wired so that each reference the exporter has to
    /// close over is exercised exactly once: a spawner reaches the mob, the quest reaches the
    /// item it requires, and the mob's loot table reaches an item nothing else names.
    /// </summary>
    private static async Task<AuthoredZone> AuthorZoneAsync(HttpClient client)
    {
        var (worldKey, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "start");
        var northKey = await BuilderClient.NewRoomAsync(client, zoneKey, "north");

        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/exits/north",
            new { to = northKey, reciprocal = true })).EnsureSuccessStatusCode();

        var lootKey = BuilderClient.UniqueName("loot").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{lootKey}", new
        {
            name = "a tarnished fang",
            description = "Dropped, never placed.",
        })).EnsureSuccessStatusCode();

        var questItemKey = BuilderClient.UniqueName("token").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{questItemKey}", new
        {
            name = "a carved token",
            description = "What the quest asks for.",
            isQuestItem = true,
        })).EnsureSuccessStatusCode();

        var mobKey = BuilderClient.UniqueName("mob").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{mobKey}", new
        {
            name = "a hollow warden",
            description = "Stands here.",
            level = 3,
            loot = new[]
            {
                new Dictionary<string, object>
                {
                    ["itemTemplateKey"] = lootKey,
                    ["chance"] = 0.5,
                },
            },
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey = mobKey,
            templateKind = "Mob",
            roomKeys = new[] { roomKey },
            targetCount = 1,
        })).EnsureSuccessStatusCode();

        var questKey = BuilderClient.UniqueName("quest").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/quests/{questKey}", new
        {
            zoneKey,
            name = "The carved token",
            giverMobKey = mobKey,
            turninMobKey = mobKey,
            requiredItemKey = questItemKey,
            requiredCount = 1,
            rewardXp = 40,
        })).EnsureSuccessStatusCode();

        return new AuthoredZone(
            worldKey, zoneKey, roomKey, "The start room", mobKey, questItemKey, lootKey, questKey);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<string> ExportZoneJsonAsync(HttpClient client, string zoneKey)
    {
        var response = await client.GetAsync(
            new Uri($"/api/builder/export?zone={zoneKey}", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> ExportZoneAsync(HttpClient client, string zoneKey) =>
        JsonDocument.Parse(await ExportZoneJsonAsync(client, zoneKey)).RootElement;

    private static async Task<JsonElement> ImportAsync(
        HttpClient client,
        string bundleJson,
        bool dryRun = false)
    {
        var response = await client.PostAsync(
            new Uri($"/api/builder/import?dryRun={dryRun}", UriKind.Relative),
            new StringContent(bundleJson, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();

        // 207 is a partial import, which the assertions read rather than throw on - the report
        // names what failed, and that is more useful than an exception saying only that it did.
        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MultiStatus,
            $"Import returned {(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement;
    }

    private static IReadOnlyList<string> KeysOf(JsonElement bundle, string collection) =>
    [
        .. bundle.GetProperty(collection).EnumerateArray()
            .Select(e => e.GetProperty("key").GetString()!),
    ];

    private static string Raw(JsonElement element) => element.GetRawText();

    /// <summary>
    /// Writes a flag straight to the row, because the builder API refuses to create an unknown
    /// one - which is the situation being simulated: a flag another build wrote.
    /// </summary>
    private static async Task SetFlagDirectlyAsync(
        WebApplicationFactory<Program> factory,
        string roomKey,
        Func<FlagSet, FlagSet> edit)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DikuWebDbContext>();

        var key = RoomKey.Parse(roomKey);
        var room = await db.Rooms.FirstAsync(r => r.Key == key);

        // A new instance rather than a mutation in place: the column is a converted value, so a
        // change EF cannot see by reference would not be written.
        room.Flags = edit(room.Flags);
        await db.SaveChangesAsync();
    }

    private static async Task<bool> HasFlagDirectlyAsync(
        WebApplicationFactory<Program> factory,
        string roomKey,
        string flag)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DikuWebDbContext>();

        var key = RoomKey.Parse(roomKey);
        var room = await db.Rooms.AsNoTracking().FirstAsync(r => r.Key == key);

        return room.Flags.Has(flag);
    }
}
