using DikuWeb.Domain.Combat;
using DikuWeb.Server.Building;

namespace DikuWeb.Server.Tests.Building;

/// <summary>
/// The authored weapons follow the rules they were tuned by (PLAN.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// Weapons used to declare a <c>damageMultiplier</c> and no dice, so a weapon's damage was
/// <c>ceil(1×m)</c>–<c>ceil(2×m)</c> over a hidden unarmed baseline and its authored number said
/// nothing about what it hit for. 22 distinct multipliers across 35 weapons resolved to 14 distinct
/// dice: <c>1.6</c>, <c>1.8</c> and <c>2.0</c> were the same weapon, and <c>1.4</c> was worth exactly
/// what <c>1.1</c> would have been.
/// </para>
/// <para>
/// What that opacity hid is why these are tests rather than a spreadsheet. <b>Speed was a free
/// bonus</b>: <c>epic-warden-2</c>, <c>-4</c> and <c>-5</c> carried identical dice and identical
/// attack rating to the Hallow line and swung slower, so the Warden's reward was strictly worse at
/// three of five tiers. And <c>ossara-hand-axe</c> and <c>ossara-walking-staff</c> were the same
/// item. Nothing in the numbers made either visible.
/// </para>
/// </remarks>
public sealed class WeaponBalanceTests
{
    /// <summary>Pulses per second — a pulse is 250 ms (§2.1).</summary>
    private const double PulsesPerSecond = 4.0;

    /// <summary>
    /// Integer dice cannot land on an exact ratio, so equality is asserted within a band.
    /// </summary>
    /// <remarks>
    /// 15% because the low tiers are the loose ones and cannot be otherwise: at an average of three
    /// damage a single point is a third of the roll, so <c>ossara</c> lands at 11% spread and
    /// <c>grask</c> at 13% while <c>azhen</c> and <c>nemhal</c> are exact. Tightening this would be
    /// asserting something integer dice cannot do.
    /// </remarks>
    private const double Tolerance = 0.15;

    private static readonly string[][] ShopLines =
    [
        ["ossara-short-blade", "ossara-hand-axe", "ossara-walking-staff"],
        ["grask-claim-cleaver", "grask-dredge-hook", "grask-long-pick"],
        ["azhen-gate-stylus", "azhen-measuring-rod", "azhen-counterweight-maul"],
        ["nemhal-keening-blade", "nemhal-vigil-spear", "nemhal-standing-maul"],
        ["unlit-long-blade", "unlit-binding-spike", "unlit-standing-hammer"],
    ];

    /// <summary>Highest damage first — the ranking the epic line is tuned to.</summary>
    private static readonly string[] EpicOrder = ["shade", "warden", "hallow", "adept"];

    private static WorldBundle World()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DikuWeb.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var sources = Directory
            .EnumerateFiles(Path.Combine(dir!.FullName, "content"), "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(path =>
            {
                Assert.True(BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var error), error);
                return new BundleSource(path, bundle!);
            })
            .ToList();

        var merged = BundleMerge.Merge(sources);
        Assert.True(merged.Ok, string.Join("\n", merged.Errors));
        return merged.Bundle!;
    }

    private static Dictionary<string, BundleItemTemplate> Weapons() =>
        World().ItemTemplates
            .Where(i => i.AttackDelayPulses is not null)
            .ToDictionary(i => i.Key, StringComparer.Ordinal);

    /// <summary>
    /// Through <see cref="StatReader"/>, which is how the engine reads these.
    /// </summary>
    /// <remarks>
    /// Not <c>Convert.ToDouble</c>. <c>BaseStats</c> is a <c>Dictionary&lt;string, object&gt;</c>, so a
    /// value that arrived through JSON is a <c>JsonElement</c> and casting it throws — and a test that
    /// read these its own way would be asserting numbers the engine might not agree about.
    /// </remarks>
    private static double Dps(BundleItemTemplate weapon)
    {
        var stats = weapon.BaseStats ?? [];

        // Read both before asserting: `&&` short-circuits, so the compiler cannot see `max` as
        // assigned if the first call is what failed.
        var hasMin = StatReader.TryReadInt(stats, "damageMin", out var min);
        var hasMax = StatReader.TryReadInt(stats, "damageMax", out var max);

        Assert.True(
            hasMin && hasMax,
            $"{weapon.Key} declares no dice the engine can read, so it swings as a bare fist "
            + "whatever else it carries");

        var seconds = weapon.AttackDelayPulses!.Value / PulsesPerSecond;

        return (min + max) / 2.0 / seconds;
    }

    /// <summary>A stat as the engine reads it, for comparing two weapons' lines.</summary>
    private static string Stat(BundleItemTemplate weapon, string key) =>
        StatReader.TryReadInt(weapon.BaseStats ?? [], key, out var value)
            ? value.ToString()
            : "-";

    // -----------------------------------------------------------------------
    // Every weapon says what it hits for
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_weapon_declares_its_own_dice()
    {
        // The whole point of the change. A weapon without dice is a fist with a name on it.
        var weapons = Weapons();
        Assert.Equal(35, weapons.Count);

        foreach (var weapon in weapons.Values)
        {
            Dps(weapon);
        }
    }

    [Fact]
    public void No_weapon_carries_a_damage_multiplier()
    {
        Assert.All(
            Weapons().Values,
            weapon => Assert.DoesNotContain("damageMultiplier", (weapon.BaseStats ?? []).Keys));
    }

    /// <summary>
    /// Two weapons with the same numbers are one weapon with two names. <c>ossara-hand-axe</c> and
    /// <c>ossara-walking-staff</c> were exactly that — delay 8, 2–4, no bonus, no way to tell.
    /// </summary>
    [Fact]
    public void No_two_weapons_share_a_stat_line()
    {
        var lines = Weapons().Values
            .Select(w => (
                w.Key,
                Line: $"{w.AttackDelayPulses}|"
                    + string.Join(",", (w.BaseStats ?? []).Keys.Order(StringComparer.Ordinal)
                        .Select(k => $"{k}={Stat(w, k)}"))))
            .GroupBy(x => x.Line, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(" == ", g.Select(x => x.Key)))
            .ToList();

        Assert.True(lines.Count == 0, "weapons with identical stat lines:\n  " + string.Join("\n  ", lines));
    }

    // -----------------------------------------------------------------------
    // Shop lines: delay is a feel choice, not a power choice
    // -----------------------------------------------------------------------

    /// <summary>
    /// The shop weapons are Path-open (<c>paths: []</c>), so the same weapon may be picked by anyone
    /// and the only thing separating them is speed. Equal damage per second is what makes that a
    /// choice — bigger numbers less often, or smaller numbers more often, for the same output.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_shop_line_offers_one_damage_per_second_at_three_speeds(int line)
    {
        var weapons = Weapons();
        var keys = ShopLines[line];

        var rates = keys.Select(k =>
        {
            Assert.True(weapons.ContainsKey(k), $"{k} is not an authored weapon");
            return (Key: k, Dps: Dps(weapons[k]));
        }).ToList();

        // Three distinct delays, so the choice is a real one rather than three of the same weapon.
        Assert.Equal(3, keys.Select(k => weapons[k].AttackDelayPulses).Distinct().Count());

        var low = rates.Min(r => r.Dps);
        var high = rates.Max(r => r.Dps);

        Assert.True(
            (high - low) / low <= Tolerance,
            $"the {keys[0].Split('-')[0]} line is not one rate: "
            + string.Join(", ", rates.Select(r => $"{r.Key} {r.Dps:F2}")));
    }

    [Fact]
    public void Every_shop_line_is_stronger_than_the_one_below_it()
    {
        var weapons = Weapons();

        var means = ShopLines.Select(line => line.Average(k => Dps(weapons[k]))).ToList();

        for (var i = 1; i < means.Count; i++)
        {
            Assert.True(
                means[i] > means[i - 1],
                $"shop line {i} ({means[i]:F2} dps) is not above line {i - 1} ({means[i - 1]:F2})");
        }
    }

    // -----------------------------------------------------------------------
    // Epic lines: ranked by Path
    // -----------------------------------------------------------------------

    /// <summary>
    /// Path-locked, so each says what melee means to that Path: Shade &gt; Warden &gt; Hallow &gt;
    /// Adept, with the two casters close together because their output is focus abilities.
    /// </summary>
    /// <remarks>
    /// <b>Tier 1 is excluded, and that is a decision rather than a gap.</b> At an average of three
    /// damage the Hallow/Adept gap rounds away and both land on 2–4; integer dice have a floor on how
    /// fine a distinction they can carry. The order holds from tier 2 up, where the dice are large
    /// enough to express it.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void An_epic_tier_ranks_shade_then_warden_then_hallow_then_adept(int tier)
    {
        var weapons = Weapons();

        var rates = EpicOrder.Select(path =>
        {
            var key = $"epic-{path}-{tier}";
            Assert.True(weapons.ContainsKey(key), $"{key} is not an authored weapon");
            return (Path: path, Dps: Dps(weapons[key]));
        }).ToList();

        for (var i = 1; i < rates.Count; i++)
        {
            Assert.True(
                rates[i - 1].Dps > rates[i].Dps,
                $"epic tier {tier} is out of order: "
                + string.Join(" ", rates.Select(r => $"{r.Path}={r.Dps:F2}")));
        }
    }

    /// <summary>
    /// The specific defect this pass fixed: a Warden's reward must not be a slower Hallow's.
    /// </summary>
    /// <remarks>
    /// It was, at tiers 2, 4 and 5 — identical dice, identical attack rating, delay 10 against 8. A
    /// Path's capstone being strictly dominated by another's is not a balance opinion, it is a bug,
    /// and it survived because the multiplier made the comparison unreadable.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void The_warden_line_is_never_a_slower_copy_of_the_hallow_line(int tier)
    {
        var weapons = Weapons();
        var warden = weapons[$"epic-warden-{tier}"];
        var hallow = weapons[$"epic-hallow-{tier}"];

        var sameDice = Stat(warden, "damageMin") == Stat(hallow, "damageMin")
            && Stat(warden, "damageMax") == Stat(hallow, "damageMax");

        Assert.False(
            sameDice && warden.AttackDelayPulses > hallow.AttackDelayPulses,
            $"epic-warden-{tier} has the same dice as epic-hallow-{tier} and swings slower");

        Assert.True(
            Dps(warden) > Dps(hallow),
            $"epic-warden-{tier} ({Dps(warden):F2} dps) is not above epic-hallow-{tier} ({Dps(hallow):F2})");
    }

    [Fact]
    public void Every_epic_tier_is_stronger_than_the_one_below_it()
    {
        var weapons = Weapons();

        for (var tier = 2; tier <= 5; tier++)
        {
            foreach (var path in EpicOrder)
            {
                var below = Dps(weapons[$"epic-{path}-{tier - 1}"]);
                var here = Dps(weapons[$"epic-{path}-{tier}"]);

                Assert.True(
                    here > below,
                    $"epic-{path}-{tier} ({here:F2} dps) is not above epic-{path}-{tier - 1} ({below:F2})");
            }
        }
    }
}
