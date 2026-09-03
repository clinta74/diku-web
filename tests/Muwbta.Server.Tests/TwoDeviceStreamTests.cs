using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// One character opened on two devices (PLAN.md §3.6).
/// </summary>
/// <remarks>
/// Reported from play: signing in on a second device left the output arriving on neither screen
/// in full. It was not a race — a session's event channel is declared <c>SingleReader</c>, so two
/// SSE responses draining it get roughly half each, every time. The rule now is that the newest
/// connection holds the stream and the older one is told, in words, to stop.
///
/// The connection id is what makes any of this answerable: two devices playing one character send
/// byte-identical requests, so nothing else distinguishes the device that was replaced from the
/// device that replaced it. It is minted by the client and survives <c>EventSource</c>'s own
/// retries, because a retry re-requests the same URL.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class TwoDeviceStreamTests(PostgresFixture postgres)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return prefix + new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
    }

    private static async Task RegisterAsync(HttpClient client)
    {
        var username = UniqueName("acct").ToLowerInvariant();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateCharacterAsync(HttpClient client, string prefix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/characters",
            new { name = UniqueName(prefix), path = "Warden" });
        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>One device's stream, named the way a browser names it.</summary>
    private static async Task<SseStream> OpenStreamAsync(
        HttpClient client,
        Guid characterId,
        string connection)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/game/{characterId}/stream?connection={connection}");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return new SseStream(await response.Content.ReadAsStreamAsync());
    }

    private static bool IsDisplaced(IReadOnlyList<SseFrame> frames) =>
        frames.Any(f => f.EventType == "sys"
            && f.Json.TryGetProperty("kind", out var kind)
            && kind.GetString() == "displaced");

    /// <summary>
    /// Walks the character and reads the echo back off <paramref name="stream"/>.
    /// </summary>
    /// <remarks>
    /// The liveness check every case here needs, and deliberately not "wait for a room frame": a
    /// second stream on an existing session has nothing queued for it, because the room snapshot
    /// was sent when the character entered and the first device has already read it. Waiting for
    /// one is waiting for something the design says will not come.
    ///
    /// Directions alternate so a character that has walked east can walk back, which keeps a test
    /// that moves twice from being refused by the map rather than by anything under test.
    /// </remarks>
    private static async Task<bool> WalksAsync(
        HttpClient client,
        Guid characterId,
        SseStream stream,
        string direction)
    {
        await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = direction });

        var expected = $"You walk {direction}";

        var frames = await stream.ReadUntilAsync(
            f => f.HasText(expected) || IsDisplaced(f), EventTimeout);

        return frames.HasText(expected);
    }

    /// <summary>A character in the world, with its first device already streaming.</summary>
    private static async Task<(HttpClient Client, Guid CharacterId, SseStream First)> ArriveAsync(
        WebApplicationFactory<Program> factory)
    {
        var client = NewClient(factory);
        await RegisterAsync(client);

        var characterId = await CreateCharacterAsync(client, "Dev");
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        var first = await OpenStreamAsync(client, characterId, "device-one");
        await first.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        return (client, characterId, first);
    }

    [Fact]
    public async Task The_newest_device_gets_the_output_and_gets_all_of_it()
    {
        // The report, as a test. Before this both devices read one SingleReader channel and each
        // received about half the frames, so neither screen showed a whole exchange.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;
        await using var firstStream = first;

        await using var second = await OpenStreamAsync(client, characterId, "device-two");

        Assert.True(
            await WalksAsync(client, characterId, second, "east"),
            "The newest device did not receive its output.");
    }

    [Fact]
    public async Task A_second_device_entering_turns_the_first_one_out_at_once()
    {
        // The reported sequence, exactly, and the one the first attempt at this got wrong.
        // Entering builds a *new* session, so the older stream's channel was completed and its
        // response simply ended - indistinguishable, from the browser, from a dropped network.
        // It reconnected, took the character back off the device that had just claimed it, and
        // the two swapped roles from then on.
        //
        // The older screen is now told at the moment the newer one *enters*, before it has opened
        // a stream at all. Entering is the decision; the stream only carries it out.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;
        await using var firstStream = first;

        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });

        var frames = await firstStream.ReadUntilAsync(IsDisplaced, EventTimeout);

        Assert.True(IsDisplaced(frames),
            "The first device was not told when a second one entered - it will reconnect.");
    }

    [Fact]
    public async Task The_device_that_was_turned_out_cannot_take_the_character_back()
    {
        // The whole reported symptom, end to end. Both halves matter: the old device is refused,
        // *and* the new one still has the character afterwards. Asserting only the first would
        // pass on an implementation that simply broke both streams.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;
        await using var firstStream = first;

        // The second device: enter, then stream, which is what the client does.
        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });
        await using var second = await OpenStreamAsync(client, characterId, "device-two");
        Assert.True(await WalksAsync(client, characterId, second, "east"));

        // The first device's browser retrying the URL it already had.
        await using var retry = await OpenStreamAsync(client, characterId, "device-one");
        var refused = await retry.ReadUntilAsync(IsDisplaced, EventTimeout);

        Assert.True(IsDisplaced(refused), "The turned-out device took the character back.");

        Assert.True(
            await WalksAsync(client, characterId, second, "west"),
            "The device holding the character lost it to the other one's retry.");
    }

    [Fact]
    public async Task The_displaced_device_is_told_rather_than_left_guessing()
    {
        // A screen that simply stops is indistinguishable from a broken game. It gets a sys frame
        // naming what happened, which is also the signal the client uses to stop reconnecting.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;
        await using var firstStream = first;

        await using var second = await OpenStreamAsync(client, characterId, "device-two");

        var frames = await firstStream.ReadUntilAsync(IsDisplaced, EventTimeout);

        Assert.True(IsDisplaced(frames), "The displaced device was never told why it went quiet.");
    }

    [Fact]
    public async Task A_displaced_connection_reconnecting_is_turned_away()
    {
        // The ping-pong guard, and the reason a connection id exists at all. EventSource retries
        // by itself after three seconds, so without this the replaced device would come back,
        // displace the device that replaced it, and the two would trade the stream indefinitely.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;
        await using var firstStream = first;

        await using var second = await OpenStreamAsync(client, characterId, "device-two");
        Assert.True(await WalksAsync(client, characterId, second, "east"));

        // The first device's browser, retrying the same URL it was opened with.
        await using var retry = await OpenStreamAsync(client, characterId, "device-one");
        var frames = await retry.ReadUntilAsync(IsDisplaced, EventTimeout);

        Assert.True(IsDisplaced(frames), "A replaced device was served the stream again.");

        // And the device that holds it still has it - the refusal must cost the live screen
        // nothing, which is the half a ping-pong would break.
        Assert.True(
            await WalksAsync(client, characterId, second, "west"),
            "The live device lost the stream to a retry.");
    }

    [Fact]
    public async Task The_same_device_reconnecting_is_not_read_as_a_takeover()
    {
        // The other half of the rule, and the one that must not break: a dropped connection comes
        // back under the id it already had, which is a screen resuming rather than a new device.
        // Turning it away would lock a player out of their own character after one bad packet.
        //
        // Asserted as "not refused" rather than "and output flows", because whether output flows
        // is a different mechanism entirely: going link-dead completes the session's channel
        // (GameLoop.HandleLeave), so *any* reconnect is silent until the character enters again.
        // That is why the client's Rejoin calls `enter`, and it is unchanged by this.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;

        // The connection drops.
        await first.DisposeAsync();

        await using var again = await OpenStreamAsync(client, characterId, "device-one");
        var frames = await again.ReadUntilAsync(IsDisplaced, TimeSpan.FromSeconds(2));

        Assert.False(IsDisplaced(frames), "A reconnect of the same device was read as a takeover.");
    }

    [Fact]
    public async Task Entering_again_puts_a_reconnected_device_back_in_touch()
    {
        // The path the client actually takes out of a drop, end to end: the stream is gone, the
        // character is link-dead, and Rejoin enters again before opening a fresh stream.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;

        await first.DisposeAsync();

        await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { });
        await using var again = await OpenStreamAsync(client, characterId, "device-one-again");

        Assert.True(
            await WalksAsync(client, characterId, again, "east"),
            "A character that entered again was still not receiving output.");
    }

    [Fact]
    public async Task Being_displaced_does_not_take_the_character_out_of_the_world()
    {
        // A displaced stream must not report link-dead: the character is standing there being
        // played on another screen, and the room would be told they had gone still.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;
        await using var firstStream = first;

        await using var second = await OpenStreamAsync(client, characterId, "device-two");
        await firstStream.ReadUntilAsync(IsDisplaced, EventTimeout);

        var sessions = await client.GetFromJsonAsync<JsonElement>("/api/game/sessions");

        Assert.Equal(1, sessions.GetArrayLength());
        Assert.True(sessions[0].GetProperty("streaming").GetBoolean(),
            "The session stopped counting as streaming after the older device was displaced.");
    }

    [Fact]
    public async Task A_client_that_names_no_connection_is_still_served()
    {
        // Backwards compatible on purpose. An older client loses only the ability to be told
        // apart from its successor; the single-reader guarantee does not depend on the id.
        var (client, characterId, first) = await ArriveAsync(postgres.App);
        using var _ = client;
        await using var firstStream = first;

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var plain = new SseStream(await response.Content.ReadAsStreamAsync());

        Assert.True(
            await WalksAsync(client, characterId, plain, "east"),
            "A client sending no connection id was refused.");
    }
}
