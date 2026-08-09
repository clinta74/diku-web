using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Who a mob decides to hit, and why (PLAN.md §4.2).
/// </summary>
/// <remarks>
/// The hate list has always been a damage meter — <c>GetTopHater</c> reads cumulative damage, so
/// it already reorders itself the moment someone out-damages the current leader. What it was
/// missing was most of the damage. Only landed melee swings were counted: ability damage went
/// from the executor straight into <c>Vitals.Health</c>, and damage-over-time ticks went straight
/// in from the combat loop.
///
/// That inverted the design. <b>The Adept, the Path built to deal the largest damage in the game,
/// was the only one that could never pull a mob off anybody</b>, and the Shade's Ambush — most of
/// that Path's damage — was worth nothing either. Every point of damage now counts once, so
/// "whoever hurts it most gets hit" is a rule about the game rather than about melee.
/// </remarks>
public sealed class ThreatTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static PlayerActor Caster(
        WorldHarness harness,
        string abilityKey,
        string name = "Ilse",
        CharacterPath path = CharacterPath.Adept,
        int level = 16)
    {
        var actor = harness.AddPlayer(name, West, path: path, level: level);
        actor.Character.Vitals.FocusMax = 500;
        actor.Character.Vitals.Focus = 500;
        actor.Character.Vitals.StaminaMax = 500;
        actor.Character.Vitals.Stamina = 500;
        harness.DefineAbility(abilityKey);
        return actor;
    }

    private static int HateFor(WorldHarness harness, Mob mob, PlayerActor actor) =>
        harness.World.FindCombat(West)?.HateOf(
            EntityId.ForMob(mob.Id), EntityId.ForCharacter(actor.CharacterId)) ?? 0;

    private static string? TopHater(WorldHarness harness, Mob mob) =>
        harness.World.FindCombat(West)?.GetTopHater(EntityId.ForMob(mob.Id));

    // -----------------------------------------------------------------------
    // Ability damage
    // -----------------------------------------------------------------------

    [Fact]
    public void An_ability_that_wounds_a_mob_earns_threat_for_it()
    {
        var harness = Loaded();
        var caster = Caster(harness, "adept.disjunction");
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(caster, "kill rat");
        harness.Execute(caster, "cast disjunction");
        harness.Pump(20);

        Assert.True(HateFor(harness, rat, caster) > 1);
    }

    [Fact]
    public void A_big_cast_pulls_the_mob_off_whoever_was_tanking_it()
    {
        // The scenario the hate list exists for: a Warden holds a mob, an Adept lands something
        // far larger, and the mob turns on the Adept. This is what taunt is the answer to - and
        // before ability damage counted, it could not happen at all.
        var harness = Loaded();

        var warden = harness.AddPlayer("Theron", West, path: CharacterPath.Warden, level: 16);
        var adept = Caster(harness, "adept.cataclysm", level: 20);
        var rat = harness.AddMob("rat", West, health: 5_000);

        harness.Execute(warden, "kill rat");
        harness.Pump(12);

        Assert.Equal(EntityId.ForCharacter(warden.CharacterId), TopHater(harness, rat));

        // Named, because the Adept has not attacked anything yet and a harmful ability with no
        // name falls back to the caster's current target - which is nothing.
        harness.Execute(adept, "cast cataclysm rat");
        harness.Pump(20);

        Assert.Equal(EntityId.ForCharacter(adept.CharacterId), TopHater(harness, rat));
    }

    [Fact]
    public void Hurting_a_mob_with_an_ability_starts_the_fight()
    {
        // An opening Bolt used to be free: the mob took the damage and stood there, because
        // nothing had put it in combat and the threat had nowhere to be recorded.
        var harness = Loaded();
        var caster = Caster(harness, "adept.bolt", level: 1);
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        Assert.NotNull(harness.World.FindCombat(West));
        Assert.Equal(EntityId.ForCharacter(caster.CharacterId), TopHater(harness, rat));
        Assert.Equal(CombatState.Fighting, rat.CombatState);
    }

    [Fact]
    public void A_heal_earns_no_threat()
    {
        // Threat follows damage, and a heal is not damage. Healers drawing aggro is a design a
        // game can have, but it is not this one's - and inventing it as a side effect of the
        // damage path would be the wrong way to arrive at it.
        var harness = Loaded();
        var hallow = Caster(harness, "hallow.mend", name: "Bram", path: CharacterPath.Hallow, level: 10);
        var warden = harness.AddPlayer("Theron", West, path: CharacterPath.Warden, level: 10);
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(warden, "kill rat");
        warden.Character.Vitals.Health = 5;
        harness.Execute(hallow, "cast mend Theron");
        harness.Pump(12);

        Assert.Equal(0, HateFor(harness, rat, hallow));
    }

    [Fact]
    public void An_area_effect_does_not_swing_the_casters_own_weapon_around()
    {
        // Engaging is about what the *mobs* are now doing. Repointing the caster's own attacks at
        // the last thing the flames touched would be the room choosing their target for them.
        var harness = Loaded();
        var caster = Caster(harness, "adept.firestorm", level: 18);
        var rat = harness.AddMob("rat", West, health: 500);
        harness.AddMob("wolf", West, health: 500, name: "wolf");

        harness.Execute(caster, "kill rat");
        var chosen = caster.Character.CurrentTarget;

        harness.Execute(caster, "cast firestorm");
        harness.Pump(24);

        Assert.Equal(chosen, caster.Character.CurrentTarget);
        Assert.Equal(EntityId.ForMob(rat.Id), caster.Character.CurrentTarget);
    }

    // -----------------------------------------------------------------------
    // Damage over time
    // -----------------------------------------------------------------------

    [Fact]
    public void A_wound_credits_whoever_opened_it()
    {
        // The caster may have fled the room; the bleed keeps working either way, and the threat
        // for it belongs to them rather than to nobody.
        var harness = Loaded();
        var shade = Caster(harness, "shade.ambush", name: "Vess", path: CharacterPath.Shade, level: 10);
        var rat = harness.AddMob("rat", West, health: 5_000);

        harness.Execute(shade, "kill rat");
        harness.Execute(shade, "cast ambush");
        harness.Pump(30);

        var afterCast = HateFor(harness, rat, shade);
        harness.Pump(20);

        // Threat kept climbing while nothing new was cast, which is the tick being counted.
        Assert.True(HateFor(harness, rat, shade) > afterCast);
    }

    [Fact]
    public void A_wound_from_an_unreadable_source_is_ignored_rather_than_fatal()
    {
        // An effect's source survives a jsonb round trip and outlives the cast that set it. A
        // malformed one used to reach the hate list, become the top hater, and take the whole
        // combat tick down on the next pulse when nothing could resolve its room - and a dead
        // loop is a dead world for everyone connected, not just the room it happened in.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 10);
        var rat = harness.AddMob("rat", West, health: 5_000);

        harness.Execute(player, "kill rat");

        harness.World.ApplyEffect(rat.Id, new ActiveEffect
        {
            EffectKey = "damage.overtime",
            Name = "bleeding",
            SourceEntityId = "not-an-entity-id",
            TickDamage = 5,
            TickIntervalPulses = 4,
            NextTickPulse = harness.Clock.CurrentPulse,
            ExpiresAtPulse = harness.Clock.CurrentPulse + 200,
        });

        harness.Pump(40);

        // The wound still worked; only the threat for it was dropped.
        Assert.True(rat.Vitals.Health < 5_000);
        Assert.Equal(EntityId.ForCharacter(player.CharacterId), TopHater(harness, rat));
    }

    // -----------------------------------------------------------------------
    // The list itself
    // -----------------------------------------------------------------------

    [Fact]
    public void Threat_is_cumulative_so_the_leader_can_change_back()
    {
        // Nothing is a lock: the meter keeps running, so whoever was displaced climbs back by
        // out-damaging whoever displaced them.
        var harness = Loaded();
        var combat = harness.World.GetOrCreateCombat(West);
        var mobId = EntityId.ForMob(Guid.CreateVersion7());
        var a = EntityId.ForCharacter(Guid.CreateVersion7());
        var b = EntityId.ForCharacter(Guid.CreateVersion7());

        combat.AddCombatant(mobId);
        combat.AddToHateList(mobId, a, 100);
        combat.AddToHateList(mobId, b, 60);
        Assert.Equal(a, combat.GetTopHater(mobId));

        combat.AddToHateList(mobId, b, 50);
        Assert.Equal(b, combat.GetTopHater(mobId));
    }

    [Fact]
    public void Re_engaging_does_not_hand_out_a_free_point_of_threat()
    {
        // The opening seed is a floor, not a bonus. Adding it on every engage would make walking
        // in and out of a fight a way to nudge yourself up the list.
        var harness = Loaded();
        var combat = harness.World.GetOrCreateCombat(West);
        var player = harness.AddPlayer("Theron", West);
        var rat = harness.AddMob("rat", West, health: 500);

        var mobId = EntityId.ForMob(rat.Id);
        var playerId = EntityId.ForCharacter(player.CharacterId);

        combat.AddCombatant(mobId);
        combat.AddToHateList(mobId, playerId, 40);

        Engine.Systems.CombatEngagement.Engage(harness.World, player.Character, rat);

        Assert.Equal(40, combat.HateOf(mobId, playerId));
    }
}
