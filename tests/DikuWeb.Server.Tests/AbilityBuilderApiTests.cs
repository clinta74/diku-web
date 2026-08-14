using System.Net;
using System.Net.Http.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Authoring abilities through the builder API — the surface that makes "retune without a code
/// change" true rather than aspirational.
/// </summary>
/// <remarks>
/// The refusals are most of this. Everything else the builder API saves follows §7.4 and lets the
/// world be temporarily broken, because a dangling exit announces itself the moment somebody walks
/// into it. A broken ability does not: it spends its cost, starts its cooldown, and does nothing,
/// so the mistake reaches a player as "this spell feels weak". That is why the ability routes are
/// the one place that refuses on content grounds, and why each refusal is tested by making the
/// mistake rather than asserted in the abstract.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AbilityBuilderApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static object ValidBody(
        string path = "Warden",
        int unlockLevel = 6,
        int cost = 12,
        long cooldown = 24,
        string effectKey = "damage.physical",
        Dictionary<string, string>? effectParams = null) => new
        {
            path,
            unlockLevel,
            name = "Test Strike",
            description = "For testing.",
            costType = "Stamina",
            costValue = cost,
            cooldownPulses = cooldown,
            targetingType = "SingleTarget",
            effectKey,
            effectParams = effectParams
                ?? new Dictionary<string, string> { ["scalingFactor"] = "1.2", ["minDamage"] = "3" },
        };

    private static string NewKey() => $"warden.t{Guid.NewGuid():N}"[..24];

    [Fact]
    public async Task Abilities_are_closed_to_ordinary_players()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterAsync(client);

        var list = await client.GetAsync(new Uri("/api/builder/abilities", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task An_ability_can_be_created_and_read_back()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = NewKey();
        var created = await client.PostAsJsonAsync($"/api/builder/abilities/{key}", ValidBody());
        created.EnsureSuccessStatusCode();

        var read = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/abilities/{key}", UriKind.Relative)));

        Assert.Equal("Test Strike", read.GetProperty("name").GetString());
        Assert.Equal(6, read.GetProperty("unlockLevel").GetInt32());
        Assert.Equal(24, read.GetProperty("cooldownPulses").GetInt64());
    }

    [Fact]
    public async Task Enums_cross_the_wire_as_names_rather_than_ordinals()
    {
        // The gap that let a working API ship next to an empty screen. Every other test here
        // asserted values and status codes, none asserted the *shape* of an enum - so `path` came
        // back as 0, the client filtered `path === 'Warden'` against it, and the Abilities tab
        // rendered nothing while the server was entirely correct.
        //
        // Asserted on the raw JSON rather than through a typed client, because a typed client is
        // exactly what deserialises the difference away.
        //
        // Against an ability this test creates, not a seeded one. It first read warden.kick and
        // failed only in a full run: AbilityReconcileTests deliberately overwrites
        // AbilityCatalogue.All[0] - which is warden.kick - to prove a builder's edit survives a
        // restart, and leaves it that way. The suite shares one database, so a seeded row is
        // whatever the run has done to it by now.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = NewKey();
        (await client.PostAsJsonAsync($"/api/builder/abilities/{key}", ValidBody()))
            .EnsureSuccessStatusCode();

        var raw = await client.GetStringAsync(
            new Uri($"/api/builder/abilities/{key}", UriKind.Relative));

        Assert.Contains("\"path\":\"Warden\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"costType\":\"Stamina\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"targetingType\":\"SingleTarget\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retuned_cooldown_is_what_comes_back()
    {
        // The whole point of the exercise: change a number without touching code.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = NewKey();
        (await client.PostAsJsonAsync($"/api/builder/abilities/{key}", ValidBody(cooldown: 24)))
            .EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/abilities/{key}", new { cooldownPulses = 48 }))
            .EnsureSuccessStatusCode();

        var read = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/abilities/{key}", UriKind.Relative)));

        Assert.Equal(48, read.GetProperty("cooldownPulses").GetInt64());
    }

    [Fact]
    public async Task An_unknown_effect_key_is_refused()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.PostAsJsonAsync(
            $"/api/builder/abilities/{NewKey()}",
            ValidBody(effectKey: "damage.nonexistent"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_effect_missing_its_parameter_is_refused()
    {
        // "magnitude" instead of "scalingFactor" - the executor skips what it does not recognise,
        // so this would save cleanly and produce an ability that does nothing.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.PostAsJsonAsync(
            $"/api/builder/abilities/{NewKey()}",
            ValidBody(effectParams: new Dictionary<string, string> { ["magnitude"] = "1.2" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_key_naming_the_wrong_path_is_refused()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.PostAsJsonAsync(
            "/api/builder/abilities/shade.misfiled",
            ValidBody(path: "Warden"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_edit_that_would_break_a_working_ability_is_refused_and_changes_nothing()
    {
        // A refusal must not half-apply. The row has to still be castable afterwards.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = NewKey();
        (await client.PostAsJsonAsync($"/api/builder/abilities/{key}", ValidBody()))
            .EnsureSuccessStatusCode();

        var broken = await client.PatchAsJsonAsync(
            $"/api/builder/abilities/{key}",
            new { effectKey = "damage.nonexistent" });

        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);

        var read = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/abilities/{key}", UriKind.Relative)));

        Assert.Equal("damage.physical", read.GetProperty("effectKey").GetString());
    }

    [Fact]
    public async Task An_ability_can_be_deleted()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = NewKey();
        (await client.PostAsJsonAsync($"/api/builder/abilities/{key}", ValidBody()))
            .EnsureSuccessStatusCode();

        (await client.DeleteAsync(new Uri($"/api/builder/abilities/{key}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var read = await client.GetAsync(new Uri($"/api/builder/abilities/{key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task Creating_the_same_key_twice_conflicts()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = NewKey();
        (await client.PostAsJsonAsync($"/api/builder/abilities/{key}", ValidBody()))
            .EnsureSuccessStatusCode();

        var again = await client.PostAsJsonAsync($"/api/builder/abilities/{key}", ValidBody());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task The_list_reports_problems_on_rows_nobody_saved_through_the_api()
    {
        // The reason problems ride on the response rather than only coming back from a save: a row
        // can arrive by import or by hand, and then nobody ever saw a refusal. The shipped
        // catalogue is clean, so the assertion is that the field exists and is empty for a good
        // row - the machinery being present is the property, since a missing field would make the
        // editor silently unable to warn.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var read = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/abilities/warden.kick", UriKind.Relative)));

        Assert.Empty(read.GetProperty("problems").EnumerateArray());
    }
}
