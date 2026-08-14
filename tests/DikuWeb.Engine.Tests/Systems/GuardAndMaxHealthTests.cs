using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// The two effects added for Last Stand: a guard that changes how hard somebody is to hit, and a
/// maximum-health buff that grants the health to go under the new ceiling.
/// </summary>
/// <remarks>
/// Both are state rather than an event — nothing happens at the moment of the cast — so the thing
/// worth testing is that the state reaches the code that reads it. A buff nothing consults is the
/// silent failure this whole area keeps producing.
/// </remarks>
public sealed class GuardAndMaxHealthTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static ActiveEffect Guard(int defense, int armor, long expires) => new()
    {
        EffectKey = "buff.defense",
        Name = "guarded",
        SourceEntityId = "unknown",
        DefenseRatingDelta = defense,
        ArmorFlatDelta = armor,
        ExpiresAtPulse = expires,
    };

    private static ActiveEffect MaxHealth(int bonus, long expires) => new()
    {
        EffectKey = "buff.max-health",
        Name = "unbroken",
        SourceEntityId = "unknown",
        MaxHealthDelta = bonus,
        ExpiresAtPulse = expires,
    };

    // -----------------------------------------------------------------------
    // Maximum health
    // -----------------------------------------------------------------------

    [Fact]
    public void Applying_it_raises_the_ceiling_and_hands_over_the_health()
    {
        // Raising the ceiling alone would be a buff that does nothing at the moment you need it:
        // 40/100 becoming 40/150 is further from safety than before.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var actor = harness.AddPlayer("Kael", West);

        actor.Character.Vitals.HealthMax = 100;
        actor.Character.Vitals.Health = 40;

        harness.World.ApplyEffect(actor.Character.Id, MaxHealth(50, expires: 100));

        Assert.Equal(150, actor.Character.Vitals.HealthMax);
        Assert.Equal(90, actor.Character.Vitals.Health);
    }

    [Fact]
    public void Re_applying_it_grants_no_further_health()
    {
        // The whole difference between this and a heal. A refresh tops the ceiling back up but
        // hands over nothing, so the ability cannot be milked as a repeatable top-up.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var actor = harness.AddPlayer("Kael", West);

        actor.Character.Vitals.HealthMax = 100;
        actor.Character.Vitals.Health = 40;

        harness.World.ApplyEffect(actor.Character.Id, MaxHealth(50, expires: 100));
        harness.World.ApplyEffect(actor.Character.Id, MaxHealth(50, expires: 200));

        Assert.Equal(150, actor.Character.Vitals.HealthMax);
        Assert.Equal(90, actor.Character.Vitals.Health);
    }

    [Fact]
    public void Expiring_lowers_the_ceiling_and_clamps_health_under_it()
    {
        // 150/150 becomes 100/100, not 150/100.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var actor = harness.AddPlayer("Kael", West);

        actor.Character.Vitals.HealthMax = 100;
        actor.Character.Vitals.Health = 100;

        harness.World.ApplyEffect(actor.Character.Id, MaxHealth(50, expires: 10));
        Assert.Equal(150, actor.Character.Vitals.Health);

        harness.World.ExpireEffects(currentPulse: 20);

        Assert.Equal(100, actor.Character.Vitals.HealthMax);
        Assert.Equal(100, actor.Character.Vitals.Health);
    }

    [Fact]
    public void Expiring_leaves_a_wounded_character_where_they_were()
    {
        // Only the excess is clamped away. Somebody who spent the granted health and then some
        // must not be topped up by the buff ending.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var actor = harness.AddPlayer("Kael", West);

        actor.Character.Vitals.HealthMax = 100;
        actor.Character.Vitals.Health = 100;

        harness.World.ApplyEffect(actor.Character.Id, MaxHealth(50, expires: 10));
        actor.Character.Vitals.Health = 30;

        harness.World.ExpireEffects(currentPulse: 20);

        Assert.Equal(100, actor.Character.Vitals.HealthMax);
        Assert.Equal(30, actor.Character.Vitals.Health);
    }

    [Fact]
    public void A_buff_larger_than_the_whole_maximum_cannot_expire_somebody_to_zero()
    {
        // An authored delta bigger than the bearer's own maximum would otherwise kill them when it
        // ran out, which is not a death §4.12 describes.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var actor = harness.AddPlayer("Kael", West);

        actor.Character.Vitals.HealthMax = 40;
        actor.Character.Vitals.Health = 40;

        harness.World.ApplyEffect(actor.Character.Id, MaxHealth(500, expires: 10));
        harness.World.ExpireEffects(currentPulse: 20);

        Assert.True(actor.Character.Vitals.HealthMax >= 1);
        Assert.True(actor.Character.Vitals.Health >= 1);
    }

    // -----------------------------------------------------------------------
    // The guard, read by combat
    // -----------------------------------------------------------------------

    [Fact]
    public void A_guard_makes_its_bearer_harder_to_hit()
    {
        // The property that matters: the effect reaches the defender combat actually rolls
        // against. Asserted through swings rather than on the effect, because a buff nothing
        // consults is exactly the silent failure this area keeps producing.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        // A deliberately decisive guard, and the size is worth explaining rather than tuning
        // quietly. The first version used +20 and failed while the code was correct: the harness
        // mob's attack rating is high enough to clear a defence of 30 on every roll, so a modest
        // guard changed no outcome and the test reported a working feature as broken. Nothing here
        // is measuring balance - the property is that the delta reaches the defender combat rolls
        // against at all, and a value that cannot be lost in the dice is the right way to ask it.
        var guarded = DamageTaken(guard: 500, seed: 20260814);
        var open = DamageTaken(guard: 0, seed: 20260814);

        Assert.True(
            guarded < open,
            $"Guarded took {guarded} damage and unguarded took {open}; " +
            "the guard is not reaching the defender combat rolls against.");
    }

    /// <summary>
    /// Runs a fixed exchange against a mob and returns the damage the *player* took.
    /// </summary>
    /// <remarks>
    /// A mob attacker rather than a duel, because a duel needs the pvp flag and a party check and
    /// none of that is what is being measured. Seeded RNG, so the two runs differ only by the
    /// guard.
    /// </remarks>
    private static int DamageTaken(int guard, int seed)
    {
        var harness = new WorldHarness(new SeededRandomSource(seed));
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Kael", West);
        var rat = harness.AddMob("rat", West, health: 10_000);

        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;

        if (guard > 0)
        {
            harness.World.ApplyEffect(
                player.Character.Id,
                Guard(defense: guard, armor: 0, expires: 100_000));
        }

        Engine.Systems.CombatEngagement.Engage(harness.World, player.Character, rat);

        var before = player.Character.Vitals.Health;
        harness.Pump(400);

        return before - player.Character.Vitals.Health;
    }
}
