using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// Stun — the sixth effect, and the first that takes a *turn* away rather than changing a number.
/// </summary>
/// <remarks>
/// "Cannot act" has to hold in three separate places: the combat loop that swings, the cast
/// command that starts spells, and the mob AI that emotes, wanders, and picks fights. Each one
/// asks independently, so each one is worth a test — a stun that stopped swings but let the
/// target stroll out of the room would read as having done nothing.
/// </remarks>
public sealed class StunTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static void Stun(WorldHarness harness, Guid entityId, long duration = 12)
    {
        harness.World.ApplyEffect(entityId, new ActiveEffect
        {
            EffectKey = "control.stun",
            Name = "reeling",
            SourceEntityId = "c_test",
            PreventsActing = true,
            ExpiresAtPulse = harness.Clock.CurrentPulse + duration,
        });
    }

    // -----------------------------------------------------------------------
    // Swings
    // -----------------------------------------------------------------------

    [Fact]
    public void A_stunned_mob_does_not_swing()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West, level: 1);
        var rat = harness.AddMob(
            "rat", West, attacks: [new MobAttack { Verb = "bite", DelayPulses = 4 }],
            health: 100_000, damageMin: 5, damageMax: 5);

        harness.Execute(player, "kill rat");
        harness.Drain(player);

        Stun(harness, rat.Id, duration: 40);
        var before = player.Character.Vitals.Health;

        harness.Pump(30);

        Assert.Equal(before, player.Character.Vitals.Health);
    }

    [Fact]
    public void The_mob_swings_again_once_the_stun_runs_out()
    {
        // A stun that never ended would be a removal rather than a tempo tool.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West, level: 1);
        var rat = harness.AddMob(
            "rat", West, attacks: [new MobAttack { Verb = "bite", DelayPulses = 4 }],
            health: 100_000, damageMin: 5, damageMax: 5);

        harness.Execute(player, "kill rat");
        harness.Drain(player);

        Stun(harness, rat.Id, duration: 8);
        harness.Pump(8);
        var afterStun = player.Character.Vitals.Health;

        harness.Pump(30);

        Assert.True(player.Character.Vitals.Health < afterStun, "The rat should be biting again.");
    }

    [Fact]
    public void A_stunned_player_does_not_swing()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West, level: 1);
        var sword = harness.DefineWeapon("sword", "a sword", Domain.Items.ItemSlot.MainHand, 4, "slash");
        harness.Equip(player, sword, Domain.Items.ItemSlot.MainHand);
        var rat = harness.AddMob("rat", West, health: 100_000);

        harness.Execute(player, "kill rat");
        harness.Drain(player);

        Stun(harness, player.CharacterId, duration: 40);
        var before = rat.Vitals.Health;

        harness.Pump(30);

        Assert.Equal(before, rat.Vitals.Health);
    }

    // -----------------------------------------------------------------------
    // Casting
    // -----------------------------------------------------------------------

    [Fact]
    public void A_stunned_character_cannot_start_a_cast()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("adept.bolt");

        var caster = harness.AddPlayer("Mira", West, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.Focus = 100;
        var rat = harness.AddMob("rat", West, health: 500);

        Stun(harness, caster.CharacterId, duration: 40);
        harness.Drain(caster);

        harness.Execute(caster, "cast bolt rat");
        harness.Pump(20);

        Assert.Contains("cannot gather yourself", harness.DrainText(caster), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100, caster.Character.Vitals.Focus);
    }

    [Fact]
    public void A_stun_breaks_a_cast_already_in_progress()
    {
        // The interrupt half, and most of why a stun is worth pressing. Bolt is an eight-pulse
        // cast, so there is a real window to break.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("adept.bolt");
        harness.DefineAbility("warden.shield-bash");

        var caster = harness.AddPlayer("Mira", West, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.Focus = 100;
        var rat = harness.AddMob("rat", West, health: 500);
        var before = rat.Vitals.Health;

        harness.Execute(caster, "cast bolt rat");
        Assert.NotEmpty(harness.World.CastQueue.Pending);

        // Stunned mid-cast, before the eight pulses are up.
        Stun(harness, caster.CharacterId, duration: 24);
        harness.Pump(20);

        Assert.Empty(harness.World.CastQueue.Pending);
        Assert.Equal(before, rat.Vitals.Health);
    }

    // -----------------------------------------------------------------------
    // The AI
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_stunned_mob_does_not_wander_off()
    {
        // The gate the combat loop does not cover. Wandering away mid-stun would read as the
        // stun having done nothing at all.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var mob = harness.AddMob("rat", West, name: "rat");
        Stun(harness, mob.Id, duration: 400);

        var ai = new Engine.Inhabitants.MobAiSystem(
            new StubMobTemplates(harness.MobTemplates),
            new Domain.Randomness.SeededRandomSource(1),
            harness.Clock,
            harness.View);

        for (var i = 0; i < 50; i++)
        {
            await ai.RunAsync(harness.World, CancellationToken.None);
            harness.Clock.AdvancePulses(1);
        }

        Assert.Equal(West.ToString(), mob.RoomKey);
    }

    /// <summary>Serves the harness's in-memory templates through the repository interface.</summary>
    private sealed class StubMobTemplates(Engine.Inhabitants.MobTemplateCache cache)
        : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(cache.Get(key));

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([]);
    }

    // -----------------------------------------------------------------------
    // Bounds
    // -----------------------------------------------------------------------

    [Fact]
    public void An_over_long_stun_is_clamped_rather_than_honoured()
    {
        // Authored content, so a typo that adds a zero must not be an opponent removed from the
        // game. StunEffect clamps; this pins that it does.
        var effect = new StunEffect();
        var caster = WorldHarness.NewCharacter("Theron", West);

        var active = effect.CreateActiveEffect(
            caster,
            caster,
            new Dictionary<string, string> { ["durationPulses"] = "9999" },
            currentPulse: 0);

        Assert.Equal(StunEffect.MaxDurationPulses, active.ExpiresAtPulse);
    }

    [Fact]
    public void A_stun_never_stacks()
    {
        // Chaining stuns into a permanent lock is the failure mode this guards.
        var effect = new StunEffect();
        var caster = WorldHarness.NewCharacter("Theron", West);

        var active = effect.CreateActiveEffect(caster, caster, [], currentPulse: 0);

        Assert.Equal(1, active.MaxStacks);
        Assert.Equal(EffectStackingRule.Refresh, active.StackingRule);
    }

    [Fact]
    public void An_expired_stun_no_longer_prevents_acting()
    {
        // Expiry is checked when asked, not left to the 60s sweep - a stun measured in a couple
        // of swings would otherwise outlive itself by most of a minute.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Theron", West);

        Stun(harness, player.CharacterId, duration: 4);

        Assert.True(harness.World.IsStunned(player.CharacterId, 0));
        Assert.False(harness.World.IsStunned(player.CharacterId, 4));
    }
}
