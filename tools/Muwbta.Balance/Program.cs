using Muwbta.Balance;
using Muwbta.Balance.Content;
using Muwbta.Balance.Reporting;

// Where the authored world's combat numbers actually land, measured by fighting it.
//
//     dotnet run --project tools/Muwbta.Balance
//     dotnet run --project tools/Muwbta.Balance -- --content build/live.json
//     dotnet run --project tools/Muwbta.Balance -- --paths Temper --levels 40,50 --runs 200
//     dotnet run --project tools/Muwbta.Balance -- --csv build/balance.csv
//
// PLAN.md §14 has said "balance is unmeasured" since it was written. This is the measurement.
// MobAttackBaseline, the cooldown curve and the flat ability base are all decisions that cannot be
// settled by arithmetic, because mob health is superlinear in level and every other quantity in the
// game is not. A time-to-kill number settles them.

var options = Options.Parse(args);

if (options.ShowHelp)
{
    Options.PrintUsage();
    return 0;
}

ContentSet content;

try
{
    content = ContentSet.Load(options.ContentPaths);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not load content: {ex.Message}");
    return 1;
}

Console.WriteLine("Muwbta balance harness");
Console.WriteLine(new string('=', 78));

foreach (var line in content.Describe())
{
    Console.WriteLine(line);
}

Console.WriteLine(
    $"{content.Encounters.Count} placed encounters, {content.Abilities.Count} abilities, " +
    $"{content.Items.Count} item templates");
Console.WriteLine($"{options.Runs} run(s) per cell, seed base {options.Seed}, cap {options.Cap}s, regen x{options.RegenScale}");
Console.WriteLine();

var report = new Report(content, options);

report.WriteStuck();
report.WriteResourceEconomy();
report.WriteBarUse();
report.WriteDamagePerPoint();
report.WriteEncounters();
report.WriteDamageSplit();
report.WriteAbilityWorth();
report.WriteLoadouts();
report.WriteAbilityLedger();

if (options.CsvPath is { } csv)
{
    report.WriteCsv(csv);
    Console.WriteLine($"Per-run rows written to {csv}");
}

return 0;
