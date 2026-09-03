using Muwbta.Domain.Combat;
using Muwbta.Domain.Narration;
using Muwbta.Domain.Randomness;

namespace Muwbta.Domain.Tests.Combat;

/// <summary>
/// A weapon names its own blow. The verb is authored in base form and conjugated for third
/// person, so one field serves "You slash a rat" and "A rat slashes you".
/// </summary>
public sealed class AttackVerbTests
{
    [Theory]
    [InlineData("slash", "slashes")]
    [InlineData("crush", "crushes")]
    [InlineData("bite", "bites")]
    [InlineData("stab", "stabs")]
    [InlineData("gore", "gores")]
    [InlineData("hit", "hits")]
    [InlineData("parry", "parries")]
    [InlineData("slay", "slays")]
    [InlineData("box", "boxes")]
    [InlineData("buzz", "buzzes")]
    [InlineData("go", "goes")]
    // Only the head verb inflects, so a phrase stays readable.
    [InlineData("chops at", "chopses at")]
    [InlineData("chop at", "chops at")]
    // Silence narrates exactly as the game did before weapons had verbs.
    [InlineData("", "hits")]
    [InlineData("   ", "hits")]
    [InlineData(null, "hits")]
    public void ThirdPerson_conjugates_an_authored_verb(string? verb, string expected) =>
        Assert.Equal(expected, NarrationHelper.ThirdPerson(verb!));

    [Fact]
    public void A_hit_is_narrated_with_the_weapons_verb()
    {
        var round = Round(verb: "slash", seed: 1234);

        Assert.True(round.Damage.Hit);
        Assert.Contains("You slash a goblin", round.AttackerNarration, StringComparison.Ordinal);
        Assert.Contains("Theron slashes you", round.TargetNarration, StringComparison.Ordinal);
        Assert.Contains("Theron slashes a goblin", round.RoomNarration, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unauthored_verb_narrates_as_it_always_did()
    {
        var round = Round(verb: "", seed: 1234);

        Assert.True(round.Damage.Hit);
        Assert.Contains("You hit a goblin", round.AttackerNarration, StringComparison.Ordinal);
        Assert.Contains("Theron hits you", round.TargetNarration, StringComparison.Ordinal);
    }

    /// <summary>
    /// A miss keeps "miss"/"misses". "You miss the rat" is already correct English, and asking a
    /// builder for a second verb form to say it would buy nothing.
    /// </summary>
    [Fact]
    public void A_miss_ignores_the_verb()
    {
        var round = Round(verb: "slash", seed: 7, attackRating: -50);

        Assert.False(round.Damage.Hit);
        Assert.Contains("You miss a goblin", round.AttackerNarration, StringComparison.Ordinal);
        Assert.Contains("Theron misses you", round.TargetNarration, StringComparison.Ordinal);
        Assert.DoesNotContain("slash", round.RoomNarration, StringComparison.Ordinal);
    }

    private static CombatRound Round(string verb, int seed, int attackRating = 20) =>
        Muwbta.Domain.Combat.CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Theron",
            new AttackerStats(Level: 0, AttackRating: attackRating, BaseDamage: 2, MinDamage: 4, MaxDamage: 8),
            CombatantType.Mob,
            "goblin",
            new DefenderStats(Level: 0, DefenseRating: 2, Armor: 0),
            targetCurrentHealth: 50,
            new TargetValidationResult(true, null),
            verb,
            new SeededRandomSource(seed));
}
