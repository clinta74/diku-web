using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Levelling up says what it granted.
/// </summary>
/// <remarks>
/// <c>LevelUpUnlocksTests</c> covers what the lines say; this covers whether they are ever sent —
/// which is the half that was missing. A new ability used to arrive as a row appearing in a panel,
/// and now that the panel shows only what is cooling, it would arrive as nothing at all.
/// </remarks>
public sealed class LevelUpAnnouncementTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>
    /// Kills one mob worth exactly enough to land <paramref name="player"/> on
    /// <paramref name="targetLevel"/>. Level-matched, so the relevance taper pays in full.
    /// </summary>
    private static void KillUpTo(WorldHarness harness, PlayerActor player, int targetLevel)
    {
        var owed = XpProgression.XpForLevel(targetLevel) - player.Character.Xp;

        var mob = harness.AddMob("rat", West, health: 1, level: player.Character.Level);
        mob.ResolvedXp = (int)owed;
        mob.ResolvedStats["defense"] = -100; // hittable on anything but a natural 1

        harness.Execute(player, "kill rat");
        harness.Pump(20);
    }

    [Fact]
    public void A_new_ability_is_named_when_it_is_earned()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.bash"); // Warden, unlocks at 3

        var player = harness.AddPlayer("Kael", West, level: 2, path: CharacterPath.Warden);
        KillUpTo(harness, player, 3);

        var text = harness.DrainText(player);
        Assert.Equal(3, player.Character.Level);
        Assert.Contains("Level 3 — Bash", text, StringComparison.Ordinal);
        Assert.Contains("Type 'bash'.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_level_of_a_jump_is_announced()
    {
        // The case the feature exists for. One mob can carry a low-level character several levels
        // at once, and TryLevelUp jumps straight to the level the total buys - so what was unlocked
        // on the way is never mentioned again by anything.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.bash");         // 3
        harness.DefineAbility("warden.battle-fury");  // 5

        var player = harness.AddPlayer("Kael", West, level: 2, path: CharacterPath.Warden);
        KillUpTo(harness, player, 5);

        var text = harness.DrainText(player);
        Assert.Equal(5, player.Character.Level);
        Assert.Contains("Bash", text, StringComparison.Ordinal);
        Assert.Contains("Battle Fury", text, StringComparison.Ordinal);
        // Passives too, at 4 and 5 - the split between table and code is not the player's problem.
        Assert.Contains("Parry", text, StringComparison.Ordinal);
        Assert.Contains("Dual Wield", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_level_that_grants_nothing_says_nothing_extra()
    {
        // Level 2 unlocks nothing for a Warden. The advance line is still owed; the roster pointer
        // is not, or it arrives on every level for the whole game.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.bash");

        var player = harness.AddPlayer("Kael", West, level: 1, path: CharacterPath.Warden);
        KillUpTo(harness, player, 2);

        var text = harness.DrainText(player);
        Assert.Contains("You advance to level 2!", text, StringComparison.Ordinal);
        Assert.DoesNotContain("'abilities'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_paths_abilities_are_not_offered()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.bash");
        harness.DefineAbility("adept.shield"); // Adept, also unlocks at 3

        var player = harness.AddPlayer("Ilse", West, level: 2, path: CharacterPath.Warden);
        KillUpTo(harness, player, 3);

        Assert.DoesNotContain("Arcane Shield", harness.DrainText(player), StringComparison.Ordinal);
    }
}
