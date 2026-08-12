using System.Net;
using System.Net.Http.Json;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Changing your own password (PLAN.md §7.7) — including the half that is easy to leave out,
/// which is that the sessions opened with the old password stop working.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class PasswordChangeTests(PostgresFixture postgres)
{
    private const string Original = "correcthorse";
    private const string Replacement = "batterystaple9";

    private HttpClient NewClient() =>
        postgres.App.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static Task<HttpResponseMessage> ChangeAsync(
        HttpClient client, string current, string next) =>
        client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = current,
            newPassword = next,
        });

    private Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { username, password });

    [Fact]
    public async Task An_anonymous_caller_cannot_change_a_password()
    {
        using var client = NewClient();

        var response = await ChangeAsync(client, Original, Replacement);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_new_password_works_and_the_old_one_stops()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterAsync(client);

        var change = await ChangeAsync(client, Original, Replacement);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using var withOld = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(withOld, username, Original)).StatusCode);

        using var withNew = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(withNew, username, Replacement)).StatusCode);
    }

    [Fact]
    public async Task The_current_password_is_required_and_a_wrong_one_changes_nothing()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterAsync(client);

        var response = await ChangeAsync(client, "notmypassword", Replacement);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The point of the assertion: a rejected attempt must not have written anything.
        using var other = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(other, username, Original)).StatusCode);
    }

    [Fact]
    public async Task A_password_below_the_minimum_length_is_refused()
    {
        using var client = NewClient();
        await BuilderClient.RegisterAsync(client);

        var response = await ChangeAsync(client, Original, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_session_that_changed_the_password_stays_signed_in()
    {
        // Otherwise the first person signed out by a password change is the one who made it.
        using var client = NewClient();
        await BuilderClient.RegisterAsync(client);

        await ChangeAsync(client, Original, Replacement);

        var me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Every_other_session_is_signed_out_by_the_change()
    {
        // The case this exists for is "somebody else has my password" - which a change that left
        // their fortnight-long cookie working would not fix.
        using var first = NewClient();
        var username = await BuilderClient.RegisterAsync(first);

        using var second = NewClient();
        (await LoginAsync(second, username, Original)).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.OK,
            (await second.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);

        await ChangeAsync(first, Original, Replacement);

        var after = await second.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_session_opened_after_the_change_is_unaffected_by_it()
    {
        // The guard is a stamp comparison, not a timestamp cutoff: a cookie minted after the
        // change carries the new stamp and has to keep working, including across the renewal
        // that sliding expiry performs.
        using var client = NewClient();
        var username = await BuilderClient.RegisterAsync(client);

        await ChangeAsync(client, Original, Replacement);

        using var fresh = NewClient();
        (await LoginAsync(fresh, username, Replacement)).EnsureSuccessStatusCode();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await fresh.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);
        }
    }
}
