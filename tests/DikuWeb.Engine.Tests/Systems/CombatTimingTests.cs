using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Every combatant swings on its own clock. These are the rules that clock obeys - and until
/// this file existed, nothing anywhere ticked the engine's combat system at all.
/// </summary>
/// <remarks>
/// The oracle throughout is <em>which pulse a swing lands on</em>, so every test pumps one pulse
/// at a time and records the pulses that produced attack narration. Assertions name exact pulses
/// rather than counts: "it hit three times in twelve pulses" would pass for a weapon firing on
/// entirely the wrong beat.
/// </remarks>
public sealed class CombatTimingTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>
    /// Attack ratings are set high enough that every blow lands, so a missing swing means the
    /// clock did not fire rather than the dice went the other way. A side effect is that every
    /// blow is also a critical, which doubles the dice - the damage assertions account for it.
    /// </summary>
    private const int MobHealthThatOutlastsTheTest = 100_000;

    [Fact]
    public void A_weapons_delay_sets_the_cadence()
    {
        var fight = Fight(mainHandDelay: 4);

        Assert.Equal([4, 8, 12], fight.PlayerSwingPulses(through: 13));
    }

    [Fact]
    public void An_unarmed_hand_swings_at_the_pre_existing_rate()
    {
        // Nothing wielded, nothing authored: the 8-pulse round the whole game used to share.
        var fight = Fight(mainHandDelay: null, wieldMainHand: false);

        Assert.Equal([8, 16], fight.PlayerSwingPulses(through: 17));
    }

    [Fact]
    public void A_weapon_with_no_declared_speed_swings_at_the_default()
    {
        var fight = Fight(mainHandDelay: null);

        Assert.Equal([8, 16], fight.PlayerSwingPulses(through: 17));
    }

    [Fact]
    public void A_delay_below_the_floor_is_clamped_at_runtime()
    {
        // The builder API refuses this, but a row written before the rule - or by hand - must
        // not be able to outrun the engine either.
        var fight = Fight(mainHandDelay: 1);

        Assert.Equal([4, 8, 12], fight.PlayerSwingPulses(through: 13));
    }

    [Fact]
    public void The_first_swing_costs_a_full_delay()
    {
        // Never on the pulse you engage: otherwise flee-and-re-attack is a free opening blow.
        var fight = Fight(mainHandDelay: 4);

        Assert.DoesNotContain(0, fight.PlayerSwingPulses(through: 3));
    }

    // --- The off hand ----------------------------------------------------

    [Fact]
    public void An_untrained_off_hand_never_swings()
    {
        // A Warden learns to dual-wield at 5. At 4 the dagger is carried, not swung.
        var fight = Fight(mainHandDelay: 8, offHandDelay: 4, level: 4);

        Assert.Empty(fight.OffHandSwingPulses(through: 33));
        Assert.NotEmpty(fight.MainHandSwingPulses(through: 33));
    }

    [Theory]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Channeler)]
    public void A_path_that_never_learns_it_never_swings_an_off_hand(CharacterPath path)
    {
        var fight = Fight(mainHandDelay: 8, offHandDelay: 4, level: 50, path: path);

        Assert.Empty(fight.OffHandSwingPulses(through: 33));
    }

    [Fact]
    public void A_trained_off_hand_rides_the_main_hands_rhythm()
    {
        // Warden 5 has dual-wield but not ambidexterity: the dagger cannot lead, so despite
        // being twice as fast it lands with the sword rather than between its blows.
        var fight = Fight(mainHandDelay: 8, offHandDelay: 4, level: 5);

        Assert.Equal([8, 16, 24], fight.MainHandSwingPulses(through: 25));
        Assert.Equal([8, 16, 24], fight.OffHandSwingPulses(through: 25));
    }

    [Fact]
    public void A_slow_off_hand_still_waits_for_its_own_delay()
    {
        // Main every 4, off every 12: the off hand skips the main-hand blows it is not ready for.
        var fight = Fight(mainHandDelay: 4, offHandDelay: 12, level: 5);

        Assert.Equal([4, 8, 12, 16, 20, 24], fight.MainHandSwingPulses(through: 25));
        Assert.Equal([12, 24], fight.OffHandSwingPulses(through: 25));
    }

    [Fact]
    public void Ambidexterity_frees_the_off_hand_from_the_main_hand()
    {
        // Shade 10. The same pair of weapons now beats independently.
        var fight = Fight(mainHandDelay: 8, offHandDelay: 4, level: 10, path: CharacterPath.Shade);

        Assert.Equal([8, 16], fight.MainHandSwingPulses(through: 17));
        Assert.Equal([4, 8, 12, 16], fight.OffHandSwingPulses(through: 17));
    }

    [Fact]
    public void Ambidexterity_is_level_gated()
    {
        // One level short: trained to dual-wield, not yet to lead with the off hand.
        var fight = Fight(mainHandDelay: 8, offHandDelay: 4, level: 9, path: CharacterPath.Shade);

        Assert.Equal([8, 16], fight.OffHandSwingPulses(through: 17));
    }

    /// <summary>
    /// The single most likely regression in the whole change: a shield declares no attack delay,
    /// and the nullable column is the only thing that says "this is not a weapon".
    /// </summary>
    [Fact]
    public void A_shield_in_the_off_hand_never_swings()
    {
        // Ambidextrous Warden, so nothing but the missing delay is holding the shield back.
        var fight = Fight(mainHandDelay: 8, offHandDelay: null, equipOffHand: true, level: 15);

        Assert.Empty(fight.OffHandSwingPulses(through: 33));
        Assert.Equal([8, 16, 24, 32], fight.MainHandSwingPulses(through: 33));
    }

    // --- Death -----------------------------------------------------------

    /// <summary>
    /// The reported bug: "attacking a mob and killing it on the first strike the mob can still
    /// hit back". Both are due on the same pulse; the player resolves first and the rat is out
    /// of the fight before its own attack is considered.
    /// </summary>
    [Fact]
    public void A_mob_killed_this_pulse_does_not_hit_back()
    {
        var fight = Fight(
            mainHandDelay: 4,
            mobAttacks: [new MobAttack { Verb = "bite", DelayPulses = 4 }],
            mobHealth: 1,
            mobDamage: 5);

        fight.Pump(through: 8);

        Assert.Contains("a rat falls", fight.Log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bites you", fight.Log, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fight.Player.Character.Vitals.HealthMax, fight.Player.Character.Vitals.Health);
    }

    [Fact]
    public void A_dead_mob_is_removed_and_its_fight_reclaimed()
    {
        var fight = Fight(mainHandDelay: 4, mobHealth: 1);

        fight.Pump(through: 8);

        Assert.Empty(fight.Harness.World.MobsIn(West));
        Assert.Null(fight.Harness.World.FindCombat(West));
        Assert.Equal(CombatState.Idle, fight.Player.Character.CombatState);
    }

    // --- Mob attack arrays ------------------------------------------------

    [Fact]
    public void Each_mob_attack_runs_its_own_clock()
    {
        var fight = Fight(
            mainHandDelay: 40,
            mobAttacks:
            [
                new MobAttack { Verb = "bite", DelayPulses = 4 },
                new MobAttack { Verb = "claw", DelayPulses = 6 },
            ],
            mobHealth: MobHealthThatOutlastsTheTest);

        fight.Pump(through: 13);

        Assert.Equal([4, 8, 12], fight.MobSwingPulses("bites"));
        Assert.Equal([6, 12], fight.MobSwingPulses("claws"));
    }

    [Fact]
    public void A_mob_with_no_authored_attacks_fights_as_it_always_did()
    {
        var fight = Fight(mainHandDelay: 40, mobAttacks: [], mobHealth: MobHealthThatOutlastsTheTest);

        fight.Pump(through: 17);

        Assert.Equal([8, 16], fight.MobSwingPulses("hits"));
    }

    [Fact]
    public void A_mob_attacks_damage_multiplier_scales_that_attack_only()
    {
        var fight = Fight(
            mainHandDelay: 40,
            mobAttacks:
            [
                new MobAttack { Verb = "bite", DelayPulses = 4, DamageMultiplier = 3m },
                new MobAttack { Verb = "claw", DelayPulses = 8 },
            ],
            mobHealth: MobHealthThatOutlastsTheTest,
            mobDamage: 2);

        fight.Pump(through: 9);

        // 2 damage a swing, tripled to 6 for the bite, and doubled again because every blow
        // here is a critical (see MobHealthThatOutlastsTheTest). The claw keeps the mob's own.
        Assert.Contains("bites you for 12 damage", fight.Log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("claws you for 4 damage", fight.Log, StringComparison.OrdinalIgnoreCase);
    }

    // --- Re-engaging and swapping ----------------------------------------

    [Fact]
    public void Re_engaging_costs_a_fresh_delay()
    {
        var fight = Fight(mainHandDelay: 4, mobHealth: MobHealthThatOutlastsTheTest);

        fight.Pump(through: 5);
        Assert.Equal([4], fight.PlayerSwingPulses(through: 5));

        fight.Harness.Execute(fight.Player, "flee");
        fight.Harness.Execute(fight.Player, "kill rat");
        fight.Pump(through: 13);

        // Re-engaged on pulse 5, so the next swing is at 9 - not immediately on rejoining.
        Assert.Equal([4, 9], fight.PlayerSwingPulses(through: 13));
    }

    [Fact]
    public void Fleeing_takes_the_player_off_the_mobs_hate_list()
    {
        // The mob used to keep swinging at someone who had just been told they escaped, because
        // RemoveCombatant only cleared the fighter's own hate list, never its entries in others'.
        var fight = Fight(
            mainHandDelay: 4,
            mobAttacks: [new MobAttack { Verb = "bite", DelayPulses = 4 }],
            mobHealth: MobHealthThatOutlastsTheTest);

        fight.Pump(through: 3);
        fight.Harness.Execute(fight.Player, "flee");
        fight.Harness.Drain(fight.Player);
        fight.Pump(through: 30);

        Assert.DoesNotContain("bites you", fight.Harness.DrainText(fight.Player), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CombatState.Idle, fight.Mob.CombatState);
    }

    [Fact]
    public void Swapping_weapons_mid_fight_retimes_from_the_next_check()
    {
        var fight = Fight(mainHandDelay: 12);
        var fast = fight.Harness.DefineWeapon("dirk", "a dirk", ItemSlot.MainHand, 4, "stab");

        fight.Pump(through: 6);
        Assert.Empty(fight.PlayerSwingPulses(through: 6));

        // Drop the slow blade for a fast one. Speed is read fresh at every readiness check, so
        // there is no schedule to invalidate: the dirk's 4 pulses have already elapsed since
        // engagement and it swings on the very next check.
        fight.MainHand!.EquippedSlot = null;
        fight.Harness.Equip(fight.Player, fast, ItemSlot.MainHand);
        fight.Pump(through: 9);

        Assert.Equal([6], fight.PlayerSwingPulses(through: 9));
    }

    // --- Casting ---------------------------------------------------------

    [Fact]
    public void A_pending_cast_silences_the_weapons()
    {
        var fight = Fight(mainHandDelay: 4);

        fight.Harness.World.CastQueue.Enqueue(new CastJob
        {
            CharacterId = fight.Player.CharacterId,
            AbilityKey = "warden.slash",
            ResolveAtPulse = 9,
            StartingRoomKey = West.ToString(),
        });

        // Nothing at 4 or 8 while the cast is up. It resolves on 9, and because the sword has
        // been ready since 4 it swings on that same pulse, then resumes its own cadence.
        Assert.Equal([9, 13], fight.PlayerSwingPulses(through: 14));
    }

    [Fact]
    public void Being_in_a_fight_no_longer_cancels_a_cast()
    {
        // This used to be an interrupt, which made casting in combat impossible - and made the
        // rule above unreachable, since a Fighting character could never be mid-cast.
        var fight = Fight(mainHandDelay: 40, mobAttacks: [], mobDamage: 0);

        fight.Harness.World.CastQueue.Enqueue(new CastJob
        {
            CharacterId = fight.Player.CharacterId,
            AbilityKey = "warden.slash",
            ResolveAtPulse = 20,
            StartingRoomKey = West.ToString(),
        });

        fight.Pump(through: 6);

        Assert.True(fight.Harness.World.CastQueue.IsCasting(fight.Player.CharacterId));
    }

    [Fact]
    public void A_blow_that_lands_breaks_concentration()
    {
        var fight = Fight(
            mainHandDelay: 40,
            mobAttacks: [new MobAttack { Verb = "bite", DelayPulses = 4 }],
            mobHealth: MobHealthThatOutlastsTheTest,
            mobDamage: 3);

        fight.Harness.World.CastQueue.Enqueue(new CastJob
        {
            CharacterId = fight.Player.CharacterId,
            AbilityKey = "warden.slash",
            ResolveAtPulse = 20,
            StartingRoomKey = West.ToString(),
        });

        fight.Pump(through: 6);

        Assert.False(fight.Harness.World.CastQueue.IsCasting(fight.Player.CharacterId));
        Assert.Contains("was interrupted", fight.Log, StringComparison.OrdinalIgnoreCase);
    }

    // --- Harness ---------------------------------------------------------

    /// <param name="offHandDelay">Null with <paramref name="equipOffHand"/> set is the shield case.</param>
    /// <param name="equipOffHand">Defaults to whether an off-hand delay was asked for.</param>
    private static FightFixture Fight(
        int? mainHandDelay,
        int? offHandDelay = null,
        bool? equipOffHand = null,
        bool wieldMainHand = true,
        int level = 1,
        CharacterPath path = CharacterPath.Warden,
        IEnumerable<MobAttack>? mobAttacks = null,
        int mobHealth = MobHealthThatOutlastsTheTest,
        int mobDamage = 1)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West, path: path, level: level);

        ItemInstance? mainHand = null;
        if (wieldMainHand)
        {
            var sword = harness.DefineWeapon(
                "sword", "a sword", ItemSlot.MainHand, mainHandDelay, "slash");
            mainHand = harness.Equip(player, sword, ItemSlot.MainHand);
        }

        if (equipOffHand ?? offHandDelay is not null)
        {
            var offHand = harness.DefineWeapon(
                "dagger", "a dagger", ItemSlot.OffHand, offHandDelay, "stab");
            harness.Equip(player, offHand, ItemSlot.OffHand);
        }

        var mob = harness.AddMob(
            "rat", West, mobAttacks ?? [], mobHealth, damageMin: mobDamage, damageMax: mobDamage);

        harness.Execute(player, "kill rat");
        harness.Drain(player);
        return new FightFixture(harness, player, mob, mainHand);
    }

    private sealed class FightFixture(
        WorldHarness harness,
        PlayerActor player,
        Mob mob,
        ItemInstance? mainHand)
    {
        private readonly List<(long Pulse, string Text)> _lines = [];

        public WorldHarness Harness => harness;

        public PlayerActor Player => player;

        public Mob Mob => mob;

        public ItemInstance? MainHand => mainHand;

        public string Log => string.Concat(_lines.Select(l => l.Text));

        /// <summary>
        /// Pumps to the given pulse, tagging everything narrated with the pulse it arrived on.
        /// </summary>
        public void Pump(int through)
        {
            while (harness.Clock.CurrentPulse < through)
            {
                var pulse = harness.Clock.CurrentPulse;
                harness.Pump();
                var text = harness.DrainText(player);
                if (!string.IsNullOrEmpty(text))
                {
                    _lines.Add((pulse, text));
                }
            }
        }

        private IReadOnlyList<long> PulsesMatching(string fragment) =>
            [.. _lines
                .Where(l => l.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Pulse)];

        /// <summary>Pulses on which the player landed any blow.</summary>
        public IReadOnlyList<long> PlayerSwingPulses(int through)
        {
            Pump(through);
            // A miss counts: the question is when the swing happened, not whether it connected.
            // Unarmed carries no attack bonus, so its blows genuinely do miss.
            return [.. _lines
                .Where(l => l.Text.Contains("You slash", StringComparison.OrdinalIgnoreCase)
                         || l.Text.Contains("You stab", StringComparison.OrdinalIgnoreCase)
                         || l.Text.Contains("You hit", StringComparison.OrdinalIgnoreCase)
                         || l.Text.Contains("You miss", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Pulse)];
        }

        public IReadOnlyList<long> MainHandSwingPulses(int through)
        {
            Pump(through);
            return PulsesMatching("You slash");
        }

        public IReadOnlyList<long> OffHandSwingPulses(int through)
        {
            Pump(through);
            return PulsesMatching("You stab");
        }

        public IReadOnlyList<long> MobSwingPulses(string thirdPersonVerb) =>
            PulsesMatching($"{thirdPersonVerb} you");
    }
}
