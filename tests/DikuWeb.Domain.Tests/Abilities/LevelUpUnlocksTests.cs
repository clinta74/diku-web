using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Abilities;

/// <summary>
/// What a level-up says it granted.
/// </summary>
/// <remarks>
/// The behaviour worth pinning is the range, not the sentence: a single award can carry a
/// character across several levels at once, and everything unlocked on the way has to be named.
/// That is the case a "what did I just get" message written against one level silently drops.
/// </remarks>
public sealed class LevelUpUnlocksTests
{
    private static Ability Ability(
        string key,
        CharacterPath path,
        int unlockLevel,
        string name,
        CostType cost = CostType.Stamina) => new()
        {
            Key = key,
            Path = path,
            UnlockLevel = unlockLevel,
            Name = name,
            Description = name,
            CostType = cost,
            CostValue = 10,
            CooldownPulses = 24,
            TargetingType = TargetingType.SingleTarget,
            Effects = [],
        };

    private static readonly Ability[] Table =
    [
        Ability("adept.bolt", CharacterPath.Adept, 1, "Bolt", CostType.Focus),
        Ability("adept.ember", CharacterPath.Adept, 6, "Ember", CostType.Focus),
        Ability("adept.rift", CharacterPath.Adept, 9, "Rift", CostType.Focus),
        Ability("warden.bash", CharacterPath.Warden, 6, "Bash"),
    ];

    [Fact]
    public void Nothing_is_said_when_the_levels_granted_nothing()
    {
        // Most level-ups. The footer must not arrive on its own - a line telling you to go look at
        // a list that has not changed is noise attached to every single level.
        Assert.Empty(LevelUpUnlocks.Announce(Table, CharacterPath.Adept, 6, 8));
    }

    [Fact]
    public void A_new_ability_is_named_with_the_words_to_type()
    {
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Adept, 5, 6);

        Assert.Equal("Level 6 — Ember, a new spell. Type 'cast ember'.", lines[0]);
    }

    [Fact]
    public void A_skill_is_typed_without_cast()
    {
        // `cast` refuses a skill, so a message that taught "cast bash" would teach a refusal.
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Warden, 5, 6);

        Assert.Contains("Type 'bash'.", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Every_level_crossed_is_announced_not_only_the_last()
    {
        // The reason this takes a range. A quest paying a whole band drops a character from 5 to 9
        // in one step, and TryLevelUp jumps straight there - so an announcement written against
        // the level landed on would never mention Ember at all.
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Adept, 5, 9);

        Assert.Contains(lines, line => line.Contains("Ember", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Rift", StringComparison.Ordinal));
    }

    [Fact]
    public void Unlocks_are_listed_in_the_order_they_were_earned()
    {
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Adept, 0, 9);

        var levels = lines
            .Where(line => line.StartsWith("Level ", StringComparison.Ordinal))
            .Select(line => int.Parse(line.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal([.. levels.Order()], levels);
    }

    [Fact]
    public void Another_paths_abilities_are_not_announced()
    {
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Adept, 5, 6);

        Assert.DoesNotContain(lines, line => line.Contains("Bash", StringComparison.Ordinal));
    }

    [Fact]
    public void Passives_are_announced_alongside_abilities()
    {
        // Passives live in code and abilities live in the table; a player does not know that and
        // should not be able to tell. Warden learns Parry at 4 and Dual Wield at 5.
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Warden, 3, 5);

        Assert.Contains(lines, line => line.Contains("Parry", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Dual Wield", StringComparison.Ordinal));
    }

    [Fact]
    public void A_passive_is_described_rather_than_given_a_verb()
    {
        // There is nothing to type. Offering a word here would put it in front of a parser that
        // can never resolve it.
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Warden, 3, 4);

        Assert.Contains("turn aside a blow", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Type 'parry'", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_roster_is_pointed_at_once_however_many_were_learned()
    {
        // Cooldowns and costs live there; this message carries neither, and now that the cooldown
        // bar shows only what is cooling, `abilities` is the only place left to read them.
        var lines = LevelUpUnlocks.Announce(Table, CharacterPath.Warden, 3, 6);

        Assert.Single(lines, line => line.Contains("'abilities'", StringComparison.Ordinal));
        Assert.EndsWith("Type 'abilities' to see everything you know.", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_with_no_ability_table_still_announces_passives()
    {
        // A cache that never loaded is a test harness rather than a game, and answering nothing at
        // all would make a Warden's Parry depend on whether abilities had been loaded.
        var lines = LevelUpUnlocks.Announce([], CharacterPath.Warden, 3, 4);

        Assert.Contains(lines, line => line.Contains("Parry", StringComparison.Ordinal));
    }

    [Fact]
    public void Losing_a_level_announces_nothing()
    {
        // Dying costs XP but never a level today. If that ever changes, this must not read the
        // range backwards and re-announce everything between.
        Assert.Empty(LevelUpUnlocks.Announce(Table, CharacterPath.Adept, 9, 5));
    }
}
