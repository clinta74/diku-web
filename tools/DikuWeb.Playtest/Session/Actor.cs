using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Playtest.Recording;
using DikuWeb.Playtest.Targets;

namespace DikuWeb.Playtest.Session;

/// <summary>
/// One character the apparatus is driving: its own account, its own stream, its own scrollback.
/// </summary>
/// <remarks>
/// An account each, not a character each. Sessions are keyed by character so one account could
/// drive three, but sharing one would also share the mute state, the role, and the per-account cap
/// — so a moderation plan would silence its own observer, and a four-actor plan would fail on the
/// cap for reasons that have nothing to do with what it was testing.
/// </remarks>
public sealed class Actor : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly Transcript _transcript;
    private readonly CancellationTokenSource _pumpStop = new();

    private SseStream? _stream;
    private Task? _pump;

    private Actor(
        string role,
        string characterName,
        string username,
        string password,
        Guid characterId,
        HttpClient client,
        Transcript transcript)
    {
        Role = role;
        CharacterName = characterName;
        Username = username;
        Password = password;
        CharacterId = characterId;
        _client = client;
        _transcript = transcript;
    }

    /// <summary>What the plan calls this actor.</summary>
    public string Role { get; }

    /// <summary>
    /// What the world calls them, which is not always the same thing.
    /// </summary>
    /// <remarks>
    /// Character names are globally unique (3–16 letters, no digits), so a plan run twice against
    /// the same server cannot have "Theron" both times. The preferred name is tried first — against
    /// a fresh database it is free, and the transcript reads exactly as a hand-played session would
    /// — and a letter suffix is added only when it is taken.
    /// </remarks>
    public string CharacterName { get; }

    public string Username { get; }

    public string Password { get; }

    public Guid CharacterId { get; private set; }

    /// <summary>Whether the world will accept commands from this actor.</summary>
    public bool IsStreaming => _pump is { IsCompleted: false };

    /// <summary>
    /// Registers an account, creates a character, enters the world, and opens the stream.
    /// </summary>
    public static async Task<Actor> ArriveAsync(
        IGameTarget target,
        Transcript transcript,
        string role,
        CharacterPath path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(transcript);

        var client = target.NewClient();
        var username = Names.Unique();
        const string password = "correcthorse";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"{username}@playtest.invalid", username, password },
            cancellationToken);

        if (!register.IsSuccessStatusCode)
        {
            throw new PlaytestException(
                $"Could not register an account for '{role}': " +
                $"{(int)register.StatusCode} {await Body(register, cancellationToken)}");
        }

        var (characterId, characterName) =
            await CreateCharacterAsync(client, role, path, cancellationToken);

        var actor = new Actor(
            role, characterName, username, password, characterId, client, transcript);

        transcript.Add(role, EntryKind.Meta,
            $"registered as {username}, playing {characterName} the {path}");

        await actor.EnterAsync(cancellationToken);
        return actor;
    }

    /// <summary>
    /// Takes the preferred name where it is free, and a suffixed one where it is not.
    /// </summary>
    private static async Task<(Guid Id, string Name)> CreateCharacterAsync(
        HttpClient client,
        string role,
        CharacterPath path,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in Names.Candidates(role))
        {
            var response = await client.PostAsJsonAsync(
                "/api/characters",
                new { name = candidate, path = path.ToString() },
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return (body.RootElement.GetProperty("id").GetGuid(), candidate);
            }

            // Taken. Try the next candidate; anything else is a real failure.
            if (response.StatusCode != HttpStatusCode.Conflict)
            {
                throw new PlaytestException(
                    $"Could not create a character for '{role}': " +
                    $"{(int)response.StatusCode} {await Body(response, cancellationToken)}");
            }
        }

        throw new PlaytestException(
            $"Every candidate name for '{role}' was taken. Names are globally unique and only " +
            "letters are allowed, so this means the server has an improbable number of them.");
    }

    /// <summary>Enters the world and starts pumping the stream into the transcript.</summary>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        var enter = await _client.PostAsJsonAsync(
            $"/api/game/{CharacterId}/enter", new { }, cancellationToken);

        if (!enter.IsSuccessStatusCode)
        {
            throw new PlaytestException(
                $"'{Role}' could not enter the world: " +
                $"{(int)enter.StatusCode} {await Body(enter, cancellationToken)}");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{CharacterId}/stream");

        var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PlaytestException(
                $"'{Role}' entered the world but could not open a stream: " +
                $"{(int)response.StatusCode} {await Body(response, cancellationToken)}");
        }

        _stream = new SseStream(await response.Content.ReadAsStreamAsync(cancellationToken));
        _pump = Task.Run(() => PumpAsync(_pumpStop.Token), CancellationToken.None);
    }

    /// <summary>
    /// Reads this actor's stream for as long as it lives, recording everything.
    /// </summary>
    /// <remarks>
    /// Continuously, and on its own task. Reading only while a step waits would lose everything
    /// that arrived while a command was being posted — which is most of the output, since the
    /// server answers a command asynchronously over this same stream (PLAN.md §3.3). It is also
    /// what makes a second actor's view of the first actor's actions land at the right time.
    /// </remarks>
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            await foreach (var frame in _stream.ReadAsync(cancellationToken))
            {
                if (FrameRenderer.Render(frame) is not { } rendered)
                {
                    continue;
                }

                _transcript.Add(Role, rendered.Kind, rendered.Text, frame.Data);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException)
        {
            _transcript.Add(Role, EntryKind.Meta, $"stream ended: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends what a player would have typed.
    /// </summary>
    /// <remarks>
    /// A 429 is retried rather than treated as a failure. The command endpoint is the one a player
    /// can hold down a key against and the only one the loop's budget cares about, so it is rate
    /// limited — and a plan that fires a scripted sequence faster than a human types will hit that
    /// limit while testing something else entirely. <c>Retry-After</c> is honoured, since the
    /// server is the authority on how long the bucket needs.
    /// </remarks>
    public async Task SendAsync(string input, CancellationToken cancellationToken)
    {
        _transcript.Add(Role, EntryKind.Sent, input);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var response = await _client.PostAsJsonAsync(
                $"/api/game/{CharacterId}/command", new { input }, cancellationToken);

            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _transcript.Add(Role, EntryKind.Meta,
                        $"'{input}' was refused: {(int)response.StatusCode} " +
                        $"{await Body(response, cancellationToken)}");
                }

                return;
            }

            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            _transcript.Add(Role, EntryKind.Meta,
                $"rate limited; waiting {wait.TotalSeconds:0.#}s before retrying '{input}'");

            await Task.Delay(wait, cancellationToken);
        }

        _transcript.Add(Role, EntryKind.Meta, $"gave up on '{input}' after four attempts");
    }

    /// <summary>
    /// Signs in again, so a role granted since the last sign-in is actually in the cookie.
    /// </summary>
    /// <remarks>
    /// Not optional after a promotion. The role is a claim minted at sign-in, so an account
    /// promoted to Builder mid-session keeps a cookie that says Player until it signs in again —
    /// which reads as the promotion having silently failed.
    /// </remarks>
    public async Task RefreshRoleAsync(CancellationToken cancellationToken)
    {
        var login = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = Username, password = Password },
            cancellationToken);

        if (!login.IsSuccessStatusCode)
        {
            throw new PlaytestException(
                $"'{Role}' could not sign in again after promotion: {(int)login.StatusCode}.");
        }
    }

    /// <summary>The client this actor holds, for setup that is not a game command.</summary>
    public HttpClient Client => _client;

    public async ValueTask DisposeAsync()
    {
        await _pumpStop.CancelAsync();

        if (_pump is not null)
        {
            // The pump is blocked on a socket read; cancelling it is enough, and waiting for a
            // clean exit past a second is not worth holding the run up for.
            await Task.WhenAny(_pump, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        try
        {
            await _client.PostAsync(
                new Uri($"/api/game/{CharacterId}/leave", UriKind.Relative), null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Leaving is a courtesy — it frees the account's slot against the cap rather than
            // waiting out the 90-second link-dead window. A failure here costs nothing.
        }

        _pumpStop.Dispose();
    }

    private static async Task<string> Body(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "(no body)";
        }
    }
}

/// <summary>Something went wrong that the run cannot sensibly carry on past.</summary>
public sealed class PlaytestException : Exception
{
    public PlaytestException(string message) : base(message)
    {
    }

    public PlaytestException(string message, Exception inner) : base(message, inner)
    {
    }

    public PlaytestException()
    {
    }
}
