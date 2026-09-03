using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Entities;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Abilities;

/// <summary>
/// Abilities answer to the same damage buffs and debuffs a weapon swing does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by asking whether the abilities do what they say.</b> They mostly did — and then
/// seventeen of them turned out to describe a multiplier the engine applied in exactly one place:
/// the weapon strike. <c>DamageEffect.Apply</c> takes <c>(caster, target, params, random)</c> and
/// has no access to the world, so it could not see an active effect even in principle; the
/// damage-over-time tick read <c>TickDamage × Stacks</c> and applied it raw.
/// </para>
/// <para>
/// <b>The Adept was the worst case</b>, because an Adept's damage is very nearly all abilities.
/// Arcane Surge — <em>"raises your damage by 60% for 40s"</em>, the capstone of that Path's
/// offence — improved their melee and nothing else. Sunder's <em>"raises the damage your target
/// takes by 30%"</em> did nothing for anyone casting at the sundered mob.
/// </para>
/// <para>
/// Every test here pairs a buffed cast against an unbuffed one on <b>separate harnesses seeded
/// alike</b>, so both roll the same number out of <c>SeededRandomSource</c> and the comparison is
/// exact rather than statistical. Asserting an absolute number instead would pin the ±20% variance
/// as well as the multiplier, and break on any retune of Bolt.
/// </para>
/// </remarks>
public sealed class AbilityDamageMultiplierTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    /// <summary>Middle 12, variance ±2 — <c>scalingFactor 1.2</c> over the flat base of 10.</summary>
    private const string Bolt = "adept.bolt";

    private static ActiveEffect Multiplier(
        Guid source,
        decimal outgoing = 1.0m,
        decimal incoming = 1.0m,
        int tickDamage = 0) => new()
    {
        EffectKey = tickDamage > 0 ? "damage.overtime" : "test.multiplier",
        Name = "tested",
        SourceEntityId = EntityId.ForCharacter(source),
        OutgoingDamageMultiplier = outgoing,
        IncomingDamageMultiplier = incoming,
        TickDamage = tickDamage,
        TickIntervalPulses = 8,
        ExpiresAtPulse = long.MaxValue,
        Stacks = 1,
        MaxStacks = 1,
        StackingRule = EffectStackingRule.Refresh,
    };

    /// <summary>
    /// One Bolt at a rat, and what it cost the rat.
    /// </summary>
    /// <remarks>
    /// The effects are applied straight to the world rather than cast, so that what is under test
    /// is the multiplier and not a second ability's cost, cooldown and cast time.
    /// </remarks>
    private static int BoltDamage(
        decimal onCaster = 1.0m,
        decimal onTarget = 1.0m)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility(Bolt);

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.FocusMax = 500;
        caster.Character.Vitals.Focus = 500;

        var rat = harness.AddMob("rat", Room, name: "rat", health: 500);

        if (onCaster != 1.0m)
        {
            harness.World.ApplyEffect(
                caster.CharacterId, Multiplier(caster.CharacterId, outgoing: onCaster));
        }

        if (onTarget != 1.0m)
        {
            harness.World.ApplyEffect(
                rat.Id, Multiplier(caster.CharacterId, incoming: onTarget));
        }

        var before = rat.Vitals.Health;
        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        return before - rat.Vitals.Health;
    }

    // -----------------------------------------------------------------------
    // The gap, stated
    // -----------------------------------------------------------------------

    /// <summary>A caster's damage buff reaches what the caster casts.</summary>
    /// <remarks>
    /// Failed before this change: the two numbers were identical, because the buff was read only
    /// inside the weapon strike.
    /// </remarks>
    [Fact]
    public void A_damage_buff_on_the_caster_raises_ability_damage()
    {
        var plain = BoltDamage();
        var buffed = BoltDamage(onCaster: 1.6m);

        Assert.True(
            buffed > plain,
            $"Arcane Surge should raise a Bolt; it dealt {buffed} against {plain} unbuffed.");

        Assert.Equal(DamageMultipliers.Apply(plain, 1.6m), buffed);
    }

    /// <summary>And a debuff on the target reaches what is cast at the target.</summary>
    [Fact]
    public void A_vulnerability_on_the_target_raises_ability_damage()
    {
        var plain = BoltDamage();
        var sundered = BoltDamage(onTarget: 1.3m);

        Assert.Equal(DamageMultipliers.Apply(plain, 1.3m), sundered);
    }

    /// <summary>
    /// The two compose, so a fury and a curse are worth both rather than whichever landed last.
    /// </summary>
    [Fact]
    public void A_buff_and_a_vulnerability_compound()
    {
        var plain = BoltDamage();
        var both = BoltDamage(onCaster: 1.6m, onTarget: 1.3m);

        Assert.Equal(DamageMultipliers.Apply(plain, 1.6m * 1.3m), both);
    }

    /// <summary>A weaken blunts an ability as well as a swing.</summary>
    [Fact]
    public void A_weaken_on_the_caster_lowers_ability_damage()
    {
        var plain = BoltDamage();
        var sapped = BoltDamage(onCaster: 0.55m);

        Assert.True(sapped < plain, $"A sapped Adept dealt {sapped} against {plain} unsapped.");
        Assert.Equal(DamageMultipliers.Apply(plain, 0.55m), sapped);
    }

    // -----------------------------------------------------------------------
    // What must not move
    // -----------------------------------------------------------------------

    /// <summary>
    /// A damage buff does not inflate a heal.
    /// </summary>
    /// <remarks>
    /// <b>The constraint that makes scaling the wound safe.</b> The health delta is the one measure
    /// every executor has in common, which is exactly why it has to be read with its sign: a heal
    /// moves health the other way, and a "damage up" buff that grew one would be the same
    /// silent-wrong-field bug wearing the opposite face.
    /// </remarks>
    [Theory]
    [InlineData(1.6)]
    [InlineData(0.55)]
    public void A_damage_multiplier_leaves_healing_alone(double multiplier)
    {
        static int Healed(decimal onCaster)
        {
            var harness = new WorldHarness();
            harness.LoadTestWorld();
            harness.DefineAbility("hallow.mend");

            var hallow = harness.AddPlayer("Bram", Room, path: CharacterPath.Hallow, level: 1);
            hallow.Character.Vitals.FocusMax = 500;
            hallow.Character.Vitals.Focus = 500;
            hallow.Character.Vitals.HealthMax = 500;
            hallow.Character.Vitals.Health = 5;

            if (onCaster != 1.0m)
            {
                harness.World.ApplyEffect(
                    hallow.CharacterId, Multiplier(hallow.CharacterId, outgoing: onCaster));
            }

            var before = hallow.Character.Vitals.Health;
            harness.Execute(hallow, "cast mend");
            harness.Pump(20);

            return hallow.Character.Vitals.Health - before;
        }

        var plain = Healed(1.0m);

        Assert.True(plain > 0, "The unbuffed heal must actually heal, or this proves nothing.");
        Assert.Equal(plain, Healed((decimal)multiplier));
    }

    // -----------------------------------------------------------------------
    // Wounds that keep working
    // -----------------------------------------------------------------------

    /// <summary>
    /// A bleed tick answers to the buffs on whoever opened it.
    /// </summary>
    /// <remarks>
    /// Read live rather than frozen when the wound was applied, the same way a swing reads it: the
    /// damage happens at this pulse, so it is this pulse's buffs that decide it.
    /// </remarks>
    [Fact]
    public void A_bleed_tick_is_scaled_by_the_buff_on_whoever_applied_it()
    {
        static int Bled(decimal onCaster)
        {
            var harness = new WorldHarness();
            harness.LoadTestWorld();
            harness.DefineAbility(Bolt);

            var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
            caster.Character.Vitals.FocusMax = 500;
            caster.Character.Vitals.Focus = 500;

            var rat = harness.AddMob("rat", Room, name: "rat", health: 500);

            if (onCaster != 1.0m)
            {
                harness.World.ApplyEffect(
                    caster.CharacterId, Multiplier(caster.CharacterId, outgoing: onCaster));
            }

            // A fight has to exist for a wound to tick in - TickDamageOverTime walks a combat's
            // combatants, not the room.
            harness.Execute(caster, "cast bolt rat");
            harness.Pump(12);

            var opened = rat.Vitals.Health;
            harness.World.ApplyEffect(
                rat.Id, Multiplier(caster.CharacterId, tickDamage: 20));

            harness.Pump(10);

            return opened - rat.Vitals.Health;
        }

        var plain = Bled(1.0m);

        Assert.True(plain > 0, "The wound must tick at all, or this proves nothing.");
        Assert.Equal(DamageMultipliers.Apply(plain, 1.6m), Bled(1.6m));
    }

    /// <summary>
    /// A wound whose caster cannot be resolved still ticks, rather than taking the loop down.
    /// </summary>
    /// <remarks>
    /// <c>EffectSource.Of</c> writes the literal <c>"unknown"</c> for a caster that is neither a
    /// character nor a mob, and <c>EntityId.ToGuid</c> throws on anything unprefixed. A malformed
    /// id has killed this loop once already (HISTORY.md, 5.2f), and a dead loop is a dead world
    /// for everyone connected — a great deal worse than a bleed that ticks unbuffed.
    /// </remarks>
    [Fact]
    public void A_wound_from_an_unresolvable_caster_still_ticks()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility(Bolt);

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.FocusMax = 500;
        caster.Character.Vitals.Focus = 500;

        var rat = harness.AddMob("rat", Room, name: "rat", health: 500);

        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        var opened = rat.Vitals.Health;

        harness.World.ApplyEffect(rat.Id, new ActiveEffect
        {
            EffectKey = "damage.overtime",
            Name = "bleeding",
            SourceEntityId = "unknown",
            TickDamage = 20,
            TickIntervalPulses = 8,
            ExpiresAtPulse = long.MaxValue,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        });

        harness.Pump(10);

        Assert.True(rat.Vitals.Health < opened, "The wound should still have ticked.");
    }

    // -----------------------------------------------------------------------
    // What follows from scaling the wound
    // -----------------------------------------------------------------------

    /// <summary>
    /// Threat is credited on what landed, not on what was rolled.
    /// </summary>
    /// <remarks>
    /// Free, and worth pinning anyway: <c>CreditThreat</c> derives from the same <c>before</c> the
    /// scaling writes through, so the hate list follows without being touched. If it ever stops
    /// following, the Path that deals the most damage stops being able to pull — which is the bug
    /// <c>ThreatCredit</c> was written for.
    /// </remarks>
    [Fact]
    public void Threat_is_credited_on_the_scaled_damage()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility(Bolt);

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.FocusMax = 500;
        caster.Character.Vitals.Focus = 500;

        var rat = harness.AddMob("rat", Room, name: "rat", health: 500);

        harness.World.ApplyEffect(
            caster.CharacterId, Multiplier(caster.CharacterId, outgoing: 1.6m));

        var before = rat.Vitals.Health;
        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        var dealt = before - rat.Vitals.Health;

        var hate = harness.World.FindCombat(Room)?.HateOf(
            EntityId.ForMob(rat.Id), EntityId.ForCharacter(caster.CharacterId)) ?? 0;

        Assert.True(dealt > 0);

        // Plus the point CombatEngagement seeds so a mob has someone to fight from the moment it
        // is engaged rather than from the moment it is first hurt.
        Assert.Equal(dealt + CombatEngagement.OpeningThreat, hate);
    }

    /// <summary>The line a player reads carries the scaled number too.</summary>
    [Fact]
    public void The_narration_reports_what_actually_landed()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility(Bolt);

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.FocusMax = 500;
        caster.Character.Vitals.Focus = 500;

        var rat = harness.AddMob("rat", Room, name: "rat", health: 500);

        harness.World.ApplyEffect(
            caster.CharacterId, Multiplier(caster.CharacterId, outgoing: 1.6m));

        var before = rat.Vitals.Health;
        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        var dealt = before - rat.Vitals.Health;

        // "a rat" - a mob takes the indefinite article in prose, a player takes none.
        Assert.Contains(
            $"hits a rat for {dealt}",
            harness.DrainText(caster),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A buff big enough to finish something still finishes it.
    /// </summary>
    /// <remarks>
    /// The scaling writes health through a second time, so the clamp at zero has to survive that.
    /// Without it a heavily buffed blow would drive health negative and <c>IsDead</c> would still
    /// be true — but every number on screen would be wrong.
    /// </remarks>
    [Fact]
    public void A_scaled_blow_that_reaches_zero_still_kills()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility(Bolt);

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.FocusMax = 500;
        caster.Character.Vitals.Focus = 500;

        var rat = harness.AddMob("rat", Room, name: "rat", health: 14);

        harness.World.ApplyEffect(
            caster.CharacterId, Multiplier(caster.CharacterId, outgoing: 4.0m));

        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        Assert.Equal(0, rat.Vitals.Health);
    }
}
