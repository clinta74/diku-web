using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Muwbta.Server.Building;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// An import costs a pulse per batch, not a pulse per entity (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The importer used to await each entity's loop round trip before submitting the next, and the loop
/// drains on a 250ms pulse — so the cost was a quarter second an entity however fast the database
/// was. The Reaches bundle is 1005 entities: four and a bit minutes, essentially all of it waiting,
/// and longer than any sensible proxy will hold a request open. A 504 then stopped the import where
/// it stood, with no report.
/// </para>
/// <para>
/// <b>These are wall-clock assertions, which is unusual and deliberate.</b> The defect was not a
/// wrong answer — every one of the existing round-trip tests passed throughout — it was the time
/// taken, so time is the only thing that can catch it. The ceilings are set several times above what
/// the batched path needs and far below what the per-entity path would take, so they fail on an
/// architectural regression rather than on a slow machine.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ImportBatchingTests(PostgresFixture postgres)
{
    /// <summary>Rooms in the test bundle. Over two batches of 128, so the chunking is exercised.</summary>
    private const int Rooms = 200;

    /// <summary>
    /// What the old path would have needed for <see cref="Rooms"/> rooms plus a world and a zone:
    /// one 250ms pulse each.
    /// </summary>
    private static readonly TimeSpan PerEntityCost = TimeSpan.FromMilliseconds(250 * (Rooms + 2));

    /// <summary>
    /// The ceiling. Generous — the batched path needs about two pulses of loop time plus the database
    /// work — but comfortably under <see cref="PerEntityCost"/>, which is the distinction that matters.
    /// </summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>
    /// A bundle of one world, one zone and <paramref name="rooms"/> rooms, each linked to the next so
    /// the exit phase is exercised too.
    /// </summary>
    /// <remarks>
    /// Built as a real <see cref="WorldBundle"/> and rendered by <see cref="BundleFormat.Write"/>,
    /// rather than as hand-written JSON. That way the fixture cannot drift from the shape the importer
    /// actually reads, and a field added to a bundle record is a compile error here rather than a
    /// silently-missing key.
    /// </remarks>
    /// <param name="brokenRoom">
    /// Index of one room to give an unparseable key, or null for a clean bundle. Built in here rather
    /// than patched into the rendered JSON by the caller: <see cref="BundleFormat.Write"/> indents, so
    /// a textual replace has to guess at the whitespace and silently matches nothing when it guesses
    /// wrong - which is exactly what it did.
    /// </param>
    private static string Bundle(string worldKey, string zoneKey, int rooms, int? brokenRoom = null)
    {
        using var empty = JsonDocument.Parse("{}");
        var flags = empty.RootElement.Clone();

        var roomList = new List<BundleRoom>(rooms);

        for (var i = 0; i < rooms; i++)
        {
            roomList.Add(new BundleRoom(
                Key: i == brokenRoom ? "not a room key" : $"{zoneKey}.r{i}",
                ZoneKey: zoneKey,
                Title: $"Room {i}",
                Description: "One of many.",
                Flags: flags,
                Grid: [],
                Legend: new Dictionary<string, string>(),
                EditorX: i % 20,
                EditorY: i / 20,

                // Linked to the next, so the exits phase gets a batch of its own to chunk.
                Exits: i + 1 < rooms ? [new BundleExit("north", $"{zoneKey}.r{i + 1}")] : []));
        }

        return BundleFormat.Write(new WorldBundle(
            FormatVersion: WorldBundle.CurrentFormatVersion,
            ExportedAt: DateTimeOffset.UnixEpoch,
            Scope: new BundleScope("all", null),
            Worlds: [new BundleWorld(worldKey, "Timing", "For the batching test.", 0, flags,
                new Dictionary<string, decimal>())],
            Zones: [new BundleZone(zoneKey, worldKey, "Timing Zone", "For the batching test.", 1, 5,
                flags, new Dictionary<string, decimal>())],
            Rooms: roomList,
            ItemTemplates: [],
            MobTemplates: [],
            Abilities: [],
            Spawners: [],
            Quests: [],
            Configurations: []));
    }

    private static async Task<(JsonElement Report, TimeSpan Took)> ImportAsync(
        HttpClient client,
        string bundleJson,
        bool dryRun = false)
    {
        var started = Stopwatch.GetTimestamp();

        var response = await client.PostAsync(
            new Uri($"/api/builder/import?dryRun={dryRun}", UriKind.Relative),
            new StringContent(bundleJson, Encoding.UTF8, "application/json"));

        var took = Stopwatch.GetElapsedTime(started);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MultiStatus,
            $"{(int)response.StatusCode}: {body}");

        return (JsonDocument.Parse(body).RootElement, took);
    }

    /// <summary>
    /// <b>The test that would have caught this.</b> Two hundred rooms and their exits, in well under
    /// the fifty seconds the per-entity path would have spent on the rooms alone.
    /// </summary>
    [Fact]
    public async Task A_bundle_of_two_hundred_rooms_imports_in_seconds()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var worldKey = BuilderClient.UniqueName("timing").ToLowerInvariant();
        var (report, took) = await ImportAsync(client, Bundle(worldKey, $"{worldKey}.zone", Rooms));

        Assert.Empty(report.GetProperty("failures").EnumerateArray());

        Assert.True(
            took < Ceiling,
            $"took {took.TotalSeconds:F1}s; a pulse per entity would have been about "
            + $"{PerEntityCost.TotalSeconds:F0}s and the ceiling is {Ceiling.TotalSeconds:F0}s");
    }

    /// <summary>
    /// Every room and every exit actually landed. The timing assertion above is worthless without
    /// this beside it — the fastest possible import is one that writes nothing.
    /// </summary>
    [Fact]
    public async Task Everything_in_the_bundle_lands()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var worldKey = BuilderClient.UniqueName("lands").ToLowerInvariant();
        var (report, _) = await ImportAsync(client, Bundle(worldKey, $"{worldKey}.zone", Rooms));

        var counts = report.GetProperty("counts").EnumerateArray()
            .ToDictionary(
                c => c.GetProperty("kind").GetString() ?? "",
                c => c.GetProperty("created").GetInt32() + c.GetProperty("updated").GetInt32());

        Assert.Equal(1, counts["world"]);
        Assert.Equal(1, counts["zone"]);
        Assert.Equal(Rooms, counts["room"]);
        Assert.Equal(Rooms - 1, counts["exit"]);
    }

    /// <summary>
    /// Re-importing the same bundle updates rather than creating, and is still fast — the batched path
    /// has to be quick on the update route as well, which is the one a real re-import takes.
    /// </summary>
    [Fact]
    public async Task Re_importing_is_updates_and_is_still_fast()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var worldKey = BuilderClient.UniqueName("again").ToLowerInvariant();
        var bundle = Bundle(worldKey, $"{worldKey}.zone", Rooms);

        await ImportAsync(client, bundle);
        var (report, took) = await ImportAsync(client, bundle);

        var rooms = report.GetProperty("counts").EnumerateArray()
            .Single(c => c.GetProperty("kind").GetString() == "room");

        Assert.Equal(0, rooms.GetProperty("created").GetInt32());
        Assert.Equal(Rooms, rooms.GetProperty("updated").GetInt32());
        Assert.True(took < Ceiling, $"re-import took {took.TotalSeconds:F1}s");
    }

    /// <summary>
    /// A dry run touches the loop not at all, so it was always fast — asserted so that a future
    /// change which starts sending rehearsals through the loop is noticed here rather than in a
    /// builder's browser.
    /// </summary>
    [Fact]
    public async Task A_dry_run_stays_fast_and_writes_nothing()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var worldKey = BuilderClient.UniqueName("rehearse").ToLowerInvariant();
        var (report, took) = await ImportAsync(
            client, Bundle(worldKey, $"{worldKey}.zone", Rooms), dryRun: true);

        Assert.True(report.GetProperty("dryRun").GetBoolean());
        Assert.True(took < Ceiling, $"dry run took {took.TotalSeconds:F1}s");

        // And nothing was written, which is the whole promise of a rehearsal.
        var after = await client.GetAsync(
            new Uri($"/api/builder/worlds/{worldKey}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    /// <summary>
    /// <b>One bad entity in a batch does not cost the rest.</b> The report still names precisely the
    /// one that failed, which is the property batching most easily loses: the outcomes come back as a
    /// list and have to be put back beside the keys they belong to.
    /// </summary>
    [Fact]
    public async Task A_refused_entity_in_a_batch_is_named_and_the_others_land()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var worldKey = BuilderClient.UniqueName("mixed").ToLowerInvariant();
        var zoneKey = $"{worldKey}.zone";

        // A room key the importer cannot parse, dropped in the middle of the batch rather than at
        // either end - a positional mix-up at the edges is easy to get right by accident.
        var (report, _) = await ImportAsync(client, Bundle(worldKey, zoneKey, 20, brokenRoom: 10));

        var failures = report.GetProperty("failures").EnumerateArray().ToList();
        var failed = Assert.Single(failures);
        Assert.Equal("not a room key", failed.GetProperty("key").GetString());

        // And the other nineteen are there.
        var rooms = report.GetProperty("counts").EnumerateArray()
            .Single(c => c.GetProperty("kind").GetString() == "room");

        Assert.Equal(19, rooms.GetProperty("created").GetInt32() + rooms.GetProperty("updated").GetInt32());
    }
}
