using System.Globalization;
using System.Text;
using DikuWeb.Balance.Content;
using DikuWeb.Balance.Sim;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Items;

namespace DikuWeb.Balance.Reporting;

/// <summary>
/// The tables. Each answers one question, and each says which fights it asked.
/// </summary>
public sealed class Report(ContentSet content, Options options)
{
    private readonly FightSimulator _simulator = new();
    private readonly List<Row> _rows = [];

    /// <summary>
    /// One measured cell: a Path at a level, against a level-appropriate mob, in that realm's gear.
    /// </summary>
    private sealed record Cell(
        CharacterPath Path,
        int Level,
        Encounter Encounter,
        Loadout Loadout,
        IReadOnlyList<FightResult> WithAbilities,
        IReadOnlyList<FightResult> WithoutAbilities);

    private sealed record Row(
        CharacterPath Path, int Level, string Encounter, int Seed, FightResult Result, bool Abilities);

    private readonly Dictionary<(CharacterPath, int), Cell> _cells = [];

    // -------------------------------------------------------------------------------------
    // Table 1: where the damage comes from
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The split between weapon swings, abilities landing, and wounds ticking.
    /// </summary>
    /// <remarks>
    /// <b>The headline number is the ability share, and it is meant to be read down the column.</b>
    /// A share that falls as the level rises is the flat ability base showing through: nothing in
    /// <c>DamageEffect</c> reads the caster, so the same cast that took a third of a Gatetown mob
    /// takes a fiftieth of an Unlit one, while the weapon beside it grew six-fold with the gear
    /// tiers.
    /// </remarks>
    public void WriteDamageSplit()
    {
        Section("Where a Path's damage comes from");

        Console.WriteLine(
            $"{"Path",-7} {"Lvl",3}  {"target",-26} {"kill",6} {"result",-9} " +
            $"{"weapon",7} {"cast",7} {"wound",7}  {"ability",7}  {"hp left",7}");
        Console.WriteLine(new string('-', 106));

        foreach (var path in options.Paths)
        {
            foreach (var level in options.Levels)
            {
                var cell = Measure(path, level);

                if (cell is null)
                {
                    continue;
                }

                var runs = cell.WithAbilities;

                Console.WriteLine(
                    $"{path,-7} {level,3}  {Trim(cell.Encounter.Name, 20),-20}{cell.Encounter.Level,3}L " +
                    $"{Seconds(Median(runs, r => r.Seconds)),6} " +
                    $"{Outcomes(runs),-9} " +
                    $"{Median(runs, r => r.WeaponDamage),7:N0} " +
                    $"{Median(runs, r => r.AbilityDamage),7:N0} " +
                    $"{Median(runs, r => r.WoundDamage),7:N0}  " +
                    $"{Median(runs, r => r.AbilityShare),6:P0}  " +
                    $"{Median(runs, r => r.HealthShare),6:P0}");
            }

            Console.WriteLine();
        }

        Console.WriteLine(
            "  ability = (cast + wound) / everything the player dealt. Read it down the column:");
        Console.WriteLine(
            "  a share that falls with level is the flat ability base showing through.");
        Console.WriteLine();
    }

    // -------------------------------------------------------------------------------------
    // Table 2: what the bar is actually worth
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The same fights with the ability bar switched off.
    /// </summary>
    /// <remarks>
    /// <b>A better measure than the damage split, and it exists because the split flatters.</b> A
    /// share of 20% sounds material; if switching the bar off lengthens the fight by 20% it is
    /// material, and if it lengthens it by 4% then most of that share was damage the weapon would
    /// have dealt anyway in the time the casts occupied. Time-to-kill is the quantity a player
    /// actually experiences, and it is the one that carries the incoming damage a longer fight
    /// costs.
    /// </remarks>
    public void WriteAbilityWorth()
    {
        Section("What the ability bar is worth (same fights, same seeds, bar switched off)");

        Console.WriteLine(
            $"{"Path",-7} {"Lvl",3}  {"won with",8} {"won without",11}  {"kill with",9} " +
            $"{"kill without",12} {"faster by",9}  {"casts",5} {"starved",7}");
        Console.WriteLine(new string('-', 84));

        foreach (var path in options.Paths)
        {
            foreach (var level in options.Levels)
            {
                var cell = Measure(path, level);

                if (cell is null)
                {
                    continue;
                }

                // Won fights only, on both sides.
                //
                // A lost fight's Seconds is how long the player lasted, not how long a kill took,
                // and averaging the two together produces exactly the wrong sign: a bar that keeps
                // a Hallow alive for 142 seconds instead of dying at 60 reported as "137% slower".
                // Every cell where the ability bar mattered most was the one this misread.
                var with = WonSeconds(cell.WithAbilities);
                var without = WonSeconds(cell.WithoutAbilities);

                var wonWith = cell.WithAbilities.Count(r => r.Outcome == FightOutcome.Won);
                var wonWithout = cell.WithoutAbilities.Count(r => r.Outcome == FightOutcome.Won);

                // Only comparable when both sides actually finished some fights. Where one side
                // never wins, the survival columns beside this are the finding and a percentage
                // would only obscure it.
                var faster = with > 0 && without > 0
                    ? $"{1 - (with / without),8:P0}"
                    : wonWith > 0 && wonWithout == 0
                        ? " decides"
                        : "       --";

                Console.WriteLine(
                    $"{path,-7} {level,3}  {Rate(wonWith, cell.WithAbilities.Count),8} " +
                    $"{Rate(wonWithout, cell.WithoutAbilities.Count),11}  " +
                    $"{Seconds(with),9} {Seconds(without),12} {faster,9}  " +
                    $"{Median(cell.WithAbilities, r => r.Casts),5:N0} " +
                    $"{Median(cell.WithAbilities, r => r.StarvedPulses),7:N0}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("  \"decides\" = the bar is the difference between winning and not; there is no");
        Console.WriteLine("  kill time to compare against. Kill times are medians over WON fights only.");
        Console.WriteLine("  starved = pulses on which something was off cooldown and unaffordable.");
        Console.WriteLine();
    }

    /// <summary>
    /// The encounters the report chose, and what they hit for.
    /// </summary>
    /// <remarks>
    /// Half of "where are we" is the other side of the fight, and a mob's punch is mostly
    /// <em>not</em> authored: <c>DamageCalculator.StatsFrom</c> falls back to <c>1 + level/2</c> to
    /// <c>4 + 3·level/2</c> for any template that declares no damage, which is most of them. That
    /// fallback is the steepest curve in the game and it is invisible in the content files, so it
    /// is printed here beside the health it has to chew through.
    /// </remarks>
    public void WriteEncounters()
    {
        Section("The other side of the fight");

        Console.WriteLine(
            $"{"Lvl",3}  {"target",-26} {"zone",-22} {"mob lv",6} {"health",7} {"swing",11} {"armour",6}");
        Console.WriteLine(new string('-', 92));

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var level in options.Levels)
        {
            var encounter = PickEncounter(level);

            if (encounter is null || !seen.Add(encounter.TemplateKey + encounter.ZoneKey))
            {
                continue;
            }

            var mob = encounter.ToMob();
            var attacker = DamageCalculator.StatsFrom(mob);
            var defender = DamageCalculator.DefenderStatsFrom(mob);

            Console.WriteLine(
                $"{level,3}  {Trim(encounter.Name, 26),-26} {Trim(encounter.ZoneKey, 22),-22} " +
                $"{encounter.Level,6} {encounter.Health,7:N0} " +
                $"{$"{attacker.MinDamage}-{attacker.MaxDamage}",11} {defender.Armor,6}");
        }

        Console.WriteLine();
        Console.WriteLine("  swing = one attack's dice, before armour and before the multipliers.");
        Console.WriteLine();
    }

    /// <summary>Median seconds across the fights that were actually won, or zero if none were.</summary>
    private static double WonSeconds(IReadOnlyList<FightResult> runs)
    {
        var won = runs.Where(r => r.Outcome == FightOutcome.Won).ToList();

        return won.Count == 0 ? 0 : Median(won, r => r.Seconds);
    }

    private static string Rate(int won, int total) => $"{won}/{total}";

    // -------------------------------------------------------------------------------------
    // Table 3: the abilities themselves
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Every damage ability, what it actually deals, and what that is worth against a mob of the
    /// level it unlocks at.
    /// </summary>
    /// <remarks>
    /// <b>Static, not simulated, and that is the point.</b> This is the authored file read back as
    /// numbers rather than as multipliers over a private constant — which is how
    /// <c>scalingFactor: 3.5</c> came to mean 35 damage without anybody noticing. Every dial in the
    /// content is a ratio over something invisible; this table is where they become damage.
    /// </remarks>
    public void WriteAbilityLedger()
    {
        Section("Every damage ability, as damage");

        Console.WriteLine(
            $"{"Path",-7} {"Lvl",3} {"ability",-24} {"hit",5} {"wound",6} {"cost",5} " +
            $"{"cd",6} {"per sec",7}  {"of target",9}");
        Console.WriteLine(new string('-', 88));

        var registry = new EffectRegistry();

        foreach (var path in options.Paths)
        {
            var abilities = content.Abilities.Values
                .Where(a => a.Path == path)
                .OrderBy(a => a.UnlockLevel)
                .ThenBy(a => a.Key, StringComparer.Ordinal);

            foreach (var ability in abilities)
            {
                var hit = ability.Effects
                    .Where(e => string.Equals(e.Key, "damage.physical", StringComparison.Ordinal))
                    .Sum(e => DamageEffect.Middle(e.Params));

                var wound = ability.Effects
                    .Where(e => string.Equals(e.Key, "damage.overtime", StringComparison.Ordinal))
                    .Sum(e =>
                    {
                        var tick = Read(e.Params, "tickDamage", 4);
                        var interval = Read(e.Params, "tickIntervalPulses", 8);
                        var duration = Read(e.Params, "durationPulses", 48);
                        return tick * DamageOverTimeEffect.TickCount(duration, interval);
                    });

                if (hit == 0 && wound == 0)
                {
                    continue;
                }

                var total = hit + wound;
                var cooldownSeconds = ability.CooldownPulses / (double)FightSimulator.PulsesPerSecond;
                var perSecond = cooldownSeconds > 0 ? total / cooldownSeconds : total;

                // Against a mob of the level this unlocks at, which is the fight it was tuned for.
                var target = PickEncounter(ability.UnlockLevel);
                var share = target is null ? 0 : (double)total / target.Health;

                Console.WriteLine(
                    $"{path,-7} {ability.UnlockLevel,3} {Trim(ability.Name, 24),-24} " +
                    $"{(hit > 0 ? hit.ToString(CultureInfo.InvariantCulture) : "-"),5} " +
                    $"{(wound > 0 ? wound.ToString(CultureInfo.InvariantCulture) : "-"),6} " +
                    $"{ability.CostValue,5} " +
                    $"{cooldownSeconds,5:0.#}s " +
                    $"{perSecond,7:0.00}  {share,9:P1}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("  per sec = everything one cast deals, over its own cooldown. The sustained");
        Console.WriteLine("  contribution of pressing only this button, ignoring every other.");
        Console.WriteLine("  of target = that total against a typical mob of the unlock level.");
        Console.WriteLine();
    }

    // -------------------------------------------------------------------------------------
    // Machinery
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Runs — or returns already-run — every fight for one Path at one level.
    /// </summary>
    private Cell? Measure(CharacterPath path, int level)
    {
        if (_cells.TryGetValue((path, level), out var cached))
        {
            return cached;
        }

        var encounter = PickEncounter(level);

        if (encounter is null)
        {
            return null;
        }

        var loadout = Loadout.Best(content, path, encounter.WorldKey);

        var with = new List<FightResult>();
        var without = new List<FightResult>();

        for (var i = 0; i < options.Runs; i++)
        {
            // The same seed for both configurations, so the pair differs only in the bar. A
            // different roll on each side would put the whole comparison inside the noise.
            var seed = options.Seed + i;

            var a = _simulator.Run(content, path, level, loadout, encounter, seed, options.Cap, true);
            var b = _simulator.Run(content, path, level, loadout, encounter, seed, options.Cap, false);

            with.Add(a);
            without.Add(b);

            _rows.Add(new Row(path, level, encounter.TemplateKey, seed, a, true));
            _rows.Add(new Row(path, level, encounter.TemplateKey, seed, b, false));
        }

        var cell = new Cell(path, level, encounter, loadout, with, without);
        _cells[(path, level)] = cell;

        return cell;
    }

    /// <summary>
    /// A typical mob of the content authored for this level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Chosen by the zone's own level band, not by the mob's derived level.</b> A zone declares
    /// who it is for (<c>minLevel</c>/<c>maxLevel</c>) and that is the author speaking directly. The
    /// mob's <em>effective</em> level is a derived number - template level times the strength dial
    /// through <c>MobLevel.Effective</c> - and matching on it produces nonsense at the top of the
    /// game: the Unlit sits at <c>strength 4.7</c>, so its level 9 template resolves to level 42 and
    /// a level 42 character would be sent to fight endgame content in endgame gear.
    /// </para>
    /// <para>
    /// <b>The median by health within the band, not the toughest and not the first.</b> A zone's
    /// roster runs from flavour critters to bosses with ten times the pool, so either end would
    /// measure an outlier and name it after the tier.
    /// </para>
    /// </remarks>
    private Encounter? PickEncounter(int level)
    {
        var inBand = content.Encounters
            .Where(e => level >= e.ZoneMinLevel && level <= e.ZoneMaxLevel)
            .OrderBy(e => e.Health)
            .ToList();

        if (inBand.Count > 0)
        {
            return inBand[inBand.Count / 2];
        }

        // Outside every band - the level gaps between realms. Fall back to the nearest band rather
        // than the nearest mob, so the gear tier stays the one the author paired with it.
        var nearest = content.Encounters
            .Select(e => (Encounter: e, Distance: Math.Min(
                Math.Abs(e.ZoneMinLevel - level), Math.Abs(e.ZoneMaxLevel - level))))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Encounter.Health)
            .ToList();

        return nearest.Count > 0 ? nearest[0].Encounter : null;
    }

    /// <summary>
    /// What each cell was fought in.
    /// </summary>
    /// <remarks>
    /// Printed rather than assumed: the gear model is the harness's largest inference - nothing on
    /// an item states a level or a tier - and a reader who disagrees with a pick should be able to
    /// see it rather than reverse-engineer it from a damage number.
    /// </remarks>
    public void WriteLoadouts()
    {
        Section("What each Path was wearing");

        foreach (var path in options.Paths)
        {
            foreach (var level in options.Levels)
            {
                if (!_cells.TryGetValue((path, level), out var cell))
                {
                    continue;
                }

                var weapons = cell.Loadout.Equipped
                    .Where(i => i.EquippedSlot is ItemSlot.MainHand or ItemSlot.OffHand)
                    .Select(i => i.TemplateKey);

                var armour = cell.Loadout.Equipped.Count(i =>
                    i.EquippedSlot is not (ItemSlot.MainHand or ItemSlot.OffHand));

                Console.WriteLine(
                    $"{path,-7} {level,3}  {cell.Loadout.Realm,-10} " +
                    $"{armour} armour piece(s), {string.Join(" + ", weapons)}");
            }

            Console.WriteLine();
        }
    }

    /// <summary>One row per fight, for anyone who would rather look at this in a spreadsheet.</summary>
    public void WriteCsv(string path)
    {
        var text = new StringBuilder();
        text.AppendLine(
            "path,level,encounter,seed,abilities,outcome,seconds,weapon,cast,wound,taken," +
            "healthRemaining,healthMax,swings,casts,starvedPulses");

        foreach (var row in _rows)
        {
            var r = row.Result;
            text.AppendLine(string.Join(',',
                row.Path, row.Level, row.Encounter, row.Seed, row.Abilities, r.Outcome,
                r.Seconds.ToString("0.00", CultureInfo.InvariantCulture),
                r.WeaponDamage, r.AbilityDamage, r.WoundDamage, r.DamageTaken,
                r.HealthRemaining, r.HealthMax, r.Swings, r.Casts, r.StarvedPulses));
        }

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, text.ToString());
    }

    /// <summary>
    /// The median rather than the mean, because a stalemate is capped rather than infinite and
    /// would otherwise drag an average toward a number no fight actually took.
    /// </summary>
    private static double Median(IReadOnlyList<FightResult> runs, Func<FightResult, double> selector)
    {
        if (runs.Count == 0)
        {
            return 0;
        }

        var values = runs.Select(selector).Order().ToList();

        return values.Count % 2 == 1
            ? values[values.Count / 2]
            : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
    }

    /// <summary>How the fights ended, as a word plus the count when they disagreed.</summary>
    private static string Outcomes(IReadOnlyList<FightResult> runs)
    {
        var won = runs.Count(r => r.Outcome == FightOutcome.Won);
        var lost = runs.Count(r => r.Outcome == FightOutcome.Lost);
        var stuck = runs.Count - won - lost;

        if (won == runs.Count)
        {
            return "won";
        }

        if (lost == runs.Count)
        {
            return "DIED";
        }

        if (stuck == runs.Count)
        {
            return "STUCK";
        }

        return $"{won}w/{lost}d/{stuck}s";
    }

    private static string Seconds(double value) =>
        value <= 0 ? "--" : value >= 100 ? $"{value:0}s" : $"{value:0.0}s";

    private static string Trim(string value, int width) =>
        value.Length <= width ? value : value[..(width - 1)] + "…";

    private static long Read(Dictionary<string, string> parameters, string key, long fallback) =>
        parameters.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : fallback;

    private static void Section(string title)
    {
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 78));
    }
}
