using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Abilities;

/// <summary>
/// The ability catalogue, and the properties that keep level-ups meaning something.
/// </summary>
/// <remarks>
/// The seeder and the progression table used to be written out separately and had drifted apart
/// in both directions: four abilities were unlocked at level 6 with no row behind them, so
/// levelling granted something uncastable; and three that were seeded appeared in no progression,
/// so the whole of the buff/debuff feature was unlearnable. Both lists now derive from
/// <see cref="AbilityCatalogue"/>, and these are the guards that keep it honest.
/// </remarks>
public sealed class AbilityCatalogueTests
{
    private static readonly CharacterPath[] Paths =
        [CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Shade, CharacterPath.Channeler];

    /// <summary>The effect executors that exist. An ability naming anything else does nothing.</summary>
    private static readonly HashSet<string> KnownEffects = new(StringComparer.Ordinal)
    {
        "damage.physical",
        "heal.restore",
        "buff.damage-up",
        "debuff.weaken",
    };

    [Fact]
    public void Every_ability_key_is_unique()
    {
        var duplicates = AbilityCatalogue.All
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_ability_the_progression_grants_exists_in_the_catalogue()
    {
        var known = AbilityCatalogue.All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        var dangling = Paths
            .SelectMany(AbilityProgression.GetAbilitiesForPath)
            .Select(x => x.AbilityKey)
            .Where(key => !known.Contains(key))
            .ToList();

        Assert.Empty(dangling);
    }

    [Fact]
    public void Every_ability_in_the_catalogue_is_reachable_by_some_path()
    {
        // The other direction. `warden.battle-fury`, `adept.weaken`, and `shade.fortify` were
        // seeded and unreachable - content that existed only in the database.
        var granted = Paths
            .SelectMany(AbilityProgression.GetAbilitiesForPath)
            .Select(x => x.AbilityKey)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = AbilityCatalogue.All
            .Select(e => e.Key)
            .Where(key => !granted.Contains(key))
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Every_ability_names_an_effect_that_exists()
    {
        // An unknown effect key resolves to nothing and the cast silently does nothing.
        var unknown = AbilityCatalogue.All
            .Where(e => !KnownEffects.Contains(e.EffectKey))
            .Select(e => $"{e.Key} -> {e.EffectKey}")
            .ToList();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// Effects read their parameters by name and skip anything they do not recognise, so a
    /// plausible-but-wrong key produces an ability that costs a resource and does nothing.
    /// </summary>
    [Theory]
    [InlineData("damage.physical", "scalingFactor")]
    [InlineData("heal.restore", "baseHeal")]
    [InlineData("buff.damage-up", "outgoingMultiplier")]
    [InlineData("debuff.weaken", "incomingMultiplier")]
    public void Every_ability_carries_the_parameter_its_effect_reads(string effectKey, string parameter)
    {
        var missing = AbilityCatalogue.All
            .Where(e => e.EffectKey == effectKey && !e.EffectParams.ContainsKey(parameter))
            .Select(e => e.Key)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_path_starts_with_something_castable_at_level_one()
    {
        foreach (var path in Paths)
        {
            Assert.Contains(AbilityCatalogue.For(path), e => e.UnlockLevel == 1);
        }
    }

    /// <summary>
    /// The playtesting complaint in one assertion: progression used to stop at level 6 for every
    /// Path, so every level after it granted nothing at all.
    /// </summary>
    [Fact]
    public void Every_path_keeps_unlocking_abilities_to_level_twenty()
    {
        foreach (var path in Paths)
        {
            var levels = AbilityCatalogue.For(path).Select(e => e.UnlockLevel).ToList();

            Assert.True(levels.Count >= 6, $"{path} has only {levels.Count} abilities.");
            Assert.True(levels.Max() >= 20, $"{path} stops unlocking at {levels.Max()}.");
        }
    }

    [Fact]
    public void No_path_has_two_abilities_at_the_same_level()
    {
        // One reward per level-up reads more clearly than two at once and none for four levels.
        foreach (var path in Paths)
        {
            var levels = AbilityCatalogue.For(path).Select(e => e.UnlockLevel).ToList();
            Assert.Equal(levels.Count, levels.Distinct().Count());
        }
    }

    [Fact]
    public void No_path_goes_more_than_four_levels_without_something_new()
    {
        foreach (var path in Paths)
        {
            var levels = AbilityCatalogue.For(path).Select(e => e.UnlockLevel).Order().ToList();

            for (var i = 1; i < levels.Count; i++)
            {
                var gap = levels[i] - levels[i - 1];
                Assert.True(gap <= 4, $"{path} has a {gap}-level gap after {levels[i - 1]}.");
            }
        }
    }

    [Fact]
    public void An_ability_key_is_prefixed_with_the_path_that_learns_it()
    {
        foreach (var entry in AbilityCatalogue.All)
        {
            var prefix = entry.Path.ToString().ToLowerInvariant() + ".";
            Assert.StartsWith(prefix, entry.Key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_character_knows_only_what_their_level_has_reached()
    {
        var atOne = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 1);
        var atSeven = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 7);

        Assert.Contains("warden.slash", atOne);
        Assert.DoesNotContain("warden.parry", atOne);
        Assert.Contains("warden.parry", atSeven);
    }

    [Fact]
    public void A_path_does_not_learn_another_paths_abilities()
    {
        var warden = AbilityProgression.GetKnownAbilitiesForLevel(CharacterPath.Warden, 20);

        Assert.DoesNotContain("adept.bolt", warden);
        Assert.DoesNotContain("shade.strike", warden);
    }

    [Fact]
    public void Every_ability_costs_something()
    {
        // A free ability with a cooldown is a worse version of an auto-attack.
        Assert.All(AbilityCatalogue.All, e => Assert.True(e.CostValue > 0, $"{e.Key} is free."));
    }
}
