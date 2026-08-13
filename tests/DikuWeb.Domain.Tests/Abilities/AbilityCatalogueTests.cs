using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
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
        [CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Shade, CharacterPath.Hallow];

    /// <summary>The effect executors that exist. An ability naming anything else does nothing.</summary>
    /// <summary>
    /// The executors that actually exist, asked of the registry rather than listed again here.
    /// </summary>
    /// <remarks>
    /// This was a literal set, and it was a second source of truth for the same question the
    /// registry already answers — so every executor added after it was written had to be
    /// remembered in two places, and forgetting the second one turns this test from a guard into
    /// a thing that fails for no reason. That is the same shape as the seeder-versus-progression
    /// drift <c>AbilityCatalogue</c> exists to make impossible.
    /// </remarks>
    private static readonly EffectRegistry KnownEffects = new();

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
            .SelectMany(p => AbilityProgression.GetAbilitiesForPath(AbilityCatalogue.AsAbilities, p))
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
            .SelectMany(p => AbilityProgression.GetAbilitiesForPath(AbilityCatalogue.AsAbilities, p))
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
    [InlineData("damage.overtime", "tickDamage")]
    [InlineData("damage.overtime", "tickIntervalPulses")]
    public void Every_ability_carries_the_parameter_its_effect_reads(string effectKey, string parameter)
    {
        var missing = AbilityCatalogue.All
            .Where(e => e.EffectKey == effectKey && !e.EffectParams.ContainsKey(parameter))
            .Select(e => e.Key)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// A debuff has to move at least one multiplier, and move it the harmful way.
    /// </summary>
    /// <remarks>
    /// This is the assertion that would have caught the original mistake. Every "weaken" in the
    /// catalogue was written as <c>incomingMultiplier</c> below 1.0, which reads plausibly and
    /// does the opposite of what it says: incoming scales the damage the target *takes*, so those
    /// abilities made their target 25-45% harder to kill. Nothing failed - the spell landed, the
    /// effect appeared on the status screen, and the fight simply went worse.
    /// </remarks>
    [Fact]
    public void Every_debuff_actually_debuffs_its_target()
    {
        foreach (var entry in AbilityCatalogue.All.Where(e => e.EffectKey == "debuff.weaken"))
        {
            var incoming = Read(entry, "incomingMultiplier");
            var outgoing = Read(entry, "outgoingMultiplier");

            Assert.True(
                incoming is not null || outgoing is not null,
                $"{entry.Key} is a debuff that moves neither multiplier.");

            // Taking more damage, or dealing less. Either is a debuff; the wrong side of 1.0 is a
            // gift to whoever it was cast at.
            Assert.True(
                incoming is null or > 1.0m,
                $"{entry.Key} sets incomingMultiplier to {incoming}, which protects the target.");

            Assert.True(
                outgoing is null or < 1.0m,
                $"{entry.Key} sets outgoingMultiplier to {outgoing}, which strengthens the target.");
        }
    }

    /// <summary>
    /// A wound has to actually work: damage above zero, on an interval above zero, for long
    /// enough to tick at least once.
    /// </summary>
    /// <remarks>
    /// <c>ActiveEffect.Ticks</c> requires both a positive damage and a positive interval, so
    /// either at zero produces an effect that sits on the target doing nothing — the same silent
    /// shape as a buff with the wrong parameter name. A duration shorter than one interval is the
    /// subtler version: it ticks zero times and expires.
    /// </remarks>
    [Fact]
    public void Every_wound_ticks_at_least_once_before_it_expires()
    {
        foreach (var entry in AbilityCatalogue.All.Where(e => e.EffectKey == "damage.overtime"))
        {
            var damage = Read(entry, "tickDamage");
            var interval = Read(entry, "tickIntervalPulses");
            var duration = Read(entry, "durationPulses");

            Assert.True(damage is > 0, $"{entry.Key} ticks for {damage}.");
            Assert.True(interval is > 0, $"{entry.Key} has a tick interval of {interval}.");
            // Strictly greater: a tick due on the expiry pulse is skipped, so a wound whose
            // duration equals its interval ticks zero times.
            Assert.True(
                duration > interval,
                $"{entry.Key} lasts {duration} pulses but ticks every {interval}, so it never lands.");
        }
    }

    /// <summary>
    /// A stun stays short enough to be a tempo tool rather than a removal.
    /// </summary>
    /// <remarks>
    /// <c>StunEffect</c> clamps to its own ceiling, so an over-long value cannot reach play — but
    /// it would clamp *silently*, and an author who wrote 200 and got 24 would have no idea. This
    /// fails the build instead.
    /// </remarks>
    [Fact]
    public void No_stun_is_authored_longer_than_the_ceiling()
    {
        foreach (var entry in AbilityCatalogue.All.Where(e => e.EffectKey == "control.stun"))
        {
            var duration = Read(entry, "durationPulses");

            Assert.True(duration is > 0, $"{entry.Key} stuns for {duration}.");
            Assert.True(
                duration <= Domain.Abilities.Effects.StunEffect.MaxDurationPulses,
                $"{entry.Key} stuns for {duration}, past the {Domain.Abilities.Effects.StunEffect.MaxDurationPulses}-pulse ceiling.");
        }
    }

    /// <summary>Snares are clamped for the same reason stuns are, and pinned for the same reason.</summary>
    [Fact]
    public void No_snare_is_authored_longer_than_the_ceiling()
    {
        foreach (var entry in AbilityCatalogue.All.Where(e => e.EffectKey == "control.root"))
        {
            var duration = Read(entry, "durationPulses");

            Assert.True(duration is > 0, $"{entry.Key} snares for {duration}.");
            Assert.True(
                duration <= Domain.Abilities.Effects.RootEffect.MaxDurationPulses,
                $"{entry.Key} snares for {duration}, past the {Domain.Abilities.Effects.RootEffect.MaxDurationPulses}-pulse ceiling.");
        }
    }

    /// <summary>
    /// Control effects hold one target at a time and never stack, so nothing can be chained into
    /// a permanent lock.
    /// </summary>
    [Fact]
    public void No_control_effect_stacks()
    {
        foreach (var entry in AbilityCatalogue.All.Where(e => e.EffectKey.StartsWith("control.", StringComparison.Ordinal)))
        {
            var maxStacks = Read(entry, "maxStacks");
            Assert.True(
                maxStacks is null or 1,
                $"{entry.Key} stacks to {maxStacks}, which chains into a lock.");
        }
    }

    [Fact]
    public void Every_buff_actually_buffs_its_caster()
    {
        foreach (var entry in AbilityCatalogue.All.Where(e => e.EffectKey == "buff.damage-up"))
        {
            var outgoing = Read(entry, "outgoingMultiplier");

            Assert.True(
                outgoing is > 1.0m,
                $"{entry.Key} sets outgoingMultiplier to {outgoing}, which is not an improvement.");
        }
    }

    private static decimal? Read(AbilityCatalogue.Entry entry, string key) =>
        entry.EffectParams.TryGetValue(key, out var raw) &&
        decimal.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// An area effect never carries a cast-time-free instant burst on a short cooldown.
    /// </summary>
    /// <remarks>
    /// This replaces the guard that used to forbid <c>Aoe</c> outright while nothing resolved it.
    /// The mode is implemented now, and what needs watching has moved: an AoE pays one cost and
    /// one cooldown however many things it lands on, so the same numbers that are fair on a single
    /// target are not fair spread across a room. The floor here is deliberately crude — it catches
    /// a room-wide nuke authored with single-target economics, which is the mistake that is easy
    /// to make and hard to notice until a Path trivialises every group of mobs in the game.
    /// </remarks>
    [Fact]
    public void An_area_ability_costs_more_than_a_single_target_one()
    {
        var aoe = AbilityCatalogue.All.Where(e => e.TargetingType == TargetingType.Aoe).ToList();

        foreach (var entry in aoe)
        {
            var comparable = AbilityCatalogue.For(entry.Path)
                .Where(e => e.TargetingType == TargetingType.SingleTarget)
                .Where(e => e.UnlockLevel <= entry.UnlockLevel)
                .ToList();

            Assert.All(comparable, single => Assert.True(
                entry.CostValue > single.CostValue || entry.CooldownPulses > single.CooldownPulses,
                $"{entry.Key} hits the whole room for no more cost or cooldown than {single.Key} " +
                "spends on one target."));
        }
    }

    /// <summary>
    /// Every area ability points somewhere the area filter understands.
    /// </summary>
    /// <remarks>
    /// The filter gathers two different sets depending on <c>IAbilityEffect.IsHarmful</c> — mobs
    /// and (in a <c>pvp</c> room) other players for one, the caster and the people standing with
    /// them for the other. An effect key with no executor behind it has neither, so the ability
    /// would take a cost and fizzle.
    /// </remarks>
    [Fact]
    public void Every_area_ability_has_an_executor_behind_it() =>
        Assert.All(
            AbilityCatalogue.All.Where(e => e.TargetingType == TargetingType.Aoe),
            entry => Assert.NotNull(KnownEffects.Get(entry.EffectKey)));

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
        var atOne = AbilityProgression.GetKnownAbilitiesForLevel(AbilityCatalogue.AsAbilities, CharacterPath.Warden, 1);
        var atSeven = AbilityProgression.GetKnownAbilitiesForLevel(AbilityCatalogue.AsAbilities, CharacterPath.Warden, 7);

        Assert.Contains("warden.kick", atOne);
        Assert.DoesNotContain("warden.sunder", atOne);
        Assert.Contains("warden.sunder", atSeven);
    }

    [Fact]
    public void A_path_does_not_learn_another_paths_abilities()
    {
        var warden = AbilityProgression.GetKnownAbilitiesForLevel(AbilityCatalogue.AsAbilities, CharacterPath.Warden, 20);

        Assert.DoesNotContain("adept.bolt", warden);
        Assert.DoesNotContain("shade.strike", warden);
    }

    /// <summary>
    /// Every cooldown is a whole number of two-second combat beats.
    /// </summary>
    /// <remarks>
    /// A swing is 8 pulses (PLAN.md §2.3), so a cooldown that is not a multiple of it never lines
    /// up with the fight: at 20 pulses an opener is ready at 5s, 10s, 15s while swings land at 2s,
    /// 4s, 6s, and the two drift against each other for as long as the fight lasts. Fourteen of
    /// the thirty-seven were fractional before the retune.
    ///
    /// Held on the shipped set rather than in <c>AbilityValidator</c> on purpose. It is a design
    /// rule for the game's own abilities, not a law about all possible ones - warning a builder
    /// who types 30 would be nagging about a number that works.
    /// </remarks>
    [Fact]
    public void Every_cooldown_lands_on_the_combat_beat()
    {
        const int PulsesPerSwing = 8;

        var offBeat = AbilityCatalogue.All
            .Where(e => e.CooldownPulses % PulsesPerSwing != 0)
            .Select(e => $"{e.Key} at {e.CooldownPulses} pulses ({e.CooldownPulses / 8.0:0.##} beats)")
            .ToList();

        Assert.True(offBeat.Count == 0, "Off the beat:\n  " + string.Join("\n  ", offBeat));
    }

    /// <summary>
    /// Nothing with a duration outlasts its own cooldown.
    /// </summary>
    /// <remarks>
    /// Buffs, debuffs, and wounds all refresh rather than stack, so a duration longer than the
    /// cooldown means the effect can be held up permanently and the cooldown does nothing at all.
    /// Ten of the eleven timed effects were in that state - Weaken at 200% uptime, Scorch at 225%
    /// - which is why the retune's largest moves are all on this list rather than on the damage
    /// abilities the original playtest note was about.
    ///
    /// Two exceptions, both deliberate rather than tolerated:
    ///
    /// <b>A wound may exactly equal its cooldown.</b> Re-applying a damage-over-time as it falls
    /// off *is* the rotation, and 100% uptime on it is not free power the way a permanent buff is
    /// - each application costs the resource and the turn again, and the damage is the whole
    /// point of the ability. What is still wrong for a wound is a duration *longer* than the
    /// cooldown, which lets a second application land on top of the first.
    ///
    /// <b>Ambush stacks to three</b>, so it is meant to be re-applied inside its own duration. At
    /// its old 28-pulse cooldown it could never reach even two: the first expired before a third
    /// could land.
    /// </remarks>
    [Fact]
    public void No_timed_effect_can_be_maintained_permanently()
    {
        var permanent = new List<string>();

        foreach (var entry in AbilityCatalogue.All)
        {
            if (Read(entry, "durationPulses") is not { } duration || duration <= 0)
            {
                continue;
            }

            if ((Read(entry, "maxStacks") ?? 1) > 1)
            {
                continue;
            }

            // A wound is allowed to be re-applied exactly as it expires; a buff or debuff at that
            // point has a cooldown that does nothing.
            var overlaps = entry.EffectKey == "damage.overtime"
                ? duration > entry.CooldownPulses
                : duration >= entry.CooldownPulses;

            if (overlaps)
            {
                permanent.Add(
                    $"{entry.Key} ({entry.EffectKey}) lasts {duration} on a " +
                    $"{entry.CooldownPulses} cooldown ({duration / entry.CooldownPulses:P0} uptime)");
            }
        }

        Assert.True(permanent.Count == 0, "Permanently maintainable:\n  " + string.Join("\n  ", permanent));
    }

    [Fact]
    public void Every_ability_costs_something()
    {
        // A free ability with a cooldown is a worse version of an auto-attack.
        Assert.All(AbilityCatalogue.All, e => Assert.True(e.CostValue > 0, $"{e.Key} is free."));
    }
}
