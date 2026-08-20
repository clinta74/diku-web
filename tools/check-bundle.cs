#:project ../src/DikuWeb.Server/DikuWeb.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Checks a WorldBundle JSON file before it is imported.
//
//     dotnet run tools/check-bundle.cs content/ossara/gatetown.json
//     dotnet run tools/check-bundle.cs build/the-reaches.json
//
// A pre-flight check, not a replacement for `POST /api/builder/import?dryRun=true`. The dry run is
// authoritative: it knows what is already in the target database, and it is the same code path a
// real import takes. This runs with no server and no database, which is what makes it useful in an
// editor loop.
//
// **A shim, deliberately.** Every rule lives in BundleValidator, in the server project, and runs in
// the test suite as well - which is the half that matters, since a checker nobody remembers to run
// is a checker that reports nothing. This exists so the same rules are one command away while
// authoring.
//
// It was a Python script until the port. Two directives replaced the whole reason it was one:
// `#:project` reaches the real `WorldBundle`, `RoomKey`, `RoomFlags`, `MobBehavior` and
// `QuestDialogue` rather than recovering them with regular expressions over the C#, and the
// property is needed because file-based apps disable reflection-based JSON by default, which is
// how a bundle gets deserialized here at all.
//
// Warnings never block; only errors set the exit status.
//
// **Gotcha, and it will cost you an hour.** `dotnet run` caches a file-based app's build against
// the *script's* content, so editing a referenced project does not invalidate it and neither does
// `dotnet build`. A shim can go on running library code from before your last change, silently and
// with a plausible answer. Touch this file to force a rebuild if a fix does not seem to have landed.
// The rules themselves run in `dotnet test`, which has no such cache - that is the authority, and
// this is the convenience.

using DikuWeb.Server.Building;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run tools/check-bundle.cs <bundle.json> [more.json ...]");
    return 2;
}

var errors = 0;
var warnings = 0;

foreach (var path in args)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"  ERROR  {path} does not exist");
        errors++;
        continue;
    }

    if (!BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var failure))
    {
        // The message carries the JSON path and line number, which is the payoff for reading
        // through the real record instead of a hand-rolled shape check.
        Console.WriteLine($"{Path.GetFileName(path)}: not a bundle this build can read");
        Console.WriteLine($"  ERROR  {failure}");
        errors++;
        continue;
    }

    var check = BundleValidator.Validate(bundle!);

    Console.WriteLine(
        $"{Path.GetFileName(path)}: {bundle!.Rooms.Count} rooms, "
        + $"{bundle.Rooms.Sum(r => r.Exits.Count)} exits, {bundle.MobTemplates.Count} mobs, "
        + $"{bundle.ItemTemplates.Count} items, {bundle.Spawners.Count} spawners, "
        + $"{bundle.Quests.Count} quests, {bundle.Abilities.Count} abilities");

    foreach (var finding in check.Warnings)
    {
        Console.WriteLine($"  warn   {finding.Message}");
    }

    foreach (var finding in check.Errors)
    {
        Console.WriteLine($"  ERROR  {finding.Message}");
    }

    errors += check.Errors.Count();
    warnings += check.Warnings.Count();
}

// Said out loud, because "OK" printed above a list of warnings reads as a contradiction and the
// next question is always whether it is safe to go on. It is: several of these are things content
// is allowed to be - a one-way exit can be the story - and the dry run is what decides, since it
// is the only check that knows what is already in the target database.
if (errors > 0)
{
    var also = warnings > 0 ? $", {warnings} warning(s)" : string.Empty;
    Console.WriteLine($"FAILED  {errors} error(s){also}. Nothing here is worth importing until they are fixed.");
    return 1;
}

if (warnings > 0)
{
    Console.WriteLine($"OK      {warnings} warning(s), none blocking. Read them, then dry-run the import:");
    Console.WriteLine("        POST /api/builder/import?dryRun=true");
    return 0;
}

Console.WriteLine("OK");
return 0;
