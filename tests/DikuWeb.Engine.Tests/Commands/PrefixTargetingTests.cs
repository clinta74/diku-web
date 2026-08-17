using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// A player's name is matched the same way everywhere (BUGS.md #21).
/// </summary>
/// <remarks>
/// `attack`, `assist`, `consider`, `cast`'s target and `autofollow` used exact `string.Equals` on
/// the name, while `tell`, `group invite`, `group kick` and every admin verb went through
/// <see cref="NameMatch.Best"/>. So `tell kae hello` worked and `attack kae` answered
/// "You don't see 'kae' here" — and `ResolveTarget`'s own doc claimed targets were "matched the
/// same way every other targeting command matches", which was true for mobs and false for players
/// two lines above it.
/// </remarks>
public sealed class PrefixTargetingTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void Attack_finds_a_player_by_a_prefix_of_their_name()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddPlayer("Mirabel", Room);
        harness.World.FindRoom(Room)!.Flags.Set(RoomFlags.Pvp.Key, true);
        harness.Drain(kael);

        harness.Execute(kael, "attack mira");

        Assert.DoesNotContain("don't see", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Consider_finds_a_player_by_a_prefix_of_their_name()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddPlayer("Mirabel", Room);
        harness.Drain(kael);

        harness.Execute(kael, "consider mira");

        Assert.DoesNotContain("don't see", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// And a name that matches nothing still says so, rather than picking somebody at random.
    /// </summary>
    [Fact]
    public void A_name_nobody_answers_to_is_still_refused()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddPlayer("Mirabel", Room);
        harness.Drain(kael);

        harness.Execute(kael, "consider zzz");

        Assert.Contains("don't see", harness.DrainText(kael), StringComparison.Ordinal);
    }
}
