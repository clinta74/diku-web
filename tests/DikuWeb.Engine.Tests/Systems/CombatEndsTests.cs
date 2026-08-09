using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// When a fight is over, and who it lets go (PLAN.md §4.2).
/// </summary>
/// <remarks>
/// The rule used to be "two or more combatants means a fight", which is right for exactly one
/// shape: one player against one mob. <b>A group fight never ended.</b> Two players on one mob is
/// three combatants; the mob dies, two remain, the count is still two, and both players were left
/// permanently <c>Fighting</c> — refused every later <c>kill</c> and unable to walk out of the
/// room. It scaled with the party, so the bigger the group the more people it stranded, and it was
/// invisible solo, which is how it survived.
///
/// The rule is now "somebody still has somebody to hit", which is the same question
/// <c>RunCombatant</c> asks before swinging. These tests are the shapes that rule has to get right.
/// </remarks>
public sealed class CombatEndsTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static void Group(WorldHarness harness, PlayerActor leader, PlayerActor member)
    {
        harness.Execute(leader, $"group invite {member.Name}");
        harness.Execute(member, "group accept");
    }

    [Fact]
    public void A_group_fight_ends_for_everyone_when_the_mob_dies()
    {
        // The reported bug, and the reason it was reported about groups: solo, the same code was
        // correct, because one player left is one combatant.
        var harness = Loaded();
        var bram = harness.AddPlayer("Bram", West, level: 5);
        var kael = harness.AddPlayer("Kael", West, level: 5);
        Group(harness, bram, kael);

        harness.AddMob("rat", West, health: 1);

        harness.Execute(bram, "kill rat");
        harness.Execute(kael, "kill rat");
        harness.Pump(40);

        Assert.Equal(CombatState.Idle, bram.Character.CombatState);
        Assert.Equal(CombatState.Idle, kael.Character.CombatState);
        Assert.Null(bram.Character.CurrentTarget);
        Assert.Null(kael.Character.CurrentTarget);
        Assert.Null(harness.World.FindCombat(West));
    }

    [Fact]
    public void And_they_can_start_another_fight()
    {
        // What a player actually notices. `kill` refuses anyone already Fighting, so a fight that
        // never ended took the character out of the game until they logged out.
        var harness = Loaded();
        var bram = harness.AddPlayer("Bram", West, level: 5);
        var kael = harness.AddPlayer("Kael", West, level: 5);
        Group(harness, bram, kael);

        harness.AddMob("rat", West, health: 1);
        harness.AddMob("wolf", West, health: 500, name: "wolf");

        harness.Execute(bram, "kill rat");
        harness.Execute(kael, "kill rat");
        harness.Pump(40);

        harness.Drain(bram);
        harness.Execute(bram, "kill wolf");

        Assert.DoesNotContain(
            "already in combat", harness.DrainText(bram), StringComparison.Ordinal);
    }

    [Fact]
    public void Three_on_one_ends_too()
    {
        // The bug scaled with the party: every extra member was another combatant propping the
        // count above the threshold.
        var harness = Loaded();
        var players = new[] { "Bram", "Kael", "Vess" }
            .Select(name => harness.AddPlayer(name, West, level: 5))
            .ToList();

        harness.AddMob("rat", West, health: 1);

        foreach (var player in players)
        {
            harness.Execute(player, "kill rat");
        }

        harness.Pump(40);

        Assert.All(players, p => Assert.Equal(CombatState.Idle, p.Character.CombatState));
        Assert.Null(harness.World.FindCombat(West));
    }

    [Fact]
    public void A_bystander_who_never_swung_is_let_go_as_well()
    {
        // Taunt puts a mob onto someone without giving them a target of their own, so they are in
        // the fight while pointing at nothing. They must still be released when it ends — a rule
        // that only freed people who had chosen a target would strand exactly the tank.
        var harness = Loaded();
        var warden = harness.AddPlayer("Theron", West, path: CharacterPath.Warden, level: 10);
        var other = harness.AddPlayer("Kael", West, level: 5);

        var rat = harness.AddMob("rat", West, health: 1);

        harness.Execute(other, "kill rat");

        var combat = harness.World.GetOrCreateCombat(West);
        combat.AddCombatant(EntityId.ForCharacter(warden.CharacterId));
        warden.Character.CombatState = CombatState.Fighting;

        harness.Pump(40);

        Assert.Equal(CombatState.Idle, warden.Character.CombatState);
        Assert.Equal(CombatState.Idle, other.Character.CombatState);
    }

    // -----------------------------------------------------------------------
    // The shapes that must stay in a fight
    // -----------------------------------------------------------------------

    [Fact]
    public void A_duel_stays_a_fight()
    {
        // Two combatants and no mob, which the old rule allowed by accident and a naive fix -
        // "one of each side" - would break. Each duellist targets the other, so it is live.
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetRoomFlag(West, RoomFlags.Pvp.Key, true));

        var alice = harness.AddPlayer("Alice", West, level: 5);
        var bob = harness.AddPlayer("Bob", West, level: 5);

        harness.Execute(alice, "kill Bob");
        harness.Execute(bob, "kill Alice");
        harness.Pump(8);

        Assert.NotNull(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Fighting, alice.Character.CombatState);
    }

    [Fact]
    public void A_mob_that_has_not_been_hit_back_keeps_the_fight_alive()
    {
        // The mob has a hate list naming the player; the player has chosen a target. Either side
        // alone is enough, and this is the ordinary case for the whole of a fight.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        harness.AddMob("rat", West, health: 500);

        harness.Execute(player, "kill rat");
        harness.Pump(8);

        Assert.NotNull(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
    }

    [Fact]
    public void One_of_two_mobs_dying_does_not_end_the_fight()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        harness.AddMob("rat", West, health: 1);
        harness.AddMob("wolf", West, health: 500, name: "wolf");

        harness.Execute(player, "kill wolf");
        harness.Pump(20);

        Assert.NotNull(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
    }
}
