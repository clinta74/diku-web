using System.Net;
using System.Net.Http.Json;
using DikuWeb.Domain.Accounts;

namespace DikuWeb.Playtest.Targets;

/// <summary>
/// A server already running somewhere: a dev box, staging, or anything else reachable by URL.
/// </summary>
/// <remarks>
/// The primary mode, because the point of a playtest is the world somebody actually built. The
/// starter seeder lays down rooms and nothing else — no mob templates, no items, no spawners — so
/// the rats and shopkeepers a plan wants to meet only exist on a server whose builder has been
/// used. Running against that server is the only way to play the game as it is.
/// </remarks>
public sealed class RemoteTarget(Uri baseAddress, AdminCredentials? admin = null) : IGameTarget
{
    private readonly List<HttpClient> _clients = [];
    private HttpClient? _adminClient;

    public Uri BaseAddress { get; } = baseAddress;

    public string Describe() =>
        $"remote {BaseAddress}" + (admin is null ? " (no admin credential)" : $" as {admin.Username}");

    /// <summary>
    /// A client with its own cookie container, which is what makes it a separate account.
    /// </summary>
    /// <remarks>
    /// <c>UseCookies</c> defaults to true, but the container is shared per handler — so actors
    /// built on one handler would share a session and every one of them would be the last to log
    /// in. A handler each is the whole isolation mechanism.
    /// </remarks>
    public HttpClient NewClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false,
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = BaseAddress,

            // Longer than any single request should need, but finite. The SSE stream is read with
            // HttpCompletionOption.ResponseHeadersRead so this does not cap the stream's life.
            Timeout = TimeSpan.FromSeconds(30),
        };

        _clients.Add(client);
        return client;
    }

    /// <summary>
    /// Promotes through the admin API, signing the apparatus's own admin in first.
    /// </summary>
    /// <remarks>
    /// There is no "promote me" endpoint, deliberately, so this needs a real admin account supplied
    /// in configuration. Without one the honest answer is that the plan cannot be set up — see
    /// <see cref="PromotionResult"/> for why that is a result rather than a throw.
    /// </remarks>
    public async Task<PromotionResult> PromoteAsync(
        string username,
        AccountRole role,
        CancellationToken cancellationToken)
    {
        if (admin is null)
        {
            return PromotionResult.Refused(
                $"'{username}' needs the {role} role, but no admin credential was supplied. " +
                "Pass --admin-user and --admin-password, or run this plan with --hosted.");
        }

        if (_adminClient is null)
        {
            _adminClient = NewClient();

            var login = await _adminClient.PostAsJsonAsync(
                "/api/auth/login",
                new { username = admin.Username, password = admin.Password },
                cancellationToken);

            if (!login.IsSuccessStatusCode)
            {
                _adminClient = null;
                return PromotionResult.Refused(
                    $"Could not sign in as '{admin.Username}': {(int)login.StatusCode} {login.StatusCode}.");
            }
        }

        var response = await _adminClient.PatchAsJsonAsync(
            $"/api/admin/accounts/{username}/role",
            new { role = role.ToString() },
            cancellationToken);

        return response.IsSuccessStatusCode
            ? PromotionResult.Ok
            : PromotionResult.Refused(
                $"The admin API refused to make '{username}' a {role}: " +
                $"{(int)response.StatusCode} {response.StatusCode}.");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _clients.Clear();
        return ValueTask.CompletedTask;
    }
}

/// <summary>An existing admin account the apparatus borrows to promote its own throwaway ones.</summary>
public sealed record AdminCredentials(string Username, string Password);
