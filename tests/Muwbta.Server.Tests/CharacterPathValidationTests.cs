using System.Net;
using System.Net.Http.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// A character's path is one of the four, spelled by name.
/// </summary>
/// <remarks>
/// <c>Enum.TryParse</c> accepts any integer string, so <c>"42"</c> used to parse to a path that
/// is not one: a character with no abilities, a number where its path should be in every payload,
/// and every switch on it falling through to a default. Not a crash — the loop catches what a
/// handler throws — but an out-of-model row that the client's type union cannot represent. The
/// role endpoint had guarded against the same thing from the start; this one had not.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CharacterPathValidationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_path_given_as_a_number_is_refused()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/characters", new { name = UniqueName("num"), path = "42" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_path_given_by_name_in_any_case_is_accepted()
    {
        // The guard must not cost the case-insensitivity that was already there.
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/characters", new { name = UniqueName("low"), path = "warden" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = postgres.App.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var username = UniqueName("pathv");

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Letters only: names are validated against ^[A-Za-z]{3,16}$.</summary>
    private static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return prefix + new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
    }
}
