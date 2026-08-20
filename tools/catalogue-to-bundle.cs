#:project ../src/DikuWeb.Server/DikuWeb.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Writes AbilityCatalogue out as an importable bundle.
//
//     dotnet run tools/catalogue-to-bundle.cs -o content/abilities.json
//
// A ONE-WAY DOOR, RUN ONCE. This is the tool that moved the ability set out of C# and into
// content. After it has run, `content/abilities.json` is the source and this is only good for
// dumping whatever examples the catalogue still holds - which is four abilities, and not a game.
//
// Kept because the reverse trip has no tool and should not have one: an ability edited in the
// builder, exported, and merged into content is how a change travels now. Writing content back
// into C# would put the two in a race.

using DikuWeb.Domain.Abilities;
using DikuWeb.Server.Building;

string output = "content/abilities.json";

for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "-o" or "--out" && i + 1 < args.Length)
    {
        output = args[++i];
    }
}

var abilities = AbilityCatalogue.All
    .OrderBy(e => e.Path)
    .ThenBy(e => e.UnlockLevel)
    .Select(e => new BundleAbility(
        e.Key,
        e.Path,
        e.UnlockLevel,
        e.Name,
        e.Description,
        e.CostType,
        e.CostValue,
        e.CooldownPulses,
        e.CooldownGroup,
        e.CastTimePulses,
        e.TargetingType,
        [.. e.Effects]))
    .ToList();

var bundle = new WorldBundle(
    BundleFormat.CurrentVersion,
    DateTimeOffset.UtcNow,
    new BundleScope("all", null),
    [], [], [], [], [],
    abilities,
    [], [], []);

File.WriteAllText(output, BundleFormat.Write(bundle));

Console.WriteLine($"wrote {output} ({abilities.Count} abilities, formatVersion {BundleFormat.CurrentVersion})");
return 0;
