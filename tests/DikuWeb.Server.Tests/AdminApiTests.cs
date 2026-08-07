using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DikuWeb.Domain.Accounts;
using DikuWeb.Persistence;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Role administration end to end (PLAN.md §7.7) — including the half that matters most, which
/// is that a demotion or a ban reaches a session that is already signed in.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AdminApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<string> RegisterAdminAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client)
    {
        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, AccountRole.Admin);
        await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });
        return username;
    }

    // -----------------------------------------------------------------------
    // Authorization
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_admin_api_is_closed_to_anonymous_callers()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var response = await client.GetAsync(new Uri("/api/admin/accounts", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_builder_cannot_reach_the_admin_api()
    {
        // Builders edit the world; they do not hand out access to it. If they could, the role
        // would be self-propagating and the boundary would mean nothing.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var read = await client.GetAsync(new Uri("/api/admin/accounts", UriKind.Relative));
        var write = await client.PatchAsJsonAsync(
            "/api/admin/accounts/somebody/role", new { role = "Admin" });

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Promotion
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_admin_can_promote_somebody_to_builder()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        await RegisterAdminAsync(factory, admin);
        var username = await BuilderClient.RegisterAsync(target);

        var response = await admin.PatchAsJsonAsync(
            $"/api/admin/accounts/{username}/role", new { role = "Builder" });

        var account = await BuilderClient.JsonAsync(response);
        Assert.Equal("Builder", account.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Promoting_writes_an_admin_audit_row_naming_who_did_it()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        var adminName = await RegisterAdminAsync(factory, admin);
        var username = await BuilderClient.RegisterAsync(target);

        await admin.PatchAsJsonAsync($"/api/admin/accounts/{username}/role", new { role = "Builder" });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DikuWebDbContext>();

        var actor = await db.Accounts.AsNoTracking().FirstAsync(a => a.Username == adminName);
        var subject = await db.Accounts.AsNoTracking().FirstAsync(a => a.Username == username);

        var row = await db.AdminAudits.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TargetAccountId == subject.Id);

        Assert.NotNull(row);
        Assert.Equal(actor.Id, row.ActorAccountId);
        Assert.Equal(AdminAction.RoleChanged, row.Action);
        Assert.Equal("Player", row.Before);
        Assert.Equal("Builder", row.After);
    }

    [Fact]
    public async Task Promoting_somebody_who_does_not_exist_is_a_404()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        await RegisterAdminAsync(factory, admin);

        var response = await admin.PatchAsJsonAsync(
            "/api/admin/accounts/nobodyatall/role", new { role = "Builder" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_role_is_rejected()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        await RegisterAdminAsync(factory, admin);
        var username = await BuilderClient.RegisterAsync(target);

        var response = await admin.PatchAsJsonAsync(
            $"/api/admin/accounts/{username}/role", new { role = "Overlord" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_demote_themselves()
    {
        // There is no recovery from an installation with zero admins except the SQL that §7.7
        // exists to eliminate.
        var factory = postgres.App;
        using var admin = NewClient(factory);
        var username = await RegisterAdminAsync(factory, admin);

        var response = await admin.PatchAsJsonAsync(
            $"/api/admin/accounts/{username}/role", new { role = "Player" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var still = await BuilderClient.JsonAsync(
            await admin.GetAsync(new Uri($"/api/admin/accounts/{username}", UriKind.Relative)));

        Assert.Equal("Admin", still.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Searching_finds_accounts_by_partial_name()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        await RegisterAdminAsync(factory, admin);
        var username = await BuilderClient.RegisterAsync(target);

        var results = await BuilderClient.JsonAsync(
            await admin.GetAsync(new Uri($"/api/admin/accounts?q={username[..8]}", UriKind.Relative)));

        Assert.Contains(
            results.EnumerateArray(),
            a => a.GetProperty("username").GetString() == username);
    }

    // -----------------------------------------------------------------------
    // Revalidation — the security-relevant half (PLAN.md §7.7)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_promotion_reaches_an_already_signed_in_session()
    {
        // The role is a cookie claim written at sign-in. Without revalidation the freshly
        // promoted builder would keep a cookie saying Player and find the builder closed.
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        await RegisterAdminAsync(factory, admin);
        var username = await BuilderClient.RegisterAsync(target);

        // Establishes the cookie, and confirms the door is shut before the promotion.
        var before = await target.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        await admin.PatchAsJsonAsync($"/api/admin/accounts/{username}/role", new { role = "Builder" });

        var after = await target.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task A_demotion_revokes_builder_access_without_waiting_for_the_cookie_to_expire()
    {
        var factory = postgres.App;
        using var admin = NewClient(factory);
        using var target = NewClient(factory);

        await RegisterAdminAsync(factory, admin);
        var username = await BuilderClient.RegisterBuilderAsync(factory, target);

        Assert.Equal(
            HttpStatusCode.OK,
            (await target.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative))).StatusCode);

        await admin.PatchAsJsonAsync($"/api/admin/accounts/{username}/role", new { role = "Player" });

        var after = await target.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact]
    public async Task Banning_someone_who_is_already_connected_takes_effect()
    {
        // Before revalidation, is_banned was read at login and never again - so banning a
        // connected player did nothing at all until they chose to reconnect.
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DikuWebDbContext>();
            var account = await db.Accounts.FirstAsync(a => a.Username == username);
            account.IsBanned = true;
            account.BanReason = "Testing.";
            await db.SaveChangesAsync();
        }

        var after = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task An_account_deleted_under_a_live_session_stops_being_a_credential()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DikuWebDbContext>();
            var account = await db.Accounts.FirstAsync(a => a.Username == username);
            db.Accounts.Remove(account);
            await db.SaveChangesAsync();
        }

        var after = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
