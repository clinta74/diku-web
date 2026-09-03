using System.Globalization;
using Muwbta.Domain.Characters;

namespace Muwbta.Balance;

/// <summary>Command line, parsed once.</summary>
public sealed record Options(
    IReadOnlyList<string> ContentPaths,
    IReadOnlyList<CharacterPath> Paths,
    IReadOnlyList<int> Levels,
    int Runs,
    int Seed,
    int Cap,
    string? CsvPath,
    double RegenScale,
    int RegenSeconds,
    bool ShowHelp)
{
    /// <summary>
    /// The level rungs the report walks.
    /// </summary>
    /// <remarks>
    /// Five-level steps from 1. Abilities unlock on levels ending in 0, 3, 5, 6 and 8, so a stride
    /// of five lands close enough to every unlock that no tier of the progression goes unmeasured,
    /// without running eleven Paths' worth of fights fifty times over.
    /// </remarks>
    private static readonly int[] DefaultLevels = [1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50];

    /// <summary>
    /// The four playable Paths.
    /// </summary>
    /// <remarks>
    /// <c>CharacterPath.Shade</c> is deliberately absent: it is the retired name the catalogue still
    /// carries, and it has no abilities in the table to measure.
    /// </remarks>
    private static readonly CharacterPath[] DefaultPaths =
        [CharacterPath.Warden, CharacterPath.Temper, CharacterPath.Adept, CharacterPath.Hallow];

    public static Options Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var contentPaths = new List<string>();
        var paths = new List<CharacterPath>();
        var levels = new List<int>();
        var runs = 60;
        var seed = 20260821;
        var cap = 600;
        string? csv = null;
        var regenScale = 1.0;
        var regenSeconds = 60;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : string.Empty;

            switch (arg)
            {
                case "--content" or "-c":
                    contentPaths.Add(Next());
                    break;

                case "--paths" or "-p":
                    paths.AddRange(Split(Next())
                        .Select(p => Enum.Parse<CharacterPath>(p, ignoreCase: true)));
                    break;

                case "--levels" or "-l":
                    levels.AddRange(Split(Next())
                        .Select(v => int.Parse(v, CultureInfo.InvariantCulture)));
                    break;

                case "--runs" or "-n":
                    runs = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;

                case "--seed":
                    seed = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;

                case "--cap":
                    cap = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;

                case "--csv":
                    csv = Next();
                    break;

                case "--regen":
                    regenScale = double.Parse(Next(), CultureInfo.InvariantCulture);
                    break;

                case "--regen-seconds":
                    regenSeconds = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;

                case "--help" or "-h":
                    help = true;
                    break;

                default:
                    Console.Error.WriteLine($"Unknown argument: {arg}");
                    help = true;
                    break;
            }
        }

        if (contentPaths.Count == 0)
        {
            contentPaths.Add("content");
        }

        return new Options(
            contentPaths,
            paths.Count > 0 ? paths : DefaultPaths,
            levels.Count > 0 ? [.. levels.Order()] : DefaultLevels,
            Math.Max(1, runs),
            seed,
            Math.Max(10, cap),
            csv,
            regenScale,
            Math.Max(1, regenSeconds),
            help);
    }

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static void PrintUsage() => Console.WriteLine("""
        Measures where the authored world's combat numbers land, by fighting it.

          --content, -c <path>   Bundle file or directory. Repeatable. Default: content
          --paths,   -p <list>   Warden,Temper,Adept,Hallow. Default: all four
          --levels,  -l <list>   Comma-separated levels. Default: 1,5,10..50
          --runs,    -n <count>  Fights per cell, medians reported. Default: 60
          --seed        <int>    Base seed. Runs are seed+index, so a cell is reproducible
          --cap         <secs>   Give up on a fight after this. Default: 600
          --csv         <file>   Also write one row per fight
          --regen       <x>      Multiply in-combat regeneration, to size a proposed change to
                                 RegenCalculator without making one. Default: 1.0
          --regen-seconds <n>    How often a regeneration tick lands. Default: 60, which is what
                                 the server uses - and why a 30-second fight gets none at all
          --help,    -h

        content/ is an EXPORT of the database, not the database. To measure what is live:

          curl "http://localhost:5000/api/builder/export" -o build/live.json
          dotnet run --project tools/Muwbta.Balance -- --content build/live.json
        """);
}
