using Muwbta.Domain.Accounts;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Retiring a character (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// The only verb that takes something away from a player permanently, so the tests are mostly
/// about the refusals. The work spans the one line the architecture will not let anything cross:
/// removing the character from the world happens on the loop thread, and writing
/// <c>DeletedAt</c> is enqueued because the loop may not touch a database (§2.1). What the loop
/// half can assert is exactly that — that it enqueued the right request and removed the right
/// session.
/// </remarks>
public sealed class DeleteCharacterTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static DeleteCharacterRequest? RequestIn(WorldHarness harness) =>
        harness.Admin.Requests.OfType<DeleteCharacterRequest>().SingleOrDefault();

    [Fact]
    public void An_admin_deletes_an_offline_character_by_name()
    {
        // The ordinary case: clearing out a name nobody is using. Being online is not required,
        // and requiring it would make the verb useless for the thing it is mostly for.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "deletecharacter Ghostly");

        Assert.Equal("Ghostly", RequestIn(harness)?.CharacterName);
    }

    [Fact]
    public void An_online_character_is_removed_from_the_world_as_well()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        var victim = harness.AddPlayer("Kael", West);

        var context = harness.Execute(admin, "deletecharacter Kael");

        Assert.Contains(victim.CharacterId, context.RemovalsRequested.Select(r => r.CharacterId));
        Assert.Equal("Kael", RequestIn(harness)?.CharacterName);
    }

    [Fact]
    public void The_room_is_told_rather_than_left_to_guess()
    {
        // Somebody vanishing with no explanation reads as a bug to everyone standing there.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddPlayer("Kael", West);
        var bystander = harness.AddPlayer("Bram", West);

        harness.Execute(admin, "deletecharacter Kael");

        Assert.Contains("removed from the world", harness.DrainText(bystander), StringComparison.Ordinal);
    }

    [Fact]
    public void A_prefix_resolves_to_the_full_name_before_it_is_enqueued()
    {
        // Prefix matching is how every other targeted verb works, and the database half needs the
        // whole name — enqueuing "Kae" would delete nothing and say so confusingly.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddPlayer("Kael", West);

        harness.Execute(admin, "deletecharacter Kae");

        Assert.Equal("Kael", RequestIn(harness)?.CharacterName);
    }

    // -----------------------------------------------------------------------
    // Refusals
    // -----------------------------------------------------------------------

    [Fact]
    public void You_cannot_delete_the_character_you_are_playing()
    {
        // The loop would be left holding a session for a row that no longer exists.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "deletecharacter Root");

        Assert.Null(RequestIn(harness));
        Assert.Contains("cannot delete", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccountRole.Player)]
    [InlineData(AccountRole.Builder)]
    public void Anyone_below_admin_is_told_the_verb_does_not_exist(AccountRole role)
    {
        // Worded as an unknown verb rather than as a refusal, matching every other admin verb:
        // nobody below Admin should learn from the game that this exists. Builder is included
        // because content authority is not moderation authority.
        var harness = Loaded();
        var actor = harness.AddPlayer("Mason", West, role: role);
        harness.AddPlayer("Bram", West);

        harness.Execute(actor, "deletecharacter Bram");

        Assert.Null(RequestIn(harness));
        Assert.Contains(
            "not something you can do", harness.DrainText(actor), StringComparison.Ordinal);
    }

    [Fact]
    public void It_needs_a_name()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);

        harness.Execute(admin, "deletecharacter");

        Assert.Null(RequestIn(harness));
        Assert.Contains("Usage", harness.DrainText(admin), StringComparison.Ordinal);
    }

    [Fact]
    public void It_cannot_be_abbreviated_to_something_careless()
    {
        // Eight characters minimum, for the reason `shutdown` demands them: there is no undo, and
        // `del kael` is how the wrong Kael gets deleted.
        var harness = Loaded();
        var admin = harness.AddPlayer("Root", West, role: AccountRole.Admin);
        harness.AddPlayer("Kael", West);

        Assert.Throws<InvalidOperationException>(() => harness.Execute(admin, "del Kael"));

        harness.Execute(admin, "deletech Kael");
        Assert.Equal("Kael", RequestIn(harness)?.CharacterName);
    }
}
