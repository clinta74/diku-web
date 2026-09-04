using System.Net.Http.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// The flags on the session cookie, checked on the wire rather than in the options.
/// </summary>
/// <remarks>
/// <b>Why Production is asked for by name.</b> The cookie's <c>Secure</c> flag used to follow the
/// request scheme, and the request always reached Kestrel as plain HTTP from the nginx in front —
/// so the flag was never set on any deployment with a proxy, which is every deployment. It is now
/// unconditional in Production. The other environments keep following the scheme, because the
/// dev client and this test host both talk plain HTTP to localhost and a cookie the browser
/// refuses to send back over HTTP is a sign-in that does not stick.
///
/// Read from <c>Set-Cookie</c> rather than through a cookie container, which would hide a
/// <c>Secure</c> cookie set over HTTP by never sending it back — the very behaviour this asserts.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SessionCookieTests(PostgresFixture postgres)
{
    [Fact]
    public async Task In_production_the_session_cookie_is_marked_secure()
    {
        using var factory = new MuwbtaAppFactory(postgres.ConnectionString, environment: "Production");
        var cookie = await RegisterAndReadCookieAsync(factory);

        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Outside_production_the_flag_follows_the_request_scheme()
    {
        // Plain HTTP to the test host, so no flag - the shape development runs in.
        var cookie = await RegisterAndReadCookieAsync(postgres.App);

        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> RegisterAndReadCookieAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var name = "cookie" + Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{name}@example.test",
            username = name,
            password = "a-long-enough-password",
        });

        response.EnsureSuccessStatusCode();

        return Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            v => v.StartsWith("muwbta.session=", StringComparison.Ordinal));
    }
}
