using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// What happens to a fight when somebody walks out of it (PLAN.md §4.2, §4.11).
/// </summary>
/// <remarks>
/// Leaving used to be one condition with one consequence: whoever left, the <em>attacker</em> was
/// removed. When the target was the one who left, that took the wrong party out — and took them
/// out without releasing them, so they kept <c>CombatState.Fighting</c> and their target while no
/// longer being in <c>Combatants</c>, where the end-of-fight sweep would have found them.
///
/// <b>Stuck for the rest of the session.</b> Every later <c>kill</c> refused with "You're already
/// in combat!", every direction refused with "You can't leave while in combat!". The only way out
/// was logging in again. Reported from live play as a zombie wandering off mid-fight; these are
/// that transcript, and the shapes around it.
/// </remarks>
public sealed class CombatDepartureTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>Walks a mob out of the room the way wandering does.</summary>
    private static void Wander(Mob mob, RoomKey to) => mob.RoomKey = to.ToString();

    [Fact]
    public void A_target_that_leaves_releases_the_attacker()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");

        harness.Execute(player, "kill zombie");
        harness.Pump(8);
        Assert.Equal(CombatState.Fighting, player.Character.CombatState);

        Wander(zombie, Middle);
        harness.Pump(20);

        Assert.Equal(CombatState.Idle, player.Character.CombatState);
        Assert.Null(player.Character.CurrentTarget);
    }

    [Fact]
    public void And_the_player_can_walk_away()
    {
        // The reported symptom, verbatim: "> n" answered with "You can't leave while in combat!"
        // long after the zombie had gone.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");

        harness.Execute(player, "kill zombie");
        harness.Pump(8);

        Wander(zombie, Middle);
        harness.Pump(20);
        harness.Drain(player);

        harness.Execute(player, "east");

        Assert.DoesNotContain(
            "while in combat", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void And_can_start_another_fight()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");
        harness.AddMob("rat", West, health: 500);

        harness.Execute(player, "kill zombie");
        harness.Pump(8);

        Wander(zombie, Middle);
        harness.Pump(20);
        harness.Drain(player);

        harness.Execute(player, "kill rat");

        Assert.DoesNotContain(
            "already in combat", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void The_departure_is_narrated_rather_than_silent()
    {
        // Every other ending has words on it. This one had none, and the silence is why being
        // trapped went unnoticed: it was indistinguishable from the bug.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");

        harness.Execute(player, "kill zombie");
        harness.Pump(8);
        harness.Drain(player);

        Wander(zombie, Middle);
        harness.Pump(20);

        Assert.Contains("You stop fighting a zombie", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void The_attacker_leaving_still_removes_the_attacker()
    {
        // The case the old code got right, and the reason it was written that way. A mob that
        // wanders off mid-swing takes itself out of the fight and is released.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(player, "kill rat");
        harness.Pump(8);
        Assert.Equal(CombatState.Fighting, rat.CombatState);

        Wander(rat, Middle);
        harness.Pump(20);

        Assert.Equal(CombatState.Idle, rat.CombatState);
        Assert.Null(rat.CurrentTarget);
    }

    [Fact]
    public void A_fight_with_somebody_left_to_hit_carries_on()
    {
        // One of two mobs wandering off must not end the fight for the other. This is what the
        // "remove the target, then let IsCombatActive judge" shape buys over ending it here.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(player, "kill rat");
        harness.Pump(8);

        // The zombie joins the fight, then thinks better of it.
        var combat = harness.World.GetOrCreateCombat(West);
        combat.AddCombatant(EntityId.ForMob(zombie.Id));
        combat.AddToHateList(
            EntityId.ForMob(zombie.Id), EntityId.ForCharacter(player.CharacterId), 5);
        zombie.CombatState = CombatState.Fighting;

        Wander(zombie, Middle);
        harness.Pump(20);

        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
        Assert.NotNull(harness.World.FindCombat(West));
    }

    [Fact]
    public void Fleeing_still_works()
    {
        // `flee` cleans up after itself and must keep doing so; it is the one departure that was
        // always handled, and the easiest to break while fixing the others.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        harness.AddMob("rat", West, health: 500);

        harness.Execute(player, "kill rat");
        harness.Pump(8);
        harness.Drain(player);

        harness.Execute(player, "flee");
        harness.Pump(4);

        Assert.Equal(CombatState.Idle, player.Character.CombatState);
        Assert.Null(player.Character.CurrentTarget);
    }
}
