using System.Net;
using System.Net.Http.Json;
using Muwbta.Server.Game;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// An account may have <see cref="SessionRegistryOptions.MaxCharactersPerAccount"/> characters.
/// </summary>
/// <remarks>
/// <para>
/// This cap did not exist until the configuration for it was found not to work. The shipped
/// compose files had set <c>Sessions__MaxCharactersPerAccount</c> for months, and the server had
/// no roster limit of any kind — the key bound to nothing, so an account could create characters
/// without end while the deployment read as though it were capped.
/// </para>
/// <para>
/// It is a different limit from <c>MaxConcurrentCharactersPerAccount</c>, which bounds how many
/// may be <em>in the world at once</em> because each holds an SSE connection. Confusing the two is
/// what produced the dead key, so both are tested and both are named in full wherever they appear.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class CharacterRosterCapTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>Letters only: names are validated against ^[A-Za-z]{3,16}$.</summary>
    private static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return prefix + new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
    }

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        response.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/api/characters", new { name, path = "Warden" });

    [Fact]
    public async Task An_account_may_fill_its_roster_and_no_further()
    {
        var factory = postgres.App;
        var cap = factory.Services.GetRequiredService<SessionRegistryOptions>().MaxCharactersPerAccount;

        using var client = NewClient(factory);
        await RegisterAsync(client, UniqueName("roster").ToLowerInvariant());

        for (var i = 0; i < cap; i++)
        {
            var allowed = await CreateAsync(client, UniqueName("Ros"));
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var refused = await CreateAsync(client, UniqueName("Ros"));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // The refusal has to say what to do about it. "Conflict" alone is indistinguishable from
        // the name already being taken, which is the other 409 this endpoint returns.
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("limit", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delete one", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bad_name_is_reported_before_the_roster_is_counted()
    {
        // Somebody at the cap who also mistyped a name should be told about the name. The cap is
        // the part they can do nothing about in this request, and reporting it first would send
        // them off to delete a character they did not need to delete.
        var factory = postgres.App;
        var cap = factory.Services.GetRequiredService<SessionRegistryOptions>().MaxCharactersPerAccount;

        using var client = NewClient(factory);
        await RegisterAsync(client, UniqueName("order").ToLowerInvariant());

        for (var i = 0; i < cap; i++)
        {
            (await CreateAsync(client, UniqueName("Ord"))).EnsureSuccessStatusCode();
        }

        var response = await CreateAsync(client, "x");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("3-16 letters", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cap_is_per_account_rather_than_global()
    {
        // Two accounts each filling a roster must both succeed. A count that forgot its WHERE
        // clause would pass every test above and fail the moment a second player registered.
        var factory = postgres.App;

        using var first = NewClient(factory);
        await RegisterAsync(first, UniqueName("apart").ToLowerInvariant());
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(first, UniqueName("Apa"))).StatusCode);

        using var second = NewClient(factory);
        await RegisterAsync(second, UniqueName("bpart").ToLowerInvariant());
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(second, UniqueName("Bpa"))).StatusCode);
    }
}
