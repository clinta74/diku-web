using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Abilities;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Every ability in the catalogue can actually be used, by the words a player would type.
/// </summary>
/// <remarks>
/// From playtesting: <em>"warden kick ability cannot seem to be used. check all abilities and how
/// a player would use them."</em>
///
/// The individual ability tests elsewhere each pick one ability and drive it, which is how a whole
/// class of abilities stayed unusable without any test noticing — nothing walked the catalogue and
/// asked whether each entry could be reached from a keyboard. These do.
/// </remarks>
public sealed class AbilityUsabilityTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>
    /// A character of the right Path and level, with the resources to pay for anything.
    /// </summary>
    /// <remarks>
    /// Maxima are raised rather than left at the Path's defaults: the harness does not recompute
    /// them when it sets a level, and the point here is whether the ability can be <em>named</em>,
    /// not whether a level 20 has the stamina for Last Stand.
    /// </remarks>
    private static (WorldHarness Harness, PlayerActor Actor) CasterFor(AbilityCatalogue.Entry entry)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var actor = harness.AddPlayer("Caster", West, path: entry.Path, level: entry.UnlockLevel);
        var vitals = actor.Character.Vitals;

        vitals.HealthMax = 5000;
        vitals.Health = 5000;
        vitals.FocusMax = 5000;
        vitals.Focus = 5000;
        vitals.StaminaMax = 5000;
        vitals.Stamina = 5000;

        harness.DefineAbility(entry.Key);
        harness.AddMob("rat", West, health: 500, name: "a rat");

        return (harness, actor);
    }

    public static TheoryData<string> EveryAbility()
    {
        var data = new TheoryData<string>();

        foreach (var entry in AbilityCatalogue.All)
        {
            data.Add(entry.Key);
        }

        return data;
    }

    private static AbilityCatalogue.Entry EntryFor(string key) =>
        AbilityCatalogue.All.Single(e => e.Key == key);

    [Theory]
    [MemberData(nameof(EveryAbility))]
    public void Every_ability_can_be_cast_by_its_display_name(string key)
    {
        // The name is what `abilities` prints and what the builder shows, so it is the string a
        // player will type. Keys are an implementation detail nobody should have to learn.
        var entry = EntryFor(key);
        var (harness, actor) = CasterFor(entry);

        harness.Execute(actor, $"cast {entry.Name} rat");

        var text = harness.DrainText(actor);

        Assert.DoesNotContain("You don't know an ability", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not configured", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(EveryAbility))]
    public void Every_ability_can_be_used_as_a_verb_of_its_own(string key)
    {
        // How a Diku player expects a skill to work: `kick rat`, not `cast kick rat`. The verb
        // table is checked first, so this can never shadow an existing command.
        var entry = EntryFor(key);
        var (harness, actor) = CasterFor(entry);

        harness.Execute(actor, $"{entry.Name} rat");

        var text = harness.DrainText(actor);

        Assert.DoesNotContain("is not something you can do", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("You don't know an ability", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_warden_opener_works_by_name_the_way_it_was_reported()
    {
        // The exact case from playtesting, spelled out rather than left to the theory above.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 1);
        harness.DefineAbility("warden.kick");
        var rat = harness.AddMob("rat", West, health: 100, name: "a rat");

        harness.Execute(warden, "kick rat");
        harness.Pump(20);

        Assert.True(rat.Vitals.Health < 100, "Kick should have landed.");
    }

    [Fact]
    public void A_two_word_ability_is_not_read_as_an_ability_and_a_target()
    {
        // `cast shield bash rat` used to take the first word as the ability and the second as the
        // target, so every multi-word ability in the catalogue was unreachable by name.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 9);
        warden.Character.Vitals.StaminaMax = 500;
        warden.Character.Vitals.Stamina = 500;

        harness.DefineAbility("warden.shield-bash");
        var rat = harness.AddMob("rat", West, health: 200, name: "a rat");

        harness.Execute(warden, "shield bash rat");

        // Four pulses, not twenty. Shield Bash stuns for sixteen, and the harness expires effects
        // now that GameLoop does it every pulse - so pumping past the duration and then looking
        // for the stun asked whether it had ever landed by checking after it was over.
        harness.Pump(4);

        // Shield Bash is a stun, not damage, so the effect on the rat is what landing looks like.
        Assert.NotEmpty(harness.World.GetActiveEffects(rat.Id));
    }

    [Fact]
    public void Casting_a_skill_is_refused_and_says_what_to_type_instead()
    {
        // "cast kick" reads wrong because it is wrong. The refusal is the teaching, so it names
        // the verb form rather than just objecting - including for a two-word skill, which is
        // also the case that used to be unparseable.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 9);
        warden.Character.Vitals.StaminaMax = 500;
        warden.Character.Vitals.Stamina = 500;

        harness.DefineAbility("warden.shield-bash");
        harness.AddMob("rat", West, health: 200, name: "a rat");

        harness.Execute(warden, "cast shield bash rat");

        var text = harness.DrainText(warden);

        Assert.Contains("is a skill, not a spell", text, StringComparison.Ordinal);
        Assert.Contains("'shield bash rat'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Casting_a_spell_is_exactly_right()
    {
        // The other half: `cast` is for spells, and an Adept casting one is told nothing about
        // vocabulary because there is nothing wrong with what they typed.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 1);
        adept.Character.Vitals.FocusMax = 500;
        adept.Character.Vitals.Focus = 500;

        harness.DefineAbility("adept.bolt");
        var rat = harness.AddMob("rat", West, health: 200, name: "a rat");

        harness.Execute(adept, "cast bolt rat");
        harness.Pump(20);

        Assert.True(rat.Vitals.Health < 200, "Bolt should have landed.");
    }

    // -----------------------------------------------------------------------
    // Saying what it did
    // -----------------------------------------------------------------------

    [Fact]
    public void A_damaging_ability_names_its_target_and_its_number()
    {
        // From playtesting: "Alloran's Kick takes effect!" and nothing else — no target, no
        // amount, no way to tell a hit from a miss or an area effect that caught four things
        // from one that caught one.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 1);
        harness.DefineAbility("warden.kick");
        harness.AddMob("rat", West, health: 200, name: "a rat");

        harness.Execute(warden, "kick rat");
        harness.Pump(20);

        var text = harness.DrainText(warden);

        Assert.Contains("Your Kick hits a rat for", text, StringComparison.Ordinal);
        Assert.DoesNotContain("takes effect", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_room_sees_what_landed_on_whom()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 1);
        var watcher = harness.AddPlayer("Kael", West);
        harness.DefineAbility("warden.kick");
        harness.AddMob("rat", West, health: 200, name: "a rat");

        harness.Execute(warden, "kick rat");
        harness.Pump(20);
        harness.Drain(warden);

        Assert.Contains(
            "Bram's Kick hits a rat for",
            harness.DrainText(watcher),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_heal_reports_what_it_restored()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var hallow = harness.AddPlayer("Bram", West, path: CharacterPath.Hallow, level: 1);
        hallow.Character.Vitals.FocusMax = 500;
        hallow.Character.Vitals.Focus = 500;
        hallow.Character.Vitals.Health = 5;

        harness.DefineAbility("hallow.mend");

        harness.Execute(hallow, "cast mend");
        harness.Pump(20);

        Assert.Contains("restores", harness.DrainText(hallow), StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_ability_says_what_it_left_behind()
    {
        // Nothing moves a health bar here, and reporting that as nothing would make half the
        // catalogue look broken. The effect's own name is what tells a stun from a snare.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 9);
        warden.Character.Vitals.StaminaMax = 500;
        warden.Character.Vitals.Stamina = 500;

        harness.DefineAbility("warden.shield-bash");
        harness.AddMob("rat", West, health: 200, name: "a rat");

        harness.Execute(warden, "shield bash rat");
        harness.Pump(20);

        Assert.Contains(
            "leaves a rat reeling",
            harness.DrainText(warden),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_area_effect_reports_every_target_it_caught()
    {
        // The line per target is the point: one line for the whole cast could not distinguish a
        // Firestorm that caught the room from one that caught a single rat.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 18);
        adept.Character.Vitals.FocusMax = 500;
        adept.Character.Vitals.Focus = 500;

        harness.DefineAbility("adept.firestorm");
        harness.AddMob("rat", West, health: 300, name: "a rat");
        harness.AddMob("wolf", West, health: 300, name: "a wolf");

        harness.Execute(adept, "cast firestorm");
        harness.Pump(24);

        var text = harness.DrainText(adept);

        Assert.Contains("a rat for", text, StringComparison.Ordinal);
        Assert.Contains("a wolf for", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ability_another_path_owns_is_not_a_verb_for_you()
    {
        // The fallback resolves against what this character has learned, so a Warden typing
        // `firestorm` gets the unknown-verb message: for them it genuinely is not a verb. Naming
        // it through `cast` is where "you don't know that" belongs, and the next test covers it.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 20);
        harness.DefineAbility("adept.firestorm");

        Assert.Null(harness.Commands.FindAbilityVerb(warden.Character, "firestorm", "rat"));
    }

    [Fact]
    public void Casting_an_ability_another_path_owns_says_you_do_not_know_it()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 20);
        harness.DefineAbility("adept.firestorm");
        harness.AddMob("rat", West, health: 100, name: "a rat");

        harness.Execute(warden, "cast firestorm rat");

        Assert.Contains("don't know", harness.DrainText(warden), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_real_typo_is_still_a_typo()
    {
        // Asserted at the resolver rather than through the harness, because "no ability answered"
        // is the fact that matters: the loop turns it into the unknown-verb message, and a test
        // that went through the harness would be asserting the harness's own throw instead.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 20);
        harness.DefineAbility("warden.kick");

        Assert.Null(harness.Commands.FindAbilityVerb(warden.Character, "flurgle", "rat"));
    }

    [Fact]
    public void An_ability_you_know_resolves_to_the_cast_handler()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 20);
        harness.DefineAbility("warden.kick");

        var resolved = harness.Commands.FindAbilityVerb(warden.Character, "kick", "rat");

        Assert.NotNull(resolved);
        Assert.Equal("cast", resolved.Value.Definition.Name);
        Assert.Equal("kick rat", resolved.Value.Argument);
    }

    [Fact]
    public void An_existing_verb_always_wins()
    {
        // The verb table is checked first, so adding abilities as verbs can never take a command
        // out from under someone. `rest` is a verb; nothing about it changes.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 20);

        harness.Execute(warden, "rest");

        Assert.Equal(CharacterRestState.Rest, warden.Character.RestState);
    }

    /// <summary>
    /// A single letter is not an ability.
    /// </summary>
    /// <remarks>
    /// From playtesting: <em>"mass provocation triggers on just an m."</em> A verb the table misses
    /// is tried as an ability, and the resolver's fuzzy pass ranked prefixes with no floor under
    /// them — so one stray keystroke fired a level 46 ability and spent its cooldown. Every verb in
    /// the table carries a <c>MinLength</c> against precisely this; abilities came in through
    /// another door and had none.
    /// </remarks>
    [Theory]
    [InlineData("m")]
    [InlineData("ma")]
    public void A_letter_or_two_does_not_reach_an_ability(string typed)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 50);
        harness.DefineAbility("warden.mass-provocation");

        Assert.Null(harness.Commands.FindAbilityVerb(warden.Character, typed, "rat"));
    }

    /// <summary>And three letters still does, so the floor is a floor and not a wall.</summary>
    [Fact]
    public void Three_letters_still_reaches_it()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var warden = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 50);
        harness.DefineAbility("warden.mass-provocation");

        Assert.NotNull(harness.Commands.FindAbilityVerb(warden.Character, "mas", "rat"));
    }

    /// <summary>
    /// The shortest name in the catalogue is still typeable in full.
    /// </summary>
    /// <remarks>
    /// Sap is three letters, which is why the floor is three. Asserted against the shipped ability
    /// rather than a stand-in, so shipping a shorter name than the rule can spell fails here.
    /// </remarks>
    [Fact]
    public void The_shortest_ability_is_still_reachable_by_name()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var hallow = harness.AddPlayer("Ivy", West, path: CharacterPath.Hallow, level: 20);
        var sap = harness.DefineAbility("hallow.sap");

        Assert.True(
            sap.Name.Length >= AbilityLookup.MinimumAbbreviation,
            $"'{sap.Name}' is shorter than the abbreviation floor and could not be typed in full.");

        Assert.NotNull(harness.Commands.FindAbilityVerb(hallow.Character, sap.Name.ToLowerInvariant(), "rat"));
    }

    /// <summary>
    /// No shipped ability is shorter than the floor.
    /// </summary>
    /// <remarks>
    /// The rule the previous test states about Sap, asserted over the whole catalogue — because the
    /// ability that breaks it is the one somebody adds later, and the floor silently making it
    /// untypeable is the failure this file exists to catch.
    /// </remarks>
    [Fact]
    public void Every_ability_can_be_typed_in_full()
    {
        var tooShort = ShippedAbilities.All
            .Where(a => a.Name.Trim().Length < AbilityLookup.MinimumAbbreviation)
            .Select(a => $"{a.Key} is named '{a.Name}'")
            .ToList();

        Assert.Empty(tooShort);
    }
}
