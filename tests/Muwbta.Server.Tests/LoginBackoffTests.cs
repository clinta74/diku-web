using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// Repeated wrong passwords against one account are made to wait, whoever is sending them.
/// </summary>
/// <remarks>
/// The per-address limit bounds one machine; this bounds one <em>account</em>, which is the
/// thing a guesser with many machines is actually attacking. The arithmetic is unit-tested in
/// <see cref="LoginThrottleTests"/>; these prove the endpoints consult it, that a success clears
/// it, and that an admin can see it and lift it — the cap and the lift being what keep "hammer an
/// account" from turning into "own an account".
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class LoginBackoffTests(PostgresFixture postgres) : IDisposable
{
    /// <summary>Three wrong guesses, then a one-second pause - short enough to wait out in a test.</summary>
    private MuwbtaAppFactory? _quick;

    private MuwbtaAppFactory Quick => _quick ??= new MuwbtaAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["Auth:LoginFailuresBeforeBackoff"] = "3",
            ["Auth:LoginBackoffSeconds"] = "1",
            ["Auth:LoginBackoffMaxSeconds"] = "1",
        });

    /// <summary>The same threshold with a pause long enough for an admin to find it.</summary>
    private MuwbtaAppFactory? _slow;

    private MuwbtaAppFactory Slow => _slow ??= new MuwbtaAppFactory(
        postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["Auth:LoginFailuresBeforeBackoff"] = "3",
            ["Auth:LoginBackoffSeconds"] = "600",
            ["Auth:LoginBackoffMaxSeconds"] = "600",
        });

    public void Dispose()
    {
        _quick?.Dispose();
        _slow?.Dispose();
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { username, password });

    [Fact]
    public async Task After_the_threshold_the_next_attempt_is_told_to_wait_and_a_success_clears_it()
    {
        var factory = Quick;
        using var owner = NewClient(factory);
        var username = await BuilderClient.RegisterAsync(owner);

        using var guesser = NewClient(factory);

        for (var i = 0; i < 3; i++)
        {
            var wrong = await LoginAsync(guesser, username, "not-the-password");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // The fuse is lit by the third failure; the fourth attempt meets it.
        var refused = await LoginAsync(guesser, username, "not-the-password");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"), "A 429 must say when to try again.");

        // Waited out, the right password gets in - and forgives the count. Without that, the
        // owner's own successful sign-in would be one more failure away from the next pause.
        await Task.Delay(TimeSpan.FromMilliseconds(1300));
        var ok = await LoginAsync(guesser, username, "correcthorse");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var wrongAgain = await LoginAsync(guesser, username, "not-the-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAgain.StatusCode);
    }

    [Fact]
    public async Task An_unknown_name_is_slowed_like_a_known_one()
    {
        // Otherwise the throttle answers "does this account exist" by whether it ever fires.
        var factory = Quick;
        using var guesser = NewClient(factory);
        var nobody = "nobody" + Guid.NewGuid().ToString("N")[..8];

        for (var i = 0; i < 3; i++)
        {
            await LoginAsync(guesser, nobody, "guess");
        }

        var refused = await LoginAsync(guesser, nobody, "guess");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_see_the_pause_and_lift_it()
    {
        var factory = Slow;
        using var owner = NewClient(factory);
        var target = await BuilderClient.RegisterAsync(owner);

        using var guesser = NewClient(factory);
        for (var i = 0; i < 3; i++)
        {
            await LoginAsync(guesser, target, "not-the-password");
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await LoginAsync(guesser, target, "correcthorse")).StatusCode);

        using var admin = NewClient(factory);
        var adminName = await BuilderClient.RegisterAsync(admin);
        await BuilderClient.SetRoleAsync(factory, adminName, AccountRole.Admin);
        await LoginAsync(admin, adminName, "correcthorse");

        // Visible on the account, so the panel can show why somebody cannot get in.
        var summary = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/accounts/{target}");
        Assert.NotEqual(JsonValueKind.Null, summary.GetProperty("loginLockedUntil").ValueKind);

        var lifted = await admin.PostAsync($"/api/admin/accounts/{target}/unlock", content: null);
        Assert.Equal(HttpStatusCode.OK, lifted.StatusCode);

        // Straight back in, with no wait.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(guesser, target, "correcthorse")).StatusCode);

        // And lifting nothing says so, rather than pretending.
        var nothing = await admin.PostAsync($"/api/admin/accounts/{target}/unlock", content: null);
        Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);
    }
}
