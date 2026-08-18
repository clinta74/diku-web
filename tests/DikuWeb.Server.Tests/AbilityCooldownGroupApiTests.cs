using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// A shared timer survives the builder API and a bundle round trip (PLAN.md §4.5).
/// </summary>
/// <remarks>
/// The field is nullable and null is a real value — "shares no timer" — which is what makes the
/// two halves worth testing separately. Setting one is the easy direction; clearing one is where a
/// coalesced <c>?? existing</c> would leave a builder with a timer they could never take off.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AbilityCooldownGroupApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<JsonElement> AbilityAsync(HttpClient client, string key) =>
        await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/abilities/{key}", UriKind.Relative)));

    /// <summary>A fresh Warden ability, so the shared suite's other tests are not disturbed.</summary>
    private static async Task<string> AuthorAsync(HttpClient client, int? group)
    {
        var key = $"warden.timer{Guid.NewGuid():N}"[..22];

        (await client.PostAsJsonAsync($"/api/builder/abilities/{key}", new
        {
            path = "Warden",
            unlockLevel = 4,
            name = "Timed Thing",
            description = "Authored for the shared-timer tests.",
            costType = "Stamina",
            costValue = 9,
            cooldownPulses = 16,
            cooldownGroup = group,
            targetingType = "SingleTarget",
            effects = new[]
            {
                new
                {
                    key = "damage.physical",
                    @params = new Dictionary<string, string> { ["scalingFactor"] = "1.1" },
                },
            },
        })).EnsureSuccessStatusCode();

        return key;
    }

    [Fact]
    public async Task An_ability_can_be_created_on_a_timer()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var key = await AuthorAsync(client, group: 3);

        Assert.Equal(3, (await AbilityAsync(client, key)).GetProperty("cooldownGroup").GetInt32());
    }

    [Fact]
    public async Task An_ability_with_no_timer_reports_none()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var key = await AuthorAsync(client, group: null);

        Assert.Equal(
            JsonValueKind.Null,
            (await AbilityAsync(client, key)).GetProperty("cooldownGroup").ValueKind);
    }

    [Fact]
    public async Task A_timer_survives_a_save_and_a_reload()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var key = await AuthorAsync(client, group: null);

        (await client.PatchAsJsonAsync(
            $"/api/builder/abilities/{key}",
            new { cooldownGroup = 5 })).EnsureSuccessStatusCode();

        Assert.Equal(5, (await AbilityAsync(client, key)).GetProperty("cooldownGroup").GetInt32());
    }

    /// <summary>
    /// <b>The direction that a coalesced update would break.</b> Null means "shares no timer", so
    /// <c>?? existing</c> on the way in would make a timer impossible to take off — the same trap
    /// the cast time beside it already carries a comment about.
    /// </summary>
    [Fact]
    public async Task A_timer_can_be_cleared_again()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var key = await AuthorAsync(client, group: 6);

        (await client.PatchAsJsonAsync(
            $"/api/builder/abilities/{key}",
            new { cooldownGroup = (int?)null })).EnsureSuccessStatusCode();

        Assert.Equal(
            JsonValueKind.Null,
            (await AbilityAsync(client, key)).GetProperty("cooldownGroup").ValueKind);
    }

    /// <summary>
    /// A timer is a positive number or nothing at all — zero is a second spelling of "no timer" and
    /// an author who typed it would reasonably expect it to mean something.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task A_timer_that_is_not_a_positive_number_is_refused(int group)
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var key = await AuthorAsync(client, group: null);

        var response = await client.PatchAsJsonAsync(
            $"/api/builder/abilities/{key}", new { cooldownGroup = group });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A timer with one ability on it refuses nothing, so the list says so — the only place a
    /// builder would find out, since it is not a save-time error.
    /// </summary>
    [Fact]
    public async Task A_timer_with_only_one_ability_on_it_is_reported_in_the_listing()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        // A number nothing else in the shared database is likely to be using.
        var key = await AuthorAsync(client, group: 991);

        var listed = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/abilities", UriKind.Relative)));

        var mine = listed.EnumerateArray().Single(a => a.GetProperty("key").GetString() == key);
        var problems = mine.GetProperty("problems").EnumerateArray()
            .Select(p => p.GetProperty("message").GetString() ?? string.Empty)
            .ToList();

        Assert.Contains(problems, m => m.Contains("timer 991", StringComparison.Ordinal));
    }
}
