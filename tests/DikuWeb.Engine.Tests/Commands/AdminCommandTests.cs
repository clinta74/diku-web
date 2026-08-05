using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// The in-game admin verbs (PLAN.md §7.7). These never do the work themselves - the loop cannot
/// read the account store - so what is asserted here is that they gate correctly, parse
/// correctly, and enqueue exactly what the worker needs.
/// </summary>
public sealed class AdminCommandTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void An_admin_promoting_someone_enqueues_the_change()
    {
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "promote kael builder");

        var request = Assert.IsType<SetAccountRoleRequest>(Assert.Single(harness.Admin.Requests));
        Assert.Equal("kael", request.TargetUsername);
        Assert.Equal(AccountRole.Builder, request.Role);
        Assert.Equal(root.Character.AccountId, request.ActorAccountId);

        // The reply has to be addressed at the connection that asked, not the character - an
        // account may have several tabs open and only one of them typed this.
        Assert.Equal(root.SessionId, request.ReplyToSessionId);
    }

    [Fact]
    public void A_builder_cannot_promote_anyone()
    {
        // Builders edit the world; they do not hand out access to it. Otherwise the role is
        // self-propagating and there is no meaningful boundary at all.
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);

        harness.Execute(mira, "promote mira admin");

        Assert.Empty(harness.Admin.Requests);
        Assert.Contains("not something you can do", harness.DrainText(mira), StringComparison.Ordinal);
    }

    [Fact]
    public void A_player_cannot_promote_anyone()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);

        harness.Execute(kael, "promote kael admin");

        Assert.Empty(harness.Admin.Requests);
    }

    [Fact]
    public void Roles_must_be_typed_in_full()
    {
        // No prefix matching on the role: "promote kael a" quietly meaning Admin is precisely
        // the convenience nobody wants here. Typing the whole word is the confirmation step.
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "promote kael a");
        harness.Execute(root, "promote kael build");

        Assert.Empty(harness.Admin.Requests);
        Assert.Contains("is not a role", harness.DrainText(root), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_role_is_refused_without_enqueueing()
    {
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "promote kael overlord");

        Assert.Empty(harness.Admin.Requests);
    }

    [Fact]
    public void Promote_without_a_role_explains_itself()
    {
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "promote kael");

        Assert.Empty(harness.Admin.Requests);
        Assert.Contains("Usage: promote", harness.DrainText(root), StringComparison.Ordinal);
    }

    [Fact]
    public void Demote_is_promote_to_player()
    {
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "demote mira");

        var request = Assert.IsType<SetAccountRoleRequest>(Assert.Single(harness.Admin.Requests));
        Assert.Equal("mira", request.TargetUsername);
        Assert.Equal(AccountRole.Player, request.Role);
    }

    [Fact]
    public void Whois_enqueues_a_lookup()
    {
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "whois kael");

        var request = Assert.IsType<LookupAccountRequest>(Assert.Single(harness.Admin.Requests));
        Assert.Equal("kael", request.TargetUsername);
    }

    [Fact]
    public void The_target_does_not_have_to_be_online()
    {
        // These name an account, not somebody standing in the room. Requiring presence would
        // make the ordinary case - "so-and-so asked to help build" - impossible.
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "promote somebody-not-here builder");

        Assert.Single(harness.Admin.Requests);
    }

    [Fact]
    public void Help_shows_admin_verbs_only_to_admins()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(kael, "help");
        harness.Execute(mira, "help");
        harness.Execute(root, "help");

        var player = harness.DrainText(kael);
        var builder = harness.DrainText(mira);
        var admin = harness.DrainText(root);

        Assert.DoesNotContain("promote <name>", player, StringComparison.Ordinal);
        Assert.DoesNotContain("dig <dir>", player, StringComparison.Ordinal);

        // A builder sees the building verbs but not the ones that grant access.
        Assert.Contains("dig <dir>", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("promote <name>", builder, StringComparison.Ordinal);

        Assert.Contains("promote <name>", admin, StringComparison.Ordinal);
        Assert.Contains("dig <dir>", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void An_admin_can_also_build()
    {
        var harness = Loaded();
        var root = harness.AddPlayer("Root", West, AccountRole.Admin);

        harness.Execute(root, "dig north");

        Assert.NotNull(harness.World.FindRoom(West)!.ExitTo(Direction.North));
    }

    [Fact]
    public void Prefix_matching_still_favours_movement_over_the_new_verbs()
    {
        // "promote" and "demote" both start with letters that matter. Nothing may shadow a
        // direction or the most-typed verbs.
        var harness = Loaded();

        Assert.Equal("down", harness.Commands.Find("d")!.Name);
        Assert.Equal("down", harness.Commands.Find("do")!.Name);
        Assert.Equal("west", harness.Commands.Find("w")!.Name);
        Assert.Equal("promote", harness.Commands.Find("promote")!.Name);
        Assert.Equal("demote", harness.Commands.Find("demote")!.Name);
        Assert.Equal("who", harness.Commands.Find("who")!.Name);
        Assert.Equal("whois", harness.Commands.Find("whois")!.Name);
    }
}
