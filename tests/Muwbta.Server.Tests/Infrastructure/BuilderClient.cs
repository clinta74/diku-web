using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests.Infrastructure;

/// <summary>
/// Registration helpers for the builder tests.
/// </summary>
/// <remarks>
/// Roles cannot be granted over the API - there is no "promote me" endpoint, deliberately -
/// so a test writes the row and signs in again. Signing in again is not optional: the role
/// lands in the auth cookie as a claim at sign-in, so a promotion that skipped it would leave
/// the client holding a cookie that still says Player.
/// </remarks>
internal static class BuilderClient
{
    public static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return prefix + new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
    }

    public static async Task<string> RegisterAsync(HttpClient client)
    {
        var username = UniqueName("acct").ToLowerInvariant();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        response.EnsureSuccessStatusCode();
        return username;
    }

    /// <summary>Registers, promotes to Builder in the database, and signs in again.</summary>
    public static async Task<string> RegisterBuilderAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client)
    {
        var username = await RegisterAsync(client);
        await SetRoleAsync(factory, username, AccountRole.Builder);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password = "correcthorse" });

        login.EnsureSuccessStatusCode();
        return username;
    }

    public static async Task SetRoleAsync(
        WebApplicationFactory<Program> factory,
        string username,
        AccountRole role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var account = await db.Accounts.FirstAsync(a => a.Username == username);
        account.Role = role;
        await db.SaveChangesAsync();
    }

    /// <summary>Creates an isolated world and zone so tests never collide over shared keys.</summary>
    public static async Task<(string WorldKey, string ZoneKey)> NewZoneAsync(HttpClient client)
    {
        var worldKey = UniqueName("w").ToLowerInvariant();
        var zoneKey = $"{worldKey}.zone";

        (await client.PostAsJsonAsync($"/api/builder/worlds/{worldKey}", new
        {
            name = "Test World",
            description = "Made by a test.",
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            worldKey,
            name = "Test Zone",
            description = "Made by a test.",
        })).EnsureSuccessStatusCode();

        return (worldKey, zoneKey);
    }

    public static async Task<string> NewRoomAsync(HttpClient client, string zoneKey, string slug)
    {
        var key = $"{zoneKey}.{slug}";

        (await client.PostAsJsonAsync($"/api/builder/rooms/{key}", new
        {
            zoneKey,
            title = $"The {slug} room",
            description = "A room made by a test.",
        })).EnsureSuccessStatusCode();

        return key;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
