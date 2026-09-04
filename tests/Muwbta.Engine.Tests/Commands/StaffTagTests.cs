using Muwbta.Domain.Accounts;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Staff wear their role on every line they send; players wear nothing.
/// </summary>
/// <remarks>
/// The tag is the half of the impersonation fix that lives in the engine (the other half is
/// <see cref="ReservedNames"/>, which stops anyone taking a staff word as a name). It is only
/// worth anything if it is unforgeable and consistent: on the lines a character speaks or is
/// listed on, and never on a player's. A player who could produce "[Admin]" by any means, or a
/// staff line that sometimes went out bare, would put the forgery back.
/// </remarks>
public sealed class StaffTagTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void An_admins_tell_arrives_wearing_the_role()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Bram", West);
        admin.Role = AccountRole.Admin;
        var target = harness.AddPlayer("Kael", East);

        harness.Execute(admin, "tell Kael your account is fine, nobody will ever ask for your password");

        Assert.Contains(
            "[Admin] Bram tells you, 'your account is fine",
            harness.DrainText(target),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_players_tell_arrives_bare()
    {
        // The comparison the tag exists to provide. A player claiming to be staff has exactly
        // this line to work with, and it says who they are.
        var harness = Loaded();
        var player = harness.AddPlayer("Bram", West);
        var target = harness.AddPlayer("Kael", East);

        harness.Execute(player, "tell Kael send me your password");

        var text = harness.DrainText(target);
        Assert.Contains("Bram tells you, 'send me your password'", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccountRole.Admin, "[Admin] Bram")]
    [InlineData(AccountRole.Moderator, "[Moderator] Bram")]
    [InlineData(AccountRole.Builder, "[Builder] Bram")]
    public void Who_lists_staff_by_role(AccountRole role, string expected)
    {
        // Builders are staff here on purpose: what they write arrives styled as the world.
        var harness = Loaded();
        var staff = harness.AddPlayer("Bram", West);
        staff.Role = role;
        var looker = harness.AddPlayer("Kael", East);

        harness.Execute(looker, "who");

        Assert.Contains(expected, harness.DrainText(looker), StringComparison.Ordinal);
    }

    [Fact]
    public void Say_in_the_room_wears_the_role_too()
    {
        var harness = Loaded();
        var admin = harness.AddPlayer("Bram", West);
        admin.Role = AccountRole.Admin;
        var listener = harness.AddPlayer("Kael", West);

        harness.Execute(admin, "say the crypt is closed tonight");

        Assert.Contains(
            "[Admin] Bram says, 'the crypt is closed tonight'",
            harness.DrainText(listener),
            StringComparison.Ordinal);
    }
}
