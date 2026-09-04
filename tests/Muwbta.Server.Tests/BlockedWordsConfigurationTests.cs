using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Engine;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// The word list is part of a configuration: written from the builder, read back, and live on
/// the loop the moment that configuration is activated.
/// </summary>
/// <remarks>
/// Its own host, because activating a configuration changes what the running loop obeys — the
/// starting room as well as the list — and the shared host's other tests assume the starter one.
/// The matching itself is tested on <c>WordFilter</c> and the five speech doors in the Engine;
/// this is the plumbing from the panel to the options, and the one door the Engine cannot see,
/// which is a new character's name.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class BlockedWordsConfigurationTests(PostgresFixture postgres) : IDisposable
{
    private readonly MuwbtaAppFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_list_is_written_read_back_and_live_once_activated()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(_factory, client);

        var key = $"words-{Guid.NewGuid():N}"[..24];

        var created = await client.PostAsJsonAsync($"/api/builder/configurations/{key}", new
        {
            name = "With words",
            description = "Authored by a test.",
            startingRoomKey = "aldenmoor.millbrook.north-gate",
            welcomeMessage = "Welcome, {name}.",
            blockedWords = "blort\nzarg",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var listed = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/builder/configurations", UriKind.Relative));
        var mine = listed.GetProperty("configurations").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == key);
        Assert.Equal("blort\nzarg", mine.GetProperty("blockedWords").GetString());

        // Not live merely by existing.
        var options = _factory.Services.GetRequiredService<EngineOptions>();
        Assert.True(options.WordFilter.IsEmpty);

        var activated = await client.PostAsync(
            new Uri($"/api/builder/configurations/{key}/activate", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

        // Live on the options the loop is holding, with no restart.
        Assert.Equal("blort\nzarg", options.BlockedWords);
        Assert.True(options.WordFilter.Matches("what a zarg", out _));
    }

    [Fact]
    public async Task A_name_on_the_active_list_cannot_be_a_character()
    {
        // The one door the Engine cannot see. Set directly rather than through activation, so
        // this test says nothing about the plumbing the one above already covers.
        _factory.Services.GetRequiredService<EngineOptions>().BlockedWords = "blort";

        using var client = NewClient();
        await BuilderClient.RegisterAsync(client);

        var refused = await client.PostAsJsonAsync("/api/characters", new { name = "Blort", path = "Warden" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Whole words: a name that merely contains one is somebody's name.
        var allowed = await client.PostAsJsonAsync("/api/characters", new { name = "Blortimer", path = "Warden" });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
}
