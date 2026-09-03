using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// Mobs heal (PLAN.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// Nothing restored a mob's health, ever. <c>RegenSystem</c> iterated players only, ending a fight
/// touched neither side's vitals, no leash existed, and the spawner replaces a slot only when its
/// occupant is <em>dead</em> — so a wounded mob stayed wounded for the life of the process. Chip
/// something to 5%, walk away, come back an hour later and it is still at 5%: attrition was free and
/// permanent, and a camped room degraded monotonically until a restart (BUGS.md #25).
/// </para>
/// <para>
/// Two rules, and the second is mostly redundant on purpose. <b>A mob that leaves a fight alive is
/// whole again</b> — that is the one players feel. <b>A wounded idle mob trends back to full on the
/// regen tick</b> — that is the invariant underneath, which holds even where a disengage path is
/// missed. A promise kept only by healing at every exit is enforced by remembering to find them all,
/// which is the defect class this file exists inside.
/// </para>
/// <para>
/// <b>No leashing.</b> Mobs do not walk home; <see cref="Mob"/> carries no home room.
/// </para>
/// </remarks>
public sealed class MobRecoveryTests
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

    /// <summary>
    /// Engages and swings until the mob is actually hurt, so a test about recovery cannot pass by
    /// never having done any damage.
    /// </summary>
    private static Mob Wounded(WorldHarness harness, out PlayerActor player)
    {
        player = harness.AddPlayer("Theron", West, level: 5);
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");

        harness.Execute(player, "attack zombie");
        harness.Pump(24);

        Assert.True(
            zombie.Vitals.Health < zombie.Vitals.HealthMax,
            "The fight did no damage, so this test would pass without healing anything.");

        return zombie;
    }

    // -----------------------------------------------------------------------
    // Rule 1: leaving a fight alive
    // -----------------------------------------------------------------------

    /// <summary>
    /// The test that would have caught the bug.
    /// </summary>
    [Fact]
    public void A_mob_that_leaves_a_fight_alive_is_whole_again()
    {
        var harness = Loaded();
        var zombie = Wounded(harness, out _);

        // The departure path: the mob wanders off, which releases it through EndCombatFor.
        Wander(zombie, Middle);
        harness.Pump(20);

        Assert.Equal(zombie.Vitals.HealthMax, zombie.Vitals.Health);
        Assert.Equal(CombatState.Idle, zombie.CombatState);
        Assert.Null(zombie.CurrentTarget);
    }

    /// <summary>
    /// The <c>flee</c> path specifically — the second call site, and the reason a shared method
    /// exists rather than two hand-written copies.
    /// </summary>
    [Fact]
    public void Fleeing_heals_the_mob_you_fled_from()
    {
        var harness = Loaded();
        var zombie = Wounded(harness, out var player);

        harness.Execute(player, "flee");

        Assert.Equal(zombie.Vitals.HealthMax, zombie.Vitals.Health);
        Assert.Equal(CombatState.Idle, zombie.CombatState);
    }

    /// <summary>
    /// The guard: healing on disengage must not resurrect anything.
    /// </summary>
    /// <remarks>
    /// It should be unreachable in practice — <c>HandleDeath</c> removes the dead from
    /// <c>Combatants</c> before the end-of-fight sweep looks — which is exactly why it is asserted
    /// here rather than trusted. Idling a corpse is still correct, so only the heal is guarded.
    /// </remarks>
    [Fact]
    public void A_dead_mob_is_not_resurrected_by_disengaging()
    {
        var mob = new Mob
        {
            TemplateKey = "zombie",
            TemplateName = "a zombie",
            RoomKey = West.ToString(),
            CombatState = CombatState.Fighting,
            CurrentTarget = "character:whoever",
            Vitals = new Vitals
            {
                Health = 0,
                HealthMax = 500,
                Focus = 0,
                FocusMax = 0,
                Stamina = 0,
                StaminaMax = 0,
            },
        };

        mob.Disengage();

        Assert.Equal(0, mob.Vitals.Health);
        Assert.Equal(CombatState.Idle, mob.CombatState);
        Assert.Null(mob.CurrentTarget);
    }

    // -----------------------------------------------------------------------
    // Rule 2: the regen tick
    // -----------------------------------------------------------------------

    [Fact]
    public void A_wounded_idle_mob_regains_health_on_the_tick()
    {
        var harness = Loaded();

        // Damaged directly, with no fight involved: this is the arm that has to work on its own,
        // for a mob left wounded by a disengage path nothing healed.
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");
        zombie.Vitals.Health = 25;

        RegenSystem.Tick(harness.World);

        Assert.True(zombie.Vitals.Health > 25);
        Assert.True(zombie.Vitals.Health <= zombie.Vitals.HealthMax);
    }

    [Fact]
    public void Regen_does_not_carry_a_mob_past_its_maximum()
    {
        var harness = Loaded();
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");
        zombie.Vitals.Health = zombie.Vitals.HealthMax - 1;

        RegenSystem.Tick(harness.World);

        Assert.Equal(zombie.Vitals.HealthMax, zombie.Vitals.Health);
    }

    /// <summary>
    /// The same rule players get (§4.5): you do not heal up in the middle of a fight.
    /// </summary>
    [Fact]
    public void A_fighting_mob_does_not_regen()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 5);
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");

        harness.Execute(player, "attack zombie");
        harness.Pump(24);

        var wounded = zombie.Vitals.Health;
        Assert.True(wounded < zombie.Vitals.HealthMax);
        Assert.Equal(CombatState.Fighting, zombie.CombatState);

        RegenSystem.Tick(harness.World);

        Assert.Equal(wounded, zombie.Vitals.Health);
    }

    /// <summary>
    /// A corpse still standing in the room while the sweep runs is not topped back up. Health is the
    /// death test everywhere else in combat, so it is the death test here.
    /// </summary>
    [Fact]
    public void A_dead_mob_does_not_regen()
    {
        var harness = Loaded();
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");
        zombie.Vitals.Health = 0;

        RegenSystem.Tick(harness.World);

        Assert.Equal(0, zombie.Vitals.Health);
    }

    /// <summary>
    /// Mobs regen health and nothing else. Regenerating a bar with no readers is <c>itemPower</c> in
    /// miniature — the defect this whole document is about.
    /// </summary>
    [Fact]
    public void Regen_leaves_a_mobs_focus_and_stamina_alone()
    {
        var harness = Loaded();
        var zombie = harness.AddMob("zombie", West, health: 500, name: "zombie");
        zombie.Vitals.Health = 25;
        zombie.Vitals.FocusMax = 100;
        zombie.Vitals.StaminaMax = 100;

        RegenSystem.Tick(harness.World);

        Assert.Equal(0, zombie.Vitals.Focus);
        Assert.Equal(0, zombie.Vitals.Stamina);
    }
}
