using System.Net;
using System.Net.Http.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// Neither an account nor a character may be called something that reads as staff.
/// </summary>
/// <remarks>
/// The rule itself is tested in the Domain; this proves both doors apply it. Two doors, because a
/// username is what an admin sees in the panel and a character name is what every other player
/// sees in the room — and a name that passes one and not the other is a name that gets used.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ReservedNameTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("moderator_1")]
    [InlineData("staff")]
    public async Task A_reserved_username_cannot_be_registered(string username)
    {
        using var client = NewClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{Guid.NewGuid():N}@example.test",
            username,
            password = "correcthorse",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Moderator")]
    [InlineData("Adminah")]
    public async Task A_reserved_character_name_cannot_be_created(string name)
    {
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/characters", new { name, path = "Warden" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient NewClient() =>
        postgres.App.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = NewClient();
        var username = "rsv" + Guid.NewGuid().ToString("N")[..10];

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        response.EnsureSuccessStatusCode();
        return client;
    }
}
