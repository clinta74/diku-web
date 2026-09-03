using System.Net;
using System.Net.Http.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Persistence;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// The in-game admin verbs through the whole round trip (PLAN.md §7.7): command → loop →
/// queue → worker → database → <c>Notify</c> back to the session that asked.
///
/// That reply is the part worth testing against a real stack. The loop cannot read the account
/// store, so the command can only ever enqueue; without the notification an admin typing
/// <c>promote</c> gets silence whether it worked, failed, or named nobody.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AdminCommandFlowTests(PostgresFixture postgres)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>Registers at the given role, enters the world, and opens the SSE stream.</summary>
    private static async Task<(string Username, Guid CharacterId, SseStream Stream)> PlayAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        AccountRole role)
    {
        var username = await BuilderClient.RegisterAsync(client);

        if (role != AccountRole.Player)
        {
            await BuilderClient.SetRoleAsync(factory, username, role);
            await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });
        }

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            "/api/characters",
            new { name = BuilderClient.UniqueName("Ad"), path = "Warden" }));

        var characterId = created.GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/game/{characterId}/enter", new { })).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/game/{characterId}/stream");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var stream = new SseStream(await response.Content.ReadAsStreamAsync());
        await stream.ReadUntilAsync(f => f.Any(x => x.EventType == "room"), EventTimeout);

        return (username, characterId, stream);
    }

    [Fact]
    public async Task Promoting_from_the_command_line_changes_the_row_and_reports_back()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        var (_, adminCharacter, stream) = await PlayAsync(factory, admin, AccountRole.Admin);
        await using var _ = stream;

        var username = await BuilderClient.RegisterAsync(target);

        await admin.PostAsJsonAsync(
            $"/api/game/{adminCharacter}/command",
            new { input = $"promote {username} builder" });

        var frames = await stream.ReadUntilAsync(f => f.HasSys("is now Builder"), EventTimeout);
        Assert.True(frames.HasSys("is now Builder"), "The admin was never told what happened.");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();
        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Username == username);

        Assert.Equal(AccountRole.Builder, account.Role);
    }

    [Fact]
    public async Task Promoting_somebody_who_does_not_exist_says_so()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);

        var (_, characterId, stream) = await PlayAsync(factory, admin, AccountRole.Admin);
        await using var _ = stream;

        await admin.PostAsJsonAsync(
            $"/api/game/{characterId}/command",
            new { input = "promote nobodyatall builder" });

        var frames = await stream.ReadUntilAsync(f => f.HasSys("no account named"), EventTimeout);
        Assert.True(frames.HasSys("no account named"));
    }

    [Fact]
    public async Task An_admin_cannot_demote_themselves_from_the_command_line_either()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);

        var (username, characterId, stream) = await PlayAsync(factory, admin, AccountRole.Admin);
        await using var _ = stream;

        await admin.PostAsJsonAsync(
            $"/api/game/{characterId}/command",
            new { input = $"demote {username}" });

        var frames = await stream.ReadUntilAsync(f => f.HasSys("cannot reduce your own role"), EventTimeout);
        Assert.True(frames.HasSys("cannot reduce your own role"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();
        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Username == username);

        Assert.Equal(AccountRole.Admin, account.Role);
    }

    [Fact]
    public async Task Whois_answers_with_the_account_and_its_characters()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        var (_, characterId, stream) = await PlayAsync(factory, admin, AccountRole.Admin);
        await using var _ = stream;

        // The target never enters the world, which is the point: whois names an account.
        var username = await BuilderClient.RegisterAsync(target);
        await target.PostAsJsonAsync("/api/characters", new
        {
            name = BuilderClient.UniqueName("Off"),
            path = "Adept",
        });

        await admin.PostAsJsonAsync(
            $"/api/game/{characterId}/command", new { input = $"whois {username}" });

        var frames = await stream.ReadUntilAsync(f => f.HasSys(username), EventTimeout);
        Assert.True(frames.HasSys(username));
        Assert.True(frames.HasSys("Player"));
    }

    [Fact]
    public async Task A_promoted_character_already_in_the_world_gains_the_verbs_without_relogging()
    {
        // PlayerActor.Role is a copy taken at EnterWorld, so without SetActorRole the promotion
        // would not reach somebody already playing.
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        var (_, adminCharacter, adminStream) = await PlayAsync(factory, admin, AccountRole.Admin);
        await using var _ = adminStream;

        var (username, targetCharacter, targetStream) = await PlayAsync(factory, target, AccountRole.Player);
        await using var __ = targetStream;

        // Before: an unknown verb, because a player must not learn these exist.
        await target.PostAsJsonAsync(
            $"/api/game/{targetCharacter}/command", new { input = "rflag" });

        var before = await targetStream.ReadUntilAsync(
            f => f.HasText("not something you can do"), EventTimeout);
        Assert.True(before.HasText("not something you can do"));

        await admin.PostAsJsonAsync(
            $"/api/game/{adminCharacter}/command",
            new { input = $"promote {username} builder" });

        // The character is told, because it changes what their commands do.
        var granted = await targetStream.ReadUntilAsync(
            f => f.HasSys("granted building privileges"), EventTimeout);
        Assert.True(granted.HasSys("granted building privileges"));

        await target.PostAsJsonAsync(
            $"/api/game/{targetCharacter}/command", new { input = "rflag" });

        var after = await targetStream.ReadUntilAsync(f => f.HasText("Flags for"), EventTimeout);
        Assert.True(after.HasText("Flags for"), "The promotion never reached the character in world.");
    }

    [Fact]
    public async Task A_demoted_builder_loses_the_verbs_without_relogging()
    {
        // The direction that actually matters for security.
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        var (_, adminCharacter, adminStream) = await PlayAsync(factory, admin, AccountRole.Admin);
        await using var _ = adminStream;

        var (username, targetCharacter, targetStream) = await PlayAsync(factory, target, AccountRole.Builder);
        await using var __ = targetStream;

        await target.PostAsJsonAsync($"/api/game/{targetCharacter}/command", new { input = "rflag" });
        var before = await targetStream.ReadUntilAsync(f => f.HasText("Flags for"), EventTimeout);
        Assert.True(before.HasText("Flags for"));

        await admin.PostAsJsonAsync(
            $"/api/game/{adminCharacter}/command", new { input = $"demote {username}" });

        var revoked = await targetStream.ReadUntilAsync(
            f => f.HasSys("privileges have been removed"), EventTimeout);
        Assert.True(revoked.HasSys("privileges have been removed"));

        await target.PostAsJsonAsync($"/api/game/{targetCharacter}/command", new { input = "rflag" });

        var after = await targetStream.ReadUntilAsync(
            f => f.HasText("not something you can do"), EventTimeout);
        Assert.True(after.HasText("not something you can do"), "A demoted builder kept their verbs.");
    }

    [Fact]
    public async Task A_builder_typing_promote_gets_an_unknown_verb_and_nothing_happens()
    {
        var factory = postgres.App;
        using var builder = NewClient(factory);
        using var target = NewClient(factory);

        var (_, characterId, stream) = await PlayAsync(factory, builder, AccountRole.Builder);
        await using var _ = stream;

        var username = await BuilderClient.RegisterAsync(target);

        await builder.PostAsJsonAsync(
            $"/api/game/{characterId}/command", new { input = $"promote {username} admin" });

        var frames = await stream.ReadUntilAsync(f => f.HasText("not something you can do"), EventTimeout);
        Assert.True(frames.HasText("not something you can do"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();
        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Username == username);

        Assert.Equal(AccountRole.Player, account.Role);
    }

    [Fact]
    public async Task A_freshly_promoted_builder_can_reach_the_builder_api_too()
    {
        // The in-game verbs and the HTTP surface must agree - a builder who can dig from the
        // command line but gets a 403 from the panel would be a confusing half-promotion.
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        var (_, adminCharacter, stream) = await PlayAsync(factory, admin, AccountRole.Admin);
        await using var _ = stream;

        var username = await BuilderClient.RegisterAsync(target);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await target.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative))).StatusCode);

        await admin.PostAsJsonAsync(
            $"/api/game/{adminCharacter}/command",
            new { input = $"promote {username} builder" });

        await stream.ReadUntilAsync(f => f.HasSys("is now Builder"), EventTimeout);

        Assert.Equal(
            HttpStatusCode.OK,
            (await target.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative))).StatusCode);
    }
}
