using System.Net;
using System.Net.Http.Json;
using DikuWeb.Server.Tests.Infrastructure;

namespace DikuWeb.Server.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AuthFlowTests(PostgresFixture postgres)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16];

    private HttpClient NewClient()
    {
        var factory = new DikuWebAppFactory(postgres.ConnectionString);
        // Cookies are the whole auth mechanism here, so the handler must keep them.
        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
    }

    [Fact]
    public async Task Register_signs_the_new_account_in()
    {
        using var client = NewClient();
        var username = Unique("kael");

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The session cookie should already be in play, so /me works with no further login.
        var me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Usernames_are_case_insensitively_unique()
    {
        using var first = NewClient();
        var username = Unique("mira");

        await first.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        using var second = NewClient();
        var response = await second.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"other{username}@example.test",
            username = username.ToUpperInvariant(),
            password = "correcthorse",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_indistinguishable_from_an_unknown_user()
    {
        // Both must answer 401 with no body difference, or the endpoint becomes a way to
        // enumerate which usernames exist.
        using var client = NewClient();
        var username = Unique("tarn");

        await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        using var fresh = NewClient();

        var wrongPassword = await fresh.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = "wrongwrongwrong",
        });

        var unknownUser = await fresh.PostAsJsonAsync("/api/auth/login", new
        {
            username = Unique("nobody"),
            password = "correcthorse",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Short_passwords_are_rejected()
    {
        using var client = NewClient();
        var username = Unique("weak");

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "short",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_answer_401_rather_than_redirecting()
    {
        // Cookie auth defaults to a 302 toward a login page, which reaches a fetch() caller
        // as a confusing 200 full of HTML. Program.cs overrides that; this locks it in.
        using var factory = new DikuWebAppFactory(postgres.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(new Uri("/api/characters", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        using var client = NewClient();
        var username = Unique("gone");

        await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });

        var logout = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
}
