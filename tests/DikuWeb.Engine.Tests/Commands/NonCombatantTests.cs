using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// A mob whose disposition is <c>npc</c> is a non-combatant: it cannot be drawn into a fight
/// from either side.
/// </summary>
/// <remarks>
/// Quest givers, quest turn-ins, and shopkeepers are all NPCs. Killing one takes the quest out
/// of the world until the spawner comes back around, stranding anybody who had it - which
/// PLAN.md §7.4 rules out. Refusing the attack is cheaper than repairing the aftermath.
/// </remarks>
public sealed class NonCombatantTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static Dictionary<string, object> Disposition(string type) =>
        WorldHarness.AsPersisted(new Dictionary<string, object> { ["type"] = type });

    [Fact]
    public void An_npc_cannot_be_attacked()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var barkeep = harness.AddMob("barkeep", Room, name: "barkeep", behavior: Disposition("npc"));
        harness.Drain(kael);

        harness.Execute(kael, "kill barkeep");

        Assert.Equal(CombatState.Idle, kael.Character.CombatState);
        Assert.Equal(CombatState.Idle, barkeep.CombatState);
        Assert.Null(kael.Character.CurrentTarget);
        Assert.Contains("not someone you can fight", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_npc_is_unattackable_even_in_a_room_that_permits_combat()
    {
        // The refusal is a property of the mob, not of the room. A builder must not have to
        // remember to flag every room a quest giver might wander into.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("barkeep", Room, name: "barkeep", behavior: Disposition("npc"));
        harness.Drain(kael);

        harness.Execute(kael, "kill barkeep");

        Assert.Equal(CombatState.Idle, kael.Character.CombatState);
    }

    [Fact]
    public void An_ordinary_mob_is_still_attackable()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var rat = harness.AddMob("rat", Room, name: "rat");
        harness.Drain(kael);

        harness.Execute(kael, "kill rat");

        Assert.Equal(CombatState.Fighting, kael.Character.CombatState);
        Assert.Equal(CombatState.Fighting, rat.CombatState);
    }

    [Fact]
    public void A_passive_mob_is_attackable()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var deer = harness.AddMob("deer", Room, name: "deer", behavior: Disposition("passive"));
        harness.Drain(kael);

        harness.Execute(kael, "kill deer");

        Assert.Equal(CombatState.Fighting, deer.CombatState);
    }

    [Fact]
    public void An_aggressive_mob_is_attackable()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var wolf = harness.AddMob("wolf", Room, name: "wolf", behavior: Disposition("aggressive"));
        harness.Drain(kael);

        harness.Execute(kael, "kill wolf");

        Assert.Equal(CombatState.Fighting, wolf.CombatState);
    }

    /// <summary>
    /// A mob with no template in the cache must stay attackable. Failing the other way would let
    /// a cache miss quietly make the whole world invulnerable.
    /// </summary>
    [Fact]
    public void A_mob_whose_template_is_missing_is_still_attackable()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var rat = harness.AddMob("rat", Room, name: "rat");
        harness.MobTemplates.Remove("rat");
        harness.Drain(kael);

        harness.Execute(kael, "kill rat");

        Assert.Equal(CombatState.Fighting, rat.CombatState);
    }
}
