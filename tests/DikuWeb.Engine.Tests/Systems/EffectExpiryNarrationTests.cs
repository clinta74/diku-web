using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// An effect ending is told to whoever was carrying it, when it actually ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported from play: "abilities show the wear off message from a mob even after the combat is
/// over", with Sunder as the example.</b> Two separate faults produced that one line.
/// </para>
/// <para>
/// <b>It went to the caster.</b> <c>ExpireEffects</c> returned bare effects, and the only entity id
/// on one is <c>SourceEntityId</c> — whoever cast it. The expiry system read that as the bearer,
/// which is true of a self-buff and false of every debuff, so a Warden landing Sunder on a mob was
/// told "Your sundered fades" about an effect that had never been on them.
/// </para>
/// <para>
/// <b>And it went late.</b> Expiry ran on the sixty-second regen tick while Sunder lasts eighty
/// pulses — twenty seconds — so the line arrived up to a minute after the effect ended, by which
/// time the mob was dead and the message had nothing to attach to. The mechanics were never late:
/// combat re-reads <c>ExpiresAtPulse</c> every pulse and stops applying an expired effect on time.
/// </para>
/// </remarks>
public sealed class EffectExpiryNarrationTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static ActiveEffect Sunder(Guid caster, long expiresAt) => new()
    {
        EffectKey = "debuff.weaken",
        Name = "sundered",
        SourceEntityId = $"c_{caster:N}",
        IncomingDamageMultiplier = 1.3m,
        ExpiresAtPulse = expiresAt,
        Stacks = 1,
        MaxStacks = 1,
        StackingRule = EffectStackingRule.Refresh,
    };

    /// <summary>The reported bug, stated: a debuff on a mob says nothing to the player who cast it.</summary>
    [Fact]
    public void A_debuff_expiring_on_a_mob_says_nothing_to_the_caster()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", Room);
        var rat = harness.AddMob("rat", Room);

        harness.World.ApplyEffect(rat.Id, Sunder(kael.CharacterId, harness.Clock.CurrentPulse + 4));
        harness.Drain(kael);

        harness.Pump(6);

        Assert.DoesNotContain("sundered", harness.DrainText(kael), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.World.GetActiveEffects(rat.Id));
    }

    /// <summary>What the player carries themselves, they are told about.</summary>
    [Fact]
    public void An_effect_expiring_on_the_player_is_narrated_to_them()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", Room);

        harness.World.ApplyEffect(kael.CharacterId, Sunder(kael.CharacterId, harness.Clock.CurrentPulse + 4));
        harness.Drain(kael);

        harness.Pump(6);

        Assert.Contains("no longer sundered", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// And it arrives when the effect ends, rather than on the next minute.
    /// </summary>
    /// <remarks>
    /// The half of the report that made the first half visible. Announced on the regen tick, a
    /// twenty-second debuff could be reported up to a minute late — long after the fight, which is
    /// exactly when a player notices a line they cannot account for.
    /// </remarks>
    [Fact]
    public void The_line_arrives_on_the_pulse_the_effect_ends()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", Room);

        harness.World.ApplyEffect(kael.CharacterId, Sunder(kael.CharacterId, harness.Clock.CurrentPulse + 4));
        harness.Drain(kael);

        // A pulse ticks its systems and then advances, so the four pulses before the expiry one
        // are silent and the fifth tick is the first to see it due.
        harness.Pump(4);
        Assert.DoesNotContain("no longer", harness.DrainText(kael), StringComparison.Ordinal);

        harness.Pump(1);
        Assert.Contains("no longer sundered", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// A mob's effects die with it, rather than waiting for a sweep to find them.
    /// </summary>
    /// <remarks>
    /// Not the reported symptom — with the bearer travelling alongside the effect nothing would be
    /// narrated anyway — but it is the state behind it: a corpse's wounds and debuffs sat in the
    /// table until the next expiry pass, belonging to something that no longer existed.
    /// </remarks>
    [Fact]
    public void A_mob_takes_its_effects_with_it_when_it_dies()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", Room);
        var rat = harness.AddMob("rat", Room);

        harness.World.ApplyEffect(rat.Id, Sunder(kael.CharacterId, harness.Clock.CurrentPulse + 400));
        Assert.Single(harness.World.GetActiveEffects(rat.Id));

        harness.World.RemoveMob(rat);

        Assert.Empty(harness.World.GetActiveEffects(rat.Id));
    }

    /// <summary>An empty table is the common case, and the sweep over it costs nothing.</summary>
    [Fact]
    public void A_world_with_nothing_running_expires_nothing()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddPlayer("Kael", Room);

        EffectExpirySystem.Tick(harness.World, harness.Clock.CurrentPulse);

        Assert.Empty(harness.World.ExpireEffects(harness.Clock.CurrentPulse));
    }
}
