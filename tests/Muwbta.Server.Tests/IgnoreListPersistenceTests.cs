using System.Net.Http.Json;
using Muwbta.Engine;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// An ignore list survives the trip through the save queue to the database.
/// </summary>
/// <remarks>
/// The snapshot is the whole of what reaches the database, and the queue's apply step is where
/// three fields have already been silently dropped in this project's history — gold, the bind
/// point, and quest flags all went missing there. So this does not stop at the snapshot: it
/// pushes one through the real queue and reads the row back.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class IgnoreListPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_ignore_list_is_saved_and_read_back()
    {
        var factory = postgres.App;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await BuilderClient.RegisterAsync(client);

        var name = BuilderClient.UniqueName("Ign");
        var created = await client.PostAsJsonAsync("/api/characters", new { name, path = "Warden" });
        created.EnsureSuccessStatusCode();

        await using var db = postgres.CreateDbContext();
        var character = await db.Characters.SingleAsync(c => c.Name == name);

        character.IgnoredNames.Add("Bram");
        character.IgnoredNames.Add("Vurn");

        var queue = factory.Services.GetRequiredService<ICharacterSaveQueue>();
        queue.Enqueue(CharacterSnapshot.From(character, DateTimeOffset.UtcNow));
        await queue.FlushAsync(CancellationToken.None);

        await using var fresh = postgres.CreateDbContext();
        var saved = await fresh.Characters.AsNoTracking().SingleAsync(c => c.Id == character.Id);

        Assert.Equal(["Bram", "Vurn"], saved.IgnoredNames);
    }
}
