using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Playtest.Plans;
using DikuWeb.Playtest.Targets;

namespace DikuWeb.Playtest.Building;

/// <summary>
/// Checks that the content a plan needs is in the world, and builds what is not.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> The starter seeder lays down rooms and nothing else, so every plan that
/// meets a mob, buys something, or picks something up used to depend on content a person had built
/// by hand and written down in PLAYTEST.md. That is a footnote nobody reads until a plan fails, and
/// the failure it produces is the confusing kind: the transcript shows a player standing in an
/// empty room typing at nobody, which reads exactly like a broken game.
///
/// <b>Through the builder API, never SQL.</b> A running server plays an in-memory world owned by
/// one loop thread and loaded at boot (PLAN.md §2.1). A builder edit is a mutation queued into that
/// loop, applied, and written through to Postgres — so it reaches the live world at once. An INSERT
/// reaches storage and nothing else: the row would be invisible until a restart, and the plan would
/// run against a server that really does not have the mob. Same door as the builder, same
/// guarantees, and the apparatus stays a client.
///
/// <b>Idempotent by key, and it never reconciles.</b> Anything already there is left exactly as it
/// is, including where its numbers differ from the plan's. A run that quietly re-pointed somebody's
/// hand-built shopkeeper at a plan's markup would be editing a world it was invited to observe.
/// The report says found or made, and a plan reading oddly against pre-existing content is a
/// question for a person, which is what the whole apparatus is for.
/// </remarks>
public sealed class FixtureProvisioner(HttpClient builder)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Makes sure everything in <paramref name="fixtures"/> exists, creating what does not.
    /// </summary>
    public async Task<IReadOnlyList<FixtureOutcome>> EnsureAsync(
        WorldFixtures fixtures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        var outcomes = new List<FixtureOutcome>();

        // Zones before rooms, rooms before spawners, items before mobs. Each layer is named by the
        // one above it: a room carries a zone key, a spawner is refused for a room that is not
        // there, and a shop stocking an item key nothing defines lists a hole.
        foreach (var zone in fixtures.Zones)
        {
            outcomes.Add(await EnsureZoneAsync(zone, cancellationToken));
        }

        foreach (var room in fixtures.Rooms)
        {
            outcomes.Add(await EnsureRoomAsync(room, cancellationToken));
        }

        foreach (var item in fixtures.Items)
        {
            outcomes.Add(await EnsureItemAsync(item, cancellationToken));
        }

        foreach (var mob in fixtures.Mobs)
        {
            outcomes.Add(await EnsureMobAsync(mob, cancellationToken));
        }

        return outcomes;
    }

    // -----------------------------------------------------------------------
    // Zones
    // -----------------------------------------------------------------------

    private async Task<FixtureOutcome> EnsureZoneAsync(
        ZoneFixture zone,
        CancellationToken cancellationToken)
    {
        var what = $"zone {zone.Key}";

        if (zone.Key.Split('.') is not [var worldKey, _])
        {
            return FixtureOutcome.Blocked(what, "a zone key must be 'world.zone'");
        }

        if (await ExistsAsync($"/api/builder/zones/{zone.Key}", cancellationToken))
        {
            return FixtureOutcome.Found(what);
        }

        // The world above it has to be there. A plan inventing a whole world is a plan authoring
        // the game rather than playing it, so this stops here and says which world is missing.
        if (!await ExistsAsync($"/api/builder/worlds/{worldKey}", cancellationToken))
        {
            return FixtureOutcome.Blocked(what, $"there is no world '{worldKey}' to put it in");
        }

        var created = await builder.PostAsJsonAsync(
            $"/api/builder/zones/{zone.Key}",
            new
            {
                worldKey,
                name = Named(zone.Name, zone.Key),
                description = zone.Description ?? string.Empty,
            },
            Json,
            cancellationToken);

        return created.IsSuccessStatusCode
            ? FixtureOutcome.Made(what)
            : FixtureOutcome.Blocked(what, await WhyAsync(created, cancellationToken));
    }

    // -----------------------------------------------------------------------
    // Rooms
    // -----------------------------------------------------------------------

    private async Task<FixtureOutcome> EnsureRoomAsync(
        RoomFixture room,
        CancellationToken cancellationToken)
    {
        var what = $"room {room.Key}";

        if (string.IsNullOrWhiteSpace(room.Key))
        {
            return FixtureOutcome.Blocked(what, "a room fixture needs a key");
        }

        if (await ExistsAsync($"/api/builder/rooms/{room.Key}", cancellationToken))
        {
            return FixtureOutcome.Found(what);
        }

        // Verify-only unless the plan said where the room lies. A room made from nothing has no
        // exit into it, so it is one no player can walk to - a fixture that reports success and
        // leaves the plan exactly as broken.
        if (string.IsNullOrWhiteSpace(room.From) || string.IsNullOrWhiteSpace(room.Direction))
        {
            return FixtureOutcome.Blocked(
                what,
                "it is not in the world, and the fixture does not say which room to dig it from");
        }

        if (!await ExistsAsync($"/api/builder/rooms/{room.From}", cancellationToken))
        {
            return FixtureOutcome.Blocked(what, $"there is no room '{room.From}' to dig from");
        }

        var body = new
        {
            direction = room.Direction,
            reciprocal = true,
            newRoomKey = room.Key,

            // Null unless the plan crosses a boundary, in which case the far room belongs to the
            // far zone - the exit is ordinary, and the difficulty on the other side of it is not.
            zoneKey = room.Zone,
        };

        var dug = await builder.PostAsJsonAsync(
            $"/api/builder/rooms/{room.From}/dig", body, Json, cancellationToken);

        // `dig` is throttled to one every couple of seconds per account (DigThrottle), which is a
        // rule about a builder's hands rather than about a machine provisioning a plan. Waiting it
        // out once is honest and enough: a chain of rooms is dug one link at a time anyway.
        if (dug.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);
            dug = await builder.PostAsJsonAsync(
                $"/api/builder/rooms/{room.From}/dig", body, Json, cancellationToken);
        }

        if (!dug.IsSuccessStatusCode)
        {
            return FixtureOutcome.Blocked(what, await WhyAsync(dug, cancellationToken));
        }

        if (room.Title is not null || room.Description is not null)
        {
            await builder.PatchAsJsonAsync(
                $"/api/builder/rooms/{room.Key}",
                new { title = room.Title, description = room.Description },
                Json,
                cancellationToken);
        }

        return FixtureOutcome.Made(what, $"dug {room.Direction} from {room.From}");
    }

    // -----------------------------------------------------------------------
    // Items
    // -----------------------------------------------------------------------

    private async Task<FixtureOutcome> EnsureItemAsync(
        ItemFixture item,
        CancellationToken cancellationToken)
    {
        var what = $"item {item.Key}";

        if (string.IsNullOrWhiteSpace(item.Key))
        {
            return FixtureOutcome.Blocked(what, "an item fixture needs a key");
        }

        var existed = await ExistsAsync($"/api/builder/item-templates/{item.Key}", cancellationToken);

        if (!existed)
        {
            var created = await builder.PostAsJsonAsync(
                $"/api/builder/item-templates/{item.Key}",
                new
                {
                    name = Named(item.Name, item.Key),
                    description = item.Description ?? string.Empty,
                    icon = item.Icon ?? "$",
                    slots = item.Slot is null ? Array.Empty<string>() : [item.Slot],
                    baseValue = item.Value,
                    isQuestItem = item.QuestItem,
                },
                Json,
                cancellationToken);

            if (!created.IsSuccessStatusCode)
            {
                return FixtureOutcome.Blocked(what, await WhyAsync(created, cancellationToken));
            }
        }

        if (string.IsNullOrWhiteSpace(item.Room))
        {
            return existed ? FixtureOutcome.Found(what) : FixtureOutcome.Made(what, "template");
        }

        var spawner = await EnsureSpawnerAsync(
            item.Key, "Item", item.Room, item.Count, sentinel: false, cancellationToken);

        return Combine(what, existed, spawner);
    }

    // -----------------------------------------------------------------------
    // Mobs
    // -----------------------------------------------------------------------

    private async Task<FixtureOutcome> EnsureMobAsync(
        MobFixture mob,
        CancellationToken cancellationToken)
    {
        var what = $"mob {mob.Key}";

        if (string.IsNullOrWhiteSpace(mob.Key))
        {
            return FixtureOutcome.Blocked(what, "a mob fixture needs a key");
        }

        var existed = await ExistsAsync($"/api/builder/mob-templates/{mob.Key}", cancellationToken);

        if (!existed)
        {
            // The behavior bag is assembled the way the builder's editor assembles it: keys that
            // say nothing are left out entirely, because absence is the neutral value everywhere
            // it is read (§4.10, §4.13) and a stored default is a decision nobody made.
            var behavior = new Dictionary<string, object> { ["type"] = mob.Disposition };

            if (mob.Shopkeeper)
            {
                behavior["shopkeeper"] = true;
                behavior["sells"] = mob.Sells.ToList();

                if (mob.Markup > 0m)
                {
                    behavior["markup"] = mob.Markup;
                }
            }

            var created = await builder.PostAsJsonAsync(
                $"/api/builder/mob-templates/{mob.Key}",
                new
                {
                    name = Named(mob.Name, mob.Key),
                    description = mob.Description ?? string.Empty,
                    icon = mob.Icon ?? "m",
                    level = mob.Level,
                    baseStats = new Dictionary<string, object> { ["health"] = mob.Health },
                    baseXp = mob.Xp,
                    baseGold = mob.Gold,
                    behavior,
                },
                Json,
                cancellationToken);

            if (!created.IsSuccessStatusCode)
            {
                return FixtureOutcome.Blocked(what, await WhyAsync(created, cancellationToken));
            }
        }

        if (string.IsNullOrWhiteSpace(mob.Room))
        {
            return existed ? FixtureOutcome.Found(what) : FixtureOutcome.Made(what, "template");
        }

        // Sentinel always. A fixture that wanders off between provisioning and the plan reaching
        // it fails for reasons about mob AI, and every plan that names a room means "it is there".
        var spawner = await EnsureSpawnerAsync(
            mob.Key, "Mob", mob.Room, mob.Count, sentinel: true, cancellationToken);

        return Combine(what, existed, spawner);
    }

    // -----------------------------------------------------------------------
    // Spawners
    // -----------------------------------------------------------------------

    /// <summary>
    /// One spawner for this template in this room, made only if there is not one already.
    /// </summary>
    /// <remarks>
    /// Matched on template and room rather than on an id the plan would have to carry, because the
    /// spawner is an implementation detail of "one of these stands here". Without the check a run
    /// would add a spawner every time it was run, and the room would fill up with smiths - the
    /// population target is per spawner, so ten spawners is ten smiths.
    /// </remarks>
    private async Task<FixtureOutcome> EnsureSpawnerAsync(
        string templateKey,
        string kind,
        string room,
        int count,
        bool sentinel,
        CancellationToken cancellationToken)
    {
        var what = $"spawner for {templateKey} in {room}";
        var zoneKey = ZoneOf(room);

        if (zoneKey is null)
        {
            return FixtureOutcome.Blocked(what, $"'{room}' is not a world.zone.room key");
        }

        var listed = await builder.GetAsync(
            new Uri($"/api/builder/spawners?zone={zoneKey}", UriKind.Relative), cancellationToken);

        if (listed.IsSuccessStatusCode)
        {
            var existing = await listed.Content.ReadFromJsonAsync<List<SpawnerRow>>(
                Json, cancellationToken);

            if (existing?.Any(s =>
                    string.Equals(s.TemplateKey, templateKey, StringComparison.OrdinalIgnoreCase)
                    && s.RoomKeys is not null
                    && s.RoomKeys.Contains(room, StringComparer.OrdinalIgnoreCase)) == true)
            {
                return FixtureOutcome.Found(what);
            }
        }

        var created = await builder.PostAsJsonAsync(
            "/api/builder/spawners",
            new
            {
                zoneKey,
                templateKey,
                templateKind = kind,
                roomKeys = new[] { room },
                targetCount = Math.Max(1, count),
                respawnSeconds = 30,
                sentinel,
            },
            Json,
            cancellationToken);

        return created.IsSuccessStatusCode
            ? FixtureOutcome.Made(what, $"{count} in {room}") with { SpawnPending = true }
            : FixtureOutcome.Blocked(what, await WhyAsync(created, cancellationToken));
    }

    // -----------------------------------------------------------------------
    // Plumbing
    // -----------------------------------------------------------------------

    private async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        var response = await builder.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        return response.StatusCode != HttpStatusCode.NotFound && response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Rolls a template result and its spawner result into one line about the fixture.
    /// </summary>
    /// <remarks>
    /// A reader cares whether the thing is now standing there, not which of its two halves was
    /// missing. A blocked spawner is the whole fixture blocked, because a template with nothing
    /// spawning it is a mob no plan can meet.
    /// </remarks>
    private static FixtureOutcome Combine(string what, bool templateExisted, FixtureOutcome spawner) =>
        spawner.State switch
        {
            FixtureState.Blocked => FixtureOutcome.Blocked(what, spawner.Detail ?? "spawner refused"),
            FixtureState.Made => FixtureOutcome.Made(
                    what, templateExisted ? "spawner only" : "template and spawner")
                with { SpawnPending = true },
            _ => templateExisted
                ? FixtureOutcome.Found(what)
                : FixtureOutcome.Made(what, "template; spawner already there"),
        };

    /// <summary>The <c>world.zone</c> half of a room key, which is what a spawner is scoped by.</summary>
    private static string? ZoneOf(string roomKey)
    {
        var parts = roomKey.Split('.');
        return parts.Length == 3 ? $"{parts[0]}.{parts[1]}" : null;
    }

    private static string Named(string name, string key) =>
        string.IsNullOrWhiteSpace(name) ? key : name;

    /// <summary>Why the API refused, short enough for one line of a summary.</summary>
    private static async Task<string> WhyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var trimmed = body.Trim();

        return trimmed.Length is > 0 and < 200
            ? $"{(int)response.StatusCode} {trimmed}"
            : $"{(int)response.StatusCode} {response.StatusCode}";
    }

    private sealed record SpawnerRow(string TemplateKey, List<string>? RoomKeys);
}

/// <summary>What happened to one fixture.</summary>
public enum FixtureState
{
    /// <summary>Already in the world. Left exactly as it was.</summary>
    Found,

    /// <summary>Not there, and now is.</summary>
    Made,

    /// <summary>Not there, and could not be created. The plan will play without it.</summary>
    Blocked,
}

/// <summary>One line of the provisioning report.</summary>
/// <remarks>
/// Blocked is deliberately not an exception. A plan whose content could not be built is still worth
/// playing — the transcript shows what a player meets in a world that does not have it, which is
/// occasionally the more interesting reading, and always better than a run that produced nothing.
/// </remarks>
public sealed record FixtureOutcome(FixtureState State, string What, string? Detail)
{
    /// <summary>
    /// A spawner was created, so the thing it makes is not standing there yet.
    /// </summary>
    /// <remarks>
    /// A spawner is a population rule rather than an instance: the loop's spawn sweep is what
    /// turns it into a mob in a room, and that runs every 60 pulses. A plan starting inside that
    /// window walks into an empty room — which is exactly what the first fixtured run produced,
    /// <em>"You don't see 'rat' here."</em> and then, two seconds later, <em>"A rat appears."</em>
    /// Nothing was wrong with the fixture, the plan, or the game; the run was simply faster than
    /// the world. The caller waits out one sweep when this is set.
    /// </remarks>
    public bool SpawnPending { get; init; }


    public static FixtureOutcome Found(string what) => new(FixtureState.Found, what, null);

    public static FixtureOutcome Made(string what, string? detail = null) =>
        new(FixtureState.Made, what, detail);

    public static FixtureOutcome Blocked(string what, string why) =>
        new(FixtureState.Blocked, what, why);

    public override string ToString() => State switch
    {
        FixtureState.Found => $"found {What}",
        FixtureState.Made => Detail is null ? $"made {What}" : $"made {What} ({Detail})",
        _ => $"COULD NOT make {What}: {Detail}",
    };
}
