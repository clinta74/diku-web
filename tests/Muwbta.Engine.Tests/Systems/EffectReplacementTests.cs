using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// A stronger application of an effect replaces a weaker one; a weaker one does nothing.
/// </summary>
/// <remarks>
/// <para>
/// <c>WorldState.ApplyEffect</c> dedupes on (<c>EffectKey</c>, <c>SourceEntityId</c>), so every one
/// of a Path's maximum-health buffs collides with the rest — and <c>Refresh</c> kept the
/// <em>first</em> effect's numbers. Sanctuary cast over Fortitude was worth +150 rather than +220:
/// the weaker buff won, and then outlived itself.
/// </para>
/// <para>
/// The other half is the one that was exploitable. Re-applying the weaker ability refreshed the
/// clock while leaving the stronger numbers in place, so a Temper could hold Hemorrhage's
/// 16-damage bleed open indefinitely with a cheap Ambush. A weaker application is now ignored
/// outright rather than extending anything.
/// </para>
/// </remarks>
public sealed class EffectReplacementTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static ActiveEffect MaxHealth(
        string source,
        int bonus,
        int level,
        long expiresAt = 480) => new()
        {
            EffectKey = "buff.max-health",
            Name = "warded",
            SourceEntityId = source,
            MaxHealthDelta = bonus,
            ExpiresAtPulse = expiresAt,
            SourceUnlockLevel = level,
        };

    private static (WorldHarness Harness, Character Subject, string Caster) Subject(
        int health = 120,
        int healthMax = 120)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var actor = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 50);
        actor.Character.Vitals.HealthMax = healthMax;
        actor.Character.Vitals.Health = health;

        return (harness, actor.Character, $"c_{actor.CharacterId:N}");
    }

    // -----------------------------------------------------------------------
    // Magnitude
    // -----------------------------------------------------------------------

    /// <summary>The Hallow case: Fortitude then Sanctuary must be worth Sanctuary.</summary>
    [Fact]
    public void A_stronger_buff_replaces_a_weaker_one()
    {
        var (harness, subject, caster) = Subject();

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 150, level: 28));
        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 220, level: 40));

        var effect = Assert.Single(harness.World.GetActiveEffects(subject.Id));
        Assert.Equal(220, effect.MaxHealthDelta);
        Assert.Equal(40, effect.SourceUnlockLevel);
    }

    /// <summary>And the reverse order leaves the same answer.</summary>
    [Fact]
    public void A_weaker_buff_cast_over_a_stronger_one_changes_nothing()
    {
        var (harness, subject, caster) = Subject();

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 220, level: 40));
        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 150, level: 28));

        var effect = Assert.Single(harness.World.GetActiveEffects(subject.Id));
        Assert.Equal(220, effect.MaxHealthDelta);
    }

    /// <summary>
    /// <b>And it does not move the expiry</b>, which is the half that is easy to get wrong and the
    /// half that was exploitable: extending a strong effect with a cheap weak one.
    /// </summary>
    [Fact]
    public void A_weaker_application_does_not_extend_what_is_running()
    {
        var (harness, subject, caster) = Subject();

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 220, level: 40, expiresAt: 300));
        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 150, level: 28, expiresAt: 9000));

        Assert.Equal(300, Assert.Single(harness.World.GetActiveEffects(subject.Id)).ExpiresAtPulse);
    }

    /// <summary>The stronger one brings its own clock with it.</summary>
    [Fact]
    public void A_stronger_application_brings_its_own_expiry()
    {
        var (harness, subject, caster) = Subject();

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 150, level: 28, expiresAt: 9000));
        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 220, level: 40, expiresAt: 300));

        Assert.Equal(300, Assert.Single(harness.World.GetActiveEffects(subject.Id)).ExpiresAtPulse);
    }

    // -----------------------------------------------------------------------
    // The health arithmetic, from a damaged bar so a leak would show
    // -----------------------------------------------------------------------

    /// <summary>
    /// Replacing revokes the old ceiling and grants the new one, which lands the bearer exactly
    /// where the stronger buff alone would have — and the ceiling is what a leak would show in.
    /// </summary>
    [Fact]
    public void Replacing_leaves_the_ceiling_the_stronger_buff_alone_would_have_given()
    {
        var (weakFirst, damaged, caster) = Subject(health: 60, healthMax: 120);
        weakFirst.World.ApplyEffect(damaged.Id, MaxHealth(caster, 80, level: 20));
        weakFirst.World.ApplyEffect(damaged.Id, MaxHealth(caster, 200, level: 40));

        var (alone, direct, otherCaster) = Subject(health: 60, healthMax: 120);
        alone.World.ApplyEffect(direct.Id, MaxHealth(otherCaster, 200, level: 40));

        Assert.Equal(direct.Vitals.HealthMax, damaged.Vitals.HealthMax);
        Assert.Equal(320, damaged.Vitals.HealthMax);
    }

    /// <summary>
    /// Health never ends above the ceiling, and the first grant is not clawed back — the bearer is
    /// better off for having cast the weak one first, which is honest: they did cast it.
    /// </summary>
    [Fact]
    public void Health_stays_under_the_ceiling_through_a_replacement()
    {
        var (harness, subject, caster) = Subject(health: 60, healthMax: 120);

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 80, level: 20));
        Assert.Equal(140, subject.Vitals.Health);

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 200, level: 40));

        Assert.Equal(320, subject.Vitals.HealthMax);
        Assert.Equal(320, subject.Vitals.Health);
        Assert.True(subject.Vitals.Health <= subject.Vitals.HealthMax);
    }

    /// <summary>
    /// <b>No ratchet.</b> Weak then strong grants exactly what strong then weak does, so alternating
    /// them cannot pump the bar upwards.
    /// </summary>
    [Fact]
    public void Alternating_the_two_orders_cannot_pump_the_bar()
    {
        var (up, rising, upCaster) = Subject(health: 60, healthMax: 120);
        var (down, falling, downCaster) = Subject(health: 60, healthMax: 120);

        for (var i = 0; i < 5; i++)
        {
            up.World.ApplyEffect(rising.Id, MaxHealth(upCaster, 80, level: 20));
            up.World.ApplyEffect(rising.Id, MaxHealth(upCaster, 200, level: 40));

            down.World.ApplyEffect(falling.Id, MaxHealth(downCaster, 200, level: 40));
            down.World.ApplyEffect(falling.Id, MaxHealth(downCaster, 80, level: 20));
        }

        Assert.Equal(320, rising.Vitals.HealthMax);
        Assert.Equal(320, falling.Vitals.HealthMax);
    }

    // -----------------------------------------------------------------------
    // Equal strength keeps every behaviour it had
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recasting the same ability refreshes and grants no more health, so
    /// <c>MaxHealthEffect</c>'s once-only grant survives intact — that rule is the whole difference
    /// between the buff and a heal on a short cooldown.
    /// </summary>
    [Fact]
    public void Recasting_the_same_ability_refreshes_without_granting_again()
    {
        var (harness, subject, caster) = Subject(health: 60, healthMax: 120);

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 200, level: 40, expiresAt: 300));
        var afterFirst = subject.Vitals.Health;

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 200, level: 40, expiresAt: 900));

        Assert.Equal(afterFirst, subject.Vitals.Health);
        Assert.Equal(320, subject.Vitals.HealthMax);
        Assert.Equal(900, Assert.Single(harness.World.GetActiveEffects(subject.Id)).ExpiresAtPulse);
    }

    /// <summary>
    /// Two mob riders carry no ability, compare equal, and refresh exactly as they always did — so
    /// no tuned fight changes.
    /// </summary>
    [Fact]
    public void Two_effects_with_no_ability_behind_them_still_refresh()
    {
        var (harness, subject, _) = Subject();
        const string mob = "m_deadbeef";

        harness.World.ApplyEffect(subject.Id, new ActiveEffect
        {
            EffectKey = "debuff.weaken",
            Name = "weakened",
            SourceEntityId = mob,
            OutgoingDamageMultiplier = 0.7m,
            ExpiresAtPulse = 100,
        });

        harness.World.ApplyEffect(subject.Id, new ActiveEffect
        {
            EffectKey = "debuff.weaken",
            Name = "weakened",
            SourceEntityId = mob,
            OutgoingDamageMultiplier = 0.7m,
            ExpiresAtPulse = 400,
        });

        var effect = Assert.Single(harness.World.GetActiveEffects(subject.Id));
        Assert.Equal(400, effect.ExpiresAtPulse);
        Assert.Equal(0, effect.SourceUnlockLevel);
    }

    /// <summary>
    /// Two casters are two effects, whatever their levels. They never collided and still do not —
    /// <c>SourceEntityId</c> keeps them apart before strength is ever consulted.
    /// </summary>
    [Fact]
    public void Two_casters_are_two_effects()
    {
        var (harness, subject, caster) = Subject();

        harness.World.ApplyEffect(subject.Id, MaxHealth(caster, 220, level: 40));
        harness.World.ApplyEffect(subject.Id, MaxHealth("c_someoneelse", 150, level: 28));

        Assert.Equal(2, harness.World.GetActiveEffects(subject.Id).Count);
    }

    // -----------------------------------------------------------------------
    // Through the cast path, which is what stamps the level
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The test the rest of this class cannot replace.</b> Every case above sets
    /// <c>SourceUnlockLevel</c> by hand, so all twelve would go on passing if <c>AbilitySystem</c>
    /// stopped stamping it — and the whole rule would quietly do nothing. This one casts the real
    /// abilities and reads what lands.
    ///
    /// The Hallow's wards are the case that bites in play: 300-second durations against 24s and 60s
    /// cooldowns, deliberately left off any shared timer, so both are up together constantly.
    /// </summary>
    [Fact]
    public void Sanctuary_cast_over_fortitude_is_worth_sanctuary()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("hallow.fortitude");
        harness.DefineAbility("hallow.sanctuary");

        var actor = harness.AddPlayer("Wen", West, path: CharacterPath.Hallow, level: 50);
        actor.Character.Vitals.FocusMax = 1000;
        actor.Character.Vitals.Focus = 1000;

        Cast(harness, actor, "cast fortitude");
        var afterFortitude = harness.World
            .GetActiveEffects(actor.CharacterId)
            .Single(e => e.EffectKey == "buff.max-health");

        Assert.Equal(150, afterFortitude.MaxHealthDelta);
        Assert.Equal(28, afterFortitude.SourceUnlockLevel);

        Cast(harness, actor, "cast sanctuary");
        var afterSanctuary = harness.World
            .GetActiveEffects(actor.CharacterId)
            .Single(e => e.EffectKey == "buff.max-health");

        Assert.Equal(220, afterSanctuary.MaxHealthDelta);
        Assert.Equal(40, afterSanctuary.SourceUnlockLevel);
    }

    /// <summary>And Fortitude after Sanctuary leaves Sanctuary standing, untouched.</summary>
    [Fact]
    public void Fortitude_cast_over_sanctuary_leaves_sanctuary_alone()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("hallow.fortitude");
        harness.DefineAbility("hallow.sanctuary");

        var actor = harness.AddPlayer("Wen", West, path: CharacterPath.Hallow, level: 50);
        actor.Character.Vitals.FocusMax = 1000;
        actor.Character.Vitals.Focus = 1000;

        Cast(harness, actor, "cast sanctuary");
        var expiry = harness.World
            .GetActiveEffects(actor.CharacterId)
            .Single(e => e.EffectKey == "buff.max-health")
            .ExpiresAtPulse;

        Cast(harness, actor, "cast fortitude");
        var standing = harness.World
            .GetActiveEffects(actor.CharacterId)
            .Single(e => e.EffectKey == "buff.max-health");

        Assert.Equal(220, standing.MaxHealthDelta);
        Assert.Equal(expiry, standing.ExpiresAtPulse);
    }

    /// <summary>Runs a cast and pumps long enough for its cast bar to finish.</summary>
    private static void Cast(WorldHarness harness, PlayerActor actor, string input)
    {
        harness.Drain(actor);
        harness.Execute(actor, input);

        // Longer than any cast time in the catalogue, so the job leaves the queue and resolves.
        harness.Pump(32);
        harness.Drain(actor);
    }

    // -----------------------------------------------------------------------
    // The rule reaches effects that are not buffs
    // -----------------------------------------------------------------------

    /// <summary>A short stun no longer extends a long one, which it used to for free.</summary>
    [Fact]
    public void A_short_stun_does_not_extend_a_long_one()
    {
        var (harness, subject, caster) = Subject();

        ActiveEffect Stun(long expires, int level) => new()
        {
            EffectKey = "control.stun",
            Name = "stunned",
            SourceEntityId = caster,
            PreventsActing = true,
            ExpiresAtPulse = expires,
            SourceUnlockLevel = level,
        };

        harness.World.ApplyEffect(subject.Id, Stun(expires: 24, level: 43));
        harness.World.ApplyEffect(subject.Id, Stun(expires: 8, level: 9));

        Assert.Equal(24, Assert.Single(harness.World.GetActiveEffects(subject.Id)).ExpiresAtPulse);
    }

    /// <summary>
    /// And the Temper's bleed: a cheap Ambush cannot hold Hemorrhage's tick open.
    /// </summary>
    [Fact]
    public void A_weak_wound_cannot_keep_a_strong_one_alive()
    {
        var (harness, subject, caster) = Subject();

        ActiveEffect Wound(int tick, long expires, int level) => new()
        {
            EffectKey = "damage.overtime",
            Name = "bleeding",
            SourceEntityId = caster,
            TickDamage = tick,
            TickIntervalPulses = 8,
            NextTickPulse = 8,
            ExpiresAtPulse = expires,
            SourceUnlockLevel = level,
        };

        harness.World.ApplyEffect(subject.Id, Wound(tick: 16, expires: 96, level: 36));
        harness.World.ApplyEffect(subject.Id, Wound(tick: 5, expires: 9000, level: 10));

        var effect = Assert.Single(harness.World.GetActiveEffects(subject.Id));
        Assert.Equal(16, effect.TickDamage);
        Assert.Equal(96, effect.ExpiresAtPulse);
    }
}
