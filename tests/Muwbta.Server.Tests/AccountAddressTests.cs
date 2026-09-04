using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// Where an account was created from and last signed in from, as the admin panel sees it.
/// </summary>
/// <remarks>
/// Through a trusted proxy on purpose: the whole point of recording an address is defeated if the
/// address recorded is the proxy's, which is what every account would have shown before the
/// forwarded headers were honoured. The test server presents loopback, so loopback is the proxy
/// and the caller is whatever <c>X-Forwarded-For</c> says.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AccountAddressTests(PostgresFixture postgres) : IDisposable
{
    private MuwbtaAppFactory? _behindProxy;

    private MuwbtaAppFactory BehindProxy => _behindProxy ??= new MuwbtaAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string> { ["Proxy:KnownProxies"] = "127.0.0.1" });

    public void Dispose() => _behindProxy?.Dispose();

    [Fact]
    public async Task Registration_and_each_sign_in_record_the_callers_address()
    {
        var factory = BehindProxy;

        using var player = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var username = "addr" + Guid.NewGuid().ToString("N")[..10];

        var registered = await SendAsync(player, HttpMethod.Post, "/api/auth/register", "203.0.113.7", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });
        registered.EnsureSuccessStatusCode();

        var signedIn = await SendAsync(player, HttpMethod.Post, "/api/auth/login", "203.0.113.8", new
        {
            username,
            password = "correcthorse",
        });
        signedIn.EnsureSuccessStatusCode();

        using var admin = await AdminClientAsync(factory);

        var summary = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/accounts/{username}");
        Assert.Equal("203.0.113.7", summary.GetProperty("registeredFromAddress").GetString());
        Assert.Equal("203.0.113.8", summary.GetProperty("lastLoginAddress").GetString());

        // And the question a ban raises next: who else came from there.
        var byAddress = await admin.GetFromJsonAsync<JsonElement>("/api/admin/accounts?address=203.0.113.8");
        Assert.Contains(
            byAddress.EnumerateArray(),
            a => a.GetProperty("username").GetString() == username);

        var nobody = await admin.GetFromJsonAsync<JsonElement>("/api/admin/accounts?address=203.0.113.99");
        Assert.DoesNotContain(
            nobody.EnumerateArray(),
            a => a.GetProperty("username").GetString() == username);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string forwardedFor, object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    private static async Task<HttpClient> AdminClientAsync(MuwbtaAppFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var name = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, name, AccountRole.Admin);
        await client.PostAsJsonAsync("/api/auth/login", new { username = name, password = "correcthorse" });
        return client;
    }
}
