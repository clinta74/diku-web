using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Using one ability on a timer uses all of them (PLAN.md §4.5).
/// </summary>
/// <remarks>
/// The Warden's four maximum-health walls chain into 470 seconds of continuous cover, which no one
/// of them was tuned to give. They now share a timer: using any puts the whole timer down for that
/// ability's own cooldown.
/// </remarks>
public sealed class SharedCooldownTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>A level-50 Warden who knows all four walls.</summary>
    private static (WorldHarness Harness, PlayerActor Actor) Warden()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        foreach (var key in new[]
        {
            "warden.last-stand",
            "warden.ground-and-centre",
            "warden.unbreakable",
            "warden.the-last-wall",
            "warden.kick",
        })
        {
            harness.DefineAbility(key);
        }

        var actor = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 50);

        // Stamina enough for several of these in a row, so nothing is refused for the wrong reason.
        actor.Character.Vitals.StaminaMax = 500;
        actor.Character.Vitals.Stamina = 500;

        // Something to kick. Kick is the ungrouped ability these tests compare against, and it is
        // single-target - without a rat in the room it is refused for having nothing to aim at,
        // which would look exactly like the timer working.
        harness.AddMob("rat", West, health: 10_000);

        return (harness, actor);
    }

    private static string Say(WorldHarness harness, PlayerActor actor, string input)
    {
        harness.Drain(actor);
        harness.Execute(actor, input);

        // One pulse, so an instant ability leaves the cast queue and records its cooldown - which is
        // the difference between "used" and "queued" that this whole feature turns on.
        harness.Pump();
        return harness.DrainText(actor);
    }

    // -----------------------------------------------------------------------
    // The timer
    // -----------------------------------------------------------------------

    /// <summary>The refusal names the ability responsible, because the bar will not.</summary>
    [Fact]
    public void One_wall_puts_the_others_out_of_reach()
    {
        var (harness, actor) = Warden();

        Assert.Contains("You use Unbreakable", Say(harness, actor, "unbreakable"), StringComparison.Ordinal);

        var refused = Say(harness, actor, "ground and centre");

        Assert.Contains("shares a timer with Unbreakable", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("You use Ground and Centre", refused, StringComparison.Ordinal);
    }

    /// <summary>An ungrouped ability is untouched by any of it.</summary>
    [Fact]
    public void An_ability_off_the_timer_is_unaffected()
    {
        var (harness, actor) = Warden();

        Say(harness, actor, "unbreakable");

        Assert.Contains("You use Kick", Say(harness, actor, "kick rat"), StringComparison.Ordinal);
    }

    /// <summary>The whole timer frees together, on the cooldown of whatever locked it.</summary>
    [Fact]
    public void The_timer_frees_when_the_ability_that_locked_it_does()
    {
        var (harness, actor) = Warden();
        var unbreakable = harness.AbilityCache.Get("warden.unbreakable")!;

        Say(harness, actor, "unbreakable");

        harness.Pump((int)unbreakable.CooldownPulses - 2);
        Assert.Contains("shares a timer", Say(harness, actor, "ground and centre"), StringComparison.Ordinal);

        harness.Pump((int)unbreakable.CooldownPulses);
        Assert.Contains("You use Ground and Centre", Say(harness, actor, "ground and centre"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The shorter ability locks the timer only for its own, shorter cooldown — so a timer is not
    /// silently "the longest cooldown on it, always".
    /// </summary>
    [Fact]
    public void The_shorter_ability_locks_the_longer_one_only_for_its_own_cooldown()
    {
        var (harness, actor) = Warden();
        var groundAndCentre = harness.AbilityCache.Get("warden.ground-and-centre")!;
        var unbreakable = harness.AbilityCache.Get("warden.unbreakable")!;

        Assert.True(groundAndCentre.CooldownPulses < unbreakable.CooldownPulses);

        Say(harness, actor, "ground and centre");
        harness.Pump((int)groundAndCentre.CooldownPulses);

        Assert.Contains("You use Unbreakable", Say(harness, actor, "unbreakable"), StringComparison.Ordinal);
    }

    /// <summary>Its own cooldown still refuses it in its own words, naming no one else.</summary>
    [Fact]
    public void An_ability_refused_by_its_own_cooldown_still_says_so_plainly()
    {
        var (harness, actor) = Warden();

        Say(harness, actor, "kick rat");
        var refused = Say(harness, actor, "kick rat");

        Assert.Contains("Kick is on cooldown", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("shares a timer", refused, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // What the player is told
    // -----------------------------------------------------------------------

    /// <summary>
    /// The roster is where a timer is learnable, since the cooling bar never shows a group-mate.
    /// </summary>
    [Fact]
    public void The_abilities_listing_names_the_others_on_a_timer()
    {
        var (harness, actor) = Warden();

        harness.Drain(actor);
        harness.Execute(actor, "abilities");
        var listing = harness.DrainText(actor);

        Assert.Contains("shares a timer with", listing, StringComparison.Ordinal);
        Assert.Contains("Ground and Centre", listing, StringComparison.Ordinal);

        // Kick is on no timer, so it gets no such line. Checked by counting: four walls, four lines.
        Assert.Equal(4, listing.Split("shares a timer with").Length - 1);
    }

    /// <summary>
    /// <b>The cooling bar still lists only what was used.</b> A group-mate held down by something
    /// else must not appear there — the bar is a record of what the player did, and this asymmetry
    /// is the one most likely to be "fixed" by mistake later.
    /// </summary>
    [Fact]
    public void The_roster_reports_own_cooldown_only_for_an_untouched_group_mate()
    {
        var (harness, actor) = Warden();

        Say(harness, actor, "unbreakable");

        var roster = harness.Drain(actor)
            .Where(e => e.Type == EventTypes.Abilities)
            .Select(e => (AbilitiesPayload)e.Payload)
            .LastOrDefault();

        harness.Drain(actor);
        PlayerView.SendAbilities(
            actor, harness.World, harness.AbilityCache, harness.Clock.CurrentPulse);

        roster = harness.Drain(actor)
            .Where(e => e.Type == EventTypes.Abilities)
            .Select(e => (AbilitiesPayload)e.Payload)
            .Last();

        var used = roster.Abilities.Single(a => a.Key == "warden.unbreakable");
        var untouched = roster.Abilities.Single(a => a.Key == "warden.ground-and-centre");

        Assert.True(used.RemainingPulses > 0, "the ability that fired should be cooling");
        Assert.Equal(0, untouched.RemainingPulses);
    }

    // -----------------------------------------------------------------------
    // The queue gate the timer depends on
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The test that ties the queue gate to the timer.</b> A cast is recorded as used when it
    /// resolves, and the loop drains several commands before resolving anything — so without a gate,
    /// two abilities on one timer typed into the same pulse would both find it cold and both land.
    /// </summary>
    [Fact]
    public void Two_abilities_on_one_timer_in_the_same_pulse_do_not_both_land()
    {
        var (harness, actor) = Warden();
        harness.Drain(actor);

        // Both commands drained before a single pulse is pumped, exactly as GameLoop.DrainInbound
        // would hand them over.
        harness.Execute(actor, "unbreakable");
        harness.Execute(actor, "ground and centre");
        harness.Pump();

        var said = harness.DrainText(actor);

        Assert.Contains("You use Unbreakable", said, StringComparison.Ordinal);
        Assert.DoesNotContain("You use Ground and Centre", said, StringComparison.Ordinal);
    }

    /// <summary>The same gate, on an ordinary ability: two of one thing is one of it.</summary>
    [Fact]
    public void Two_commands_for_the_same_ability_in_one_pulse_land_once()
    {
        var (harness, actor) = Warden();
        harness.Drain(actor);

        harness.Execute(actor, "kick rat");
        harness.Execute(actor, "kick rat");
        harness.Pump();

        var said = harness.DrainText(actor);

        Assert.Equal(1, said.Split("You use Kick").Length - 1);
    }

    /// <summary>
    /// The refusal follows the kind of whatever is in flight. A Warden mid-kick is not "casting",
    /// and being told so would teach the wrong vocabulary at the moment they are reading it.
    /// </summary>
    [Fact]
    public void Being_mid_action_is_described_as_the_kind_of_action_it_is()
    {
        var (harness, actor) = Warden();
        harness.Drain(actor);

        harness.Execute(actor, "kick rat");
        harness.Execute(actor, "unbreakable");

        var said = harness.DrainText(actor);

        Assert.Contains("You are still in the middle of Kick.", said, StringComparison.Ordinal);
        Assert.DoesNotContain("casting", said, StringComparison.Ordinal);
    }

    /// <summary>And a real cast bar says "casting", because that is what it is.</summary>
    [Fact]
    public void A_spell_winding_up_is_described_as_a_cast()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("adept.bolt");
        harness.DefineAbility("adept.shield");

        var actor = harness.AddPlayer("Wen", West, path: CharacterPath.Adept, level: 10);
        actor.Character.Vitals.FocusMax = 500;
        actor.Character.Vitals.Focus = 500;

        var rat = harness.AddMob("rat", West);
        harness.Drain(actor);

        // Bolt has a cast time, so it is still in the queue when the second command arrives.
        harness.Execute(actor, $"cast bolt {rat.TemplateName}");
        harness.Execute(actor, "cast arcane shield");

        Assert.Contains(
            "You are already casting Bolt.", harness.DrainText(actor), StringComparison.Ordinal);
    }
}
