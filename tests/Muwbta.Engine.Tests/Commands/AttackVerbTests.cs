using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// <c>attack</c> replaced <c>kill</c> as the verb the game puts in front of people, and
/// <c>kill</c> still works.
/// </summary>
/// <remarks>
/// A rename that quietly dropped the old spelling would have taken <c>k</c> with it — the most
/// reflexive keystroke in the genre — so the change is to what is advertised, not to what is
/// accepted. Both halves are asserted here, because either one alone is the wrong change: only
/// the new verb and the reflex is gone; only the alias and nothing about the tone has moved.
/// </remarks>
public sealed class AttackVerbTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static (WorldHarness Harness, Engine.World.PlayerActor Player) Ready()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Bram", West, level: 5);
        harness.AddMob("rat", West, health: 100, level: 5);
        return (harness, player);
    }

    [Theory]
    [InlineData("attack rat")]
    [InlineData("att rat")]
    [InlineData("kill rat")]
    [InlineData("k rat")]
    public void Every_spelling_starts_the_same_fight(string input)
    {
        var (harness, player) = Ready();

        harness.Execute(player, input);

        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
    }

    [Fact]
    public void Attack_with_no_target_asks_in_the_new_words()
    {
        // The prompt is the other place the old word was in front of people, and it is the one a
        // player sees by accident rather than by typing it.
        var (harness, player) = Ready();

        harness.Execute(player, "attack");

        Assert.Contains("Attack what?", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_lists_attack_and_not_kill()
    {
        var (harness, player) = Ready();

        harness.Execute(player, "help");
        var help = harness.DrainText(player);

        Assert.Contains("attack <target>", help, StringComparison.Ordinal);
        Assert.DoesNotContain("kill <target>", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Attack_does_not_steal_the_abilities_shortcut()
    {
        // "a" and "ab" belong to `abilities`, which is why attack asks for three characters.
        // VerbReachabilityTests would catch the shadowing in general; this pins the specific pair,
        // because the tempting fix for "att is a lot to type" is exactly the one that breaks it.
        var (harness, player) = Ready();

        harness.Execute(player, "ab");

        // A level 5 with no ability defined in the harness still gets the passive list, which is
        // enough to prove "ab" reached `abilities` rather than swinging at the rat.
        Assert.Contains("Passives", harness.DrainText(player), StringComparison.Ordinal);
        Assert.Equal(CombatState.Idle, player.Character.CombatState);
    }
}
