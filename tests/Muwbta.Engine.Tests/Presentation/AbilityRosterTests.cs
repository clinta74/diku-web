using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Presentation;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Presentation;

/// <summary>
/// The two frames the cooldown display runs on (PLAN.md §3.5): the roster, and the one event a
/// cast emits.
/// </summary>
/// <remarks>
/// The roster is the half without which the other says nothing - the client had never been told
/// what abilities a character has, so an event naming <c>warden.kick</c> would have arrived at a
/// screen with nothing to grey out.
/// </remarks>
public sealed class AbilityRosterTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static AbilitiesPayload RosterOf(WorldHarness harness, PlayerActor actor)
    {
        PlayerView.SendAbilities(actor, harness.World, harness.AbilityCache, harness.Clock.CurrentPulse);

        return harness.Drain(actor)
            .Where(e => e.Type == EventTypes.Abilities)
            .Select(e => (AbilitiesPayload)e.Payload)
            .Last();
    }

    [Fact]
    public void The_roster_carries_only_what_this_path_has_reached()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.kick");     // level 1
        harness.DefineAbility("warden.bash");     // level 3
        harness.DefineAbility("adept.bolt");      // another Path entirely

        var actor = harness.AddPlayer("Kael", West, path: CharacterPath.Warden, level: 1);

        var keys = RosterOf(harness, actor).Abilities.Select(a => a.Key).ToList();

        Assert.Contains("warden.kick", keys);
        Assert.DoesNotContain("warden.bash", keys);
        Assert.DoesNotContain("adept.bolt", keys);
    }

    [Fact]
    public void An_ability_that_has_not_been_used_is_ready()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.kick");

        var actor = harness.AddPlayer("Kael", West, path: CharacterPath.Warden, level: 1);

        var entry = Assert.Single(RosterOf(harness, actor).Abilities);

        Assert.Equal(0, entry.RemainingPulses);
    }

    [Fact]
    public void The_roster_reports_what_is_left_of_a_running_cooldown()
    {
        // The property that makes a reconnect correct. The client counts down locally, so one that
        // has been away has missed every cooldown event that fired meanwhile - and may replay a
        // stale one out of the ring buffer. Sending the remainder with the roster, and the roster
        // on entry, makes resynchronising a property of reconnecting rather than a thing to
        // remember.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var ability = harness.DefineAbility("warden.kick");

        var actor = harness.AddPlayer("Kael", West, path: CharacterPath.Warden, level: 1);

        harness.World.SetAbilityCooldown(actor.Character.Id, ability.Key, harness.Clock.CurrentPulse);
        harness.Clock.AdvancePulses(4);

        var entry = Assert.Single(RosterOf(harness, actor).Abilities);

        Assert.Equal(ability.CooldownPulses - 4, entry.RemainingPulses);
    }

    [Fact]
    public void A_cooldown_that_has_expired_reads_as_ready_rather_than_negative()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var ability = harness.DefineAbility("warden.kick");

        var actor = harness.AddPlayer("Kael", West, path: CharacterPath.Warden, level: 1);

        harness.World.SetAbilityCooldown(actor.Character.Id, ability.Key, harness.Clock.CurrentPulse);
        harness.Clock.AdvancePulses((int)ability.CooldownPulses + 20);

        var entry = Assert.Single(RosterOf(harness, actor).Abilities);

        Assert.Equal(0, entry.RemainingPulses);
    }

    [Fact]
    public void A_spell_is_named_with_cast_and_a_skill_is_not()
    {
        // The verb is what a player types, and `cast` refuses a skill (§4.7) - so a panel that
        // showed the bare name for both would teach one of them wrong.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.kick");
        harness.DefineAbility("adept.bolt");

        var warden = harness.AddPlayer("Kael", West, path: CharacterPath.Warden, level: 1);

        var adept = harness.AddPlayer("Mira", West, path: CharacterPath.Adept, level: 1);

        var skill = Assert.Single(RosterOf(harness, warden).Abilities);
        var spell = Assert.Single(RosterOf(harness, adept).Abilities);

        Assert.Equal("kick", skill.Verb);
        Assert.False(skill.IsSpell);
        Assert.Equal("cast bolt", spell.Verb);
        Assert.True(spell.IsSpell);
    }

    [Fact]
    public void Levelling_resends_the_roster_without_a_relog()
    {
        // Comparing the level once per pulse rather than pushing from each of the three places a
        // character can level - a kill, a quest turn-in, an admin `set`. The same argument
        // SendVitalsIfChanged already makes: no mutation site can forget to announce itself.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("warden.kick");
        harness.DefineAbility("warden.bash");

        var actor = harness.AddPlayer("Kael", West, path: CharacterPath.Warden, level: 1);

        PlayerView.SendAbilitiesIfLevelled(actor, harness.World, harness.AbilityCache, harness.Clock.CurrentPulse);
        harness.Drain(actor);

        // Nothing changed, so nothing is sent.
        PlayerView.SendAbilitiesIfLevelled(actor, harness.World, harness.AbilityCache, harness.Clock.CurrentPulse);
        Assert.DoesNotContain(harness.Drain(actor), e => e.Type == EventTypes.Abilities);

        actor.Character.Level = 3;
        PlayerView.SendAbilitiesIfLevelled(actor, harness.World, harness.AbilityCache, harness.Clock.CurrentPulse);

        var resent = harness.Drain(actor)
            .Where(e => e.Type == EventTypes.Abilities)
            .Select(e => (AbilitiesPayload)e.Payload)
            .Last();

        Assert.Contains(resent.Abilities, a => a.Key == "warden.bash");
    }
}
