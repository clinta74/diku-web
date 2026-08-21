using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Tests.Inhabitants;

/// <summary>
/// What a zone actually does to a mob's numbers (PLAN.md §4.4).
/// </summary>
/// <remarks>
/// <b>Nothing proved this before, and it was wrong.</b> <c>Mob.ResolvedStats</c> was a verbatim copy
/// of the template, so the <c>damage</c> dial reached nothing at all and <c>strength</c> — the
/// master dial, documented as scaling health <em>and</em> damage — only made mobs tankier. Every
/// test in the suite ran against a zone at 1.0, where a straight copy and a correct resolve are
/// indistinguishable.
/// </remarks>
public sealed class MobScalingTests
{
    private static Multipliers None => new();

    private static MobScaling Zone(decimal strength = 1m, decimal health = 1m, decimal damage = 1m) =>
        MobScaling.FromZone(10, None, new Multipliers
        {
            Strength = strength,
            Health = health,
            Damage = damage,
        }, zoneMinLevel: 1);

    // -----------------------------------------------------------------------
    // §4.4's worked example, which is the specification
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, 40, 4, 7)]
    [InlineData(2.5, 100, 10, 18)]
    [InlineData(6.0, 240, 24, 42)]
    public void The_kobold_sentry_resolves_the_way_the_plan_says(
        double strength,
        int health,
        int minDamage,
        int maxDamage)
    {
        // PLAN.md §4.4 states this table outright — 40 hp and 4-7 damage at strength 1.0, 2.5 and
        // 6.0 — and until now nothing asserted it. The damage half of every row was false.
        var scaling = MobScaling.FromZone(
            8,
            None,
            new Multipliers { Strength = (decimal)strength },
            zoneMinLevel: 1);

        var resolved = scaling.ResolveStats(new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damage"] = "4-7",
        });

        Assert.True(StatReader.TryReadInt(resolved, "health", out var resolvedHealth));
        Assert.Equal(health, resolvedHealth);

        Assert.True(StatReader.TryReadRange(resolved, "damage", out var min, out var max));
        Assert.Equal(minDamage, min);
        Assert.Equal(maxDamage, max);
    }

    // -----------------------------------------------------------------------
    // Which keys move, and which must not
    // -----------------------------------------------------------------------

    [Fact]
    public void An_untouched_zone_changes_nothing()
    {
        // The identity case is the whole suite's blind spot, so it gets its own test: every live
        // zone is at 1.0, and if this ever stops holding, everything already authored shifts.
        var stats = new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damage"] = "4-7",
            ["damageMin"] = 3,
            ["attackRating"] = 9,
            ["defense"] = 5,
        };

        var resolved = Zone().ResolveStats(stats);

        Assert.Equal("4-7", resolved["damage"]);
        foreach (var key in new[] { "health", "damageMin", "attackRating", "defense" })
        {
            Assert.True(StatReader.TryReadInt(stats, key, out var before));
            Assert.True(StatReader.TryReadInt(resolved, key, out var after));
            Assert.Equal(before, after);
        }
    }

    [Fact]
    public void Toughness_moves_on_the_health_dial_and_the_punch_does_not()
    {
        var resolved = Zone(health: 3m).ResolveStats(new Dictionary<string, object>
        {
            ["health"] = 40,
            ["armor"] = 2,
            ["damageMin"] = 5,
            ["attackRating"] = 8,
        });

        Assert.Equal(120, Read(resolved, "health"));
        Assert.Equal(6, Read(resolved, "armor"));

        Assert.Equal(5, Read(resolved, "damageMin"));
        Assert.Equal(8, Read(resolved, "attackRating"));
    }

    /// <summary>
    /// <b>Defence is never scaled, by any dial.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Armour and defence look like the same kind of number and are not. Armour feeds a curve with
    /// a cap, so a rating four times larger absorbs more and never absorbs everything. Defence
    /// feeds a d20 comparison, where twenty faces is the whole budget — so a dial applied to it
    /// pushes the gap between mob defence and player attack rating outside what the die can say,
    /// and the roll stops being consulted.
    /// </para>
    /// <para>
    /// It did scale, and the Unlit is what that produced: <c>strength 4.7</c> turned four mobs
    /// authored 2 / 3 / 4 / 6 apart into 10 / 14 / 20 / 30 apart, and a fully equipped level 48
    /// needed a natural 20 to land a blow in the zone written for level 48.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(4.7)]
    [InlineData(3.0)]
    [InlineData(0.5)]
    public void Defence_is_never_scaled(double dial)
    {
        var factor = (decimal)dial;

        foreach (var scaling in new[]
        {
            Zone(strength: factor),
            Zone(health: factor),
            Zone(damage: factor),
        })
        {
            var resolved = scaling.ResolveStats(new Dictionary<string, object>
            {
                ["health"] = 40,
                ["defense"] = 6,
            });

            Assert.Equal(6, Read(resolved, "defense"));
        }
    }

    [Fact]
    public void The_punch_moves_on_the_damage_dial_and_toughness_does_not()
    {
        var resolved = Zone(damage: 3m).ResolveStats(new Dictionary<string, object>
        {
            ["health"] = 40,
            ["defense"] = 6,
            ["damageMin"] = 5,
            ["damageMax"] = 9,
            ["baseDamage"] = 4,
            ["attackRating"] = 8,
        });

        Assert.Equal(15, Read(resolved, "damageMin"));
        Assert.Equal(27, Read(resolved, "damageMax"));
        Assert.Equal(12, Read(resolved, "baseDamage"));
        Assert.Equal(24, Read(resolved, "attackRating"));

        Assert.Equal(40, Read(resolved, "health"));
        Assert.Equal(6, Read(resolved, "defense"));
    }

    [Fact]
    public void Strength_moves_both_sides()
    {
        // Which is the one thing the dial has always claimed and never done.
        var resolved = Zone(strength: 2m).ResolveStats(new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damageMin"] = 5,
            ["armor"] = 10,
            ["defense"] = 6,
        });

        Assert.Equal(80, Read(resolved, "health"));
        Assert.Equal(10, Read(resolved, "damageMin"));

        // Tougher and harder-hitting, and no harder to hit.
        Assert.Equal(20, Read(resolved, "armor"));
        Assert.Equal(6, Read(resolved, "defense"));
    }

    [Fact]
    public void A_ratio_is_never_scaled()
    {
        // damageMultiplier, armorMultiplier and armorPercent are already factors. Multiplying a
        // factor by a factor applies the zone twice - and DamageCalculator applies
        // damageMultiplier again after this, to whichever dice are in play.
        var resolved = Zone(strength: 4m).ResolveStats(new Dictionary<string, object>
        {
            ["damageMultiplier"] = 1.5m,
            ["armorMultiplier"] = 2.0m,
            ["armorPercent"] = 0.25m,
        });

        Assert.Equal(1.5m, ReadDecimal(resolved, "damageMultiplier"));
        Assert.Equal(2.0m, ReadDecimal(resolved, "armorMultiplier"));
        Assert.Equal(0.25m, ReadDecimal(resolved, "armorPercent"));
    }

    [Fact]
    public void A_stat_it_does_not_understand_is_carried_across_untouched()
    {
        // The bag is open (§4.8). A key this does not recognise still belongs to the mob, and
        // dropping it would make an unknown stat disappear on spawn.
        var resolved = Zone(strength: 2m).ResolveStats(new Dictionary<string, object>
        {
            ["might"] = 12,
            ["temperament"] = "sullen",
        });

        Assert.Equal(12, Read(resolved, "might"));
        Assert.Equal("sullen", resolved["temperament"]);
    }

    [Fact]
    public void Nothing_scales_away_to_nothing()
    {
        // The floor Multipliers.Resolve already applies, for the same reason: a mob with zero
        // damage is not a gentle mob, it is a fight that cannot end.
        var resolved = Zone(damage: 0.01m).ResolveStats(new Dictionary<string, object>
        {
            ["damageMin"] = 2,
            ["damage"] = "2-4",
        });

        Assert.Equal(1, Read(resolved, "damageMin"));
        Assert.True(StatReader.TryReadRange(resolved, "damage", out var min, out var max));
        Assert.Equal(1, min);
        Assert.Equal(1, max);
    }

    [Fact]
    public void An_inverted_range_does_not_survive_scaling()
    {
        // Rounding can cross a narrow range over itself; DamageCalculator guards this too, but a
        // bag that leaves here already inverted would make the guard the only thing standing
        // between a template typo and a throwing damage roll.
        var resolved = Zone(damage: 0.3m).ResolveStats(new Dictionary<string, object>
        {
            ["damage"] = "3-4",
        });

        Assert.True(StatReader.TryReadRange(resolved, "damage", out var min, out var max));
        Assert.True(max >= min);
    }

    // -----------------------------------------------------------------------
    // Pinning a level
    // -----------------------------------------------------------------------

    [Fact]
    public void A_pinned_level_is_the_level()
    {
        var scaling = MobScaling.FromTarget(templateLevel: 10, targetLevel: 27);

        Assert.Equal(27, scaling.Level);
        Assert.Equal(2.7m, scaling.Health);
        Assert.Equal(2.7m, scaling.Damage);
    }

    [Fact]
    public void Pinning_a_template_to_its_own_level_changes_nothing()
    {
        var scaling = MobScaling.FromTarget(templateLevel: 25, targetLevel: 25);

        Assert.Equal(1m, scaling.Health);
        Assert.Equal(1m, scaling.Damage);
    }

    [Fact]
    public void A_template_at_level_zero_does_not_divide_by_it()
    {
        // Real, not theoretical: the builder API accepts level 0 with no floor.
        var scaling = MobScaling.FromTarget(templateLevel: 0, targetLevel: 12);

        Assert.Equal(12, scaling.Level);
        Assert.Equal(12m, scaling.Health);
    }

    [Fact]
    public void Pinning_a_level_agrees_with_scaling_a_zone_to_it()
    {
        // The exponent argument, as a property rather than a comment. Whatever factor FromTarget
        // picks to reach level N, a zone whose strength dial is that same factor must produce the
        // same level - otherwise there are two ways to say "twice as hard" that disagree, which is
        // exactly the divergence MobLevel exists to close.
        foreach (var (templateLevel, target) in new[] { (10, 20), (5, 30), (8, 24), (12, 36) })
        {
            var pinned = MobScaling.FromTarget(templateLevel, target);

            var viaZone = MobScaling.FromZone(
                templateLevel,
                None,
                new Multipliers { Strength = pinned.Health },
                zoneMinLevel: 1);

            Assert.Equal(target, viaZone.Level);
            Assert.Equal(pinned.Health, viaZone.Health);
            Assert.Equal(pinned.Damage, viaZone.Damage);
        }
    }

    private static int Read(IReadOnlyDictionary<string, object> stats, string key)
    {
        Assert.True(StatReader.TryReadInt(stats, key, out var value), $"'{key}' was not readable.");
        return value;
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, object> stats, string key)
    {
        Assert.True(StatReader.TryReadDecimal(stats, key, out var value), $"'{key}' was not readable.");
        return value;
    }
}
