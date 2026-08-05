using DikuWeb.Domain.Accounts;

namespace DikuWeb.Domain.Tests.Accounts;

/// <summary>
/// PLAN.md §7.7. The roles look like a ladder because the numbers ascend, and they are not one -
/// this is the file that pins that down, because both the HTTP policies and the in-game command
/// table derive from it.
/// </summary>
public sealed class AccountRoleTests
{
    [Theory]
    [InlineData(AccountRole.Player)]
    [InlineData(AccountRole.Builder)]
    [InlineData(AccountRole.Moderator)]
    [InlineData(AccountRole.Admin)]
    public void Everyone_satisfies_a_player_requirement(AccountRole actor) =>
        Assert.True(actor.Satisfies(AccountRole.Player));

    [Theory]
    [InlineData(AccountRole.Player)]
    [InlineData(AccountRole.Builder)]
    [InlineData(AccountRole.Moderator)]
    [InlineData(AccountRole.Admin)]
    public void Admin_satisfies_everything(AccountRole required) =>
        Assert.True(AccountRole.Admin.Satisfies(required));

    [Fact]
    public void A_moderator_is_not_a_builder()
    {
        // The whole reason Satisfies exists rather than actor >= required. Moderator sorts
        // above Builder numerically and must still not be able to edit the world.
        Assert.False(AccountRole.Moderator.Satisfies(AccountRole.Builder));
    }

    [Fact]
    public void A_builder_is_not_a_moderator_and_not_an_admin()
    {
        Assert.False(AccountRole.Builder.Satisfies(AccountRole.Moderator));
        Assert.False(AccountRole.Builder.Satisfies(AccountRole.Admin));
    }

    [Fact]
    public void A_player_satisfies_nothing_above_player()
    {
        Assert.False(AccountRole.Player.Satisfies(AccountRole.Builder));
        Assert.False(AccountRole.Player.Satisfies(AccountRole.Moderator));
        Assert.False(AccountRole.Player.Satisfies(AccountRole.Admin));
    }

    [Fact]
    public void Only_admin_satisfies_an_admin_requirement()
    {
        var admins = AccountRoleExtensions.RolesSatisfying(AccountRole.Admin);

        Assert.Equal([nameof(AccountRole.Admin)], admins);
    }

    [Fact]
    public void The_builder_requirement_is_satisfied_by_builders_and_admins_only()
    {
        var roles = AccountRoleExtensions.RolesSatisfying(AccountRole.Builder);

        Assert.Equal([nameof(AccountRole.Builder), nameof(AccountRole.Admin)], roles);
    }

    [Fact]
    public void Roles_satisfying_agrees_with_satisfies_for_every_pair()
    {
        // RolesSatisfying builds the authorization policies; Satisfies gates the in-game
        // commands. If they ever disagree, one surface is more permissive than the other and
        // nothing else would notice.
        foreach (var required in Enum.GetValues<AccountRole>())
        {
            var listed = AccountRoleExtensions.RolesSatisfying(required);

            foreach (var actor in Enum.GetValues<AccountRole>())
            {
                Assert.Equal(actor.Satisfies(required), listed.Contains(actor.ToString()));
            }
        }
    }
}
