using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// The <c>abilities</c> listing says what each ability does, not only what it costs.
/// </summary>
/// <remarks>
/// The listing gave a name and a price and left the effect to be discovered by casting it. These
/// are the engine-side half of that change; <c>AbilityDescriptionTests</c> covers what the phrases
/// say, and this covers that they reach the screen at all and are the ones the game would run.
/// </remarks>
public sealed class AbilityListingTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly EffectRegistry Effects = new();

    private static (WorldHarness Harness, PlayerActor Actor) WithAbilities(
        CharacterPath path,
        int level,
        params string[] keys)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        foreach (var key in keys)
        {
            harness.DefineAbility(key);
        }

        return (harness, harness.AddPlayer("Kaeda", West, path: path, level: level));
    }

    private static string Listing(WorldHarness harness, PlayerActor actor)
    {
        harness.Drain(actor);
        harness.Execute(actor, "abilities");
        return harness.DrainText(actor);
    }

    /// <summary>
    /// The line a player reads: name, price, and what it is for.
    /// </summary>
    [Fact]
    public void The_listing_says_what_an_ability_does()
    {
        var (harness, actor) = WithAbilities(CharacterPath.Hallow, level: 1, "hallow.mend");

        var listing = Listing(harness, actor);

        Assert.Contains("Mend", listing, StringComparison.Ordinal);
        Assert.Contains("(20 focus)", listing, StringComparison.Ordinal);
        Assert.Contains("restores 23-27 health to your target", listing, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Compared against the describer rather than against a copy of its output.</b> A test that
    /// wrote out the expected sentence would go on passing while the listing showed a stale one
    /// built from different parameters — the disagreement to catch is between the screen and the
    /// ability, not between the screen and a string in a test.
    /// </summary>
    [Theory]
    [InlineData(CharacterPath.Warden, "warden.kick")]
    [InlineData(CharacterPath.Shade, "shade.strike")]
    [InlineData(CharacterPath.Adept, "adept.bolt")]
    [InlineData(CharacterPath.Hallow, "hallow.mend")]
    public void The_printed_line_is_the_one_the_describer_derives(CharacterPath path, string key)
    {
        var (harness, actor) = WithAbilities(path, level: 1, key);
        var ability = harness.AbilityCache.Get(key)!;

        Assert.Contains(
            AbilityDescriber.Describe(ability, Effects),
            Listing(harness, actor),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every ability a character knows carries a description, so the listing never explains some of
    /// itself and leaves the rest to be found out the hard way.
    /// </summary>
    [Fact]
    public void Every_ability_in_a_full_listing_is_described()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        foreach (var ability in AbilityCatalogue.AsAbilities.Where(a => a.Path == CharacterPath.Adept))
        {
            harness.DefineAbility(ability.Key);
        }

        var actor = harness.AddPlayer("Kaeda", West, path: CharacterPath.Adept, level: 50);
        var listing = Listing(harness, actor);

        foreach (var ability in AbilityCatalogue.AsAbilities.Where(a => a.Path == CharacterPath.Adept))
        {
            Assert.Contains(
                AbilityDescriber.Describe(ability, Effects),
                listing,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Passives keep the description they already had. They are never cast, have no cost and no
    /// dials, so there is nothing to derive — and running them together with the derived lines
    /// would read as spells that refuse to work.
    /// </summary>
    [Fact]
    public void Passives_keep_their_own_words()
    {
        var (harness, actor) = WithAbilities(CharacterPath.Shade, level: 10, "shade.strike");

        var listing = Listing(harness, actor);

        Assert.Contains("Passives:", listing, StringComparison.Ordinal);
        Assert.Contains(
            PassiveKeys.DescriptionOf(PassiveKeys.DualWield),
            listing,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The seconds in a description are the game's seconds.
    /// </summary>
    /// <remarks>
    /// <c>AbilityAudience</c> lives in the Domain and so cannot see <see cref="GameTiming"/>, which
    /// is in the Engine — so it carries its own quarter second. This is the test that keeps the two
    /// honest: a description measured in the wrong seconds is worse than no description, because it
    /// is wrong in a way nobody would think to check.
    /// </remarks>
    [Fact]
    public void A_described_second_is_a_game_second()
    {
        Assert.Equal(
            (double)AbilityAudience.SecondsPerPulse,
            GameTiming.PulseInterval.TotalSeconds,
            precision: 10);
    }
}
