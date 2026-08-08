using System.Net.Http.Json;
using DikuWeb.Domain.Quests;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Quest progress has to survive a logout. <c>CharacterQuestSaveQueue</c> always wrote the rows,
/// but nothing ever read them back: <c>EnterWorld.Quests</c> was left at its default empty list,
/// so every login began with an empty journal and a completed non-repeatable quest could be
/// turned in again indefinitely. The loop cannot query a database (PLAN.md §2.1), so the only
/// place this can be fixed is the enter endpoint - which is exactly why it went unnoticed.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class QuestPersistenceTests(PostgresFixture postgres)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>Creates a character and enters the world, returning its id and open stream.</summary>
    private static async Task<(Guid CharacterId, SseStream Stream)> EnterAsync(HttpClient client)
    {
        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/characters",
            new { name = BuilderClient.UniqueName("Qp"), path = "Warden" }));

        var characterId = created.GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { })).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var stream = new SseStream(await response.Content.ReadAsStreamAsync());
        await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        return (characterId, stream);
    }

    [Fact]
    public async Task An_active_quest_is_still_in_the_journal_after_logging_back_in()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterAsync(client);

        var (characterId, stream) = await EnterAsync(client);

        var questKey = $"test.{BuilderClient.UniqueName("q").ToLowerInvariant()}";

        // Seed progress the way the save queue would have, then leave the world.
        await using (var db = postgres.CreateDbContext())
        {
            db.CharacterQuests.Add(new CharacterQuest
            {
                CharacterId = characterId,
                QuestKey = questKey,
                Status = QuestStatus.Active,
                StartedAt = DateTimeOffset.UtcNow,
                TimesCompleted = 0,
            });
            await db.SaveChangesAsync();
        }

        await stream.DisposeAsync();
        await client.PostAsJsonAsync($"/api/game/{characterId}/leave", new { });

        // Log back in. Before the fix, EnterWorld.Quests defaulted to [] and this was empty.
        (await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { })).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var second = new SseStream(await response.Content.ReadAsStreamAsync());
        await second.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        await client.PostAsJsonAsync($"/api/game/{characterId}/command", new { input = "quests" });

        var frames = await second.ReadUntilAsync(f => f.HasText(questKey), EventTimeout);
        Assert.True(frames.HasText(questKey));
    }
}
